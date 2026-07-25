using Cnidaria.X86;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs
{
    internal sealed class X86CodeGeneratorOptions
    {
        public static X86CodeGeneratorOptions Default => new X86CodeGeneratorOptions();

        public int EntryMethodId { get; set; } = -1;
        public bool EmitStartup { get; set; } = true;
        public bool MarkMethodsCodeGenerated { get; set; } = true;
        public bool EmbedRuntime { get; set; } = true;
        public Func<RuntimeMethod, string>? InternalCallSymbolResolver { get; set; }
        public Func<RuntimeMethod, string>? ExternalSymbolResolver { get; set; }
    }

    internal static class X86CodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";

        public static X86Program Build(
            GenTreeProgram program,
            X86CodeGeneratorOptions? options = null,
            TargetInfo? target = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= X86CodeGeneratorOptions.Default;
            target ??= program.Target;
            if (target.Architecture is not (TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64))
                throw new ArgumentException("Managed x86 code generation requires an x86 or x64 target.", nameof(target));
            if (target.OperatingSystem is not (OperatingSystemKind.Linux or OperatingSystemKind.Windows))
                throw new ArgumentException("Managed x86 code generation requires a Linux or Windows target.", nameof(target));
            if (target.Endianness != TargetEndianness.Little)
                throw new NotSupportedException("Big-endian x86 code generation is not supported.");

            var allocatedTarget = program.Target;
            if (allocatedTarget.Architecture != target.Architecture ||
                allocatedTarget.PointerSize != target.PointerSize ||
                allocatedTarget.OperatingSystem != target.OperatingSystem)
            {
                throw new ArgumentException("x86 code generation target is ABI-incompatible with the LSRA input.", nameof(target));
            }

            X86Program managed = new Generator(program, target, X86Target.FromTargetInfo(target), options).Generate();
            return options.EmbedRuntime
                ? X86ObjectComposer.Compose(managed, X86Runtime.GetObject(target))
                : managed;
        }

        private sealed class Generator
        {
            private readonly GenTreeProgram _program;
            private readonly TargetInfo _target;
            private readonly X86Target _machineTarget;
            private readonly X86CodeGeneratorOptions _options;
            private readonly TextSectionBuilder _text;
            private readonly DataSectionBuilder _rodata;
            private readonly DataSectionBuilder _data;
            private readonly List<X86ObjectSymbol> _symbols = new List<X86ObjectSymbol>();
            private readonly Dictionary<int, GenTreeMethod> _methodsById = new Dictionary<int, GenTreeMethod>();
            private readonly Dictionary<int, string> _methodLabels = new Dictionary<int, string>();
            private readonly Dictionary<string, StringLiteralDraft> _stringLiterals = new Dictionary<string, StringLiteralDraft>(StringComparer.Ordinal);
            private readonly Dictionary<int, TypeDescriptorDraft> _typeDescriptors = new Dictionary<int, TypeDescriptorDraft>();
            private readonly List<InterfaceDispatchCellDraft> _interfaceDispatchCells = new List<InterfaceDispatchCellDraft>();
            private readonly Dictionary<int, string> _unboxingStubLabels = new Dictionary<int, string>();
            private readonly List<UnboxingStubDraft> _unboxingStubs = new List<UnboxingStubDraft>();
            private readonly Dictionary<int, string> _virtualDispatchMethodLabels = new Dictionary<int, string>();
            private readonly Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft> _delegateTargetThunks = new Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft>();
            private readonly List<SafePointDraft> _safePoints = new List<SafePointDraft>();
            private readonly Dictionary<int, StaticStorageDraft> _staticStorageByTypeId = new Dictionary<int, StaticStorageDraft>();
            private readonly Dictionary<int, TypeInitializationThunkDraft> _typeInitializationThunksByTypeId = new Dictionary<int, TypeInitializationThunkDraft>();
            private readonly List<TypeInitializationThunkDraft> _typeInitializationThunks = new List<TypeInitializationThunkDraft>();
            private readonly List<StaticRootDraft> _staticRoots = new List<StaticRootDraft>();
            private readonly Dictionary<int, StaticExceptionDraft> _staticExceptionsByTypeId = new Dictionary<int, StaticExceptionDraft>();
            private readonly List<StaticExceptionDraft> _staticExceptions = new List<StaticExceptionDraft>();
            private readonly Dictionary<int, EhMethodDraft> _ehMethodsByMethodId = new Dictionary<int, EhMethodDraft>();
            private readonly List<EhMethodDraft> _ehMethods = new List<EhMethodDraft>();
            private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _externalFunctions = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _externalObjects = new HashSet<string>(StringComparer.Ordinal);
            private string? _virtualDispatchFailureStubLabel;
            private bool _virtualDispatchMetadataPrepared;
            private int _nextLocalLabel;

            public Generator(
                GenTreeProgram program,
                TargetInfo target,
                X86Target machineTarget,
                X86CodeGeneratorOptions options)
            {
                _program = program;
                _target = target;
                _machineTarget = machineTarget;
                _options = options;
                _text = new TextSectionBuilder(machineTarget, TextSectionName);
                _rodata = new DataSectionBuilder(RodataSectionName, X86ObjectSectionKind.Rodata, machineTarget);
                _data = new DataSectionBuilder(DataSectionName, X86ObjectSectionKind.Data, machineTarget);
            }

            public X86Program Generate()
            {
                IndexMethods();
                IndexDelegateStubs();
                GenTreeMethod entryMethod = SelectEntryMethod();

                foreach (GenTreeMethod method in _program.Methods)
                    EmitMethod(method);

                EmitDelegateStubs();

                RuntimeMethod? entryTypeInitializer = null;
                if (_options.EmitStartup &&
                    !StringComparer.Ordinal.Equals(entryMethod.RuntimeMethod.Name, ".cctor") &&
                    !entryMethod.RuntimeMethod.DeclaringType.IsBeforeFieldInit &&
                    (entryMethod.RuntimeMethod.IsStatic ||
                     entryMethod.RuntimeMethod.DeclaringType.IsValueType ||
                     StringComparer.Ordinal.Equals(entryMethod.RuntimeMethod.Name, ".ctor")))
                {
                    entryTypeInitializer = FindTypeInitializer(entryMethod.RuntimeMethod.DeclaringType);
                    if (entryTypeInitializer is not null)
                        _ = GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType);
                }

                EmitTypeInitializationThunks();
                EmitEhTransferHelper();
                PrepareVirtualDispatchMetadata();
                EmitUnboxingStubs();
                EmitVirtualDispatchFailureStub();
                RuntimeMetadataLabels metadata = EmitRuntimeMetadata();
                string entry = _methodLabels[entryMethod.RuntimeMethod.MethodId];
                if (_options.EmitStartup)
                {
                    entry = EmitStartup(
                        entryMethod,
                        entry,
                        metadata.SafePointTableLabel,
                        _safePoints.Count,
                        metadata.TypeInfoTableLabel,
                        _typeDescriptors.Count,
                        metadata.StaticRootTableLabel,
                        _staticRoots.Count,
                        entryTypeInitializer);
                }

                _symbols.Add(new X86ObjectSymbol(
                    TextSectionName,
                    TextSectionName,
                    0,
                    _text.ByteLength,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Section));

                var sections = ImmutableArray.CreateBuilder<X86DataSection>();
                if (_rodata.ByteLength != 0)
                {
                    sections.Add(_rodata.ToSection());
                    _symbols.Add(new X86ObjectSymbol(
                        RodataSectionName,
                        RodataSectionName,
                        0,
                        _rodata.ByteLength,
                        X86ObjectSymbolBinding.Local,
                        X86ObjectSymbolKind.Section));
                }
                if (_data.ByteLength != 0)
                {
                    sections.Add(_data.ToSection());
                    _symbols.Add(new X86ObjectSymbol(
                        DataSectionName,
                        DataSectionName,
                        0,
                        _data.ByteLength,
                        X86ObjectSymbolBinding.Local,
                        X86ObjectSymbolKind.Section));
                }

                return new X86Program(
                    _machineTarget,
                    _text.ToSection(),
                    sections.ToImmutable(),
                    _symbols.ToImmutableArray(),
                    entry);
            }

            private void IndexMethods()
            {
                foreach (GenTreeMethod method in _program.Methods)
                {
                    if (method.Phase < GenTreeMethodPhase.RegisterAllocated)
                        throw new InvalidOperationException("x86 code generation requires LSRA-annotated LIR.");
                    if (!_methodsById.TryAdd(method.RuntimeMethod.MethodId, method))
                        throw new InvalidOperationException($"Duplicate method in x86 code generation input: M{method.RuntimeMethod.MethodId}.");
                    string methodLabel = CreateUniqueGlobalLabel(FormatMethodSymbol(method.RuntimeMethod));
                    _methodLabels.Add(method.RuntimeMethod.MethodId, methodLabel);
                    if (method.Cfg.ExceptionRegions.Length != 0)
                        PrepareEhMethod(method, methodLabel);
                }
            }

            private void IndexDelegateStubs()
            {
                for (int m = 0; m < _program.Methods.Length; m++)
                {
                    GenTreeMethod method = _program.Methods[m];
                    for (int i = 0; i < method.LinearNodes.Length; i++)
                    {
                        GenTree node = method.LinearNodes[i];
                        if (node.TreeKind == GenTreeKind.NewDelegate)
                        {
                            RuntimeType delegateType = node.RuntimeType ?? node.Type ??
                                throw new InvalidOperationException("NewDelegate node has no delegate type.");
                            RuntimeMethod target = node.Method ??
                                throw new InvalidOperationException("NewDelegate node has no target method.");
                            EnsureDelegateType(delegateType);
                            RuntimeMethod invoke = ResolveDelegateInvoke(delegateType);
                            bool closed = node.Uses.Length != 0;
                            var key = new DelegateTargetThunkKey(delegateType.TypeId, target.MethodId, closed);
                            if (!_delegateTargetThunks.ContainsKey(key))
                            {
                                _delegateTargetThunks.Add(
                                    key,
                                    new DelegateTargetThunkDraft(
                                        delegateType,
                                        invoke,
                                        target,
                                        closed,
                                        CreateUniqueGlobalLabel($"__delegate_target_T{delegateType.TypeId}_M{target.MethodId}_{(closed ? "closed" : "open")}")));
                            }
                        }
                    }
                }
            }

            private string GetDelegateTargetThunkLabel(RuntimeType delegateType, RuntimeMethod target, bool closed)
            {
                var key = new DelegateTargetThunkKey(delegateType.TypeId, target.MethodId, closed);
                if (_delegateTargetThunks.TryGetValue(key, out DelegateTargetThunkDraft? draft))
                    return draft.Label;
                throw new InvalidOperationException(
                    $"Missing x86 delegate target thunk for T{delegateType.TypeId}, M{target.MethodId}, closed={closed}.");
            }

            private static RuntimeMethod ResolveDelegateInvoke(RuntimeType delegateType)
            {
                for (RuntimeType? current = delegateType; current is not null; current = current.BaseType)
                {
                    for (int i = 0; i < current.Methods.Length; i++)
                    {
                        RuntimeMethod method = current.Methods[i];
                        if (StringComparer.Ordinal.Equals(method.Name, "Invoke"))
                            return method;
                    }
                }
                throw new MissingMethodException(delegateType.Name, "Invoke");
            }

            private static void EnsureDelegateType(RuntimeType type)
            {
                for (RuntimeType? current = type; current is not null; current = current.BaseType)
                {
                    if (StringComparer.Ordinal.Equals(current.Namespace, "System") &&
                        StringComparer.Ordinal.Equals(current.Name, "MulticastDelegate"))
                    {
                        return;
                    }
                }
                throw new TypeLoadException($"T{type.TypeId} '{type}' is not a delegate type.");
            }

            private int FindDelegateFieldOffset(RuntimeType delegateType, string fieldName)
            {
                for (RuntimeType? current = delegateType; current is not null; current = current.BaseType)
                {
                    for (int i = 0; i < current.InstanceFields.Length; i++)
                    {
                        RuntimeField field = current.InstanceFields[i];
                        if (!field.IsStatic && StringComparer.Ordinal.Equals(field.Name, fieldName))
                            return field.Offset;
                    }
                }
                throw new MissingFieldException(delegateType.Name, fieldName);
            }

            private RuntimeType FindSystemType(string name)
            {
                RuntimeTypeSystem typeSystem = _program.TypeSystem ??
                    throw new InvalidOperationException("Code generation requires a runtime type system.");
                RuntimeType[] knownTypes = typeSystem.SnapshotKnownTypes();
                for (int i = 0; i < knownTypes.Length; i++)
                {
                    RuntimeType candidate = knownTypes[i];
                    if (StringComparer.Ordinal.Equals(candidate.Namespace, "System") &&
                        StringComparer.Ordinal.Equals(candidate.Name, name))
                    {
                        return candidate;
                    }
                }
                throw new TypeLoadException($"Runtime type 'System.{name}' is required by x86 delegate lowering.");
            }

            private RuntimeType GetDelegateInvocationListArrayType()
            {
                RuntimeTypeSystem typeSystem = _program.TypeSystem ??
                    throw new InvalidOperationException("Code generation requires a runtime type system.");
                return typeSystem.GetArrayType(FindSystemType("Delegate"));
            }

            private DelegateAbiBundle GetDelegateInvokeAbi(RuntimeMethod invokeMethod)
                => BuildDelegateAbiBundle(invokeMethod);

            private DelegateAbiBundle BuildDelegateAbiBundle(RuntimeMethod method)
            {
                int logicalCount = method.ParameterTypes.Length + (method.HasThis ? 1 : 0);
                int hiddenInsertion = MachineAbi.RequiresHiddenReturnBuffer(method, _target)
                    ? MachineAbi.HiddenReturnBufferInsertionIndex(method, logicalCount, _target)
                    : -1;
                int general = 0;
                int floating = 0;
                int stack = RegisterInfo.MinimumOutgoingArgumentSlots(_target);
                int saveCursor = 0;
                int maxStackSlot = stack - 1;
                DelegateAbiEntity? hidden = null;
                var arguments = ImmutableArray.CreateBuilder<DelegateAbiEntity>(logicalCount);
                var ordered = ImmutableArray.CreateBuilder<DelegateAbiSlice>();

                for (int i = 0; i < logicalCount; i++)
                {
                    if (hiddenInsertion == i)
                        hidden = AddHiddenReturnBuffer();

                    RuntimeType type = GetLogicalArgumentType(method, i);
                    int entityBase = saveCursor;
                    ImmutableArray<DelegateAbiSlice> slices = BuildDelegateArgumentSlices(
                        type,
                        ref general,
                        ref floating,
                        ref stack,
                        entityBase,
                        ref maxStackSlot);
                    arguments.Add(new DelegateAbiEntity(type, entityBase, slices));
                    ordered.AddRange(slices);
                    int entitySize = Math.Max(_target.PointerSize, Math.Max(1, type.SizeOf));
                    for (int sliceIndex = 0; sliceIndex < slices.Length; sliceIndex++)
                        entitySize = Math.Max(entitySize, checked(slices[sliceIndex].ValueOffset + slices[sliceIndex].Size));
                    saveCursor = AlignUp(checked(entityBase + entitySize), _target.PointerSize);
                }

                if (hiddenInsertion == logicalCount)
                    hidden = AddHiddenReturnBuffer();

                int outgoingStackSize = maxStackSlot < 0
                    ? 0
                    : checked((maxStackSlot + 1) * _target.StackSlotSize);
                return new DelegateAbiBundle(
                    method,
                    hidden,
                    arguments.ToImmutable(),
                    ordered.ToImmutable(),
                    saveCursor,
                    outgoingStackSize);

                DelegateAbiEntity AddHiddenReturnBuffer()
                {
                    int entityBase = saveCursor;
                    AbiArgumentLocation location = MachineAbi.AssignScalarArgumentLocation(
                        RegisterClass.General,
                        _target.PointerSize,
                        ref general,
                        ref floating,
                        ref stack,
                        _target);
                    var slice = new DelegateAbiSlice(
                        location,
                        RegisterClass.General,
                        0,
                        _target.PointerSize,
                        entityBase);
                    if (location.IsStack)
                        maxStackSlot = Math.Max(maxStackSlot, MachineAbi.LastStackSlotIndex(location, _target));
                    ordered.Add(slice);
                    saveCursor = AlignUp(checked(saveCursor + _target.PointerSize), _target.PointerSize);
                    return new DelegateAbiEntity(null, entityBase, ImmutableArray.Create(slice));
                }
            }

            private ImmutableArray<DelegateAbiSlice> BuildDelegateArgumentSlices(
                RuntimeType type,
                ref int general,
                ref int floating,
                ref int stack,
                int saveBase,
                ref int maxStackSlot)
            {
                AbiValueInfo valueAbi = MachineAbi.ClassifyValue(
                    type,
                    MachineAbi.StackKindForType(type),
                    isReturn: false,
                    target: _target);
                AbiValueInfo abi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                    valueAbi,
                    general,
                    floating,
                    _target);
                var result = ImmutableArray.CreateBuilder<DelegateAbiSlice>();

                switch (abi.PassingKind)
                {
                    case AbiValuePassingKind.Void:
                        return result.ToImmutable();

                    case AbiValuePassingKind.ScalarRegister:
                        {
                            RegisterClass registerClass = abi.RegisterClass == RegisterClass.Invalid
                                ? RegisterClass.General
                                : abi.RegisterClass;
                            int size = Math.Max(1, abi.Size <= 0 ? _target.GeneralRegisterSize : abi.Size);
                            AbiArgumentLocation location = MachineAbi.AssignScalarArgumentLocation(
                                registerClass,
                                size,
                                ref general,
                                ref floating,
                                ref stack,
                                _target);
                            if (location.IsStack)
                                maxStackSlot = Math.Max(maxStackSlot, MachineAbi.LastStackSlotIndex(location, _target));
                            result.Add(new DelegateAbiSlice(location, registerClass, 0, size, saveBase));
                            return result.ToImmutable();
                        }

                    case AbiValuePassingKind.MultiRegister:
                        {
                            int aggregateStackSlot = -1;
                            int aggregateStackBaseOffset = 0;
                            ImmutableArray<AbiRegisterSegment> segments = MachineAbi.GetRegisterSegments(abi, _target);
                            for (int i = 0; i < segments.Length; i++)
                            {
                                AbiRegisterSegment segment = segments[i];
                                AbiArgumentLocation location = MachineAbi.AssignAggregateSegmentArgumentLocation(
                                    segment,
                                    ref general,
                                    ref floating,
                                    ref stack,
                                    ref aggregateStackSlot,
                                    ref aggregateStackBaseOffset,
                                    _target);
                                if (location.IsStack)
                                    maxStackSlot = Math.Max(maxStackSlot, MachineAbi.LastStackSlotIndex(location, _target));
                                result.Add(new DelegateAbiSlice(
                                    location,
                                    segment.RegisterClass,
                                    segment.Offset,
                                    segment.Size,
                                    checked(saveBase + segment.Offset)));
                            }
                            return result.ToImmutable();
                        }

                    case AbiValuePassingKind.Stack:
                    case AbiValuePassingKind.Indirect:
                        {
                            int size = Math.Max(1, abi.Size <= 0 ? _target.PointerSize : abi.Size);
                            int stackSlot = stack;
                            stack = checked(stack + MachineAbi.StackSlotsForArgumentSize(size, _target));
                            AbiArgumentLocation location = AbiArgumentLocation.ForStack(
                                RegisterClass.General,
                                stackSlot,
                                0,
                                size);
                            maxStackSlot = Math.Max(maxStackSlot, MachineAbi.LastStackSlotIndex(location, _target));
                            result.Add(new DelegateAbiSlice(location, RegisterClass.General, 0, size, saveBase));
                            return result.ToImmutable();
                        }

                    default:
                        throw new InvalidOperationException($"Unsupported delegate ABI passing kind {abi.PassingKind}.");
                }
            }

            private static RuntimeType GetLogicalArgumentType(RuntimeMethod method, int logicalIndex)
            {
                if (method.HasThis)
                {
                    if (logicalIndex == 0)
                        return method.DeclaringType;
                    logicalIndex--;
                }
                if ((uint)logicalIndex >= (uint)method.ParameterTypes.Length)
                    throw new ArgumentOutOfRangeException(nameof(logicalIndex));
                return method.ParameterTypes[logicalIndex];
            }

            private void EmitDelegateStubs()
            {
                foreach (DelegateTargetThunkDraft thunk in _delegateTargetThunks.Values.OrderBy(static thunk => thunk.Label, StringComparer.Ordinal))
                    EmitDelegateTargetThunk(thunk);
            }

            private void EmitDelegateTargetThunk(DelegateTargetThunkDraft thunk)
            {
                DelegateAbiBundle incoming = BuildDelegateAbiBundle(thunk.InvokeMethod);
                DelegateAbiBundle target = BuildDelegateAbiBundle(thunk.TargetMethod);
                ValidateDelegateTargetThunk(thunk, incoming, target);

                int outgoingSize = AlignUp(
                    Math.Max(incoming.OutgoingStackSize, target.OutgoingStackSize),
                    Math.Max(1, _target.StackSlotSize));
                int incomingSaveOffset = AlignUp(outgoingSize, _target.PointerSize);
                int targetSaveOffset = AlignUp(
                    checked(incomingSaveOffset + incoming.TotalSaveSize),
                    _target.PointerSize);
                int frameSize = AlignUp(
                    checked(targetSaveOffset + target.TotalSaveSize),
                    _target.CallFrameAlignment);

                int start = _text.ByteLength;
                DefineLabel(thunk.Label);
                Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rbp, 8)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbp, 8), Reg(X86Register.Rsp, 8)));
                if (frameSize != 0)
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(frameSize)));

                EmitDelegateSaveIncomingBundle(incoming, incomingSaveOffset, frameSize);
                MaterializeDelegateTargetArguments(thunk, incoming, incomingSaveOffset, target, targetSaveOffset, frameSize);

                var roots = ImmutableArray.CreateBuilder<SafePointRootDraft>();
                if (target.HiddenReturnBuffer is DelegateAbiEntity hidden)
                {
                    roots.Add(new SafePointRootDraft(
                        checked(-frameSize + targetSaveOffset + hidden.SaveBase),
                        RegisterGcRootKind.InteriorPointer));
                }
                for (int i = 0; i < target.LogicalArguments.Length; i++)
                {
                    DelegateAbiEntity entity = target.LogicalArguments[i];
                    if (entity.Type is null)
                        continue;
                    var fields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                    AppendTypedGcFields(
                        fields,
                        checked(-frameSize + targetSaveOffset + entity.SaveBase),
                        entity.Type);
                    for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                    {
                        roots.Add(new SafePointRootDraft(fields[fieldIndex].Offset, fields[fieldIndex].Kind));
                    }
                }

                string returnLabel = CreateLocalLabel(thunk.Label + "_gc_return");
                SafePointDraft safePoint = AddSafePoint(
                    thunk.Label,
                    returnLabel,
                    savedFramePointerOffset: 0,
                    savedReturnAddressOffset: _target.PointerSize,
                    roots: roots.ToImmutable());
                EmitDelegateRestoreBundle(target, targetSaveOffset, frameSize);
                PublishSyntheticGcTransition(safePoint);
                EmitCall(ResolveMethodLabel(thunk.TargetMethod));
                DefineLabel(returnLabel);

                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rsp, 8), Reg(X86Register.Rbp, 8)));
                Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.Rbp, 8)));
                Emit(X86Instruction.Ret());

                _symbols.Add(new X86ObjectSymbol(
                    thunk.Label,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Function));
            }

            private void ValidateDelegateTargetThunk(
                DelegateTargetThunkDraft thunk,
                DelegateAbiBundle incoming,
                DelegateAbiBundle target)
            {
                if ((incoming.HiddenReturnBuffer is null) != (target.HiddenReturnBuffer is null))
                {
                    throw new InvalidOperationException(
                        $"Delegate Invoke and target M{thunk.TargetMethod.MethodId} use incompatible return ABIs.");
                }

                AbiValueInfo incomingReturn = MachineAbi.ClassifyValue(
                    thunk.InvokeMethod.ReturnType,
                    MachineAbi.StackKindForType(thunk.InvokeMethod.ReturnType),
                    isReturn: true,
                    target: _target);
                AbiValueInfo targetReturn = MachineAbi.ClassifyValue(
                    thunk.TargetMethod.ReturnType,
                    MachineAbi.StackKindForType(thunk.TargetMethod.ReturnType),
                    isReturn: true,
                    target: _target);
                if (!MachineAbi.HaveMatchingArgumentValueLayout(
                        incomingReturn,
                        targetReturn,
                        _target,
                        requireMatchingRegisterClasses: true))
                {
                    throw new InvalidOperationException(
                        $"Delegate Invoke and target M{thunk.TargetMethod.MethodId} use incompatible return ABIs.");
                }

                int expectedTargetArgumentCount = checked(
                    incoming.LogicalArguments.Length - 1 + (thunk.Closed ? 1 : 0));
                if (incoming.LogicalArguments.Length == 0 ||
                    target.LogicalArguments.Length != expectedTargetArgumentCount)
                {
                    throw new InvalidOperationException(
                        $"Delegate target thunk argument mismatch for M{thunk.TargetMethod.MethodId}.");
                }
            }

            private void EmitDelegateSaveIncomingBundle(DelegateAbiBundle bundle, int saveBase, int frameSize)
            {
                for (int i = 0; i < bundle.OrderedSlices.Length; i++)
                {
                    DelegateAbiSlice slice = bundle.OrderedSlices[i];
                    int destinationOffset = checked(-frameSize + saveBase + slice.SaveOffset);
                    if (slice.Location.IsRegister)
                    {
                        EmitDelegateMemoryStore(
                            slice.Location.Register,
                            X86Register.Rbp,
                            destinationOffset,
                            slice.Size);
                    }
                    else
                    {
                        int sourceOffset = checked(
                            2 * _target.PointerSize +
                            slice.Location.StackSlotIndex * _target.StackSlotSize +
                            slice.Location.StackOffset);
                        EmitDelegateCopyMemory(
                            X86Register.Rbp,
                            sourceOffset,
                            X86Register.Rbp,
                            destinationOffset,
                            slice.Size);
                    }
                }
            }

            private void EmitDelegateRestoreBundle(DelegateAbiBundle bundle, int saveBase, int frameSize)
            {
                for (int i = 0; i < bundle.OrderedSlices.Length; i++)
                {
                    DelegateAbiSlice slice = bundle.OrderedSlices[i];
                    int sourceOffset = checked(-frameSize + saveBase + slice.SaveOffset);
                    if (slice.Location.IsRegister)
                    {
                        EmitDelegateMemoryLoad(
                            slice.Location.Register,
                            X86Register.Rbp,
                            sourceOffset,
                            slice.Size);
                    }
                    else
                    {
                        int destinationOffset = checked(
                            slice.Location.StackSlotIndex * _target.StackSlotSize +
                            slice.Location.StackOffset);
                        EmitDelegateCopyMemory(
                            X86Register.Rbp,
                            sourceOffset,
                            X86Register.Rsp,
                            destinationOffset,
                            slice.Size);
                    }
                }
            }

            private void MaterializeDelegateTargetArguments(
                DelegateTargetThunkDraft thunk,
                DelegateAbiBundle incoming,
                int incomingSaveBase,
                DelegateAbiBundle target,
                int targetSaveBase,
                int frameSize)
            {
                if (incoming.HiddenReturnBuffer is DelegateAbiEntity incomingHidden &&
                    target.HiddenReturnBuffer is DelegateAbiEntity targetHidden)
                {
                    CopyDelegateSavedEntity(
                        incomingHidden,
                        incomingSaveBase,
                        targetHidden,
                        targetSaveBase,
                        frameSize);
                }

                for (int i = 0; i < target.LogicalArguments.Length; i++)
                {
                    DelegateAbiEntity destination = target.LogicalArguments[i];
                    if (thunk.Closed && i == 0)
                    {
                        if (incoming.LogicalArguments.Length == 0 || incoming.LogicalArguments[0].Slices.Length == 0)
                            throw new InvalidOperationException($"Delegate target thunk for M{thunk.TargetMethod.MethodId} has no delegate receiver.");
                        DelegateAbiSlice receiver = incoming.LogicalArguments[0].Slices[0];
                        Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R10, _target.PointerSize),
                            Mem(X86Register.Rbp, checked(-frameSize + incomingSaveBase + receiver.SaveOffset), _target.PointerSize)));
                        Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R11, _target.PointerSize),
                            Mem(X86Register.R10, FindDelegateFieldOffset(thunk.DelegateType, "_target"), _target.PointerSize)));
                        StoreRegisterToDelegateSavedEntity(
                            X86Register.R11,
                            destination,
                            targetSaveBase,
                            frameSize);
                        continue;
                    }

                    int incomingArgumentIndex = checked(1 + i - (thunk.Closed ? 1 : 0));
                    if ((uint)incomingArgumentIndex >= (uint)incoming.LogicalArguments.Length)
                    {
                        throw new InvalidOperationException(
                            $"Delegate target thunk argument mismatch for M{thunk.TargetMethod.MethodId}.");
                    }
                    CopyDelegateSavedEntity(
                        incoming.LogicalArguments[incomingArgumentIndex],
                        incomingSaveBase,
                        destination,
                        targetSaveBase,
                        frameSize);
                }
            }

            private void CopyDelegateSavedEntity(
                DelegateAbiEntity source,
                int sourceSaveBase,
                DelegateAbiEntity destination,
                int destinationSaveBase,
                int frameSize)
            {
                if (source.Slices.Length != destination.Slices.Length)
                    throw new InvalidOperationException("Delegate argument ABI slice count mismatch.");

                for (int i = 0; i < source.Slices.Length; i++)
                {
                    DelegateAbiSlice sourceSlice = source.Slices[i];
                    DelegateAbiSlice destinationSlice = destination.Slices[i];
                    if (sourceSlice.ValueOffset != destinationSlice.ValueOffset ||
                        sourceSlice.Size != destinationSlice.Size)
                    {
                        throw new InvalidOperationException("Delegate argument ABI slice layout mismatch.");
                    }
                    EmitDelegateCopyMemory(
                        X86Register.Rbp,
                        checked(-frameSize + sourceSaveBase + sourceSlice.SaveOffset),
                        X86Register.Rbp,
                        checked(-frameSize + destinationSaveBase + destinationSlice.SaveOffset),
                        sourceSlice.Size);
                }
            }

            private void StoreRegisterToDelegateSavedEntity(
                X86Register source,
                DelegateAbiEntity destination,
                int destinationSaveBase,
                int frameSize)
            {
                if (destination.Slices.Length != 1)
                    throw new InvalidOperationException("A closed delegate target must bind to a scalar first argument.");
                DelegateAbiSlice slice = destination.Slices[0];
                Emit(X86Instruction.Binary(
                    X86InstrKind.Mov,
                    Mem(
                        X86Register.Rbp,
                        checked(-frameSize + destinationSaveBase + slice.SaveOffset),
                        Math.Min(_target.PointerSize, slice.Size)),
                    Reg(source, Math.Min(_target.PointerSize, slice.Size))));
            }

            private void EmitDelegateCopyMemory(
                X86Register sourceBase,
                int sourceOffset,
                X86Register destinationBase,
                int destinationOffset,
                int size)
            {
                int copied = 0;
                while (copied < size)
                {
                    int remaining = size - copied;
                    int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                    Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, chunk),
                        Mem(sourceBase, checked(sourceOffset + copied), chunk)));
                    Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(destinationBase, checked(destinationOffset + copied), chunk),
                        Reg(X86Register.R11, chunk)));
                    copied += chunk;
                }
            }

            private void EmitDelegateMemoryLoad(
                MachineRegister destination,
                X86Register baseRegister,
                int offset,
                int size)
            {
                X86Register register = ToX86Register(destination, _target);
                X86InstrKind opcode = MachineRegisters.GetClass(destination) == RegisterClass.Float
                    ? size switch
                    {
                        4 => X86InstrKind.Movss,
                        8 => X86InstrKind.Movsd,
                        16 => X86InstrKind.Movdqu,
                        _ => throw new NotSupportedException($"Unsupported delegate floating load size {size}.")
                    }
                    : X86InstrKind.Mov;
                Emit(X86Instruction.Binary(opcode, Reg(register, size), Mem(baseRegister, offset, size)));
            }

            private void EmitDelegateMemoryStore(
                MachineRegister source,
                X86Register baseRegister,
                int offset,
                int size)
            {
                X86Register register = ToX86Register(source, _target);
                X86InstrKind opcode = MachineRegisters.GetClass(source) == RegisterClass.Float
                    ? size switch
                    {
                        4 => X86InstrKind.Movss,
                        8 => X86InstrKind.Movsd,
                        16 => X86InstrKind.Movdqu,
                        _ => throw new NotSupportedException($"Unsupported delegate floating store size {size}.")
                    }
                    : X86InstrKind.Mov;
                Emit(X86Instruction.Binary(opcode, Mem(baseRegister, offset, size), Reg(register, size)));
            }

            private void PublishSyntheticGcTransition(SafePointDraft safePoint)
            {
                EmitLea(X86Register.R10, safePoint.DescriptorLabel);
                Emit(X86Instruction.Binary(
                    X86InstrKind.Mov,
                    SymbolMemory(ResolveExternalObject(X86Runtime.CurrentSafePointSymbol), size: _target.PointerSize),
                    Reg(X86Register.R10, _target.PointerSize)));
                Emit(X86Instruction.Binary(
                    X86InstrKind.Mov,
                    SymbolMemory(ResolveExternalObject(X86Runtime.CurrentFramePointerSymbol), size: _target.PointerSize),
                    Reg(X86Register.Rbp, _target.PointerSize)));
            }

            private void PrepareEhMethod(GenTreeMethod method, string methodLabel)
            {
                ImmutableArray<CfgExceptionRegion> regions = method.Cfg.ExceptionRegions;
                ImmutableArray<int> order = EhFuncletLayout.ComputeVmRegionOrder(method.Cfg);
                var localIndexByRegion = new Dictionary<int, int>(order.Length);
                for (int i = 0; i < order.Length; i++)
                    localIndexByRegion[regions[order[i]].Index] = i;

                var clauses = ImmutableArray.CreateBuilder<EhClauseDraft>(order.Length);
                for (int i = 0; i < order.Length; i++)
                {
                    CfgExceptionRegion region = regions[order[i]];
                    if (region.Kind == CfgExceptionRegionKind.Filter)
                    {
                        throw new NotSupportedException(
                            $"Exception filters are not supported in method M{method.RuntimeMethod.MethodId} '{method.RuntimeMethod.Name}'.");
                    }

                    string? catchTypeLabel = null;
                    int kind;
                    switch (region.Kind)
                    {
                        case CfgExceptionRegionKind.Catch:
                            if (region.CatchTypeToken == 0)
                            {
                                kind = 2;
                            }
                            else
                            {
                                kind = 1;
                                RuntimeModule bodyModule = method.RuntimeMethod.BodyModule ?? method.Module;
                                if (_program.TypeSystem == null)
                                    throw new InvalidOperationException("Code generation requires a runtime type system for exception metadata.");
                                RuntimeType catchType = _program.TypeSystem.ResolveTypeInMethodContext(
                                    bodyModule,
                                    region.CatchTypeToken,
                                    method.RuntimeMethod);
                                catchTypeLabel = GetTypeDescriptorLabel(catchType);
                            }
                            break;
                        case CfgExceptionRegionKind.Finally:
                            kind = 3;
                            break;
                        case CfgExceptionRegionKind.Fault:
                            kind = 4;
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported x86 exception region kind {region.Kind}.");
                    }

                    int parentLocalIndex = -1;
                    if (region.ParentIndex >= 0 && localIndexByRegion.TryGetValue(region.ParentIndex, out int mappedParent))
                        parentLocalIndex = mappedParent;

                    clauses.Add(new EhClauseDraft(region, kind, catchTypeLabel, parentLocalIndex));
                }

                var draft = new EhMethodDraft(
                    method,
                    CreateLocalLabel(methodLabel + "_eh_info"),
                    CreateLocalLabel(methodLabel + "_eh_clauses"),
                    clauses.ToImmutable());
                _ehMethodsByMethodId.Add(method.RuntimeMethod.MethodId, draft);
                _ehMethods.Add(draft);
            }

            private GenTreeMethod SelectEntryMethod()
            {
                if (_program.Methods.IsDefaultOrEmpty)
                    throw new InvalidOperationException("Cannot generate an x86 program without methods.");

                if (_options.EntryMethodId >= 0)
                {
                    if (_methodsById.TryGetValue(_options.EntryMethodId, out GenTreeMethod? selected))
                        return selected;
                    throw new InvalidOperationException($"Entry method M{_options.EntryMethodId} is not present in the generated program.");
                }

                GenTreeMethod? firstStaticParameterless = null;
                foreach (GenTreeMethod method in _program.Methods)
                {
                    RuntimeMethod runtimeMethod = method.RuntimeMethod;
                    if (runtimeMethod.IsStatic && runtimeMethod.ParameterTypes.Length == 0 && firstStaticParameterless is null)
                        firstStaticParameterless = method;

                    if (!StringComparer.Ordinal.Equals(runtimeMethod.Name, "Main"))
                        continue;
                    if (!runtimeMethod.IsStatic)
                        continue;
                    if (runtimeMethod.ParameterTypes.Length == 0 ||
                        (runtimeMethod.ParameterTypes.Length == 1 && IsStringArray(runtimeMethod.ParameterTypes[0])))
                    {
                        return method;
                    }
                }

                return firstStaticParameterless ?? _program.Methods[0];
            }

            private void EmitMethod(GenTreeMethod method)
            {
                string label = _methodLabels[method.RuntimeMethod.MethodId];
                int start = _text.ByteLength;
                _text.DefineLabel(label);
                new MethodEmitter(this, method, label).Emit();
                _symbols.Add(new X86ObjectSymbol(
                    label,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Global,
                    X86ObjectSymbolKind.Function));
                if (_options.MarkMethodsCodeGenerated)
                    method.SetPhase(GenTreeMethodPhase.CodeGenerated);
            }

            public string ResolveMethodLabel(RuntimeMethod method)
            {
                if (method.HasInternalCall)
                {
                    if (X86Runtime.TryEvaluateIsReferenceOrContainsReferences(method, out _))
                        throw new InvalidOperationException("Compile-time InternalCall has no external symbol.");
                    if (X86Runtime.IsGcSafePointInternalCall(method))
                        return ResolveExternalFunction(X86Runtime.NewArraySymbol);

                    string? label = _options.InternalCallSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(label))
                        label = X86Runtime.ResolveInternalCall(method);
                    return ResolveExternalFunction(label);
                }

                if (method.IsExtern)
                {
                    string? label = _options.ExternalSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(label))
                        label = method.DllImportData?.EntryPointName;
                    if (string.IsNullOrWhiteSpace(label))
                        label = method.Name;
                    return ResolveExternalFunction(label);
                }

                if (_methodLabels.TryGetValue(method.MethodId, out string? managedLabel))
                    return managedLabel;

                string? external = _options.ExternalSymbolResolver?.Invoke(method);
                if (string.IsNullOrWhiteSpace(external))
                    external = SanitizeSymbolName(FormatMethodSymbol(method));
                return ResolveExternalFunction(external);
            }

            public string ResolveExternalFunction(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("External function symbol is empty.", nameof(label));
                if (_externalFunctions.Add(label))
                {
                    _symbols.Add(new X86ObjectSymbol(
                        label,
                        string.Empty,
                        0,
                        0,
                        X86ObjectSymbolBinding.External,
                        X86ObjectSymbolKind.Function));
                }
                return label;
            }

            public string ResolveExternalObject(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("External object symbol is empty.", nameof(label));
                if (_externalObjects.Add(label))
                {
                    _symbols.Add(new X86ObjectSymbol(
                        label,
                        string.Empty,
                        0,
                        0,
                        X86ObjectSymbolBinding.External,
                        X86ObjectSymbolKind.Object));
                }
                return label;
            }

            public string GetStringLiteralLabel(RuntimeType type, string text)
            {
                if (!IsSystemStringType(type))
                    throw new InvalidOperationException("A string literal must have System.String runtime type.");
                text ??= string.Empty;
                if (_stringLiterals.TryGetValue(text, out StringLiteralDraft? existing))
                    return existing.Label;

                string typeLabel = GetTypeDescriptorLabel(type);
                var literal = new StringLiteralDraft(text, CreateLocalLabel("string_literal"), typeLabel);
                _stringLiterals.Add(text, literal);
                return literal.Label;
            }

            public string AddConstantData(byte[] bytes, int alignment, string prefix)
            {
                if (bytes is null)
                    throw new ArgumentNullException(nameof(bytes));
                if (bytes.Length == 0)
                    throw new ArgumentException("Constant data is empty.", nameof(bytes));
                if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(alignment));

                int offset = _rodata.Align(alignment);
                string label = CreateLocalLabel(prefix);
                _rodata.EmitBytes(bytes);
                AddDataSymbol(label, offset, bytes.Length);
                return label;
            }

            private string GetTypeDescriptorLabel(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_typeDescriptors.TryGetValue(type.TypeId, out TypeDescriptorDraft? existing))
                    return existing.Label;
                if (_virtualDispatchMetadataPrepared)
                    throw new InvalidOperationException("A MethodTable was requested after virtual dispatch metadata was finalized.");
                if (type.Kind == RuntimeTypeKind.TypeParam)
                    throw new NotSupportedException("Open generic parameters do not have standalone MethodTables.");

                _program.TypeSystem?.EnsureConstructedMembers(type);
                EnsureVirtualTable(type);
                string label = CreateLocalLabel($"type_{type.TypeId}");
                ImmutableArray<RuntimeType> interfaces = CollectImplementedInterfaces(type);
                var descriptor = new TypeDescriptorDraft(type, label, interfaces);
                _typeDescriptors.Add(type.TypeId, descriptor);

                var fields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                var componentFields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                if (type.Kind == RuntimeTypeKind.Array)
                {
                    RuntimeType elementType = type.ElementType ?? throw new InvalidOperationException("Array runtime type has no element type.");
                    AppendTypedGcFields(componentFields, 0, elementType);
                }
                else if (type.IsValueType)
                {
                    AppendTypedGcFields(fields, _target.ManagedObjectHeaderSize, type);
                }
                else
                {
                    AppendObjectGcFields(fields, type);
                }
                descriptor.Fields = fields.ToImmutable();
                descriptor.ComponentFields = componentFields.ToImmutable();

                RuntimeType? relatedType = type.Kind is RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef
                    ? type.ElementType
                    : type.BaseType;
                if (relatedType is not null)
                    descriptor.RelatedTypeLabel = GetTypeDescriptorLabel(relatedType);
                for (int i = 0; i < interfaces.Length; i++)
                    _ = GetTypeDescriptorLabel(interfaces[i]);
                return label;
            }

            private void EnsureVirtualTable(RuntimeType type)
            {
                if (type.Kind is not (RuntimeTypeKind.Class or
                    RuntimeTypeKind.Interface or
                    RuntimeTypeKind.Struct or
                    RuntimeTypeKind.Enum or
                    RuntimeTypeKind.Array))
                {
                    return;
                }

                if (type.VTable.Length != 0)
                    return;

                RuntimeType? baseType = type.BaseType;
                if (baseType is not null)
                {
                    _program.TypeSystem?.EnsureConstructedMembers(baseType);
                    EnsureVirtualTable(baseType);
                }

                RuntimeMethod[] baseVtable = baseType?.VTable ?? Array.Empty<RuntimeMethod>();
                bool declaresVirtualMethod = false;
                for (int i = 0; i < type.Methods.Length; i++)
                {
                    if (!type.Methods[i].IsStatic && type.Methods[i].IsVirtual)
                    {
                        declaresVirtualMethod = true;
                        break;
                    }
                }

                if (baseVtable.Length == 0 && !declaresVirtualMethod)
                    return;

                var vtable = new List<RuntimeMethod>(baseVtable.Length + 8);
                vtable.AddRange(baseVtable);
                for (int i = 0; i < type.Methods.Length; i++)
                {
                    RuntimeMethod method = type.Methods[i];
                    if (method.IsStatic || !method.IsVirtual)
                        continue;

                    int slot = -1;
                    if (baseType is not null && !method.IsNewSlot)
                    {
                        for (int candidateSlot = 0; candidateSlot < baseVtable.Length; candidateSlot++)
                        {
                            RuntimeMethod candidate = baseVtable[candidateSlot];
                            if (StringComparer.Ordinal.Equals(candidate.Name, method.Name) &&
                                SameRuntimeSignature(candidate, method))
                            {
                                slot = candidateSlot;
                                break;
                            }
                        }
                    }

                    if (slot >= 0)
                    {
                        method.VTableSlot = slot;
                        vtable[slot] = method;
                    }
                    else
                    {
                        method.VTableSlot = vtable.Count;
                        vtable.Add(method);
                    }
                }

                type.VTable = vtable.ToArray();
            }

            public string CreateInterfaceDispatchCell(RuntimeMethod declaredMethod)
            {
                if (declaredMethod is null)
                    throw new ArgumentNullException(nameof(declaredMethod));
                if (_virtualDispatchMetadataPrepared)
                    throw new InvalidOperationException("An interface dispatch cell was requested after virtual dispatch metadata was finalized.");
                if (declaredMethod.DeclaringType.Kind != RuntimeTypeKind.Interface)
                    throw new ArgumentException("Interface dispatch cells require an interface method.", nameof(declaredMethod));

                _ = GetTypeDescriptorLabel(declaredMethod.DeclaringType);
                string label = CreateLocalLabel($"interface_dispatch_M{declaredMethod.MethodId}");
                _interfaceDispatchCells.Add(new InterfaceDispatchCellDraft(declaredMethod, label));
                return label;
            }

            private void PrepareVirtualDispatchMetadata()
            {
                if (_virtualDispatchMetadataPrepared)
                    throw new InvalidOperationException("Virtual dispatch metadata was already finalized.");

                TypeDescriptorDraft[] descriptors = _typeDescriptors.Values.OrderBy(static d => d.Type.TypeId).ToArray();
                for (int i = 0; i < descriptors.Length; i++)
                {
                    TypeDescriptorDraft descriptor = descriptors[i];
                    RuntimeMethod[] vtable = descriptor.Type.VTable;
                    var targets = ImmutableArray.CreateBuilder<string>(vtable.Length);
                    for (int slot = 0; slot < vtable.Length; slot++)
                        targets.Add(GetVirtualDispatchTargetLabel(vtable[slot]));
                    descriptor.VTableTargets = targets.ToImmutable();
                }

                for (int i = 0; i < _interfaceDispatchCells.Count; i++)
                {
                    InterfaceDispatchCellDraft cell = _interfaceDispatchCells[i];
                    var entries = ImmutableArray.CreateBuilder<InterfaceDispatchEntryDraft>();
                    for (int t = 0; t < descriptors.Length; t++)
                    {
                        TypeDescriptorDraft descriptor = descriptors[t];
                        RuntimeType receiverType = descriptor.Type;
                        if (receiverType.Kind == RuntimeTypeKind.Interface ||
                            (!receiverType.IsReferenceType && !receiverType.IsValueType))
                        {
                            continue;
                        }

                        RuntimeMethod? target = ResolveInterfaceDispatchTarget(receiverType, cell.DeclaredMethod);
                        if (target is null)
                            continue;

                        entries.Add(new InterfaceDispatchEntryDraft(
                            descriptor.Label,
                            GetVirtualDispatchTargetLabel(target)));
                    }
                    cell.Entries = entries.ToImmutable();
                }

                _virtualDispatchMetadataPrepared = true;
            }

            private string GetVirtualDispatchTargetLabel(RuntimeMethod target)
            {
                if (!TryResolveVirtualDispatchMethodLabel(target, out string targetLabel))
                    return GetVirtualDispatchFailureStubLabel();

                if (!target.DeclaringType.IsValueType)
                    return targetLabel;

                if (_unboxingStubLabels.TryGetValue(target.MethodId, out string? existing))
                    return existing;

                string label = CreateLocalLabel($"unbox_M{target.MethodId}");
                _unboxingStubLabels.Add(target.MethodId, label);
                _unboxingStubs.Add(new UnboxingStubDraft(targetLabel, label));
                return label;
            }

            private bool TryResolveVirtualDispatchMethodLabel(RuntimeMethod target, out string label)
            {
                if (_virtualDispatchMethodLabels.TryGetValue(target.MethodId, out string? existing))
                {
                    label = existing;
                    return true;
                }

                if (target.HasInternalCall)
                {
                    string? internalCallLabel = _options.InternalCallSymbolResolver?.Invoke(target);
                    if (string.IsNullOrWhiteSpace(internalCallLabel))
                    {
                        try
                        {
                            internalCallLabel = X86Runtime.ResolveInternalCall(target);
                        }
                        catch (MissingMethodException)
                        {
                            internalCallLabel = null;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(internalCallLabel))
                    {
                        label = string.Empty;
                        return false;
                    }
                    label = ResolveExternalFunction(internalCallLabel);
                }
                else if (target.IsExtern)
                {
                    string? externalLabel = _options.ExternalSymbolResolver?.Invoke(target);
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = target.DllImportData?.EntryPointName;
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = target.Name;
                    label = ResolveExternalFunction(externalLabel);
                }
                else if (_methodLabels.TryGetValue(target.MethodId, out string? methodLabel))
                {
                    label = methodLabel;
                }
                else
                {
                    string? externalLabel = _options.ExternalSymbolResolver?.Invoke(target);
                    if (string.IsNullOrWhiteSpace(externalLabel))
                    {
                        label = string.Empty;
                        return false;
                    }
                    label = ResolveExternalFunction(externalLabel);
                }

                _virtualDispatchMethodLabels.Add(target.MethodId, label);
                return true;
            }

            public string GetVirtualDispatchFailureStubLabel()
            {
                _virtualDispatchFailureStubLabel ??= CreateLocalLabel("virtual_dispatch_failure");
                return _virtualDispatchFailureStubLabel;
            }

            private void EmitUnboxingStubs()
            {
                for (int i = 0; i < _unboxingStubs.Count; i++)
                {
                    UnboxingStubDraft stub = _unboxingStubs[i];
                    int start = _text.ByteLength;
                    DefineLabel(stub.Label);
                    X86Register receiver = AbiArgumentRegister(0);
                    Emit(X86Instruction.Binary(
                        X86InstrKind.Add,
                        Reg(receiver, _target.PointerSize),
                        Imm(_target.ManagedObjectHeaderSize)));
                    Emit(X86Instruction.Branch(
                        X86InstrKind.Jmp,
                        X86Operand.SymbolOperand(stub.TargetLabel, 4, X86ObjectRelocationKind.Relative32)));
                    _symbols.Add(new X86ObjectSymbol(
                        stub.Label,
                        TextSectionName,
                        start,
                        _text.ByteLength - start,
                        X86ObjectSymbolBinding.Local,
                        X86ObjectSymbolKind.Function));
                }
            }

            private void EmitVirtualDispatchFailureStub()
            {
                if (_virtualDispatchFailureStubLabel is null)
                    return;

                int start = _text.ByteLength;
                DefineLabel(_virtualDispatchFailureStubLabel);
                Emit(X86Instruction.Binary(
                    X86InstrKind.Mov,
                    Reg(AbiArgumentRegister(0), _target.PointerSize),
                    Imm(151)));
                Emit(X86Instruction.Branch(
                    X86InstrKind.Jmp,
                    X86Operand.SymbolOperand(
                        ResolveExternalFunction(X86Runtime.FailFastSymbol),
                        4,
                        X86ObjectRelocationKind.Relative32)));
                _symbols.Add(new X86ObjectSymbol(
                    _virtualDispatchFailureStubLabel,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Function));
            }

            private RuntimeMethod? ResolveInterfaceDispatchTarget(RuntimeType actual, RuntimeMethod declared)
            {
                if (!ImplementsInterface(actual, declared.DeclaringType))
                    return null;

                for (RuntimeType? type = actual; type is not null; type = type.BaseType)
                {
                    RuntimeMethod? explicitImpl = TryResolveExplicitInterfaceImpl(type, declared);
                    if (explicitImpl is not null)
                        return explicitImpl;
                }

                RuntimeMethod? implicitImpl = FindMostDerivedMethodByNameAndSig(actual, declared);
                if (implicitImpl is not null)
                    return implicitImpl;

                return null;
            }

            private RuntimeMethod? TryResolveExplicitInterfaceImpl(RuntimeType implementationType, RuntimeMethod declared)
            {
                Dictionary<int, RuntimeMethod>? map = implementationType.ExplicitInterfaceMethodImpls;
                if (map is null || map.Count == 0)
                    return null;

                if (map.TryGetValue(declared.MethodId, out RuntimeMethod? exact))
                    return ProjectRuntimeMethodToOwner(implementationType, exact);

                RuntimeTypeSystem? typeSystem = _program.TypeSystem;
                if (typeSystem is null)
                    return null;

                foreach (KeyValuePair<int, RuntimeMethod> pair in map)
                {
                    RuntimeMethod interfaceMethod;
                    try
                    {
                        interfaceMethod = typeSystem.GetMethodById(pair.Key);
                    }
                    catch (MissingMethodException)
                    {
                        continue;
                    }

                    if (SameInterfaceMethodIdentity(interfaceMethod, declared))
                        return ProjectRuntimeMethodToOwner(implementationType, pair.Value);
                }

                return null;
            }

            private RuntimeMethod ProjectRuntimeMethodToOwner(RuntimeType owner, RuntimeMethod method)
            {
                if (method.DeclaringType.TypeId == owner.TypeId)
                    return method;

                _program.TypeSystem?.EnsureConstructedMembers(owner);
                RuntimeMethod[] methods = owner.Methods;
                for (int i = 0; i < methods.Length; i++)
                {
                    RuntimeMethod candidate = methods[i];
                    if (!StringComparer.Ordinal.Equals(candidate.Name, method.Name) ||
                        candidate.GenericArity != method.GenericArity ||
                        candidate.IsStatic != method.IsStatic)
                    {
                        continue;
                    }

                    if (candidate.Body is not null && method.Body is not null && ReferenceEquals(candidate.Body, method.Body))
                        return candidate;
                    if (SameRuntimeSignature(candidate, method))
                        return candidate;
                }

                return method;
            }

            private static RuntimeMethod? FindMostDerivedMethodByNameAndSig(RuntimeType actual, RuntimeMethod declared)
            {
                for (RuntimeType? type = actual; type is not null; type = type.BaseType)
                {
                    RuntimeMethod[] methods = type.Methods;
                    for (int i = 0; i < methods.Length; i++)
                    {
                        RuntimeMethod candidate = methods[i];
                        if (candidate.IsStatic ||
                            candidate.IsPrivate ||
                            !StringComparer.Ordinal.Equals(candidate.Name, declared.Name) ||
                            !SameRuntimeSignature(candidate, declared))
                        {
                            continue;
                        }

                        return candidate;
                    }
                }

                return null;
            }

            private static bool ImplementsInterface(RuntimeType type, RuntimeType target)
            {
                var seen = new HashSet<int>();
                for (RuntimeType? current = type; current is not null; current = current.BaseType)
                {
                    RuntimeType[] interfaces = current.Interfaces;
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        if (InterfaceDerivesFromOrEquals(interfaces[i], target, seen))
                            return true;
                    }
                }
                return false;
            }

            private static bool InterfaceDerivesFromOrEquals(RuntimeType current, RuntimeType target, HashSet<int> seen)
            {
                if (SameInterfaceType(current, target))
                    return true;
                if (!seen.Add(current.TypeId))
                    return false;

                RuntimeType[] interfaces = current.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                {
                    if (InterfaceDerivesFromOrEquals(interfaces[i], target, seen))
                        return true;
                }
                return false;
            }

            private static bool SameInterfaceType(RuntimeType implemented, RuntimeType declared)
            {
                if (implemented.TypeId == declared.TypeId)
                    return true;

                RuntimeType? implementedDefinition = implemented.GenericTypeDefinition;
                RuntimeType? declaredDefinition = declared.GenericTypeDefinition;
                if (implementedDefinition is null || declaredDefinition is null ||
                    implementedDefinition.TypeId != declaredDefinition.TypeId)
                {
                    return false;
                }

                RuntimeType[] implementedArguments = implemented.GenericTypeArguments;
                RuntimeType[] declaredArguments = declared.GenericTypeArguments;
                if (implementedArguments.Length != declaredArguments.Length)
                    return false;
                for (int i = 0; i < implementedArguments.Length; i++)
                {
                    if (!CompatibleInterfaceSignatureType(implementedArguments[i], declaredArguments[i]))
                        return false;
                }
                return true;
            }

            private static bool SameInterfaceMethodIdentity(RuntimeMethod interfaceMethod, RuntimeMethod declared)
            {
                if (!StringComparer.Ordinal.Equals(interfaceMethod.Name, declared.Name) ||
                    interfaceMethod.GenericArity != declared.GenericArity ||
                    !SameRuntimeTypeDefinitionOrExact(interfaceMethod.DeclaringType, declared.DeclaringType) ||
                    interfaceMethod.ParameterTypes.Length != declared.ParameterTypes.Length ||
                    !CompatibleInterfaceSignatureType(interfaceMethod.ReturnType, declared.ReturnType))
                {
                    return false;
                }

                for (int i = 0; i < interfaceMethod.ParameterTypes.Length; i++)
                {
                    if (!CompatibleInterfaceSignatureType(interfaceMethod.ParameterTypes[i], declared.ParameterTypes[i]))
                        return false;
                }
                return true;
            }

            private static bool SameRuntimeSignature(RuntimeMethod left, RuntimeMethod right)
            {
                if (left.GenericArity != right.GenericArity ||
                    left.ParameterTypes.Length != right.ParameterTypes.Length ||
                    left.ReturnType.TypeId != right.ReturnType.TypeId)
                {
                    return false;
                }

                for (int i = 0; i < left.ParameterTypes.Length; i++)
                {
                    if (left.ParameterTypes[i].TypeId != right.ParameterTypes[i].TypeId)
                        return false;
                }
                return true;
            }

            private static bool SameRuntimeTypeDefinitionOrExact(RuntimeType left, RuntimeType right)
            {
                if (left.TypeId == right.TypeId)
                    return true;
                RuntimeType leftDefinition = left.GenericTypeDefinition ?? left;
                RuntimeType rightDefinition = right.GenericTypeDefinition ?? right;
                return leftDefinition.TypeId == rightDefinition.TypeId;
            }

            private static bool CompatibleInterfaceSignatureType(RuntimeType left, RuntimeType right)
            {
                if (left.TypeId == right.TypeId)
                    return true;
                if (left.Kind == RuntimeTypeKind.TypeParam || right.Kind == RuntimeTypeKind.TypeParam)
                    return true;
                return SameRuntimeTypeDefinitionOrExact(left, right);
            }

            public string GetStaticStorageLabel(RuntimeType type)
                => GetOrCreateStaticStorage(type).StorageLabel;

            public RuntimeMethod? FindTypeInitializer(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));

                _program.TypeSystem?.EnsureConstructedMembers(type);
                for (int i = 0; i < type.Methods.Length; i++)
                {
                    RuntimeMethod method = type.Methods[i];
                    if (method.IsStatic &&
                        method.ParameterTypes.Length == 0 &&
                        StringComparer.Ordinal.Equals(method.Name, ".cctor"))
                    {
                        return method;
                    }
                }

                return null;
            }

            public string GetTypeInitializationThunkLabel(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_typeInitializationThunksByTypeId.TryGetValue(type.TypeId, out TypeInitializationThunkDraft? existing))
                    return existing.Label;

                RuntimeMethod initializer = FindTypeInitializer(type) ??
                    throw new InvalidOperationException($"Type T{type.TypeId} '{type}' has no static initializer.");
                string stateLabel = GetTypeInitializationStateLabel(type);
                string label = CreateUniqueGlobalLabel($"__cctor_thunk_T{type.TypeId}");
                var draft = new TypeInitializationThunkDraft(initializer, stateLabel, label);
                _typeInitializationThunksByTypeId.Add(type.TypeId, draft);
                _typeInitializationThunks.Add(draft);
                return label;
            }

            private void EmitTypeInitializationThunks()
            {
                for (int i = 0; i < _typeInitializationThunks.Count; i++)
                    EmitTypeInitializationThunk(_typeInitializationThunks[i]);
            }

            private void EmitTypeInitializationThunk(TypeInitializationThunkDraft thunk)
            {
                int start = _text.ByteLength;
                DefineLabel(thunk.Label);

                Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rbp, _target.PointerSize)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbp, _target.PointerSize), Reg(X86Register.Rsp, _target.PointerSize)));
                if (RegisterInfo.IsWindowsX64(_target))
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, _target.PointerSize), Imm(32)));

                string executeLabel = CreateLocalLabel(thunk.Label + "_execute");
                string doneLabel = CreateLocalLabel(thunk.Label + "_done");
                string invalidStateLabel = CreateLocalLabel(thunk.Label + "_invalid_state");

                EmitLea(X86Register.R10, thunk.StateLabel);
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R11, 4), Mem(X86Register.R10, 0, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R11, 4), Reg(X86Register.R11, 4)));
                Emit(X86Instruction.ConditionalBranch(
                    X86Condition.E,
                    X86Operand.SymbolOperand(doneLabel, 4, X86ObjectRelocationKind.Relative32)));
                Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(2)));
                Emit(X86Instruction.ConditionalBranch(
                    X86Condition.E,
                    X86Operand.SymbolOperand(doneLabel, 4, X86ObjectRelocationKind.Relative32)));
                Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(1)));
                Emit(X86Instruction.ConditionalBranch(
                    X86Condition.E,
                    X86Operand.SymbolOperand(executeLabel, 4, X86ObjectRelocationKind.Relative32)));
                Emit(X86Instruction.Branch(
                    X86InstrKind.Jmp,
                    X86Operand.SymbolOperand(invalidStateLabel, 4, X86ObjectRelocationKind.Relative32)));

                DefineLabel(executeLabel);
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R10, 0, 4), Imm(2)));
                string returnLabel = CreateLocalLabel(thunk.Label + "_gc_return");
                AddSafePoint(
                    thunk.Label,
                    returnLabel,
                    savedFramePointerOffset: 0,
                    savedReturnAddressOffset: _target.PointerSize,
                    roots: ImmutableArray<SafePointRootDraft>.Empty);
                EmitCall(ResolveMethodLabel(thunk.Initializer));
                DefineLabel(returnLabel);
                EmitLea(X86Register.R10, thunk.StateLabel);
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R10, 0, 4), Imm(0)));
                Emit(X86Instruction.Branch(
                    X86InstrKind.Jmp,
                    X86Operand.SymbolOperand(doneLabel, 4, X86ObjectRelocationKind.Relative32)));

                DefineLabel(invalidStateLabel);
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(AbiArgumentRegister(0), _target.PointerSize), Imm(150)));
                EmitCall(ResolveExternalFunction(X86Runtime.FailFastSymbol));
                Emit(new X86Instruction(X86InstrKind.Ud2));

                DefineLabel(doneLabel);
                if (RegisterInfo.IsWindowsX64(_target))
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, _target.PointerSize), Imm(32)));
                Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.Rbp, _target.PointerSize)));
                Emit(X86Instruction.Ret());

                _symbols.Add(new X86ObjectSymbol(
                    thunk.Label,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Function));
            }

            public string GetTypeInitializationStateLabel(RuntimeType type)
            {
                StaticStorageDraft storage = GetOrCreateStaticStorage(type);
                if (storage.InitializationStateLabel is not null)
                    return storage.InitializationStateLabel;

                string label = CreateLocalLabel($"type_init_{type.TypeId}");
                int offset = _data.Align(4);
                _data.EmitInt32(1);
                AddDataSymbol(label, DataSectionName, offset, 4);
                storage.InitializationStateLabel = label;
                return label;
            }

            private StaticStorageDraft GetOrCreateStaticStorage(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_staticStorageByTypeId.TryGetValue(type.TypeId, out StaticStorageDraft? existing))
                    return existing;
                if (type.StaticFields.Length != 0 && type.StaticSize <= 0)
                    throw new InvalidOperationException($"Static layout was not computed for T{type.TypeId} '{type.Namespace}.{type.Name}'.");

                string label = CreateLocalLabel($"statics_{type.TypeId}");
                int alignment = Math.Max(1, type.StaticAlign);
                int offset = _data.Align(alignment);
                int size = Math.Max(1, type.StaticSize);
                _data.EmitBytes(new byte[size]);
                AddDataSymbol(label, DataSectionName, offset, size);

                var storage = new StaticStorageDraft(type, label);
                _staticStorageByTypeId.Add(type.TypeId, storage);

                var roots = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                for (int i = 0; i < type.StaticFields.Length; i++)
                {
                    RuntimeField field = type.StaticFields[i];
                    AppendTypedGcFields(roots, field.Offset, field.FieldType);
                }
                for (int i = 0; i < roots.Count; i++)
                    _staticRoots.Add(new StaticRootDraft(label, roots[i].Offset, roots[i].Kind));

                return storage;
            }

            private void AppendObjectGcFields(ImmutableArray<TypeGcFieldDraft>.Builder fields, RuntimeType type)
            {
                var hierarchy = new List<RuntimeType>();
                for (RuntimeType? current = type; current is not null; current = current.BaseType)
                    hierarchy.Add(current);
                hierarchy.Reverse();

                for (int i = 0; i < hierarchy.Count; i++)
                {
                    RuntimeType current = hierarchy[i];
                    for (int f = 0; f < current.InstanceFields.Length; f++)
                    {
                        RuntimeField field = current.InstanceFields[f];
                        if (!field.IsStatic)
                            AppendTypedGcFields(fields, field.Offset, field.FieldType);
                    }
                }
            }

            private void AppendTypedGcFields(ImmutableArray<TypeGcFieldDraft>.Builder fields, int baseOffset, RuntimeType type)
            {
                if (type.IsReferenceType || type.Kind == RuntimeTypeKind.TypeParam)
                {
                    fields.Add(new TypeGcFieldDraft(baseOffset, RegisterGcRootKind.ObjectReference));
                    return;
                }
                if (type.Kind == RuntimeTypeKind.ByRef)
                {
                    fields.Add(new TypeGcFieldDraft(baseOffset, RegisterGcRootKind.InteriorPointer));
                    return;
                }
                if (type.Kind == RuntimeTypeKind.Pointer || !type.ContainsGcPointers)
                    return;

                for (int i = 0; i < type.InstanceFields.Length; i++)
                {
                    RuntimeField field = type.InstanceFields[i];
                    if (field.IsStatic)
                        continue;
                    int repeat = type.InlineArrayLength > 0 && ReferenceEquals(field, type.InlineArrayElementField)
                        ? type.InlineArrayLength
                        : 1;
                    int elementSize = Math.Max(1, field.FieldType.SizeOf);
                    for (int element = 0; element < repeat; element++)
                        AppendTypedGcFields(fields, checked(baseOffset + field.Offset + element * elementSize), field.FieldType);
                }
            }

            private SafePointDraft AddSafePoint(
                string methodLabel,
                string returnLabel,
                int savedFramePointerOffset,
                int savedReturnAddressOffset,
                ImmutableArray<SafePointRootDraft> roots)
            {
                var draft = new SafePointDraft(
                    CreateLocalLabel(methodLabel + "_gc_safe_point"),
                    returnLabel,
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    roots);
                _safePoints.Add(draft);
                return draft;
            }

            private static int ToRuntimeRootKind(RegisterGcRootKind kind)
                => kind == RegisterGcRootKind.ObjectReference ? 0 : 1;

            private static ImmutableArray<RuntimeType> CollectImplementedInterfaces(RuntimeType type)
            {
                var result = ImmutableArray.CreateBuilder<RuntimeType>();
                var seen = new HashSet<int>();

                void AddInterface(RuntimeType interfaceType)
                {
                    if (interfaceType.Kind == RuntimeTypeKind.TypeParam || !seen.Add(interfaceType.TypeId))
                        return;

                    result.Add(interfaceType);
                    for (int i = 0; i < interfaceType.Interfaces.Length; i++)
                        AddInterface(interfaceType.Interfaces[i]);
                }

                for (RuntimeType? current = type; current is not null; current = current.BaseType)
                {
                    for (int i = 0; i < current.Interfaces.Length; i++)
                        AddInterface(current.Interfaces[i]);
                }

                return result.ToImmutable();
            }

            private string GetStaticExceptionObjectLabel(string @namespace, string name)
            {
                if (_program.TypeSystem == null)
                    throw new InvalidOperationException("Code generation requires a runtime type system for exception metadata.");

                RuntimeType? type = null;
                RuntimeType[] knownTypes = _program.TypeSystem.SnapshotKnownTypes();
                for (int i = 0; i < knownTypes.Length; i++)
                {
                    RuntimeType candidate = knownTypes[i];
                    if (StringComparer.Ordinal.Equals(candidate.Namespace, @namespace) &&
                        StringComparer.Ordinal.Equals(candidate.Name, name))
                    {
                        type = candidate;
                        break;
                    }
                }

                if (type is null)
                    throw new TypeLoadException($"Runtime type '{@namespace}.{name}' is required by x86 exception lowering.");
                if (_staticExceptionsByTypeId.TryGetValue(type.TypeId, out StaticExceptionDraft? existing))
                    return existing.ObjectLabel;

                var draft = new StaticExceptionDraft(
                    type,
                    CreateLocalLabel("static_exception_" + name),
                    GetTypeDescriptorLabel(type));
                _staticExceptionsByTypeId.Add(type.TypeId, draft);
                _staticExceptions.Add(draft);
                return draft.ObjectLabel;
            }

            private void EmitExceptionMetadata()
            {
                int pointerSize = _target.PointerSize;
                for (int i = 0; i < _ehMethods.Count; i++)
                {
                    EhMethodDraft method = _ehMethods[i];
                    int clausesOffset = _rodata.Align(pointerSize);
                    AddDataSymbol(method.ClausesLabel, clausesOffset, checked(method.Clauses.Length * pointerSize * 12));
                    for (int c = 0; c < method.Clauses.Length; c++)
                    {
                        EhClauseDraft clause = method.Clauses[c];
                        if (clause.TryStartLabel is null || clause.TryEndLabel is null ||
                            clause.HandlerStartLabel is null || clause.HandlerEndLabel is null)
                        {
                            throw new InvalidOperationException($"x86 EH native ranges were not bound for method M{method.Method.RuntimeMethod.MethodId}.");
                        }

                        _rodata.EmitPointer(clause.Kind);
                        _rodata.EmitPointerRelocation(clause.TryStartLabel);
                        _rodata.EmitPointerRelocation(clause.TryEndLabel);
                        _rodata.EmitPointerRelocation(clause.HandlerStartLabel);
                        _rodata.EmitPointerRelocation(clause.HandlerEndLabel);
                        if (clause.CatchTypeLabel is null)
                            _rodata.EmitPointer(0);
                        else
                            _rodata.EmitPointerRelocation(clause.CatchTypeLabel);
                        _rodata.EmitPointer(clause.ParentLocalIndex);
                        _rodata.EmitPointer(clause.Region.TryStartPc);
                        _rodata.EmitPointer(clause.Region.TryEndPc);
                        _rodata.EmitPointer(clause.Region.HandlerStartPc);
                        _rodata.EmitPointer(clause.Region.HandlerEndPc);
                        _rodata.EmitPointer(clause.Region.SourceHandlerIndex);
                    }

                    int infoOffset = _rodata.Align(pointerSize);
                    AddDataSymbol(method.InfoLabel, infoOffset, pointerSize * 2);
                    _rodata.EmitPointer(method.Clauses.Length);
                    _rodata.EmitPointerRelocation(method.ClausesLabel);
                }
            }

            private RuntimeMetadataLabels EmitRuntimeMetadata()
            {
                EmitExceptionMetadata();
                TypeDescriptorDraft[] descriptors = _typeDescriptors.Values.OrderBy(static d => d.Type.TypeId).ToArray();

                for (int i = 0; i < descriptors.Length; i++)
                {
                    TypeDescriptorDraft descriptor = descriptors[i];
                    if (descriptor.Fields.Length != 0)
                    {
                        descriptor.FieldsLabel = CreateLocalLabel(descriptor.Label + "_gc_fields");
                        int offset = _rodata.Align(_target.PointerSize);
                        AddDataSymbol(descriptor.FieldsLabel, offset, checked(descriptor.Fields.Length * _target.PointerSize * 2));
                        for (int f = 0; f < descriptor.Fields.Length; f++)
                        {
                            _rodata.EmitPointer(descriptor.Fields[f].Offset);
                            _rodata.EmitPointer(ToRuntimeRootKind(descriptor.Fields[f].Kind));
                        }
                    }

                    if (descriptor.ComponentFields.Length != 0)
                    {
                        descriptor.ComponentFieldsLabel = CreateLocalLabel(descriptor.Label + "_gc_component_fields");
                        int offset = _rodata.Align(_target.PointerSize);
                        AddDataSymbol(descriptor.ComponentFieldsLabel, offset, checked(descriptor.ComponentFields.Length * _target.PointerSize * 2));
                        for (int f = 0; f < descriptor.ComponentFields.Length; f++)
                        {
                            _rodata.EmitPointer(descriptor.ComponentFields[f].Offset);
                            _rodata.EmitPointer(ToRuntimeRootKind(descriptor.ComponentFields[f].Kind));
                        }
                    }

                    if (descriptor.Interfaces.Length != 0)
                    {
                        descriptor.InterfacesLabel = CreateLocalLabel(descriptor.Label + "_interfaces");
                        int offset = _rodata.Align(_target.PointerSize);
                        AddDataSymbol(
                            descriptor.InterfacesLabel,
                            offset,
                            checked((descriptor.Interfaces.Length + 1) * _target.PointerSize));
                        for (int f = 0; f < descriptor.Interfaces.Length; f++)
                            _rodata.EmitPointerRelocation(GetTypeDescriptorLabel(descriptor.Interfaces[f]));
                        _rodata.EmitPointer(0);
                    }

                    if (descriptor.VTableTargets.Length != 0)
                    {
                        descriptor.VTableLabel = CreateLocalLabel(descriptor.Label + "_vtable");
                        int offset = _rodata.Align(_target.PointerSize);
                        AddDataSymbol(
                            descriptor.VTableLabel,
                            offset,
                            checked(descriptor.VTableTargets.Length * _target.PointerSize));
                        for (int slot = 0; slot < descriptor.VTableTargets.Length; slot++)
                            _rodata.EmitPointerRelocation(descriptor.VTableTargets[slot]);
                    }
                }

                for (int i = 0; i < descriptors.Length; i++)
                {
                    TypeDescriptorDraft descriptor = descriptors[i];
                    RuntimeType type = descriptor.Type;
                    bool isString = IsSystemStringType(type);
                    bool isArray = type.Kind == RuntimeTypeKind.Array;
                    int componentSize = isString
                        ? 2
                        : isArray
                            ? GetArrayComponentSize(type)
                            : 0;
                    uint flags = ComputeMethodTableFlags(descriptor, componentSize);
                    int baseSize = isString
                        ? checked(_target.SyncBlockSize + _target.StringCharsOffset + 2)
                        : isArray
                            ? checked(
                                _target.SyncBlockSize +
                                _target.ArrayDataOffset +
                                (type.IsSzArray ? 0 : type.ArrayRank * 8))
                            : type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer
                                ? 0
                                : type.Kind == RuntimeTypeKind.ByRef
                                    ? 1
                                    : Math.Max(
                                        _target.MinimumGcObjectSize,
                                        type.IsValueType
                                            ? checked(_target.SyncBlockSize + _target.ManagedObjectHeaderSize + type.SizeOf)
                                            : checked(_target.SyncBlockSize + type.InstanceSize));

                    int offset = _rodata.Align(_target.PointerSize);
                    AddDataSymbol(descriptor.Label, offset, checked(16 + _target.PointerSize * 3));
                    _rodata.EmitUInt32(flags);
                    _rodata.EmitUInt32(checked((uint)baseSize));
                    if (descriptor.RelatedTypeLabel is null)
                        _rodata.EmitPointer(0);
                    else
                        _rodata.EmitPointerRelocation(descriptor.RelatedTypeLabel);
                    _rodata.EmitUInt16(checked((ushort)descriptor.VTableTargets.Length));
                    _rodata.EmitUInt16(checked((ushort)descriptor.Interfaces.Length));
                    _rodata.EmitUInt32(unchecked((uint)type.TypeId));
                    if (descriptor.InterfacesLabel is null)
                        _rodata.EmitPointer(0);
                    else
                        _rodata.EmitPointerRelocation(descriptor.InterfacesLabel);
                    if (descriptor.VTableLabel is null)
                        _rodata.EmitPointer(0);
                    else
                        _rodata.EmitPointerRelocation(descriptor.VTableLabel);
                }

                for (int i = 0; i < _interfaceDispatchCells.Count; i++)
                {
                    InterfaceDispatchCellDraft cell = _interfaceDispatchCells[i];
                    int offset = _rodata.Align(_target.PointerSize);
                    AddDataSymbol(
                        cell.Label,
                        offset,
                        checked((1 + cell.Entries.Length * 2) * _target.PointerSize));
                    _rodata.EmitPointer(cell.Entries.Length);
                    for (int entry = 0; entry < cell.Entries.Length; entry++)
                    {
                        _rodata.EmitPointerRelocation(cell.Entries[entry].ReceiverTypeLabel);
                        _rodata.EmitPointerRelocation(cell.Entries[entry].TargetLabel);
                    }
                }

                string typeInfoTable = CreateLocalLabel("gc_type_infos");
                int typeInfoOffset = _rodata.Align(_target.PointerSize);
                int typeInfoSize = descriptors.Length == 0
                    ? _target.PointerSize
                    : checked(descriptors.Length * _target.PointerSize * 6);
                AddDataSymbol(typeInfoTable, typeInfoOffset, typeInfoSize);
                if (descriptors.Length == 0)
                {
                    _rodata.EmitPointer(0);
                }
                else
                {
                    for (int i = 0; i < descriptors.Length; i++)
                    {
                        TypeDescriptorDraft descriptor = descriptors[i];
                        _rodata.EmitPointerRelocation(descriptor.Label);
                        _rodata.EmitPointer(descriptor.Fields.Length);
                        if (descriptor.FieldsLabel is null)
                            _rodata.EmitPointer(0);
                        else
                            _rodata.EmitPointerRelocation(descriptor.FieldsLabel);
                        _rodata.EmitPointer(descriptor.ComponentFields.Length);
                        if (descriptor.ComponentFieldsLabel is null)
                            _rodata.EmitPointer(0);
                        else
                            _rodata.EmitPointerRelocation(descriptor.ComponentFieldsLabel);
                        _rodata.EmitPointer(GetRuntimeTypeInfoKind(descriptor.Type));
                    }
                }

                foreach (StringLiteralDraft literal in _stringLiterals.Values)
                {
                    byte[] chars = Encoding.Unicode.GetBytes(literal.Text);
                    _rodata.Align(_target.PointerSize);
                    _rodata.EmitPointer(0);
                    int offset = _rodata.ByteLength;
                    int objectSize = checked(_target.PointerSize + 4 + chars.Length + 2);
                    AddDataSymbol(literal.Label, offset, objectSize);
                    _rodata.EmitPointerRelocation(literal.TypeDescriptorLabel);
                    _rodata.EmitInt32(literal.Text.Length);
                    _rodata.EmitBytes(chars);
                    _rodata.EmitUInt16(0);
                }

                for (int i = 0; i < _staticExceptions.Count; i++)
                {
                    StaticExceptionDraft exception = _staticExceptions[i];
                    int objectSize = Math.Max(_target.PointerSize, exception.Type.InstanceSize);
                    _rodata.Align(_target.PointerSize);
                    _rodata.EmitPointer(0);
                    int offset = _rodata.ByteLength;
                    AddDataSymbol(exception.ObjectLabel, offset, objectSize);
                    _rodata.EmitPointerRelocation(exception.TypeDescriptorLabel);
                    if (objectSize > _target.PointerSize)
                        _rodata.EmitBytes(new byte[objectSize - _target.PointerSize]);
                }

                for (int i = 0; i < _safePoints.Count; i++)
                {
                    SafePointDraft safePoint = _safePoints[i];
                    if (safePoint.Roots.Length == 0)
                        continue;
                    safePoint.RootsLabel = CreateLocalLabel(safePoint.DescriptorLabel + "_roots");
                    int offset = _rodata.Align(_target.PointerSize);
                    AddDataSymbol(safePoint.RootsLabel, offset, checked(safePoint.Roots.Length * _target.PointerSize * 2));
                    for (int r = 0; r < safePoint.Roots.Length; r++)
                    {
                        _rodata.EmitPointer(safePoint.Roots[r].FrameOffset);
                        _rodata.EmitPointer(ToRuntimeRootKind(safePoint.Roots[r].Kind));
                    }
                }

                string safePoints = CreateLocalLabel("gc_safe_points");
                int safePointOffset = _rodata.Align(_target.PointerSize);
                int safePointSize = _safePoints.Count == 0
                    ? _target.PointerSize
                    : checked(_safePoints.Count * _target.PointerSize * 5);
                AddDataSymbol(safePoints, safePointOffset, safePointSize);
                if (_safePoints.Count == 0)
                {
                    _rodata.EmitPointer(0);
                }
                else
                {
                    for (int i = 0; i < _safePoints.Count; i++)
                    {
                        SafePointDraft safePoint = _safePoints[i];
                        int recordOffset = _rodata.ByteLength;
                        AddDataSymbol(safePoint.DescriptorLabel, recordOffset, _target.PointerSize * 5);
                        _rodata.EmitPointerRelocation(safePoint.ReturnLabel);
                        _rodata.EmitPointer(safePoint.SavedFramePointerOffset);
                        _rodata.EmitPointer(safePoint.SavedReturnAddressOffset);
                        _rodata.EmitPointer(safePoint.Roots.Length);
                        if (safePoint.RootsLabel is null)
                            _rodata.EmitPointer(0);
                        else
                            _rodata.EmitPointerRelocation(safePoint.RootsLabel);
                    }
                }

                string staticRoots = CreateLocalLabel("gc_static_roots");
                int staticRootOffset = _rodata.Align(_target.PointerSize);
                int staticRootSize = _staticRoots.Count == 0
                    ? _target.PointerSize
                    : checked(_staticRoots.Count * _target.PointerSize * 2);
                AddDataSymbol(staticRoots, staticRootOffset, staticRootSize);
                if (_staticRoots.Count == 0)
                {
                    _rodata.EmitPointer(0);
                }
                else
                {
                    for (int i = 0; i < _staticRoots.Count; i++)
                    {
                        StaticRootDraft root = _staticRoots[i];
                        _rodata.EmitPointerRelocation(root.StorageLabel, root.Offset);
                        _rodata.EmitPointer(ToRuntimeRootKind(root.Kind));
                    }
                }

                return new RuntimeMetadataLabels(safePoints, typeInfoTable, staticRoots);
            }

            private int GetArrayComponentSize(RuntimeType arrayType)
            {
                RuntimeType elementType = arrayType.ElementType ?? throw new InvalidOperationException("Array runtime type has no element type.");
                if (elementType.IsReferenceType || elementType.Kind is RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                    return _target.PointerSize;
                return Math.Max(1, elementType.SizeOf);
            }

            private static int GetRuntimeTypeInfoKind(RuntimeType type)
            {
                if (IsSystemStringType(type))
                    return 1;
                if (type.Kind == RuntimeTypeKind.Array)
                    return type.IsSzArray ? 2 : 3;
                if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                    return 4;
                return 0;
            }

            private static uint ComputeMethodTableFlags(TypeDescriptorDraft descriptor, int componentSize)
            {
                const uint parameterizedKind = 0x00020000u;
                const uint hasPointers = 0x01000000u;
                const uint elementTypeShift = 26u;
                const uint hasComponentSize = 0x80000000u;

                RuntimeType type = descriptor.Type;
                uint flags = GetMethodTableElementType(type) << (int)elementTypeShift;
                if (type.Kind is RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                    flags |= parameterizedKind;
                if (descriptor.Fields.Length != 0 || descriptor.ComponentFields.Length != 0)
                    flags |= hasPointers;
                if (componentSize != 0)
                    flags |= hasComponentSize | checked((uint)(ushort)componentSize);
                return flags;
            }

            private static uint GetMethodTableElementType(RuntimeType type)
            {
                RuntimePrimitiveKind primitiveKind = type.PrimitiveKind;
                if (type.Kind == RuntimeTypeKind.Enum && primitiveKind == RuntimePrimitiveKind.None)
                {
                    for (int i = 0; i < type.InstanceFields.Length; i++)
                    {
                        RuntimeField field = type.InstanceFields[i];
                        if (!field.IsStatic)
                        {
                            primitiveKind = field.FieldType.PrimitiveKind;
                            break;
                        }
                    }
                }

                uint primitiveElementType = primitiveKind switch
                {
                    RuntimePrimitiveKind.Void => 0x01u,
                    RuntimePrimitiveKind.Boolean => 0x02u,
                    RuntimePrimitiveKind.Char => 0x03u,
                    RuntimePrimitiveKind.Int8 => 0x04u,
                    RuntimePrimitiveKind.UInt8 => 0x05u,
                    RuntimePrimitiveKind.Int16 => 0x06u,
                    RuntimePrimitiveKind.UInt16 => 0x07u,
                    RuntimePrimitiveKind.Int32 => 0x08u,
                    RuntimePrimitiveKind.UInt32 => 0x09u,
                    RuntimePrimitiveKind.Int64 => 0x0au,
                    RuntimePrimitiveKind.UInt64 => 0x0bu,
                    RuntimePrimitiveKind.NativeInt => 0x0cu,
                    RuntimePrimitiveKind.NativeUInt => 0x0du,
                    RuntimePrimitiveKind.Single => 0x0eu,
                    RuntimePrimitiveKind.Double => 0x0fu,
                    _ => 0u,
                };
                if (primitiveElementType != 0u)
                    return primitiveElementType;
                if (StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                    StringComparer.Ordinal.Equals(type.Name, "Array"))
                {
                    return 0x16u;
                }

                return type.Kind switch
                {
                    RuntimeTypeKind.Struct => 0x10u,
                    RuntimeTypeKind.Enum => 0x10u,
                    RuntimeTypeKind.Interface => 0x15u,
                    RuntimeTypeKind.Array => type.IsSzArray ? 0x18u : 0x17u,
                    RuntimeTypeKind.ByRef => 0x19u,
                    RuntimeTypeKind.Pointer => 0x1au,
                    RuntimeTypeKind.FunctionPointer => 0x1bu,
                    RuntimeTypeKind.Class => 0x14u,
                    _ => 0u,
                };
            }

            private void EmitEhTransferHelper()
            {
                string label = X86Runtime.EhTransferSymbol;
                if (!_usedLabels.Add(label))
                    throw new InvalidOperationException($"Duplicate x86 EH transfer symbol: {label}.");

                int start = _text.ByteLength;
                DefineLabel(label);

                if (_machineTarget.Is32Bit)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbx, 4), Mem(X86Register.Rsp, 4, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbp, 4), Reg(X86Register.Rcx, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rbp, -4, 4), Reg(X86Register.Rdx, 4)));

                    ImmutableArray<MachineRegister> generalRegisters = RegisterInfo.AllocatableGeneralRegisters(_target);
                    for (int i = 0; i < generalRegisters.Length; i++)
                    {
                        MachineRegister register = generalRegisters[i];
                        Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(ToX86Register(register, _target), 4),
                            Mem(X86Register.Rbx, (byte)register * 4, 4)));
                    }

                    ImmutableArray<MachineRegister> floatingRegisters = RegisterInfo.AllocatableFloatingRegisters(_target);
                    for (int i = 0; i < floatingRegisters.Length; i++)
                    {
                        MachineRegister register = floatingRegisters[i];
                        int registerIndex = (byte)register - (byte)MachineRegister.F0;
                        Emit(X86Instruction.Binary(
                            X86InstrKind.Movdqu,
                            Reg(ToX86Register(register, _target), 16),
                            Mem(X86Register.Rbx, 256 + registerIndex * 16, 16)));
                    }

                    Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(X86Register.Rsp, 4),
                        Mem(X86Register.Rbp, -4, 4)));
                    Emit(X86Instruction.Ret());
                }
                else
                {
                    X86Register framePointerArgument = AbiArgumentRegister(0);
                    X86Register targetArgument = AbiArgumentRegister(1);
                    X86Register contextArgument = AbiArgumentRegister(2);
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbp, 8), Reg(framePointerArgument, 8)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rbp, -8, 8), Reg(targetArgument, 8)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R11, 8), Reg(contextArgument, 8)));

                    MachineRegister scratchMachineRegister = MachineRegister.Invalid;
                    ImmutableArray<MachineRegister> generalRegisters = RegisterInfo.AllocatableGeneralRegisters(_target);
                    for (int i = 0; i < generalRegisters.Length; i++)
                    {
                        MachineRegister register = generalRegisters[i];
                        X86Register physical = ToX86Register(register, _target);
                        if (physical == X86Register.R11)
                        {
                            scratchMachineRegister = register;
                            continue;
                        }

                        Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(physical, 8),
                            Mem(X86Register.R11, (byte)register * 8, 8)));
                    }

                    ImmutableArray<MachineRegister> floatingRegisters = RegisterInfo.AllocatableFloatingRegisters(_target);
                    for (int i = 0; i < floatingRegisters.Length; i++)
                    {
                        MachineRegister register = floatingRegisters[i];
                        int registerIndex = (byte)register - (byte)MachineRegister.F0;
                        Emit(X86Instruction.Binary(
                            X86InstrKind.Movdqu,
                            Reg(ToX86Register(register, _target), 16),
                            Mem(X86Register.R11, 256 + registerIndex * 16, 16)));
                    }

                    if (scratchMachineRegister == MachineRegister.Invalid)
                        throw new InvalidOperationException("x86 EH transfer requires R11 in the allocatable register set.");
                    Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, 8),
                        Mem(X86Register.R11, (byte)scratchMachineRegister * 8, 8)));
                    Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(X86Register.Rsp, 8),
                        Mem(X86Register.Rbp, -8, 8)));
                    Emit(X86Instruction.Ret());
                }

                _symbols.Add(new X86ObjectSymbol(
                    label,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Global,
                    X86ObjectSymbolKind.Function));
            }

            private X86Register AbiArgumentRegister(int index)
            {
                if (_machineTarget.Is32Bit)
                {
                    return index switch
                    {
                        0 => X86Register.Rcx,
                        1 => X86Register.Rdx,
                        _ => throw new ArgumentOutOfRangeException(nameof(index)),
                    };
                }

                if (_target.OperatingSystem == OperatingSystemKind.Windows)
                {
                    return index switch
                    {
                        0 => X86Register.Rcx,
                        1 => X86Register.Rdx,
                        2 => X86Register.R8,
                        3 => X86Register.R9,
                        _ => throw new ArgumentOutOfRangeException(nameof(index)),
                    };
                }

                return index switch
                {
                    0 => X86Register.Rdi,
                    1 => X86Register.Rsi,
                    2 => X86Register.Rdx,
                    3 => X86Register.Rcx,
                    4 => X86Register.R8,
                    5 => X86Register.R9,
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
            }

            private string EmitStartup(
                GenTreeMethod entryMethod,
                string entryMethodLabel,
                string safePointTableLabel,
                int safePointCount,
                string typeInfoTableLabel,
                int typeInfoCount,
                string staticRootTableLabel,
                int staticRootCount,
                RuntimeMethod? entryTypeInitializer)
            {
                RuntimeMethod runtimeMethod = entryMethod.RuntimeMethod;
                if (!runtimeMethod.IsStatic)
                    throw new NotSupportedException("x86 startup requires a static entry method.");
                if (runtimeMethod.ParameterTypes.Length > 1 ||
                    (runtimeMethod.ParameterTypes.Length == 1 && !IsStringArray(runtimeMethod.ParameterTypes[0])))
                {
                    throw new NotSupportedException("x86 startup supports parameterless or string[] entry methods.");
                }
                if (!IsVoid(runtimeMethod.ReturnType) && !IsIntegerEntryReturn(runtimeMethod.ReturnType))
                    throw new NotSupportedException("x86 startup supports void or integer entry returns.");

                string label = CreateUniqueGlobalLabel("_start");
                int start = _text.ByteLength;
                _text.DefineLabel(label);
                Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rbp, 4), Reg(X86Register.Rbp, 4)));

                string initialize = ResolveExternalFunction(X86Runtime.InitializeSymbol);
                if (_machineTarget.Is32Bit)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rsi, 4), Reg(X86Register.Rsp, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.Rsp, 4), Imm(-16)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rcx, 4), Reg(X86Register.Rsi, 4)));
                    EmitLea(X86Register.Rdx, safePointTableLabel);
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Imm(staticRootCount)));
                    EmitLea(X86Register.Rax, staticRootTableLabel);
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Imm(typeInfoCount)));
                    EmitLea(X86Register.Rax, typeInfoTableLabel);
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Imm(safePointCount)));
                    EmitCall(initialize);
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 4), Imm(20)));

                    if (entryTypeInitializer is not null)
                        EmitCall(GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType));

                    if (runtimeMethod.ParameterTypes.Length == 1)
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rcx, 4), Reg(X86Register.Rcx, 4)));
                    EmitCall(entryMethodLabel);
                    if (IsVoid(runtimeMethod.ReturnType))
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));

                    if (_target.OperatingSystem == OperatingSystemKind.Windows)
                    {
                        string exitImport = ResolveExternalObject("__imp_ExitProcess");
                        Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 4)));
                        Emit(X86Instruction.Branch(X86InstrKind.Call, SymbolMemory(exitImport, size: 4)));
                    }
                    else
                    {
                        Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbx, 4), Reg(X86Register.Rax, 4)));
                        Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Imm(1)));
                        Emit(X86Instruction.Raw(new byte[] { 0xcd, 0x80 }));
                    }
                    Emit(new X86Instruction(X86InstrKind.Ud2));
                }
                else if (_target.OperatingSystem == OperatingSystemKind.Windows)
                {
                    string exitImport = ResolveExternalObject("__imp_ExitProcess");
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R12, 8), Reg(X86Register.Rsp, 8)));
                    Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.Rsp, 8), Imm(-16)));
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(64)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rcx, 8), Reg(X86Register.R12, 8)));
                    EmitLea(X86Register.Rdx, safePointTableLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R8, 8), Imm(safePointCount)));
                    EmitLea(X86Register.R9, typeInfoTableLabel);
                    EmitStoreImmediate(X86Register.Rsp, 32, typeInfoCount, 8);
                    EmitLea(X86Register.Rax, staticRootTableLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rsp, 40, 8), Reg(X86Register.Rax, 8)));
                    EmitStoreImmediate(X86Register.Rsp, 48, staticRootCount, 8);
                    EmitCall(initialize);
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 8), Imm(64)));

                    if (entryTypeInitializer is not null)
                    {
                        Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(32)));
                        EmitCall(GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType));
                        Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 8), Imm(32)));
                    }

                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(32)));
                    if (runtimeMethod.ParameterTypes.Length == 1)
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rcx, 4), Reg(X86Register.Rcx, 4)));
                    EmitCall(entryMethodLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 8), Imm(32)));
                    if (IsVoid(runtimeMethod.ReturnType))
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rcx, 4), Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(32)));
                    Emit(X86Instruction.Branch(X86InstrKind.Call, SymbolMemory(exitImport, size: 8)));
                    Emit(new X86Instruction(X86InstrKind.Ud2));
                }
                else
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R12, 8), Reg(X86Register.Rsp, 8)));
                    Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.Rsp, 8), Imm(-16)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rdi, 8), Reg(X86Register.R12, 8)));
                    EmitLea(X86Register.Rsi, safePointTableLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rdx, 8), Imm(safePointCount)));
                    EmitLea(X86Register.Rcx, typeInfoTableLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R8, 8), Imm(typeInfoCount)));
                    EmitLea(X86Register.R9, staticRootTableLabel);
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(16)));
                    EmitStoreImmediate(X86Register.Rsp, 0, staticRootCount, 8);
                    EmitCall(initialize);
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 8), Imm(16)));
                    if (entryTypeInitializer is not null)
                        EmitCall(GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType));
                    if (runtimeMethod.ParameterTypes.Length == 1)
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rdi, 4), Reg(X86Register.Rdi, 4)));
                    EmitCall(entryMethodLabel);
                    if (IsVoid(runtimeMethod.ReturnType))
                        Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rdi, 4), Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Imm(60)));
                    Emit(new X86Instruction(X86InstrKind.Syscall));
                    Emit(new X86Instruction(X86InstrKind.Ud2));
                }

                _symbols.Add(new X86ObjectSymbol(
                    label,
                    TextSectionName,
                    start,
                    _text.ByteLength - start,
                    X86ObjectSymbolBinding.Global,
                    X86ObjectSymbolKind.Function));
                return label;
            }

            public void Emit(X86Instruction instruction)
                => _text.Emit(instruction);

            public void DefineLabel(string label)
                => _text.DefineLabel(label);

            public void EmitCall(string symbol)
                => Emit(X86Instruction.Branch(
                    X86InstrKind.Call,
                    X86Operand.SymbolOperand(symbol, 4, X86ObjectRelocationKind.Relative32)));

            public X86Operand SymbolMemory(string symbol, long addend = 0, int size = 0)
            {
                int operandSize = size == 0 ? _target.PointerSize : size;
                return _machineTarget.Is64Bit
                    ? X86Operand.RipRelative(symbol, addend, operandSize)
                    : X86Operand.Memory(X86Register.Invalid, 0, operandSize, symbol: symbol, relocationKind: X86ObjectRelocationKind.Absolute32, addend: addend);
            }

            public void EmitLea(X86Register destination, string symbol, long addend = 0)
                => Emit(X86Instruction.Binary(
                    X86InstrKind.Lea,
                    Reg(destination, _target.PointerSize),
                    SymbolMemory(symbol, addend, _target.PointerSize)));

            private void EmitStoreImmediate(X86Register baseRegister, int offset, long value, int size)
            {
                if (value >= int.MinValue && value <= int.MaxValue)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(baseRegister, offset, size), Imm(value)));
                    return;
                }
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, size), Imm(value)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(baseRegister, offset, size), Reg(X86Register.Rax, size)));
            }

            public string CreateLocalLabel(string prefix)
            {
                string baseName = ".L" + SanitizeSymbolName(prefix);
                string candidate;
                do
                {
                    candidate = baseName + "_" + (++_nextLocalLabel).ToString();
                }
                while (!_usedLabels.Add(candidate));
                return candidate;
            }

            private string CreateUniqueGlobalLabel(string name)
            {
                string baseName = SanitizeSymbolName(name);
                if (baseName.Length == 0)
                    baseName = "symbol";
                string candidate = baseName;
                int suffix = 0;
                while (!_usedLabels.Add(candidate))
                    candidate = baseName + "_" + (++suffix).ToString();
                return candidate;
            }

            private void AddDataSymbol(string label, int offset, int size)
                => AddDataSymbol(label, RodataSectionName, offset, size);

            private void AddDataSymbol(string label, string section, int offset, int size)
                => _symbols.Add(new X86ObjectSymbol(
                    label,
                    section,
                    offset,
                    size,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Object));

            private static string FormatMethodSymbol(RuntimeMethod method)
            {
                var sb = new StringBuilder();
                sb.Append('M').Append(method.MethodId).Append('_');
                if (!string.IsNullOrEmpty(method.DeclaringType.Namespace))
                    sb.Append(method.DeclaringType.Namespace).Append('_');
                sb.Append(method.DeclaringType.Name).Append('_').Append(method.Name);
                return sb.ToString();
            }

            private static string SanitizeSymbolName(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return string.Empty;
                var sb = new StringBuilder(value.Length);
                foreach (char c in value)
                    sb.Append(char.IsLetterOrDigit(c) || c is '_' or '$' or '.' ? c : '_');
                return sb.ToString();
            }

            private sealed class MethodEmitter
            {
                private readonly Generator _owner;
                private readonly GenTreeMethod _method;
                private readonly string _methodLabel;
                private readonly string[] _blockLabels;
                private readonly string[] _blockEndLabels;
                private readonly Dictionary<int, int> _nodePositions;
                private readonly EhMethodDraft? _ehMethod;
                private readonly string _returnThunkLabel;
                private int _nextBlockId = -1;
                private bool _ehFrameRegistered;
                private bool _returnThunkNeeded;

                public MethodEmitter(Generator owner, GenTreeMethod method, string methodLabel)
                {
                    _owner = owner;
                    _method = method;
                    _methodLabel = methodLabel;
                    _nodePositions = BuildNodePositions(method);
                    _blockLabels = new string[method.Blocks.Length];
                    _blockEndLabels = new string[method.Blocks.Length];
                    for (int i = 0; i < method.Blocks.Length; i++)
                    {
                        _blockLabels[i] = owner.CreateLocalLabel(methodLabel + "_B" + i.ToString());
                        _blockEndLabels[i] = owner.CreateLocalLabel(methodLabel + "_B" + i.ToString() + "_end");
                    }
                    owner._ehMethodsByMethodId.TryGetValue(method.RuntimeMethod.MethodId, out _ehMethod);
                    _returnThunkLabel = owner.CreateLocalLabel(methodLabel + "_eh_return");
                }

                private TargetInfo Target => _owner._target;

                public void Emit()
                {
                    ValidateMethodShape();
                    ImmutableArray<int> order = _method.LinearBlockOrder;
                    for (int i = 0; i < order.Length; i++)
                    {
                        int blockId = order[i];
                        _nextBlockId = i + 1 < order.Length ? order[i + 1] : -1;
                        GenTreeBlock block = _method.Blocks[blockId];
                        int firstBodyNode = blockId == 0 ? GenTreeLirKinds.PrologPrefixLength(block.LinearNodes) : 0;
                        for (int n = 0; n < firstBodyNode; n++)
                            EmitNode(block.LinearNodes[n]);
                        _owner.DefineLabel(_blockLabels[blockId]);
                        for (int n = firstBodyNode; n < block.LinearNodes.Length; n++)
                            EmitNode(block.LinearNodes[n]);
                        EmitFallthroughFixup(blockId, _nextBlockId);
                        _owner.DefineLabel(_blockEndLabels[blockId]);
                    }
                    BindEhNativeRanges();
                    if (_returnThunkNeeded)
                        EmitReturnThunk();
                    if (_ehMethod is not null && !_ehFrameRegistered)
                        throw new InvalidOperationException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' has EH metadata but no registered establisher frame.");
                    _nextBlockId = -1;
                }

                private void ValidateMethodShape()
                {
                    if (Target.Is32Bit)
                    {
                        if (_ehMethod is not null)
                            throw new NotSupportedException($"Exception handling is not supported by the minimal i386 backend for method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}'.");

                        for (int i = 0; i < _method.LinearNodes.Length; i++)
                        {
                            GenTree node = _method.LinearNodes[i];
                            if (ValueStackKind(node) == GenStackKind.I8 || node.TreeKind == GenTreeKind.ConstI8)
                                throw Unsupported(node, "64-bit integer values are not supported by the minimal i386 backend");
                            if (node.TreeKind is GenTreeKind.NewDelegate or GenTreeKind.DelegateInvoke or GenTreeKind.DelegateCombine or GenTreeKind.DelegateRemove)
                                throw Unsupported(node, "delegates are not supported by the minimal i386 backend");
                            if (node.TreeKind is GenTreeKind.NewArray or GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement or GenTreeKind.ArrayDataRef)
                                throw Unsupported(node, "managed arrays are not supported by the minimal i386 backend");
                            if (node.TreeKind is GenTreeKind.CastClass or GenTreeKind.IsInst or GenTreeKind.Box or GenTreeKind.UnboxAny)
                                throw Unsupported(node, "runtime type operations are not supported by the minimal i386 backend");
                        }
                    }

                    if (!MethodHasGcSafePoint())
                        return;
                    if (!_method.StackFrame.UsesFramePointer)
                    {
                        throw new InvalidOperationException(
                            $"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' must use a frame pointer for precise GC stack walking.");
                    }
                    if (!_method.StackFrame.TryGetCalleeSavedSlot(RegisterInfo.FramePointer(Target), out _))
                    {
                        throw new InvalidOperationException(
                            $"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' has no saved frame-pointer slot for precise GC stack walking.");
                    }
                }

                private bool MethodHasGcSafePoint()
                {
                    for (int i = 0; i < _method.LinearNodes.Length; i++)
                    {
                        GenTree node = _method.LinearNodes[i];
                        if (node.TreeKind is
                            GenTreeKind.ClassInit or
                            GenTreeKind.GcPoll or
                            GenTreeKind.NewObject or
                            GenTreeKind.NewArray or
                            GenTreeKind.NewDelegate or
                            GenTreeKind.DelegateCombine or
                            GenTreeKind.DelegateRemove or
                            GenTreeKind.Box)
                            return true;
                        if (node.TreeKind is GenTreeKind.DelegateInvoke or GenTreeKind.IndirectCall or GenTreeKind.VirtualCall)
                            return true;
                        if (node.TreeKind == GenTreeKind.Call &&
                            (node.Method?.HasInternalCall != true ||
                             (node.Method is not null && X86Runtime.IsGcSafePointInternalCall(node.Method))))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                private static Dictionary<int, int> BuildNodePositions(GenTreeMethod method)
                {
                    var result = new Dictionary<int, int>();
                    int position = 0;
                    ImmutableArray<int> order = method.LinearBlockOrder.IsDefaultOrEmpty
                        ? LinearBlockOrder.Compute(method.Cfg)
                        : method.LinearBlockOrder;

                    for (int o = 0; o < order.Length; o++)
                    {
                        ImmutableArray<GenTree> nodes = method.Blocks[order[o]].LinearNodes;
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            GenTree node = nodes[i];
                            result[node.LinearId] = position;
                            if (node.IsPhiCopy)
                            {
                                while (i + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[i + 1]))
                                {
                                    i++;
                                    result[nodes[i].LinearId] = position;
                                }
                            }
                            position += 2;
                        }
                        position += 2;
                    }
                    return result;
                }

                private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                    => left.IsPhiCopy &&
                       right.IsPhiCopy &&
                       left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                       left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

                private SafePointDraft PrepareSafePoint(
                    GenTree node,
                    RegisterOperand additionalRoot = default,
                    int dynamicStackAdjustment = 0)
                {
                    if (!_nodePositions.TryGetValue(node.LinearId, out int position))
                        throw Unsupported(node, "GC safe point has no final LIR position");
                    if (!_method.StackFrame.UsesFramePointer)
                        throw Unsupported(node, "GC safe point requires a frame pointer");
                    if (!_method.StackFrame.TryGetCalleeSavedSlot(RegisterInfo.FramePointer(Target), out StackFrameSlot savedFramePointer))
                        throw Unsupported(node, "GC safe point requires a saved frame-pointer slot");

                    var liveRoots = new List<RegisterGcLiveRoot>();
                    for (int i = 0; i < _method.GcLiveRanges.Length; i++)
                    {
                        RegisterGcLiveRange range = _method.GcLiveRanges[i];
                        if (range.FuncletIndex != 0 || range.StartPosition > position || position >= range.EndPosition)
                            continue;
                        if (!ContainsRootCell(liveRoots, range.Root))
                            liveRoots.Add(range.Root);
                    }

                    int rootCount = checked(liveRoots.Count + (additionalRoot.IsNone ? 0 : 1));
                    if (rootCount > _method.StackFrame.GcRootSpillSlotCount)
                    {
                        throw Unsupported(
                            node,
                            $"GC spill area has {_method.StackFrame.GcRootSpillSlotCount} slots but {rootCount} roots are live");
                    }

                    var roots = ImmutableArray.CreateBuilder<SafePointRootDraft>(rootCount);
                    int rootIndex = 0;
                    for (int i = 0; i < liveRoots.Count; i++)
                    {
                        SpillGcRoot(liveRoots[i], rootIndex, dynamicStackAdjustment);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize),
                            liveRoots[i].RootKind));
                        rootIndex++;
                    }

                    if (!additionalRoot.IsNone)
                    {
                        SpillGcRoot(
                            additionalRoot,
                            RegisterGcRootKind.ObjectReference,
                            0,
                            rootIndex,
                            dynamicStackAdjustment);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize),
                            RegisterGcRootKind.ObjectReference));
                    }

                    string returnLabel = _owner.CreateLocalLabel(_methodLabel + "_gc_return");
                    return _owner.AddSafePoint(
                        _methodLabel,
                        returnLabel,
                        savedFramePointer.Offset,
                        _method.StackFrame.FrameSize,
                        roots.ToImmutable());
                }

                private static bool ContainsRootCell(List<RegisterGcLiveRoot> roots, RegisterGcLiveRoot candidate)
                {
                    for (int i = 0; i < roots.Count; i++)
                    {
                        RegisterGcLiveRoot root = roots[i];
                        if (root.RootKind == candidate.RootKind &&
                            root.Offset == candidate.Offset &&
                            root.Location.Equals(candidate.Location))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                private void SpillGcRoot(RegisterGcLiveRoot root, int rootIndex, int dynamicStackAdjustment)
                    => SpillGcRoot(root.Location, root.RootKind, root.Offset, rootIndex, dynamicStackAdjustment);

                private void SpillGcRoot(
                    RegisterOperand location,
                    RegisterGcRootKind rootKind,
                    int cellOffset,
                    int rootIndex,
                    int dynamicStackAdjustment)
                {
                    int spillOffset = checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize);
                    if (location.IsRegister)
                    {
                        if (cellOffset != 0)
                            throw new InvalidOperationException("A register GC root cannot have a non-zero cell offset.");
                        if (location.RegisterClass != RegisterClass.General)
                            throw new InvalidOperationException("A GC root cannot reside in a floating-point register.");
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rbp, spillOffset, Target.PointerSize),
                            Reg(ToX86Register(location.Register, Target), Target.PointerSize)));
                        return;
                    }
                    if (!location.IsFrameSlot)
                        throw new InvalidOperationException("GC root location is not final: " + location);

                    X86Register frameBase = FrameBase(location);
                    int sourceOffset = checked(EffectiveFrameOffset(location) + cellOffset);
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    if (frameBase == X86Register.Rsp)
                    {
                        sourceOffset = checked(
                            sourceOffset + dynamicStackAdjustment + Target.PointerSize);
                    }
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(frameBase, sourceOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rbp, spillOffset, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                }

                private int TypeOperationScratchOffset
                    => AlignUp(
                        checked(_method.StackFrame.GcSpillAreaOffset +
                                _method.StackFrame.GcRootSpillSlotCount * Target.PointerSize),
                        TypeOperationScratchAlignment);

                private int TypeOperationScratchSize
                {
                    get
                    {
                        int size = 0;
                        for (int i = 0; i < _method.LinearNodes.Length; i++)
                        {
                            GenTree node = _method.LinearNodes[i];
                            RuntimeType? type = TypeOperationScratchType(node);
                            if (type?.IsValueType == true)
                                size = Math.Max(size, Math.Max(1, type.SizeOf));
                            if (node.TreeKind == GenTreeKind.NewDelegate && node.Uses.Length != 0)
                                size = Math.Max(size, Target.PointerSize);
                            if (node.TreeKind is GenTreeKind.DelegateCombine or GenTreeKind.DelegateRemove)
                                size = Math.Max(size, checked(Target.PointerSize * 3));
                        }
                        return size == 0 ? 0 : AlignUp(size, TypeOperationScratchAlignment);
                    }
                }

                private int TypeOperationScratchAlignment
                {
                    get
                    {
                        int alignment = Target.PointerSize;
                        for (int i = 0; i < _method.LinearNodes.Length; i++)
                        {
                            RuntimeType? type = TypeOperationScratchType(_method.LinearNodes[i]);
                            if (type?.IsValueType == true)
                                alignment = Math.Max(alignment, Math.Max(1, type.AlignOf));
                        }
                        return alignment;
                    }
                }

                private RuntimeType? TypeOperationScratchType(GenTree node)
                {
                    if (node.TreeKind is not (GenTreeKind.Box or GenTreeKind.UnboxAny))
                        return null;
                    if (node.TreeKind == GenTreeKind.Box && !node.RegisterUses.IsDefaultOrEmpty)
                        return _method.GetValueInfo(node.RegisterUses[0]).Type ?? node.RuntimeType ?? node.Type;
                    return node.RuntimeType ?? node.Type;
                }

                private int NewObjectArgumentSaveAlignment
                {
                    get
                    {
                        int alignment = Target.PointerSize;
                        for (int i = 1; i < 32; i++)
                        {
                            MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                            if (register == MachineRegister.Invalid)
                                break;
                            alignment = Math.Max(alignment, RegisterInfo.RegisterSaveAlignment(Target, register));
                        }
                        for (int i = 0; i < 32; i++)
                        {
                            MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                            if (register == MachineRegister.Invalid)
                                break;
                            alignment = Math.Max(alignment, RegisterInfo.RegisterSaveAlignment(Target, register));
                        }
                        return alignment;
                    }
                }

                private int NewObjectArgumentSaveOffset
                    => AlignUp(
                        checked(TypeOperationScratchOffset + TypeOperationScratchSize),
                        NewObjectArgumentSaveAlignment);

                private void SaveNewObjectArguments()
                {
                    int offset = NewObjectArgumentSaveOffset;
                    for (int i = 1; i < 32; i++)
                    {
                        MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, alignment);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rbp, offset, size),
                            Reg(ToX86Register(register, Target), size)));
                        offset = checked(offset + size);
                    }
                    for (int i = 0; i < 32; i++)
                    {
                        MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, alignment);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Movdqu,
                            Mem(X86Register.Rbp, offset, size),
                            Reg(ToX86Register(register, Target), size)));
                        offset = checked(offset + size);
                    }
                    if (offset > _method.StackFrame.GcSpillAreaOffset + _method.StackFrame.GcSpillAreaSize)
                        throw new InvalidOperationException("GC transition area is smaller than the x86 argument register save set.");
                }

                private void RestoreNewObjectArguments()
                {
                    int offset = NewObjectArgumentSaveOffset;
                    for (int i = 1; i < 32; i++)
                    {
                        MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, alignment);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(ToX86Register(register, Target), size),
                            Mem(X86Register.Rbp, offset, size)));
                        offset = checked(offset + size);
                    }
                    for (int i = 0; i < 32; i++)
                    {
                        MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int alignment = RegisterInfo.RegisterSaveAlignment(Target, register);
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, alignment);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Movdqu,
                            Reg(ToX86Register(register, Target), size),
                            Mem(X86Register.Rbp, offset, size)));
                        offset = checked(offset + size);
                    }
                }

                private void PublishGcTransition(SafePointDraft safePoint)
                {
                    string currentSafePoint = _owner.ResolveExternalObject(X86Runtime.CurrentSafePointSymbol);
                    string currentFramePointer = _owner.ResolveExternalObject(X86Runtime.CurrentFramePointerSymbol);
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.EmitLea(X86Register.R10, safePoint.DescriptorLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        _owner.SymbolMemory(currentSafePoint, size: Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        _owner.SymbolMemory(currentFramePointer, size: Target.PointerSize),
                        Reg(X86Register.Rbp, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                }

                private void EmitGcPoll(GenTree node)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    MarkEhCallSite(node, "gc_poll");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.GcPollSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void BindEhNativeRanges()
                {
                    if (_ehMethod is null)
                        return;

                    for (int i = 0; i < _ehMethod.Clauses.Length; i++)
                    {
                        EhClauseDraft clause = _ehMethod.Clauses[i];
                        BindEhNativeRange(
                            EhFuncletLayout.BuildTryBlockIds(_method.Cfg, clause.Region),
                            clause.Region.TryStartBlockId,
                            clause.Region.TryEndBlockIdExclusive,
                            "EH try",
                            out string tryStart,
                            out string tryEnd);
                        BindEhNativeRange(
                            EhFuncletLayout.BuildFuncletBlockIds(_method.Cfg, clause.Region),
                            clause.Region.HandlerStartBlockId,
                            clause.Region.HandlerEndBlockIdExclusive,
                            "EH handler",
                            out string handlerStart,
                            out string handlerEnd);
                        clause.TryStartLabel = tryStart;
                        clause.TryEndLabel = tryEnd;
                        clause.HandlerStartLabel = handlerStart;
                        clause.HandlerEndLabel = handlerEnd;
                    }
                }

                private void BindEhNativeRange(
                    ImmutableArray<int> blocks,
                    int fallbackStartBlockId,
                    int fallbackEndBlockIdExclusive,
                    string rangeName,
                    out string startLabel,
                    out string endLabel)
                {
                    if (blocks.Length != 0)
                    {
                        EnsureContiguousNativeBlocks(blocks, rangeName);
                        startLabel = BlockStartLabel(blocks[0]);
                        endLabel = BlockEndLabel(blocks[blocks.Length - 1]);
                        return;
                    }

                    startLabel = BlockStartLabel(fallbackStartBlockId);
                    int lastBlockId = fallbackEndBlockIdExclusive - 1;
                    if (lastBlockId < fallbackStartBlockId)
                        lastBlockId = fallbackStartBlockId;
                    endLabel = BlockEndLabel(lastBlockId);
                }

                private void EnsureContiguousNativeBlocks(ImmutableArray<int> blocks, string rangeName)
                {
                    if (blocks.Length <= 1)
                        return;

                    var members = new HashSet<int>();
                    for (int i = 0; i < blocks.Length; i++)
                        members.Add(blocks[i]);

                    int firstIndex = -1;
                    int lastIndex = -1;
                    ImmutableArray<int> order = _method.LinearBlockOrder;
                    for (int i = 0; i < order.Length; i++)
                    {
                        if (!members.Contains(order[i]))
                            continue;
                        if (firstIndex < 0)
                            firstIndex = i;
                        lastIndex = i;
                    }

                    if (firstIndex < 0 || lastIndex < firstIndex)
                        throw new InvalidOperationException(rangeName + " range contains blocks that were not emitted.");

                    for (int i = firstIndex; i <= lastIndex; i++)
                    {
                        if (!members.Contains(order[i]))
                            throw new InvalidOperationException(rangeName + " native range is not contiguous in funclet layout.");
                    }
                }

                private string BlockStartLabel(int blockId)
                {
                    if ((uint)blockId >= (uint)_blockLabels.Length)
                        throw new InvalidOperationException($"Block B{blockId} was not emitted.");
                    return _blockLabels[blockId];
                }

                private string BlockEndLabel(int blockId)
                {
                    if ((uint)blockId >= (uint)_blockEndLabels.Length)
                        throw new InvalidOperationException($"Block B{blockId} was not emitted.");
                    return _blockEndLabels[blockId];
                }

                private void EmitEhFramePush(string currentIpLabel)
                {
                    if (_ehMethod is null)
                        return;

                    string countSymbol = _owner.ResolveExternalObject(X86Runtime.EhFrameCountSymbol);
                    string framesSymbol = _owner.ResolveExternalObject(X86Runtime.EhFramesSymbol);
                    string capacityAvailable = _owner.CreateLocalLabel(_methodLabel + "_eh_frame_capacity");

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        _owner.SymbolMemory(countSymbol, size: 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R10, 8), Imm(4096)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.B,
                        X86Operand.SymbolOperand(capacityAvailable, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                    EmitRuntimeArgument(0, 150);
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.FailFastSymbol));
                    EmitUnreachableTrap();

                    _owner.DefineLabel(capacityAvailable);
                    EmitEhFrameAddress(X86Register.R10, framesSymbol, X86Register.R11);
                    _owner.EmitLea(X86Register.R10, _ehMethod.InfoLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R11, 0, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R11, 8, 8), Reg(X86Register.Rbp, Target.PointerSize)));
                    _owner.EmitLea(X86Register.R10, currentIpLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R11, 16, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        _owner.SymbolMemory(countSymbol, size: 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Inc, Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        _owner.SymbolMemory(countSymbol, size: 8),
                        Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                    _ehFrameRegistered = true;
                }

                private void EmitEhFrameAddress(X86Register frameIndex, string framesSymbol, X86Register destination)
                {
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(destination, 8),
                        X86Operand.Memory(frameIndex, 0, 8, frameIndex, 2)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shl, Reg(destination, 8), Imm(3)));
                    _owner.EmitLea(X86Register.R10, framesSymbol);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(destination, 8), Reg(X86Register.R10, 8)));
                }

                private void MarkEhCallSite(GenTree node, string suffix)
                {
                    if (_ehMethod is null)
                        return;

                    string label = _owner.CreateLocalLabel($"{_methodLabel}_eh_{suffix}_{node.LinearId}");
                    _owner.DefineLabel(label);
                    EmitEhSetCurrentIpAndSaveContext(label);
                }

                private void EmitEhSetCurrentIpAndSaveContext(string currentIpLabel)
                {
                    if (!_ehFrameRegistered)
                        throw new InvalidOperationException($"Method M{_method.RuntimeMethod.MethodId} updates EH state before establishing its frame.");

                    string countSymbol = _owner.ResolveExternalObject(X86Runtime.EhFrameCountSymbol);
                    string framesSymbol = _owner.ResolveExternalObject(X86Runtime.EhFramesSymbol);
                    string contextsSymbol = _owner.ResolveExternalObject(X86Runtime.EhRegisterContextsSymbol);

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R11, Target.PointerSize)));

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        _owner.SymbolMemory(countSymbol, size: 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Dec, Reg(X86Register.R10, 8)));
                    EmitEhFrameAddress(X86Register.R10, framesSymbol, X86Register.R11);
                    _owner.EmitLea(X86Register.R10, currentIpLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R11, 16, 8), Reg(X86Register.R10, 8)));

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        _owner.SymbolMemory(countSymbol, size: 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Dec, Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shl, Reg(X86Register.R10, 8), Imm(9)));
                    _owner.EmitLea(X86Register.R11, contextsSymbol);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.R10, 8), Reg(X86Register.R11, 8)));

                    ImmutableArray<MachineRegister> generalRegisters = RegisterInfo.AllocatableGeneralRegisters(Target);
                    for (int i = 0; i < generalRegisters.Length; i++)
                    {
                        MachineRegister register = generalRegisters[i];
                        X86Register physical = ToX86Register(register, Target);
                        if (physical == X86Register.R10)
                        {
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R11, 8), Mem(X86Register.Rsp, 8, 8)));
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R10, (byte)register * 8, 8), Reg(X86Register.R11, 8)));
                        }
                        else if (physical == X86Register.R11)
                        {
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R11, 8), Mem(X86Register.Rsp, 0, 8)));
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.R10, (byte)register * 8, 8), Reg(X86Register.R11, 8)));
                        }
                        else
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Mem(X86Register.R10, (byte)register * 8, 8),
                                Reg(physical, 8)));
                        }
                    }

                    ImmutableArray<MachineRegister> floatingRegisters = RegisterInfo.AllocatableFloatingRegisters(Target);
                    for (int i = 0; i < floatingRegisters.Length; i++)
                    {
                        MachineRegister register = floatingRegisters[i];
                        int registerIndex = (byte)register - (byte)MachineRegister.F0;
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Movdqu,
                            Mem(X86Register.R10, 256 + registerIndex * 16, 16),
                            Reg(ToX86Register(register, Target), 16)));
                    }

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                }

                private void EmitEhFramePop()
                {
                    if (_ehMethod is null)
                        return;

                    string countSymbol = _owner.ResolveExternalObject(X86Runtime.EhFrameCountSymbol);
                    _owner.Emit(X86Instruction.Unary(
                        X86InstrKind.Dec,
                        _owner.SymbolMemory(countSymbol, size: 8)));
                }

                private void EmitExceptionObject(GenTree node)
                {
                    X86Register destination = ToX86Register(RequireResultRegister(node), Target);
                    string currentException = _owner.ResolveExternalObject(X86Runtime.CurrentExceptionSymbol);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(destination, Target.PointerSize),
                        _owner.SymbolMemory(currentException, size: Target.PointerSize)));
                }

                private void EmitThrow(GenTree node)
                {
                    MachineRegister exception = RequireUseRegister(node, 0);
                    MarkEhCallSite(node, "throw");
                    X86Register argument = RuntimeArgumentRegister(0);
                    X86Register source = ToX86Register(exception, Target);
                    if (argument != source)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(argument, Target.PointerSize), Reg(source, Target.PointerSize)));

                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_throw_non_null_{node.LinearId}");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(argument, Target.PointerSize), Reg(argument, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(nonNull, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.EmitLea(argument, _owner.GetStaticExceptionObjectLabel("System", "NullReferenceException"));
                    _owner.DefineLabel(nonNull);
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.ThrowSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitRethrow(GenTree node)
                {
                    MarkEhCallSite(node, "rethrow");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.RethrowSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitLeave(GenTree node)
                {
                    MarkEhCallSite(node, "leave");
                    _owner.EmitLea(RuntimeArgumentRegister(0), LabelForTarget(node));
                    EmitRuntimeArgument(1, 1);
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.LeaveSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitEndFinally(GenTree node)
                {
                    MarkEhCallSite(node, "endfinally");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.EndFinallySymbol));
                    EmitUnreachableTrap();
                }

                private void EmitRuntimeArgument(int index, long value)
                    => _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(RuntimeArgumentRegister(index), Target.PointerSize),
                        Imm(value)));

                private X86Register RuntimeArgumentRegister(int index)
                    => _owner.AbiArgumentRegister(index);

                private void EmitUnreachableTrap()
                    => _owner.Emit(new X86Instruction(X86InstrKind.Ud2));

                private void EmitReturn(GenTree node)
                {
                    if (_ehMethod is not null &&
                        (FuncletIndexForBlock(node.BlockId) != 0 || ReturnMustRunFinallyBeforeMethodExit(node.BlockId)))
                    {
                        EmitReturnThroughEh(node);
                        return;
                    }

                    EmitEhFramePop();
                    _owner.Emit(X86Instruction.Ret());
                }

                private void EmitReturnThroughEh(GenTree node)
                {
                    _returnThunkNeeded = true;
                    MarkEhCallSite(node, "return");
                    _owner.EmitLea(RuntimeArgumentRegister(0), _returnThunkLabel);
                    EmitRuntimeArgument(1, 2);
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.LeaveSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitReturnThunk()
                {
                    _owner.DefineLabel(_returnThunkLabel);
                    EmitEhFramePop();
                    if (_method.StackFrame.UsesFramePointer)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rsp, Target.PointerSize), Reg(X86Register.Rbp, Target.PointerSize)));

                    for (int i = _method.StackFrame.CalleeSavedSlots.Length - 1; i >= 0; i--)
                    {
                        StackFrameSlot slot = _method.StackFrame.CalleeSavedSlots[i];
                        X86Register register = ToX86Register(slot.SavedRegister, Target);
                        if (MachineRegisters.GetClass(slot.SavedRegister) == RegisterClass.Float)
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Movdqu,
                                Reg(register, 16),
                                Mem(X86Register.Rsp, slot.Offset, 16)));
                        }
                        else
                        {
                            int size = slot.Size > 0 ? slot.Size : RegisterInfo.RegisterSaveSize(Target, slot.SavedRegister);
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Reg(register, size),
                                Mem(X86Register.Rsp, slot.Offset, size)));
                        }
                    }

                    if (_method.StackFrame.FrameSize > 0)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, Target.PointerSize), Imm(_method.StackFrame.FrameSize)));
                    _owner.Emit(X86Instruction.Ret());
                }

                private int FuncletIndexForBlock(int blockId)
                {
                    for (int i = 0; i < _method.Funclets.Length; i++)
                    {
                        RegisterFunclet funclet = _method.Funclets[i];
                        for (int b = 0; b < funclet.BlockIds.Length; b++)
                        {
                            if (funclet.BlockIds[b] == blockId)
                                return funclet.Index;
                        }
                    }
                    return 0;
                }

                private bool ReturnMustRunFinallyBeforeMethodExit(int blockId)
                {
                    ImmutableArray<CfgExceptionRegion> regions = _method.Cfg.ExceptionRegions;
                    for (int i = 0; i < regions.Length; i++)
                    {
                        CfgExceptionRegion region = regions[i];
                        if (region.Kind == CfgExceptionRegionKind.Finally &&
                            blockId >= region.TryStartBlockId &&
                            blockId < region.TryEndBlockIdExclusive)
                        {
                            return true;
                        }
                    }
                    return false;
                }

                private void EmitNode(GenTree node)
                {
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.Nop:
                        case GenTreeKind.Eval:
                            return;
                        case GenTreeKind.Copy:
                        case GenTreeKind.Reload:
                        case GenTreeKind.Spill:
                            EmitMove(node);
                            return;
                        case GenTreeKind.StackFrameOp:
                            EmitFrameOperation(node);
                            return;
                        case GenTreeKind.ConstI4:
                            EmitIntegerConstant(RequireResultRegister(node), node.Int32, 4);
                            return;
                        case GenTreeKind.ConstI8:
                            EmitIntegerConstant(RequireResultRegister(node), node.Int64, 8);
                            return;
                        case GenTreeKind.ConstR4Bits:
                            EmitFloatConstant(node, 4, BitConverter.GetBytes(node.Int32));
                            return;
                        case GenTreeKind.ConstR8Bits:
                            EmitFloatConstant(node, 8, BitConverter.GetBytes(node.Int64));
                            return;
                        case GenTreeKind.ConstNull:
                            EmitZeroRegister(RequireResultRegister(node));
                            return;
                        case GenTreeKind.ConstString:
                            _owner.EmitLea(
                                ToX86Register(RequireResultRegister(node), Target),
                                _owner.GetStringLiteralLabel(RequireRuntimeType(node), node.Text ?? string.Empty));
                            return;
                        case GenTreeKind.DefaultValue:
                            EmitDefaultValue(node);
                            return;
                        case GenTreeKind.SizeOf:
                            EmitIntegerConstant(RequireResultRegister(node), RequireRuntimeType(node).SizeOf, 4);
                            return;
                        case GenTreeKind.Local:
                        case GenTreeKind.Arg:
                        case GenTreeKind.Temp:
                        case GenTreeKind.StoreLocal:
                        case GenTreeKind.StoreArg:
                        case GenTreeKind.StoreTemp:
                            EmitLocalLike(node);
                            return;
                        case GenTreeKind.LocalAddr:
                        case GenTreeKind.ArgAddr:
                        case GenTreeKind.TempAddr:
                            EmitAddressTree(node);
                            return;
                        case GenTreeKind.FunctionPointer:
                            EmitFunctionPointer(node);
                            return;
                        case GenTreeKind.Field:
                        case GenTreeKind.FieldAddr:
                        case GenTreeKind.StoreField:
                            EmitField(node);
                            return;
                        case GenTreeKind.StaticField:
                        case GenTreeKind.StaticFieldAddr:
                        case GenTreeKind.StoreStaticField:
                            EmitStaticField(node);
                            return;
                        case GenTreeKind.ArrayElement:
                        case GenTreeKind.ArrayElementAddr:
                        case GenTreeKind.StoreArrayElement:
                        case GenTreeKind.ArrayDataRef:
                            EmitArray(node);
                            return;
                        case GenTreeKind.Unary:
                            EmitUnary(node);
                            return;
                        case GenTreeKind.Binary:
                            EmitBinary(node);
                            return;
                        case GenTreeKind.Conv:
                        case GenTreeKind.PointerToByRef:
                            EmitSimpleConversion(node);
                            return;
                        case GenTreeKind.PointerElementAddr:
                            EmitPointerElementAddress(node);
                            return;
                        case GenTreeKind.LoadIndirect:
                        case GenTreeKind.StoreIndirect:
                            EmitIndirect(node);
                            return;
                        case GenTreeKind.Branch:
                            if (node.SourceOp == BytecodeOp.Leave && _ehMethod is not null)
                                EmitLeave(node);
                            else
                                EmitJump(LabelForTarget(node));
                            return;
                        case GenTreeKind.BranchTrue:
                        case GenTreeKind.BranchFalse:
                            EmitConditionalBranch(node);
                            return;
                        case GenTreeKind.Call:
                            EmitCall(node);
                            return;
                        case GenTreeKind.IndirectCall:
                            EmitIndirectCall(node);
                            return;
                        case GenTreeKind.VirtualCall:
                            EmitVirtualCall(node);
                            return;
                        case GenTreeKind.GcPoll:
                            EmitGcPoll(node);
                            return;
                        case GenTreeKind.ClassInit:
                            EmitClassInit(node);
                            return;
                        case GenTreeKind.NewObject:
                            EmitNewObject(node);
                            return;
                        case GenTreeKind.NewArray:
                            EmitNewArray(node);
                            return;
                        case GenTreeKind.Box:
                            EmitBox(node);
                            return;
                        case GenTreeKind.ExceptionObject:
                            EmitExceptionObject(node);
                            return;
                        case GenTreeKind.Throw:
                            EmitThrow(node);
                            return;
                        case GenTreeKind.Rethrow:
                            EmitRethrow(node);
                            return;
                        case GenTreeKind.EndFinally:
                            EmitEndFinally(node);
                            return;
                        case GenTreeKind.Return:
                            EmitReturn(node);
                            return;
                        case GenTreeKind.StackAlloc:
                            EmitStackAlloc(node);
                            return;
                        case GenTreeKind.AllocHGlobal:
                            EmitAllocHGlobal(node);
                            return;
                        case GenTreeKind.FreeHGlobal:
                            EmitFreeHGlobal(node);
                            return;
                        case GenTreeKind.CastClass:
                            EmitRuntimeTypeCheck(node, throwOnFailure: true);
                            return;
                        case GenTreeKind.IsInst:
                            EmitRuntimeTypeCheck(node, throwOnFailure: false);
                            return;
                        case GenTreeKind.NewDelegate:
                            EmitNewDelegate(node);
                            return;
                        case GenTreeKind.DelegateInvoke:
                            EmitDelegateInvoke(node);
                            return;
                        case GenTreeKind.DelegateRemove:
                            EmitDelegateCombineOrRemove(node, remove: true);
                            return;
                        case GenTreeKind.DelegateCombine:
                            EmitDelegateCombineOrRemove(node, remove: false);
                            return;
                        default:
                            throw Unsupported(node, "tree kind is not implemented by the x86 backend");
                    }
                }

                private void EmitClassInit(GenTree node)
                {
                    RuntimeType type = node.RuntimeType ?? throw Unsupported(node, "ClassInit node has no runtime type");
                    string initialized = _owner.CreateLocalLabel($"{_methodLabel}_type_init_initialized");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_type_init_done");

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, 4),
                        _owner.SymbolMemory(_owner.GetTypeInitializationStateLabel(type), size: 4)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(X86Register.R11, 4),
                        Reg(X86Register.R11, 4)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(initialized, 4, X86ObjectRelocationKind.Relative32)));

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "class_init");
                    _owner.EmitCall(_owner.GetTypeInitializationThunkLabel(type));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    EmitJump(done);

                    _owner.DefineLabel(initialized);
                    _owner.DefineLabel(done);
                }

                private void EmitStackAlloc(GenTree node)
                {
                    if (node.Int32 <= 0)
                        throw Unsupported(node, "Stack allocation element size must be positive");

                    X86Register destination = ToX86Register(RequireResultRegister(node), Target);
                    X86Register count = ToX86Register(RequireUseRegister(node, 0), Target);
                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_stackalloc_nonnegative");

                    if (Target.Is32Bit)
                    {
                        if (destination != count)
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, 4), Reg(count, 4)));
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Movsxd,
                            Reg(destination, 8),
                            Reg(count, 4)));
                    }
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(destination, Target.PointerSize),
                        Reg(destination, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ns,
                        X86Operand.SymbolOperand(nonNegative, 4, X86ObjectRelocationKind.Relative32)));
                    EmitStackAllocFailure();
                    _owner.DefineLabel(nonNegative);

                    if (node.Int32 != 1)
                    {
                        _owner.Emit(X86Instruction.Ternary(
                            X86InstrKind.Imul,
                            Reg(destination, Target.PointerSize),
                            Reg(destination, Target.PointerSize),
                            Imm(node.Int32)));
                    }

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Add,
                        Reg(destination, Target.PointerSize),
                        Imm(Target.CallFrameAlignment - 1)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.And,
                        Reg(destination, Target.PointerSize),
                        Imm(-Target.CallFrameAlignment)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Sub,
                        Reg(X86Register.Rsp, Target.PointerSize),
                        Reg(destination, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(destination, Target.PointerSize),
                        Reg(X86Register.Rsp, Target.PointerSize)));
                }

                private void EmitStackAllocFailure()
                {
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(RuntimeArgumentRegister(0), Target.PointerSize),
                        Imm(134)));
                    if (RegisterInfo.IsWindowsX64(Target))
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Sub,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(32)));
                    }
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.FailFastSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitAllocHGlobal(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 1)
                        throw Unsupported(node, "AllocHGlobal requires one size operand and one result");

                    X86Register size = ToX86Register(RequireUseRegister(node, 0), Target);
                    X86Register result = ToX86Register(RequireResultRegister(node), Target);
                    X86Register argument = RuntimeArgumentRegister(0);
                    if (argument != size)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(argument, Target.PointerSize),
                            Reg(size, Target.PointerSize)));
                    }

                    MarkEhCallSite(node, "alloc_hglobal");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.AllocHGlobalSymbol));
                    if (result != X86Register.Rax)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(result, Target.PointerSize),
                            Reg(X86Register.Rax, Target.PointerSize)));
                    }
                }

                private void EmitFreeHGlobal(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 0)
                        throw Unsupported(node, "FreeHGlobal requires one pointer operand and no result");

                    X86Register pointer = ToX86Register(RequireUseRegister(node, 0), Target);
                    X86Register argument = RuntimeArgumentRegister(0);
                    if (argument != pointer)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(argument, Target.PointerSize),
                            Reg(pointer, Target.PointerSize)));
                    }

                    MarkEhCallSite(node, "free_hglobal");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.FreeHGlobalSymbol));
                }

                private void EmitPointerElementAddress(GenTree node)
                {
                    if (node.Uses.Length != 2 || node.Results.Length != 1)
                        throw Unsupported(node, "Pointer element address requires base and index operands and one result");
                    if (node.Int32 <= 0)
                        throw Unsupported(node, "Pointer element size must be positive");

                    int baseUseIndex = RequireCodegenUseIndexForOperand(node, 0, "pointer base");
                    int indexUseIndex = RequireCodegenUseIndexForOperand(node, 1, "pointer index");
                    if (!node.Uses[baseUseIndex].IsRegister || !node.Uses[indexUseIndex].IsRegister)
                        throw Unsupported(node, "Pointer element address requires register operands");

                    X86Register destination = ToX86Register(RequireResultRegister(node), Target);
                    X86Register baseRegister = ToX86Register(node.Uses[baseUseIndex].Register, Target);
                    X86Register indexRegister = ToX86Register(node.Uses[indexUseIndex].Register, Target);
                    X86Register scaledIndex = ToX86Register(RequireInternalGeneralRegister(node, 0), Target);
                    RuntimeType? indexType = OperandType(node, 1);
                    GenStackKind indexKind = OperandStackKind(node, 1);

                    if (indexKind == GenStackKind.I4)
                    {
                        bool unsignedIndex = IsUnsigned(indexType);
                        if (Target.Is32Bit || unsignedIndex)
                        {
                            if (scaledIndex != indexRegister)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(scaledIndex, 4), Reg(indexRegister, 4)));
                        }
                        else
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Movsxd,
                                Reg(scaledIndex, 8),
                                Reg(indexRegister, 4)));
                        }
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(scaledIndex, Target.PointerSize),
                            Reg(indexRegister, Target.PointerSize)));
                    }

                    int scale = node.Int32;
                    if (scale is 1 or 2 or 4 or 8)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Lea,
                            Reg(destination, Target.PointerSize),
                            X86Operand.Memory(baseRegister, 0, Target.PointerSize, scaledIndex, scale)));
                        return;
                    }

                    _owner.Emit(X86Instruction.Ternary(
                        X86InstrKind.Imul,
                        Reg(scaledIndex, Target.PointerSize),
                        Reg(scaledIndex, Target.PointerSize),
                        Imm(scale)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(destination, Target.PointerSize),
                        X86Operand.Memory(baseRegister, 0, Target.PointerSize, scaledIndex, 1)));
                }

                private void EmitRuntimeTypeCheck(GenTree node, bool throwOnFailure)
                {
                    RuntimeType targetType = RequireRuntimeType(node);
                    if (throwOnFailure && !targetType.IsReferenceType)
                        throw Unsupported(node, "CastClass target must be a reference type");

                    X86Register source = ToX86Register(RequireUseRegister(node, 0), Target);
                    X86Register destination = ToX86Register(RequireResultRegister(node), Target);
                    string success = _owner.CreateLocalLabel($"{_methodLabel}_type_check_success");
                    string failure = _owner.CreateLocalLabel($"{_methodLabel}_type_check_failure");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_type_check_done");

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(source, 8)));
                    PushTypeCheckScratch();
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, 8),
                        Mem(X86Register.Rsp, 24, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(X86Register.R11, 8),
                        Reg(X86Register.R11, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R11, 0, 8)));
                    _owner.EmitLea(X86Register.Rax, _owner.GetTypeDescriptorLabel(targetType));
                    EmitLoadedTypeAssignabilityCheck(success, failure);

                    _owner.DefineLabel(failure);
                    PopTypeCheckScratch();
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Add,
                        Reg(X86Register.Rsp, Target.PointerSize),
                        Imm(8)));
                    if (throwOnFailure)
                    {
                        EmitManagedExceptionThrow(node, "InvalidCastException");
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Xor,
                            Reg(destination, 8),
                            Reg(destination, 8)));
                        EmitJump(done);
                    }

                    _owner.DefineLabel(success);
                    PopTypeCheckScratch();
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(destination, 8)));
                    _owner.DefineLabel(done);
                }

                private void EmitFrameOperation(GenTree node)
                {
                    switch (node.FrameOperation)
                    {
                        case FrameOperation.AllocateFrame:
                            if (node.Immediate != 0)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, Target.PointerSize), Imm(node.Immediate)));
                            return;
                        case FrameOperation.FreeFrame:
                            if (node.Immediate != 0)
                                _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, Target.PointerSize), Imm(node.Immediate)));
                            return;
                        case FrameOperation.EstablishFramePointer:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbp, Target.PointerSize), Reg(X86Register.Rsp, Target.PointerSize)));
                            if (_ehMethod is not null && !_ehFrameRegistered)
                                EmitEhFramePush(_blockLabels[node.BlockId]);
                            return;
                        case FrameOperation.RestoreStackPointerFromFramePointer:
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rsp, Target.PointerSize), Reg(X86Register.Rbp, Target.PointerSize)));
                            return;
                        case FrameOperation.SaveCalleeSavedRegister:
                            if (node.Results.Length != 1 || !node.Results[0].IsFrameSlot ||
                                node.Uses.Length != 1 || !node.Uses[0].IsRegister)
                            {
                                throw Unsupported(node, "invalid callee-saved register save");
                            }
                            EmitStore(
                                node.Results[0],
                                node.Uses[0].Register,
                                node.Results[0].FrameSlotSize > 0 ? node.Results[0].FrameSlotSize : RegisterInfo.RegisterSaveSize(Target, node.Uses[0].Register));
                            return;
                        case FrameOperation.RestoreCalleeSavedRegister:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister ||
                                node.Uses.Length != 1 || !node.Uses[0].IsFrameSlot)
                            {
                                throw Unsupported(node, "invalid callee-saved register restore");
                            }
                            EmitLoad(
                                node.Results[0].Register,
                                node.Uses[0],
                                node.Uses[0].FrameSlotSize > 0 ? node.Uses[0].FrameSlotSize : RegisterInfo.RegisterSaveSize(Target, node.Results[0].Register));
                            return;
                        case FrameOperation.EnterFuncletFrame:
                        case FrameOperation.LeaveFuncletFrame:
                            return;
                        case FrameOperation.SaveReturnAddress:
                        case FrameOperation.RestoreReturnAddress:
                            throw Unsupported(node, "return addresses are maintained by the hardware stack");
                        default:
                            throw Unsupported(node, $"unsupported frame operation {node.FrameOperation}");
                    }
                }

                private void EmitMove(GenTree node)
                {
                    if (node.Results.Length != 1 || node.Uses.Length != 1)
                        throw Unsupported(node, "move requires one source and one destination");
                    RegisterOperand destination = node.Results[0];
                    RegisterOperand source = node.Uses[0];
                    int size = StorageSize(ValueType(node), ValueStackKind(node), destination, source);

                    if (destination.Equals(source))
                        return;
                    if (source.IsAddress)
                    {
                        if (!destination.IsRegister)
                            throw Unsupported(node, "address move destination is not a register");
                        EmitLoadAddress(destination.Register, source);
                        return;
                    }
                    if (source.IsRegister && destination.IsRegister)
                    {
                        EmitRegisterMove(destination.Register, source.Register, size);
                        return;
                    }
                    if (!source.IsRegister && destination.IsRegister)
                    {
                        EmitLoad(destination.Register, source, size);
                        return;
                    }
                    if (source.IsRegister && !destination.IsRegister)
                    {
                        EmitStore(destination, source.Register, size);
                        return;
                    }
                    EmitMemoryToMemory(destination, source, size);
                }

                private void EmitFloatConstant(GenTree node, int size, byte[] bytes)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    if (MachineRegisters.GetClass(destination) != RegisterClass.Float)
                        throw Unsupported(node, "floating-point constant result is not in a floating-point register");

                    string label = _owner.AddConstantData(bytes, size, size == 4 ? "f32" : "f64");
                    _owner.Emit(X86Instruction.Binary(
                        size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                        Reg(ToX86Register(destination, Target), size),
                        _owner.SymbolMemory(label, 0, size)));
                }

                private void EmitLocalLike(GenTree node)
                {
                    bool isLoad = node.TreeKind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp;
                    bool isStore = node.TreeKind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp;
                    RuntimeType? type = node.LocalDescriptor?.Type ?? node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.LocalDescriptor?.StackKind ?? node.StackKind;
                    int size = StorageSize(type, kind);

                    if (isLoad && node.Uses.Length == 0 && node.Results.Length == 1)
                    {
                        RegisterOperand home = FrameSlotForLocalLike(node, size, node.Results[0].RegisterClass);
                        if (node.Results[0].IsRegister)
                            EmitLoad(node.Results[0].Register, home, size);
                        else
                            EmitMemoryToMemory(node.Results[0], home, size);
                        return;
                    }

                    if (isStore && node.Results.Length == 0 && node.Uses.Length == 1)
                    {
                        RegisterOperand home = FrameSlotForLocalLike(node, size, node.Uses[0].RegisterClass);
                        if (node.Uses[0].IsRegister)
                            EmitStore(home, node.Uses[0].Register, size);
                        else
                            EmitMemoryToMemory(home, node.Uses[0], size);
                        return;
                    }

                    if (node.Results.Length == 1 && node.Uses.Length == 1)
                    {
                        EmitMoveBetween(node.Results[0], node.Uses[0], size);
                        return;
                    }

                    if (node.Results.Length == 0 && node.Uses.Length == 0)
                        return;

                    throw Unsupported(node, "unsupported local, argument, or temp operand shape");
                }

                private void EmitAddressTree(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    if (node.Uses.Length == 1)
                    {
                        EmitLoadAddress(destination, node.Uses[0]);
                        return;
                    }
                    if (node.Uses.Length == 0)
                    {
                        EmitLoadAddress(destination, FrameSlotForAddress(node));
                        return;
                    }
                    throw Unsupported(node, "address tree has an invalid operand shape");
                }

                private void EmitFunctionPointer(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "function pointer has no target method");
                    _owner.EmitLea(ToX86Register(RequireResultRegister(node), Target), _owner.ResolveMethodLabel(method));
                }

                private void EmitField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Field node has no field metadata");
                    if (field.IsStatic)
                        throw Unsupported(node, "Instance field node references a static field");
                    if (node.Uses.Length == 0 || !node.Uses[0].IsRegister)
                        throw Unsupported(node, "Instance field operation has no instance register");

                    X86Register instance = ToX86Register(node.Uses[0].Register, Target);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, instance, "field");

                    int size = FieldStorageSize(field.FieldType);
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.FieldAddr:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                                throw Unsupported(node, "Field address requires one register result");
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Lea,
                                Reg(ToX86Register(node.Results[0].Register, Target), Target.PointerSize),
                                Mem(instance, field.Offset, Target.PointerSize)));
                            return;

                        case GenTreeKind.Field:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                                throw Unsupported(node, "Field load requires one register result");
                            EmitScalarLoad(
                                ToX86Register(node.Results[0].Register, Target),
                                Mem(instance, field.Offset, size),
                                field.FieldType,
                                size);
                            return;

                        case GenTreeKind.StoreField:
                            if (node.Uses.Length != 2 || !node.Uses[1].IsRegister)
                                throw Unsupported(node, "Field store requires instance and value registers");
                            EmitScalarStore(
                                Mem(instance, field.Offset, size),
                                ToX86Register(node.Uses[1].Register, Target),
                                size);
                            return;

                        default:
                            throw Unsupported(node, "Unsupported instance field operation");
                    }
                }

                private void EmitStaticField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Static field node has no field metadata");
                    if (!field.IsStatic)
                        throw Unsupported(node, "Static field node references an instance field");

                    int size = FieldStorageSize(field.FieldType);
                    string storage = _owner.GetStaticStorageLabel(field.DeclaringType);
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.StaticFieldAddr:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                                throw Unsupported(node, "Static field address requires one register result");
                            _owner.EmitLea(
                                ToX86Register(node.Results[0].Register, Target),
                                storage,
                                field.Offset);
                            return;

                        case GenTreeKind.StaticField:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                                throw Unsupported(node, "Static field load requires one register result");
                            EmitScalarLoad(
                                ToX86Register(node.Results[0].Register, Target),
                                _owner.SymbolMemory(storage, field.Offset, size),
                                field.FieldType,
                                size);
                            return;

                        case GenTreeKind.StoreStaticField:
                            if (node.Uses.Length != 1 || !node.Uses[0].IsRegister)
                                throw Unsupported(node, "Static field store requires one value register");
                            EmitScalarStore(
                                _owner.SymbolMemory(storage, field.Offset, size),
                                ToX86Register(node.Uses[0].Register, Target),
                                size);
                            return;

                        default:
                            throw Unsupported(node, "Unsupported static field operation");
                    }
                }

                private void EmitArray(GenTree node)
                {
                    if (node.TreeKind == GenTreeKind.ArrayDataRef)
                    {
                        EmitArrayDataReference(node);
                        return;
                    }

                    RuntimeType elementType = node.RuntimeType ?? node.Type ??
                        throw Unsupported(node, "Typed array operation has no element runtime type");
                    RuntimeType? arrayType = OperandType(node, 0);
                    if (arrayType is not null && arrayType.Kind == RuntimeTypeKind.Array && !arrayType.IsSzArray)
                        throw Unsupported(node, "Only single-dimensional zero-based arrays are implemented");

                    switch (node.TreeKind)
                    {
                        case GenTreeKind.ArrayElement:
                            EmitArrayElementLoad(node, elementType);
                            return;
                        case GenTreeKind.ArrayElementAddr:
                            EmitArrayElementAddressResult(node, elementType);
                            return;
                        case GenTreeKind.StoreArrayElement:
                            EmitArrayElementStore(node, elementType);
                            return;
                        default:
                            throw Unsupported(node, "Unsupported array operation");
                    }
                }

                private void EmitArrayDataReference(GenTree node)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array data reference requires one register result");

                    int arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array data reference");
                    if (!node.Uses[arrayUseIndex].IsRegister)
                        throw Unsupported(node, "Array data reference requires a register array operand");

                    X86Register array = ToX86Register(node.Uses[arrayUseIndex].Register, Target);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, array, "array_data");

                    string valid = _owner.CreateLocalLabel($"{_methodLabel}_array_data_valid_{node.LinearId}");
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        PreservedScratchSource(array, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, 0, 8)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(0x18)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(valid, 4, X86ObjectRelocationKind.Relative32)));
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(valid);

                    X86Register destination = ToX86Register(node.Results[0].Register, Target);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(destination, 8),
                        Mem(array, Target.ArrayDataOffset, 8)));
                }

                private void EmitArrayElementAddressResult(GenTree node, RuntimeType elementType)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array element address requires one register result");

                    EmitArrayElementTypeCheck(node, elementType, requireExact: true);
                    X86Operand address = ArrayElementMemory(node, elementType, nullChecked: true, size: 8);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(ToX86Register(node.Results[0].Register, Target), 8),
                        address));
                }

                private void EmitArrayElementLoad(GenTree node, RuntimeType elementType)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array element load requires one register result");

                    int size = ArrayElementSize(elementType);
                    if (size is not (1 or 2 or 4 or 8))
                        throw Unsupported(node, "x86 array element loads currently require scalar elements");

                    EmitArrayElementTypeCheck(node, elementType, requireExact: false);
                    EmitScalarLoad(
                        ToX86Register(node.Results[0].Register, Target),
                        ArrayElementMemory(node, elementType, nullChecked: true, size: size),
                        elementType,
                        size);
                }

                private void EmitArrayElementStore(GenTree node, RuntimeType elementType)
                {
                    int arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array-element store array");
                    int valueUseIndex = RequireCodegenUseIndexForOperand(node, 2, "array-element store value");
                    if (!node.Uses[arrayUseIndex].IsRegister || !node.Uses[valueUseIndex].IsRegister)
                        throw Unsupported(node, "Array-element store requires register array and value operands");

                    int size = ArrayElementSize(elementType);
                    if (size is not (1 or 2 or 4 or 8))
                        throw Unsupported(node, "x86 array element stores currently require scalar elements");

                    X86Register array = ToX86Register(node.Uses[arrayUseIndex].Register, Target);
                    X86Register value = ToX86Register(node.Uses[valueUseIndex].Register, Target);
                    EmitArrayElementTypeCheck(node, elementType, requireExact: false, arrayUseIndex: arrayUseIndex);
                    X86Operand address = ArrayElementMemory(
                        node,
                        elementType,
                        arrayUseIndex: arrayUseIndex,
                        nullChecked: true,
                        size: size);

                    if (elementType.IsReferenceType)
                        EmitArrayReferenceStoreCheck(node, array, value);

                    EmitScalarStore(
                        address,
                        value,
                        size);
                }

                private X86Operand ArrayElementMemory(
                    GenTree node,
                    RuntimeType elementType,
                    int arrayUseIndex = -1,
                    bool nullChecked = false,
                    int size = 0)
                {
                    if (arrayUseIndex < 0)
                        arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array operand");
                    int indexUseIndex = RequireCodegenUseIndexForOperand(node, 1, "array index");
                    if (!node.Uses[arrayUseIndex].IsRegister || !node.Uses[indexUseIndex].IsRegister)
                        throw Unsupported(node, "Array element access requires register operands");

                    X86Register array = ToX86Register(node.Uses[arrayUseIndex].Register, Target);
                    X86Register index = ToX86Register(node.Uses[indexUseIndex].Register, Target);
                    if (OperandStackKind(node, 1) != GenStackKind.I4)
                        throw Unsupported(node, "x86 indexed array access currently requires an Int32 index");
                    if (!nullChecked && (node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, array, "array");

                    if ((node.Flags & GenTreeFlags.BoundsCheckEliminated) == 0)
                    {
                        string inRange = _owner.CreateLocalLabel($"{_methodLabel}_array_index_in_range_{node.LinearId}");
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Cmp,
                            Reg(index, 4),
                            Mem(array, Target.ArrayLengthOffset, 4)));
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            X86Condition.B,
                            X86Operand.SymbolOperand(inRange, 4, X86ObjectRelocationKind.Relative32)));
                        EmitManagedExceptionThrow(node, "IndexOutOfRangeException");
                        _owner.DefineLabel(inRange);
                    }

                    int elementSize = ArrayElementSize(elementType);
                    if (elementSize is not (1 or 2 or 4 or 8))
                        throw Unsupported(node, "x86 indexed array addressing requires a 1, 2, 4, or 8 byte element");
                    return X86Operand.Memory(
                        array,
                        Target.ArrayDataOffset,
                        size == 0 ? elementSize : size,
                        index,
                        elementSize);
                }

                private void EmitArrayElementTypeCheck(
                    GenTree node,
                    RuntimeType elementType,
                    bool requireExact,
                    int arrayUseIndex = -1)
                {
                    if (arrayUseIndex < 0)
                        arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array operand");
                    if (!node.Uses[arrayUseIndex].IsRegister)
                        throw Unsupported(node, "Array type check requires a register array operand");

                    X86Register array = ToX86Register(node.Uses[arrayUseIndex].Register, Target);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, array, "array_type");

                    string done = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_ok_{node.LinearId}");
                    string fail = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_fail_{node.LinearId}");
                    string cleanupDone = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_cleanup_ok_{node.LinearId}");
                    string cleanupFail = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_cleanup_fail_{node.LinearId}");

                    PushTypeCheckScratch();
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        PreservedTypeCheckScratchSource(array, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, 0, 8)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(0x18)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(cleanupFail, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, 8, 8)));
                    _owner.EmitLea(X86Register.Rax, _owner.GetTypeDescriptorLabel(elementType));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, 8),
                        Reg(X86Register.Rax, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(cleanupDone, 4, X86ObjectRelocationKind.Relative32)));

                    if (!requireExact && elementType.IsReferenceType)
                        EmitLoadedTypeAssignabilityCheck(cleanupDone, cleanupFail);
                    else
                        EmitJump(cleanupFail);

                    _owner.DefineLabel(cleanupDone);
                    PopTypeCheckScratch();
                    EmitJump(done);
                    _owner.DefineLabel(cleanupFail);
                    PopTypeCheckScratch();
                    EmitJump(fail);

                    _owner.DefineLabel(fail);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(done);
                }

                private void EmitArrayReferenceStoreCheck(GenTree node, X86Register array, X86Register value)
                {
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_ok_{node.LinearId}");
                    string fail = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_fail_{node.LinearId}");
                    string cleanupDone = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_cleanup_ok_{node.LinearId}");
                    string cleanupFail = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_cleanup_fail_{node.LinearId}");

                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(value, 8), Reg(value, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(done, 4, X86ObjectRelocationKind.Relative32)));

                    PushTypeCheckScratch();
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, 8),
                        PreservedTypeCheckScratchSource(array, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, 8),
                        Mem(X86Register.Rax, 0, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, 8),
                        Mem(X86Register.Rax, 8, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        PreservedTypeCheckScratchSource(value, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, 0, 8)));
                    EmitLoadedTypeAssignabilityCheck(cleanupDone, cleanupFail);

                    _owner.DefineLabel(cleanupDone);
                    PopTypeCheckScratch();
                    EmitJump(done);
                    _owner.DefineLabel(cleanupFail);
                    PopTypeCheckScratch();
                    EmitJump(fail);

                    _owner.DefineLabel(fail);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(done);
                }

                private void EmitLoadedTypeAssignabilityCheck(string success, string failure)
                {
                    const int methodTableRelatedTypeOffset = 8;
                    int methodTableInterfaceMapOffset = checked(16 + Target.PointerSize);
                    const int elementTypeClass = 0x14;
                    const int elementTypeInterface = 0x15;
                    const int elementTypeSystemArray = 0x16;
                    const int elementTypeArray = 0x17;
                    const int elementTypeSzArray = 0x18;

                    string loop = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_loop");
                    string targetClass = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_class");
                    string targetInterface = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_interface");
                    string targetSystemArray = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_system_array");
                    string targetSzArray = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_szarray");
                    string sourceBase = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_source_base");
                    string interfaceLoop = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_interface_loop");

                    _owner.DefineLabel(loop);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, 8),
                        Reg(X86Register.Rax, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.Rax);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeClass)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(targetClass, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeInterface)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(targetInterface, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeSystemArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(targetSystemArray, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeSzArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(targetSzArray, 4, X86ObjectRelocationKind.Relative32)));
                    EmitJump(failure);

                    _owner.DefineLabel(targetClass);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, 8),
                        Mem(X86Register.Rax, methodTableRelatedTypeOffset, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R11, 8), Reg(X86Register.R11, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeSzArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    EmitJump(sourceBase);

                    _owner.DefineLabel(targetSystemArray);
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeSzArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    EmitJump(failure);

                    _owner.DefineLabel(targetSzArray);
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(X86Register.R11, 4), Imm(elementTypeSzArray)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, 8),
                        Mem(X86Register.Rax, methodTableRelatedTypeOffset, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, methodTableRelatedTypeOffset, 8)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, 8),
                        Reg(X86Register.Rax, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.Rax);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.R11, 4), Imm(elementTypeClass)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R11, 4),
                        Imm(elementTypeSzArray - elementTypeClass)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.A,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    EmitMethodTableElementType(X86Register.R11, X86Register.R10);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.R11, 4), Imm(elementTypeClass)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R11, 4),
                        Imm(elementTypeSzArray - elementTypeClass)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.A,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    EmitJump(loop);

                    _owner.DefineLabel(targetInterface);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, 8),
                        Mem(X86Register.R10, methodTableInterfaceMapOffset, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R11, 8), Reg(X86Register.R11, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.DefineLabel(interfaceLoop);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R11, 0, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R10, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, 8),
                        Reg(X86Register.Rax, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(success, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.R11, 8), Imm(Target.PointerSize)));
                    EmitJump(interfaceLoop);

                    _owner.DefineLabel(sourceBase);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, 8),
                        Mem(X86Register.R10, methodTableRelatedTypeOffset, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R10, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(failure, 4, X86ObjectRelocationKind.Relative32)));
                    EmitJump(loop);
                }

                private void EmitMethodTableElementType(X86Register destination, X86Register methodTable)
                {
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(destination, 4),
                        Mem(methodTable, 0, 4)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shr, Reg(destination, 4), Imm(26)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.And, Reg(destination, 4), Imm(0x1f)));
                }

                private void PushTypeCheckScratch()
                {
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R11, Target.PointerSize)));
                }

                private void PopTypeCheckScratch()
                {
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.Rax, 8)));
                }

                private static X86Operand PreservedTypeCheckScratchSource(X86Register register, int size)
                {
                    return register switch
                    {
                        X86Register.R11 => Mem(X86Register.Rsp, 0, size),
                        X86Register.R10 => Mem(X86Register.Rsp, 8, size),
                        X86Register.Rax => Mem(X86Register.Rsp, 16, size),
                        _ => Reg(register, size),
                    };
                }

                private static X86Operand PreservedScratchSource(X86Register register, int size)
                {
                    return register switch
                    {
                        X86Register.R11 => Mem(X86Register.Rsp, 0, size),
                        X86Register.R10 => Mem(X86Register.Rsp, 8, size),
                        _ => Reg(register, size),
                    };
                }

                private int ArrayElementSize(RuntimeType elementType)
                {
                    if (elementType.IsReferenceType || elementType.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                        return Target.PointerSize;
                    int size = elementType.SizeOf;
                    if (size <= 0)
                        throw new InvalidOperationException($"Array element type T{elementType.TypeId} has invalid size {size}.");
                    return size;
                }

                private void EmitScalarLoad(X86Register destination, X86Operand source, RuntimeType? type, int size)
                {
                    if (X86Registers.IsVector(destination))
                    {
                        if (size is not (4 or 8))
                            throw new NotSupportedException($"x86 floating-point load does not support size {size}.");
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                            Reg(destination, size),
                            source));
                        return;
                    }

                    if (size == 1 || size == 2)
                    {
                        bool signed = type?.PrimitiveKind is RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.Int16;
                        _owner.Emit(X86Instruction.Binary(
                            signed ? X86InstrKind.Movsx : X86InstrKind.Movzx,
                            Reg(destination, 4),
                            source));
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(destination, size),
                        source));
                }

                private void EmitScalarStore(X86Operand destination, X86Register source, int size)
                {
                    if (X86Registers.IsVector(source))
                    {
                        if (size is not (4 or 8))
                            throw new NotSupportedException($"x86 floating-point store does not support size {size}.");
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                            destination,
                            Reg(source, size)));
                        return;
                    }

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        destination,
                        Reg(source, size)));
                }

                private int FieldStorageSize(RuntimeType type)
                {
                    if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                        return Target.PointerSize;
                    if (type.SizeOf is 1 or 2 or 4 or 8)
                        return type.SizeOf;
                    throw new NotSupportedException($"x86 field access does not support T{type.TypeId} with size {type.SizeOf}.");
                }

                private void EmitCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "call has no runtime method");
                    if (method.HasInternalCall)
                    {
                        if (X86Runtime.TryEvaluateIsReferenceOrContainsReferences(method, out bool result))
                        {
                            EmitIntegerConstant(MachineRegister.X0, result ? 1 : 0, 4);
                            return;
                        }
                        if (X86Runtime.IsGcSafePointInternalCall(method))
                        {
                            EmitFastAllocateString(node, method);
                            return;
                        }

                        MarkEhCallSite(node, "call");
                        _owner.EmitCall(_owner.ResolveMethodLabel(method));
                        return;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "call");
                    _owner.EmitCall(_owner.ResolveMethodLabel(method));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitIndirectCall(GenTree node)
                {
                    int targetIndex = -1;
                    for (int i = 0; i < node.UseRoles.Length; i++)
                    {
                        if (node.UseRoles[i] == OperandRole.IndirectCallTarget)
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                    if (targetIndex < 0)
                        targetIndex = node.Uses.Length - 1;
                    if ((uint)targetIndex >= (uint)node.Uses.Length || !node.Uses[targetIndex].IsRegister)
                        throw Unsupported(node, "indirect call has no target register");

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "calli");
                    _owner.Emit(X86Instruction.Branch(
                        X86InstrKind.Call,
                        Reg(ToX86Register(node.Uses[targetIndex].Register, Target), Target.PointerSize)));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitVirtualCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "VirtualCall node has no runtime method");
                    if (!method.HasThis)
                        throw Unsupported(node, "VirtualCall target has no implicit this parameter");

                    MachineRegister receiverRegister = RequireVirtualCallReceiverRegister(node);
                    X86Register receiver = ToX86Register(receiverRegister, Target);
                    if (receiver != RuntimeArgumentRegister(0))
                        throw Unsupported(node, "VirtualCall receiver is not in the first integer argument register");

                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, receiver, "virtual_call");

                    if (method.DeclaringType.Kind != RuntimeTypeKind.Interface)
                        _owner.EnsureVirtualTable(method.DeclaringType);

                    if (method.DeclaringType.Kind != RuntimeTypeKind.Interface &&
                        (method.DeclaringType.IsValueType || method.VTableSlot < 0))
                    {
                        EmitCall(node);
                        return;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, method.DeclaringType.Kind == RuntimeTypeKind.Interface
                        ? "interface_call"
                        : "virtual_call");

                    if (method.DeclaringType.Kind == RuntimeTypeKind.Interface)
                    {
                        EmitInterfaceVirtualCall(node, method, receiver, safePoint);
                        return;
                    }

                    if (method.VTableSlot < 0)
                        throw Unsupported(node, "Class virtual call target has no vtable slot");

                    int vtablePointerOffset = checked(16 + Target.PointerSize * 2);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(receiver, 0, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.R10, vtablePointerOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Branch(
                        X86InstrKind.Call,
                        Mem(
                            X86Register.R11,
                            checked(method.VTableSlot * Target.PointerSize),
                            Target.PointerSize)));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitInterfaceVirtualCall(
                    GenTree node,
                    RuntimeMethod method,
                    X86Register receiver,
                    SafePointDraft safePoint)
                {
                    string cellLabel = _owner.CreateInterfaceDispatchCell(method);
                    string loop = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_loop_{node.LinearId}");
                    string found = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_found_{node.LinearId}");
                    string missing = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_missing_{node.LinearId}");

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(receiver, 0, Target.PointerSize)));
                    _owner.EmitLea(X86Register.R11, cellLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, Target.PointerSize),
                        Mem(X86Register.R11, 0, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Add,
                        Reg(X86Register.R11, Target.PointerSize),
                        Imm(Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(X86Register.Rax, Target.PointerSize),
                        Reg(X86Register.Rax, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(missing, 4, X86ObjectRelocationKind.Relative32)));

                    _owner.DefineLabel(loop);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.R11, 0, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(found, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Add,
                        Reg(X86Register.R11, Target.PointerSize),
                        Imm(checked(Target.PointerSize * 2))));
                    _owner.Emit(X86Instruction.Unary(
                        X86InstrKind.Dec,
                        Reg(X86Register.Rax, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(loop, 4, X86ObjectRelocationKind.Relative32)));

                    _owner.DefineLabel(missing);
                    _owner.EmitCall(_owner.GetVirtualDispatchFailureStubLabel());
                    EmitUnreachableTrap();

                    _owner.DefineLabel(found);
                    _owner.Emit(X86Instruction.Branch(
                        X86InstrKind.Call,
                        Mem(X86Register.R11, Target.PointerSize, Target.PointerSize)));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private static MachineRegister RequireVirtualCallReceiverRegister(GenTree node)
                {
                    for (int i = 0; i < node.Uses.Length; i++)
                    {
                        if (i < node.UseRoles.Length && node.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                            continue;
                        return RequireUseRegister(node, i);
                    }

                    throw Unsupported(node, "VirtualCall node has no receiver ABI operand");
                }

                private void EmitFastAllocateString(GenTree node, RuntimeMethod method)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    X86Register typeArgument = RuntimeArgumentRegister(0);
                    X86Register lengthArgument = RuntimeArgumentRegister(1);
                    if (lengthArgument != typeArgument)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(lengthArgument, 4),
                            Reg(typeArgument, 4)));
                    }
                    _owner.EmitLea(typeArgument, _owner.GetTypeDescriptorLabel(method.DeclaringType));
                    MarkEhCallSite(node, "string_alloc");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.NewArraySymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    MoveAllocationResult(node);
                }

                private void EmitNewArray(GenTree node)
                {
                    RuntimeType arrayType = node.Type ?? node.RuntimeType ?? throw Unsupported(node, "NewArray node has no array runtime type");
                    if (arrayType.Kind != RuntimeTypeKind.Array)
                        throw Unsupported(node, "NewArray node has a non-array runtime type");
                    if (!arrayType.IsSzArray)
                        throw Unsupported(node, "Multidimensional array allocation is not implemented");
                    if (node.Uses.Length != 1 || node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "NewArray must have one register length operand and one register result");

                    MachineRegister length = RequireUseRegister(node, 0);
                    X86Register lengthRegister = ToX86Register(length, Target);
                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_array_length_non_negative_{node.LinearId}");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(lengthRegister, 4), Imm(0)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ge,
                        X86Operand.SymbolOperand(nonNegative, 4, X86ObjectRelocationKind.Relative32)));
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(nonNegative);

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    X86Register typeArgument = RuntimeArgumentRegister(0);
                    X86Register lengthArgument = RuntimeArgumentRegister(1);
                    if (lengthArgument != lengthRegister)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(lengthArgument, 4),
                            Reg(lengthRegister, 4)));
                    }
                    _owner.EmitLea(typeArgument, _owner.GetTypeDescriptorLabel(arrayType));
                    MarkEhCallSite(node, "new_array");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.NewArraySymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    MoveAllocationResult(node);
                }

                private void EmitBox(GenTree node)
                {
                    RuntimeType boxedType = BoxSourceRuntimeType(node);
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Box requires one register result");

                    MachineRegister destination = node.Results[0].Register;
                    if (boxedType.IsReferenceType)
                    {
                        MachineRegister source = RequireUseRegister(node, 0);
                        EmitRegisterMove(destination, source, Target.PointerSize);
                        return;
                    }
                    if (!boxedType.IsValueType)
                        throw Unsupported(node, "Box source must be a value type or reference type");

                    int size = Math.Max(1, boxedType.SizeOf);
                    if (size is not (1 or 2 or 4 or 8))
                        throw Unsupported(node, "x86 boxing currently requires a scalar value type");
                    if (TypeOperationScratchSize < size)
                        throw Unsupported(node, "Type-operation scratch area is smaller than the box source");
                    if (node.Uses.Length != 1)
                        throw Unsupported(node, "Box requires one source operand");

                    RegisterOperand sourceOperand = node.Uses[0];
                    if (sourceOperand.IsRegister)
                    {
                        EmitScalarStore(
                            Mem(X86Register.Rbp, TypeOperationScratchOffset, size),
                            ToX86Register(sourceOperand.Register, Target),
                            size);
                    }
                    else if (sourceOperand.IsFrameSlot)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R10, size),
                            FrameMemory(sourceOperand, size)));
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rbp, TypeOperationScratchOffset, size),
                            Reg(X86Register.R10, size)));
                    }
                    else
                    {
                        throw Unsupported(node, "Box source is not addressable");
                    }

                    RuntimeType allocationType = boxedType;
                    int sourceOffset = 0;
                    int copySize = size;
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_box_done");
                    if (TryGetNullableInfo(boxedType, out RuntimeType underlyingType, out RuntimeField hasValueField, out RuntimeField valueField))
                    {
                        string hasValue = _owner.CreateLocalLabel($"{_methodLabel}_box_nullable_has_value");
                        int hasValueSize = Math.Max(1, hasValueField.FieldType.SizeOf);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Cmp,
                            Mem(X86Register.Rbp, checked(TypeOperationScratchOffset + hasValueField.Offset), hasValueSize),
                            Imm(0)));
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            X86Condition.Ne,
                            X86Operand.SymbolOperand(hasValue, 4, X86ObjectRelocationKind.Relative32)));
                        EmitZeroRegister(destination);
                        EmitJump(done);
                        _owner.DefineLabel(hasValue);
                        allocationType = underlyingType;
                        sourceOffset = valueField.Offset;
                        copySize = Math.Max(1, underlyingType.SizeOf);
                        if (copySize is not (1 or 2 or 4 or 8))
                            throw Unsupported(node, "nullable boxing requires a scalar underlying type");
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitLea(RuntimeArgumentRegister(0), _owner.GetTypeDescriptorLabel(allocationType));
                    MarkEhCallSite(node, "box");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.NewFastSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, copySize),
                        Mem(X86Register.Rbp, checked(TypeOperationScratchOffset + sourceOffset), copySize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rax, Target.ManagedObjectHeaderSize, copySize),
                        Reg(X86Register.R10, copySize)));
                    EmitRegisterMove(destination, MachineRegister.X0, Target.PointerSize);
                    _owner.DefineLabel(done);
                }

                private RuntimeType BoxSourceRuntimeType(GenTree node)
                {
                    if (!node.RegisterUses.IsDefaultOrEmpty)
                    {
                        RuntimeType? type = _method.GetValueInfo(node.RegisterUses[0]).Type;
                        if (type is not null)
                            return type;
                    }
                    return RequireRuntimeType(node);
                }

                private static bool TryGetNullableInfo(
                    RuntimeType type,
                    out RuntimeType underlyingType,
                    out RuntimeField hasValueField,
                    out RuntimeField valueField)
                {
                    underlyingType = null!;
                    hasValueField = null!;
                    valueField = null!;

                    if (!type.IsValueType)
                        return false;
                    RuntimeType definition = type.GenericTypeDefinition ?? type;
                    if (!StringComparer.Ordinal.Equals(definition.Namespace, "System") ||
                        !definition.Name.StartsWith("Nullable", StringComparison.Ordinal) ||
                        type.GenericTypeArguments.Length != 1)
                    {
                        return false;
                    }

                    RuntimeField? hasValue = null;
                    RuntimeField? value = null;
                    for (int i = 0; i < type.InstanceFields.Length; i++)
                    {
                        RuntimeField field = type.InstanceFields[i];
                        if (StringComparer.Ordinal.Equals(field.Name, "hasValue"))
                            hasValue = field;
                        else if (StringComparer.Ordinal.Equals(field.Name, "value"))
                            value = field;
                    }

                    RuntimeType underlying = type.GenericTypeArguments[0];
                    if (hasValue is null || value is null ||
                        hasValue.FieldType.PrimitiveKind != RuntimePrimitiveKind.Boolean ||
                        value.FieldType.TypeId != underlying.TypeId)
                    {
                        return false;
                    }

                    underlyingType = underlying;
                    hasValueField = hasValue;
                    valueField = value;
                    return true;
                }

                private void EmitNewDelegate(GenTree node)
                {
                    RuntimeType delegateType = node.RuntimeType ?? node.Type ??
                        throw Unsupported(node, "NewDelegate node has no delegate runtime type");
                    RuntimeMethod targetMethod = node.Method ??
                        throw Unsupported(node, "NewDelegate node has no target method");
                    if (node.Results.Length != 1)
                        throw Unsupported(node, "NewDelegate requires one result");

                    int targetOffset = _owner.FindDelegateFieldOffset(delegateType, "_target");
                    int methodPtrOffset = _owner.FindDelegateFieldOffset(delegateType, "_methodPtr");
                    int invocationListOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationList");
                    int invocationCountOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationCount");
                    bool closed = node.Uses.Length != 0;
                    if (closed && node.Uses.Length != 1)
                        throw Unsupported(node, "Closed NewDelegate requires exactly one target operand");
                    string thunkLabel = _owner.GetDelegateTargetThunkLabel(delegateType, targetMethod, closed);

                    if (closed)
                    {
                        LoadDelegatePointerOperand(node.Uses[0], X86Register.R10, dynamicStackAdjustment: 0);
                        EmitNullCheck(node, X86Register.R10, "new_delegate_target");
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize),
                            Reg(X86Register.R10, Target.PointerSize)));
                    }

                    int runtimeFrameSize = DelegateRuntimeCallFrameSize(argumentCount: 1);
                    if (runtimeFrameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Sub,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(runtimeFrameSize)));
                    }
                    SafePointDraft safePoint = PrepareSafePoint(
                        node,
                        dynamicStackAdjustment: runtimeFrameSize);
                    PublishGcTransition(safePoint);
                    _owner.EmitLea(RuntimeArgumentRegister(0), _owner.GetTypeDescriptorLabel(delegateType));
                    MarkEhCallSite(node, "new_delegate");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.NewFastSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    if (runtimeFrameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Add,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(runtimeFrameSize)));
                    }

                    if (closed)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R10, Target.PointerSize),
                            Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize)));
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Xor,
                            Reg(X86Register.R10, 4),
                            Reg(X86Register.R10, 4)));
                    }
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rax, targetOffset, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.EmitLea(X86Register.R11, thunkLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rax, methodPtrOffset, Target.PointerSize),
                        Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rax, invocationListOffset, Target.PointerSize),
                        Imm(0)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rax, invocationCountOffset, Target.PointerSize),
                        Imm(1)));
                    StoreDelegatePointerResult(node, X86Register.Rax);
                }

                private void EmitDelegateInvoke(GenTree node)
                {
                    RuntimeMethod invokeMethod = node.Method ??
                        throw Unsupported(node, "DelegateInvoke node has no Invoke method");
                    RuntimeType delegateType = invokeMethod.DeclaringType;
                    DelegateAbiBundle abi = _owner.GetDelegateInvokeAbi(invokeMethod);
                    if (abi.OrderedSlices.Length != node.Uses.Length)
                    {
                        throw Unsupported(
                            node,
                            $"DelegateInvoke ABI has {abi.OrderedSlices.Length} slices but the node has {node.Uses.Length} uses");
                    }

                    int receiverUseIndex = -1;
                    for (int i = 0; i < node.Uses.Length; i++)
                    {
                        if (i < node.UseRoles.Length && node.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                            continue;
                        receiverUseIndex = i;
                        break;
                    }
                    if (receiverUseIndex < 0)
                        throw Unsupported(node, "DelegateInvoke has no receiver operand");

                    LoadDelegatePointerOperand(node.Uses[receiverUseIndex], X86Register.R10, dynamicStackAdjustment: 0);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, X86Register.R10, "delegate_invoke");

                    int methodPtrOffset = _owner.FindDelegateFieldOffset(delegateType, "_methodPtr");
                    int invocationListOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationList");
                    int invocationCountOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationCount");
                    int outgoingSize = AlignUp(abi.OutgoingStackSize, Math.Max(1, Target.StackSlotSize));
                    int saveOffset = outgoingSize;
                    int saveSize = AlignUp(abi.TotalSaveSize, Target.PointerSize);
                    int listOffset = checked(saveOffset + saveSize);
                    int countOffset = checked(listOffset + Target.PointerSize);
                    int indexOffset = checked(countOffset + Target.PointerSize);
                    int frameSize = AlignUp(
                        checked(indexOffset + Target.PointerSize),
                        Target.CallFrameAlignment);
                    int receiverSaveOffset = checked(saveOffset + abi.OrderedSlices[receiverUseIndex].SaveOffset);

                    if (frameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Sub,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(frameSize)));
                    }
                    for (int i = 0; i < node.Uses.Length; i++)
                        SaveDelegateInvokeOperand(node.Uses[i], abi.OrderedSlices[i], saveOffset, frameSize);

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, receiverSaveOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.R10, invocationCountOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R11, Target.PointerSize),
                        Imm(1)));
                    string multicastLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_multicast_{node.LinearId}");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_done_{node.LinearId}");
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.A,
                        X86Operand.SymbolOperand(multicastLabel, 4, X86ObjectRelocationKind.Relative32)));

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    SafePointDraft singleSafePoint = PrepareSafePoint(
                        node,
                        dynamicStackAdjustment: frameSize);
                    MarkEhCallSite(node, "delegate_single");
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, receiverSaveOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.R10, methodPtrOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Branch(X86InstrKind.Call, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.DefineLabel(singleSafePoint.ReturnLabel);
                    EmitJump(doneLabel);

                    _owner.DefineLabel(multicastLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, receiverSaveOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.R10, invocationListOffset, Target.PointerSize)));
                    string listValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_list_valid_{node.LinearId}");
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(X86Register.R10, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(listValidLabel, 4, X86ObjectRelocationKind.Relative32)));
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(listValidLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rsp, listOffset, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rsp, countOffset, Target.PointerSize),
                        Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rsp, indexOffset, Target.PointerSize),
                        Imm(0)));

                    string loopLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_loop_{node.LinearId}");
                    _owner.DefineLabel(loopLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.Rsp, indexOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.Rsp, countOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ae,
                        X86Operand.SymbolOperand(doneLabel, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, listOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        X86Operand.Memory(
                            X86Register.R10,
                            Target.ArrayDataOffset,
                            Target.PointerSize,
                            X86Register.R11,
                            Target.PointerSize)));
                    string leafValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_leaf_valid_{node.LinearId}");
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Test,
                        Reg(X86Register.R10, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(leafValidLabel, 4, X86ObjectRelocationKind.Relative32)));
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(leafValidLabel);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rsp, receiverSaveOffset, Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, listOffset, Target.PointerSize)));
                    SafePointDraft multicastSafePoint = PrepareSafePoint(
                        node,
                        RegisterOperand.ForRegister(
                            Target.OperatingSystem == OperatingSystemKind.Windows
                                ? MachineRegister.X5
                                : MachineRegister.X7),
                        dynamicStackAdjustment: frameSize);
                    MarkEhCallSite(node, "delegate_multicast");
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rsp, receiverSaveOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.R10, methodPtrOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Branch(X86InstrKind.Call, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.DefineLabel(multicastSafePoint.ReturnLabel);
                    _owner.Emit(X86Instruction.Unary(
                        X86InstrKind.Inc,
                        Mem(X86Register.Rsp, indexOffset, Target.PointerSize)));
                    EmitJump(loopLabel);

                    _owner.DefineLabel(doneLabel);
                    if (frameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Add,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(frameSize)));
                    }
                }

                private void SaveDelegateInvokeOperand(
                    RegisterOperand operand,
                    DelegateAbiSlice slice,
                    int saveBase,
                    int dynamicStackAdjustment)
                {
                    int destinationOffset = checked(saveBase + slice.SaveOffset);
                    if (operand.IsRegister)
                    {
                        X86Register register = ToX86Register(operand.Register, Target);
                        X86InstrKind opcode = operand.RegisterClass == RegisterClass.Float
                            ? slice.Size switch
                            {
                                4 => X86InstrKind.Movss,
                                8 => X86InstrKind.Movsd,
                                16 => X86InstrKind.Movdqu,
                                _ => throw new NotSupportedException($"Unsupported delegate floating operand size {slice.Size}.")
                            }
                            : X86InstrKind.Mov;
                        _owner.Emit(X86Instruction.Binary(
                            opcode,
                            Mem(X86Register.Rsp, destinationOffset, slice.Size),
                            Reg(register, slice.Size)));
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw new InvalidOperationException($"Delegate ABI operand is not finalized: {operand}.");

                    X86Register sourceBase = FrameBase(operand);
                    int sourceOffset = EffectiveFrameOffset(operand);
                    if (sourceBase == X86Register.Rsp)
                        sourceOffset = checked(sourceOffset + dynamicStackAdjustment);
                    EmitDelegateBlockCopy(
                        sourceBase,
                        sourceOffset,
                        X86Register.Rsp,
                        destinationOffset,
                        slice.Size);
                }

                private void RestoreDelegateInvokeAbi(DelegateAbiBundle abi, int saveBase)
                {
                    for (int i = 0; i < abi.OrderedSlices.Length; i++)
                    {
                        DelegateAbiSlice slice = abi.OrderedSlices[i];
                        int sourceOffset = checked(saveBase + slice.SaveOffset);
                        if (slice.Location.IsRegister)
                        {
                            MachineRegister machineRegister = slice.Location.Register;
                            X86Register register = ToX86Register(machineRegister, Target);
                            X86InstrKind opcode = MachineRegisters.GetClass(machineRegister) == RegisterClass.Float
                                ? slice.Size switch
                                {
                                    4 => X86InstrKind.Movss,
                                    8 => X86InstrKind.Movsd,
                                    16 => X86InstrKind.Movdqu,
                                    _ => throw new NotSupportedException($"Unsupported delegate floating ABI size {slice.Size}.")
                                }
                                : X86InstrKind.Mov;
                            _owner.Emit(X86Instruction.Binary(
                                opcode,
                                Reg(register, slice.Size),
                                Mem(X86Register.Rsp, sourceOffset, slice.Size)));
                            continue;
                        }

                        int destinationOffset = checked(
                            slice.Location.StackSlotIndex * Target.StackSlotSize +
                            slice.Location.StackOffset);
                        EmitDelegateBlockCopy(
                            X86Register.Rsp,
                            sourceOffset,
                            X86Register.Rsp,
                            destinationOffset,
                            slice.Size);
                    }
                }

                private void EmitDelegateCombineOrRemove(GenTree node, bool remove)
                {
                    if (node.Uses.Length != 2 || node.Results.Length != 1)
                        throw Unsupported(node, "Delegate combine/remove requires two operands and one result");

                    RuntimeType delegateLayoutType = _owner.FindSystemType("MulticastDelegate");
                    RuntimeType delegateArrayType = _owner.GetDelegateInvocationListArrayType();
                    string leftNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_left_null_{node.LinearId}");
                    string rightNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_right_null_{node.LinearId}");
                    string typesMatch = _owner.CreateLocalLabel($"{_methodLabel}_delegate_types_match_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_delegate_combine_done_{node.LinearId}");
                    if (!node.Uses[0].IsRegister || !node.Uses[1].IsRegister)
                        throw Unsupported(node, "Delegate combine/remove operands must be registers");

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize),
                        Reg(ToX86Register(node.Uses[0].Register, Target), Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rbp, checked(TypeOperationScratchOffset + Target.PointerSize), Target.PointerSize),
                        Reg(ToX86Register(node.Uses[1].Register, Target), Target.PointerSize)));
                    SafePointDraft safePoint = PrepareSafePoint(node);

                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R10, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(leftNull, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R11, Target.PointerSize),
                        Mem(X86Register.Rbp, checked(TypeOperationScratchOffset + Target.PointerSize), Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R11, 8), Reg(X86Register.R11, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(rightNull, 4, X86ObjectRelocationKind.Relative32)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.R10, 0, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Cmp,
                        Reg(X86Register.R10, Target.PointerSize),
                        Mem(X86Register.R11, 0, Target.PointerSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.E,
                        X86Operand.SymbolOperand(typesMatch, 4, X86ObjectRelocationKind.Relative32)));
                    if (remove)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.Rax, Target.PointerSize),
                            Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize)));
                        StoreDelegatePointerResult(node, X86Register.Rax);
                        EmitJump(done);
                    }
                    else
                    {
                        EmitManagedExceptionThrow(node, "ArgumentException");
                    }

                    _owner.DefineLabel(typesMatch);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Mem(X86Register.Rbp, checked(TypeOperationScratchOffset + Target.PointerSize * 2), Target.PointerSize),
                        Reg(X86Register.R10, Target.PointerSize)));
                    int runtimeFrameSize = DelegateRuntimeCallFrameSize(argumentCount: 8);
                    if (runtimeFrameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Sub,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(runtimeFrameSize)));
                    }
                    PublishGcTransition(safePoint);
                    EmitDelegateRuntimePointerArgumentFromFrame(0, TypeOperationScratchOffset);
                    EmitDelegateRuntimePointerArgumentFromFrame(1, checked(TypeOperationScratchOffset + Target.PointerSize));
                    EmitDelegateRuntimePointerArgumentFromFrame(2, checked(TypeOperationScratchOffset + Target.PointerSize * 2));
                    EmitDelegateRuntimeLabelArgument(3, _owner.GetTypeDescriptorLabel(delegateArrayType));
                    EmitDelegateRuntimeImmediateArgument(4, _owner.FindDelegateFieldOffset(delegateLayoutType, "_target"));
                    EmitDelegateRuntimeImmediateArgument(5, _owner.FindDelegateFieldOffset(delegateLayoutType, "_methodPtr"));
                    EmitDelegateRuntimeImmediateArgument(6, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationList"));
                    EmitDelegateRuntimeImmediateArgument(7, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationCount"));
                    MarkEhCallSite(node, remove ? "delegate_remove" : "delegate_combine");
                    _owner.EmitCall(_owner.ResolveExternalFunction(
                        remove ? X86Runtime.DelegateRemoveSymbol : X86Runtime.DelegateCombineSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    if (runtimeFrameSize != 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Add,
                            Reg(X86Register.Rsp, Target.PointerSize),
                            Imm(runtimeFrameSize)));
                    }
                    StoreDelegatePointerResult(node, X86Register.Rax);
                    EmitJump(done);

                    _owner.DefineLabel(leftNull);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, Target.PointerSize),
                        Mem(
                            X86Register.Rbp,
                            checked(TypeOperationScratchOffset + (remove ? 0 : Target.PointerSize)),
                            Target.PointerSize)));
                    StoreDelegatePointerResult(node, X86Register.Rax);
                    EmitJump(done);
                    _owner.DefineLabel(rightNull);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(X86Register.Rax, Target.PointerSize),
                        Mem(X86Register.Rbp, TypeOperationScratchOffset, Target.PointerSize)));
                    StoreDelegatePointerResult(node, X86Register.Rax);
                    _owner.DefineLabel(done);
                }

                private int DelegateRuntimeCallFrameSize(int argumentCount)
                {
                    int registerCount = Target.OperatingSystem == OperatingSystemKind.Windows ? 4 : 6;
                    int stackArgumentCount = Math.Max(0, argumentCount - registerCount);
                    int shadowSize = Target.OperatingSystem == OperatingSystemKind.Windows
                        ? checked(registerCount * Target.StackSlotSize)
                        : 0;
                    return AlignUp(
                        checked(shadowSize + stackArgumentCount * Target.StackSlotSize),
                        Target.CallFrameAlignment);
                }

                private void EmitDelegateRuntimePointerArgumentFromFrame(int index, int frameOffset)
                {
                    if (TryGetDelegateRuntimeArgumentRegister(index, out X86Register register))
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(register, Target.PointerSize),
                            Mem(X86Register.Rbp, frameOffset, Target.PointerSize)));
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R10, Target.PointerSize),
                            Mem(X86Register.Rbp, frameOffset, Target.PointerSize)));
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rsp, DelegateRuntimeStackArgumentOffset(index), Target.PointerSize),
                            Reg(X86Register.R10, Target.PointerSize)));
                    }
                }

                private void EmitDelegateRuntimeLabelArgument(int index, string label)
                {
                    if (TryGetDelegateRuntimeArgumentRegister(index, out X86Register register))
                    {
                        _owner.EmitLea(register, label);
                    }
                    else
                    {
                        _owner.EmitLea(X86Register.R10, label);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rsp, DelegateRuntimeStackArgumentOffset(index), Target.PointerSize),
                            Reg(X86Register.R10, Target.PointerSize)));
                    }
                }

                private void EmitDelegateRuntimeImmediateArgument(int index, long value)
                {
                    if (TryGetDelegateRuntimeArgumentRegister(index, out X86Register register))
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(register, Target.PointerSize),
                            Imm(value)));
                    }
                    else
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(X86Register.Rsp, DelegateRuntimeStackArgumentOffset(index), Target.PointerSize),
                            Imm(value)));
                    }
                }

                private bool TryGetDelegateRuntimeArgumentRegister(int index, out X86Register register)
                {
                    int count = Target.OperatingSystem == OperatingSystemKind.Windows ? 4 : 6;
                    if ((uint)index < (uint)count)
                    {
                        register = RuntimeArgumentRegister(index);
                        return true;
                    }
                    register = X86Register.Invalid;
                    return false;
                }

                private int DelegateRuntimeStackArgumentOffset(int index)
                {
                    int registerCount = Target.OperatingSystem == OperatingSystemKind.Windows ? 4 : 6;
                    if (index < registerCount)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return Target.OperatingSystem == OperatingSystemKind.Windows
                        ? checked(index * Target.StackSlotSize)
                        : checked((index - registerCount) * Target.StackSlotSize);
                }

                private void EmitDelegateFailFast(int code)
                {
                    EmitDelegateRuntimeImmediateArgument(0, code);
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.FailFastSymbol));
                    EmitUnreachableTrap();
                }

                private void LoadDelegatePointerOperand(
                    RegisterOperand operand,
                    X86Register destination,
                    int dynamicStackAdjustment)
                {
                    if (operand.IsRegister)
                    {
                        X86Register source = ToX86Register(operand.Register, Target);
                        if (source != destination)
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Reg(destination, Target.PointerSize),
                                Reg(source, Target.PointerSize)));
                        }
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw new InvalidOperationException($"Delegate pointer operand is not finalized: {operand}.");
                    X86Register sourceBase = FrameBase(operand);
                    int sourceOffset = EffectiveFrameOffset(operand);
                    if (sourceBase == X86Register.Rsp)
                        sourceOffset = checked(sourceOffset + dynamicStackAdjustment);
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Mov,
                        Reg(destination, Target.PointerSize),
                        Mem(sourceBase, sourceOffset, Target.PointerSize)));
                }

                private void StoreDelegatePointerResult(GenTree node, X86Register source)
                {
                    if (node.Results.Length != 1)
                        throw Unsupported(node, "Delegate operation requires one result");
                    RegisterOperand result = node.Results[0];
                    if (result.IsRegister)
                    {
                        X86Register destination = ToX86Register(result.Register, Target);
                        if (destination != source)
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Reg(destination, Target.PointerSize),
                                Reg(source, Target.PointerSize)));
                        }
                        return;
                    }
                    if (result.IsFrameSlot)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            FrameMemory(result, Target.PointerSize),
                            Reg(source, Target.PointerSize)));
                        return;
                    }
                    throw Unsupported(node, "Delegate result is not finalized");
                }

                private void EmitDelegateBlockCopy(
                    X86Register sourceBase,
                    int sourceOffset,
                    X86Register destinationBase,
                    int destinationOffset,
                    int size)
                {
                    int copied = 0;
                    while (copied < size)
                    {
                        int remaining = size - copied;
                        int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R11, chunk),
                            Mem(sourceBase, checked(sourceOffset + copied), chunk)));
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Mem(destinationBase, checked(destinationOffset + copied), chunk),
                            Reg(X86Register.R11, chunk)));
                        copied += chunk;
                    }
                }

                private void EmitNewObject(GenTree node)
                {
                    RuntimeMethod constructor = node.Method ?? throw Unsupported(node, "NewObject node has no constructor");
                    RuntimeType objectType = constructor.DeclaringType;
                    if (!constructor.HasThis)
                        throw Unsupported(node, "NewObject constructor has no implicit this parameter");
                    if (objectType.IsValueType)
                        throw Unsupported(node, "Value-type newobj must be lowered before x86 code generation");
                    if (node.Results.Length != 1 || !node.Results[0].IsFrameSlot)
                        throw Unsupported(node, "Reference newobj result must have a frame home");

                    RegisterOperand objectHome = node.Results[0];
                    if (IsSystemStringType(objectType))
                    {
                        EmitNewStringObject(node, constructor, objectHome);
                        return;
                    }
                    if (objectType.InstanceSize < Target.ManagedObjectHeaderSize)
                        throw Unsupported(node, "Allocated object layout is smaller than the runtime object header");

                    SafePointDraft allocationSafePoint = PrepareSafePoint(node);
                    SaveNewObjectArguments();
                    PublishGcTransition(allocationSafePoint);
                    _owner.EmitLea(RuntimeArgumentRegister(0), _owner.GetTypeDescriptorLabel(objectType));
                    MarkEhCallSite(node, "new_object_alloc");
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.NewFastSymbol));
                    _owner.DefineLabel(allocationSafePoint.ReturnLabel);
                    EmitStore(objectHome, MachineRegister.X0, Target.PointerSize);

                    RestoreNewObjectArguments();
                    EmitLoad(RegisterInfo.GetIntegerArgumentRegister(Target, 0), objectHome, Target.PointerSize);
                    SafePointDraft constructorSafePoint = PrepareSafePoint(node, objectHome);
                    MarkEhCallSite(node, "new_object_ctor");
                    _owner.EmitCall(_owner.ResolveMethodLabel(constructor));
                    _owner.DefineLabel(constructorSafePoint.ReturnLabel);
                }

                private void EmitNewStringObject(GenTree node, RuntimeMethod constructor, RegisterOperand objectHome)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    RuntimeType[] parameters = constructor.ParameterTypes;
                    string runtimeSymbol;

                    PublishGcTransition(safePoint);
                    _owner.EmitLea(RuntimeArgumentRegister(0), _owner.GetTypeDescriptorLabel(constructor.DeclaringType));
                    if (parameters.Length == 0)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Xor,
                            Reg(RuntimeArgumentRegister(1), 4),
                            Reg(RuntimeArgumentRegister(1), 4)));
                        runtimeSymbol = X86Runtime.NewArraySymbol;
                    }
                    else if (parameters.Length == 2 && IsCharType(parameters[0]) && IsInt32Type(parameters[1]))
                    {
                        runtimeSymbol = X86Runtime.NewStringFromCharSymbol;
                    }
                    else if (parameters.Length == 1 && IsCharPointerType(parameters[0]))
                    {
                        runtimeSymbol = X86Runtime.NewStringFromUtf16Symbol;
                    }
                    else if (parameters.Length == 1 && IsCharArrayType(parameters[0]))
                    {
                        runtimeSymbol = X86Runtime.NewStringFromCharArraySymbol;
                    }
                    else if (parameters.Length == 3 &&
                             IsCharArrayType(parameters[0]) &&
                             IsInt32Type(parameters[1]) &&
                             IsInt32Type(parameters[2]))
                    {
                        runtimeSymbol = X86Runtime.NewStringFromCharArrayRangeSymbol;
                    }
                    else
                    {
                        throw Unsupported(node, "Unsupported System.String constructor shape");
                    }

                    MarkEhCallSite(node, "new_string");
                    _owner.EmitCall(_owner.ResolveExternalFunction(runtimeSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    EmitStore(objectHome, MachineRegister.X0, Target.PointerSize);
                }

                private void MoveAllocationResult(GenTree node)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        return;
                    MachineRegister destination = node.Results[0].Register;
                    if (destination != MachineRegister.X0)
                        EmitRegisterMove(destination, MachineRegister.X0, Target.PointerSize);
                }

                private static bool IsCharType(RuntimeType type)
                    => type.PrimitiveKind == RuntimePrimitiveKind.Char ||
                       (StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                        StringComparer.Ordinal.Equals(type.Name, "Char"));

                private static bool IsInt32Type(RuntimeType type)
                    => type.PrimitiveKind == RuntimePrimitiveKind.Int32 ||
                       (StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                        StringComparer.Ordinal.Equals(type.Name, "Int32"));

                private static bool IsCharArrayType(RuntimeType type)
                    => type.Kind == RuntimeTypeKind.Array &&
                       type.ElementType is not null &&
                       IsCharType(type.ElementType);

                private static bool IsCharPointerType(RuntimeType type)
                    => type.Kind == RuntimeTypeKind.Pointer &&
                       type.ElementType is not null &&
                       IsCharType(type.ElementType);

                private void EmitUnary(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister source = RequireUseRegister(node, 0);
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);
                    int size = StorageSize(type, kind);

                    if (IsFloating(type, kind))
                    {
                        if (node.SourceOp != BytecodeOp.Neg)
                            throw Unsupported(node, $"unsupported floating unary opcode {node.SourceOp}");
                        if (MachineRegisters.GetClass(destination) != RegisterClass.Float ||
                            MachineRegisters.GetClass(source) != RegisterClass.Float)
                        {
                            throw Unsupported(node, "floating-point unary operands are not in floating-point registers");
                        }

                        EmitRegisterMove(destination, source, size);
                        byte[] mask = size == 4
                            ? new byte[] { 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                            : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0 };
                        string label = _owner.AddConstantData(mask, 16, size == 4 ? "f32_sign" : "f64_sign");
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Xorps : X86InstrKind.Xorpd,
                            Reg(ToX86Register(destination, Target), 16),
                            _owner.SymbolMemory(label, 0, 16)));
                        return;
                    }

                    EmitRegisterMove(destination, source, size);
                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Neg:
                            _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(ToX86Register(destination, Target), size)));
                            return;
                        case BytecodeOp.Not:
                            _owner.Emit(X86Instruction.Unary(X86InstrKind.Not, Reg(ToX86Register(destination, Target), size)));
                            return;
                        case BytecodeOp.FnPtrToPtr:
                        case BytecodeOp.PtrToFnPtr:
                            return;
                        default:
                            throw Unsupported(node, $"unsupported unary opcode {node.SourceOp}");
                    }
                }

                private void EmitBinary(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister left = RequireUseRegister(node, 0);
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);
                    int size = StorageSize(type, kind);

                    if (IsFloating(type, kind))
                    {
                        EmitFloatingBinary(node, destination, left, RequireUseRegister(node, 1), size);
                        return;
                    }

                    if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                    {
                        EmitBinaryImmediate(node, destination, left, immediate, size);
                        return;
                    }

                    MachineRegister right = RequireUseRegister(node, 1);
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register lhs = ToX86Register(left, Target);
                    X86Register rhs = ToX86Register(right, Target);

                    if (node.SourceOp is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un)
                    {
                        EmitComparison(node, dst, Reg(lhs, size), Reg(rhs, size));
                        return;
                    }

                    if (node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                    {
                        EmitIntegerDivRem(node, destination, left, right, type, size);
                        return;
                    }

                    bool isShift = node.SourceOp is BytecodeOp.Shl or BytecodeOp.Shr or BytecodeOp.Shr_Un;
                    if (isShift && rhs != X86Register.Rcx)
                        throw Unsupported(node, "variable shift count was not allocated to RCX");
                    if (isShift && dst == rhs && dst != lhs)
                        throw Unsupported(node, "shift result conflicts with the fixed RCX count register");

                    bool isCommutative = node.SourceOp is BytecodeOp.Add or BytecodeOp.Mul or BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor;
                    X86Register sourceForOperation = rhs;
                    bool destinationContainsLeft = dst == lhs;
                    if (dst == rhs && dst != lhs)
                    {
                        if (isCommutative)
                        {
                            sourceForOperation = lhs;
                            destinationContainsLeft = true;
                        }
                        else if (node.SourceOp == BytecodeOp.Sub)
                        {
                            _owner.Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(dst, size)));
                            _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(dst, size), Reg(lhs, size)));
                            return;
                        }
                    }
                    if (!destinationContainsLeft)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, size), Reg(lhs, size)));

                    X86InstrKind opcode = BinaryOpcode(node);
                    _owner.Emit(X86Instruction.Binary(opcode, Reg(dst, size), Reg(sourceForOperation, isShift ? 1 : size)));
                }

                private void EmitFloatingBinary(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    MachineRegister right,
                    int size)
                {
                    if (MachineRegisters.GetClass(left) != RegisterClass.Float ||
                        MachineRegisters.GetClass(right) != RegisterClass.Float)
                    {
                        throw Unsupported(node, "floating-point operands are not in floating-point registers");
                    }

                    if (node.SourceOp is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un)
                    {
                        if (MachineRegisters.GetClass(destination) != RegisterClass.General)
                            throw Unsupported(node, "floating-point comparison result is not in a general register");
                        EmitFloatingComparison(node, destination, left, right, size);
                        return;
                    }

                    if (MachineRegisters.GetClass(destination) != RegisterClass.Float)
                        throw Unsupported(node, "floating-point arithmetic result is not in a floating-point register");

                    X86InstrKind opcode = node.SourceOp switch
                    {
                        BytecodeOp.Add => size == 4 ? X86InstrKind.Addss : X86InstrKind.Addsd,
                        BytecodeOp.Sub => size == 4 ? X86InstrKind.Subss : X86InstrKind.Subsd,
                        BytecodeOp.Mul => size == 4 ? X86InstrKind.Mulss : X86InstrKind.Mulsd,
                        BytecodeOp.Div => size == 4 ? X86InstrKind.Divss : X86InstrKind.Divsd,
                        _ => throw Unsupported(node, "unsupported floating binary opcode " + node.SourceOp)
                    };

                    X86Register dst = ToX86Register(destination, Target);
                    X86Register lhs = ToX86Register(left, Target);
                    X86Register rhs = ToX86Register(right, Target);
                    bool commutative = node.SourceOp is BytecodeOp.Add or BytecodeOp.Mul;
                    if (dst == rhs && dst != lhs && commutative)
                    {
                        _owner.Emit(X86Instruction.Binary(opcode, Reg(dst, size), Reg(lhs, size)));
                        return;
                    }

                    if (dst == rhs && dst != lhs)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                            Mem(X86Register.Rsp, 0, size),
                            Reg(rhs, size)));
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                            Reg(dst, size),
                            Reg(lhs, size)));
                        _owner.Emit(X86Instruction.Binary(opcode, Reg(dst, size), Mem(X86Register.Rsp, 0, size)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));
                        return;
                    }

                    if (dst != lhs)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                            Reg(dst, size),
                            Reg(lhs, size)));
                    }
                    _owner.Emit(X86Instruction.Binary(opcode, Reg(dst, size), Reg(rhs, size)));
                }

                private void EmitFloatingComparison(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    MachineRegister right,
                    int size)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    EmitFloatingCompareFlags(node, left, right, size);

                    string trueLabel = _owner.CreateLocalLabel($"{_methodLabel}_fcmp_true_{node.LinearId}");
                    string falseLabel = _owner.CreateLocalLabel($"{_methodLabel}_fcmp_false_{node.LinearId}");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_fcmp_done_{node.LinearId}");
                    EmitFloatingComparisonBranches(node.SourceOp, trueLabel, falseLabel);

                    _owner.DefineLabel(falseLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(dst, 4), Reg(dst, 4)));
                    EmitJump(doneLabel);
                    _owner.DefineLabel(trueLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, 4), Imm(1)));
                    _owner.DefineLabel(doneLabel);
                }

                private void EmitFloatingCompareFlags(GenTree node, MachineRegister left, MachineRegister right, int size)
                {
                    if (size is not (4 or 8) ||
                        MachineRegisters.GetClass(left) != RegisterClass.Float ||
                        MachineRegisters.GetClass(right) != RegisterClass.Float)
                    {
                        throw Unsupported(node, "floating-point comparison operands are invalid");
                    }

                    _owner.Emit(X86Instruction.Binary(
                        size == 4 ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd,
                        Reg(ToX86Register(left, Target), size),
                        Reg(ToX86Register(right, Target), size)));
                }

                private void EmitFloatingComparisonBranches(BytecodeOp op, string trueLabel, string falseLabel)
                {
                    switch (op)
                    {
                        case BytecodeOp.Ceq:
                            EmitConditionalJump(X86Condition.P, falseLabel);
                            EmitConditionalJump(X86Condition.E, trueLabel);
                            EmitJump(falseLabel);
                            return;
                        case BytecodeOp.Clt:
                            EmitConditionalJump(X86Condition.P, falseLabel);
                            EmitConditionalJump(X86Condition.B, trueLabel);
                            EmitJump(falseLabel);
                            return;
                        case BytecodeOp.Clt_Un:
                            EmitConditionalJump(X86Condition.B, trueLabel);
                            EmitJump(falseLabel);
                            return;
                        case BytecodeOp.Cgt:
                            EmitConditionalJump(X86Condition.A, trueLabel);
                            EmitJump(falseLabel);
                            return;
                        case BytecodeOp.Cgt_Un:
                            EmitConditionalJump(X86Condition.P, trueLabel);
                            EmitConditionalJump(X86Condition.A, trueLabel);
                            EmitJump(falseLabel);
                            return;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(op));
                    }
                }

                private void EmitConditionalJump(X86Condition condition, string target)
                {
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        condition,
                        X86Operand.SymbolOperand(target, 4, X86ObjectRelocationKind.Relative32)));
                }

                private void EmitBinaryImmediate(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    long immediate,
                    int size)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register lhs = ToX86Register(left, Target);

                    if (node.SourceOp is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un)
                    {
                        EmitComparison(node, dst, Reg(lhs, size), Imm(immediate));
                        return;
                    }

                    if (node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                        throw Unsupported(node, "integer division cannot use a contained immediate operand");

                    if (node.SourceOp == BytecodeOp.Mul)
                    {
                        _owner.Emit(X86Instruction.Ternary(
                            X86InstrKind.Imul,
                            Reg(dst, size),
                            Reg(lhs, size),
                            Imm(immediate)));
                        return;
                    }

                    if (dst != lhs)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, size), Reg(lhs, size)));

                    bool isShift = node.SourceOp is BytecodeOp.Shl or BytecodeOp.Shr or BytecodeOp.Shr_Un;
                    long encodedImmediate = isShift ? immediate & (size == 8 ? 63 : 31) : immediate;
                    _owner.Emit(X86Instruction.Binary(
                        BinaryOpcode(node),
                        Reg(dst, size),
                        Imm(encodedImmediate)));
                }

                private void EmitComparison(GenTree node, X86Register destination, X86Operand left, X86Operand right)
                {
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, left, right));
                    X86Condition condition = node.SourceOp switch
                    {
                        BytecodeOp.Ceq => X86Condition.E,
                        BytecodeOp.Clt => X86Condition.L,
                        BytecodeOp.Clt_Un => X86Condition.B,
                        BytecodeOp.Cgt => X86Condition.G,
                        BytecodeOp.Cgt_Un => X86Condition.A,
                        _ => throw Unsupported(node, $"unsupported comparison opcode {node.SourceOp}"),
                    };
                    EmitBooleanResult(condition, destination);
                }

                private void EmitBooleanResult(X86Condition condition, X86Register destination)
                {
                    if (!Target.Is32Bit)
                    {
                        _owner.Emit(X86Instruction.Setcc(condition, Reg(destination, 1)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(destination, 4), Reg(destination, 1)));
                        return;
                    }

                    string trueLabel = _owner.CreateLocalLabel($"{_methodLabel}_bool_true");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_bool_done");
                    EmitConditionalJump(condition, trueLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(destination, 4), Reg(destination, 4)));
                    EmitJump(doneLabel);
                    _owner.DefineLabel(trueLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, 4), Imm(1)));
                    _owner.DefineLabel(doneLabel);
                }

                private static X86InstrKind BinaryOpcode(GenTree node)
                {
                    X86InstrKind opcode = node.SourceOp switch
                    {
                        BytecodeOp.Add => X86InstrKind.Add,
                        BytecodeOp.Sub => X86InstrKind.Sub,
                        BytecodeOp.Mul => X86InstrKind.Imul,
                        BytecodeOp.And => X86InstrKind.And,
                        BytecodeOp.Or => X86InstrKind.Or,
                        BytecodeOp.Xor => X86InstrKind.Xor,
                        BytecodeOp.Shl => X86InstrKind.Shl,
                        BytecodeOp.Shr => X86InstrKind.Sar,
                        BytecodeOp.Shr_Un => X86InstrKind.Shr,
                        _ => X86InstrKind.Invalid,
                    };
                    if (opcode == X86InstrKind.Invalid)
                        throw Unsupported(node, $"unsupported binary opcode {node.SourceOp}");
                    return opcode;
                }

                private void EmitIntegerDivRem(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    MachineRegister right,
                    RuntimeType? type,
                    int size)
                {
                    if (size is not (4 or 8))
                        throw Unsupported(node, "integer division requires a 32-bit or 64-bit operand");

                    MachineRegister expectedLeft = RegisterInfo.AccumulatorRegister(Target);
                    MachineRegister expectedResult = node.SourceOp is BytecodeOp.Rem or BytecodeOp.Rem_Un
                        ? RegisterInfo.DataRegister(Target)
                        : expectedLeft;
                    if (left != expectedLeft || destination != expectedResult)
                        throw Unsupported(node, "integer division does not satisfy the fixed accumulator/data-register constraints");

                    MachineRegister scratch = RequireInternalGeneralRegister(node, 0);
                    if (scratch == RegisterInfo.AccumulatorRegister(Target) || scratch == RegisterInfo.DataRegister(Target))
                        throw Unsupported(node, "integer division scratch register conflicts with an implicit operand");

                    X86Register divisor = ToX86Register(scratch, Target);
                    X86Register source = ToX86Register(right, Target);
                    if (divisor != source)
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(divisor, size), Reg(source, size)));

                    if ((node.Flags & GenTreeFlags.DivModNoByZero) == 0)
                    {
                        string nonZero = _owner.CreateLocalLabel($"{_methodLabel}_div_nonzero_{node.LinearId}");
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(divisor, size), Reg(divisor, size)));
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            X86Condition.Ne,
                            X86Operand.SymbolOperand(nonZero, 4, X86ObjectRelocationKind.Relative32)));
                        EmitManagedExceptionThrow(node, "DivideByZeroException");
                        _owner.DefineLabel(nonZero);
                    }

                    bool unsigned = IsUnsigned(type) || node.SourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un;
                    if (!unsigned && (node.Flags & GenTreeFlags.DivModNoOverflow) == 0)
                    {
                        string perform = _owner.CreateLocalLabel($"{_methodLabel}_div_perform_{node.LinearId}");
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(divisor, size), Imm(-1)));
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            X86Condition.Ne,
                            X86Operand.SymbolOperand(perform, 4, X86ObjectRelocationKind.Relative32)));
                        if (size == 4)
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Cmp,
                                Reg(X86Register.Rax, 4),
                                Imm(int.MinValue)));
                        }
                        else
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Reg(X86Register.Rdx, 8),
                                Imm(long.MinValue)));
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Cmp,
                                Reg(X86Register.Rax, 8),
                                Reg(X86Register.Rdx, 8)));
                        }
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            X86Condition.Ne,
                            X86Operand.SymbolOperand(perform, 4, X86ObjectRelocationKind.Relative32)));
                        EmitManagedExceptionThrow(node, "OverflowException");
                        _owner.DefineLabel(perform);
                    }

                    if (unsigned)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Xor,
                            Reg(X86Register.Rdx, size),
                            Reg(X86Register.Rdx, size)));
                    }
                    else
                    {
                        _owner.Emit(new X86Instruction(size == 4 ? X86InstrKind.Cdq : X86InstrKind.Cqo));
                    }

                    _owner.Emit(X86Instruction.Unary(
                        unsigned ? X86InstrKind.Div : X86InstrKind.Idiv,
                        Reg(divisor, size)));
                }

                private void EmitManagedExceptionThrow(GenTree node, string exceptionTypeName)
                {
                    MarkEhCallSite(node, "implicit_throw");
                    _owner.EmitLea(
                        RuntimeArgumentRegister(0),
                        _owner.GetStaticExceptionObjectLabel("System", exceptionTypeName));
                    _owner.EmitCall(_owner.ResolveExternalFunction(X86Runtime.ThrowSymbol));
                    EmitUnreachableTrap();
                }

                private static MachineRegister RequireInternalGeneralRegister(GenTree node, int index)
                {
                    int seen = 0;
                    for (int i = 0; i < node.InternalRegisters.Length; i++)
                    {
                        GenTreeInternalRegister register = node.InternalRegisters[i];
                        if (register.RegisterClass != RegisterClass.General)
                            continue;
                        if (seen++ == index)
                            return register.Register;
                    }

                    throw Unsupported(node, "required internal general register was not allocated");
                }

                private void EmitSimpleConversion(GenTree node)
                {
                    if (node.Results.Length != 1 || node.Uses.Length != 1 ||
                        !node.Results[0].IsRegister || !node.Uses[0].IsRegister)
                    {
                        throw Unsupported(node, "conversion requires one register source and result");
                    }

                    MachineRegister destination = node.Results[0].Register;
                    MachineRegister source = node.Uses[0].Register;
                    if (node.TreeKind == GenTreeKind.PointerToByRef)
                    {
                        EmitRegisterMove(destination, source, Target.PointerSize);
                        return;
                    }

                    RuntimeType? sourceType = OperandType(node, 0);
                    GenStackKind sourceKind = OperandStackKind(node, 0);
                    bool sourceFloat = IsFloating(sourceType, sourceKind);
                    bool sourceUnsigned = IsUnsigned(sourceType) || (node.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;
                    bool targetFloat = node.ConvKind is NumericConvKind.R4 or NumericConvKind.R8;
                    bool checkedConversion = (node.ConvFlags & NumericConvFlags.Checked) != 0;
                    RegisterClass expectedSourceClass = sourceFloat ? RegisterClass.Float : RegisterClass.General;
                    RegisterClass expectedDestinationClass = targetFloat ? RegisterClass.Float : RegisterClass.General;
                    if (MachineRegisters.GetClass(source) != expectedSourceClass ||
                        MachineRegisters.GetClass(destination) != expectedDestinationClass)
                    {
                        throw Unsupported(node, "numeric conversion operands are in invalid register classes");
                    }

                    if (checkedConversion && sourceFloat && !targetFloat && node.ConvKind != NumericConvKind.Bool)
                        EmitCheckedFloatConversionGuard(node, source, sourceType, sourceKind);
                    else if (checkedConversion && !sourceFloat && !targetFloat && node.ConvKind != NumericConvKind.Bool)
                        throw Unsupported(node, "checked integer conversions are not implemented by the x86 backend");

                    switch (node.ConvKind)
                    {
                        case NumericConvKind.Bool:
                            EmitBoolConversion(destination, source, sourceType, sourceKind);
                            return;
                        case NumericConvKind.R4:
                            EmitToFloat(destination, source, sourceType, sourceKind, sourceUnsigned, 4);
                            return;
                        case NumericConvKind.R8:
                            EmitToFloat(destination, source, sourceType, sourceKind, sourceUnsigned, 8);
                            return;
                        case NumericConvKind.I1:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 8, false);
                            else
                                EmitIntegerNarrow(destination, source, 1, true);
                            return;
                        case NumericConvKind.U1:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 8, true);
                            else
                                EmitIntegerNarrow(destination, source, 1, false);
                            return;
                        case NumericConvKind.I2:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 16, false);
                            else
                                EmitIntegerNarrow(destination, source, 2, true);
                            return;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 16, true);
                            else
                                EmitIntegerNarrow(destination, source, 2, false);
                            return;
                        case NumericConvKind.I4:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 32, false);
                            else
                                _owner.Emit(X86Instruction.Binary(
                                    X86InstrKind.Mov,
                                    Reg(ToX86Register(destination, Target), 4),
                                    Reg(ToX86Register(source, Target), 4)));
                            return;
                        case NumericConvKind.U4:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 32, true);
                            else
                                _owner.Emit(X86Instruction.Binary(
                                    X86InstrKind.Mov,
                                    Reg(ToX86Register(destination, Target), 4),
                                    Reg(ToX86Register(source, Target), 4)));
                            return;
                        case NumericConvKind.I8:
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 64, false);
                                return;
                            }
                            EmitIntegerTo64(destination, source, sourceType, sourceKind, sourceUnsigned);
                            return;
                        case NumericConvKind.U8:
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 64, true);
                                return;
                            }
                            EmitIntegerTo64(destination, source, sourceType, sourceKind, sourceUnsigned);
                            return;
                        case NumericConvKind.NativeInt:
                            if (Target.Is32Bit)
                            {
                                if (sourceFloat)
                                    EmitFloatToInteger(destination, source, sourceType, sourceKind, 32, false);
                                else
                                    EmitRegisterMove(destination, source, 4);
                                return;
                            }
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 64, false);
                                return;
                            }
                            EmitIntegerTo64(destination, source, sourceType, sourceKind, sourceUnsigned);
                            return;
                        case NumericConvKind.NativeUInt:
                            if (Target.Is32Bit)
                            {
                                if (sourceFloat)
                                    EmitFloatToInteger(destination, source, sourceType, sourceKind, 32, true);
                                else
                                    EmitRegisterMove(destination, source, 4);
                                return;
                            }
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, 64, true);
                                return;
                            }
                            EmitIntegerTo64(destination, source, sourceType, sourceKind, sourceUnsigned);
                            return;
                        default:
                            throw Unsupported(node, $"unsupported numeric conversion {node.ConvKind}");
                    }
                }

                private void EmitBoolConversion(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    if (!IsFloating(sourceType, sourceKind))
                    {
                        int sourceSize = StorageSize(sourceType, sourceKind);
                        X86Register src = ToX86Register(source, Target);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(src, sourceSize), Reg(src, sourceSize)));
                        EmitBooleanResult(X86Condition.Ne, dst);
                        return;
                    }

                    int size = StorageSize(sourceType, sourceKind);
                    X86Operand zero = FloatingConstantOperand(0.0, size, size == 4 ? "f32_zero" : "f64_zero");
                    _owner.Emit(X86Instruction.Binary(
                        size == 4 ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd,
                        Reg(ToX86Register(source, Target), size),
                        zero));

                    string trueLabel = _owner.CreateLocalLabel($"{_methodLabel}_conv_bool_true");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_conv_bool_done");
                    EmitConditionalJump(X86Condition.P, trueLabel);
                    EmitConditionalJump(X86Condition.Ne, trueLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(dst, 4), Reg(dst, 4)));
                    EmitJump(doneLabel);
                    _owner.DefineLabel(trueLabel);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, 4), Imm(1)));
                    _owner.DefineLabel(doneLabel);
                }

                private void EmitIntegerNarrow(MachineRegister destination, MachineRegister source, int size, bool signed)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register src = ToX86Register(source, Target);
                    _owner.Emit(X86Instruction.Binary(
                        signed ? X86InstrKind.Movsx : X86InstrKind.Movzx,
                        Reg(dst, 4),
                        Reg(src, size)));
                }

                private void EmitIntegerTo64(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    bool sourceUnsigned)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register src = ToX86Register(source, Target);
                    bool source64 = sourceKind == GenStackKind.I8 ||
                        sourceKind is GenStackKind.NativeInt or GenStackKind.NativeUInt ||
                        sourceType?.SizeOf == 8;
                    if (source64)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, 8), Reg(src, 8)));
                        return;
                    }

                    _owner.Emit(sourceUnsigned
                        ? X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, 4), Reg(src, 4))
                        : X86Instruction.Binary(X86InstrKind.Movsxd, Reg(dst, 8), Reg(src, 4)));
                }

                private void EmitToFloat(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    bool sourceUnsigned,
                    int destinationSize)
                {
                    int sourceSize = StorageSize(sourceType, sourceKind);
                    if (IsFloating(sourceType, sourceKind))
                    {
                        if (sourceSize == destinationSize)
                        {
                            EmitRegisterMove(destination, source, destinationSize);
                            return;
                        }

                        _owner.Emit(X86Instruction.Binary(
                            destinationSize == 4 ? X86InstrKind.Cvtsd2ss : X86InstrKind.Cvtss2sd,
                            Reg(ToX86Register(destination, Target), destinationSize),
                            Reg(ToX86Register(source, Target), sourceSize)));
                        return;
                    }

                    bool source64 = sourceKind == GenStackKind.I8 ||
                        sourceKind is GenStackKind.NativeInt or GenStackKind.NativeUInt ||
                        sourceType?.SizeOf == 8;
                    if (sourceUnsigned)
                    {
                        EmitUnsignedIntegerToFloat(destination, source, source64, destinationSize);
                        return;
                    }

                    _owner.Emit(X86Instruction.Binary(
                        destinationSize == 4 ? X86InstrKind.Cvtsi2ss : X86InstrKind.Cvtsi2sd,
                        Reg(ToX86Register(destination, Target), destinationSize),
                        Reg(ToX86Register(source, Target), source64 ? 8 : 4)));
                }

                private void EmitUnsignedIntegerToFloat(
                    MachineRegister destination,
                    MachineRegister source,
                    bool source64,
                    int destinationSize)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register src = ToX86Register(source, Target);
                    X86InstrKind convert = destinationSize == 4 ? X86InstrKind.Cvtsi2ss : X86InstrKind.Cvtsi2sd;
                    X86InstrKind add = destinationSize == 4 ? X86InstrKind.Addss : X86InstrKind.Addsd;

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R10, Target.PointerSize)));
                    if (!source64)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R10, 4), Reg(src, 4)));
                        _owner.Emit(X86Instruction.Binary(convert, Reg(dst, destinationSize), Reg(X86Register.R10, 8)));
                        _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                        return;
                    }

                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R10, 8), Reg(src, 8)));
                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_u64_to_float_non_negative");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_u64_to_float_done");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(X86Register.R10, 8), Reg(X86Register.R10, 8)));
                    EmitConditionalJump(X86Condition.Ns, nonNegative);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R11, 8), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.R11, 8), Imm(1)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Shr, Reg(X86Register.R10, 8), Imm(1)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Or, Reg(X86Register.R10, 8), Reg(X86Register.R11, 8)));
                    _owner.Emit(X86Instruction.Binary(convert, Reg(dst, destinationSize), Reg(X86Register.R10, 8)));
                    _owner.Emit(X86Instruction.Binary(add, Reg(dst, destinationSize), Reg(dst, destinationSize)));
                    EmitJump(done);
                    _owner.DefineLabel(nonNegative);
                    _owner.Emit(X86Instruction.Binary(convert, Reg(dst, destinationSize), Reg(X86Register.R10, 8)));
                    _owner.DefineLabel(done);
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R11, Target.PointerSize)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.R10, Target.PointerSize)));
                }

                private void EmitFloatToInteger(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    int targetBits,
                    bool targetUnsigned)
                {
                    int sourceSize = StorageSize(sourceType, sourceKind);
                    if (targetUnsigned && targetBits == 64)
                    {
                        EmitFloatToUInt64(destination, source, sourceSize);
                        return;
                    }

                    X86Register dst = ToX86Register(destination, Target);
                    X86Register src = ToX86Register(source, Target);
                    int conversionSize = targetBits == 64 || (targetUnsigned && targetBits == 32) ? 8 : 4;
                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Cvttss2si : X86InstrKind.Cvttsd2si,
                        Reg(dst, conversionSize),
                        Reg(src, sourceSize)));

                    if (targetBits == 8)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            targetUnsigned ? X86InstrKind.Movzx : X86InstrKind.Movsx,
                            Reg(dst, 4),
                            Reg(dst, 1)));
                    }
                    else if (targetBits == 16)
                    {
                        _owner.Emit(X86Instruction.Binary(
                            targetUnsigned ? X86InstrKind.Movzx : X86InstrKind.Movsx,
                            Reg(dst, 4),
                            Reg(dst, 2)));
                    }
                    else if (targetUnsigned && targetBits == 32)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(dst, 4), Reg(dst, 4)));
                    }
                }

                private void EmitFloatToUInt64(MachineRegister destination, MachineRegister source, int sourceSize)
                {
                    X86Register dst = ToX86Register(destination, Target);
                    X86Register src = ToX86Register(source, Target);
                    X86Operand threshold = FloatingConstantOperand(
                        9223372036854775808.0,
                        sourceSize,
                        sourceSize == 4 ? "f32_two63" : "f64_two63");
                    string below = _owner.CreateLocalLabel($"{_methodLabel}_float_to_u64_below_two63");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_float_to_u64_done");

                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd,
                        Reg(src, sourceSize),
                        threshold));
                    EmitConditionalJump(X86Condition.B, below);

                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Movdqu,
                        Mem(X86Register.Rsp, 0, 16),
                        Reg(X86Register.Xmm15, 16)));
                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                        Reg(X86Register.Xmm15, sourceSize),
                        Reg(src, sourceSize)));
                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Subss : X86InstrKind.Subsd,
                        Reg(X86Register.Xmm15, sourceSize),
                        threshold));
                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Cvttss2si : X86InstrKind.Cvttsd2si,
                        Reg(dst, 8),
                        Reg(X86Register.Xmm15, sourceSize)));
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Movdqu,
                        Reg(X86Register.Xmm15, 16),
                        Mem(X86Register.Rsp, 0, 16)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));

                    X86Register scratch = dst == X86Register.R10 ? X86Register.R11 : X86Register.R10;
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(scratch, 8)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(scratch, 8), Imm(long.MinValue)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Or, Reg(dst, 8), Reg(scratch, 8)));
                    _owner.Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(scratch, 8)));
                    EmitJump(done);

                    _owner.DefineLabel(below);
                    _owner.Emit(X86Instruction.Binary(
                        sourceSize == 4 ? X86InstrKind.Cvttss2si : X86InstrKind.Cvttsd2si,
                        Reg(dst, 8),
                        Reg(src, sourceSize)));
                    _owner.DefineLabel(done);
                }

                private void EmitCheckedFloatConversionGuard(
                    GenTree node,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind)
                {
                    int bits;
                    bool unsigned;
                    switch (node.ConvKind)
                    {
                        case NumericConvKind.I1: bits = 8; unsigned = false; break;
                        case NumericConvKind.U1: bits = 8; unsigned = true; break;
                        case NumericConvKind.I2: bits = 16; unsigned = false; break;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char: bits = 16; unsigned = true; break;
                        case NumericConvKind.I4: bits = 32; unsigned = false; break;
                        case NumericConvKind.U4: bits = 32; unsigned = true; break;
                        case NumericConvKind.I8:
                        case NumericConvKind.NativeInt: bits = 64; unsigned = false; break;
                        case NumericConvKind.U8:
                        case NumericConvKind.NativeUInt: bits = 64; unsigned = true; break;
                        default: return;
                    }

                    int size = StorageSize(sourceType, sourceKind);
                    double minimum = -Math.Pow(2.0, bits - 1);
                    double lowerBoundary;
                    X86Condition lowerOverflowCondition;
                    if (unsigned)
                    {
                        lowerBoundary = -1.0;
                        lowerOverflowCondition = X86Condition.Be;
                    }
                    else if (bits == 64 || (bits == 32 && size == 4))
                    {
                        lowerBoundary = minimum;
                        lowerOverflowCondition = X86Condition.B;
                    }
                    else
                    {
                        lowerBoundary = minimum - 1.0;
                        lowerOverflowCondition = X86Condition.Be;
                    }

                    double maximumExclusive = bits == 64
                        ? unsigned ? 18446744073709551616.0 : 9223372036854775808.0
                        : Math.Pow(2.0, unsigned ? bits : bits - 1);
                    X86Register src = ToX86Register(source, Target);
                    X86Operand lowerOperand = FloatingConstantOperand(lowerBoundary, size, "conv_min");
                    X86Operand maximumOperand = FloatingConstantOperand(maximumExclusive, size, "conv_max");
                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_conv_overflow_{node.LinearId}");
                    string valid = _owner.CreateLocalLabel($"{_methodLabel}_conv_valid_{node.LinearId}");

                    _owner.Emit(X86Instruction.Binary(
                        size == 4 ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd,
                        Reg(src, size),
                        lowerOperand));
                    EmitConditionalJump(X86Condition.P, overflow);
                    EmitConditionalJump(lowerOverflowCondition, overflow);
                    _owner.Emit(X86Instruction.Binary(
                        size == 4 ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd,
                        Reg(src, size),
                        maximumOperand));
                    EmitConditionalJump(X86Condition.P, overflow);
                    EmitConditionalJump(X86Condition.Ae, overflow);
                    EmitJump(valid);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(valid);
                }

                private X86Operand FloatingConstantOperand(double value, int size, string prefix)
                {
                    byte[] bytes = size == 4
                        ? BitConverter.GetBytes((float)value)
                        : BitConverter.GetBytes(value);
                    string label = _owner.AddConstantData(bytes, size, prefix);
                    return _owner.SymbolMemory(label, 0, size);
                }

                private void EmitIndirect(GenTree node)
                {
                    int size = StorageSize(node.RuntimeType ?? node.Type, node.StackKind);
                    if (node.TreeKind == GenTreeKind.LoadIndirect)
                    {
                        MachineRegister address = RequireUseRegister(node, 0);
                        MachineRegister destination = RequireResultRegister(node);
                        X86Register addressRegister = ToX86Register(address, Target);
                        if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                            EmitNullCheck(node, addressRegister, "indirect_load");
                        EmitScalarLoad(
                            ToX86Register(destination, Target),
                            Mem(addressRegister, 0, size),
                            node.RuntimeType ?? node.Type,
                            size);
                        return;
                    }

                    if (node.Uses.Length != 2 || !node.Uses[0].IsRegister || !node.Uses[1].IsRegister)
                        throw Unsupported(node, "indirect store requires address and value registers");
                    X86Register destinationRegister = ToX86Register(node.Uses[0].Register, Target);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, destinationRegister, "indirect_store");
                    EmitScalarStore(
                        Mem(destinationRegister, 0, size),
                        ToX86Register(node.Uses[1].Register, Target),
                        size);
                }

                private void EmitNullCheck(GenTree node, X86Register value, string suffix)
                {
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_{suffix}_non_null_{node.LinearId}");
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(value, 8), Reg(value, 8)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        X86Condition.Ne,
                        X86Operand.SymbolOperand(nonNull, 4, X86ObjectRelocationKind.Relative32)));
                    EmitManagedExceptionThrow(node, "NullReferenceException");
                    _owner.DefineLabel(nonNull);
                }

                private void EmitConditionalBranch(GenTree node)
                {
                    bool branchWhenTrue = node.TreeKind == GenTreeKind.BranchTrue;
                    if (node.SourceOp is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un)
                    {
                        MachineRegister left = RequireUseRegister(node, 0);
                        MachineRegister right = RequireUseRegister(node, 1);
                        RuntimeType? type = OperandType(node, 0);
                        GenStackKind kind = OperandStackKind(node, 0);
                        if (IsFloating(type, kind))
                        {
                            EmitFloatingConditionalBranch(node, left, right, StorageSize(type, kind), branchWhenTrue);
                            return;
                        }

                        int size = StorageSize(type, kind);
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Cmp,
                            Reg(ToX86Register(left, Target), size),
                            Reg(ToX86Register(right, Target), size)));
                        _owner.Emit(X86Instruction.ConditionalBranch(
                            ComparisonBranchCondition(node.SourceOp, branchWhenTrue),
                            X86Operand.SymbolOperand(LabelForTarget(node), 4, X86ObjectRelocationKind.Relative32)));
                        return;
                    }
                    MachineRegister condition = RequireUseRegister(node, 0);
                    int conditionSize = StorageSize(OperandType(node, 0), OperandStackKind(node, 0));
                    X86Register register = ToX86Register(condition, Target);
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(register, conditionSize), Reg(register, conditionSize)));
                    _owner.Emit(X86Instruction.ConditionalBranch(
                        branchWhenTrue ? X86Condition.Ne : X86Condition.E,
                        X86Operand.SymbolOperand(LabelForTarget(node), 4, X86ObjectRelocationKind.Relative32)));
                }

                private void EmitFloatingConditionalBranch(
                    GenTree node,
                    MachineRegister left,
                    MachineRegister right,
                    int size,
                    bool branchWhenTrue)
                {
                    EmitFloatingCompareFlags(node, left, right, size);
                    string target = LabelForTarget(node);
                    string fallthrough = _owner.CreateLocalLabel($"{_methodLabel}_fcmp_fallthrough_{node.LinearId}");

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Ceq:
                            if (branchWhenTrue)
                            {
                                EmitConditionalJump(X86Condition.P, fallthrough);
                                EmitConditionalJump(X86Condition.E, target);
                            }
                            else
                            {
                                EmitConditionalJump(X86Condition.P, target);
                                EmitConditionalJump(X86Condition.Ne, target);
                            }
                            break;
                        case BytecodeOp.Clt:
                            if (branchWhenTrue)
                            {
                                EmitConditionalJump(X86Condition.P, fallthrough);
                                EmitConditionalJump(X86Condition.B, target);
                            }
                            else
                            {
                                EmitConditionalJump(X86Condition.P, target);
                                EmitConditionalJump(X86Condition.Ae, target);
                            }
                            break;
                        case BytecodeOp.Clt_Un:
                            EmitConditionalJump(branchWhenTrue ? X86Condition.B : X86Condition.Ae, target);
                            break;
                        case BytecodeOp.Cgt:
                            EmitConditionalJump(branchWhenTrue ? X86Condition.A : X86Condition.Be, target);
                            break;
                        case BytecodeOp.Cgt_Un:
                            if (branchWhenTrue)
                            {
                                EmitConditionalJump(X86Condition.P, target);
                                EmitConditionalJump(X86Condition.A, target);
                            }
                            else
                            {
                                EmitConditionalJump(X86Condition.P, fallthrough);
                                EmitConditionalJump(X86Condition.Be, target);
                            }
                            break;
                        default:
                            throw Unsupported(node, $"unsupported floating branch comparison {node.SourceOp}");
                    }

                    _owner.DefineLabel(fallthrough);
                }

                private static X86Condition ComparisonBranchCondition(BytecodeOp op, bool branchWhenTrue)
                {
                    if (branchWhenTrue)
                    {
                        return op switch
                        {
                            BytecodeOp.Ceq => X86Condition.E,
                            BytecodeOp.Clt => X86Condition.L,
                            BytecodeOp.Clt_Un => X86Condition.B,
                            BytecodeOp.Cgt => X86Condition.G,
                            BytecodeOp.Cgt_Un => X86Condition.A,
                            _ => throw new ArgumentOutOfRangeException(nameof(op))
                        };
                    }

                    return op switch
                    {
                        BytecodeOp.Ceq => X86Condition.Ne,
                        BytecodeOp.Clt => X86Condition.Ge,
                        BytecodeOp.Clt_Un => X86Condition.Ae,
                        BytecodeOp.Cgt => X86Condition.Le,
                        BytecodeOp.Cgt_Un => X86Condition.Be,
                        _ => throw new ArgumentOutOfRangeException(nameof(op))
                    };
                }

                private void EmitDefaultValue(GenTree node)
                {
                    RuntimeType? type = node.RuntimeType ?? node.Type;
                    int size = StorageSize(type, node.StackKind);
                    if (node.Results.Length == 1 && node.Results[0].IsRegister)
                    {
                        EmitZeroRegister(node.Results[0].Register);
                        return;
                    }
                    if (node.Results.Length == 1 && node.Results[0].IsFrameSlot)
                    {
                        ZeroFrame(node.Results[0], size);
                        return;
                    }
                    if (node.Results.Length == 0)
                        return;
                    throw Unsupported(node, "unsupported default-value result");
                }

                private void EmitMoveBetween(RegisterOperand destination, RegisterOperand source, int size)
                {
                    if (destination.Equals(source))
                        return;
                    if (source.IsAddress)
                    {
                        if (!destination.IsRegister)
                            throw new InvalidOperationException("Address move destination must be a register.");
                        EmitLoadAddress(destination.Register, source);
                    }
                    else if (source.IsRegister && destination.IsRegister)
                        EmitRegisterMove(destination.Register, source.Register, size);
                    else if (!source.IsRegister && destination.IsRegister)
                        EmitLoad(destination.Register, source, size);
                    else if (source.IsRegister)
                        EmitStore(destination, source.Register, size);
                    else
                        EmitMemoryToMemory(destination, source, size);
                }

                private void EmitRegisterMove(MachineRegister destination, MachineRegister source, int size)
                {
                    if (destination == source)
                        return;

                    RegisterClass destinationClass = MachineRegisters.GetClass(destination);
                    RegisterClass sourceClass = MachineRegisters.GetClass(source);
                    if (destinationClass != sourceClass)
                    {
                        if (size is not (4 or 8) ||
                            destinationClass is not (RegisterClass.General or RegisterClass.Float) ||
                            sourceClass is not (RegisterClass.General or RegisterClass.Float))
                        {
                            throw new NotSupportedException(
                                $"Unsupported cross-class register move from {sourceClass} to {destinationClass} with size {size}.");
                        }

                        X86Register destinationRegister = ToX86Register(destination, Target);
                        X86Register sourceRegister = ToX86Register(source, Target);
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));
                        if (sourceClass == RegisterClass.Float)
                        {
                            _owner.Emit(X86Instruction.Binary(
                                size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                                Mem(X86Register.Rsp, 0, size),
                                Reg(sourceRegister, size)));
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Reg(destinationRegister, size),
                                Mem(X86Register.Rsp, 0, size)));
                        }
                        else
                        {
                            _owner.Emit(X86Instruction.Binary(
                                X86InstrKind.Mov,
                                Mem(X86Register.Rsp, 0, size),
                                Reg(sourceRegister, size)));
                            _owner.Emit(X86Instruction.Binary(
                                size == 4 ? X86InstrKind.Movss : X86InstrKind.Movsd,
                                Reg(destinationRegister, size),
                                Mem(X86Register.Rsp, 0, size)));
                        }
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, Target.PointerSize), Imm(16)));
                        return;
                    }

                    X86InstrKind opcode = destinationClass switch
                    {
                        RegisterClass.General => X86InstrKind.Mov,
                        RegisterClass.Float when size == 4 => X86InstrKind.Movss,
                        RegisterClass.Float when size == 8 => X86InstrKind.Movsd,
                        RegisterClass.Float when size == 16 => X86InstrKind.Movdqu,
                        _ => throw new NotSupportedException($"Unsupported register move class {destinationClass} with size {size}.")
                    };
                    _owner.Emit(X86Instruction.Binary(
                        opcode,
                        Reg(ToX86Register(destination, Target), size),
                        Reg(ToX86Register(source, Target), size)));
                }

                private void EmitLoad(MachineRegister destination, RegisterOperand source, int size)
                {
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Load source is not a finalized frame slot: {source}.");

                    RegisterClass registerClass = MachineRegisters.GetClass(destination);
                    X86InstrKind opcode = registerClass switch
                    {
                        RegisterClass.General => X86InstrKind.Mov,
                        RegisterClass.Float when size == 4 => X86InstrKind.Movss,
                        RegisterClass.Float when size == 8 => X86InstrKind.Movsd,
                        RegisterClass.Float when size == 16 => X86InstrKind.Movdqu,
                        _ => throw new NotSupportedException($"Unsupported x86 frame load class {registerClass} with size {size}.")
                    };
                    _owner.Emit(X86Instruction.Binary(
                        opcode,
                        Reg(ToX86Register(destination, Target), size),
                        FrameMemory(source, size)));
                }

                private void EmitStore(RegisterOperand destination, MachineRegister source, int size)
                {
                    if (!destination.IsFrameSlot)
                        throw new InvalidOperationException($"Store destination is not a finalized frame slot: {destination}.");

                    RegisterClass registerClass = MachineRegisters.GetClass(source);
                    X86InstrKind opcode = registerClass switch
                    {
                        RegisterClass.General => X86InstrKind.Mov,
                        RegisterClass.Float when size == 4 => X86InstrKind.Movss,
                        RegisterClass.Float when size == 8 => X86InstrKind.Movsd,
                        RegisterClass.Float when size == 16 => X86InstrKind.Movdqu,
                        _ => throw new NotSupportedException($"Unsupported frame store class {registerClass} with size {size}.")
                    };
                    _owner.Emit(X86Instruction.Binary(
                        opcode,
                        FrameMemory(destination, size),
                        Reg(ToX86Register(source, Target), size)));
                }

                private void EmitMemoryToMemory(RegisterOperand destination, RegisterOperand source, int size)
                {
                    if (!destination.IsFrameSlot || !source.IsFrameSlot)
                        throw new InvalidOperationException("Memory move operands must be finalized frame slots.");
                    if (size == 16)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R10, 8), FrameMemory(source, 8)));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, FrameMemory(destination, 8), Reg(X86Register.R10, 8)));
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            Reg(X86Register.R10, 8),
                            FrameMemory(source, 8).WithDisplacement(checked(EffectiveFrameOffset(source) + 8))));
                        _owner.Emit(X86Instruction.Binary(
                            X86InstrKind.Mov,
                            FrameMemory(destination, 8).WithDisplacement(checked(EffectiveFrameOffset(destination) + 8)),
                            Reg(X86Register.R10, 8)));
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.R10, size), FrameMemory(source, size)));
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, FrameMemory(destination, size), Reg(X86Register.R10, size)));
                }

                private void EmitLoadAddress(MachineRegister destination, RegisterOperand source)
                {
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Address source is not a finalized frame slot: {source}.");
                    _owner.Emit(X86Instruction.Binary(
                        X86InstrKind.Lea,
                        Reg(ToX86Register(destination, Target), Target.PointerSize),
                        FrameMemory(source, Target.PointerSize)));
                }

                private void EmitIntegerConstant(MachineRegister destination, long value, int size)
                {
                    X86Register register = ToX86Register(destination, Target);
                    if (value == 0)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(register, 4), Reg(register, 4)));
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(register, size), Imm(value)));
                }

                private void EmitZeroRegister(MachineRegister destination)
                {
                    X86Register register = ToX86Register(destination, Target);
                    if (MachineRegisters.GetClass(destination) == RegisterClass.Float)
                    {
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Xorps, Reg(register, 16), Reg(register, 16)));
                        return;
                    }
                    _owner.Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(register, 4), Reg(register, 4)));
                }

                private void ZeroFrame(RegisterOperand destination, int size)
                {
                    for (int offset = 0; offset < size; offset++)
                    {
                        X86Operand memory = FrameMemory(destination, 1).WithDisplacement(checked(EffectiveFrameOffset(destination) + offset));
                        _owner.Emit(X86Instruction.Binary(X86InstrKind.Mov, memory, Imm(0)));
                    }
                }

                private RegisterOperand FrameSlotForLocalLike(GenTree node, int size, RegisterClass registerClass)
                {
                    StackFrameSlot slot;
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.Local:
                        case GenTreeKind.StoreLocal:
                        case GenTreeKind.LocalAddr:
                            if (!_method.StackFrame.TryGetLocalSlot(node.Int32, out slot))
                                throw new InvalidOperationException($"No finalized frame slot for local {node.Int32}.");
                            break;
                        case GenTreeKind.Arg:
                        case GenTreeKind.StoreArg:
                        case GenTreeKind.ArgAddr:
                            if (!_method.StackFrame.TryGetArgumentSlot(node.Int32, out slot))
                                throw new InvalidOperationException($"No finalized frame slot for argument {node.Int32}.");
                            break;
                        case GenTreeKind.Temp:
                        case GenTreeKind.StoreTemp:
                        case GenTreeKind.TempAddr:
                            if (!_method.StackFrame.TryGetTempSlot(node.Int32, out slot))
                                throw new InvalidOperationException($"No finalized frame slot for temp {node.Int32}.");
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported local-like tree {node.TreeKind}.");
                    }

                    if (registerClass == RegisterClass.Invalid)
                        registerClass = RegisterClass.General;
                    return RegisterOperand.ForFrameSlot(
                        registerClass,
                        slot.Kind,
                        _method.StackFrame.UsesFramePointer ? RegisterFrameBase.FramePointer : RegisterFrameBase.StackPointer,
                        slot.Index,
                        slot.Offset,
                        slot.Size > 0 ? slot.Size : size);
                }

                private RegisterOperand FrameSlotForAddress(GenTree node)
                    => FrameSlotForLocalLike(node, Target.PointerSize, RegisterClass.General).AsAddress();

                private X86Operand FrameMemory(RegisterOperand operand, int size)
                    => Mem(FrameBase(operand), EffectiveFrameOffset(operand), size);

                private X86Register FrameBase(RegisterOperand operand)
                {
                    return operand.FrameBase switch
                    {
                        RegisterFrameBase.StackPointer => X86Register.Rsp,
                        RegisterFrameBase.FramePointer => X86Register.Rbp,
                        RegisterFrameBase.IncomingArgumentBase => _method.StackFrame.UsesFramePointer ? X86Register.Rbp : X86Register.Rsp,
                        _ => throw new InvalidOperationException($"Invalid frame base {operand.FrameBase}."),
                    };
                }

                private int EffectiveFrameOffset(RegisterOperand operand)
                    => operand.FrameBase == RegisterFrameBase.IncomingArgumentBase
                        ? checked(operand.FrameOffset + _method.StackFrame.FrameSize)
                        : operand.FrameOffset;

                private void EmitFallthroughFixup(int blockId, int nextBlockId)
                {
                    foreach (CfgEdge edge in _method.Cfg.Blocks[blockId].Successors)
                    {
                        if (edge.Kind == CfgEdgeKind.FallThrough && edge.ToBlockId != nextBlockId)
                        {
                            EmitJump(_blockLabels[edge.ToBlockId]);
                            return;
                        }
                    }
                }

                private void EmitJump(string target)
                    => _owner.Emit(X86Instruction.Branch(
                        X86InstrKind.Jmp,
                        X86Operand.SymbolOperand(target, 4, X86ObjectRelocationKind.Relative32)));

                private string LabelForTarget(GenTree node)
                {
                    if ((uint)node.TargetBlockId >= (uint)_blockLabels.Length)
                        throw Unsupported(node, $"invalid branch target block {node.TargetBlockId}");
                    return _blockLabels[node.TargetBlockId];
                }

                private int RequireCodegenUseIndexForOperand(GenTree node, int operandIndex, string context)
                {
                    if (TryGetCodegenUseIndexForOperand(node, operandIndex, out int useIndex) &&
                        (uint)useIndex < (uint)node.Uses.Length)
                    {
                        return useIndex;
                    }

                    throw Unsupported(node, $"{context} has no codegen use for operand {operandIndex}");
                }

                private bool TryGetCodegenUseIndexForOperand(GenTree node, int operandIndex, out int useIndex)
                {
                    useIndex = -1;
                    if (operandIndex < 0)
                        return false;

                    if (node.Operands.IsDefaultOrEmpty)
                    {
                        if ((uint)operandIndex < (uint)node.RegisterUses.Length &&
                            (uint)operandIndex < (uint)node.Uses.Length)
                        {
                            useIndex = operandIndex;
                            return true;
                        }

                        return false;
                    }

                    if ((uint)operandIndex >= (uint)node.Operands.Length)
                        return false;

                    int flagCount = node.OperandFlags.IsDefaultOrEmpty ? 0 : node.OperandFlags.Length;
                    int codegenUseCursor = 0;
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        LirOperandFlags flags = i < flagCount ? node.OperandFlags[i] : LirOperandFlags.None;
                        if ((flags & LirOperandFlags.Contained) != 0)
                            continue;

                        GenTree operand = node.Operands[i];
                        GenTree value = operand.RegisterResult ?? operand;
                        int slot = FindCodegenUseSlot(node, value.LinearValueKey, codegenUseCursor);
                        if (slot < 0)
                        {
                            if (i == operandIndex)
                                return false;
                            continue;
                        }

                        if (i == operandIndex)
                        {
                            useIndex = slot;
                            return true;
                        }

                        codegenUseCursor = slot + CodegenUseSlotCountForOperand(value);
                    }

                    return false;
                }

                private static int FindCodegenUseSlot(GenTree node, GenTreeValueKey operandValueKey, int startIndex)
                {
                    var useValues = node.LsraInfo.CodegenUseValues;
                    for (int i = Math.Max(0, startIndex); i < useValues.Length && i < node.Uses.Length; i++)
                    {
                        if (useValues[i].Equals(operandValueKey))
                            return i;
                    }

                    return -1;
                }

                private int CodegenUseSlotCountForOperand(GenTree value)
                {
                    if (_method.ValueInfoByNode.TryGetValue(value.LinearValueKey, out GenTreeValueInfo info))
                    {
                        var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind, _method.Target);
                        if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                            return MachineAbi.GetRegisterSegments(abi, _method.Target).Length;
                    }

                    return 1;
                }

                private RuntimeType? OperandType(GenTree node, int operandIndex)
                {
                    if (TryGetCodegenUseIndexForOperand(node, operandIndex, out int useIndex) &&
                        (uint)useIndex < (uint)node.RegisterUses.Length)
                    {
                        return node.RegisterUses[useIndex].RuntimeType ?? node.RegisterUses[useIndex].Type;
                    }
                    if ((uint)operandIndex < (uint)node.Operands.Length)
                        return node.Operands[operandIndex].RuntimeType ?? node.Operands[operandIndex].Type;
                    return node.RuntimeType ?? node.Type;
                }

                private GenStackKind OperandStackKind(GenTree node, int operandIndex)
                {
                    if (TryGetCodegenUseIndexForOperand(node, operandIndex, out int useIndex) &&
                        (uint)useIndex < (uint)node.RegisterUses.Length)
                    {
                        return node.RegisterUses[useIndex].StackKind;
                    }
                    if ((uint)operandIndex < (uint)node.Operands.Length)
                        return node.Operands[operandIndex].StackKind;
                    return node.StackKind;
                }

                private static RuntimeType? ValueType(GenTree node)
                    => node.RegisterResult?.RuntimeType ?? node.RegisterResult?.Type ??
                       (node.RegisterUses.Length != 0 ? node.RegisterUses[0].RuntimeType ?? node.RegisterUses[0].Type : null) ??
                       node.RuntimeType ?? node.Type;

                private static GenStackKind ValueStackKind(GenTree node)
                    => node.RegisterResult?.StackKind ??
                       (node.RegisterUses.Length != 0 ? node.RegisterUses[0].StackKind : node.StackKind);

                private static RuntimeType RequireRuntimeType(GenTree node)
                    => node.RuntimeType ?? node.Type ?? throw new InvalidOperationException("GenTree has no runtime type.");

                private static MachineRegister RequireResultRegister(GenTree node)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw new InvalidOperationException($"GenTree requires one register result: {node.TreeKind}.");
                    return node.Results[0].Register;
                }

                private static MachineRegister RequireUseRegister(GenTree node, int index)
                {
                    if ((uint)index >= (uint)node.Uses.Length || !node.Uses[index].IsRegister)
                        throw new InvalidOperationException($"GenTree use is not a register: {node.TreeKind} use {index}.");
                    return node.Uses[index].Register;
                }

                private int StorageSize(RuntimeType? type, GenStackKind kind, RegisterOperand destination, RegisterOperand source)
                {
                    int explicitSize = destination.FrameSlotSize > 0 ? destination.FrameSlotSize : source.FrameSlotSize;
                    return explicitSize > 0 ? Math.Min(8, explicitSize) : StorageSize(type, kind);
                }

                private int StorageSize(RuntimeType? type, GenStackKind kind)
                {
                    if (kind is GenStackKind.Ref or GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Null)
                        return Target.PointerSize;
                    if (kind == GenStackKind.I8)
                        return 8;
                    if (kind == GenStackKind.I4)
                        return 4;
                    if (kind == GenStackKind.R4)
                        return 4;
                    if (kind == GenStackKind.R8)
                        return 8;
                    if (type is not null)
                    {
                        if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                            return Target.PointerSize;
                        if (type.SizeOf is 1 or 2 or 4 or 8)
                            return type.SizeOf;
                    }
                    return Target.PointerSize;
                }

                private static bool TryGetContainedIntegerImmediate(GenTree node, int operandIndex, out long value)
                {
                    value = 0;
                    if ((uint)operandIndex >= (uint)node.Operands.Length)
                        return false;
                    if (node.OperandFlags.IsDefaultOrEmpty || operandIndex >= node.OperandFlags.Length ||
                        (node.OperandFlags[operandIndex] & LirOperandFlags.Contained) == 0)
                    {
                        return false;
                    }

                    GenTree operand = node.Operands[operandIndex];
                    if (operand.Kind == GenTreeKind.ConstI4)
                    {
                        value = operand.Int32;
                        return true;
                    }
                    if (operand.Kind == GenTreeKind.ConstI8)
                    {
                        value = operand.Int64;
                        return true;
                    }
                    return false;
                }

                private static bool IsUnsigned(RuntimeType? type)
                    => type?.PrimitiveKind is
                        RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                        RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.UInt16 or
                        RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.UInt64 or
                        RuntimePrimitiveKind.NativeUInt;

                private static bool IsFloating(RuntimeType? type, GenStackKind kind)
                    => kind is GenStackKind.R4 or GenStackKind.R8 ||
                       type?.PrimitiveKind is RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double;

                private static NotSupportedException Unsupported(GenTree node, string message)
                    => new NotSupportedException($"Code generation failed for {node.TreeKind} at IL_{node.Pc:X4}, LIR {node.LinearId}: {message}.");
            }

            private readonly struct RuntimeMetadataLabels
            {
                public readonly string SafePointTableLabel;
                public readonly string TypeInfoTableLabel;
                public readonly string StaticRootTableLabel;

                public RuntimeMetadataLabels(string safePointTableLabel, string typeInfoTableLabel, string staticRootTableLabel)
                {
                    SafePointTableLabel = safePointTableLabel;
                    TypeInfoTableLabel = typeInfoTableLabel;
                    StaticRootTableLabel = staticRootTableLabel;
                }
            }

            private sealed class EhMethodDraft
            {
                public GenTreeMethod Method { get; }
                public string InfoLabel { get; }
                public string ClausesLabel { get; }
                public ImmutableArray<EhClauseDraft> Clauses { get; }

                public EhMethodDraft(
                    GenTreeMethod method,
                    string infoLabel,
                    string clausesLabel,
                    ImmutableArray<EhClauseDraft> clauses)
                {
                    Method = method;
                    InfoLabel = infoLabel;
                    ClausesLabel = clausesLabel;
                    Clauses = clauses;
                }
            }

            private sealed class StaticExceptionDraft
            {
                public RuntimeType Type { get; }
                public string ObjectLabel { get; }
                public string TypeDescriptorLabel { get; }

                public StaticExceptionDraft(RuntimeType type, string objectLabel, string typeDescriptorLabel)
                {
                    Type = type;
                    ObjectLabel = objectLabel;
                    TypeDescriptorLabel = typeDescriptorLabel;
                }
            }

            private sealed class EhClauseDraft
            {
                public CfgExceptionRegion Region { get; }
                public int Kind { get; }
                public string? CatchTypeLabel { get; }
                public int ParentLocalIndex { get; }
                public string? TryStartLabel { get; set; }
                public string? TryEndLabel { get; set; }
                public string? HandlerStartLabel { get; set; }
                public string? HandlerEndLabel { get; set; }

                public EhClauseDraft(
                    CfgExceptionRegion region,
                    int kind,
                    string? catchTypeLabel,
                    int parentLocalIndex)
                {
                    Region = region;
                    Kind = kind;
                    CatchTypeLabel = catchTypeLabel;
                    ParentLocalIndex = parentLocalIndex;
                }
            }

            private readonly struct DelegateTargetThunkKey : IEquatable<DelegateTargetThunkKey>
            {
                public readonly int DelegateTypeId;
                public readonly int TargetMethodId;
                public readonly bool Closed;

                public DelegateTargetThunkKey(int delegateTypeId, int targetMethodId, bool closed)
                {
                    DelegateTypeId = delegateTypeId;
                    TargetMethodId = targetMethodId;
                    Closed = closed;
                }

                public bool Equals(DelegateTargetThunkKey other)
                    => DelegateTypeId == other.DelegateTypeId &&
                       TargetMethodId == other.TargetMethodId &&
                       Closed == other.Closed;

                public override bool Equals(object? obj)
                    => obj is DelegateTargetThunkKey other && Equals(other);

                public override int GetHashCode()
                    => HashCode.Combine(DelegateTypeId, TargetMethodId, Closed);
            }

            private sealed class DelegateTargetThunkDraft
            {
                public RuntimeType DelegateType { get; }
                public RuntimeMethod InvokeMethod { get; }
                public RuntimeMethod TargetMethod { get; }
                public bool Closed { get; }
                public string Label { get; }

                public DelegateTargetThunkDraft(
                    RuntimeType delegateType,
                    RuntimeMethod invokeMethod,
                    RuntimeMethod targetMethod,
                    bool closed,
                    string label)
                {
                    DelegateType = delegateType;
                    InvokeMethod = invokeMethod;
                    TargetMethod = targetMethod;
                    Closed = closed;
                    Label = label;
                }
            }

            private readonly struct DelegateAbiSlice
            {
                public readonly AbiArgumentLocation Location;
                public readonly RegisterClass RegisterClass;
                public readonly int ValueOffset;
                public readonly int Size;
                public readonly int SaveOffset;

                public DelegateAbiSlice(
                    AbiArgumentLocation location,
                    RegisterClass registerClass,
                    int valueOffset,
                    int size,
                    int saveOffset)
                {
                    Location = location;
                    RegisterClass = registerClass;
                    ValueOffset = valueOffset;
                    Size = size;
                    SaveOffset = saveOffset;
                }
            }

            private readonly struct DelegateAbiEntity
            {
                public readonly RuntimeType? Type;
                public readonly int SaveBase;
                public readonly ImmutableArray<DelegateAbiSlice> Slices;

                public DelegateAbiEntity(RuntimeType? type, int saveBase, ImmutableArray<DelegateAbiSlice> slices)
                {
                    Type = type;
                    SaveBase = saveBase;
                    Slices = slices;
                }
            }

            private readonly struct DelegateAbiBundle
            {
                public readonly RuntimeMethod Method;
                public readonly DelegateAbiEntity? HiddenReturnBuffer;
                public readonly ImmutableArray<DelegateAbiEntity> LogicalArguments;
                public readonly ImmutableArray<DelegateAbiSlice> OrderedSlices;
                public readonly int TotalSaveSize;
                public readonly int OutgoingStackSize;

                public DelegateAbiBundle(
                    RuntimeMethod method,
                    DelegateAbiEntity? hiddenReturnBuffer,
                    ImmutableArray<DelegateAbiEntity> logicalArguments,
                    ImmutableArray<DelegateAbiSlice> orderedSlices,
                    int totalSaveSize,
                    int outgoingStackSize)
                {
                    Method = method;
                    HiddenReturnBuffer = hiddenReturnBuffer;
                    LogicalArguments = logicalArguments;
                    OrderedSlices = orderedSlices;
                    TotalSaveSize = totalSaveSize;
                    OutgoingStackSize = outgoingStackSize;
                }
            }

            private sealed class StringLiteralDraft
            {
                public string Text { get; }
                public string Label { get; }
                public string TypeDescriptorLabel { get; }

                public StringLiteralDraft(string text, string label, string typeDescriptorLabel)
                {
                    Text = text;
                    Label = label;
                    TypeDescriptorLabel = typeDescriptorLabel;
                }
            }

            private sealed class TypeInitializationThunkDraft
            {
                public RuntimeMethod Initializer { get; }
                public string StateLabel { get; }
                public string Label { get; }

                public TypeInitializationThunkDraft(RuntimeMethod initializer, string stateLabel, string label)
                {
                    Initializer = initializer;
                    StateLabel = stateLabel;
                    Label = label;
                }
            }

            private sealed class StaticStorageDraft
            {
                public RuntimeType Type { get; }
                public string StorageLabel { get; }
                public string? InitializationStateLabel { get; set; }

                public StaticStorageDraft(RuntimeType type, string storageLabel)
                {
                    Type = type;
                    StorageLabel = storageLabel;
                }
            }

            private readonly struct StaticRootDraft
            {
                public readonly string StorageLabel;
                public readonly int Offset;
                public readonly RegisterGcRootKind Kind;

                public StaticRootDraft(string storageLabel, int offset, RegisterGcRootKind kind)
                {
                    StorageLabel = storageLabel;
                    Offset = offset;
                    Kind = kind;
                }
            }

            private sealed class TypeDescriptorDraft
            {
                public RuntimeType Type { get; }
                public string Label { get; }
                public ImmutableArray<RuntimeType> Interfaces { get; }
                public ImmutableArray<TypeGcFieldDraft> Fields { get; set; }
                public ImmutableArray<TypeGcFieldDraft> ComponentFields { get; set; }
                public string? RelatedTypeLabel { get; set; }
                public string? InterfacesLabel { get; set; }
                public string? VTableLabel { get; set; }
                public ImmutableArray<string> VTableTargets { get; set; }
                public string? FieldsLabel { get; set; }
                public string? ComponentFieldsLabel { get; set; }

                public TypeDescriptorDraft(RuntimeType type, string label, ImmutableArray<RuntimeType> interfaces)
                {
                    Type = type;
                    Label = label;
                    Interfaces = interfaces;
                    Fields = ImmutableArray<TypeGcFieldDraft>.Empty;
                    ComponentFields = ImmutableArray<TypeGcFieldDraft>.Empty;
                    VTableTargets = ImmutableArray<string>.Empty;
                }
            }

            private sealed class InterfaceDispatchCellDraft
            {
                public RuntimeMethod DeclaredMethod { get; }
                public string Label { get; }
                public ImmutableArray<InterfaceDispatchEntryDraft> Entries { get; set; }

                public InterfaceDispatchCellDraft(RuntimeMethod declaredMethod, string label)
                {
                    DeclaredMethod = declaredMethod;
                    Label = label;
                    Entries = ImmutableArray<InterfaceDispatchEntryDraft>.Empty;
                }
            }

            private readonly struct InterfaceDispatchEntryDraft
            {
                public readonly string ReceiverTypeLabel;
                public readonly string TargetLabel;

                public InterfaceDispatchEntryDraft(string receiverTypeLabel, string targetLabel)
                {
                    ReceiverTypeLabel = receiverTypeLabel;
                    TargetLabel = targetLabel;
                }
            }

            private sealed class UnboxingStubDraft
            {
                public string TargetLabel { get; }
                public string Label { get; }

                public UnboxingStubDraft(string targetLabel, string label)
                {
                    TargetLabel = targetLabel;
                    Label = label;
                }
            }

            private readonly struct TypeGcFieldDraft
            {
                public readonly int Offset;
                public readonly RegisterGcRootKind Kind;

                public TypeGcFieldDraft(int offset, RegisterGcRootKind kind)
                {
                    Offset = offset;
                    Kind = kind;
                }
            }

            private sealed class SafePointDraft
            {
                public string DescriptorLabel { get; }
                public string ReturnLabel { get; }
                public int SavedFramePointerOffset { get; }
                public int SavedReturnAddressOffset { get; }
                public ImmutableArray<SafePointRootDraft> Roots { get; }
                public string? RootsLabel { get; set; }

                public SafePointDraft(
                    string descriptorLabel,
                    string returnLabel,
                    int savedFramePointerOffset,
                    int savedReturnAddressOffset,
                    ImmutableArray<SafePointRootDraft> roots)
                {
                    DescriptorLabel = descriptorLabel;
                    ReturnLabel = returnLabel;
                    SavedFramePointerOffset = savedFramePointerOffset;
                    SavedReturnAddressOffset = savedReturnAddressOffset;
                    Roots = roots;
                }
            }

            private readonly struct SafePointRootDraft
            {
                public readonly int FrameOffset;
                public readonly RegisterGcRootKind Kind;

                public SafePointRootDraft(int frameOffset, RegisterGcRootKind kind)
                {
                    FrameOffset = frameOffset;
                    Kind = kind;
                }
            }
        }

        private sealed class TextSectionBuilder
        {
            private readonly X86InstructionBuilder _builder;
            private readonly List<X86ObjectRelocation> _relocations = new List<X86ObjectRelocation>();

            public string Name { get; }
            public int ByteLength => _builder.Position;

            public TextSectionBuilder(X86Target target, string name)
            {
                _builder = new X86InstructionBuilder(target);
                Name = name;
            }

            public void DefineLabel(string label)
                => _builder.DefineLabel(label);

            public void Emit(X86Instruction instruction)
                => _builder.Emit(instruction);

            public X86TextSection ToSection()
            {
                X86TextSection section = _builder.ToTextSection();
                return new X86TextSection(section.Instructions, section.Labels, _relocations.ToImmutableArray());
            }
        }

        private sealed class DataSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly List<X86ObjectRelocation> _relocations = new List<X86ObjectRelocation>();
            private readonly X86Target _target;

            public string Name { get; }
            public X86ObjectSectionKind Kind { get; }
            public int Alignment { get; private set; } = 1;
            public int ByteLength => _data.Count;

            public DataSectionBuilder(string name, X86ObjectSectionKind kind, X86Target target)
            {
                Name = name;
                Kind = kind;
                _target = target;
            }

            public int Align(int alignment)
            {
                Alignment = Math.Max(Alignment, alignment);
                int aligned = AlignUp(_data.Count, alignment);
                while (_data.Count < aligned)
                    _data.Add(0);
                return aligned;
            }

            public void EmitBytes(IEnumerable<byte> bytes)
                => _data.AddRange(bytes);

            public void EmitUInt16(ushort value)
            {
                _data.Add((byte)value);
                _data.Add((byte)(value >> 8));
            }

            public void EmitInt32(int value)
                => EmitUInt32(unchecked((uint)value));

            public void EmitUInt32(uint value)
            {
                for (int i = 0; i < 4; i++)
                    _data.Add((byte)(value >> (i * 8)));
            }

            public void EmitPointer(long value)
            {
                ulong bits = unchecked((ulong)value);
                for (int i = 0; i < _target.XLen / 8; i++)
                    _data.Add((byte)(bits >> (i * 8)));
            }

            public void EmitPointerRelocation(string symbol, long addend = 0)
            {
                int offset = _data.Count;
                EmitPointer(0);
                _relocations.Add(new X86ObjectRelocation(
                    Name,
                    offset,
                    symbol,
                    addend,
                    X86ObjectRelocationKind.AbsolutePointer));
            }

            public X86DataSection ToSection()
                => new X86DataSection(
                    Name,
                    Kind,
                    Alignment,
                    _data.ToImmutableArray(),
                    0,
                    _relocations.ToImmutableArray());
        }

        private static X86Register ToX86Register(MachineRegister register, TargetInfo target)
        {
            if (register >= MachineRegister.F0 && register <= MachineRegister.F15)
                return (X86Register)((int)X86Register.Xmm0 + ((int)register - (int)MachineRegister.F0));

            if (target.Architecture == TargetArchitectureKind.I386)
            {
                return register switch
                {
                    MachineRegister.X0 => X86Register.Rax,
                    MachineRegister.X1 => X86Register.Rcx,
                    MachineRegister.X2 => X86Register.Rdx,
                    MachineRegister.X3 => X86Register.Rbx,
                    MachineRegister.X4 => X86Register.Rsi,
                    MachineRegister.X5 => X86Register.Rdi,
                    MachineRegister.X6 => X86Register.Rsp,
                    MachineRegister.X7 => X86Register.Rbp,
                    _ => throw new NotSupportedException($"Unsupported i386 machine register: {register}."),
                };
            }

            if (register == MachineRegister.X10)
                return X86Register.Rbp;
            if (register == MachineRegister.X15)
                return X86Register.Rsp;

            if (target.OperatingSystem == OperatingSystemKind.Windows)
            {
                return register switch
                {
                    MachineRegister.X0 => X86Register.Rax,
                    MachineRegister.X1 => X86Register.Rcx,
                    MachineRegister.X2 => X86Register.Rdx,
                    MachineRegister.X3 => X86Register.R8,
                    MachineRegister.X4 => X86Register.R9,
                    MachineRegister.X5 => X86Register.R10,
                    MachineRegister.X6 => X86Register.R11,
                    MachineRegister.X7 => X86Register.Rbx,
                    MachineRegister.X8 => X86Register.Rsi,
                    MachineRegister.X9 => X86Register.Rdi,
                    MachineRegister.X11 => X86Register.R12,
                    MachineRegister.X12 => X86Register.R13,
                    MachineRegister.X13 => X86Register.R14,
                    MachineRegister.X14 => X86Register.R15,
                    _ => throw new NotSupportedException($"Unsupported Windows x86 machine register: {register}."),
                };
            }

            return register switch
            {
                MachineRegister.X0 => X86Register.Rax,
                MachineRegister.X1 => X86Register.Rdi,
                MachineRegister.X2 => X86Register.Rsi,
                MachineRegister.X3 => X86Register.Rdx,
                MachineRegister.X4 => X86Register.Rcx,
                MachineRegister.X5 => X86Register.R8,
                MachineRegister.X6 => X86Register.R9,
                MachineRegister.X7 => X86Register.R10,
                MachineRegister.X8 => X86Register.R11,
                MachineRegister.X9 => X86Register.Rbx,
                MachineRegister.X11 => X86Register.R12,
                MachineRegister.X12 => X86Register.R13,
                MachineRegister.X13 => X86Register.R14,
                MachineRegister.X14 => X86Register.R15,
                _ => throw new NotSupportedException($"Unsupported SysV x86 machine register: {register}."),
            };
        }

        private static bool IsStringArray(RuntimeType type)
            => type.Kind == RuntimeTypeKind.Array &&
               type.IsSzArray &&
               type.ElementType is not null &&
               StringComparer.Ordinal.Equals(type.ElementType.Namespace, "System") &&
               StringComparer.Ordinal.Equals(type.ElementType.Name, "String");

        private static bool IsSystemStringType(RuntimeType type)
            => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
               StringComparer.Ordinal.Equals(type.Name, "String");

        private static bool IsVoid(RuntimeType type)
            => type.PrimitiveKind == RuntimePrimitiveKind.Void ||
               (StringComparer.Ordinal.Equals(type.Namespace, "System") && StringComparer.Ordinal.Equals(type.Name, "Void"));

        private static bool IsIntegerEntryReturn(RuntimeType type)
            => type.PrimitiveKind is RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
               RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
               RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 or
               RuntimePrimitiveKind.NativeInt or RuntimePrimitiveKind.NativeUInt;

        private static X86Operand Reg(X86Register register, int size)
            => X86Operand.RegisterOperand(register, size);

        private static X86Operand Mem(X86Register register, long displacement, int size)
            => X86Operand.Memory(register, displacement, size);

        private static X86Operand Imm(long value)
            => X86Operand.ImmediateOperand(value);

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            int remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }
}
