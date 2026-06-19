using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Cnidaria.Cs;

namespace Cnidaria.C
{
    public sealed class RegisterBytecodeProgram
    {
        public CodeImage Image { get; }
        public int EntryPc { get; }
        public int PrintfMethodId { get; }
        public IReadOnlyDictionary<FunctionSymbol, int> MethodIds { get; }
        public IReadOnlyDictionary<int, FunctionType?> SignaturesByMethodId { get; }
        public TargetInfo Target { get; }

        public RegisterBytecodeProgram(
            CodeImage image,
            int entryPc,
            int printfMethodId,
            IReadOnlyDictionary<FunctionSymbol, int> methodIds,
            IReadOnlyDictionary<int, FunctionType?> signaturesByMethodId,
            TargetInfo? target = null)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            if (entryPc < 0)
                throw new ArgumentOutOfRangeException(nameof(entryPc));
            EntryPc = entryPc;
            PrintfMethodId = printfMethodId;
            MethodIds = methodIds ?? throw new ArgumentNullException(nameof(methodIds));
            SignaturesByMethodId = signaturesByMethodId ?? throw new ArgumentNullException(nameof(signaturesByMethodId));
            Target = target ?? TargetInfo.RegisterBytecode;
        }
        public RegisterBytecodeSyntheticRuntime CreateSyntheticRuntime()
            => CreateSyntheticRuntime(MethodIds, SignaturesByMethodId, EntryPc, PrintfMethodId, Target);

        internal void RegisterSyntheticRuntimeMethods(RuntimeTypeSystem runtimeTypes)
            => RegisterSyntheticRuntimeMethods(runtimeTypes, MethodIds, SignaturesByMethodId, PrintfMethodId, Target);

        internal static RegisterBytecodeSyntheticRuntime CreateSyntheticRuntime(
            IReadOnlyDictionary<FunctionSymbol, int> methodIds,
            IReadOnlyDictionary<int, FunctionType?> signaturesByMethodId,
            int entryPc,
            int printfMethodId,
            TargetInfo? target = null)
        {
            var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);
            var stdModule = new RuntimeModule(
                name: "std",
                md: new MinimalCRuntimeMetadataView(),
                methodsByDefToken: new Dictionary<int, BytecodeFunction>());

            modules.Add(stdModule.Name, stdModule);

