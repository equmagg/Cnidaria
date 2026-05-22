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
        public int EntryMethodId { get; }
        public int PrintfMethodId { get; }
        public IReadOnlyDictionary<FunctionSymbol, int> MethodIds { get; }
        public IReadOnlyDictionary<int, FunctionType?> SignaturesByMethodId { get; }

        public RegisterBytecodeProgram(
            CodeImage image,
            int entryMethodId,
            int printfMethodId,
            IReadOnlyDictionary<FunctionSymbol, int> methodIds,
            IReadOnlyDictionary<int, FunctionType?> signaturesByMethodId)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            EntryMethodId = entryMethodId;
            PrintfMethodId = printfMethodId;
            MethodIds = methodIds ?? throw new ArgumentNullException(nameof(methodIds));
            SignaturesByMethodId = signaturesByMethodId ?? throw new ArgumentNullException(nameof(signaturesByMethodId));
        }
        public RegisterBytecodeSyntheticRuntime CreateSyntheticRuntime()
        {
            var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);
            var stdModule = new RuntimeModule(
                name: "std",
                md: new MinimalCRuntimeMetadataView(),
                methodsByDefToken: new Dictionary<int, BytecodeFunction>());

            modules.Add(stdModule.Name, stdModule);

            var runtimeTypes = new RuntimeTypeSystem(modules);
            var entryMethod = RegisterSyntheticRuntimeMethods(runtimeTypes);
            return new RegisterBytecodeSyntheticRuntime(runtimeTypes, modules, entryMethod);
        }
        internal RuntimeMethod RegisterSyntheticRuntimeMethods(RuntimeTypeSystem runtimeTypes)
        {
            if (runtimeTypes is null)
                throw new ArgumentNullException(nameof(runtimeTypes));

            var cProgramType = runtimeTypes.RegisterSyntheticType("c", "__c", "Program", RuntimeTypeKind.Class);
            foreach (var pair in SignaturesByMethodId.OrderBy(static p => p.Key))
            {
                if (pair.Key == PrintfMethodId)
                    continue;

                if (TryGetMethod(runtimeTypes, pair.Key, out _))
                    continue;

                var signature = pair.Value;
                var returnType = signature is null
                    ? GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Int)
                    : MapRuntimeType(runtimeTypes, signature.ReturnType);
                var parameters = signature is null
                    ? Array.Empty<RuntimeType>()
                    : signature.Parameters.Select(p => MapRuntimeType(runtimeTypes, p.Type)).ToArray();

                var name = MethodIds.FirstOrDefault(m => m.Value == pair.Key).Key?.Name ?? ("fn_" + pair.Key.ToString(CultureInfo.InvariantCulture));
                runtimeTypes.RegisterSyntheticStaticMethod(cProgramType, name, returnType, parameters, implFlags: 0, methodId: pair.Key);
            }

            RegisterCStringWriteInternalCall(runtimeTypes, PrintfMethodId);
            return runtimeTypes.GetMethodById(EntryMethodId);
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

        private static RuntimeType MapRuntimeType(RuntimeTypeSystem runtimeTypes, QualifiedType type)
        {
            if (type.Type is BuiltinType builtin)
                return GetRuntimePrimitive(runtimeTypes, builtin.BuiltinKind);

            if (type.Type is PointerType pointer)
                return runtimeTypes.GetPointerType(MapRuntimeType(runtimeTypes, pointer.PointeeType));

            if (type.Type is ArrayType array)
                return runtimeTypes.GetPointerType(MapRuntimeType(runtimeTypes, array.ElementType));

            if (type.Type is FunctionType)
                return runtimeTypes.GetPointerType(GetRuntimePrimitive(runtimeTypes, BuiltinTypeKind.Void));

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
        private readonly Assembler _assembler = new Assembler();
        private readonly Dictionary<FunctionSymbol, int> _methodIds = new Dictionary<FunctionSymbol, int>();
        private readonly Dictionary<string, int> _methodIdsByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, FunctionType?> _signatures = new Dictionary<int, FunctionType?>();

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
            if (_target.PointerSize != TargetArchitecture.PointerSize)
                throw new NotSupportedException("Register-bytecode C backend currently supports only the VM native pointer size (" + TargetArchitecture.PointerSize.ToString(CultureInfo.InvariantCulture) + " bytes).");

            IndexMethods();

            foreach (var function in _module.Functions)
                EmitFunction(function);

            var entry = ResolveEntryMethodId();
            var flags = ImageFlags.LittleEndian;
            if (_target.PointerSize == 4)
                flags |= ImageFlags.Target32;
            var image = _assembler.Build(flags, validate: true);
            return new RegisterBytecodeProgram(image, entry, PrintfMethodId, _methodIds, _signatures);
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

        private void EmitFunction(LirFunction function)
        {
            if (function.Symbol is null || !_methodIds.TryGetValue(function.Symbol, out var methodId))
                throw new NotSupportedException("Cannot emit anonymous C functions to register bytecode.");

            var allocation = LinearScanRegisterAllocator.Allocate(function, _target, _allocationOptions);
            var labels = new Dictionary<LirBlock, Label>();
            foreach (var block in function.Blocks)
                labels.Add(block, _assembler.CreateLabel());

            var context = new FunctionEmissionContext(this, function, allocation, labels);
            _assembler.BeginMethod(methodId, ToStackFrameLayout(allocation.Frame));
            context.EmitPrologue();
            context.EmitBlocks();
            context.EmitImplicitFallthroughTrap();
            _assembler.EndMethod();
        }

        private static StackFrameLayout ToStackFrameLayout(StackFrameMap frame)
        {
            var empty = ImmutableArray<StackFrameSlot>.Empty;
            return new StackFrameLayout(
                frameSize: frame.FrameSize,
                frameAlignment: frame.FrameAlignment,
                calleeSaveAreaOffset: frame.SavedRegisterAreaOffset,
                calleeSaveAreaSize: frame.SavedRegisterAreaSize,
                argumentHomeAreaOffset: 0,
                argumentHomeAreaSize: 0,
                localAreaOffset: frame.StackSlotAreaOffset,
                localAreaSize: frame.StackSlotAreaSize,
                tempAreaOffset: frame.ParallelCopyTempOffset,
                tempAreaSize: frame.ParallelCopyTempSize,
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

        private sealed class FunctionEmissionContext
        {
            private readonly RegisterBytecodeCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly IReadOnlyDictionary<LirBlock, Label> _labels;
            private readonly Assembler _asm;
            private readonly ParameterState _parameters = new ParameterState();

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
            }

            public void EmitPrologue()
            {
                var frame = _allocation.Frame;
                if (frame.FrameSize != 0)
                    EmitI64Imm(Op.I64SubImm, Sp, Sp, frame.FrameSize);

                if (frame.HasVarArgsPointer)
                    EmitMem(Op.StPtr, VarArgsRegister, MachineRegister.Invalid, frame.VarArgsPointerOffset, 
                        MachineRegister.Invalid, MemoryBase.StackPointer, _owner._target.PointerAlignment);

                foreach (var pair in frame.SavedRegisterOffsets.OrderBy(static p => p.Value))
                {
                    var op = IsFloatRegister(pair.Key) ? Op.StF64 : Op.StI8;
                    EmitMem(op, pair.Key, MachineRegister.Invalid, pair.Value, MachineRegister.Invalid, MemoryBase.StackPointer, alignment: 8);
                }
            }

            public void EmitBlocks()
            {
                foreach (var block in _function.Blocks)
                {
                    _asm.Bind(_labels[block]);
                    foreach (var instruction in block.Instructions)
                        EmitInstruction(instruction);
                }
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
                        throw Unsupported(instruction, "Unsupported LIR instruction kind: " + instruction.Kind + ".");
                }
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var type = instruction.Result.Type;
                var cls = ClassifyArgument(type);
                if (cls == AbiRegisterClass.Floating)
                {
                    if (_parameters.Float < 8)
                    {
                        MoveRegister(destination, (MachineRegister)((int)MachineRegister.F10 + _parameters.Float));
                        _parameters.Float++;
                    }
                    else
                    {
                        EmitMem(LoadOpForType(type), destination, MachineRegister.Invalid, checked(_parameters.Stack * 8), MachineRegister.Invalid, MemoryBase.ThreadPointer, AlignmentOf(type));
                        _parameters.Stack++;
                    }
                }
                else
                {
                    if (_parameters.Integer < 8)
                    {
                        MoveRegister(destination, (MachineRegister)((int)MachineRegister.X10 + _parameters.Integer));
                        _parameters.Integer++;
                    }
                    else
                    {
                        EmitMem(LoadOpForType(type), destination, MachineRegister.Invalid, checked(_parameters.Stack * 8), MachineRegister.Invalid, MemoryBase.ThreadPointer, AlignmentOf(type));
                        _parameters.Stack++;
                    }
                }

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitCopyLike(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Copy-like instruction has no source operand.");

                var destination = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                LoadOperandInto(instruction.Operands[0], destination);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

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
                    throw Unsupported(instruction, "Unsupported unary operator '" + op + "'.");
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
                var usesFloatingOperands = IsFloatType(leftType) || IsFloatType(rightType);
                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var left = LoadOperand(instruction.Operands[0], usesFloatingOperands ? FpScratch1 : GpScratch1);
                var right = LoadOperand(instruction.Operands[1], usesFloatingOperands ? FpScratch2 : GpScratch2);
                var op = SelectBinaryOp(instruction.Operator, leftType, rightType, resultType);
                EmitRaw(op, dst, left, right, MayThrow(op));
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
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
                var src = LoadOperand(instruction.Operands[0], IsFloatType(instruction.Operands[0].Type) ? FpScratch0 : GpScratch0);
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

                    MarshalCallArguments(instruction, startOperand: 1);
                    PrepareVariadicCall(instruction);
                    var directCallOp = CallOpForReturn(instruction.Result?.Type ?? TypeCatalog.Instance.Builtin(BuiltinTypeKind.Void), isInternal: false);
                    EmitRawCall(directCallOp, methodId, CallFlags.None);
                    EmitCallResult(instruction);
                    return;
                }

                MarshalCallArguments(instruction, startOperand: 1);
                PrepareVariadicCall(instruction);
                var target = LoadOperand(callee, GpScratch0);
                var indirectCallOp = IndirectCallOpForReturn(instruction.Result?.Type ?? TypeCatalog.Instance.Builtin(BuiltinTypeKind.Void));
                EmitRawIndirectCall(indirectCallOp, target, CallFlags.None);
                EmitCallResult(instruction);
            }

            private void EmitCallResult(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var dst = GetWritableRegister(instruction.Result, GpScratch0, FpScratch0);
                var ret = ClassifyArgument(instruction.Result.Type) == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10;
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

                EmitRawCall(Op.CallInternalVoid, methodId, CallFlags.InternalCall);

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
                    var source = LoadOperand(operand, IsFloatType(operand.Type) ? FpScratch0 : GpScratch0);
                    EmitMem(StoreOpForType(operand.Type), source, MachineRegister.Invalid, 
                        checked(baseOffset + i * _owner._allocationOptions.StackArgumentSlotSize), MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                }

                EmitI64Imm(Op.I64AddImm, VarArgsRegister, Sp, baseOffset);
            }
            private int CountStackArgumentSlots(LirInstruction instruction, int startOperand)
            {
                var integer = 0;
                var floating = 0;
                var stack = 0;

                for (var i = startOperand; i < instruction.Operands.Length; i++)
                {
                    var operand = instruction.Operands[i];
                    var cls = ClassifyArgument(operand.Type);
                    if (cls == AbiRegisterClass.Floating)
                    {
                        if (floating++ >= 8)
                            stack++;
                    }
                    else
                    {
                        if (integer++ >= 8)
                            stack++;
                    }
                }

                return stack;
            }
            private void MarshalCallArguments(LirInstruction instruction, int startOperand)
            {
                var integer = 0;
                var floating = 0;
                var stack = 0;

                for (var i = startOperand; i < instruction.Operands.Length; i++)
                {
                    var operand = instruction.Operands[i];
                    var cls = ClassifyArgument(operand.Type);
                    if (cls == AbiRegisterClass.Floating)
                    {
                        if (floating < 8)
                        {
                            var target = (MachineRegister)((int)MachineRegister.F10 + floating++);
                            LoadOperandInto(operand, target);
                        }
                        else
                        {
                            var src = LoadOperand(operand, FpScratch0);
                            EmitMem(StoreOpForType(operand.Type), src, MachineRegister.Invalid,
                                checked(_allocation.Frame.OutgoingArgumentAreaOffset + stack * 8),
                                MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                            stack++;
                        }
                    }
                    else
                    {
                        if (integer < 8)
                        {
                            var target = (MachineRegister)((int)MachineRegister.X10 + integer++);
                            LoadOperandInto(operand, target);
                        }
                        else
                        {
                            var src = LoadOperand(operand, GpScratch0);
                            EmitMem(StoreOpForType(operand.Type), src, MachineRegister.Invalid,
                                checked(_allocation.Frame.OutgoingArgumentAreaOffset + stack * 8),
                                MachineRegister.Invalid, MemoryBase.StackPointer, AlignmentOf(operand.Type));
                            stack++;
                        }
                    }
                }
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand.");

                var type = instruction.Operands[0].Type;
                if (IsFloatType(type))
                {
                    if (IsLongDouble(type))
                        throw Unsupported(instruction, "long double branch conditions are not supported by the register-bytecode backend.");

                    var condf = LoadOperand(instruction.Operands[0], FpScratch0);
                    EmitLiFloat(FpScratch1, 0.0, type);
                    _asm.Branch(IsFloat32(type) ? Op.BrF32Ne : Op.BrF64Ne, condf, FpScratch1, LabelOf(instruction.TrueTarget));
                    _asm.J(LabelOf(instruction.FalseTarget));
                    return;
                }

                var cond = LoadOperand(instruction.Operands[0], GpScratch0);
                _asm.Branch(Is64BitInteger(type) || IsPointerLike(type) ? Op.BrTrueI64 : Op.BrTrueI32, cond, LabelOf(instruction.TrueTarget));
                _asm.J(LabelOf(instruction.FalseTarget));
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

                _asm.J(LabelOf(instruction.Target));
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 0)
                {
                    var operand = instruction.Operands[0];
                    if (ClassifyArgument(operand.Type) == AbiRegisterClass.Floating)
                        LoadOperandInto(operand, MachineRegister.F10);
                    else
                        LoadOperandInto(operand, MachineRegister.X10);
                }

                EmitEpilogue();

                if (instruction.Operands.Length == 0 || IsVoid(instruction.Operands[0].Type))
                {
                    _asm.RetVoid();
                }
                else if (ClassifyArgument(instruction.Operands[0].Type) == AbiRegisterClass.Floating)
                {
                    _asm.RetF(MachineRegister.F10);
                }
                else
                {
                    _asm.RetI(MachineRegister.X10);
                }
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

                var physicalCopies = ImmutableArray.CreateBuilder<LirParallelCopy>(instruction.ParallelCopies.Length);
                foreach (var copy in instruction.ParallelCopies)
                {
                    if (RequiresPhysicalParallelCopy(copy))
                        physicalCopies.Add(copy);
                }
                if (physicalCopies.Count == 0)
                    return;
                if (_allocation.Frame.ParallelCopyTempSize < physicalCopies.Count * 8)
                    throw Unsupported(instruction, "Parallel-copy temporary area was not reserved.");

                for (var i = 0; i < physicalCopies.Count; i++)
                {
                    var copy = physicalCopies[i];
                    var isFloat = IsFloatType(copy.Destination.Type);
                    var src = LoadOperand(copy.Source, isFloat ? FpScratch0 : GpScratch0);
                    EmitMem(isFloat ? Op.StF64 : Op.StI8, src, MachineRegister.Invalid,
                        checked(_allocation.Frame.ParallelCopyTempOffset + i * 8),
                        MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                }

                for (var i = 0; i < physicalCopies.Count; i++)
                {
                    var copy = physicalCopies[i];
                    var isFloat = IsFloatType(copy.Destination.Type);
                    var dst = GetWritableRegister(copy.Destination, GpScratch0, FpScratch0);
                    EmitMem(isFloat ? Op.LdF64 : Op.LdI8, dst, MachineRegister.Invalid,
                        checked(_allocation.Frame.ParallelCopyTempOffset + i * 8),
                        MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                    StoreWritableRegisterIfSpilled(copy.Destination, dst);
                }
            }
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
                       TryGetRuntimeMethodId(function, out methodId, out isPrintf);
            }

            private bool TryGetRuntimeMethodId(FunctionSymbol function, out int methodId, out bool isPrintf)
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
                if (!TryGetRuntimeMethodId(function, out var methodId, out _))
                    return false;

                if (methodId >= int.MinValue && methodId <= int.MaxValue)
                    _asm.LiI32(destination, methodId);
                else
                    _asm.LiI64(destination, methodId);
                return true;
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
                            throw new InvalidOperationException("Missing stack slot offset for " + operand.StackSlot.Name + ".");
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
                            throw new NotSupportedException("Cannot materialize external function pointer '" + function.Name + "' because it has no register-bytecode method id.");
                        }
                        throw new NotSupportedException("Cannot materialize symbol '" + operand.Symbol.Name + "' as static data.");

                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (IsFloatType(operand.Type))
                            EmitLiFloat(preferred, 0.0, operand.Type);
                        else
                            _asm.LiI64(preferred, 0);
                        return preferred;

                    default:
                        throw new NotSupportedException("Cannot load LIR operand kind " + operand.Kind + " into a register.");
                }
            }

            private MachineRegister LoadVirtualRegister(LirVirtualRegister register, MachineRegister preferred)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;

                EmitMem(IsFloatType(register.Type) ? Op.LdF64 : Op.LdI8, preferred, MachineRegister.Invalid,
                    alloc.StackOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
                return preferred;
            }

            private MachineRegister GetWritableRegister(LirVirtualRegister register, MachineRegister generalScratch, MachineRegister floatScratch)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;

                return IsFloatType(register.Type) ? floatScratch : generalScratch;
            }

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, MachineRegister source)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return;

                EmitMem(IsFloatType(register.Type) ? Op.StF64 : Op.StI8, source, MachineRegister.Invalid,
                    alloc.StackOffset, MachineRegister.Invalid, MemoryBase.StackPointer, 8);
            }

            private void EmitImmediate(MachineRegister destination, object? value, QualifiedType type)
            {
                if (value is null)
                {
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
                    if (value is float f)
                        _asm.LiF32Bits(destination, BitConverter.SingleToInt32Bits(f));
                    else if (value is double d)
                        _asm.LiF64Bits(destination, BitConverter.DoubleToInt64Bits(d));
                    else
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
                                throw new InvalidOperationException("Missing stack slot offset for " + address.StackSlot.Name + ".");
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
                        throw new NotSupportedException("Unsupported LIR address kind " + address.Kind + ".");
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
                        throw new NotSupportedException("Unsupported materialized memory base " + parts.BaseKind + ".");
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

                throw Unsupported(instruction, "Unsupported conversion from " + srcType.ToDisplayString() + " to " + dstType.ToDisplayString() + ".");
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

                throw Unsupported(instruction, "Unsupported floating conversion from " + srcType.ToDisplayString() + " to " + dstType.ToDisplayString() + ".");
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
                    _ => throw new NotSupportedException("Unsupported binary operator '" + text + "'."),
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
                    _ => throw new NotSupportedException("Unsupported floating binary operator '" + text + "'."),
                };
            }

            private Op LoadOpForType(QualifiedType type)
            {
                if (IsFloat32(type)) return Op.LdF32;
                if (IsFloat64(type)) return Op.LdF64;
                if (IsPointerLike(type)) return Op.LdPtr;
                var signed = !IsUnsignedInteger(type);
                return SizeOf(type) switch
                {
                    1 => signed ? Op.LdI1 : Op.LdU1,
                    2 => signed ? Op.LdI2 : Op.LdU2,
                    4 => signed ? Op.LdI4 : Op.LdU4,
                    8 => Op.LdI8,
                    _ => throw new NotSupportedException("Cannot load non-scalar type " + type.ToDisplayString() + "."),
                };
            }

            private Op StoreOpForType(QualifiedType type)
            {
                if (IsFloat32(type)) return Op.StF32;
                if (IsFloat64(type)) return Op.StF64;
                if (IsPointerLike(type)) return Op.StPtr;
                return SizeOf(type) switch
                {
                    1 => Op.StI1,
                    2 => Op.StI2,
                    4 => Op.StI4,
                    8 => Op.StI8,
                    _ => throw new NotSupportedException("Cannot store non-scalar type " + type.ToDisplayString() + "."),
                };
            }

            private Op CallOpForReturn(QualifiedType returnType, bool isInternal)
            {
                if (IsVoid(returnType)) return isInternal ? Op.CallInternalVoid : Op.CallVoid;
                if (IsFloatType(returnType)) return isInternal ? Op.CallInternalF : Op.CallF;
                if (IsIntegerLike(returnType) || IsPointerLike(returnType)) return isInternal ? Op.CallInternalI : Op.CallI;
                throw new NotSupportedException("Aggregate return values are not supported by this backend yet.");
            }

            private Op IndirectCallOpForReturn(QualifiedType returnType)
            {
                if (IsVoid(returnType)) return Op.CallIndirectVoid;
                if (IsFloatType(returnType)) return Op.CallIndirectF;
                if (IsIntegerLike(returnType) || IsPointerLike(returnType)) return Op.CallIndirectI;
                throw new NotSupportedException("Aggregate return values are not supported by this backend yet.");
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

            private void EmitRawCall(Op op, int methodId, CallFlags flags)
                => _asm.Emit(InstrDesc.Call(op, methodId, flags));

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
                if (IsFloat32(type))
                    _asm.LiF32Bits(destination, BitConverter.SingleToInt32Bits((float)value));
                else
                    _asm.LiF64Bits(destination, BitConverter.DoubleToInt64Bits(value));
            }

            private Label LabelOf(LirBlock? block)
            {
                if (block is null || !_labels.TryGetValue(block, out var label))
                    throw new InvalidOperationException("Missing bytecode label for LIR block.");
                return label;
            }

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException(message + " LIR ordinal=" + instruction.Ordinal.ToString(CultureInfo.InvariantCulture) + ".");

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

            private static AbiRegisterClass ClassifyArgument(QualifiedType type)
                => IsFloatType(type) ? AbiRegisterClass.Floating : AbiRegisterClass.General;

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

            private enum AbiRegisterClass
            {
                General,
                Floating,
            }
        }
    }
}