            var runtimeTypes = new RuntimeTypeSystem(modules);
            RegisterSyntheticRuntimeMethods(runtimeTypes, methodIds, signaturesByMethodId, printfMethodId, target ?? TargetInfo.RegisterBytecode);
            return new RegisterBytecodeSyntheticRuntime(runtimeTypes, modules, entryPc);
        }

        internal static void RegisterSyntheticRuntimeMethods(
            RuntimeTypeSystem runtimeTypes,
            IReadOnlyDictionary<FunctionSymbol, int> methodIds,
            IReadOnlyDictionary<int, FunctionType?> signaturesByMethodId,
            int printfMethodId,
            TargetInfo? target = null)
        {
            if (runtimeTypes is null)
                throw new ArgumentNullException(nameof(runtimeTypes));
            if (methodIds is null)
                throw new ArgumentNullException(nameof(methodIds));
            if (signaturesByMethodId is null)
                throw new ArgumentNullException(nameof(signaturesByMethodId));
            target ??= TargetInfo.RegisterBytecode;

            var cProgramType = runtimeTypes.RegisterSyntheticType("c", "__c", "Program", RuntimeTypeKind.Class);
            foreach (var pair in signaturesByMethodId.OrderBy(static p => p.Key))
            {
                if (pair.Key == printfMethodId)
                    continue;

                if (TryGetMethod(runtimeTypes, pair.Key, out _))
                    continue;

                var signature = pair.Value;
                var returnType = signature is null
                    ? GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Int)
                    : MapRuntimeType(runtimeTypes, signature.ReturnType, target);
                var parameters = signature is null
                    ? Array.Empty<RuntimeType>()
                    : signature.Parameters.Select(p => MapRuntimeType(runtimeTypes, p.Type, target)).ToArray();

                var name = methodIds.FirstOrDefault(m => m.Value == pair.Key).Key?.Name ?? $"fn_{pair.Key.ToString(CultureInfo.InvariantCulture)}";
                runtimeTypes.RegisterSyntheticStaticMethod(cProgramType, name, returnType, parameters, implFlags: 0, methodId: pair.Key);
            }

            RegisterCStringWriteInternalCall(runtimeTypes, printfMethodId);
        }

        private static bool TryGetMethod(RuntimeTypeSystem runtimeTypes, int methodId, out RuntimeMethod? method)
        {
            try
            {
                method = runtimeTypes.GetMethodById(methodId);
                return true;
            }
            catch (MissingMethodException)
            {
                method = null;
                return false;
            }
        }

        private static void RegisterCStringWriteInternalCall(RuntimeTypeSystem runtimeTypes, int methodId)
        {
            const ushort InternalCallImplFlag = 0x1000;
            if (TryGetMethod(runtimeTypes, methodId, out _))
                return;

            var console = runtimeTypes.RegisterSyntheticType("std", "System", "Console", RuntimeTypeKind.Class);
            var voidType = GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Void);
            var byteType = GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.UnsignedChar);
            var bytePointer = runtimeTypes.GetPointerType(byteType);
            runtimeTypes.RegisterSyntheticStaticMethod(console, "_Write", voidType, new[] { bytePointer }, InternalCallImplFlag, methodId);
        }

        private static RuntimeType MapRuntimeType(RuntimeTypeSystem runtimeTypes, QualifiedType type, TargetInfo target)
        {
            if (type.Type is BuiltinType builtin)
                return GetRuntimePrimitive(runtimeTypes, builtin.BuiltinKind);

            if (type.Type is PointerType pointer)
                return runtimeTypes.GetPointerType(MapRuntimeType(runtimeTypes, pointer.PointeeType, target));

            if (type.Type is ArrayType array)
                return runtimeTypes.GetPointerType(MapRuntimeType(runtimeTypes, array.ElementType, target));

            if (type.Type is FunctionType)
                return runtimeTypes.GetPointerType(GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Void));

            if (CAbi.IsAggregate(type))
                return GetRuntimeAggregate(runtimeTypes, type, target);

            return GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Int);
        }

        private static RuntimeType GetRuntimePrimitive(RuntimeTypeSystem runtimeTypes, BuiltinTypeKind kind)
        {
            var (name, primitive, size) = kind switch
            {
                BuiltinTypeKind.Void => ("Void", RuntimePrimitiveKind.Void, 0),
                BuiltinTypeKind.Bool => ("Boolean", RuntimePrimitiveKind.Boolean, 1),
                BuiltinTypeKind.Char => ("Byte", RuntimePrimitiveKind.UInt8, 1),
                BuiltinTypeKind.SignedChar => ("SByte", RuntimePrimitiveKind.Int8, 1),
                BuiltinTypeKind.UnsignedChar => ("Byte", RuntimePrimitiveKind.UInt8, 1),
                BuiltinTypeKind.Short => ("Int16", RuntimePrimitiveKind.Int16, 2),
                BuiltinTypeKind.UnsignedShort => ("UInt16", RuntimePrimitiveKind.UInt16, 2),
                BuiltinTypeKind.Int => ("Int32", RuntimePrimitiveKind.Int32, 4),
                BuiltinTypeKind.UnsignedInt => ("UInt32", RuntimePrimitiveKind.UInt32, 4),
                BuiltinTypeKind.Long => ("Int64", RuntimePrimitiveKind.Int64, 8),
                BuiltinTypeKind.UnsignedLong => ("UInt64", RuntimePrimitiveKind.UInt64, 8),
                BuiltinTypeKind.LongLong => ("Int64", RuntimePrimitiveKind.Int64, 8),
                BuiltinTypeKind.UnsignedLongLong => ("UInt64", RuntimePrimitiveKind.UInt64, 8),
                BuiltinTypeKind.Float => ("Single", RuntimePrimitiveKind.Single, 4),
                BuiltinTypeKind.Double => ("Double", RuntimePrimitiveKind.Double, 8),
                BuiltinTypeKind.LongDouble => ("Double", RuntimePrimitiveKind.Double, 8),
                _ => ("Int32", RuntimePrimitiveKind.Int32, 4),
            };

            RuntimeType type;
            try
            {
                type = runtimeTypes.GetRequiredNamedType("std", "System", name);
            }
            catch (Exception)
            {
                type = runtimeTypes.RegisterSyntheticType("std", "System", name, RuntimeTypeKind.Struct);
            }

            type.PrimitiveKind = primitive;
            type.SizeOf = Math.Max(0, size);
            type.AlignOf = Math.Max(1, Math.Min(Math.Max(1, size), 8));
            return type;
        }

        private static RuntimeType GetRuntimeAggregate(RuntimeTypeSystem runtimeTypes, QualifiedType type, TargetInfo target)
        {
            var size = Math.Max(1, target.SizeOf(type));
            var align = Math.Max(1, target.AlignOf(type));
            var name = BuildSyntheticAggregateTypeName(type, size, align);
            var runtimeType = runtimeTypes.RegisterSyntheticType("c", "__c.abi", name, RuntimeTypeKind.Struct);
            runtimeType.PrimitiveKind = RuntimePrimitiveKind.None;
            runtimeType.SizeOf = size;
            runtimeType.AlignOf = align;
            runtimeType.InstanceSize = size;
            runtimeType.StaticSize = 0;
            runtimeType.StaticAlign = 1;
            runtimeType.ContainsGcPointers = false;
            runtimeType.GcPointerOffsets = Array.Empty<int>();
            runtimeType.InstanceFields = Array.Empty<RuntimeField>();
            return runtimeType;
        }

        private static string BuildSyntheticAggregateTypeName(QualifiedType type, int size, int align)
        {
            var display = type.ToDisplayString();
            uint hash = 2166136261u;
            for (var i = 0; i < display.Length; i++)
            {
                hash ^= display[i];
                hash *= 16777619u;
            }

            return "agg_" + hash.ToString("X8", CultureInfo.InvariantCulture)
                + "_s" + size.ToString(CultureInfo.InvariantCulture)
                + "_a" + align.ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class RegisterBytecodeCodeGenerator
    {
        public const int FirstCMethodId = 10_000_000;
        public const int PrintfMethodId = 10_999_999;

        private static readonly MachineRegister Sp = MachineRegister.X2;
        private static readonly MachineRegister GpScratch0 = MachineRegister.X5;
        private static readonly MachineRegister GpScratch1 = MachineRegister.X6;
        private static readonly MachineRegister GpScratch2 = MachineRegister.X7;
        private static readonly MachineRegister VarArgsRegister = MachineRegister.X9;
        private static readonly MachineRegister FpScratch0 = MachineRegister.F0;
        private static readonly MachineRegister FpScratch1 = MachineRegister.F1;
        private static readonly MachineRegister FpScratch2 = MachineRegister.F2;

        private readonly LirModule _module;
        private readonly TargetInfo _target;
        private readonly LSRAOptions _allocationOptions;
        private Assembler _assembler = null!;
        private RuntimeTypeSystem _runtimeTypes = null!;
        private RegisterBytecodeSyntheticRuntime _syntheticRuntime = null!;
        private readonly Dictionary<FunctionSymbol, int> _methodIds = new Dictionary<FunctionSymbol, int>();
        private readonly Dictionary<string, int> _methodIdsByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, FunctionType?> _signatures = new Dictionary<int, FunctionType?>();
        private readonly Dictionary<int, RuntimeMethod> _runtimeMethodsById = new Dictionary<int, RuntimeMethod>();
        private readonly Dictionary<int, Label> _methodEntryLabels = new Dictionary<int, Label>();
        private readonly List<FunctionPointerFixup> _functionPointerFixups = new List<FunctionPointerFixup>();

        private RegisterBytecodeCodeGenerator(LirModule module, LSRAOptions? allocationOptions)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _target = module.SemanticModel.Compilation.Options.Target;
            _allocationOptions = allocationOptions ?? LSRAOptions.Default;
        }

        public static RegisterBytecodeProgram Generate(LirModule module, LSRAOptions? allocationOptions = null)
            => new RegisterBytecodeCodeGenerator(module, allocationOptions).Generate();

        private RegisterBytecodeProgram Generate()
        {
            if (_target.Architecture != TargetArchitectureKind.RegisterBytecode)
                throw new NotSupportedException($"Register-bytecode C backend cannot emit target architecture '{_target.Architecture}'. Use TargetInfo.RegisterBytecode for this backend.");

            if (_target.PointerSize != TargetArchitecture.PointerSize)
                throw new NotSupportedException($"Register-bytecode C backend currently supports only the VM native pointer size ({TargetArchitecture.PointerSize.ToString(CultureInfo.InvariantCulture)} bytes).");

            IndexMethods();
            var entryMethodId = ResolveEntryMethodId();
            CreateSyntheticRuntimeForEmission();
            _assembler = new Assembler(_runtimeTypes);
            CreateMethodEntryLabels();

            foreach (var function in _module.Functions)
                EmitFunction(function);

            var flags = ImageFlags.LittleEndian;
            if (_target.PointerSize == 4)
                flags |= ImageFlags.Target32;

            var image = _assembler.Build(flags, validate: true);
            image = PatchFunctionPointerFixups(image);
            var entryPc = ResolveMethodEntryPc(image, entryMethodId);
            return new RegisterBytecodeProgram(image, entryPc, PrintfMethodId, _methodIds, _signatures, _target);
        }

        private void IndexMethods()
        {
            var next = FirstCMethodId;
            foreach (var function in _module.Functions)
            {
                var symbol = function.Symbol;
                if (symbol is null)
                    continue;

                if (_methodIds.ContainsKey(symbol))
                    continue;

                var methodId = next++;
                _methodIds.Add(symbol, methodId);
                if (!_methodIdsByName.ContainsKey(symbol.Name))
                    _methodIdsByName.Add(symbol.Name, methodId);
                _signatures[methodId] = symbol.FunctionType;
            }
        }

        private int ResolveEntryMethodId()
        {
            if (_methodIdsByName.TryGetValue("main", out var main))
                return main;

            if (_methodIds.Count == 0)
                throw new InvalidOperationException("C module does not contain a function body to execute.");

            return _methodIds.Values.First();
        }

        private void CreateSyntheticRuntimeForEmission()
        {
            _syntheticRuntime = RegisterBytecodeProgram.CreateSyntheticRuntime(_methodIds, _signatures, entryPc: 0, printfMethodId: PrintfMethodId, target: _target);
            _runtimeTypes = _syntheticRuntime.RuntimeTypes;
            _runtimeMethodsById.Clear();

            foreach (var methodId in _signatures.Keys)
                _runtimeMethodsById[methodId] = _runtimeTypes.GetMethodById(methodId);

            _runtimeMethodsById[PrintfMethodId] = _runtimeTypes.GetMethodById(PrintfMethodId);
        }

        private void CreateMethodEntryLabels()
        {
            _methodEntryLabels.Clear();
            foreach (var methodId in _methodIds.Values.Distinct().OrderBy(static id => id))
                _methodEntryLabels.Add(methodId, _assembler.CreateLabel());
        }

        private Label GetMethodEntryLabel(int methodId)
        {
            if (_methodEntryLabels.TryGetValue(methodId, out var label))
                return label;
            throw new MissingMethodException($"C method M{methodId} is not present in the register-bytecode image.");
        }

        private static int ResolveMethodEntryPc(CodeImage image, int methodId)
        {
            if (image is null)
                throw new ArgumentNullException(nameof(image));
            if (!image.MethodIndexByRuntimeMethodId.TryGetValue(methodId, out var methodIndex))
                throw new MissingMethodException($"C method M{methodId} is not present in the register-bytecode image.");
            return image.Methods[methodIndex].EntryPc;
        }

        private CodeImage PatchFunctionPointerFixups(CodeImage image)
        {
            if (_functionPointerFixups.Count == 0)
                return image;

            var code = image.Code.ToArray();
            foreach (var fixup in _functionPointerFixups)
            {
                var targetPc = ResolveMethodEntryPc(image, fixup.MethodId);
                if ((uint)fixup.Pc >= (uint)code.Length)
                    throw new InvalidOperationException($"Invalid function-pointer fixup PC {fixup.Pc}.");

                var old = code[fixup.Pc];
                if (old.Op is not (Op.LiI32 or Op.LiI64))
                    throw new InvalidOperationException($"Function-pointer fixup at PC {fixup.Pc} points at {old.Op}, not a load-immediate instruction.");

                code[fixup.Pc] = new InstrDesc(old.Op, old.Rd, old.Rs1, old.Rs2, old.Rs3, old.Aux, targetPc);
            }

            return new CodeImage(
                image.Flags,
                code.ToImmutableArray(),
                image.Methods,
                image.EhRegions,
                image.GcSafePoints,
                image.GcRoots,
                image.Unwind,
                image.SwitchTable,
                image.TypeLayouts,
                ImmutableArray<VTableSlotRecord>.Empty,
                image.Blob,
                validate: true);
        }

        private RuntimeMethod GetRuntimeMethod(int methodId)
        {
            if (_runtimeMethodsById.TryGetValue(methodId, out var method))
                return method;
            method = _runtimeTypes.GetMethodById(methodId);
            _runtimeMethodsById.Add(methodId, method);
            return method;
        }

        private void EmitFunction(LirFunction function)
        {
            if (function.Symbol is null || !_methodIds.TryGetValue(function.Symbol, out var methodId))
                throw new NotSupportedException("Cannot emit anonymous C functions to register bytecode.");

            var allocation = LinearScanRegisterAllocator.Allocate(function, _target, _allocationOptions);
            var labels = new Dictionary<LirBlock, Label>();
            foreach (var block in function.Blocks)
                labels.Add(block, _assembler.CreateLabel());

            var context = new FunctionEmissionContext(this, function, allocation, labels);
            _assembler.BeginMethod(methodId, GetMethodEntryLabel(methodId), -1, ToStackFrameLayout(allocation.Frame));
            context.EmitPrologue();
            context.EmitBlocks();
            context.EmitImplicitFallthroughTrap();
            _assembler.EndMethod();
        }

        private static StackFrameLayout ToStackFrameLayout(StackFrameMap frame)
        {
            var empty = ImmutableArray<StackFrameSlot>.Empty;
            var tempAreaOffset = frame.ParallelCopyTempOffset;
            var tempAreaSize = checked(frame.FloatingImmediateTempOffset + frame.FloatingImmediateTempSize - tempAreaOffset);
            return new StackFrameLayout(
                frameSize: frame.FrameSize,
                frameAlignment: frame.FrameAlignment,
                calleeSaveAreaOffset: frame.SavedRegisterAreaOffset,
                calleeSaveAreaSize: frame.SavedRegisterAreaSize,
                argumentHomeAreaOffset: 0,
                argumentHomeAreaSize: 0,
                localAreaOffset: frame.StackSlotAreaOffset,
                localAreaSize: frame.StackSlotAreaSize,
                tempAreaOffset: tempAreaOffset,
                tempAreaSize: tempAreaSize,
                spillAreaOffset: frame.SpillAreaOffset,
                spillAreaSize: frame.SpillAreaSize,
                outgoingArgumentAreaOffset: frame.OutgoingArgumentAreaOffset,
                outgoingArgumentAreaSize: frame.OutgoingArgumentAreaSize,
                argumentSlots: empty,
                localSlots: empty,
                tempSlots: empty,
                spillSlots: empty,
                calleeSavedSlots: empty,
                outgoingArgumentSlots: empty,
                usesFramePointer: false,
                frameModel: RegisterStackFrameModel.Leaf);
        }


        private readonly struct FunctionPointerFixup
        {
            public readonly int Pc;
            public readonly int MethodId;

            public FunctionPointerFixup(int pc, int methodId)
            {
                if (pc < 0)
                    throw new ArgumentOutOfRangeException(nameof(pc));
                if (methodId < 0)
                    throw new ArgumentOutOfRangeException(nameof(methodId));
                Pc = pc;
                MethodId = methodId;
            }
        }

        private sealed class FunctionEmissionContext
        {
            private readonly RegisterBytecodeCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly IReadOnlyDictionary<LirBlock, Label> _labels;
            private readonly Dictionary<LirBlock, LirBlock?> _nextBlocks;
            private readonly Assembler _asm;
            private readonly ParameterState _parameters = new ParameterState();
            private LirBlock? _currentBlock;

            public FunctionEmissionContext(
                RegisterBytecodeCodeGenerator owner,
                LirFunction function,
                AllocationResult allocation,
                IReadOnlyDictionary<LirBlock, Label> labels)
            {
                _owner = owner;
                _function = function;
                _allocation = allocation;
                _labels = labels;
                _asm = owner._assembler;
                _nextBlocks = BuildNextBlockMap(function);

                var returnType = function.Symbol?.FunctionType?.ReturnType ?? TypeCatalog.Instance.Builtin(BuiltinTypeKind.Void);
                if (CAbi.RequiresHiddenReturnBuffer(owner._target, returnType))
                {
                    var cursor = new AbiCursor();
                    _ = CAbi.AssignHiddenReturnBufferLocation(owner._target, ref cursor, owner._allocationOptions.StackArgumentSlotSize);
                    SyncParameterState(cursor);
                }
            }

            private static Dictionary<LirBlock, LirBlock?> BuildNextBlockMap(LirFunction function)
            {
                var result = new Dictionary<LirBlock, LirBlock?>();
                for (var i = 0; i < function.Blocks.Length; i++)
                {
                    var next = i + 1 < function.Blocks.Length ? function.Blocks[i + 1] : null;
                    result.Add(function.Blocks[i], next);
                }

                return result;
            }

            public void EmitPrologue()
            {
                var frame = _allocation.Frame;
                if (frame.FrameSize != 0)
                    EmitI64Imm(Op.I64SubImm, Sp, Sp, frame.FrameSize);

                if (frame.HasVarArgsPointer)
                    EmitMem(Op.StPtr, VarArgsRegister, MachineRegister.Invalid, frame.VarArgsPointerOffset,
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);

                if (frame.HasHiddenReturnBuffer)
                    StoreIncomingHiddenReturnBufferAddress(frame.HiddenReturnBufferOffset);

                foreach (var pair in frame.SavedRegisterOffsets.OrderBy(static p => p.Value))
                {
                    var op = IsFloatRegister(pair.Key) ? Op.StF64 : Op.StI8;
                    EmitMem(op, pair.Key, MachineRegister.Invalid, pair.Value, MachineRegister.Invalid, MemoryBase.StackPointer, alignment: 8);
                }
            }

            private void StoreIncomingHiddenReturnBufferAddress(int frameOffset)
            {
                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (location.Kind == AbiLocationKind.Register)
                {
                    EmitMem(Op.StPtr, location.Register, MachineRegister.Invalid, frameOffset,
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    EmitMem(Op.LdPtr, GpScratch0, MachineRegister.Invalid,
                        location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize),
                        MachineRegister.Invalid, MemoryBase.ThreadPointer, _owner._target.PointerAlignment);
                    EmitMem(Op.StPtr, GpScratch0, MachineRegister.Invalid, frameOffset,
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);
                    return;
                }

                throw new InvalidOperationException("Invalid hidden return buffer ABI location.");
            }

            public void EmitBlocks()
            {
                foreach (var block in _function.Blocks)
                {
                    _currentBlock = block;
                    _asm.Bind(_labels[block]);
                    foreach (var instruction in block.Instructions)
                        EmitInstruction(instruction);
                }

                _currentBlock = null;
            }

            public void EmitImplicitFallthroughTrap()
                => _asm.Trap(2);

            private void EmitInstruction(LirInstruction instruction)
            {
                switch (instruction.Kind)
                {
                    case LirInstructionKind.Nop:
                        _asm.Nop();
                        break;

                    case LirInstructionKind.Parameter:
                        EmitParameter(instruction);
                        break;

                    case LirInstructionKind.Copy:
                    case LirInstructionKind.Constant:
                        EmitCopyLike(instruction);
                        break;

                    case LirInstructionKind.Cast:
                        EmitConvert(instruction);
                        break;

                    case LirInstructionKind.ParallelCopy:
                        EmitParallelCopy(instruction);
                        break;

                    case LirInstructionKind.Zero:
                        EmitZero(instruction);
                        break;

                    case LirInstructionKind.Unary:
                        EmitUnary(instruction);
                        break;

                    case LirInstructionKind.Binary:
                        EmitBinary(instruction);
                        break;

                    case LirInstructionKind.Convert:
                        EmitConvert(instruction);
                        break;

                    case LirInstructionKind.AddressOf:
                        EmitAddressOf(instruction);
                        break;

                    case LirInstructionKind.Load:
                        EmitLoad(instruction);
                        break;

                    case LirInstructionKind.Store:
                        EmitStore(instruction);
                        break;

                    case LirInstructionKind.ZeroMemory:
                        EmitZeroMemory(instruction);
                        break;

                    case LirInstructionKind.Call:
                        EmitCall(instruction);
                        break;

                    case LirInstructionKind.VaStart:
                        EmitVaStart(instruction);
                        break;

                    case LirInstructionKind.Jump:
                        if (!IsFallthroughTarget(instruction.Target))
                            _asm.J(LabelOf(instruction.Target));
                        break;

                    case LirInstructionKind.Branch:
                        EmitBranch(instruction);
                        break;

                    case LirInstructionKind.Switch:
                        EmitSwitch(instruction);
                        break;

                    case LirInstructionKind.Return:
                        EmitReturn(instruction);
                        break;

                    case LirInstructionKind.Unreachable:
                        _asm.Trap(1);
                        break;

                    default:
                        throw Unsupported(instruction, $"Unsupported LIR instruction kind: {instruction.Kind}.");
                }
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var type = instruction.Result.Type;
                var value = CAbi.ClassifyValue(_owner._target, type);
                var cursor = ToAbiCursor(_parameters);

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                        if (loc.Kind == AbiLocationKind.Register)
                        {
                            EmitStoreRawBitsToAddress(loc.Register, destinationAddress, segment.Offset, segment.Size);
                        }
                        else if (loc.Kind == AbiLocationKind.Stack)
                        {
                            EmitLoadRawBitsFromMemory(GpScratch1, MachineRegister.Invalid, loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), MemoryBase.ThreadPointer, segment.Size);
                            EmitStoreRawBitsToAddress(GpScratch1, destinationAddress, segment.Offset, segment.Size);
                        }
                        else
                        {
                            throw Unsupported(instruction, "Invalid aggregate parameter ABI location.");
                        }
                    }

                    SyncParameterState(cursor);
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    MaterializeIncomingStackAddress(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), GpScratch1);
                    EmitRaw(Op.CpBlk, destinationAddress, GpScratch1, MachineRegister.Invalid, imm: value.Size);
                    SyncParameterState(cursor);
                    return;
                }

                var scalarLocation = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                if (scalarLocation.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(destination, scalarLocation.Register);
                }
                else if (scalarLocation.Kind == AbiLocationKind.Stack)
                {
                    EmitMem(LoadOpForType(type), destination, MachineRegister.Invalid, scalarLocation.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), MachineRegister.Invalid, MemoryBase.ThreadPointer, AlignmentOf(type));
                }
                else
                {
                    throw Unsupported(instruction, "Invalid scalar parameter ABI location.");
                }

                SyncParameterState(cursor);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitCopyLike(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Copy-like instruction has no source operand.");

                if (IsAggregateType(instruction.Result.Type))
                {
                    EmitAggregateCopyToRegisterStorage(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                if (IsAggregateType(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    _asm.LiI32(GpScratch1, 0);
                    EmitRaw(Op.InitBlk, destinationAddress, GpScratch1, MachineRegister.Invalid, imm: Math.Max(1, SizeOf(instruction.Result.Type)));
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                if (IsFloatType(instruction.Result.Type))
                    EmitLiFloat(destination, 0.0, instruction.Result.Type);
                else
                    _asm.LiI64(destination, 0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Unary instruction has no result.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Unary instruction has no operand.");

                var op = instruction.Operator;
                if (op == "!" && IsFloatType(instruction.Operands[0].Type))
                {
                    if (IsLongDouble(instruction.Operands[0].Type))
                        throw Unsupported(instruction, "long double logical-not is not supported by the register-bytecode backend.");
                    var fsrc = LoadOperand(instruction.Operands[0], FpScratch1);
                    EmitLiFloat(FpScratch2, 0.0, instruction.Operands[0].Type);
                    EmitRaw(IsFloat32(instruction.Operands[0].Type) ? Op.F32Eq : Op.F64Eq, dst, fsrc, FpScratch2);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                var src = LoadOperand(instruction.Operands[0], IsFloatType(instruction.Result.Type) ? FpScratch1 : GpScratch1);

                if (op == "+")
                {
                    MoveRegister(dst, src);
                }
                else if (op == "-")
                {
                    EmitUnaryNeg(dst, src, instruction.Result.Type);
                }
                else if (op == "~")
                {
                    EmitRaw(Is64BitInteger(instruction.Result.Type) ? Op.I64Not : Op.I32Not, dst, src);
                }
                else if (op == "!")
                {
                    if (Is64BitInteger(instruction.Operands[0].Type) || IsPointerLike(instruction.Operands[0].Type))
                        EmitI64Imm(Op.I64EqImm, dst, src, 0);
                    else
                        EmitI32Imm(Op.I32EqImm, dst, src, 0);
                }
                else
                {
                    throw Unsupported(instruction, $"Unsupported unary operator '{op}'.");
                }

                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitBinary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Binary instruction has no result.");

                if (TryEmitPointerBinary(instruction))
                    return;

                var resultType = instruction.Result.Type;
                var leftType = instruction.Operands[0].Type;
                var rightType = instruction.Operands[1].Type;
                var usesFloatingOperands = IsFloatType(leftType) || IsFloatType(rightType) || IsFloatType(resultType);
                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var op = SelectBinaryOp(instruction.Operator, leftType, rightType, resultType);
                if (!usesFloatingOperands && TryEmitBinaryImmediate(instruction, dst, op))
                {
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                MachineRegister left;
                MachineRegister right;
                if (usesFloatingOperands)
                {
                    var floatType = FloatingBinaryComputationType(leftType, rightType, resultType);
                    left = LoadOperandForFloatingBinary(instruction.Operands[0], floatType, FpScratch1, GpScratch1, instruction);
                    right = LoadOperandForFloatingBinary(instruction.Operands[1], floatType, FpScratch2, GpScratch2, instruction);
                }
                else
                {
                    left = LoadOperand(instruction.Operands[0], GpScratch1);
                    right = LoadOperand(instruction.Operands[1], GpScratch2);
                }

                EmitRaw(op, dst, left, right, MayThrow(op));
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private QualifiedType FloatingBinaryComputationType(QualifiedType leftType, QualifiedType rightType, QualifiedType resultType)
            {
                if (IsFloat64(leftType) || IsFloat64(rightType) || IsFloat64(resultType) ||
                    IsLongDouble(leftType) || IsLongDouble(rightType) || IsLongDouble(resultType))
                    return TypeCatalog.Instance.Builtin(BuiltinTypeKind.Double);

                return TypeCatalog.Instance.Builtin(BuiltinTypeKind.Float);
            }

            private MachineRegister LoadOperandForFloatingBinary(
                LirOperand operand,
                QualifiedType floatType,
                MachineRegister floatScratch,
                MachineRegister generalScratch,
                LirInstruction instruction)
            {
                if (IsFloatType(operand.Type))
                {
                    var source = LoadOperand(operand, floatScratch);
                    if ((IsFloat32(operand.Type) && IsFloat32(floatType)) || (IsFloat64(operand.Type) && IsFloat64(floatType)))
                        return source;

                    EmitFloatConversion(floatScratch, source, operand.Type, floatType, instruction);
                    return floatScratch;
                }

                if (!IsIntegerLike(operand.Type))
                    throw Unsupported(instruction, $"Unsupported floating binary operand type {operand.Type.ToDisplayString()}.");

                var integerSource = LoadOperand(operand, generalScratch);
                EmitFloatConversion(floatScratch, integerSource, operand.Type, floatType, instruction);
                return floatScratch;
            }

            private bool TryEmitBinaryImmediate(LirInstruction instruction, MachineRegister destination, Op binaryOp)
            {
                if (instruction.Operands.Length != 2 || instruction.Operands[1].Kind != LirOperandKind.Immediate)
                    return false;

                if (!TrySelectBinaryImmediateOp(binaryOp, out var immediateOp))
                    return false;

                if (!TryGetIntegerImmediate(instruction.Operands[1].Immediate, out var immediate))
                    return false;

                var left = LoadOperand(instruction.Operands[0], GpScratch1);
                EmitI64Imm(immediateOp, destination, left, immediate);
                return true;
            }

            private static bool TrySelectBinaryImmediateOp(Op binaryOp, out Op immediateOp)
            {
                switch (binaryOp)
                {
                    case Op.I32Add: immediateOp = Op.I32AddImm; return true;
                    case Op.I32Sub: immediateOp = Op.I32SubImm; return true;
                    case Op.I32Mul: immediateOp = Op.I32MulImm; return true;
                    case Op.I32And: immediateOp = Op.I32AndImm; return true;
                    case Op.I32Or: immediateOp = Op.I32OrImm; return true;
                    case Op.I32Xor: immediateOp = Op.I32XorImm; return true;
                    case Op.I32Shl: immediateOp = Op.I32ShlImm; return true;
                    case Op.I32Shr: immediateOp = Op.I32ShrImm; return true;
                    case Op.U32Shr: immediateOp = Op.U32ShrImm; return true;
                    case Op.I32Eq: immediateOp = Op.I32EqImm; return true;
                    case Op.I32Ne: immediateOp = Op.I32NeImm; return true;
                    case Op.I32Lt: immediateOp = Op.I32LtImm; return true;
                    case Op.I32Le: immediateOp = Op.I32LeImm; return true;
                    case Op.I32Gt: immediateOp = Op.I32GtImm; return true;
                    case Op.I32Ge: immediateOp = Op.I32GeImm; return true;
                    case Op.U32Lt: immediateOp = Op.U32LtImm; return true;
                    case Op.I64Add: immediateOp = Op.I64AddImm; return true;
                    case Op.I64Sub: immediateOp = Op.I64SubImm; return true;
                    case Op.I64Mul: immediateOp = Op.I64MulImm; return true;
                    case Op.I64And: immediateOp = Op.I64AndImm; return true;
                    case Op.I64Or: immediateOp = Op.I64OrImm; return true;
                    case Op.I64Xor: immediateOp = Op.I64XorImm; return true;
                    case Op.I64Shl: immediateOp = Op.I64ShlImm; return true;
                    case Op.I64Shr: immediateOp = Op.I64ShrImm; return true;
                    case Op.U64Shr: immediateOp = Op.U64ShrImm; return true;
                    case Op.I64Eq: immediateOp = Op.I64EqImm; return true;
                    case Op.I64Ne: immediateOp = Op.I64NeImm; return true;
                    case Op.I64Lt: immediateOp = Op.I64LtImm; return true;
                    case Op.I64Le: immediateOp = Op.I64LeImm; return true;
                    case Op.I64Gt: immediateOp = Op.I64GtImm; return true;
                    case Op.I64Ge: immediateOp = Op.I64GeImm; return true;
                    case Op.U64Lt: immediateOp = Op.U64LtImm; return true;
                    default:
                        immediateOp = Op.Invalid;
                        return false;
                }
            }

            private static bool TryGetIntegerImmediate(object? value, out long immediate)
            {
                switch (value)
                {
                    case null:
                        immediate = 0;
                        return true;
                    case bool b:
                        immediate = b ? 1 : 0;
                        return true;
                    case byte b:
                        immediate = b;
                        return true;
                    case sbyte s:
                        immediate = s;
                        return true;
                    case short s:
                        immediate = s;
                        return true;
                    case ushort u:
                        immediate = u;
                        return true;
                    case int i:
                        immediate = i;
                        return true;
                    case uint u:
                        immediate = u;
                        return true;
                    case long l:
                        immediate = l;
                        return true;
                    case ulong u when u <= long.MaxValue:
                        immediate = (long)u;
                        return true;
                    case char c:
                        immediate = c;
                        return true;
                    default:
                        immediate = 0;
                        return false;
                }
            }

            private bool TryEmitPointerBinary(LirInstruction instruction)
            {
                var lhsType = instruction.Operands[0].Type;
                var rhsType = instruction.Operands[1].Type;
                var resultType = instruction.Result!.Type;
                var opText = instruction.Operator;

                if (opText == "+" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitRaw(Is64BitInteger(rhsType) ? Op.PtrAddI64 : Op.PtrAddI32, dst, ptr, index, imm: PointerScale(lhsType));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (opText == "+" && IsIntegerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    var index = LoadOperand(instruction.Operands[0], GpScratch2);
                    var ptr = LoadOperand(instruction.Operands[1], GpScratch1);
                    EmitRaw(Is64BitInteger(lhsType) ? Op.PtrAddI64 : Op.PtrAddI32, dst, ptr, index, imm: PointerScale(rhsType));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (opText == "-" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitRaw(Is64BitInteger(rhsType) ? Op.I64Neg : Op.I32Neg, GpScratch2, index);
                    EmitRaw(Is64BitInteger(rhsType) ? Op.PtrAddI64 : Op.PtrAddI32, dst, ptr, GpScratch2, imm: PointerScale(lhsType));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (opText == "-" && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitRaw(Op.PtrDiff, dst, left, right, imm: PointerScale(lhsType));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if ((opText is "==" or "!=") && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitRaw(opText == "==" ? Op.I64Eq : Op.I64Ne, dst, left, right);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                return false;
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Conversion instruction has no source operand.");

                var srcType = instruction.Operands[0].Type;
                var dstType = instruction.Result.Type;
                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var src = LoadOperand(instruction.Operands[0], IsFloatType(srcType) ? FpScratch1 : GpScratch1);
                EmitConversion(dst, src, srcType, dstType, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitAddressOf(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid addressof instruction.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                MaterializeAddress(instruction.Address, dst);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitLoad(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid load instruction.");

                if (IsAggregateType(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    MaterializeAddress(instruction.Address, GpScratch1);
                    EmitRaw(Op.CpBlk, destinationAddress, GpScratch1, MachineRegister.Invalid, imm: Math.Max(1, SizeOf(instruction.Result.Type)));
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var parts = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                EmitMem(LoadOpForType(instruction.Result.Type), dst, parts.BaseRegister, parts.Offset, parts.IndexRegister, parts.BaseKind,
                    AlignmentOf(instruction.Result.Type), parts.ScaleLog2);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid store instruction.");

                var storeType = instruction.Address.ElementType;
                if (IsAggregateType(storeType))
                {
                    if (instruction.Operands.Length == 0)
                        throw Unsupported(instruction, "Aggregate store has no source operand.");
                    MaterializeAddress(instruction.Address, GpScratch0);
                    MaterializeOperandStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                    EmitRaw(Op.CpBlk, GpScratch0, GpScratch1, MachineRegister.Invalid, imm: Math.Max(1, SizeOf(storeType)));
                    return;
                }

                var src = LoadOperandAs(instruction.Operands[0], storeType, GpScratch0, FpScratch0, instruction);
                var parts = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                EmitMem(StoreOpForType(storeType), src, parts.BaseRegister, parts.Offset, parts.IndexRegister, parts.BaseKind, AlignmentOf(storeType), parts.ScaleLog2);
            }

            private void EmitZeroMemory(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid zeromem instruction.");

                var size = instruction.Operands.Length == 0 ? SizeOf(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
                MaterializeAddress(instruction.Address, GpScratch0);
                _asm.LiI32(GpScratch1, 0);
                EmitRaw(Op.InitBlk, GpScratch0, GpScratch1, MachineRegister.Invalid, imm: size);
            }

            private void EmitCall(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Call has no callee operand.");

                var callee = instruction.Operands[0];

                if (TryResolveCallTarget(callee, out var methodId, out var isPrintf))
                {
                    if (isPrintf)
                    {
                        EmitCStringWriteCall(instruction, methodId);
                        return;
                    }

                    var directCallFlags = BuildCallFlags(instruction);
                    MarshalCallArguments(instruction, startOperand: 1);
                    PrepareVariadicCall(instruction);
                    var directCallOp = CallOpForReturn(instruction.Result?.Type ?? TypeCatalog.Instance.Builtin(BuiltinTypeKind.Void), isInternal: false);
                    EmitManagedDirectCall(directCallOp, methodId, directCallFlags);
                    EmitCallResult(instruction);
                    return;
                }

                var indirectCallFlags = BuildCallFlags(instruction);
                MarshalCallArguments(instruction, startOperand: 1);
                PrepareVariadicCall(instruction);
                var target = LoadOperand(callee, GpScratch0);
                var indirectCallOp = IndirectCallOpForReturn(instruction.Result?.Type ?? TypeCatalog.Instance.Builtin(BuiltinTypeKind.Void));
                EmitRawIndirectCall(indirectCallOp, target, indirectCallFlags);
                EmitCallResult(instruction);
            }

            private void EmitCallResult(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var value = CAbi.ClassifyValue(_owner._target, instruction.Result.Type, isReturn: true);
                if (IsAggregateType(instruction.Result.Type))
                {
                    if (value.PassingKind == AbiPassingKind.Indirect)
                    {
                        // The callee wrote directly into the hidden return buffer
                        return;
                    }

                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    EmitRaw(Op.CpBlk, destinationAddress, MachineRegisters.ReturnValue0, MachineRegister.Invalid, imm: Math.Max(1, value.Size));
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var ret = RegisterClassOf(ClassifyValue(instruction.Result.Type)) == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10;
                MoveRegister(dst, ret);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitCStringWriteCall(LirInstruction instruction, int methodId)
            {
                if (instruction.Operands.Length != 2)
                    throw Unsupported(instruction, "Only __printf with a single C string argument is supported.");

                var arg = instruction.Operands[1];
                if (arg.Kind == LirOperandKind.Immediate && arg.Immediate is string text)
                    LoadCStringLiteral(text, MachineRegister.X10);
                else
                    LoadOperandInto(arg, MachineRegister.X10);

                EmitInternalCall(Op.CallInternalVoid, methodId, CallFlags.InternalCall);

                if (instruction.Result is not null)
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                    _asm.LiI32(dst, 0);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                }
            }
            private void EmitVaStart(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 0)
                    throw Unsupported(instruction, "VaStart instruction must not have explicit operands.");

                if (instruction.Result is null)
                    return;

                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var frame = _allocation.Frame;

                if (frame.HasVarArgsPointer)
                    EmitMem(LoadOpForType(instruction.Result.Type), destination, MachineRegister.Invalid,
                        frame.VarArgsPointerOffset, MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);
                else
                    _asm.LiI64(destination, 0);

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }
            private void PrepareVariadicCall(LirInstruction instruction)
            {
                var signature = instruction.CallSignature;
                if (signature is null || !signature.IsVariadic)
                    return;

                var fixedCount = signature.Parameters.Length;
                var firstVariadicOperand = 1 + fixedCount;
                var variadicCount = instruction.Operands.Length - firstVariadicOperand;
                if (variadicCount <= 0)
                {
                    _asm.LiI64(VarArgsRegister, 0);
                    return;
                }

                var normalStackSlots = CountStackArgumentSlots(instruction, startOperand: 1);
                var baseOffset = checked(_allocation.Frame.OutgoingArgumentAreaOffset + normalStackSlots * _owner._allocationOptions.StackArgumentSlotSize);

                for (var i = 0; i < variadicCount; i++)
                {
                    var operand = instruction.Operands[firstVariadicOperand + i];
                    if (IsAggregateType(operand.Type))
                        throw Unsupported(instruction, "Aggregate variadic arguments are not supported yet.");
                    var source = LoadOperand(operand, IsFloatType(operand.Type) ? FpScratch0 : GpScratch0);
                    EmitMem(StoreOpForType(operand.Type), source, MachineRegister.Invalid,
                        checked(baseOffset + i * _owner._allocationOptions.StackArgumentSlotSize), MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                }

                EmitI64Imm(Op.I64AddImm, VarArgsRegister, Sp, baseOffset);
            }
            private int CountStackArgumentSlots(LirInstruction instruction, int startOperand)
            {
                var bytes = CAbi.ComputeOutgoingArgumentAreaSize(
                    instruction,
                    startOperand,
                    _owner._target,
                    _owner._allocationOptions.StackArgumentSlotSize,
                    includeVariadicHomeArea: false);
                return CAbi.SlotsFor(bytes, _owner._allocationOptions.StackArgumentSlotSize);
            }
            private void MarshalCallArguments(LirInstruction instruction, int startOperand)
            {
                var cursor = new AbiCursor();
                MarshalHiddenReturnBufferArgument(instruction, ref cursor);
                for (var i = startOperand; i < instruction.Operands.Length; i++)
                    MarshalCallArgument(instruction, instruction.Operands[i], ref cursor);
            }

            private CallFlags BuildCallFlags(LirInstruction instruction)
            {
                if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    return CallFlags.HiddenReturnBuffer;
                return CallFlags.None;
            }

            private void MarshalHiddenReturnBufferArgument(LirInstruction instruction, ref AbiCursor cursor)
            {
                if (instruction.Result is null || !CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    return;

                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var address = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                if (location.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(location.Register, address);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    EmitMem(Op.StPtr, address, MachineRegister.Invalid,
                        checked(_allocation.Frame.OutgoingArgumentAreaOffset + location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)),
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);
                    return;
                }

                throw Unsupported(instruction, "Invalid hidden return buffer ABI location.");
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand.");

                var trueFallsThrough = IsFallthroughTarget(instruction.TrueTarget);
                var falseFallsThrough = IsFallthroughTarget(instruction.FalseTarget);
                var type = instruction.Operands[0].Type;

                if (IsFloatType(type))
                {
                    if (IsLongDouble(type))
                        throw Unsupported(instruction, "long double branch conditions are not supported by the register-bytecode backend.");

                    var condf = LoadOperand(instruction.Operands[0], FpScratch0);
                    EmitLiFloat(FpScratch1, 0.0, type);

                    if (trueFallsThrough && !falseFallsThrough)
                    {
                        _asm.Branch(IsFloat32(type) ? Op.BrF32Eq : Op.BrF64Eq, condf, FpScratch1, LabelOf(instruction.FalseTarget));
                        return;
                    }

                    _asm.Branch(IsFloat32(type) ? Op.BrF32Ne : Op.BrF64Ne, condf, FpScratch1, LabelOf(instruction.TrueTarget));
                    if (!falseFallsThrough)
                        _asm.J(LabelOf(instruction.FalseTarget));
                    return;
                }

                var cond = LoadOperand(instruction.Operands[0], GpScratch0);
                if (trueFallsThrough && !falseFallsThrough)
                {
                    _asm.Branch(BranchOpForTruth(type, branchIfTrue: false), cond, LabelOf(instruction.FalseTarget));
                    return;
                }

                _asm.Branch(BranchOpForTruth(type, branchIfTrue: true), cond, LabelOf(instruction.TrueTarget));
                if (!falseFallsThrough)
                    _asm.J(LabelOf(instruction.FalseTarget));
            }

            private Op BranchOpForTruth(QualifiedType type, bool branchIfTrue)
            {
                if (Is64BitInteger(type) || IsPointerLike(type))
                    return branchIfTrue ? Op.BrTrueI64 : Op.BrFalseI64;

                return branchIfTrue ? Op.BrTrueI32 : Op.BrFalseI32;
            }

            private void EmitSwitch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Switch expects one key operand.");

                var key = LoadOperand(instruction.Operands[0], GpScratch0);
                var first = -1;
                var count = 0;
                foreach (var @case in instruction.SwitchCases)
                {
                    var value = ImmediateToInt64(@case.Value);
                    var index = _asm.AddSwitchEntry(value, LabelOf(@case.Target));
                    if (first < 0)
                        first = index;
                    count++;
                }

                if (count != 0)
                {
                    if (Is64BitInteger(instruction.Operands[0].Type))
                        _asm.SwitchI64(key, first, count);
                    else
                        _asm.SwitchI32(key, first, count);
                }

                if (!IsFallthroughTarget(instruction.Target))
                    _asm.J(LabelOf(instruction.Target));
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0 || IsVoid(instruction.Operands[0].Type))
                {
                    EmitEpilogue();
                    _asm.RetVoid();
                    return;
                }

                var operand = instruction.Operands[0];
                if (IsAggregateType(operand.Type))
                {
                    var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: true);
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    var sourceAddress = GpScratch0;

                    if (value.PassingKind == AbiPassingKind.Indirect)
                    {
                        MaterializeIncomingHiddenReturnBufferAddress(GpScratch1);
                        EmitRaw(Op.CpBlk, GpScratch1, sourceAddress, MachineRegister.Invalid, imm: Math.Max(1, value.Size));
                        EmitEpilogue();
                        _asm.RetVoid();
                        return;
                    }

                    EmitEpilogue();
                    _asm.RetValue(sourceAddress, Math.Max(1, value.Size));
                    return;
                }

                var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
                if (RegisterClassOf(ClassifyValue(returnType)) == AbiRegisterClass.Floating)
                {
                    LoadOperandIntoAs(operand, MachineRegister.F10, returnType, instruction);
                    EmitEpilogue();
                    _asm.RetF(MachineRegister.F10);
                    return;
                }

                LoadOperandIntoAs(operand, MachineRegister.X10, returnType, instruction);
                EmitEpilogue();
                _asm.RetI(MachineRegister.X10);
            }

            private void EmitEpilogue()
            {
                var frame = _allocation.Frame;
                foreach (var pair in frame.SavedRegisterOffsets.OrderByDescending(static p => p.Value))
                {
                    var op = IsFloatRegister(pair.Key) ? Op.LdF64 : Op.LdI8;
                    EmitMem(op, pair.Key, MachineRegister.Invalid, pair.Value, MachineRegister.Invalid, MemoryBase.StackPointer, alignment: 8);
                }

                if (frame.FrameSize != 0)
                    EmitI64Imm(Op.I64AddImm, Sp, Sp, frame.FrameSize);
            }

            private void EmitParallelCopy(LirInstruction instruction)
            {
                if (instruction.ParallelCopies.Length == 0)
                    return;

                var physicalCopiesBuilder = ImmutableArray.CreateBuilder<LirParallelCopy>(instruction.ParallelCopies.Length);
                foreach (var copy in instruction.ParallelCopies)
                {
                    if (RequiresPhysicalParallelCopy(copy))
                        physicalCopiesBuilder.Add(copy);
                }

                if (physicalCopiesBuilder.Count == 0)
                    return;

                var physicalCopies = physicalCopiesBuilder.ToImmutable();
                if (TryEmitDirectParallelCopies(physicalCopies, instruction))
                    return;

                EmitParallelCopyThroughTemporaries(physicalCopies, instruction);
            }

            private bool TryEmitDirectParallelCopies(ImmutableArray<LirParallelCopy> copies, LirInstruction instruction)
            {
                if (copies.Length == 1)
                {
                    EmitDirectParallelCopy(copies[0], instruction);
                    return true;
                }

                foreach (var copy in copies)
                {
                    if (IsAggregateType(copy.Destination.Type))
                        return false;
                }

                if (HasPhysicalStorageClobber(copies))
                    return false;

                foreach (var copy in copies)
                    EmitDirectParallelCopy(copy, instruction);

                return true;
            }

            private bool HasPhysicalStorageClobber(ImmutableArray<LirParallelCopy> copies)
            {
                for (var i = 0; i < copies.Length; i++)
                {
                    var hasDestinationRegister = TryGetDestinationPhysicalRegister(copies[i].Destination, out var destinationRegister);
                    var hasDestinationStackOffset = TryGetDestinationStackOffset(copies[i].Destination, out var destinationStackOffset);
                    if (!hasDestinationRegister && !hasDestinationStackOffset)
                        continue;

                    for (var j = 0; j < copies.Length; j++)
                    {
                        if (i == j && ReferencesSamePhysicalStorage(copies[j].Source, copies[i].Destination))
                            continue;

                        if (hasDestinationRegister &&
                            TryGetOperandPhysicalRegister(copies[j].Source, out var sourceRegister) &&
                            sourceRegister == destinationRegister)
                        {
                            return true;
                        }

                        if (hasDestinationStackOffset &&
                            TryGetOperandStackOffset(copies[j].Source, out var sourceStackOffset) &&
                            sourceStackOffset == destinationStackOffset)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private void EmitDirectParallelCopy(LirParallelCopy copy, LirInstruction instruction)
            {
                if (!RequiresPhysicalParallelCopy(copy))
                    return;

                if (ReferencesSamePhysicalStorage(copy.Source, copy.Destination))
                    return;

                if (IsAggregateType(copy.Destination.Type))
                {
                    EmitAggregateCopyToRegisterStorage(copy.Destination, copy.Source, instruction);
                    return;
                }

                var destination = GetWritableRegister(copy.Destination, GpScratch0, FpScratch0);
                LoadOperandInto(copy.Source, destination);
                StoreWritableRegisterIfSpilled(copy.Destination, destination);
            }

            private bool ReferencesSamePhysicalStorage(LirOperand source, LirVirtualRegister destination)
            {
                if (source.Kind != LirOperandKind.Register || source.Register is null)
                    return false;

                if (!_allocation.TryGetAllocation(source.Register, out var sourceAllocation) ||
                    !_allocation.TryGetAllocation(destination, out var destinationAllocation))
                {
                    return false;
                }

                if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
                    return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;

                if (sourceAllocation.IsSpilled && destinationAllocation.IsSpilled)
                    return sourceAllocation.StackOffset == destinationAllocation.StackOffset;

                return false;
            }

            private bool TryGetDestinationPhysicalRegister(LirVirtualRegister destination, out MachineRegister register)
            {
                register = MachineRegister.Invalid;
                if (!_allocation.TryGetAllocation(destination, out var allocation) || allocation.IsSpilled)
                    return false;

                register = allocation.PhysicalRegister;
                return register != MachineRegister.Invalid;
            }

            private bool TryGetOperandPhysicalRegister(LirOperand operand, out MachineRegister register)
            {
                register = MachineRegister.Invalid;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;

                if (!_allocation.TryGetAllocation(operand.Register, out var allocation) || allocation.IsSpilled)
                    return false;

                register = allocation.PhysicalRegister;
                return register != MachineRegister.Invalid;
            }

            private bool TryGetDestinationStackOffset(LirVirtualRegister destination, out int stackOffset)
            {
                stackOffset = -1;
                if (!_allocation.TryGetAllocation(destination, out var allocation) || !allocation.IsSpilled)
                    return false;

                stackOffset = allocation.StackOffset;
                return stackOffset >= 0;
            }

            private bool TryGetOperandStackOffset(LirOperand operand, out int stackOffset)
            {
                stackOffset = -1;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;

                if (!_allocation.TryGetAllocation(operand.Register, out var allocation) || !allocation.IsSpilled)
                    return false;

                stackOffset = allocation.StackOffset;
                return stackOffset >= 0;
            }

            private void EmitParallelCopyThroughTemporaries(ImmutableArray<LirParallelCopy> physicalCopies, LirInstruction instruction)
            {
                var tempOffsets = new int[physicalCopies.Length];
                var tempCursor = 0;
                for (var i = 0; i < physicalCopies.Length; i++)
                {
                    tempOffsets[i] = tempCursor;
                    tempCursor = checked(tempCursor + ParallelCopyTempSlotSize(physicalCopies[i]));
                }

                if (_allocation.Frame.ParallelCopyTempSize < tempCursor)
                    throw Unsupported(instruction, "Parallel-copy temporary area was not reserved.");

                for (var i = 0; i < physicalCopies.Length; i++)
                {
                    var copy = physicalCopies[i];
                    var tempOffset = checked(_allocation.Frame.ParallelCopyTempOffset + tempOffsets[i]);
                    if (IsAggregateType(copy.Destination.Type))
                    {
                        MaterializeOperandStorageAddress(copy.Source, GpScratch1, instruction);
                        EmitI64Imm(Op.I64AddImm, GpScratch0, Sp, tempOffset);
                        EmitRaw(Op.CpBlk, GpScratch0, GpScratch1, MachineRegister.Invalid, imm: Math.Max(1, SizeOf(copy.Destination.Type)));
                        continue;
                    }

                    var isFloat = IsFloatType(copy.Destination.Type);
                    var src = LoadOperand(copy.Source, isFloat ? FpScratch0 : GpScratch0);
                    EmitMem(isFloat ? Op.StF64 : Op.StI8, src, MachineRegister.Invalid,
                        tempOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                }

                for (var i = 0; i < physicalCopies.Length; i++)
                {
                    var copy = physicalCopies[i];
                    var tempOffset = checked(_allocation.Frame.ParallelCopyTempOffset + tempOffsets[i]);
                    if (IsAggregateType(copy.Destination.Type))
                    {
                        var dstAddress = MaterializeVirtualRegisterStorageAddress(copy.Destination, GpScratch0);
                        EmitI64Imm(Op.I64AddImm, GpScratch1, Sp, tempOffset);
                        EmitRaw(Op.CpBlk, dstAddress, GpScratch1, MachineRegister.Invalid, imm: Math.Max(1, SizeOf(copy.Destination.Type)));
                        continue;
                    }

                    var isFloat = IsFloatType(copy.Destination.Type);
                    var dst = GetWritableRegister(copy.Destination, GpScratch0, FpScratch0);
                    EmitMem(isFloat ? Op.LdF64 : Op.LdI8, dst, MachineRegister.Invalid,
                        tempOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                    StoreWritableRegisterIfSpilled(copy.Destination, dst);
                }
            }
            private int ParallelCopyTempSlotSize(LirParallelCopy copy)
            {
                var size = Math.Max(SizeOfStorage(copy.Destination.Type), SizeOfStorage(copy.Source.Type));
                return CAbi.AlignUp(Math.Max(_owner._allocationOptions.SpillSlotSize, size), _owner._allocationOptions.SpillSlotAlignment);
            }

            private int SizeOfStorage(QualifiedType type)
                => Math.Max(1, SizeOf(type));

            private static bool RequiresPhysicalParallelCopy(LirParallelCopy copy)
            {
                if (copy.Destination.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                    return false;

                if (IsVoid(copy.Destination.Type))
                    return false;

                if (copy.Source.Kind is LirOperandKind.Void or LirOperandKind.None)
                    return false;

                if (copy.Source.Kind == LirOperandKind.Register &&
                    copy.Source.Register is { RegisterClass: LirRegisterClass.Void or LirRegisterClass.Memory })
                {
                    return false;
                }

                if (IsVoid(copy.Source.Type))
                    return false;

                return true;
            }
            private bool TryResolveCallTarget(LirOperand callee, out int methodId, out bool isPrintf)
            {
                methodId = 0;
                isPrintf = false;

                return callee.Kind == LirOperandKind.Symbol &&
                       callee.Symbol is FunctionSymbol function &&
                       TryGetCallableMethodId(function, out methodId, out isPrintf);
            }

            private bool TryGetCallableMethodId(FunctionSymbol function, out int methodId, out bool isPrintf)
            {
                methodId = 0;
                isPrintf = false;

                if (function.IntrinsicKind == RuntimeIntrinsicKind.BuiltinVaStart)
                    return false;

                if (function.IntrinsicKind == RuntimeIntrinsicKind.CStringWrite
                    || string.Equals(function.Name, StandardHeaders.PrintfIntrinsicName, StringComparison.Ordinal))
                {
                    methodId = PrintfMethodId;
                    isPrintf = true;
                    return true;
                }

                if (_owner._methodIds.TryGetValue(function, out methodId))
                    return true;

                if (_owner._methodIdsByName.TryGetValue(function.Name, out methodId))
                    return true;

                return false;
            }

            private bool TryMaterializeFunctionPointer(FunctionSymbol function, MachineRegister destination)
            {
                if (!TryGetCallableMethodId(function, out var methodId, out var isPrintf))
                    return false;
                if (isPrintf)
                    return false;

                var pc = _owner._target.PointerSize == 4
                    ? _asm.Emit(InstrDesc.Li(Op.LiI32, destination, 0))
                    : _asm.Emit(InstrDesc.Li(Op.LiI64, destination, 0));
                _owner._functionPointerFixups.Add(new FunctionPointerFixup(pc, methodId));
                return true;
            }


            private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
            {
                if (SameType(operand.Type, targetType))
                {
                    LoadOperandInto(operand, destination);
                    return;
                }

                if (!CanCodegenConvert(operand.Type, targetType))
                    throw Unsupported(instruction, $"Cannot convert operand from {operand.Type.ToDisplayString()} to {targetType.ToDisplayString()} while loading it into a target register.");

                var source = LoadOperand(operand, IsFloatType(operand.Type) ? FpScratch1 : GpScratch1);
                EmitConversion(destination, source, operand.Type, targetType, instruction);
            }

            private MachineRegister LoadOperandAs(LirOperand operand, QualifiedType targetType, MachineRegister generalScratch, MachineRegister floatScratch, LirInstruction instruction)
            {
                if (SameType(operand.Type, targetType))
                    return LoadOperand(operand, IsFloatType(targetType) ? floatScratch : generalScratch);

                if (!CanCodegenConvert(operand.Type, targetType))
                    throw Unsupported(instruction, $"Cannot convert operand from {operand.Type.ToDisplayString()} to {targetType.ToDisplayString()} while loading it.");

                var source = LoadOperand(operand, IsFloatType(operand.Type) ? FpScratch1 : GpScratch1);
                var destination = IsFloatType(targetType) ? floatScratch : generalScratch;
                EmitConversion(destination, source, operand.Type, targetType, instruction);
                return destination;
            }

            private static bool CanCodegenConvert(QualifiedType from, QualifiedType to)
            {
                if (from.IsError || to.IsError)
                    return true;

                if (from.ToDisplayString() == to.ToDisplayString())
                    return true;

                var fromScalar = IsIntegerLike(from) || IsFloatType(from) || IsPointerLike(from);
                var toScalar = IsIntegerLike(to) || IsFloatType(to) || IsPointerLike(to);
                return fromScalar && toScalar;
            }

            private void LoadOperandInto(LirOperand operand, MachineRegister destination)
            {
                var actual = LoadOperand(operand, destination);
                MoveRegister(destination, actual);
            }

            private MachineRegister LoadOperand(LirOperand operand, MachineRegister preferred)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        return LoadVirtualRegister(operand.Register, preferred);

                    case LirOperandKind.Immediate:
                        EmitImmediate(preferred, operand.Immediate, operand.Type);
                        return preferred;

                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        EmitMem(LoadOpForType(operand.Type), preferred, MachineRegister.Invalid, offset, MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                        return preferred;

                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, preferred);
                        return preferred;

                    case LirOperandKind.Symbol:
                        if (operand.Symbol is null)
                            throw new InvalidOperationException("Symbol operand has no symbol.");
                        if (TryMaterializeStaticSymbolAddress(operand.Symbol, preferred))
                            return preferred;
                        if (operand.Symbol is FunctionSymbol function)
                        {
                            if (TryMaterializeFunctionPointer(function, preferred))
                                return preferred;
                            throw new NotSupportedException($"Cannot materialize external function pointer '{function.Name}' because it has no register-bytecode entry PC.");
                        }
                        throw new NotSupportedException($"Cannot materialize symbol '{operand.Symbol.Name}' as static data.");

                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (IsFloatType(operand.Type))
                            EmitLiFloat(preferred, 0.0, operand.Type);
                        else
                            _asm.LiI64(preferred, 0);
                        return preferred;

                    default:
                        throw new NotSupportedException($"Cannot load LIR operand kind {operand.Kind} into a register.");
                }
            }

            private MachineRegister LoadVirtualRegister(LirVirtualRegister register, MachineRegister preferred)
            {
                if (IsAggregateType(register.Type))
                    throw new NotSupportedException("Aggregate virtual register " + register.Name + " cannot be loaded as a scalar register.");

                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;

                EmitMem(LoadOpForType(register.Type), preferred, MachineRegister.Invalid,
                    alloc.StackOffset, MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(register.Type));
                return preferred;
            }

            private MachineRegister GetWritableRegister(LirVirtualRegister register, MachineRegister generalScratch, MachineRegister floatScratch)
            {
                if (IsAggregateType(register.Type))
                    throw new NotSupportedException("Aggregate virtual register " + register.Name + " must be accessed through its storage address.");

                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;

                return IsFloatType(register.Type) ? floatScratch : generalScratch;
            }

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, MachineRegister source)
            {
                if (IsAggregateType(register.Type))
                    throw new NotSupportedException("Aggregate virtual register " + register.Name + " must be stored with a block copy.");

                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return;

                EmitMem(StoreOpForType(register.Type), source, MachineRegister.Invalid,
                    alloc.StackOffset, MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(register.Type));
            }
            private MachineRegister MaterializeVirtualRegisterStorageAddress(LirVirtualRegister register, MachineRegister destination)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    throw new NotSupportedException("Aggregate virtual register " + register.Name + " must be stack-backed.");

                EmitI64Imm(Op.I64AddImm, destination, Sp, alloc.StackOffset);
                return destination;
            }

            private void MaterializeOperandStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        MaterializeVirtualRegisterStorageAddress(operand.Register, destination);
                        return;

                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        EmitI64Imm(Op.I64AddImm, destination, Sp, offset);
                        return;

                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, destination);
                        return;

                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        throw Unsupported(instruction, "Undefined aggregate operand has no storage address.");

                    default:
                        throw Unsupported(instruction, $"Cannot materialize storage address for aggregate operand kind {operand.Kind}.");
                }
            }

            private void EmitAggregateCopyToRegisterStorage(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, GpScratch0);
                var size = Math.Max(1, SizeOf(destination.Type));
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    _asm.LiI32(GpScratch1, 0);
                    EmitRaw(Op.InitBlk, destinationAddress, GpScratch1, MachineRegister.Invalid, imm: size);
                    return;
                }

                MaterializeOperandStorageAddress(source, GpScratch1, instruction);
                EmitRaw(Op.CpBlk, destinationAddress, GpScratch1, MachineRegister.Invalid, imm: size);
            }

            private void MaterializeIncomingStackAddress(int offset, MachineRegister destination)
            {
                EmitI64Imm(Op.I64AddImm, destination, MachineRegisters.ThreadPointer, offset);
            }

            private void MaterializeIncomingHiddenReturnBufferAddress(MachineRegister destination)
            {
                if (_allocation.Frame.HasHiddenReturnBuffer)
                {
                    EmitMem(Op.LdPtr, destination, MachineRegister.Invalid, _allocation.Frame.HiddenReturnBufferOffset,
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);
                    return;
                }

                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (location.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(destination, location.Register);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    EmitMem(Op.LdPtr, destination, MachineRegister.Invalid,
                        location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize),
                        MachineRegister.Invalid, MemoryBase.ThreadPointer, _owner._target.PointerAlignment);
                    return;
                }

                throw new InvalidOperationException("Invalid hidden return buffer ABI location.");
            }

            private void MaterializeOutgoingStackAddress(int offset, MachineRegister destination)
            {
                EmitI64Imm(Op.I64AddImm, destination, Sp, checked(_allocation.Frame.OutgoingArgumentAreaOffset + offset));
            }

            private void EmitLoadRawBitsFromAddress(MachineRegister address, int offset, MachineRegister destination, int size)
                => EmitLoadRawBitsFromMemory(destination, address, offset, MemoryBase.Register, size);

            private void EmitLoadRawBitsFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, MemoryBase memoryBase, int size)
            {
                size = Math.Max(1, size);
                switch (size)
                {
                    case 1:
                        EmitMem(Op.LdU1, destination, baseRegister, offset, MachineRegister.Invalid, memoryBase, 1);
                        return;
                    case 2:
                        EmitMem(Op.LdU2, destination, baseRegister, offset, MachineRegister.Invalid, memoryBase, 2);
                        return;
                    case 4:
                        EmitMem(Op.LdU4, destination, baseRegister, offset, MachineRegister.Invalid, memoryBase, 4);
                        return;
                    case 8:
                        EmitMem(Op.LdI8, destination, baseRegister, offset, MachineRegister.Invalid, memoryBase, 8);
                        return;
                }

                _asm.LiI64(destination, 0);
                for (var i = 0; i < size; i++)
                {
                    EmitMem(Op.LdU1, GpScratch2, baseRegister, checked(offset + i), MachineRegister.Invalid, memoryBase, 1);
                    if (i != 0)
                        EmitI64Imm(Op.I64ShlImm, GpScratch2, GpScratch2, i * 8);
                    EmitRaw(Op.I64Or, destination, destination, GpScratch2);
                }
            }

            private void EmitStoreRawBitsToAddress(MachineRegister source, MachineRegister address, int offset, int size)
                => EmitStoreRawBitsToMemory(source, address, offset, MemoryBase.Register, size);

            private void EmitStoreRawBitsToMemory(MachineRegister source, MachineRegister baseRegister, int offset, MemoryBase memoryBase, int size)
            {
                size = Math.Max(1, size);
                switch (size)
                {
                    case 1:
                        EmitMem(Op.StI1, source, baseRegister, offset, MachineRegister.Invalid, memoryBase, 1);
                        return;
                    case 2:
                        EmitMem(Op.StI2, source, baseRegister, offset, MachineRegister.Invalid, memoryBase, 2);
                        return;
                    case 4:
                        EmitMem(Op.StI4, source, baseRegister, offset, MachineRegister.Invalid, memoryBase, 4);
                        return;
                    case 8:
                        EmitMem(Op.StI8, source, baseRegister, offset, MachineRegister.Invalid, memoryBase, 8);
                        return;
                }

                for (var i = 0; i < size; i++)
                {
                    if (i == 0)
                    {
                        EmitMem(Op.StI1, source, baseRegister, offset, MachineRegister.Invalid, memoryBase, 1);
                    }
                    else
                    {
                        EmitI64Imm(Op.U64ShrImm, GpScratch2, source, i * 8);
                        EmitMem(Op.StI1, GpScratch2, baseRegister, checked(offset + i), MachineRegister.Invalid, memoryBase, 1);
                    }
                }
            }

            private void MarshalCallArgument(LirInstruction instruction, LirOperand operand, ref AbiCursor cursor)
            {
                var value = CAbi.ClassifyValue(_owner._target, operand.Type);
                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    MaterializeOperandStorageAddress(operand, GpScratch1, instruction);
                    foreach (var segment in value.Segments)
                    {
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                        if (loc.Kind == AbiLocationKind.Register)
                        {
                            EmitLoadRawBitsFromAddress(GpScratch1, segment.Offset, loc.Register, segment.Size);
                        }
                        else if (loc.Kind == AbiLocationKind.Stack)
                        {
                            EmitLoadRawBitsFromAddress(GpScratch1, segment.Offset, GpScratch0, segment.Size);
                            EmitStoreRawBitsToMemory(GpScratch0, MachineRegister.Invalid, checked(_allocation.Frame.OutgoingArgumentAreaOffset + loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), MemoryBase.StackPointer, segment.Size);
                        }
                        else
                        {
                            throw Unsupported(instruction, "Invalid aggregate argument ABI location.");
                        }
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(operand.Type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    MaterializeOperandStorageAddress(operand, GpScratch1, instruction);
                    MaterializeOutgoingStackAddress(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), GpScratch0);
                    EmitRaw(Op.CpBlk, GpScratch0, GpScratch1, MachineRegister.Invalid, imm: value.Size);
                    return;
                }

                var scalar = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (scalar.Kind == AbiLocationKind.Register)
                {
                    LoadOperandInto(operand, scalar.Register);
                }
                else if (scalar.Kind == AbiLocationKind.Stack)
                {
                    var src = LoadOperand(operand, IsFloatType(operand.Type) ? FpScratch0 : GpScratch0);
                    EmitMem(StoreOpForType(operand.Type), src, MachineRegister.Invalid,
                        checked(_allocation.Frame.OutgoingArgumentAreaOffset + scalar.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)),
                        MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                }
                else if (scalar.Kind != AbiLocationKind.None)
                {
                    throw Unsupported(instruction, "Invalid scalar argument ABI location.");
                }
            }

            private static AbiCursor ToAbiCursor(ParameterState state)
                => new AbiCursor { Integer = state.Integer, Float = state.Float, Stack = state.Stack };

            private void SyncParameterState(AbiCursor cursor)
            {
                _parameters.Integer = cursor.Integer;
                _parameters.Float = cursor.Float;
                _parameters.Stack = cursor.Stack;
            }


            private void EmitImmediate(MachineRegister destination, object? value, QualifiedType type)
            {
                if (value is null)
                {
                    if (IsFloatType(type))
                        EmitLiFloat(destination, 0.0, type);
                    else
                        _asm.LiI64(destination, 0);
                    return;
                }

                if (value is string text)
                {
                    LoadCStringLiteral(text, destination);
                    return;
                }

                if (IsFloatType(type))
                {
                    EmitLiFloat(destination, Convert.ToDouble(value, CultureInfo.InvariantCulture), type);
                    return;
                }

                if (value is bool b)
                {
                    _asm.LiI32(destination, b ? 1 : 0);
                    return;
                }

                if (value is char c)
                {
                    _asm.LiI32(destination, c);
                    return;
                }

                var i64 = ImmediateObjectToInt64(value);
                if (i64 >= int.MinValue && i64 <= int.MaxValue)
                    _asm.LiI32(destination, unchecked((int)i64));
                else
                    _asm.LiI64(destination, i64);
            }

            private void LoadCStringLiteral(string text, MachineRegister destination)
            {
                var bytes = Encoding.UTF8.GetBytes((text ?? string.Empty) + "\0");
                LoadStaticBlob(bytes, destination);
            }

            private void LoadStaticBlob(byte[] bytes, MachineRegister destination)
            {
                var offset = _asm.AddBlob(bytes);
                var packed = ((long)(uint)offset << 32) | (uint)bytes.Length;
                EmitRaw(Op.StaticData, destination, MachineRegister.Invalid, MachineRegister.Invalid, imm: packed);
            }

            private bool TryMaterializeStaticSymbolAddress(Symbol symbol, MachineRegister destination)
            {
                if (TryGetStringLiteralFromSymbol(symbol, out var text))
                {
                    LoadCStringLiteral(text, destination);
                    return true;
                }

                return false;
            }

            private static bool TryGetStringLiteralFromSymbol(Symbol symbol, out string text)
            {
                text = string.Empty;

                if (symbol is TypedSymbol { DeclaringSyntax: LiteralExpressionSyntax literal } &&
                    literal.LiteralToken.Value is string literalText)
                {
                    text = literalText;
                    return true;
                }

                return false;
            }

            private AddressParts BuildAddress(LirAddress address, MachineRegister scratchBase, MachineRegister scratchIndex)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        {
                            if (address.StackSlot is null)
                                throw new InvalidOperationException("Stack-slot address has no stack slot.");
                            if (!_allocation.Frame.StackSlotOffsets.TryGetValue(address.StackSlot, out var offset))
                                throw new InvalidOperationException($"Missing stack slot offset for {address.StackSlot.Name}.");
                            return new AddressParts(MachineRegister.Invalid, MachineRegister.Invalid, checked(offset + address.Displacement), MemoryBase.StackPointer, 0);
                        }

                    case LirAddressKind.Indirect:
                        {
                            if (address.BaseOperand is null)
                                throw new InvalidOperationException("Indirect address has no base operand.");
                            var baseReg = LoadOperand(address.BaseOperand, scratchBase);
                            return new AddressParts(baseReg, MachineRegister.Invalid, address.Displacement, MemoryBase.Register, 0);
                        }

                    case LirAddressKind.Element:
                        {
                            if (address.BaseAddress is null)
                                throw new InvalidOperationException("Element address has no base address.");
                            var baseParts = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                            if (address.Index is null)
                                return baseParts.WithOffset(checked(baseParts.Offset + address.Displacement));

                            var scale = Math.Max(1, address.Scale);
                            if (baseParts.IndexRegister == MachineRegister.Invalid && IsPowerOfTwo(scale))
                            {
                                var simpleIndex = LoadOperand(address.Index, scratchIndex);
                                return baseParts.WithIndex(simpleIndex, Log2(scale));
                            }

                            MaterializeAddress(baseParts, scratchBase);
                            var indexReg = LoadOperand(address.Index, scratchIndex);
                            if (scale != 1)
                                EmitI64Imm(Op.I64MulImm, scratchIndex, indexReg, scale);
                            return new AddressParts(scratchBase, scratchIndex, address.Displacement, MemoryBase.Register, 0);
                        }

                    case LirAddressKind.Field:
                        {
                            if (address.BaseAddress is null)
                                throw new InvalidOperationException("Field address has no base address.");
                            var baseParts = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                            return baseParts.WithOffset(checked(baseParts.Offset + address.Displacement));
                        }

                    case LirAddressKind.Symbol:
                        throw new NotSupportedException("Mutable global/static C objects are not supported by the register-bytecode backend yet.");

                    default:
                        throw new NotSupportedException($"Unsupported LIR address kind {address.Kind}.");
                }
            }

            private void MaterializeAddress(LirAddress address, MachineRegister destination)
            {
                if (address.Kind == LirAddressKind.Symbol && address.Symbol is not null)
                {
                    if (TryMaterializeStaticSymbolAddress(address.Symbol, destination))
                        return;

                    if (address.Symbol is FunctionSymbol function && TryMaterializeFunctionPointer(function, destination))
                        return;
                }

                var parts = BuildAddress(address, destination == GpScratch0 ? GpScratch1 : GpScratch0, GpScratch2);
                MaterializeAddress(parts, destination);
            }

            private void MaterializeAddress(AddressParts parts, MachineRegister destination)
            {
                switch (parts.BaseKind)
                {
                    case MemoryBase.StackPointer:
                        EmitI64Imm(Op.I64AddImm, destination, Sp, parts.Offset);
                        break;

                    case MemoryBase.ThreadPointer:
                        throw new NotSupportedException("Materializing an incoming stack-argument address is not supported.");

                    case MemoryBase.Register:
                        if (parts.BaseRegister == MachineRegister.Invalid)
                            _asm.LiI64(destination, parts.Offset);
                        else if (parts.Offset == 0)
                            MoveRegister(destination, parts.BaseRegister);
                        else
                            EmitI64Imm(Op.I64AddImm, destination, parts.BaseRegister, parts.Offset);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported materialized memory base {parts.BaseKind}.");
                }

                if (parts.IndexRegister != MachineRegister.Invalid)
                {
                    if (parts.ScaleLog2 != 0)
                    {
                        EmitI64Imm(Op.I64ShlImm, GpScratch2, parts.IndexRegister, parts.ScaleLog2);
                        EmitRaw(Op.I64Add, destination, destination, GpScratch2);
                    }
                    else
                    {
                        EmitRaw(Op.I64Add, destination, destination, parts.IndexRegister);
                    }
                }
            }

            private void EmitConversion(MachineRegister dst, MachineRegister src, QualifiedType srcType, QualifiedType dstType, LirInstruction instruction)
            {
                if (SameType(srcType, dstType))
                {
                    MoveRegister(dst, src);
                    return;
                }

                if (IsPointerLike(srcType) || IsPointerLike(dstType))
                {
                    if (IsFloatType(srcType) || IsFloatType(dstType))
                        throw Unsupported(instruction, "Pointer/float conversion is not supported.");
                    MoveRegister(dst, src);
                    return;
                }

                if (IsFloatType(srcType) || IsFloatType(dstType))
                {
                    EmitFloatConversion(dst, src, srcType, dstType, instruction);
                    return;
                }

                var srcSize = SizeOf(srcType);
                var dstSize = SizeOf(dstType);
                if (dstSize >= srcSize)
                {
                    if (dstSize == 8 && srcSize <= 4)
                    {
                        EmitRaw(IsUnsignedInteger(srcType) ? Op.U32ToI64 : Op.I32ToI64, dst, src);
                        return;
                    }

                    MoveRegister(dst, src);
                    return;
                }

                if (dstSize == 4)
                {
                    EmitRaw(Op.I64ToI32, dst, src);
                    return;
                }

                if (dstSize == 2)
                {
                    EmitRaw(Op.TruncI32ToI16, dst, src);
                    return;
                }

                if (dstSize == 1)
                {
                    EmitRaw(Op.TruncI32ToI8, dst, src);
                    return;
                }

                throw Unsupported(instruction, $"Unsupported conversion from {srcType.ToDisplayString()} to {dstType.ToDisplayString()}.");
            }

            private void EmitFloatConversion(MachineRegister dst, MachineRegister src, QualifiedType srcType, QualifiedType dstType, LirInstruction instruction)
            {
                if (IsLongDouble(srcType) || IsLongDouble(dstType))
                    throw Unsupported(instruction, "long double conversions are not supported by the register-bytecode backend.");

                if (IsFloatType(srcType) && IsFloatType(dstType))
                {
                    if (IsFloat32(srcType) && IsFloat64(dstType))
                        EmitRaw(Op.F32ToF64, dst, src);
                    else if (IsFloat64(srcType) && IsFloat32(dstType))
                        EmitRaw(Op.F64ToF32, dst, src);
                    else
                        MoveRegister(dst, src);
                    return;
                }

                if (IsIntegerLike(srcType) && IsFloatType(dstType))
                {
                    if (Is64BitInteger(srcType))
                        EmitRaw(IsFloat32(dstType) ? Op.I64ToF32 : Op.I64ToF64, dst, src);
                    else
                        EmitRaw(IsUnsignedInteger(srcType) ? (IsFloat32(dstType) ? Op.U32ToF32 : Op.U32ToF64) : (IsFloat32(dstType) ? Op.I32ToF32 : Op.I32ToF64), dst, src);
                    return;
                }

                if (IsFloatType(srcType) && IsIntegerLike(dstType))
                {
                    if (IsFloat32(srcType))
                        EmitRaw(Is64BitInteger(dstType) ? Op.F32ToI64 : Op.F32ToI32, dst, src);
                    else
                        EmitRaw(Is64BitInteger(dstType) ? Op.F64ToI64 : Op.F64ToI32, dst, src);
                    return;
                }

                throw Unsupported(instruction, $"Unsupported floating conversion from {srcType.ToDisplayString()} to {dstType.ToDisplayString()}.");
            }

            private void EmitUnaryNeg(MachineRegister dst, MachineRegister src, QualifiedType type)
            {
                if (IsLongDouble(type))
                    throw new NotSupportedException("long double is not supported by the register-bytecode backend.");
                if (IsFloat32(type))
                    EmitRaw(Op.F32Neg, dst, src);
                else if (IsFloat64(type))
                    EmitRaw(Op.F64Neg, dst, src);
                else if (Is64BitInteger(type))
                    EmitRaw(Op.I64Neg, dst, src);
                else
                    EmitRaw(Op.I32Neg, dst, src);
            }

            private Op SelectBinaryOp(string text, QualifiedType leftType, QualifiedType rightType, QualifiedType resultType)
            {
                if (IsLongDouble(leftType) || IsLongDouble(rightType) || IsLongDouble(resultType))
                    throw new NotSupportedException("long double is not supported by the register-bytecode backend.");

                var float32 = IsFloat32(leftType) || IsFloat32(rightType) || IsFloat32(resultType);
                var float64 = IsFloat64(leftType) || IsFloat64(rightType) || IsFloat64(resultType);
                if (float32 || float64)
                    return SelectFloatBinaryOp(text, float32 && !float64);

                var is64 = Is64BitInteger(leftType) || Is64BitInteger(rightType) || (Is64BitInteger(resultType) && text is not "==" and not "!=" and not "<" and not "<=" and not ">" and not ">=");
                var unsigned = IsUnsignedInteger(leftType) || IsUnsignedInteger(rightType);

                return text switch
                {
                    "+" => is64 ? Op.I64Add : Op.I32Add,
                    "-" => is64 ? Op.I64Sub : Op.I32Sub,
                    "*" => is64 ? Op.I64Mul : Op.I32Mul,
                    "/" => is64 ? (unsigned ? Op.U64Div : Op.I64Div) : (unsigned ? Op.U32Div : Op.I32Div),
                    "%" => is64 ? (unsigned ? Op.U64Rem : Op.I64Rem) : (unsigned ? Op.U32Rem : Op.I32Rem),
                    "&" => is64 ? Op.I64And : Op.I32And,
                    "|" => is64 ? Op.I64Or : Op.I32Or,
                    "^" => is64 ? Op.I64Xor : Op.I32Xor,
                    "<<" => is64 ? Op.I64Shl : Op.I32Shl,
                    ">>" => is64 ? (unsigned ? Op.U64Shr : Op.I64Shr) : (unsigned ? Op.U32Shr : Op.I32Shr),
                    "==" => is64 ? Op.I64Eq : Op.I32Eq,
                    "!=" => is64 ? Op.I64Ne : Op.I32Ne,
                    "<" => is64 ? (unsigned ? Op.U64Lt : Op.I64Lt) : (unsigned ? Op.U32Lt : Op.I32Lt),
                    "<=" => is64 ? (unsigned ? Op.U64Le : Op.I64Le) : (unsigned ? Op.U32Le : Op.I32Le),
                    ">" => is64 ? (unsigned ? Op.U64Gt : Op.I64Gt) : (unsigned ? Op.U32Gt : Op.I32Gt),
                    ">=" => is64 ? (unsigned ? Op.U64Ge : Op.I64Ge) : (unsigned ? Op.U32Ge : Op.I32Ge),
                    "&&" => is64 ? Op.I64And : Op.I32And,
                    "||" => is64 ? Op.I64Or : Op.I32Or,
                    _ => throw new NotSupportedException($"Unsupported binary operator '{text}'."),
                };
            }

            private static Op SelectFloatBinaryOp(string text, bool f32)
            {
                return text switch
                {
                    "+" => f32 ? Op.F32Add : Op.F64Add,
                    "-" => f32 ? Op.F32Sub : Op.F64Sub,
                    "*" => f32 ? Op.F32Mul : Op.F64Mul,
                    "/" => f32 ? Op.F32Div : Op.F64Div,
                    "%" => f32 ? Op.F32Rem : Op.F64Rem,
                    "==" => f32 ? Op.F32Eq : Op.F64Eq,
                    "!=" => f32 ? Op.F32Ne : Op.F64Ne,
                    "<" => f32 ? Op.F32Lt : Op.F64Lt,
                    "<=" => f32 ? Op.F32Le : Op.F64Le,
                    ">" => f32 ? Op.F32Gt : Op.F64Gt,
                    ">=" => f32 ? Op.F32Ge : Op.F64Ge,
                    _ => throw new NotSupportedException($"Unsupported floating binary operator '{text}'."),
                };
            }

            private Op LoadOpForType(QualifiedType type)
            {
                var kind = ClassifyValue(type);
                return kind switch
                {
                    ValueKind.Float32 => Op.LdF32,
                    ValueKind.Float64 => Op.LdF64,
                    ValueKind.Pointer => Op.LdPtr,
                    ValueKind.General32 or ValueKind.General64 => LoadIntegerOp(type),
                    _ => throw new NotSupportedException($"Cannot load non-scalar type {type.ToDisplayString()}."),
                };
            }

            private Op LoadIntegerOp(QualifiedType type)
            {
                var signed = !IsUnsignedInteger(type);
                return SizeOf(type) switch
                {
                    1 => signed ? Op.LdI1 : Op.LdU1,
                    2 => signed ? Op.LdI2 : Op.LdU2,
                    4 => signed ? Op.LdI4 : Op.LdU4,
                    8 => Op.LdI8,
                    _ => throw new NotSupportedException($"Cannot load integer type {type.ToDisplayString()}."),
                };
            }

            private Op StoreOpForType(QualifiedType type)
            {
                var kind = ClassifyValue(type);
                return kind switch
                {
                    ValueKind.Float32 => Op.StF32,
                    ValueKind.Float64 => Op.StF64,
                    ValueKind.Pointer => Op.StPtr,
                    ValueKind.General32 or ValueKind.General64 => StoreIntegerOp(type),
                    _ => throw new NotSupportedException($"Cannot store non-scalar type {type.ToDisplayString()}."),
                };
            }

            private Op StoreIntegerOp(QualifiedType type)
            {
                return SizeOf(type) switch
                {
                    1 => Op.StI1,
                    2 => Op.StI2,
                    4 => Op.StI4,
                    8 => Op.StI8,
                    _ => throw new NotSupportedException($"Cannot store integer type {type.ToDisplayString()}."),
                };
            }

            private Op CallOpForReturn(QualifiedType returnType, bool isInternal)
            {
                if (IsAggregateType(returnType))
                {
                    var abi = CAbi.ClassifyValue(_owner._target, returnType, isReturn: true);
                    if (abi.PassingKind == AbiPassingKind.Indirect)
                        return isInternal ? Op.CallInternalVoid : Op.CallVoid;
                    return isInternal ? Op.CallInternalValue : Op.CallValue;
                }

                return ClassifyValue(returnType) switch
                {
                    ValueKind.Void => isInternal ? Op.CallInternalVoid : Op.CallVoid,
                    ValueKind.Float32 or ValueKind.Float64 => isInternal ? Op.CallInternalF : Op.CallF,
                    ValueKind.General32 or ValueKind.General64 or ValueKind.Pointer => isInternal ? Op.CallInternalI : Op.CallI,
                    _ => throw new NotSupportedException("Aggregate return values are not supported by this backend yet."),
                };
            }

            private Op IndirectCallOpForReturn(QualifiedType returnType)
            {
                if (IsAggregateType(returnType))
                {
                    var abi = CAbi.ClassifyValue(_owner._target, returnType, isReturn: true);
                    return abi.PassingKind == AbiPassingKind.Indirect ? Op.CallIndirectVoid : Op.CallIndirectValue;
                }

                return ClassifyValue(returnType) switch
                {
                    ValueKind.Void => Op.CallIndirectVoid,
                    ValueKind.Float32 or ValueKind.Float64 => Op.CallIndirectF,
                    ValueKind.General32 or ValueKind.General64 or ValueKind.Pointer => Op.CallIndirectI,
                    _ => throw new NotSupportedException("Aggregate return values are not supported by this backend yet."),
                };
            }

            private void EmitMem(Op op, MachineRegister valueOrDestination, MachineRegister baseRegister, int offset, MachineRegister indexRegister, MemoryBase memoryBase, int alignment, int scaleLog2 = 0)
            {
                var aux = Aux.Memory((byte)Math.Max(0, scaleLog2), (byte)Log2PowerOfTwoOrZero(Math.Max(1, alignment)), memoryBase, MemoryFlags.None);
                _asm.Emit(InstrDesc.Mem(op, valueOrDestination, NormalizeMemoryBaseRegister(memoryBase, baseRegister), offset, indexRegister, aux));
            }

            private static MachineRegister NormalizeMemoryBaseRegister(MemoryBase memoryBase, MachineRegister baseRegister)
            {
                if (baseRegister != MachineRegister.Invalid)
                    return baseRegister;

                return memoryBase switch
                {
                    MemoryBase.StackPointer => MachineRegisters.StackPointer,
                    MemoryBase.FramePointer => MachineRegisters.FramePointer,
                    MemoryBase.ThreadPointer => MachineRegisters.ThreadPointer,
                    MemoryBase.GlobalPointer => MachineRegisters.GlobalPointer,
                    _ => MachineRegister.Invalid,
                };
            }

            private void EmitRaw(Op op, MachineRegister rd, MachineRegister rs1, MachineRegister rs2 = MachineRegister.Invalid, bool mayThrow = false, long imm = 0)
            {
                var aux = mayThrow ? Aux.Instruction(InstructionFlags.MayThrow) : (ushort)0;
                _asm.Emit(new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs1), RegisterVmIsa.EncodeRegister(rs2), aux: aux, imm: imm));
            }

            private void EmitManagedDirectCall(Op op, int methodId, CallFlags flags)
                => _asm.CallDirect(op, _owner.GetMethodEntryLabel(methodId), flags);

            private void EmitInternalCall(Op op, int methodId, CallFlags flags)
                => _asm.Emit(InstrDesc.Call(op, methodId, flags | CallFlags.InternalCall));

            private void EmitRawIndirectCall(Op op, MachineRegister targetMethodId, CallFlags flags)
                => _asm.Emit(new InstrDesc(op, rs1: RegisterVmIsa.EncodeRegister(targetMethodId), aux: Aux.Call(flags)));

            private void EmitI32Imm(Op op, MachineRegister rd, MachineRegister rs1, int imm)
                => _asm.Emit(InstrDesc.I(op, rd, rs1, imm));

            private void EmitI64Imm(Op op, MachineRegister rd, MachineRegister rs1, long imm)
                => _asm.Emit(InstrDesc.I(op, rd, rs1, imm));

            private void MoveRegister(MachineRegister destination, MachineRegister source)
            {
                if (destination == source)
                    return;

                var destinationIsFloat = IsFloatRegister(destination);
                var sourceIsFloat = IsFloatRegister(source);
                if (destinationIsFloat != sourceIsFloat)
                    throw new InvalidOperationException("Cannot move between integer and floating-point registers without an explicit conversion.");

                if (destinationIsFloat)
                    _asm.MovF(destination, source);
                else if (IsPointerRegisterUse(destination, source))
                    _asm.MovPtr(destination, source);
                else
                    _asm.MovI(destination, source);
            }

            private static bool IsPointerRegisterUse(MachineRegister destination, MachineRegister source)
                => false;

            private void EmitLiFloat(MachineRegister destination, double value, QualifiedType type)
            {
                if (!IsFloatRegister(destination))
                    throw new InvalidOperationException("Floating-point immediate destination must be an FPR.");

                var scratchOffset = _allocation.Frame.FloatingImmediateTempOffset;
                if (scratchOffset < 0)
                    throw new InvalidOperationException("Floating-point immediate scratch slot was not reserved.");

                if (IsFloat32(type))
                {
                    _asm.LiI32(GpScratch2, BitConverter.SingleToInt32Bits((float)value));
                    EmitMem(Op.StI4, GpScratch2, MachineRegister.Invalid, scratchOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 4);
                    EmitMem(Op.LdF32, destination, MachineRegister.Invalid, scratchOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 4);
                }
                else
                {
                    _asm.LiI64(GpScratch2, BitConverter.DoubleToInt64Bits(value));
                    EmitMem(Op.StI8, GpScratch2, MachineRegister.Invalid, scratchOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                    EmitMem(Op.LdF64, destination, MachineRegister.Invalid, scratchOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                }
            }

            private bool IsFallthroughTarget(LirBlock? target)
            {
                if (target is null || _currentBlock is null)
                    return false;

                return _nextBlocks.TryGetValue(_currentBlock, out var next) && ReferenceEquals(next, target);
            }

            private Label LabelOf(LirBlock? block)
            {
                if (block is null || !_labels.TryGetValue(block, out var label))
                    throw new InvalidOperationException("Missing bytecode label for LIR block.");
                return label;
            }

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException($"{message} LIR ordinal={instruction.Ordinal.ToString(CultureInfo.InvariantCulture)}.");

            private int SizeOf(QualifiedType type)
                => Math.Max(0, _owner._target.SizeOf(type));

            private int AlignmentOf(QualifiedType type)
                => Math.Max(1, Math.Min(_owner._target.AlignOf(type), 8));

            private int PointerScale(QualifiedType pointerType)
            {
                if (pointerType.Type is PointerType pointer)
                    return Math.Max(1, SizeOf(pointer.PointeeType));
                return 1;
            }

            private static bool SameType(QualifiedType left, QualifiedType right)
                => string.Equals(left.ToDisplayString(), right.ToDisplayString(), StringComparison.Ordinal);

            private static bool IsVoid(QualifiedType type)
                => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

            private static bool IsPointerLike(QualifiedType type)
                => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

            private static bool IsAggregateType(QualifiedType type)
                => CAbi.IsAggregate(type);

            private static bool IsIntegerLike(QualifiedType type)
                => (type.Type.Kind is TypeKind.Builtin or TypeKind.Enum) && !IsFloatType(type) && !IsVoid(type);

            private static bool IsFloatType(QualifiedType type)
                => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble };

            private static bool IsFloat32(QualifiedType type)
                => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float };

            private static bool IsFloat64(QualifiedType type)
                => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Double };

            private static bool IsLongDouble(QualifiedType type)
                => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.LongDouble };

            private bool Is64BitInteger(QualifiedType type)
            {
                if (IsPointerLike(type))
                    return true;
                return SizeOf(type) > 4;
            }

            private static bool IsUnsignedInteger(QualifiedType type)
            {
                if (type.Type is not BuiltinType builtin)
                    return false;

                return builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.UnsignedChar or BuiltinTypeKind.UnsignedShort
                    or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;
            }

            private ValueKind ClassifyValue(QualifiedType type)
            {
                if (IsVoid(type))
                    return ValueKind.Void;
                if (IsFloat32(type))
                    return ValueKind.Float32;
                if (IsFloat64(type) || IsLongDouble(type))
                    return ValueKind.Float64;
                if (IsPointerLike(type))
                    return ValueKind.Pointer;
                if (IsIntegerLike(type))
                    return SizeOf(type) <= 4 ? ValueKind.General32 : ValueKind.General64;
                return ValueKind.UnsupportedAggregate;
            }

            private static AbiRegisterClass RegisterClassOf(ValueKind kind)
            {
                return kind switch
                {
                    ValueKind.Float32 or ValueKind.Float64 => AbiRegisterClass.Floating,
                    ValueKind.General32 or ValueKind.General64 or ValueKind.Pointer => AbiRegisterClass.General,
                    _ => throw new NotSupportedException($"Value kind has no ABI register class: {kind}."),
                };
            }

            private static bool IsFloatingValue(ValueKind kind)
                => kind is ValueKind.Float32 or ValueKind.Float64;

            private static bool IsFloatRegister(MachineRegister register)
                => RegisterVmIsa.IsFloatRegister((byte)register);

            private static bool IsPowerOfTwo(int value)
                => value > 0 && (value & (value - 1)) == 0;

            private static int Log2(int value)
            {
                var result = 0;
                while (value > 1)
                {
                    value >>= 1;
                    result++;
                }
                return result;
            }

            private static int Log2PowerOfTwoOrZero(int value)
                => IsPowerOfTwo(value) ? Log2(value) : 0;

            private static bool MayThrow(Op op)
                => op is Op.I32Div or Op.I32Rem or Op.U32Div or Op.U32Rem or Op.I64Div or Op.I64Rem or Op.U64Div or Op.U64Rem
                    or Op.F32Div or Op.F32Rem or Op.F64Div or Op.F64Rem;

            private static int ImmediateToInt32(LirOperand operand)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new NotSupportedException("Expected an immediate integer operand.");
                return unchecked((int)ImmediateObjectToInt64(operand.Immediate));
            }

            private static long ImmediateToInt64(LirOperand operand)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new NotSupportedException("Expected an immediate integer operand.");
                return ImmediateObjectToInt64(operand.Immediate);
            }

            private static long ImmediateObjectToInt64(object? value)
            {
                return value switch
                {
                    null => 0,
                    bool b => b ? 1 : 0,
                    char c => c,
                    byte b => b,
                    sbyte sb => sb,
                    short s => s,
                    ushort us => us,
                    int i => i,
                    uint ui => ui,
                    long l => l,
                    ulong ul => unchecked((long)ul),
                    _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
                };
            }

            private readonly struct AddressParts
            {
                public MachineRegister BaseRegister { get; }
                public MachineRegister IndexRegister { get; }
                public int Offset { get; }
                public MemoryBase BaseKind { get; }
                public int ScaleLog2 { get; }

                public AddressParts(MachineRegister baseRegister, MachineRegister indexRegister, int offset, MemoryBase baseKind, int scaleLog2)
                {
                    BaseRegister = baseRegister;
                    IndexRegister = indexRegister;
                    Offset = offset;
                    BaseKind = baseKind;
                    ScaleLog2 = scaleLog2;
                }

                public AddressParts WithOffset(int offset)
                    => new AddressParts(BaseRegister, IndexRegister, offset, BaseKind, ScaleLog2);

                public AddressParts WithIndex(MachineRegister indexRegister, int scaleLog2)
                    => new AddressParts(BaseRegister, indexRegister, Offset, BaseKind, scaleLog2);
            }

            private sealed class ParameterState
            {
                public int Integer;
                public int Float;
                public int Stack;
            }

            private enum ValueKind
            {
                Void,
                General32,
                General64,
                Pointer,
                Float32,
                Float64,
                UnsupportedAggregate,
            }

        }
    }
}
