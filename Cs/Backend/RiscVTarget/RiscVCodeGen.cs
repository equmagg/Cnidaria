using Cnidaria.RiscV;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;

namespace Cnidaria.Cs
{
    internal sealed class RiscVCodeGeneratorOptions
    {
        public static RiscVCodeGeneratorOptions Default => new RiscVCodeGeneratorOptions();

        public int EntryMethodId { get; set; } = -1;
        public bool EmitStartup { get; set; } = true;
        public bool MarkMethodsCodeGenerated { get; set; } = true;
        public bool EmbedRuntime { get; set; } = true;
        public Func<RuntimeMethod, string>? InternalCallSymbolResolver { get; set; }
        public Func<RuntimeMethod, string>? ExternalSymbolResolver { get; set; }
    }

    internal static class RiscVCodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";

        public static RiscVProgram Build(
            GenTreeProgram program,
            RiscVCodeGeneratorOptions? options = null,
            TargetInfo? target = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= RiscVCodeGeneratorOptions.Default;
            target ??= program.Target;
            if (!target.IsRiscV)
                throw new ArgumentException("RISC-V code generation requires a RiscV32 or RiscV64 target.", nameof(target));
            if (target.Endianness != TargetEndianness.Little)
                throw new NotImplementedException("Big-endian RISC-V code generation is not implemented.");

            var allocatedTarget = program.Target;
            if (allocatedTarget.Architecture != target.Architecture ||
                allocatedTarget.PointerSize != target.PointerSize ||
                allocatedTarget.GeneralRegisterSize != target.GeneralRegisterSize ||
                RegisterInfo.AbiFloatingRegisterSize(allocatedTarget) != RegisterInfo.AbiFloatingRegisterSize(target))
            {
                throw new ArgumentException("RISC-V code generation target is ABI-incompatible with the LSRA input.", nameof(target));
            }

            return new Generator(program, target, RVTarget.FromTargetInfo(target), options).Generate();
        }

        private sealed class Generator
        {
            private readonly GenTreeProgram _program;
            private readonly TargetInfo _target;
            private readonly RVTarget _machineTarget;
            private readonly RiscVCodeGeneratorOptions _options;
            private readonly TextSectionBuilder _text = new TextSectionBuilder(TextSectionName);
            private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, RVObjectSectionKind.Rodata);
            private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, RVObjectSectionKind.Data);
            private readonly List<RVObjectSymbol> _symbols = new List<RVObjectSymbol>();
            private readonly Dictionary<int, string> _methodLabels = new Dictionary<int, string>();
            private readonly Dictionary<int, GenTreeMethod> _methodsById = new Dictionary<int, GenTreeMethod>();
            private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _externalSymbols = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> _typeDescriptorLabels = new Dictionary<int, string>();
            private readonly List<TypeDescriptorDraft> _typeDescriptors = new List<TypeDescriptorDraft>();
            private readonly List<InterfaceDispatchCellDraft> _interfaceDispatchCells = new List<InterfaceDispatchCellDraft>();
            private readonly Dictionary<int, string> _unboxingStubLabels = new Dictionary<int, string>();
            private readonly List<UnboxingStubDraft> _unboxingStubs = new List<UnboxingStubDraft>();
            private readonly Dictionary<int, string> _virtualDispatchMethodLabels = new Dictionary<int, string>();
            private string? _virtualDispatchFailureStubLabel;
            private bool _virtualDispatchMetadataPrepared;
            private readonly Dictionary<string, string> _stringLiteralLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<StringLiteralDraft> _stringLiterals = new List<StringLiteralDraft>();
            private readonly Dictionary<int, StaticExceptionDraft> _staticExceptionsByTypeId = new Dictionary<int, StaticExceptionDraft>();
            private readonly List<StaticExceptionDraft> _staticExceptions = new List<StaticExceptionDraft>();
            private readonly List<SafePointDraft> _safePoints = new List<SafePointDraft>();
            private readonly Dictionary<int, EhMethodDraft> _ehMethodsByMethodId = new Dictionary<int, EhMethodDraft>();
            private readonly List<EhMethodDraft> _ehMethods = new List<EhMethodDraft>();
            private readonly Dictionary<int, StaticStorageDraft> _staticStorageByTypeId = new Dictionary<int, StaticStorageDraft>();
            private readonly Dictionary<int, TypeInitializationThunkDraft> _typeInitializationThunksByTypeId = new Dictionary<int, TypeInitializationThunkDraft>();
            private readonly List<TypeInitializationThunkDraft> _typeInitializationThunks = new List<TypeInitializationThunkDraft>();
            private readonly Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft> _delegateTargetThunksByKey = new Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft>();
            private readonly List<DelegateTargetThunkDraft> _delegateTargetThunks = new List<DelegateTargetThunkDraft>();
            private readonly List<StaticRootDraft> _staticRoots = new List<StaticRootDraft>();
            private int _nextLocalLabel;

            public Generator(
                GenTreeProgram program,
                TargetInfo target,
                RVTarget machineTarget,
                RiscVCodeGeneratorOptions options)
            {
                _program = program;
                _target = target;
                _machineTarget = machineTarget;
                _options = options;
            }

            public RiscVProgram Generate()
            {
                IndexMethods();
                var entryMethod = SelectEntryMethod();
                var entryLabel = _methodLabels[entryMethod.RuntimeMethod.MethodId];

                foreach (var method in _program.Methods)
                    EmitMethod(method);

                EmitDelegateTargetThunks();
                EmitEhTransferHelper();

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
                        _ = GetTypeInitializationStateLabel(entryMethod.RuntimeMethod.DeclaringType);
                }

                if (entryTypeInitializer is not null)
                    _ = GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType);

                EmitTypeInitializationThunks();
                PrepareVirtualDispatchMetadata();
                EmitUnboxingStubs();
                EmitVirtualDispatchFailureStub();
                RuntimeMetadataLabels runtimeMetadata = EmitRuntimeMetadata();
                if (_options.EmitStartup)
                {
                    entryLabel = EmitStartup(
                        entryMethod,
                        entryLabel,
                        runtimeMetadata.SafePointTableLabel,
                        _safePoints.Count,
                        runtimeMetadata.TypeInfoTableLabel,
                        _typeDescriptors.Count,
                        runtimeMetadata.StaticRootTableLabel,
                        _staticRoots.Count,
                        entryTypeInitializer);
                }

                _symbols.Add(new RVObjectSymbol(
                    TextSectionName,
                    TextSectionName,
                    0,
                    _text.ByteLength,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Section));

                var dataSectionsBuilder = ImmutableArray.CreateBuilder<RVDataSection>(2);
                if (_rodata.ByteLength != 0)
                {
                    dataSectionsBuilder.Add(_rodata.ToSection());
                    _symbols.Add(new RVObjectSymbol(
                        RodataSectionName,
                        RodataSectionName,
                        0,
                        _rodata.ByteLength,
                        RVObjectSymbolBinding.Local,
                        RVObjectSymbolKind.Section));
                }
                if (_data.ByteLength != 0)
                {
                    dataSectionsBuilder.Add(_data.ToSection());
                    _symbols.Add(new RVObjectSymbol(
                        DataSectionName,
                        DataSectionName,
                        0,
                        _data.ByteLength,
                        RVObjectSymbolBinding.Local,
                        RVObjectSymbolKind.Section));
                }

                var result = new RiscVProgram(
                    _machineTarget,
                    _text.ToSection(),
                    dataSectionsBuilder.ToImmutable(),
                    _symbols.ToImmutableArray(),
                    entryLabel);

                if (_options.EmbedRuntime && _target.OperatingSystem == OperatingSystemKind.Linux)
                    result = RiscVObjectComposer.Compose(result, RiscVRuntime.GetObject(_target));

                return result;
            }

            private void IndexMethods()
            {
                foreach (var method in _program.Methods)
                {
                    if (method.Phase < GenTreeMethodPhase.RegisterAllocated)
                        throw new InvalidOperationException("RISC-V code generation requires LSRA-annotated LIR.");

                    int methodId = method.RuntimeMethod.MethodId;
                    if (!_methodsById.TryAdd(methodId, method))
                        throw new InvalidOperationException($"Duplicate method in RISC-V code generation input: M{methodId}.");

                    string methodLabel = CreateUniqueGlobalLabel(FormatMethodSymbol(method.RuntimeMethod));
                    _methodLabels.Add(methodId, methodLabel);
                    if (method.Cfg.ExceptionRegions.Length != 0)
                        PrepareEhMethod(method, methodLabel);
                }
            }

            private void PrepareEhMethod(GenTreeMethod method, string methodLabel)
            {
                var regions = method.Cfg.ExceptionRegions;
                var order = EhFuncletLayout.ComputeVmRegionOrder(method.Cfg);
                var localIndexByRegion = new Dictionary<int, int>(order.Length);
                for (int i = 0; i < order.Length; i++)
                    localIndexByRegion[regions[order[i]].Index] = i;

                var clauses = ImmutableArray.CreateBuilder<EhClauseDraft>(order.Length);
                for (int i = 0; i < order.Length; i++)
                {
                    CfgExceptionRegion region = regions[order[i]];
                    if (region.Kind == CfgExceptionRegionKind.Filter)
                        throw new NotSupportedException(
                            $"Exception filters are not supported in method M{method.RuntimeMethod.MethodId} '{method.RuntimeMethod.Name}'.");

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
                            throw new NotSupportedException($"Unsupported RISC-V exception region kind {region.Kind}.");
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
                    throw new InvalidOperationException("Cannot generate a RISC-V program without methods.");

                if (_options.EntryMethodId >= 0)
                {
                    if (_methodsById.TryGetValue(_options.EntryMethodId, out var selected))
                        return selected;
                    throw new InvalidOperationException($"Entry method M{_options.EntryMethodId} is not present in the generated program.");
                }

                GenTreeMethod? firstMain = null;
                GenTreeMethod? firstSupportedMain = null;
                GenTreeMethod? firstStaticParameterless = null;
                foreach (var method in _program.Methods)
                {
                    var runtimeMethod = method.RuntimeMethod;
                    if (runtimeMethod.IsStatic && runtimeMethod.ParameterTypes.Length == 0 && firstStaticParameterless is null)
                        firstStaticParameterless = method;

                    if (!StringComparer.Ordinal.Equals(runtimeMethod.Name, "Main"))
                        continue;

                    firstMain ??= method;

                    if (!(runtimeMethod.IsStatic && (runtimeMethod.ParameterTypes.Length == 0
                        || (runtimeMethod.ParameterTypes.Length == 1 && IsStringArray(runtimeMethod.ParameterTypes[0])))))
                    {
                        continue;
                    }
                    firstSupportedMain ??= method;


                    if (!_options.EmitStartup || (runtimeMethod.IsStatic && (runtimeMethod.ParameterTypes.Length == 0) ||
                        (runtimeMethod.ParameterTypes.Length == 1 && IsStringArray(runtimeMethod.ParameterTypes[0]))))
                    {
                        return method;
                    }
                }

                if (_options.EmitStartup && firstStaticParameterless is not null)
                    return firstStaticParameterless;
                if (firstMain is not null)
                    return firstMain;
                return _program.Methods[0];
            }
            private static bool IsStringArray(RuntimeType type)
                => type.Kind == RuntimeTypeKind.Array && type.IsSzArray && type.ElementType?.Namespace == "System" && type.ElementType.Name == "String";
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
                var runtimeMethod = entryMethod.RuntimeMethod;
                if (!runtimeMethod.IsStatic)
                    throw new NotImplementedException("Startup for an instance entry method is not implemented.");
                if (runtimeMethod.ParameterTypes.Length > 1 || (runtimeMethod.ParameterTypes.Length == 1 && !IsStringArray(runtimeMethod.ParameterTypes[0])))
                    throw new NotImplementedException("Startup currently supports only parameterless or string[] managed entry methods.");
                if (_target.OperatingSystem is not OperatingSystemKind.None and not OperatingSystemKind.Linux)
                    throw new NotImplementedException($"Startup for {_target.OperatingSystem} is not implemented.");

                if (!IsVoid(runtimeMethod.ReturnType))
                {
                    var returnKind = MethodEmitter.StackKindForType(runtimeMethod.ReturnType);
                    if (returnKind is not GenStackKind.I4 and not GenStackKind.I8 and not GenStackKind.NativeInt and not GenStackKind.NativeUInt)
                        throw new NotImplementedException("Startup currently supports only void or integer managed entry returns.");
                    if (!_machineTarget.Is64Bit && returnKind == GenStackKind.I8)
                        throw new NotImplementedException("Int64 entry returns on RV32 require a soft-long lowering pass.");
                }

                var label = CreateUniqueGlobalLabel("_start");
                int startOffset = _text.ByteLength;
                _text.DefineLabel(label);
                EmitMove(RVRegister.X8, RVRegister.X0);
                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitMove(RVRegister.X10, RVRegister.X2);
                    EmitAndImmediate(RVRegister.X2, RVRegister.X2, -_target.CallFrameAlignment);
                    EmitMaterializeAddress(safePointTableLabel, RVRegister.X11);
                    EmitLoadImmediate(RVRegister.X12, safePointCount);
                    EmitMaterializeAddress(typeInfoTableLabel, RVRegister.X13);
                    EmitLoadImmediate(RVRegister.X14, typeInfoCount);
                    EmitMaterializeAddress(staticRootTableLabel, RVRegister.X15);
                    EmitLoadImmediate(RVRegister.X16, staticRootCount);
                    EmitPcrelTransfer(ResolveExternalSymbol(RiscVRuntime.InitializeSymbol), link: true);
                    if (entryTypeInitializer is not null)
                        EmitStartupTypeInitialization(entryTypeInitializer);
                }
                else
                {
                    EmitAndImmediate(RVRegister.X2, RVRegister.X2, -_target.CallFrameAlignment);
                }
                if (runtimeMethod.ParameterTypes.Length == 1)
                    EmitLoadImmediate(RVRegister.X10, 0);
                EmitPcrelTransfer(entryMethodLabel, link: true);

                if (IsVoid(runtimeMethod.ReturnType))
                    EmitLoadImmediate(RVRegister.X10, 0);

                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitLoadImmediate(RVRegister.X17, 93);
                    Emit(new RVInstruction(RVInstrKind.Ecall));
                    Emit(new RVInstruction(RVInstrKind.Ebreak));
                }

                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, 0));
                _symbols.Add(new RVObjectSymbol(
                    label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Global,
                    RVObjectSymbolKind.Function));
                return label;
            }

            private void EmitStartupTypeInitialization(RuntimeMethod initializer)
                => EmitPcrelTransfer(GetTypeInitializationThunkLabel(initializer.DeclaringType), link: true);

            public void EmitCurrentThreadId(RVRegister destination)
            {
                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitLoadImmediate(RVRegister.X17, 178);
                    Emit(new RVInstruction(RVInstrKind.Ecall));
                    if (destination != RVRegister.X10)
                        EmitMove(destination, RVRegister.X10);
                    return;
                }

                EmitLoadImmediate(destination, 1);
            }

            public void EmitFutexWait(RVRegister address, int expected)
            {
                if (_target.OperatingSystem != OperatingSystemKind.Linux)
                    return;

                EmitMove(RVRegister.X10, address);
                EmitLoadImmediate(RVRegister.X11, 128);
                EmitLoadImmediate(RVRegister.X12, expected);
                EmitMove(RVRegister.X13, RVRegister.X0);
                EmitMove(RVRegister.X14, RVRegister.X0);
                EmitMove(RVRegister.X15, RVRegister.X0);
                EmitLoadImmediate(RVRegister.X17, 98);
                Emit(new RVInstruction(RVInstrKind.Ecall));
            }

            public void EmitFutexWakeAll(RVRegister address)
            {
                if (_target.OperatingSystem != OperatingSystemKind.Linux)
                    return;

                EmitMove(RVRegister.X10, address);
                EmitLoadImmediate(RVRegister.X11, 129);
                EmitLoadImmediate(RVRegister.X12, int.MaxValue);
                EmitMove(RVRegister.X13, RVRegister.X0);
                EmitMove(RVRegister.X14, RVRegister.X0);
                EmitMove(RVRegister.X15, RVRegister.X0);
                EmitLoadImmediate(RVRegister.X17, 98);
                Emit(new RVInstruction(RVInstrKind.Ecall));
            }

            private void EmitMethod(GenTreeMethod method)
            {
                var label = _methodLabels[method.RuntimeMethod.MethodId];
                int startOffset = _text.ByteLength;
                _text.DefineLabel(label);

                var emitter = new MethodEmitter(this, method, label);
                emitter.Emit();

                _symbols.Add(new RVObjectSymbol(
                    label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Global,
                    RVObjectSymbolKind.Function));

                if (_options.MarkMethodsCodeGenerated)
                    method.SetPhase(GenTreeMethodPhase.CodeGenerated);
            }

            public string ResolveMethodLabel(RuntimeMethod method)
            {
                if (method.HasInternalCall)
                {
                    var internalCallLabel = _options.InternalCallSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(internalCallLabel) &&
                        _target.OperatingSystem == OperatingSystemKind.Linux)
                    {
                        internalCallLabel = RiscVRuntime.ResolveInternalCall(method);
                    }
                    if (string.IsNullOrWhiteSpace(internalCallLabel))
                        throw new MissingMethodException($"RISC-V InternalCall implementation is missing for M{method.MethodId} '{method.Name}'.");
                    return ResolveExternalSymbol(internalCallLabel);
                }

                if (method.IsExtern)
                {
                    ValidateExternalMethod(method);
                    var externalLabel = _options.ExternalSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = method.DllImportData?.EntryPointName;
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = method.Name;
                    return ResolveExternalSymbol(externalLabel);
                }

                if (_methodLabels.TryGetValue(method.MethodId, out var label))
                    return label;

                label = _options.ExternalSymbolResolver?.Invoke(method);
                if (string.IsNullOrWhiteSpace(label))
                    label = SanitizeSymbolName(FormatMethodSymbol(method));
                if (label.Length == 0)
                    label = $"M{method.MethodId}";
                return ResolveExternalSymbol(label);
            }

            public string ResolveExternalSymbol(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("External symbol name is empty.", nameof(label));

                if (_externalSymbols.Add(label))
                {
                    _symbols.Add(new RVObjectSymbol(
                        label,
                        string.Empty,
                        0,
                        0,
                        RVObjectSymbolBinding.External,
                        RVObjectSymbolKind.Function));
                }

                return label;
            }

            public string ResolveExternalObjectSymbol(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("External symbol name is empty.", nameof(label));

                if (_externalSymbols.Add(label))
                {
                    _symbols.Add(new RVObjectSymbol(
                        label,
                        string.Empty,
                        0,
                        0,
                        RVObjectSymbolBinding.External,
                        RVObjectSymbolKind.Object));
                }

                return label;
            }

            public string AddConstantData(byte[] bytes, int alignment, string prefix)
            {
                var label = CreateLocalLabel(prefix);
                int offset = _rodata.Align(alignment);
                _rodata.EmitBytes(bytes);
                _symbols.Add(new RVObjectSymbol(
                    label,
                    RodataSectionName,
                    offset,
                    bytes.Length,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Object));
                return label;
            }

            public string CreateLocalLabel(string prefix)
            {
                string baseName = ".L" + SanitizeSymbolName(prefix);
                string candidate;
                do
                {
                    candidate = $"{baseName}_{(++_nextLocalLabel)}";
                }
                while (!_usedLabels.Add(candidate));
                return candidate;
            }

            public void Emit(RVInstruction instruction)
                => _text.Emit(instruction);

            public void DefineLabel(string label)
                => _text.DefineLabel(label);

            public void EmitPcrelTransfer(string symbol, bool link)
            {
                int hiOffset = _text.ByteLength;
                Emit(new RVInstruction(
                    RVInstrKind.Auipc,
                    RVRegister.X31,
                    symbol: symbol,
                    relocationKind: RVRelocationKind.AbsoluteUpper20));
                _text.AddRelocation(hiOffset, symbol, 0, RVObjectRelocationKind.PcrelHi20);

                int loOffset = _text.ByteLength;
                Emit(new RVInstruction(
                    RVInstrKind.Jalr,
                    link ? RVRegister.X1 : RVRegister.X0,
                    RVRegister.X31,
                    immediate: 0,
                    symbol: symbol,
                    relocationKind: RVRelocationKind.AbsoluteLow12));
                _text.AddRelocation(loOffset, symbol, 0, RVObjectRelocationKind.PcrelLo12I);
            }

            public void EmitMaterializeAddress(string symbol, RVRegister destination)
            {
                int hiOffset = _text.ByteLength;
                Emit(new RVInstruction(
                    RVInstrKind.Auipc,
                    destination,
                    symbol: symbol,
                    relocationKind: RVRelocationKind.AbsoluteUpper20));
                _text.AddRelocation(hiOffset, symbol, 0, RVObjectRelocationKind.PcrelHi20);

                int loOffset = _text.ByteLength;
                Emit(new RVInstruction(
                    RVInstrKind.Addi,
                    destination,
                    destination,
                    immediate: 0,
                    symbol: symbol,
                    relocationKind: RVRelocationKind.AbsoluteLow12));
                _text.AddRelocation(loOffset, symbol, 0, RVObjectRelocationKind.PcrelLo12I);
            }

            public void EmitLoadImmediate(RVRegister destination, long value)
            {
                if (FitsSignedImmediate(value, 12))
                {
                    Emit(RVInstruction.I(RVInstrKind.Addi, destination, RVRegister.X0, checked((int)value)));
                    return;
                }

                if (value >= int.MinValue && value <= int.MaxValue)
                {
                    int hi = checked((int)((value + 0x800L) >> 12));
                    int lo = checked((int)(value - ((long)hi << 12)));
                    Emit(RVInstruction.U(RVInstrKind.Lui, destination, hi));
                    if (lo != 0)
                        Emit(RVInstruction.I(RVInstrKind.Addi, destination, destination, lo));
                    return;
                }

                if (!_machineTarget.Is64Bit)
                    throw new OverflowException("Immediate does not fit an RV32 register.");

                var bytes = BitConverter.GetBytes(value);
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                string label = AddConstantData(bytes, 8, "i64");
                EmitMaterializeAddress(label, destination);
                Emit(RVInstruction.I(RVInstrKind.Ld, destination, destination, 0));
            }

            public void EmitAddImmediate(
                RVRegister destination,
                RVRegister source,
                int immediate,
                RVRegister preserve = RVRegister.Invalid)
            {
                if (immediate == 0)
                {
                    EmitMove(destination, source);
                    return;
                }

                if (FitsSignedImmediate(immediate, 12))
                {
                    Emit(RVInstruction.I(RVInstrKind.Addi, destination, source, immediate));
                    return;
                }

                RVRegister scratch = SelectIntegerScratch(destination, source, preserve);
                EmitLoadImmediate(scratch, immediate);
                Emit(RVInstruction.R(RVInstrKind.Add, destination, source, scratch));
            }

            private static RVRegister SelectIntegerScratch(
                RVRegister first,
                RVRegister second,
                RVRegister third = RVRegister.Invalid)
            {
                if (first != RVRegister.X31 && second != RVRegister.X31 && third != RVRegister.X31)
                    return RVRegister.X31;
                if (first != RVRegister.X30 && second != RVRegister.X30 && third != RVRegister.X30)
                    return RVRegister.X30;
                if (first != RVRegister.X29 && second != RVRegister.X29 && third != RVRegister.X29)
                    return RVRegister.X29;
                throw new InvalidOperationException("No integer scratch register is available.");
            }

            public void EmitAdjustStack(int delta)
            {
                if (delta == 0)
                    return;

                if (FitsSignedImmediate(delta, 12))
                {
                    Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X2, RVRegister.X2, delta));
                    return;
                }

                long magnitude = Math.Abs((long)delta);
                EmitLoadImmediate(RVRegister.X31, magnitude);
                Emit(RVInstruction.R(
                    delta < 0 ? RVInstrKind.Sub : RVInstrKind.Add,
                    RVRegister.X2,
                    RVRegister.X2,
                    RVRegister.X31));
            }

            public void EmitMove(RVRegister destination, RVRegister source)
            {
                if (destination == source)
                    return;
                Emit(RVInstruction.I(RVInstrKind.Addi, destination, source, 0));
            }

            private void EmitAndImmediate(RVRegister destination, RVRegister source, int immediate)
            {
                if (FitsSignedImmediate(immediate, 12))
                {
                    Emit(RVInstruction.I(RVInstrKind.Andi, destination, source, immediate));
                    return;
                }

                EmitLoadImmediate(RVRegister.X31, immediate);
                Emit(RVInstruction.R(RVInstrKind.And, destination, source, RVRegister.X31));
            }

            private string CreateUniqueGlobalLabel(string name)
            {
                string baseName = SanitizeSymbolName(name);
                if (baseName.Length == 0)
                    baseName = "symbol";

                string candidate = baseName;
                int suffix = 0;
                while (!_usedLabels.Add(candidate))
                    candidate = $"{baseName}_{(++suffix)}";
                return candidate;
            }

            private static string FormatMethodSymbol(RuntimeMethod method)
            {
                var type = method.DeclaringType;
                var sb = new StringBuilder();
                sb.Append("M").Append(method.MethodId).Append('_');
                if (!string.IsNullOrEmpty(type.Namespace))
                    sb.Append(type.Namespace).Append('_');
                sb.Append(type.Name).Append('_').Append(method.Name);
                return sb.ToString();
            }

            private static string SanitizeSymbolName(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return string.Empty;

                var sb = new StringBuilder(value.Length);
                foreach (char c in value)
                {
                    if (char.IsLetterOrDigit(c) || c is '_' or '.' or '$')
                        sb.Append(c);
                    else
                        sb.Append('_');
                }
                return sb.ToString();
            }

            private static bool FitsSignedImmediate(long value, int bits)
            {
                long min = -(1L << (bits - 1));
                long max = (1L << (bits - 1)) - 1;
                return value >= min && value <= max;
            }

            private static int AlignValueUp(int value, int alignment)
            {
                int remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            private static bool IsVoid(RuntimeType type)
                => type.PrimitiveKind == RuntimePrimitiveKind.Void ||
                   (StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                    StringComparer.Ordinal.Equals(type.Name, "Void"));

            private static bool IsSystemStringType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(type.Name, "String");

            private static bool IsSystemArrayType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(type.Name, "Array");

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
                public ImmutableArray<TypeGcFieldDraft> Fields { get; }
                public ImmutableArray<TypeGcFieldDraft> ComponentFields { get; }
                public ImmutableArray<RuntimeType> Interfaces { get; }
                public string? FieldsLabel { get; set; }
                public string? ComponentFieldsLabel { get; set; }
                public string? InterfacesLabel { get; set; }
                public string? VTableLabel { get; set; }
                public ImmutableArray<string> VTableTargets { get; set; }
                public string? RelatedTypeLabel { get; set; }

                public TypeDescriptorDraft(
                    RuntimeType type,
                    string label,
                    ImmutableArray<TypeGcFieldDraft> fields,
                    ImmutableArray<TypeGcFieldDraft> componentFields,
                    ImmutableArray<RuntimeType> interfaces)
                {
                    Type = type;
                    Label = label;
                    Fields = fields;
                    ComponentFields = componentFields;
                    Interfaces = interfaces;
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

            private sealed class TypeInitializationThunkDraft
            {
                public RuntimeMethod Initializer { get; }
                public string StateLabel { get; }
                public string Label { get; }

                public TypeInitializationThunkDraft(
                    RuntimeMethod initializer,
                    string stateLabel,
                    string label)
                {
                    Initializer = initializer;
                    StateLabel = stateLabel;
                    Label = label;
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

            private string GetTypeDescriptorLabel(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_typeDescriptorLabels.TryGetValue(type.TypeId, out string? existing))
                    return existing;
                if (_virtualDispatchMetadataPrepared)
                    throw new InvalidOperationException("A MethodTable was requested after virtual dispatch metadata was finalized.");

                if (type.Kind == RuntimeTypeKind.TypeParam)
                    throw new NotSupportedException("Open generic parameters do not have standalone MethodTables.");

                _program.TypeSystem?.EnsureConstructedMembers(type);
                EnsureVirtualTable(type);
                string label = CreateLocalLabel($"type_{type.TypeId}");
                var fields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                _typeDescriptorLabels.Add(type.TypeId, label);
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

                var interfaces = CollectImplementedInterfaces(type);
                var descriptor = new TypeDescriptorDraft(
                    type,
                    label,
                    fields.ToImmutable(),
                    componentFields.ToImmutable(),
                    interfaces);
                _typeDescriptors.Add(descriptor);
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

                for (int i = 0; i < _typeDescriptors.Count; i++)
                {
                    TypeDescriptorDraft descriptor = _typeDescriptors[i];
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
                    for (int t = 0; t < _typeDescriptors.Count; t++)
                    {
                        TypeDescriptorDraft descriptor = _typeDescriptors[t];
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
                    if (string.IsNullOrWhiteSpace(internalCallLabel) &&
                        _target.OperatingSystem == OperatingSystemKind.Linux)
                    {
                        try
                        {
                            internalCallLabel = RiscVRuntime.ResolveInternalCall(target);
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
                    label = ResolveExternalSymbol(internalCallLabel);
                }
                else if (target.IsExtern)
                {
                    ValidateExternalMethod(target);
                    string? externalLabel = _options.ExternalSymbolResolver?.Invoke(target);
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = target.DllImportData?.EntryPointName;
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = target.Name;
                    label = ResolveExternalSymbol(externalLabel);
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
                    label = ResolveExternalSymbol(externalLabel);
                }

                _virtualDispatchMethodLabels.Add(target.MethodId, label);
                return true;
            }

            private static void ValidateExternalMethod(RuntimeMethod method)
            {
                if (method.GenericArity != 0 || method.DeclaringType.GenericTypeDefinition is not null)
                    throw new NotSupportedException($"Generic external method M{method.MethodId} '{method.Name}' is not supported.");

                var import = method.DllImportData;
                if (import is not null)
                {
                    if (!method.IsStatic)
                        throw new NotSupportedException($"DllImport method M{method.MethodId} '{method.Name}' must be static.");
                    if (string.IsNullOrWhiteSpace(import.ModuleName))
                        throw new NotSupportedException($"DllImport method M{method.MethodId} '{method.Name}' has no library name.");
                    int characterSet = (int)import.CharacterSet;
                    if (characterSet < (int)System.Runtime.InteropServices.CharSet.None ||
                        characterSet > (int)System.Runtime.InteropServices.CharSet.Auto)
                    {
                        throw new NotSupportedException(
                            $"CharSet value '{characterSet}' is invalid for RISC-V P/Invoke method M{method.MethodId} '{method.Name}'.");
                    }
                    if (import.CallingConvention is not System.Runtime.InteropServices.CallingConvention.Winapi and
                        not System.Runtime.InteropServices.CallingConvention.Cdecl)
                    {
                        throw new NotSupportedException(
                            $"Calling convention '{import.CallingConvention}' is not supported for RISC-V P/Invoke method M{method.MethodId} '{method.Name}'.");
                    }
                    if (!import.PreserveSig)
                        throw new NotSupportedException($"PreserveSig=false is not supported for P/Invoke method M{method.MethodId} '{method.Name}'.");
                    if (import.SetLastError)
                        throw new NotSupportedException($"SetLastError=true is not supported for P/Invoke method M{method.MethodId} '{method.Name}'.");
                }

                if (!IsSupportedExternalAbiType(method.ReturnType, allowVoid: true))
                {
                    throw new NotSupportedException(
                        $"Return type '{method.ReturnType.Namespace}.{method.ReturnType.Name}' is not supported for external method M{method.MethodId} '{method.Name}'.");
                }

                var parameters = method.ParameterTypes;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (IsSupportedExternalAbiType(parameters[i], allowVoid: false))
                        continue;

                    throw new NotSupportedException(
                        $"Parameter {i} type '{parameters[i].Namespace}.{parameters[i].Name}' is not supported for external method M{method.MethodId} '{method.Name}'.");
                }
            }

            private static bool IsSupportedExternalAbiType(RuntimeType type, bool allowVoid)
            {
                if (type.PrimitiveKind == RuntimePrimitiveKind.Void)
                    return allowVoid;
                if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer)
                    return true;
                if (type.Kind == RuntimeTypeKind.ByRef)
                    return type.ElementType is not null && IsSupportedExternalAbiType(type.ElementType, allowVoid: false);
                if (type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType || type.ContainsGcPointers)
                    return false;
                return true;
            }

            public string GetVirtualDispatchFailureStubLabel()
            {
                _virtualDispatchFailureStubLabel ??= CreateLocalLabel("virtual_dispatch_failure");
                return _virtualDispatchFailureStubLabel;
            }

            private string GetDelegateTargetThunkLabel(RuntimeType delegateType, RuntimeMethod targetMethod, bool closed)
            {
                RuntimeMethod invokeMethod = ResolveDelegateInvoke(delegateType);
                var key = new DelegateTargetThunkKey(delegateType.TypeId, targetMethod.MethodId, closed);
                if (_delegateTargetThunksByKey.TryGetValue(key, out DelegateTargetThunkDraft? existing))
                    return existing.Label;

                string label = CreateLocalLabel($"delegate_target_T{delegateType.TypeId}_M{targetMethod.MethodId}_{(closed ? "closed" : "open")}");
                var draft = new DelegateTargetThunkDraft(delegateType, invokeMethod, targetMethod, closed, label);
                _delegateTargetThunksByKey.Add(key, draft);
                _delegateTargetThunks.Add(draft);
                return label;
            }

            private DelegateAbiBundle GetDelegateInvokeAbi(RuntimeMethod invokeMethod)
                => BuildDelegateAbiBundle(invokeMethod);

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

                throw new TypeLoadException($"Runtime type 'System.{name}' is required by RISC-V delegate lowering.");
            }

            private RuntimeType GetDelegateInvocationListArrayType()
            {
                RuntimeTypeSystem typeSystem = _program.TypeSystem ??
                    throw new InvalidOperationException("Code generation requires a runtime type system.");
                return typeSystem.GetArrayType(FindSystemType("Delegate"));
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

            private DelegateAbiBundle BuildDelegateAbiBundle(RuntimeMethod method)
            {
                int logicalCount = method.ParameterTypes.Length + (method.HasThis ? 1 : 0);
                int hiddenInsertion = MachineAbi.RequiresHiddenReturnBuffer(method, _target)
                    ? MachineAbi.HiddenReturnBufferInsertionIndex(method, logicalCount, _target)
                    : -1;
                int general = 0;
                int floating = 0;
                int stack = 0;
                int saveCursor = 0;
                int maxStackSlot = -1;
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
                    for (int s = 0; s < slices.Length; s++)
                        entitySize = Math.Max(entitySize, checked(slices[s].ValueOffset + slices[s].Size));
                    saveCursor = AlignValueUp(checked(entityBase + entitySize), _target.PointerSize);
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
                    saveCursor = AlignValueUp(checked(saveCursor + _target.PointerSize), _target.PointerSize);
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

            private void EmitDelegateTargetThunks()
            {
                for (int i = 0; i < _delegateTargetThunks.Count; i++)
                    EmitDelegateTargetThunk(_delegateTargetThunks[i]);
            }

            private void EmitDelegateTargetThunk(DelegateTargetThunkDraft thunk)
            {
                DelegateAbiBundle incoming = BuildDelegateAbiBundle(thunk.InvokeMethod);
                DelegateAbiBundle target = BuildDelegateAbiBundle(thunk.TargetMethod);
                ValidateDelegateTargetThunk(thunk, incoming, target);
                int incomingSaveOffset;
                int targetSaveOffset;
                int savedFramePointerOffset;
                int savedReturnAddressOffset;
                int outgoingSize = AlignValueUp(
                    Math.Max(incoming.OutgoingStackSize, target.OutgoingStackSize),
                    Math.Max(1, _target.StackSlotSize));
                savedFramePointerOffset = outgoingSize;
                savedReturnAddressOffset = checked(savedFramePointerOffset + _target.PointerSize);
                incomingSaveOffset = AlignValueUp(
                    checked(savedReturnAddressOffset + _target.PointerSize),
                    _target.PointerSize);
                targetSaveOffset = AlignValueUp(
                    checked(incomingSaveOffset + incoming.TotalSaveSize),
                    _target.PointerSize);
                int frameSize = AlignValueUp(
                    checked(targetSaveOffset + target.TotalSaveSize),
                    _target.CallFrameAlignment);

                int startOffset = _text.ByteLength;
                DefineLabel(thunk.Label);
                EmitAdjustStack(-frameSize);
                EmitDelegateMemoryStore(MachineRegister.X8, RVRegister.X2, savedFramePointerOffset, _target.PointerSize);
                EmitDelegateMemoryStore(MachineRegister.X1, RVRegister.X2, savedReturnAddressOffset, _target.PointerSize);
                EmitMove(RVRegister.X8, RVRegister.X2);

                EmitDelegateSaveIncomingBundle(incoming, incomingSaveOffset, frameSize);
                MaterializeDelegateTargetArguments(thunk, incoming, incomingSaveOffset, target, targetSaveOffset);
                EmitDelegateRestoreBundle(target, targetSaveOffset);

                var roots = ImmutableArray.CreateBuilder<SafePointRootDraft>();
                if (target.HiddenReturnBuffer is DelegateAbiEntity hidden)
                    roots.Add(new SafePointRootDraft(checked(targetSaveOffset + hidden.SaveBase), RegisterGcRootKind.InteriorPointer));
                for (int i = 0; i < target.LogicalArguments.Length; i++)
                {
                    DelegateAbiEntity entity = target.LogicalArguments[i];
                    if (entity.Type is null)
                        continue;
                    var fields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                    AppendTypedGcFields(fields, checked(targetSaveOffset + entity.SaveBase), entity.Type);
                    for (int f = 0; f < fields.Count; f++)
                        roots.Add(new SafePointRootDraft(fields[f].Offset, fields[f].Kind));
                }

                string returnLabel = CreateLocalLabel(thunk.Label + "_gc_return");
                SafePointDraft safePoint = AddSafePoint(
                    thunk.Label,
                    returnLabel,
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    roots.ToImmutable());
                EmitMaterializeAddress(ResolveExternalObjectSymbol(RiscVRuntime.CurrentSafePointSymbol), RVRegister.X31);
                EmitMaterializeAddress(safePoint.DescriptorLabel, RVRegister.X30);
                EmitDelegateMemoryStore(MachineRegister.X30, RVRegister.X31, 0, _target.PointerSize);
                EmitMaterializeAddress(ResolveExternalObjectSymbol(RiscVRuntime.CurrentFramePointerSymbol), RVRegister.X31);
                EmitDelegateMemoryStore(MachineRegister.X8, RVRegister.X31, 0, _target.PointerSize);
                EmitPcrelTransfer(ResolveMethodLabel(thunk.TargetMethod), link: true);
                DefineLabel(returnLabel);

                EmitDelegateMemoryLoad(MachineRegister.X28, RVRegister.X8, savedReturnAddressOffset, _target.PointerSize, signed: false);
                EmitDelegateMemoryLoad(MachineRegister.X29, RVRegister.X8, savedFramePointerOffset, _target.PointerSize, signed: false);
                EmitMove(RVRegister.X2, RVRegister.X8);
                EmitAdjustStack(frameSize);
                EmitMove(RVRegister.X1, RVRegister.X28);
                EmitMove(RVRegister.X8, RVRegister.X29);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

                _symbols.Add(new RVObjectSymbol(
                    thunk.Label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Function));
            }

            private void ValidateDelegateTargetThunk(
                DelegateTargetThunkDraft thunk,
                DelegateAbiBundle incoming,
                DelegateAbiBundle target)
            {
                bool incomingHasHiddenReturnBuffer = incoming.HiddenReturnBuffer is not null;
                bool targetHasHiddenReturnBuffer = target.HiddenReturnBuffer is not null;
                if (incomingHasHiddenReturnBuffer != targetHasHiddenReturnBuffer)
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
                    int destinationOffset = checked(saveBase + slice.SaveOffset);
                    if (slice.Location.IsRegister)
                    {
                        EmitDelegateMemoryStore(
                            slice.Location.Register,
                            RVRegister.X8,
                            destinationOffset,
                            slice.Size);
                    }
                    else
                    {
                        int sourceOffset = checked(
                            frameSize +
                            slice.Location.StackSlotIndex * _target.StackSlotSize +
                            slice.Location.StackOffset);
                        EmitDelegateCopyMemory(RVRegister.X8, sourceOffset, RVRegister.X8, destinationOffset, slice.Size);
                    }
                }
            }

            private void EmitDelegateRestoreBundle(DelegateAbiBundle bundle, int saveBase)
            {
                for (int i = 0; i < bundle.OrderedSlices.Length; i++)
                {
                    DelegateAbiSlice slice = bundle.OrderedSlices[i];
                    int sourceOffset = checked(saveBase + slice.SaveOffset);
                    if (slice.Location.IsRegister)
                    {
                        EmitDelegateMemoryLoad(
                            slice.Location.Register,
                            RVRegister.X8,
                            sourceOffset,
                            slice.Size,
                            signed: false);
                    }
                    else
                    {
                        int destinationOffset = checked(
                            slice.Location.StackSlotIndex * _target.StackSlotSize +
                            slice.Location.StackOffset);
                        EmitDelegateCopyMemory(RVRegister.X8, sourceOffset, RVRegister.X8, destinationOffset, slice.Size);
                    }
                }
            }

            private void MaterializeDelegateTargetArguments(
                DelegateTargetThunkDraft thunk,
                DelegateAbiBundle incoming,
                int incomingSaveBase,
                DelegateAbiBundle target,
                int targetSaveBase)
            {
                if (incoming.HiddenReturnBuffer is DelegateAbiEntity incomingHidden &&
                    target.HiddenReturnBuffer is DelegateAbiEntity targetHidden)
                {
                    CopyDelegateSavedEntity(incomingHidden, incomingSaveBase, targetHidden, targetSaveBase);
                }

                int targetArgumentCount = target.LogicalArguments.Length;
                for (int i = 0; i < targetArgumentCount; i++)
                {
                    DelegateAbiEntity destination = target.LogicalArguments[i];
                    if (thunk.Closed && i == 0)
                    {
                        if (incoming.LogicalArguments.Length == 0 || incoming.LogicalArguments[0].Slices.Length == 0)
                            throw new InvalidOperationException($"Delegate target thunk for M{thunk.TargetMethod.MethodId} has no delegate receiver.");
                        DelegateAbiSlice delegateReceiver = incoming.LogicalArguments[0].Slices[0];
                        EmitDelegateMemoryLoad(
                            MachineRegister.X30,
                            RVRegister.X8,
                            checked(incomingSaveBase + delegateReceiver.SaveOffset),
                            _target.PointerSize,
                            signed: false);
                        int targetOffset = FindDelegateFieldOffset(thunk.DelegateType, "_target");
                        EmitDelegateMemoryLoad(MachineRegister.X31, RVRegister.X30, targetOffset, _target.PointerSize, signed: false);
                        StoreRegisterToDelegateSavedEntity(MachineRegister.X31, destination, targetSaveBase);
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
                        targetSaveBase);
                }
            }

            private void CopyDelegateSavedEntity(
                DelegateAbiEntity source,
                int sourceSaveBase,
                DelegateAbiEntity destination,
                int destinationSaveBase)
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
                        RVRegister.X8,
                        checked(sourceSaveBase + sourceSlice.SaveOffset),
                        RVRegister.X8,
                        checked(destinationSaveBase + destinationSlice.SaveOffset),
                        sourceSlice.Size);
                }
            }

            private void StoreRegisterToDelegateSavedEntity(
                MachineRegister source,
                DelegateAbiEntity destination,
                int destinationSaveBase)
            {
                if (destination.Slices.Length != 1)
                    throw new InvalidOperationException("A closed delegate target must bind to a scalar first argument.");
                DelegateAbiSlice slice = destination.Slices[0];
                EmitDelegateMemoryStore(
                    source,
                    RVRegister.X8,
                    checked(destinationSaveBase + slice.SaveOffset),
                    Math.Min(_target.PointerSize, slice.Size));
            }

            private void EmitDelegateCopyMemory(
                RVRegister sourceBase,
                int sourceOffset,
                RVRegister destinationBase,
                int destinationOffset,
                int size)
            {
                int copied = 0;
                while (copied < size)
                {
                    int remaining = size - copied;
                    int chunk = remaining >= 8 && _machineTarget.Is64Bit
                        ? 8
                        : remaining >= 4
                            ? 4
                            : remaining >= 2
                                ? 2
                                : 1;
                    EmitDelegateMemoryLoad(
                        MachineRegister.X31,
                        sourceBase,
                        checked(sourceOffset + copied),
                        chunk,
                        signed: false);
                    EmitDelegateMemoryStore(
                        MachineRegister.X31,
                        destinationBase,
                        checked(destinationOffset + copied),
                        chunk);
                    copied += chunk;
                }
            }

            private void EmitDelegateMemoryLoad(
                MachineRegister destination,
                RVRegister baseRegister,
                int offset,
                int size,
                bool signed)
            {
                if (!FitsSignedImmediate(offset, 12))
                {
                    EmitAddImmediate(RVRegister.X30, baseRegister, offset, (RVRegister)(byte)destination);
                    baseRegister = RVRegister.X30;
                    offset = 0;
                }

                RVInstrKind opcode;
                if (MachineRegisters.GetClass(destination) == RegisterClass.Float)
                {
                    opcode = size switch
                    {
                        4 when _machineTarget.HasF => RVInstrKind.Flw,
                        8 when _machineTarget.HasD => RVInstrKind.Fld,
                        _ => throw new NotImplementedException($"Unsupported delegate floating load size {size}."),
                    };
                }
                else
                {
                    opcode = size switch
                    {
                        1 => signed ? RVInstrKind.Lb : RVInstrKind.Lbu,
                        2 => signed ? RVInstrKind.Lh : RVInstrKind.Lhu,
                        4 when _machineTarget.Is64Bit => signed ? RVInstrKind.Lw : RVInstrKind.Lwu,
                        4 => RVInstrKind.Lw,
                        8 when _machineTarget.Is64Bit => RVInstrKind.Ld,
                        _ => throw new NotImplementedException($"Unsupported delegate integer load size {size}."),
                    };
                }

                Emit(RVInstruction.I(opcode, (RVRegister)(byte)destination, baseRegister, offset));
            }

            private void EmitDelegateMemoryStore(
                MachineRegister source,
                RVRegister baseRegister,
                int offset,
                int size)
            {
                if (!FitsSignedImmediate(offset, 12))
                {
                    RVRegister sourceRegister = (RVRegister)(byte)source;
                    RVRegister addressScratch = sourceRegister == RVRegister.X30 ? RVRegister.X29 : RVRegister.X30;
                    EmitAddImmediate(addressScratch, baseRegister, offset, sourceRegister);
                    baseRegister = addressScratch;
                    offset = 0;
                }

                RVInstrKind opcode;
                if (MachineRegisters.GetClass(source) == RegisterClass.Float)
                {
                    opcode = size switch
                    {
                        4 when _machineTarget.HasF => RVInstrKind.Fsw,
                        8 when _machineTarget.HasD => RVInstrKind.Fsd,
                        _ => throw new NotImplementedException($"Unsupported delegate floating store size {size}."),
                    };
                }
                else
                {
                    opcode = size switch
                    {
                        1 => RVInstrKind.Sb,
                        2 => RVInstrKind.Sh,
                        4 => RVInstrKind.Sw,
                        8 when _machineTarget.Is64Bit => RVInstrKind.Sd,
                        _ => throw new NotImplementedException($"Unsupported delegate integer store size {size}."),
                    };
                }

                Emit(RVInstruction.S(opcode, (RVRegister)(byte)source, baseRegister, offset));
            }

            private void EmitUnboxingStubs()
            {
                for (int i = 0; i < _unboxingStubs.Count; i++)
                {
                    UnboxingStubDraft stub = _unboxingStubs[i];
                    int startOffset = _text.ByteLength;
                    _text.DefineLabel(stub.Label);
                    EmitAddImmediate(RVRegister.X10, RVRegister.X10, _target.ManagedObjectHeaderSize);
                    EmitPcrelTransfer(stub.TargetLabel, link: false);
                    _symbols.Add(new RVObjectSymbol(
                        stub.Label,
                        TextSectionName,
                        startOffset,
                        _text.ByteLength - startOffset,
                        RVObjectSymbolBinding.Local,
                        RVObjectSymbolKind.Function));
                }
            }

            private void EmitVirtualDispatchFailureStub()
            {
                if (_virtualDispatchFailureStubLabel is null)
                    return;

                int startOffset = _text.ByteLength;
                _text.DefineLabel(_virtualDispatchFailureStubLabel);
                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitLoadImmediate(RVRegister.X10, 151);
                    EmitPcrelTransfer(ResolveExternalSymbol(RiscVRuntime.FailFastSymbol), link: false);
                }
                else
                {
                    Emit(new RVInstruction(RVInstrKind.Ebreak));
                    Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, 0));
                }

                _symbols.Add(new RVObjectSymbol(
                    _virtualDispatchFailureStubLabel,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Function));
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

            public RuntimeMethod? FindTypeInitializer(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));

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

            public string GetStaticStorageLabel(RuntimeType type)
                => GetOrCreateStaticStorage(type).StorageLabel;

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
                string? waitHelperLabel = null;
                if (_target.OperatingSystem == OperatingSystemKind.Linux && _typeInitializationThunks.Count > 1)
                {
                    string contextTableLabel = EmitTypeInitializationContextTable();
                    waitHelperLabel = CreateUniqueGlobalLabel("__cctor_wait");
                    EmitTypeInitializationWaitHelper(waitHelperLabel, contextTableLabel, _typeInitializationThunks.Count);
                }

                for (int i = 0; i < _typeInitializationThunks.Count; i++)
                    EmitTypeInitializationThunk(_typeInitializationThunks[i], waitHelperLabel);
            }

            private string EmitTypeInitializationContextTable()
            {
                string label = CreateLocalLabel("type_init_contexts");
                int offset = _rodata.Align(_target.PointerSize);
                AddDataSymbol(label, offset, checked(_typeInitializationThunks.Count * _target.PointerSize));
                for (int i = 0; i < _typeInitializationThunks.Count; i++)
                    EmitPointer(_typeInitializationThunks[i].StateLabel);
                return label;
            }

            private void EmitTypeInitializationWaitHelper(string label, string contextTableLabel, int contextCount)
            {
                int startOffset = _text.ByteLength;
                DefineLabel(label);

                string findOwnedLoopLabel = CreateLocalLabel(label + "_find_owned");
                string findOwnedNextLabel = CreateLocalLabel(label + "_find_owned_next");
                string ownedFoundLabel = CreateLocalLabel(label + "_owned_found");
                string noOwnedContextLabel = CreateLocalLabel(label + "_no_owned_context");
                string chainLoopLabel = CreateLocalLabel(label + "_chain");
                string scanOwnerLoopLabel = CreateLocalLabel(label + "_scan_owner");
                string scanOwnerNextLabel = CreateLocalLabel(label + "_scan_owner_next");
                string noCycleLabel = CreateLocalLabel(label + "_no_cycle");
                string cycleLabel = CreateLocalLabel(label + "_cycle");
                string clearAndReturnZeroLabel = CreateLocalLabel(label + "_clear_zero");
                string clearAndReturnOneLabel = CreateLocalLabel(label + "_clear_one");

                EmitMove(RVRegister.X28, RVRegister.X10);
                EmitMove(RVRegister.X29, RVRegister.X11);
                EmitMove(RVRegister.X30, RVRegister.X12);
                EmitMove(RVRegister.X31, RVRegister.X0);
                EmitMaterializeAddress(contextTableLabel, RVRegister.X5);
                EmitLoadImmediate(RVRegister.X6, contextCount);
                EmitLoadImmediate(RVRegister.X14, 2);

                DefineLabel(findOwnedLoopLabel);
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X6, RVRegister.X0, noOwnedContextLabel));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X7,
                    RVRegister.X5,
                    0));
                Emit(RVInstruction.I(RVInstrKind.Lw, RVRegister.X13, RVRegister.X7, 0));
                Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X13, RVRegister.X14, findOwnedNextLabel));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X13,
                    RVRegister.X7,
                    _target.PointerSize));
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X13, RVRegister.X29, ownedFoundLabel));

                DefineLabel(findOwnedNextLabel);
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X5, RVRegister.X5, _target.PointerSize));
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X6, RVRegister.X6, -1));
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, findOwnedLoopLabel));

                DefineLabel(noOwnedContextLabel);
                EmitFutexWait(RVRegister.X28, 2);
                EmitMove(RVRegister.X10, RVRegister.X0);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

                DefineLabel(ownedFoundLabel);
                EmitMove(RVRegister.X31, RVRegister.X7);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X30,
                    RVRegister.X31,
                    checked(_target.PointerSize * 2)));
                Emit(new RVInstruction(RVInstrKind.Fence, immediate: 0x33));
                EmitLoadImmediate(RVRegister.X5, contextCount);
                EmitMove(RVRegister.X6, RVRegister.X30);

                DefineLabel(chainLoopLabel);
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X6, RVRegister.X29, cycleLabel));
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X5, RVRegister.X0, noCycleLabel));
                EmitMaterializeAddress(contextTableLabel, RVRegister.X7);
                EmitLoadImmediate(RVRegister.X13, contextCount);

                DefineLabel(scanOwnerLoopLabel);
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X13, RVRegister.X0, noCycleLabel));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X14,
                    RVRegister.X7,
                    0));
                Emit(RVInstruction.I(RVInstrKind.Lw, RVRegister.X15, RVRegister.X14, 0));
                EmitLoadImmediate(RVRegister.X16, 2);
                Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X15, RVRegister.X16, scanOwnerNextLabel));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X15,
                    RVRegister.X14,
                    _target.PointerSize));
                Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X15, RVRegister.X6, scanOwnerNextLabel));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X15,
                    RVRegister.X14,
                    checked(_target.PointerSize * 2)));
                Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X15, RVRegister.X0, scanOwnerNextLabel));
                EmitMove(RVRegister.X6, RVRegister.X15);
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X5, RVRegister.X5, -1));
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, chainLoopLabel));

                DefineLabel(scanOwnerNextLabel);
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X7, RVRegister.X7, _target.PointerSize));
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X13, RVRegister.X13, -1));
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, scanOwnerLoopLabel));

                DefineLabel(noCycleLabel);
                EmitFutexWait(RVRegister.X28, 2);
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, clearAndReturnZeroLabel));

                DefineLabel(cycleLabel);
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, clearAndReturnOneLabel));

                DefineLabel(clearAndReturnZeroLabel);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X0,
                    RVRegister.X31,
                    checked(_target.PointerSize * 2)));
                Emit(new RVInstruction(RVInstrKind.Fence, immediate: 0x33));
                EmitMove(RVRegister.X10, RVRegister.X0);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

                DefineLabel(clearAndReturnOneLabel);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X0,
                    RVRegister.X31,
                    checked(_target.PointerSize * 2)));
                Emit(new RVInstruction(RVInstrKind.Fence, immediate: 0x33));
                EmitLoadImmediate(RVRegister.X10, 1);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

                _symbols.Add(new RVObjectSymbol(
                    label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Function));
            }

            private void EmitTypeInitializationThunk(TypeInitializationThunkDraft thunk, string? waitHelperLabel)
            {
                int startOffset = _text.ByteLength;
                DefineLabel(thunk.Label);

                int frameSize = AlignValueUp(checked(_target.PointerSize * 2), _target.CallFrameAlignment);
                int savedReturnAddressOffset = 0;
                int savedFramePointerOffset = _target.PointerSize;
                EmitAdjustStack(-frameSize);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X1,
                    RVRegister.X2,
                    savedReturnAddressOffset));
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X8,
                    RVRegister.X2,
                    savedFramePointerOffset));
                EmitMove(RVRegister.X8, RVRegister.X2);

                string retryLabel = CreateLocalLabel(thunk.Label + "_retry");
                string runningLabel = CreateLocalLabel(thunk.Label + "_running");
                string executeLabel = CreateLocalLabel(thunk.Label + "_execute");
                string doneLabel = CreateLocalLabel(thunk.Label + "_done");
                string failedLabel = CreateLocalLabel(thunk.Label + "_failed");
                bool hasAtomics = (_target.ArchitectureFeatures & TargetArchitectureFeatures.RiscVA) != 0;

                EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                if (hasAtomics)
                {
                    DefineLabel(retryLabel);
                    Emit(RVInstruction.Amo(RVInstrKind.LrW, RVRegister.X31, RVRegister.X28, RVRegister.X0, acquire: true));
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, doneLabel));
                    EmitLoadImmediate(RVRegister.X30, 2);
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X31, RVRegister.X30, runningLabel));
                    EmitLoadImmediate(RVRegister.X30, 1);
                    Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X31, RVRegister.X30, failedLabel));
                    EmitLoadImmediate(RVRegister.X29, 2);
                    Emit(RVInstruction.Amo(RVInstrKind.ScW, RVRegister.X30, RVRegister.X28, RVRegister.X29, release: true));
                    Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X30, RVRegister.X0, retryLabel));
                    Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, executeLabel));

                    DefineLabel(runningLabel);
                    EmitCurrentThreadId(RVRegister.X29);
                    EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                    Emit(RVInstruction.I(
                        _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                        RVRegister.X30,
                        RVRegister.X28,
                        _target.PointerSize));
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X30, RVRegister.X0, retryLabel));
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, doneLabel));
                    if (waitHelperLabel is not null)
                    {
                        EmitMove(RVRegister.X10, RVRegister.X28);
                        EmitMove(RVRegister.X11, RVRegister.X29);
                        EmitMove(RVRegister.X12, RVRegister.X30);
                        EmitPcrelTransfer(waitHelperLabel, link: true);
                        Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X10, RVRegister.X0, doneLabel));
                    }
                    else
                    {
                        EmitFutexWait(RVRegister.X28, 2);
                    }
                    EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                    Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, retryLabel));
                }
                else
                {
                    Emit(RVInstruction.I(RVInstrKind.Lw, RVRegister.X31, RVRegister.X28, 0));
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, doneLabel));
                    EmitLoadImmediate(RVRegister.X30, 2);
                    Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X31, RVRegister.X30, doneLabel));
                    EmitLoadImmediate(RVRegister.X30, 1);
                    Emit(RVInstruction.B(RVInstrKind.Bne, RVRegister.X31, RVRegister.X30, failedLabel));
                    EmitLoadImmediate(RVRegister.X30, 2);
                    Emit(RVInstruction.S(RVInstrKind.Sw, RVRegister.X30, RVRegister.X28, 0));
                }

                DefineLabel(executeLabel);
                EmitCurrentThreadId(RVRegister.X29);
                EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X29,
                    RVRegister.X28,
                    _target.PointerSize));
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X0,
                    RVRegister.X28,
                    checked(_target.PointerSize * 2)));
                string returnLabel = CreateLocalLabel(thunk.Label + "_gc_return");
                AddSafePoint(
                    thunk.Label,
                    returnLabel,
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    ImmutableArray<SafePointRootDraft>.Empty);
                EmitPcrelTransfer(ResolveMethodLabel(thunk.Initializer), link: true);
                DefineLabel(returnLabel);
                EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X0,
                    RVRegister.X28,
                    checked(_target.PointerSize * 2)));
                Emit(RVInstruction.S(
                    _machineTarget.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                    RVRegister.X0,
                    RVRegister.X28,
                    _target.PointerSize));
                if (hasAtomics)
                {
                    Emit(RVInstruction.Amo(RVInstrKind.AmoSwapW, RVRegister.X0, RVRegister.X28, RVRegister.X0, release: true));
                    EmitMaterializeAddress(thunk.StateLabel, RVRegister.X28);
                    EmitFutexWakeAll(RVRegister.X28);
                }
                else
                {
                    Emit(new RVInstruction(RVInstrKind.Fence, immediate: 0x31));
                    Emit(RVInstruction.S(RVInstrKind.Sw, RVRegister.X0, RVRegister.X28, 0));
                }
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, doneLabel));

                DefineLabel(failedLabel);
                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitLoadImmediate(RVRegister.X10, 150);
                    EmitPcrelTransfer(ResolveExternalSymbol(RiscVRuntime.FailFastSymbol), link: false);
                }
                else
                {
                    Emit(new RVInstruction(RVInstrKind.Ebreak));
                    Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, 0));
                }

                DefineLabel(doneLabel);
                EmitMove(RVRegister.X2, RVRegister.X8);
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X1,
                    RVRegister.X2,
                    savedReturnAddressOffset));
                Emit(RVInstruction.I(
                    _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                    RVRegister.X8,
                    RVRegister.X2,
                    savedFramePointerOffset));
                EmitAdjustStack(frameSize);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

                _symbols.Add(new RVObjectSymbol(
                    thunk.Label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Function));
            }

            public string GetTypeInitializationStateLabel(RuntimeType type)
            {
                if (_target.OperatingSystem == OperatingSystemKind.Linux &&
                    (_target.ArchitectureFeatures & TargetArchitectureFeatures.RiscVA) == 0)
                {
                    throw new NotSupportedException("Thread-safe static initialization on Linux RISC-V requires the A extension.");
                }

                StaticStorageDraft storage = GetOrCreateStaticStorage(type);
                if (storage.InitializationStateLabel is not null)
                    return storage.InitializationStateLabel;

                string label = CreateLocalLabel($"type_init_{type.TypeId}");
                int size = checked(_target.PointerSize * 3);
                int offset = _data.Align(_target.PointerSize);
                var context = new byte[size];
                context[_target.Endianness == TargetEndianness.Little ? 0 : 3] = 1;
                _data.EmitBytes(context);
                _symbols.Add(new RVObjectSymbol(
                    label,
                    DataSectionName,
                    offset,
                    size,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Object));
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
                _symbols.Add(new RVObjectSymbol(
                    label,
                    DataSectionName,
                    offset,
                    size,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Object));

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

            private string GetStringLiteralLabel(RuntimeType type, string text)
            {
                if (!IsSystemStringType(type))
                    throw new InvalidOperationException("A string literal must have System.String runtime type.");
                text ??= string.Empty;
                if (_stringLiteralLabels.TryGetValue(text, out string? existing))
                    return existing;

                string label = CreateLocalLabel("string_literal");
                string typeDescriptorLabel = GetTypeDescriptorLabel(type);
                _stringLiteralLabels.Add(text, label);
                _stringLiterals.Add(new StringLiteralDraft(text, label, typeDescriptorLabel));
                return label;
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
                    throw new TypeLoadException($"Runtime type '{@namespace}.{name}' is required by RISC-V exception lowering.");
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

            private SafePointDraft AddSafePoint(
                string methodLabel,
                string returnLabel,
                int savedFramePointerOffset,
                int savedReturnAddressOffset,
                ImmutableArray<SafePointRootDraft> roots)
            {
                string descriptorLabel = CreateLocalLabel(methodLabel + "_gc_safe_point");
                var draft = new SafePointDraft(
                    descriptorLabel,
                    returnLabel,
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    roots);
                _safePoints.Add(draft);
                return draft;
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
                        if (field.IsStatic)
                            continue;
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

            private void EmitEhTransferHelper()
            {
                string label = RiscVRuntime.EhTransferSymbol;
                if (!_usedLabels.Add(label))
                    throw new InvalidOperationException($"Duplicate RISC-V EH transfer symbol: {label}.");

                int startOffset = _text.ByteLength;
                DefineLabel(label);
                EmitMove(RVRegister.X28, RVRegister.X10);
                EmitMove(RVRegister.X29, RVRegister.X11);
                EmitMove(RVRegister.X30, RVRegister.X12);

                for (int i = 0; i < MachineRegisters.DefaultAllocatableGprs.Length; i++)
                {
                    MachineRegister register = MachineRegisters.DefaultAllocatableGprs[i];
                    Emit(RVInstruction.I(
                        _machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                        (RVRegister)(byte)register,
                        RVRegister.X30,
                        (byte)register * 8));
                }

                int floatingSize = RegisterInfo.AbiFloatingRegisterSize(_target);
                if (floatingSize != 0)
                {
                    for (int i = 0; i < MachineRegisters.DefaultAllocatableFprs.Length; i++)
                    {
                        MachineRegister register = MachineRegisters.DefaultAllocatableFprs[i];
                        int registerIndex = (byte)register - (byte)MachineRegister.F0;
                        Emit(RVInstruction.I(
                            floatingSize <= 4 ? RVInstrKind.Flw : RVInstrKind.Fld,
                            (RVRegister)(byte)register,
                            RVRegister.X30,
                            256 + registerIndex * 8));
                    }
                }

                EmitMove(RVRegister.X2, RVRegister.X28);
                EmitMove(RVRegister.X8, RVRegister.X28);
                Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X29, 0));

                _symbols.Add(new RVObjectSymbol(
                    label,
                    TextSectionName,
                    startOffset,
                    _text.ByteLength - startOffset,
                    RVObjectSymbolBinding.Global,
                    RVObjectSymbolKind.Function));
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
                            throw new InvalidOperationException($"RISC-V EH native ranges were not bound for method M{method.Method.RuntimeMethod.MethodId}.");
                        }

                        EmitNative(clause.Kind);
                        EmitPointer(clause.TryStartLabel);
                        EmitPointer(clause.TryEndLabel);
                        EmitPointer(clause.HandlerStartLabel);
                        EmitPointer(clause.HandlerEndLabel);
                        EmitPointer(clause.CatchTypeLabel);
                        EmitNative(clause.ParentLocalIndex);
                        EmitNative(clause.Region.TryStartPc);
                        EmitNative(clause.Region.TryEndPc);
                        EmitNative(clause.Region.HandlerStartPc);
                        EmitNative(clause.Region.HandlerEndPc);
                        EmitNative(clause.Region.SourceHandlerIndex);
                    }

                    int infoOffset = _rodata.Align(pointerSize);
                    AddDataSymbol(method.InfoLabel, infoOffset, pointerSize * 2);
                    EmitNative(method.Clauses.Length);
                    EmitPointer(method.ClausesLabel);
                }
            }

            private RuntimeMetadataLabels EmitRuntimeMetadata()
            {
                int pointerSize = _target.PointerSize;

                EmitExceptionMetadata();

                for (int i = 0; i < _typeDescriptors.Count; i++)
                {
                    TypeDescriptorDraft descriptor = _typeDescriptors[i];
                    if (descriptor.Fields.Length != 0)
                    {
                        descriptor.FieldsLabel = CreateLocalLabel(descriptor.Label + "_fields");
                        int offset = _rodata.Align(pointerSize);
                        AddDataSymbol(descriptor.FieldsLabel, offset, checked(descriptor.Fields.Length * pointerSize * 2));
                        for (int f = 0; f < descriptor.Fields.Length; f++)
                        {
                            EmitNative(descriptor.Fields[f].Offset);
                            EmitNative(ToRuntimeRootKind(descriptor.Fields[f].Kind));
                        }
                    }
                    if (descriptor.ComponentFields.Length != 0)
                    {
                        descriptor.ComponentFieldsLabel = CreateLocalLabel(descriptor.Label + "_component_fields");
                        int offset = _rodata.Align(pointerSize);
                        AddDataSymbol(descriptor.ComponentFieldsLabel, offset, checked(descriptor.ComponentFields.Length * pointerSize * 2));
                        for (int f = 0; f < descriptor.ComponentFields.Length; f++)
                        {
                            EmitNative(descriptor.ComponentFields[f].Offset);
                            EmitNative(ToRuntimeRootKind(descriptor.ComponentFields[f].Kind));
                        }
                    }
                    if (descriptor.Interfaces.Length != 0)
                    {
                        descriptor.InterfacesLabel = CreateLocalLabel(descriptor.Label + "_interfaces");
                        int offset = _rodata.Align(pointerSize);
                        AddDataSymbol(descriptor.InterfacesLabel, offset, checked((descriptor.Interfaces.Length + 1) * pointerSize));
                        for (int iface = 0; iface < descriptor.Interfaces.Length; iface++)
                            EmitPointer(GetTypeDescriptorLabel(descriptor.Interfaces[iface]));
                        EmitNative(0);
                    }
                    if (descriptor.VTableTargets.Length != 0)
                    {
                        descriptor.VTableLabel = CreateLocalLabel(descriptor.Label + "_vtable");
                        int offset = _rodata.Align(pointerSize);
                        AddDataSymbol(descriptor.VTableLabel, offset, checked(descriptor.VTableTargets.Length * pointerSize));
                        for (int slot = 0; slot < descriptor.VTableTargets.Length; slot++)
                            EmitPointer(descriptor.VTableTargets[slot]);
                    }
                }

                for (int i = 0; i < _typeDescriptors.Count; i++)
                {
                    TypeDescriptorDraft descriptor = _typeDescriptors[i];
                    bool isString = IsSystemStringType(descriptor.Type);
                    bool isArray = descriptor.Type.Kind == RuntimeTypeKind.Array;
                    int componentSize = isString
                        ? 2
                        : isArray
                            ? GetArrayComponentSize(descriptor.Type)
                            : 0;
                    uint flags = ComputeMethodTableFlags(descriptor, componentSize);
                    int baseSize = isString
                        ? checked(_target.SyncBlockSize + _target.StringCharsOffset + 2)
                        : isArray
                            ? checked(
                                _target.SyncBlockSize +
                                _target.ArrayDataOffset +
                                (descriptor.Type.IsSzArray ? 0 : descriptor.Type.ArrayRank * 8))
                            : descriptor.Type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer
                                ? 0
                                : descriptor.Type.Kind == RuntimeTypeKind.ByRef
                                    ? 1
                                    : Math.Max(
                                        _target.MinimumGcObjectSize,
                                        descriptor.Type.IsValueType
                                            ? checked(
                                                _target.SyncBlockSize +
                                                _target.ManagedObjectHeaderSize +
                                                descriptor.Type.SizeOf)
                                            : checked(_target.SyncBlockSize + descriptor.Type.InstanceSize));
                    int offset = _rodata.Align(pointerSize);
                    AddDataSymbol(descriptor.Label, offset, checked(16 + pointerSize * 3));
                    EmitUInt32(flags);
                    EmitUInt32(checked((uint)baseSize));
                    EmitPointer(descriptor.RelatedTypeLabel);
                    EmitUInt16(checked((ushort)descriptor.VTableTargets.Length));
                    EmitUInt16(checked((ushort)descriptor.Interfaces.Length));
                    EmitUInt32(unchecked((uint)descriptor.Type.TypeId));
                    EmitPointer(descriptor.InterfacesLabel);
                    EmitPointer(descriptor.VTableLabel);
                }

                for (int i = 0; i < _interfaceDispatchCells.Count; i++)
                {
                    InterfaceDispatchCellDraft cell = _interfaceDispatchCells[i];
                    int offset = _rodata.Align(pointerSize);
                    AddDataSymbol(cell.Label, offset, checked((1 + cell.Entries.Length * 2) * pointerSize));
                    EmitNative(cell.Entries.Length);
                    for (int entry = 0; entry < cell.Entries.Length; entry++)
                    {
                        EmitPointer(cell.Entries[entry].ReceiverTypeLabel);
                        EmitPointer(cell.Entries[entry].TargetLabel);
                    }
                }

                string typeInfoTableLabel = CreateLocalLabel("gc_type_infos");
                int typeInfoTableOffset = _rodata.Align(pointerSize);
                int typeInfoTableSize = _typeDescriptors.Count == 0
                    ? pointerSize
                    : checked(_typeDescriptors.Count * pointerSize * 6);
                AddDataSymbol(typeInfoTableLabel, typeInfoTableOffset, typeInfoTableSize);
                if (_typeDescriptors.Count == 0)
                {
                    EmitNative(0);
                }
                else
                {
                    for (int i = 0; i < _typeDescriptors.Count; i++)
                    {
                        TypeDescriptorDraft descriptor = _typeDescriptors[i];
                        EmitPointer(descriptor.Label);
                        EmitNative(descriptor.Fields.Length);
                        EmitPointer(descriptor.FieldsLabel);
                        EmitNative(descriptor.ComponentFields.Length);
                        EmitPointer(descriptor.ComponentFieldsLabel);
                        EmitNative(GetRuntimeTypeInfoKind(descriptor.Type));
                    }
                }

                for (int i = 0; i < _stringLiterals.Count; i++)
                {
                    StringLiteralDraft literal = _stringLiterals[i];
                    byte[] chars = Encoding.Unicode.GetBytes(literal.Text);
                    int objectSize = checked(pointerSize + 4 + chars.Length + 2);
                    _rodata.Align(pointerSize);
                    EmitNative(0);
                    int offset = _rodata.ByteLength;
                    AddDataSymbol(literal.Label, offset, objectSize);
                    EmitPointer(literal.TypeDescriptorLabel);
                    EmitInt32(literal.Text.Length);
                    _rodata.EmitBytes(chars);
                    _rodata.EmitBytes(new byte[2]);
                }

                for (int i = 0; i < _staticExceptions.Count; i++)
                {
                    StaticExceptionDraft exception = _staticExceptions[i];
                    int objectSize = Math.Max(pointerSize, exception.Type.InstanceSize);
                    _rodata.Align(pointerSize);
                    EmitNative(0);
                    int offset = _rodata.ByteLength;
                    AddDataSymbol(exception.ObjectLabel, offset, objectSize);
                    EmitPointer(exception.TypeDescriptorLabel);
                    if (objectSize > pointerSize)
                        _rodata.EmitBytes(new byte[objectSize - pointerSize]);
                }

                for (int i = 0; i < _safePoints.Count; i++)
                {
                    SafePointDraft safePoint = _safePoints[i];
                    if (safePoint.Roots.Length == 0)
                        continue;
                    safePoint.RootsLabel = CreateLocalLabel(safePoint.DescriptorLabel + "_roots");
                    int offset = _rodata.Align(pointerSize);
                    AddDataSymbol(safePoint.RootsLabel, offset, checked(safePoint.Roots.Length * pointerSize * 2));
                    for (int r = 0; r < safePoint.Roots.Length; r++)
                    {
                        EmitNative(safePoint.Roots[r].FrameOffset);
                        EmitNative(ToRuntimeRootKind(safePoint.Roots[r].Kind));
                    }
                }

                string tableLabel = CreateLocalLabel("gc_safe_points");
                int tableOffset = _rodata.Align(pointerSize);
                int tableSize = _safePoints.Count == 0
                    ? pointerSize
                    : checked(_safePoints.Count * pointerSize * 5);
                AddDataSymbol(tableLabel, tableOffset, tableSize);
                if (_safePoints.Count == 0)
                {
                    EmitNative(0);
                }
                else
                {
                    for (int i = 0; i < _safePoints.Count; i++)
                    {
                        SafePointDraft safePoint = _safePoints[i];
                        int recordOffset = _rodata.ByteLength;
                        AddDataSymbol(safePoint.DescriptorLabel, recordOffset, pointerSize * 5);
                        EmitPointer(safePoint.ReturnLabel);
                        EmitNative(safePoint.SavedFramePointerOffset);
                        EmitNative(safePoint.SavedReturnAddressOffset);
                        EmitNative(safePoint.Roots.Length);
                        EmitPointer(safePoint.RootsLabel);
                    }
                }

                string staticRootTableLabel = CreateLocalLabel("gc_static_roots");
                int staticRootTableOffset = _rodata.Align(pointerSize);
                int staticRootTableSize = _staticRoots.Count == 0
                    ? pointerSize
                    : checked(_staticRoots.Count * pointerSize * 2);
                AddDataSymbol(staticRootTableLabel, staticRootTableOffset, staticRootTableSize);
                if (_staticRoots.Count == 0)
                {
                    EmitNative(0);
                }
                else
                {
                    for (int i = 0; i < _staticRoots.Count; i++)
                    {
                        StaticRootDraft root = _staticRoots[i];
                        EmitPointer(root.StorageLabel, root.Offset);
                        EmitNative(ToRuntimeRootKind(root.Kind));
                    }
                }

                return new RuntimeMetadataLabels(tableLabel, typeInfoTableLabel, staticRootTableLabel);
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

            private int GetArrayComponentSize(RuntimeType arrayType)
            {
                RuntimeType elementType = arrayType.ElementType ?? throw new InvalidOperationException("Array runtime type has no element type.");
                if (elementType.IsReferenceType || elementType.Kind is RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                    return _target.PointerSize;
                return Math.Max(1, elementType.SizeOf);
            }

            private static uint ComputeMethodTableFlags(TypeDescriptorDraft descriptor, int componentSize)
            {
                const uint parameterizedKind = 0x00020000u;
                const uint hasPointers = 0x01000000u;
                const uint elementTypeShift = 26u;
                const uint hasComponentSize = 0x80000000u;

                uint flags = GetMethodTableElementType(descriptor.Type) << (int)elementTypeShift;
                if (descriptor.Type.Kind is RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
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

                if (IsSystemArrayType(type))
                    return 0x16u;

                return type.Kind switch
                {
                    RuntimeTypeKind.Struct => IsNullableType(type) ? 0x12u : 0x10u,
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

            private static bool IsNullableType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   type.Name.StartsWith("Nullable", StringComparison.Ordinal);

            private void AddDataSymbol(string label, int offset, int size)
            {
                _symbols.Add(new RVObjectSymbol(
                    label,
                    RodataSectionName,
                    offset,
                    size,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Object));
            }

            private void EmitPointer(string? symbol, int addend = 0)
            {
                int offset = _rodata.ByteLength;
                _rodata.EmitBytes(new byte[_target.PointerSize]);
                if (symbol is not null)
                    _rodata.AddRelocation(offset, symbol, addend, RVObjectRelocationKind.AbsolutePointer);
            }

            private void EmitNative(long value)
            {
                byte[] bytes = _target.PointerSize == 8
                    ? BitConverter.GetBytes(value)
                    : BitConverter.GetBytes(checked((int)value));
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                _rodata.EmitBytes(bytes);
            }

            private void EmitInt32(int value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                _rodata.EmitBytes(bytes);
            }

            private void EmitUInt32(uint value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                _rodata.EmitBytes(bytes);
            }

            private void EmitUInt16(ushort value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                _rodata.EmitBytes(bytes);
            }

            private static int ToRuntimeRootKind(RegisterGcRootKind kind)
                => kind == RegisterGcRootKind.ObjectReference ? 0 : 1;

            private sealed class MethodEmitter
            {
                private readonly Generator _owner;
                private readonly GenTreeMethod _method;
                private readonly string _methodLabel;
                private readonly string[] _blockLabels;
                private readonly string[] _blockEndLabels;
                private readonly Dictionary<int, int> _nodePositions;
                private readonly Dictionary<(int Offset, int Length), string> _staticDataLabels = new Dictionary<(int Offset, int Length), string>();
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
                    for (int i = 0; i < _blockLabels.Length; i++)
                    {
                        _blockLabels[i] = owner.CreateLocalLabel($"{methodLabel}_B{i}");
                        _blockEndLabels[i] = owner.CreateLocalLabel($"{methodLabel}_B{i}_end");
                    }
                    owner._ehMethodsByMethodId.TryGetValue(method.RuntimeMethod.MethodId, out _ehMethod);
                    _returnThunkLabel = owner.CreateLocalLabel(methodLabel + "_eh_return");
                }

                private TargetInfo Target => _owner._target;
                private RVTarget MachineTarget => _owner._machineTarget;

                public void Emit()
                {
                    ValidateMethodShape();
                    var order = _method.LinearBlockOrder;
                    for (int i = 0; i < order.Length; i++)
                    {
                        int blockId = order[i];
                        _nextBlockId = i + 1 < order.Length ? order[i + 1] : -1;
                        var block = _method.Blocks[blockId];
                        int firstBodyNode = blockId == 0
                            ? GenTreeLirKinds.PrologPrefixLength(block.LinearNodes)
                            : 0;
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
                    if (MethodHasGcSafePoint())
                    {
                        if (!_method.StackFrame.UsesFramePointer)
                            throw new InvalidOperationException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' must use a frame pointer for precise GC stack walking.");
                        if (!_method.StackFrame.TryGetCalleeSavedSlot(MachineRegisters.FramePointer, out _) ||
                            !_method.StackFrame.TryGetCalleeSavedSlot(MachineRegisters.ReturnAddress, out _))
                        {
                            throw new InvalidOperationException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' has no unwindable frame for precise GC stack walking.");
                        }
                    }

                    if (MachineTarget.Is64Bit)
                        return;

                    if (IsI8(_method.RuntimeMethod.ReturnType, StackKindForType(_method.RuntimeMethod.ReturnType)))
                        throw new NotImplementedException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' requires soft-long return lowering on RV32.");

                    foreach (var argumentType in _method.ArgTypes)
                    {
                        if (IsI8(argumentType, StackKindForType(argumentType)))
                            throw new NotImplementedException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' requires soft-long argument lowering on RV32.");
                    }

                    foreach (var descriptor in _method.AllLocalDescriptors)
                    {
                        if (IsI8(descriptor.Type, descriptor.StackKind))
                            throw new NotImplementedException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' requires soft-long local lowering on RV32.");
                    }

                    foreach (var value in _method.Values)
                    {
                        if (IsI8(value.Type, value.StackKind))
                            throw new NotImplementedException($"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}' requires soft-long value lowering on RV32.");
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
                        {
                            return true;
                        }
                        if (node.TreeKind == GenTreeKind.DelegateInvoke ||
                            node.TreeKind == GenTreeKind.IndirectCall ||
                            node.TreeKind == GenTreeKind.VirtualCall ||
                            (node.TreeKind == GenTreeKind.Call &&
                             (node.Method?.HasInternalCall != true ||
                              (node.Method is not null && RiscVRuntime.IsGcSafePointInternalCall(node.Method)))))
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
                    var order = method.LinearBlockOrder.IsDefaultOrEmpty
                        ? LinearBlockOrder.Compute(method.Cfg)
                        : method.LinearBlockOrder;

                    for (int o = 0; o < order.Length; o++)
                    {
                        var nodes = method.Blocks[order[o]].LinearNodes;
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

                private SafePointDraft PrepareSafePoint(GenTree node, RegisterOperand additionalRoot = default)
                {
                    if (!_nodePositions.TryGetValue(node.LinearId, out int position))
                        throw Unsupported(node, "GC safe point has no final LIR position");
                    if (!_method.StackFrame.UsesFramePointer)
                        throw Unsupported(node, "GC safe point requires a frame pointer");
                    if (!_method.StackFrame.TryGetCalleeSavedSlot(MachineRegisters.FramePointer, out StackFrameSlot savedFramePointer) ||
                        !_method.StackFrame.TryGetCalleeSavedSlot(MachineRegisters.ReturnAddress, out StackFrameSlot savedReturnAddress))
                    {
                        throw Unsupported(node, "GC safe point requires saved frame-pointer and return-address slots");
                    }

                    var liveRoots = new List<RegisterGcLiveRoot>();
                    for (int i = 0; i < _method.GcLiveRanges.Length; i++)
                    {
                        RegisterGcLiveRange range = _method.GcLiveRanges[i];
                        if (range.FuncletIndex != 0 || range.StartPosition > position || position >= range.EndPosition)
                            continue;
                        if (!ContainsRootCell(liveRoots, range.Root))
                            liveRoots.Add(range.Root);
                    }

                    int additionalRootCount = additionalRoot.IsNone ? 0 : 1;
                    int rootCount = checked(liveRoots.Count + additionalRootCount);
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
                        SpillGcRoot(liveRoots[i], rootIndex);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize),
                            liveRoots[i].RootKind));
                        rootIndex++;
                    }

                    if (!additionalRoot.IsNone)
                    {
                        SpillGcRoot(additionalRoot, RegisterGcRootKind.ObjectReference, 0, rootIndex);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize),
                            RegisterGcRootKind.ObjectReference));
                    }

                    string returnLabel = _owner.CreateLocalLabel(_methodLabel + "_gc_return");
                    return _owner.AddSafePoint(
                        _methodLabel,
                        returnLabel,
                        savedFramePointer.Offset,
                        savedReturnAddress.Offset,
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

                private void SpillGcRoot(RegisterGcLiveRoot root, int rootIndex)
                    => SpillGcRoot(root.Location, root.RootKind, root.Offset, rootIndex);

                private void SpillGcRoot(RegisterOperand location, RegisterGcRootKind rootKind, int cellOffset, int rootIndex)
                {
                    MachineRegister source;
                    if (location.IsRegister)
                    {
                        if (cellOffset != 0)
                            throw new InvalidOperationException("A register GC root cannot have a non-zero cell offset.");
                        if (location.RegisterClass != RegisterClass.General)
                            throw new InvalidOperationException("A GC root cannot reside in a floating-point register.");
                        source = location.Register;
                    }
                    else if (location.IsFrameSlot)
                    {
                        int offset = checked(EffectiveFrameOffset(location) + cellOffset);
                        EmitMemoryLoad(MachineRegister.X31, FrameBase(location), offset, Target.PointerSize, signed: false);
                        source = MachineRegister.X31;
                    }
                    else
                    {
                        throw new InvalidOperationException("GC root location is not final: " + location);
                    }

                    int spillOffset = checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize);
                    EmitMemoryStore(source, RVRegister.X8, spillOffset, Target.PointerSize);
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
                            RuntimeType? type = TypeOperationScratchType(_method.LinearNodes[i]);
                            if (type?.IsValueType != true)
                                continue;

                            size = Math.Max(size, Math.Max(1, type.SizeOf));
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

                private int NewObjectArgumentSaveOffset
                    => AlignUp(
                        checked(TypeOperationScratchOffset + TypeOperationScratchSize),
                        Math.Max(Target.PointerSize, RegisterInfo.AbiFloatingRegisterSize(Target)));

                private void SaveNewObjectArguments()
                {
                    int offset = NewObjectArgumentSaveOffset;
                    for (int i = 1; i < 8; i++)
                    {
                        MachineRegister register = MachineRegisters.GetIntegerArgumentRegister(i);
                        EmitMemoryStore(register, RVRegister.X8, offset, Target.GeneralRegisterSize);
                        offset = checked(offset + Target.GeneralRegisterSize);
                    }

                    int floatingSize = RegisterInfo.AbiFloatingRegisterSize(Target);
                    if (floatingSize != 0)
                    {
                        offset = AlignUp(offset, floatingSize);
                        for (int i = 0; i < 8; i++)
                        {
                            MachineRegister register = MachineRegisters.GetFloatArgumentRegister(i);
                            EmitMemoryStore(register, RVRegister.X8, offset, floatingSize);
                            offset = checked(offset + floatingSize);
                        }
                    }

                    if (offset > _method.StackFrame.GcSpillAreaOffset + _method.StackFrame.GcSpillAreaSize)
                        throw new InvalidOperationException("GC transition area is smaller than the RISC-V argument register save set.");
                }

                private void RestoreNewObjectArguments()
                {
                    int offset = NewObjectArgumentSaveOffset;
                    for (int i = 1; i < 8; i++)
                    {
                        MachineRegister register = MachineRegisters.GetIntegerArgumentRegister(i);
                        EmitMemoryLoad(register, RVRegister.X8, offset, Target.GeneralRegisterSize, signed: false);
                        offset = checked(offset + Target.GeneralRegisterSize);
                    }

                    int floatingSize = RegisterInfo.AbiFloatingRegisterSize(Target);
                    if (floatingSize != 0)
                    {
                        offset = AlignUp(offset, floatingSize);
                        for (int i = 0; i < 8; i++)
                        {
                            MachineRegister register = MachineRegisters.GetFloatArgumentRegister(i);
                            EmitMemoryLoad(register, RVRegister.X8, offset, floatingSize, signed: false);
                            offset = checked(offset + floatingSize);
                        }
                    }
                }

                private static int AlignUp(int value, int alignment)
                {
                    int remainder = value % alignment;
                    return remainder == 0 ? value : checked(value + alignment - remainder);
                }

                private void EmitFallthroughFixup(int blockId, int nextBlockId)
                {
                    var successors = _method.Cfg.Blocks[blockId].Successors;
                    foreach (var edge in successors)
                    {
                        if (edge.Kind == CfgEdgeKind.FallThrough && edge.ToBlockId != nextBlockId)
                        {
                            EmitJump(_blockLabels[edge.ToBlockId]);
                            return;
                        }
                    }
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
                    var order = _method.LinearBlockOrder;
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

                    string countSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFrameCountSymbol);
                    string framesSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFramesSymbol);
                    string capacityAvailable = _owner.CreateLocalLabel(_methodLabel + "_eh_frame_capacity");

                    _owner.EmitMaterializeAddress(countSymbol, RVRegister.X28);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitLoadImmediate(RVRegister.X30, 4096);
                    _owner.Emit(RVInstruction.B(RVInstrKind.Bltu, RVRegister.X29, RVRegister.X30, capacityAvailable));
                    _owner.EmitLoadImmediate(RVRegister.X10, 150);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.FailFastSymbol), link: false);
                    _owner.DefineLabel(capacityAvailable);

                    EmitEhFrameAddress(RVRegister.X29, framesSymbol, RVRegister.X31);
                    _owner.EmitMaterializeAddress(_ehMethod.InfoLabel, RVRegister.X30);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X31, 0, Target.PointerSize);
                    EmitMemoryStore(MachineRegister.X8, RVRegister.X31, Target.PointerSize, Target.PointerSize);
                    _owner.EmitMaterializeAddress(currentIpLabel, RVRegister.X30);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X31, Target.PointerSize * 2, Target.PointerSize);
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, 1);
                    EmitMemoryStore(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize);
                    _ehFrameRegistered = true;
                }

                private void EmitEhSetCurrentIp(string currentIpLabel)
                {
                    if (_ehMethod is null)
                        return;
                    if (!_ehFrameRegistered)
                        throw new InvalidOperationException($"Method M{_method.RuntimeMethod.MethodId} updates EH state before establishing its frame.");

                    string countSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFrameCountSymbol);
                    string framesSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFramesSymbol);
                    _owner.EmitMaterializeAddress(countSymbol, RVRegister.X28);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, -1);
                    EmitEhFrameAddress(RVRegister.X29, framesSymbol, RVRegister.X31);
                    _owner.EmitMaterializeAddress(currentIpLabel, RVRegister.X30);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X31, Target.PointerSize * 2, Target.PointerSize);
                }

                private void EmitEhFramePop()
                {
                    if (_ehMethod is null)
                        return;

                    string countSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFrameCountSymbol);
                    _owner.EmitMaterializeAddress(countSymbol, RVRegister.X28);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, -1);
                    EmitMemoryStore(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize);
                }

                private void EmitEhFrameAddress(RVRegister frameIndex, string framesSymbol, RVRegister destination)
                {
                    int pointerShift = Target.PointerSize == 8 ? 3 : 2;
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, destination, frameIndex, pointerShift));
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, RVRegister.X30, destination, destination));
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, destination, RVRegister.X30, destination));
                    _owner.EmitMaterializeAddress(framesSymbol, RVRegister.X30);
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, destination, destination, RVRegister.X30));
                }

                private void MarkEhCallSite(GenTree node, string suffix)
                {
                    if (_ehMethod is null)
                        return;
                    string label = _owner.CreateLocalLabel($"{_methodLabel}_eh_{suffix}_{node.LinearId}");
                    _owner.DefineLabel(label);
                    EmitEhSetCurrentIp(label);
                    EmitEhSaveRegisterContext();
                }

                private void EmitEhSaveRegisterContext()
                {
                    string countSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhFrameCountSymbol);
                    string contextsSymbol = _owner.ResolveExternalObjectSymbol(RiscVRuntime.EhRegisterContextsSymbol);
                    _owner.EmitMaterializeAddress(countSymbol, RVRegister.X28);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, -1);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, RVRegister.X31, RVRegister.X29, 9));
                    _owner.EmitMaterializeAddress(contextsSymbol, RVRegister.X30);
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, RVRegister.X31, RVRegister.X31, RVRegister.X30));

                    for (int i = 0; i < MachineRegisters.DefaultAllocatableGprs.Length; i++)
                    {
                        MachineRegister register = MachineRegisters.DefaultAllocatableGprs[i];
                        EmitMemoryStore(register, RVRegister.X31, (byte)register * 8, Target.GeneralRegisterSize);
                    }

                    int floatingSize = RegisterInfo.AbiFloatingRegisterSize(Target);
                    if (floatingSize == 0)
                        return;
                    for (int i = 0; i < MachineRegisters.DefaultAllocatableFprs.Length; i++)
                    {
                        MachineRegister register = MachineRegisters.DefaultAllocatableFprs[i];
                        int registerIndex = (byte)register - (byte)MachineRegister.F0;
                        EmitMemoryStore(register, RVRegister.X31, 256 + registerIndex * 8, floatingSize);
                    }
                }

                private void EmitExceptionObject(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(RiscVRuntime.CurrentExceptionSymbol),
                        RVRegister.X31);
                    EmitMemoryLoad(destination, RVRegister.X31, 0, Target.PointerSize, signed: false);
                }

                private void EmitThrow(GenTree node)
                {
                    MachineRegister exception = RequireUseRegisterForOperand(node, 0, "exception object");
                    MarkEhCallSite(node, "throw");
                    _owner.EmitMove(RVRegister.X10, ToIntegerRegister(exception));
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_throw_non_null_{node.LinearId}");
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X10, RVRegister.X0, nonNull);
                    _owner.EmitMaterializeAddress(
                        _owner.GetStaticExceptionObjectLabel("System", "NullReferenceException"),
                        RVRegister.X10);
                    _owner.DefineLabel(nonNull);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.ThrowSymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitRethrow(GenTree node)
                {
                    MarkEhCallSite(node, "rethrow");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.RethrowSymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitLeave(GenTree node)
                {
                    MarkEhCallSite(node, "leave");
                    _owner.EmitMaterializeAddress(LabelForTarget(node), RVRegister.X10);
                    _owner.EmitLoadImmediate(RVRegister.X11, 1);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.LeaveSymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitEndFinally(GenTree node)
                {
                    MarkEhCallSite(node, "endfinally");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.EndFinallySymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitUnreachableTrap()
                {
                    _owner.Emit(new RVInstruction(RVInstrKind.Ebreak));
                    _owner.Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, 0));
                }

                private void EmitManagedExceptionThrow(GenTree node, string exceptionTypeName)
                {
                    MarkEhCallSite(node, "implicit_throw");
                    _owner.EmitMaterializeAddress(
                        _owner.GetStaticExceptionObjectLabel("System", exceptionTypeName),
                        RVRegister.X10);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.ThrowSymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitNode(GenTree node)
                {
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.Copy:
                        case GenTreeKind.Reload:
                        case GenTreeKind.Spill:
                            EmitMove(node);
                            return;
                        case GenTreeKind.GcPoll:
                            EmitGcPoll(node);
                            return;
                        case GenTreeKind.ClassInit:
                            EmitClassInit(node);
                            return;
                        case GenTreeKind.StackFrameOp:
                            EmitFrameOperation(node);
                            return;
                        case GenTreeKind.Nop:
                        case GenTreeKind.Eval:
                            return;
                        case GenTreeKind.ConstI4:
                            _owner.EmitLoadImmediate(ToIntegerRegister(RequireResultRegister(node)), node.Int32);
                            return;
                        case GenTreeKind.ConstI8:
                            if (!MachineTarget.Is64Bit)
                                throw Unsupported(node, "Int64 constants on RV32 require a soft-long lowering pass");
                            _owner.EmitLoadImmediate(ToIntegerRegister(RequireResultRegister(node)), node.Int64);
                            return;
                        case GenTreeKind.ConstR4Bits:
                            EmitFloatConstant(node, 4, BitConverter.GetBytes(node.Int32));
                            return;
                        case GenTreeKind.ConstR8Bits:
                            EmitFloatConstant(node, 8, BitConverter.GetBytes(node.Int64));
                            return;
                        case GenTreeKind.ConstNull:
                            _owner.EmitMove(ToIntegerRegister(RequireResultRegister(node)), RVRegister.X0);
                            return;
                        case GenTreeKind.ConstString:
                            _owner.EmitMaterializeAddress(
                                _owner.GetStringLiteralLabel(RequireRuntimeType(node), node.Text ?? string.Empty),
                                ToIntegerRegister(RequireResultRegister(node)));
                            return;
                        case GenTreeKind.StaticData:
                            EmitStaticData(node);
                            return;
                        case GenTreeKind.DefaultValue:
                            EmitDefaultValue(node);
                            return;
                        case GenTreeKind.SizeOf:
                            _owner.EmitLoadImmediate(ToIntegerRegister(RequireResultRegister(node)), RequireRuntimeType(node).SizeOf);
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
                        case GenTreeKind.Unary:
                            EmitUnary(node);
                            return;
                        case GenTreeKind.Binary:
                            EmitBinary(node);
                            return;
                        case GenTreeKind.Conv:
                            EmitConversion(node);
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
                        case GenTreeKind.Return:
                            EmitReturn(node);
                            return;
                        case GenTreeKind.Call:
                            EmitCall(node);
                            return;
                        case GenTreeKind.IndirectCall:
                            EmitIndirectFunctionPointerCall(node);
                            return;
                        case GenTreeKind.VirtualCall:
                            EmitVirtualCall(node);
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
                        case GenTreeKind.LoadIndirect:
                        case GenTreeKind.StoreIndirect:
                            EmitIndirect(node);
                            return;
                        case GenTreeKind.PointerElementAddr:
                            EmitPointerElementAddress(node);
                            return;
                        case GenTreeKind.PointerToByRef:
                            EmitPointerToByRef(node);
                            return;
                        case GenTreeKind.PointerDiff:
                            EmitPointerDifference(node);
                            return;
                        case GenTreeKind.StackAlloc:
                            EmitStackAlloc(node);
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
                        case GenTreeKind.NewObject:
                            EmitNewObject(node);
                            return;
                        case GenTreeKind.NewArray:
                            EmitNewArray(node);
                            return;
                        case GenTreeKind.ArrayElement:
                        case GenTreeKind.ArrayElementAddr:
                        case GenTreeKind.StoreArrayElement:
                        case GenTreeKind.ArrayDataRef:
                            EmitArray(node);
                            return;
                        case GenTreeKind.CastClass:
                            EmitRuntimeTypeCheck(node, throwOnFailure: true);
                            return;
                        case GenTreeKind.IsInst:
                            EmitRuntimeTypeCheck(node, throwOnFailure: false);
                            return;
                        case GenTreeKind.Box:
                            EmitBox(node);
                            return;
                        case GenTreeKind.UnboxAny:
                            EmitUnboxAny(node);
                            return;
                        case GenTreeKind.DelegateInvoke:
                            EmitDelegateInvoke(node);
                            return;
                        case GenTreeKind.NewDelegate:
                            EmitNewDelegate(node);
                            return;
                        case GenTreeKind.DelegateCombine:
                            EmitDelegateCombineOrRemove(node, remove: false);
                            return;
                        case GenTreeKind.DelegateRemove:
                            EmitDelegateCombineOrRemove(node, remove: true);
                            return;
                        case GenTreeKind.AllocHGlobal:
                            EmitAllocHGlobal(node);
                            return;
                        case GenTreeKind.FreeHGlobal:
                            EmitFreeHGlobal(node);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported GenTree kind {node.TreeKind}");
                    }
                }

                private void EmitAllocHGlobal(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 1)
                        throw Unsupported(node, "AllocHGlobal requires one size operand and one result");

                    MachineRegister size = RequireUseRegisterForOperand(node, 0, "allocation size");
                    MachineRegister result = RequireResultRegister(node);
                    _owner.EmitMove(RVRegister.X10, ToIntegerRegister(size));
                    MarkEhCallSite(node, "alloc_hglobal");
                    _owner.EmitPcrelTransfer(
                        _owner.ResolveExternalSymbol(RiscVRuntime.AllocHGlobalSymbol),
                        link: true);
                    _owner.EmitMove(ToIntegerRegister(result), RVRegister.X10);
                }

                private void EmitFreeHGlobal(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 0)
                        throw Unsupported(node, "FreeHGlobal requires one pointer operand and no result");

                    MachineRegister pointer = RequireUseRegisterForOperand(node, 0, "allocation pointer");
                    _owner.EmitMove(RVRegister.X10, ToIntegerRegister(pointer));
                    MarkEhCallSite(node, "free_hglobal");
                    _owner.EmitPcrelTransfer(
                        _owner.ResolveExternalSymbol(RiscVRuntime.FreeHGlobalSymbol),
                        link: true);
                }


                private void PublishGcTransition(SafePointDraft safePoint)
                {
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(RiscVRuntime.CurrentSafePointSymbol),
                        RVRegister.X31);
                    _owner.EmitMaterializeAddress(safePoint.DescriptorLabel, RVRegister.X30);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X31, 0, Target.PointerSize);
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(RiscVRuntime.CurrentFramePointerSymbol),
                        RVRegister.X31);
                    EmitMemoryStore(MachineRegister.X8, RVRegister.X31, 0, Target.PointerSize);
                }

                private void EmitGcPoll(GenTree node)
                {
                    if (Target.OperatingSystem != OperatingSystemKind.Linux)
                        return;

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    MarkEhCallSite(node, "gc_poll");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.GcPollSymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitRuntimeTypeCheck(GenTree node, bool throwOnFailure)
                {
                    RuntimeType targetType = RequireRuntimeType(node);
                    if (throwOnFailure && !targetType.IsReferenceType)
                        throw Unsupported(node, "CastClass target must be a reference type");

                    RVRegister source = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "type-check source"));
                    RVRegister destination = ToIntegerRegister(RequireResultRegister(node));
                    string success = _owner.CreateLocalLabel(_methodLabel + "_type_check_success");
                    string failure = _owner.CreateLocalLabel(_methodLabel + "_type_check_failure");
                    string done = _owner.CreateLocalLabel(_methodLabel + "_type_check_done");

                    _owner.EmitMove(RVRegister.X28, source);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X28, RVRegister.X0, success);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(targetType), RVRegister.X29);
                    EmitLoadedTypeAssignabilityCheck(success, failure);

                    _owner.DefineLabel(failure);
                    if (throwOnFailure)
                    {
                        EmitManagedExceptionThrow(node, "InvalidCastException");
                    }
                    else
                    {
                        _owner.EmitMove(destination, RVRegister.X0);
                        EmitJump(done);
                    }

                    _owner.DefineLabel(success);
                    _owner.EmitMove(destination, RVRegister.X28);
                    _owner.DefineLabel(done);
                }

                private void EmitBox(GenTree node)
                {
                    RuntimeType boxedType = BoxSourceRuntimeType(node);
                    GenStackKind boxedKind = BoxSourceStackKind(node, boxedType);
                    RVRegister destination = ToIntegerRegister(RequireResultRegister(node));

                    if (boxedType.IsReferenceType)
                    {
                        RVRegister source = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "box source"));
                        _owner.EmitMove(destination, source);
                        return;
                    }

                    if (!boxedType.IsValueType)
                        throw Unsupported(node, "Box source must be a value type or an instantiated reference type");
                    if (TypeOperationScratchSize < Math.Max(1, boxedType.SizeOf))
                        throw Unsupported(node, "Type-operation scratch area is smaller than the box source");

                    int sourceUseIndex = RequireCodegenUseIndexForOperand(node, 0, "box source");
                    _owner.EmitAddImmediate(RVRegister.X28, RVRegister.X8, TypeOperationScratchOffset);
                    EmitValueToAddress(node, sourceUseIndex, boxedType, boxedKind, RVRegister.X28);

                    RuntimeType allocationType = boxedType;
                    int sourceOffset = 0;
                    string done = _owner.CreateLocalLabel(_methodLabel + "_box_done");
                    if (TryGetNullableInfo(boxedType, out RuntimeType underlyingType, out RuntimeField hasValueField, out RuntimeField valueField))
                    {
                        string hasValue = _owner.CreateLocalLabel(_methodLabel + "_box_nullable_has_value");
                        EmitMemoryLoad(
                            MachineRegister.X31,
                            RVRegister.X8,
                            checked(TypeOperationScratchOffset + hasValueField.Offset),
                            Math.Max(1, hasValueField.FieldType.SizeOf),
                            signed: false);
                        EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X0, hasValue);
                        _owner.EmitMove(destination, RVRegister.X0);
                        EmitJump(done);
                        _owner.DefineLabel(hasValue);
                        allocationType = underlyingType;
                        sourceOffset = valueField.Offset;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(allocationType), RVRegister.X10);
                    MarkEhCallSite(node, "box");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.NewFastSymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);

                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X10, Target.ManagedObjectHeaderSize);
                    _owner.EmitAddImmediate(
                        RVRegister.X30,
                        RVRegister.X8,
                        checked(TypeOperationScratchOffset + sourceOffset));
                    EmitBlockCopy(node, RVRegister.X29, RVRegister.X30, Math.Max(1, allocationType.SizeOf));
                    _owner.EmitMove(destination, RVRegister.X10);
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

                private GenStackKind BoxSourceStackKind(GenTree node, RuntimeType boxedType)
                {
                    if (!node.RegisterUses.IsDefaultOrEmpty)
                        return _method.GetValueInfo(node.RegisterUses[0]).StackKind;
                    return StackKindForType(boxedType);
                }

                private void EmitUnboxAny(GenTree node)
                {
                    RuntimeType targetType = RequireRuntimeType(node);
                    if (!targetType.IsValueType)
                    {
                        EmitRuntimeTypeCheck(node, throwOnFailure: true);
                        return;
                    }

                    RVRegister source = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "unbox source"));
                    _owner.EmitMove(RVRegister.X28, source);

                    if (TryGetNullableInfo(targetType, out RuntimeType underlyingType, out RuntimeField hasValueField, out RuntimeField valueField))
                    {
                        EmitNullableUnboxAny(node, targetType, underlyingType, hasValueField, valueField);
                        return;
                    }

                    string nonNull = _owner.CreateLocalLabel(_methodLabel + "_unbox_non_null");
                    string typeMatch = _owner.CreateLocalLabel(_methodLabel + "_unbox_type_match");
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X28, RVRegister.X0, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");

                    _owner.DefineLabel(nonNull);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(targetType), RVRegister.X29);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, typeMatch);
                    EmitManagedExceptionThrow(node, "InvalidCastException");

                    _owner.DefineLabel(typeMatch);
                    _owner.EmitAddImmediate(RVRegister.X28, RVRegister.X28, Target.ManagedObjectHeaderSize);
                    EmitValueFromAddress(node, targetType, node.StackKind, RVRegister.X28);
                }

                private void EmitNullableUnboxAny(
                    GenTree node,
                    RuntimeType nullableType,
                    RuntimeType underlyingType,
                    RuntimeField hasValueField,
                    RuntimeField valueField)
                {
                    if (TypeOperationScratchSize < Math.Max(1, nullableType.SizeOf))
                        throw Unsupported(node, "Type-operation scratch area is smaller than the nullable result");

                    string nullValue = _owner.CreateLocalLabel(_methodLabel + "_unbox_nullable_null");
                    string directValue = _owner.CreateLocalLabel(_methodLabel + "_unbox_nullable_direct");
                    string underlyingValue = _owner.CreateLocalLabel(_methodLabel + "_unbox_nullable_underlying");
                    string done = _owner.CreateLocalLabel(_methodLabel + "_unbox_nullable_done");

                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X28, RVRegister.X0, nullValue);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X28, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(nullableType), RVRegister.X29);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, directValue);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(underlyingType), RVRegister.X29);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, underlyingValue);
                    EmitManagedExceptionThrow(node, "InvalidCastException");

                    _owner.DefineLabel(nullValue);
                    EmitDefaultValue(node);
                    EmitJump(done);

                    _owner.DefineLabel(directValue);
                    _owner.EmitAddImmediate(RVRegister.X28, RVRegister.X28, Target.ManagedObjectHeaderSize);
                    EmitValueFromAddress(node, nullableType, node.StackKind, RVRegister.X28);
                    EmitJump(done);

                    _owner.DefineLabel(underlyingValue);
                    for (int i = 0; i < Math.Max(1, nullableType.SizeOf); i++)
                    {
                        EmitMemoryStore(
                            MachineRegister.X0,
                            RVRegister.X8,
                            checked(TypeOperationScratchOffset + i),
                            1);
                    }
                    _owner.EmitLoadImmediate(RVRegister.X31, 1);
                    EmitMemoryStore(
                        MachineRegister.X31,
                        RVRegister.X8,
                        checked(TypeOperationScratchOffset + hasValueField.Offset),
                        Math.Max(1, hasValueField.FieldType.SizeOf));
                    _owner.EmitAddImmediate(
                        RVRegister.X31,
                        RVRegister.X8,
                        checked(TypeOperationScratchOffset + valueField.Offset));
                    _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X28, Target.ManagedObjectHeaderSize);
                    EmitBlockCopy(node, RVRegister.X31, RVRegister.X30, Math.Max(1, underlyingType.SizeOf));
                    _owner.EmitAddImmediate(RVRegister.X28, RVRegister.X8, TypeOperationScratchOffset);
                    EmitValueFromAddress(node, nullableType, node.StackKind, RVRegister.X28);

                    _owner.DefineLabel(done);
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

                private void EmitStackAlloc(GenTree node)
                {
                    if (node.Int32 <= 0)
                        throw Unsupported(node, "Stack allocation element size must be positive");

                    RVRegister destination = ToIntegerRegister(RequireResultRegister(node));
                    RVRegister count = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "stack allocation count"));
                    RVRegister allocationCount = count;
                    RVRegister byteCount = RVRegister.X31;

                    if (MachineTarget.Is64Bit)
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X30, count, 0));
                        allocationCount = RVRegister.X30;
                    }

                    string nonNegative = _owner.CreateLocalLabel(_methodLabel + "_stackalloc_nonnegative");
                    _owner.Emit(RVInstruction.B(RVInstrKind.Bge, allocationCount, RVRegister.X0, nonNegative));
                    EmitStackAllocFailure();
                    _owner.DefineLabel(nonNegative);

                    if (node.Int32 == 1)
                    {
                        _owner.EmitMove(byteCount, allocationCount);
                    }
                    else if (BitOperations.IsPow2(node.Int32))
                    {
                        int shift = BitOperations.TrailingZeroCount((uint)node.Int32);
                        if (MachineTarget.Is32Bit)
                        {
                            string productFits = _owner.CreateLocalLabel(_methodLabel + "_stackalloc_product_fits");
                            _owner.Emit(RVInstruction.I(RVInstrKind.Srli, RVRegister.X29, allocationCount, 32 - shift));
                            _owner.Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X29, RVRegister.X0, productFits));
                            EmitStackAllocFailure();
                            _owner.DefineLabel(productFits);
                        }
                        _owner.Emit(RVInstruction.I(RVInstrKind.Slli, byteCount, allocationCount, shift));
                    }
                    else
                    {
                        if (!MachineTarget.HasM)
                            throw Unsupported(node, "Variable stack allocation requires the M extension for a non-power-of-two element size");
                        _owner.EmitLoadImmediate(RVRegister.X29, node.Int32);
                        _owner.Emit(RVInstruction.R(RVInstrKind.Mul, byteCount, allocationCount, RVRegister.X29));
                        if (MachineTarget.Is32Bit)
                        {
                            string productFits = _owner.CreateLocalLabel(_methodLabel + "_stackalloc_product_fits");
                            _owner.Emit(RVInstruction.R(RVInstrKind.Mulhu, RVRegister.X30, allocationCount, RVRegister.X29));
                            _owner.Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X30, RVRegister.X0, productFits));
                            EmitStackAllocFailure();
                            _owner.DefineLabel(productFits);
                        }
                    }

                    _owner.EmitMove(RVRegister.X29, byteCount);
                    _owner.EmitAddImmediate(byteCount, byteCount, Target.CallFrameAlignment - 1);
                    if (MachineTarget.Is32Bit)
                    {
                        string alignmentFits = _owner.CreateLocalLabel(_methodLabel + "_stackalloc_alignment_fits");
                        _owner.Emit(RVInstruction.B(RVInstrKind.Bgeu, byteCount, RVRegister.X29, alignmentFits));
                        EmitStackAllocFailure();
                        _owner.DefineLabel(alignmentFits);
                    }
                    _owner.EmitLoadImmediate(RVRegister.X30, -Target.CallFrameAlignment);
                    _owner.Emit(RVInstruction.R(RVInstrKind.And, byteCount, byteCount, RVRegister.X30));
                    _owner.Emit(RVInstruction.R(RVInstrKind.Sub, RVRegister.X2, RVRegister.X2, byteCount));
                    _owner.EmitMove(destination, RVRegister.X2);
                }

                private void EmitStackAllocFailure()
                {
                    if (Target.OperatingSystem == OperatingSystemKind.Linux)
                    {
                        _owner.EmitLoadImmediate(RVRegister.X10, 134);
                        _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.FailFastSymbol), link: false);
                        return;
                    }

                    _owner.Emit(new RVInstruction(RVInstrKind.Ebreak));
                    _owner.Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, 0));
                }

                private void EmitFrameOperation(GenTree node)
                {
                    switch (node.FrameOperation)
                    {
                        case FrameOperation.AllocateFrame:
                            _owner.EmitAdjustStack(-node.Immediate);
                            return;
                        case FrameOperation.FreeFrame:
                            _owner.EmitAdjustStack(node.Immediate);
                            return;
                        case FrameOperation.EstablishFramePointer:
                            _owner.EmitMove(RVRegister.X8, RVRegister.X2);
                            if (_ehMethod is not null && !_ehFrameRegistered)
                                EmitEhFramePush(_blockLabels[node.BlockId]);
                            return;
                        case FrameOperation.RestoreStackPointerFromFramePointer:
                            _owner.EmitMove(RVRegister.X2, RVRegister.X8);
                            return;
                        case FrameOperation.SaveReturnAddress:
                        case FrameOperation.SaveCalleeSavedRegister:
                            if (node.Results.Length != 1 || !node.Results[0].IsFrameSlot ||
                                node.Uses.Length != 1 || !node.Uses[0].IsRegister)
                            {
                                throw Unsupported(node, "Invalid register-save frame operation");
                            }
                            EmitStore(node.Results[0], node.Uses[0].Register, null, StackKindForRegister(node.Uses[0].Register));
                            return;
                        case FrameOperation.RestoreReturnAddress:
                        case FrameOperation.RestoreCalleeSavedRegister:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister ||
                                node.Uses.Length != 1 || !node.Uses[0].IsFrameSlot)
                            {
                                throw Unsupported(node, "Invalid register-restore frame operation");
                            }
                            EmitLoad(node.Results[0].Register, node.Uses[0], null, StackKindForRegister(node.Results[0].Register));
                            return;
                        case FrameOperation.EnterFuncletFrame:
                        case FrameOperation.LeaveFuncletFrame:
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported frame operation {node.FrameOperation}");
                    }
                }

                private void EmitMove(GenTree node)
                {
                    if (node.Results.Length != 1 || node.Uses.Length != 1)
                        throw Unsupported(node, "Move requires one source and one destination");

                    var destination = node.Results[0];
                    var source = node.Uses[0];
                    RuntimeType? type = ValueType(node);
                    GenStackKind kind = ValueStackKind(node);

                    switch (node.MoveKind)
                    {
                        case MoveKind.None:
                            return;
                        case MoveKind.Register:
                            EmitRegisterMove(destination.Register, source.Register, type, kind);
                            return;
                        case MoveKind.Load:
                            EmitLoad(destination.Register, source, type, kind);
                            return;
                        case MoveKind.Store:
                            EmitStore(destination, source.Register, type, kind);
                            return;
                        case MoveKind.MemoryToMemory:
                            EmitMemoryToMemory(destination, source, type, kind);
                            return;
                        case MoveKind.LoadAddress:
                            EmitLoadAddress(destination.Register, source);
                            return;
                        case MoveKind.StoreAddress:
                            EmitLoadAddress(MachineRegister.X31, source);
                            EmitStore(destination, MachineRegister.X31, null, GenStackKind.Ptr);
                            return;
                        default:
                            throw Unsupported(node, $"Unknown LSRA move kind {node.MoveKind}");
                    }
                }

                private void EmitFloatConstant(GenTree node, int size, byte[] bytes)
                {
                    var destination = RequireResultRegister(node);
                    if (MachineRegisters.GetClass(destination) != RegisterClass.Float)
                        throw Unsupported(node, "Floating constant result is not in a floating-point register");
                    if (size == 4 && !MachineTarget.HasF)
                        throw Unsupported(node, "Single-precision constants require the F extension");
                    if (size == 8 && !MachineTarget.HasD)
                        throw Unsupported(node, "Double-precision constants require the D extension");

                    string label = _owner.AddConstantData(bytes, size, size == 4 ? "f32" : "f64");
                    _owner.EmitMaterializeAddress(label, RVRegister.X31);
                    _owner.Emit(RVInstruction.I(
                        size == 4 ? RVInstrKind.Flw : RVInstrKind.Fld,
                        ToRegister(destination),
                        RVRegister.X31,
                        0));
                }

                private void EmitStaticData(GenTree node)
                {
                    MachineRegister result = RequireResultRegister(node);
                    if (MachineRegisters.GetClass(result) != RegisterClass.General)
                        throw Unsupported(node, "Static data address result is not in an integer register");

                    int sourceOffset = node.Int32;
                    int sourceLength;
                    try
                    {
                        sourceLength = checked((int)node.Int64);
                    }
                    catch (OverflowException)
                    {
                        throw Unsupported(node, "Static data length does not fit a native image blob");
                    }

                    ImmutableArray<byte> staticData = _method.Function.StaticDataBlob;
                    if (sourceOffset < 0 || sourceLength < 0 || sourceOffset > staticData.Length || sourceLength > staticData.Length - sourceOffset)
                        throw Unsupported(node, "Invalid static data blob range");

                    RVRegister destination = ToIntegerRegister(result);
                    if (sourceLength == 0)
                    {
                        _owner.EmitMove(destination, RVRegister.X0);
                        return;
                    }

                    var key = (sourceOffset, sourceLength);
                    if (!_staticDataLabels.TryGetValue(key, out string? label))
                    {
                        byte[] bytes = staticData.AsSpan().Slice(sourceOffset, sourceLength).ToArray();
                        label = _owner.AddConstantData(bytes, 8, $"static_data_M{_method.RuntimeMethod.MethodId}");
                        _staticDataLabels.Add(key, label);
                    }

                    _owner.EmitMaterializeAddress(label, destination);
                }

                private void EmitDefaultValue(GenTree node)
                {
                    if (node.Results.Length == 0)
                        return;

                    RuntimeType? type = node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.StackKind;

                    if (node.Results.Length > 1)
                    {
                        var abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        if (segments.Length != node.Results.Length)
                            throw Unsupported(node, "Default value fragment count does not match its storage ABI");

                        for (int i = 0; i < segments.Length; i++)
                            EmitZeroOperand(node, node.Results[i], null, StackKindForSegment(segments[i]), segments[i].Size);
                        return;
                    }

                    RegisterOperand result = node.Results[0];
                    EmitZeroOperand(node, result, type, kind, StorageSize(type, kind, result));
                }

                private void EmitZeroOperand(
                    GenTree node,
                    RegisterOperand destination,
                    RuntimeType? type,
                    GenStackKind kind,
                    int size)
                {
                    if (destination.IsRegister)
                    {
                        if (destination.RegisterClass == RegisterClass.Float)
                        {
                            RequireFloatingExtension(size);
                            if (size <= 4)
                                _owner.Emit(RVInstruction.R(RVInstrKind.FmvWX, ToRegister(destination.Register), RVRegister.X0, RVRegister.X0));
                            else if (MachineTarget.Is64Bit)
                                _owner.Emit(RVInstruction.R(RVInstrKind.FmvDX, ToRegister(destination.Register), RVRegister.X0, RVRegister.X0));
                            else
                                _owner.Emit(RVInstruction.R(RVInstrKind.FcvtDW, ToRegister(destination.Register), RVRegister.X0, RVRegister.X0));
                        }
                        else
                        {
                            _owner.EmitMove(ToIntegerRegister(destination.Register), RVRegister.X0);
                        }
                        return;
                    }

                    if (!destination.IsFrameSlot)
                        throw Unsupported(node, "Default value destination is not addressable");

                    if (IsAggregate(type, kind) || !CanMoveThroughRegister(destination.RegisterClass, size))
                    {
                        EmitZeroFrame(destination, size);
                        return;
                    }

                    if (destination.RegisterClass == RegisterClass.Float)
                    {
                        RequireFloatingExtension(size);
                        if (size <= 4)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvWX, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                        else if (MachineTarget.Is64Bit)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvDX, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                        else
                            _owner.Emit(RVInstruction.R(RVInstrKind.FcvtDW, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                        EmitStore(destination, MachineRegister.F29, type, kind);
                    }
                    else
                    {
                        EmitStore(destination, MachineRegister.X0, type, kind);
                    }
                }

                private void EmitZeroFrame(RegisterOperand destination, int size)
                {
                    if (!destination.IsFrameSlot)
                        throw new InvalidOperationException($"Zero destination is not a finalized frame slot: {destination}.");
                    if (size < 0)
                        throw new InvalidOperationException("Zero initialization size is negative.");

                    RVRegister baseRegister = FrameBase(destination);
                    int baseOffset = EffectiveFrameOffset(destination);
                    for (int offset = 0; offset < size; offset++)
                        EmitMemoryStore(MachineRegister.X0, baseRegister, checked(baseOffset + offset), 1);
                }

                private void EmitLocalLike(GenTree node)
                {
                    bool isLoad = node.TreeKind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp;
                    bool isStore = node.TreeKind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp;
                    RuntimeType? type = node.LocalDescriptor?.Type ?? node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.LocalDescriptor?.StackKind ?? node.StackKind;

                    if (node.Results.Length > 1 || node.Uses.Length > 1)
                    {
                        if (isLoad && node.Uses.Length == 0)
                        {
                            EmitLocalLikeMultiLoad(node);
                            return;
                        }

                        if (isStore && node.Results.Length == 0)
                        {
                            EmitLocalLikeMultiStore(node);
                            return;
                        }

                        if (node.Results.Length == node.Uses.Length)
                        {
                            RuntimeType? valueType = LocalLikeLoadResultType(node) ?? LocalLikeStoreSourceType(node) ?? type;
                            GenStackKind valueKind = !node.RegisterResults.IsDefaultOrEmpty
                                ? LocalLikeLoadResultKind(node, valueType)
                                : LocalLikeStoreSourceKind(node, valueType);
                            var abi = MachineAbi.ClassifyStorageValue(valueType, valueKind, Target);
                            var segments = MachineAbi.GetRegisterSegments(abi, Target);
                            if (segments.Length != node.Results.Length)
                                throw Unsupported(node, "Multi-register local/argument/temp fragment count does not match its storage ABI");

                            for (int i = 0; i < segments.Length; i++)
                                EmitMoveBetween(node, node.Results[i], node.Uses[i], null, StackKindForSegment(segments[i]));
                            return;
                        }

                        throw Unsupported(node, "Multi-register local/argument/temp shape has mismatched source and destination fragment counts");
                    }

                    if (isLoad && node.Uses.Length == 0 && node.Results.Length == 1)
                    {
                        var home = FrameSlotForLocalLike(node, type, kind, node.Results[0].RegisterClass);
                        if (node.Results[0].IsRegister)
                            EmitLoad(node.Results[0].Register, home, type, kind);
                        else
                            EmitMemoryToMemory(node.Results[0], home, type, kind);
                        return;
                    }

                    if (isStore && node.Results.Length == 0 && node.Uses.Length == 1)
                    {
                        var home = FrameSlotForLocalLike(node, type, kind, node.Uses[0].RegisterClass);
                        if (node.Uses[0].IsRegister)
                            EmitStore(home, node.Uses[0].Register, type, kind);
                        else
                            EmitMemoryToMemory(home, node.Uses[0], type, kind);
                        return;
                    }

                    if (node.Results.Length == 1 && node.Uses.Length == 1)
                    {
                        EmitMoveBetween(node, node.Results[0], node.Uses[0], type, kind);
                        return;
                    }

                    if (node.Results.Length == 0 && node.Uses.Length == 0)
                        return;

                    throw Unsupported(node, "Unsupported local/argument/temp operand shape");
                }

                private void EmitLocalLikeMultiLoad(GenTree node)
                {
                    RuntimeType? valueType = LocalLikeLoadResultType(node);
                    GenStackKind valueKind = LocalLikeLoadResultKind(node, valueType);
                    RuntimeType? slotType = node.LocalDescriptor?.Type ?? node.RuntimeType ?? node.Type ?? valueType;
                    GenStackKind slotKind = node.LocalDescriptor?.StackKind ?? node.StackKind;
                    RegisterOperand slot = FrameSlotForLocalLike(node, slotType, slotKind, RegisterClass.General);
                    var abi = MachineAbi.ClassifyStorageValue(valueType, valueKind, Target);
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);
                    if (segments.Length != node.Results.Length)
                        throw Unsupported(node, "Multi-register local/argument/temp load fragment count does not match result ABI");

                    for (int i = 0; i < segments.Length; i++)
                    {
                        AbiRegisterSegment segment = segments[i];
                        RegisterOperand source = FrameSlotFragment(slot, segment);
                        RegisterOperand destination = node.Results[i];
                        if (destination.IsRegister)
                        {
                            EmitAggregateSegmentLoad(destination.Register, FrameBase(source), EffectiveFrameOffset(source), segment);
                            continue;
                        }
                        if (destination.IsFrameSlot)
                        {
                            EmitMemoryToMemory(destination, source, null, StackKindForSegment(segment));
                            continue;
                        }
                        throw Unsupported(node, "Multi-register local/argument/temp load destination is not addressable");
                    }
                }

                private void EmitLocalLikeMultiStore(GenTree node)
                {
                    RuntimeType? valueType = LocalLikeStoreSourceType(node);
                    GenStackKind valueKind = LocalLikeStoreSourceKind(node, valueType);
                    RuntimeType? slotType = node.LocalDescriptor?.Type ?? node.RuntimeType ?? node.Type ?? valueType;
                    GenStackKind slotKind = node.LocalDescriptor?.StackKind ?? node.StackKind;
                    RegisterOperand slot = FrameSlotForLocalLike(node, slotType, slotKind, RegisterClass.General);
                    var abi = MachineAbi.ClassifyStorageValue(valueType, valueKind, Target);
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);
                    if (segments.Length != node.Uses.Length)
                        throw Unsupported(node, "Multi-register local/argument/temp store fragment count does not match source ABI");

                    for (int i = 0; i < segments.Length; i++)
                    {
                        AbiRegisterSegment segment = segments[i];
                        RegisterOperand destination = FrameSlotFragment(slot, segment);
                        RegisterOperand source = node.Uses[i];
                        if (source.IsRegister)
                        {
                            EmitAggregateSegmentStore(source.Register, FrameBase(destination), EffectiveFrameOffset(destination), segment);
                            continue;
                        }
                        if (source.IsFrameSlot)
                        {
                            EmitMemoryToMemory(destination, source, null, StackKindForSegment(segment));
                            continue;
                        }
                        throw Unsupported(node, "Multi-register local/argument/temp store source is not addressable");
                    }
                }

                private void EmitAddressTree(GenTree node)
                {
                    var destination = RequireResultRegister(node);
                    if (MachineRegisters.GetClass(destination) != RegisterClass.General)
                        throw Unsupported(node, "Address result is not in an integer register");

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

                    throw Unsupported(node, "Address tree has an invalid operand shape");
                }

                private void EmitFunctionPointer(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "function pointer node has no runtime method");
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveMethodLabel(method),
                        ToIntegerRegister(RequireResultRegister(node)));
                }

                private void EmitUnary(GenTree node)
                {
                    var destination = RequireResultRegister(node);
                    var source = RequireUseRegisterForOperand(node, 0, "unary operand");
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);

                    if (IsFloating(type, kind))
                    {
                        int size = StorageSize(type, kind);
                        RequireFloatingExtension(size);
                        if (node.SourceOp != BytecodeOp.Neg)
                            throw Unsupported(node, $"Unsupported floating unary opcode {node.SourceOp}");
                        _owner.Emit(RVInstruction.R(
                            size <= 4 ? RVInstrKind.FsgnjnS : RVInstrKind.FsgnjnD,
                            ToRegister(destination),
                            ToRegister(source),
                            ToRegister(source)));
                        return;
                    }

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Neg:
                            _owner.Emit(RVInstruction.R(
                                IsI4(type, kind) && MachineTarget.Is64Bit ? RVInstrKind.Subw : RVInstrKind.Sub,
                                ToIntegerRegister(destination),
                                RVRegister.X0,
                                ToIntegerRegister(source)));
                            return;
                        case BytecodeOp.Not:
                            _owner.Emit(RVInstruction.I(
                                RVInstrKind.Xori,
                                ToIntegerRegister(destination),
                                ToIntegerRegister(source),
                                -1));
                            if (IsI4(type, kind) && MachineTarget.Is64Bit)
                                _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, ToIntegerRegister(destination), ToIntegerRegister(destination), 0));
                            return;
                        case BytecodeOp.FnPtrToPtr:
                        case BytecodeOp.PtrToFnPtr:
                            _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported unary opcode {node.SourceOp}");
                    }
                }

                private void EmitBinary(GenTree node)
                {
                    var destination = RequireResultRegister(node);
                    var left = RequireUseRegisterForOperand(node, 0, "binary left operand");
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);

                    if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                    {
                        EmitBinaryImmediate(node, destination, left, immediate, type, kind);
                        return;
                    }

                    var right = RequireUseRegisterForOperand(node, 1, "binary right operand");
                    if (IsFloating(type, kind))
                    {
                        EmitFloatingBinary(node, destination, left, right, StorageSize(type, kind));
                        return;
                    }

                    EmitIntegerBinary(node, destination, left, right, type, kind);
                }

                private void EmitFloatingBinary(GenTree node, MachineRegister destination, MachineRegister left, MachineRegister right, int size)
                {
                    RequireFloatingExtension(size);
                    bool single = size <= 4;
                    RVInstrKind opcode = node.SourceOp switch
                    {
                        BytecodeOp.Add => single ? RVInstrKind.FaddS : RVInstrKind.FaddD,
                        BytecodeOp.Sub => single ? RVInstrKind.FsubS : RVInstrKind.FsubD,
                        BytecodeOp.Mul => single ? RVInstrKind.FmulS : RVInstrKind.FmulD,
                        BytecodeOp.Div => single ? RVInstrKind.FdivS : RVInstrKind.FdivD,
                        BytecodeOp.Ceq => single ? RVInstrKind.FeqS : RVInstrKind.FeqD,
                        BytecodeOp.Clt => single ? RVInstrKind.FltS : RVInstrKind.FltD,
                        BytecodeOp.Cgt => single ? RVInstrKind.FltS : RVInstrKind.FltD,
                        _ => throw Unsupported(node, $"Unsupported floating binary opcode {node.SourceOp}"),
                    };

                    bool compare = node.SourceOp is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Cgt;
                    MachineRegister a = node.SourceOp == BytecodeOp.Cgt ? right : left;
                    MachineRegister b = node.SourceOp == BytecodeOp.Cgt ? left : right;
                    _owner.Emit(RVInstruction.R(
                        opcode,
                        compare ? ToIntegerRegister(destination) : ToRegister(destination),
                        ToRegister(a),
                        ToRegister(b)));
                }

                private void EmitIntegerBinary(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    MachineRegister right,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    bool i4 = IsI4(type, kind);
                    bool unsigned = IsUnsigned(type) || node.SourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un or BytecodeOp.Shr_Un or BytecodeOp.Clt_Un or BytecodeOp.Cgt_Un;
                    RVRegister rd = ToIntegerRegister(destination);
                    RVRegister a = ToIntegerRegister(left);
                    RVRegister b = ToIntegerRegister(right);

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add:
                            EmitIntegerR(i4 ? RVInstrKind.Addw : RVInstrKind.Add, rd, a, b, i4);
                            return;
                        case BytecodeOp.Sub:
                            EmitIntegerR(i4 ? RVInstrKind.Subw : RVInstrKind.Sub, rd, a, b, i4);
                            return;
                        case BytecodeOp.Mul:
                            RequireM(node);
                            EmitIntegerR(i4 ? RVInstrKind.Mulw : RVInstrKind.Mul, rd, a, b, i4);
                            return;
                        case BytecodeOp.Div:
                        case BytecodeOp.Div_Un:
                        case BytecodeOp.Rem:
                        case BytecodeOp.Rem_Un:
                            EmitIntegerDivRem(node, rd, a, b, i4, unsigned);
                            return;
                        case BytecodeOp.And:
                            _owner.Emit(RVInstruction.R(RVInstrKind.And, rd, a, b));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Or:
                            _owner.Emit(RVInstruction.R(RVInstrKind.Or, rd, a, b));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Xor:
                            _owner.Emit(RVInstruction.R(RVInstrKind.Xor, rd, a, b));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Shl:
                            EmitIntegerR(i4 ? RVInstrKind.Sllw : RVInstrKind.Sll, rd, a, b, i4);
                            return;
                        case BytecodeOp.Shr:
                        case BytecodeOp.Shr_Un:
                            EmitIntegerR(i4 ? (unsigned ? RVInstrKind.Srlw : RVInstrKind.Sraw) : (unsigned ? RVInstrKind.Srl : RVInstrKind.Sra), rd, a, b, i4);
                            return;
                        case BytecodeOp.Ceq:
                            _owner.Emit(RVInstruction.R(
                                i4 && MachineTarget.Is64Bit ? RVInstrKind.Subw : RVInstrKind.Xor,
                                RVRegister.X31,
                                a,
                                b));
                            _owner.Emit(RVInstruction.I(RVInstrKind.Sltiu, rd, RVRegister.X31, 1));
                            return;
                        case BytecodeOp.Clt:
                        case BytecodeOp.Clt_Un:
                            EmitLessThan(rd, a, b, i4, unsigned);
                            return;
                        case BytecodeOp.Cgt:
                        case BytecodeOp.Cgt_Un:
                            EmitLessThan(rd, b, a, i4, unsigned);
                            return;
                        case BytecodeOp.Add_Ovf:
                        case BytecodeOp.Add_Ovf_Un:
                        case BytecodeOp.Sub_Ovf:
                        case BytecodeOp.Sub_Ovf_Un:
                        case BytecodeOp.Mul_Ovf:
                        case BytecodeOp.Mul_Ovf_Un:
                            EmitCheckedIntegerBinary(node, rd, a, b, i4, node.SourceOp is BytecodeOp.Add_Ovf_Un or BytecodeOp.Sub_Ovf_Un or BytecodeOp.Mul_Ovf_Un);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported integer binary opcode {{node.SourceOp}}");
                    }
                }


                private void EmitIntegerDivRem(
                    GenTree node,
                    RVRegister destination,
                    RVRegister left,
                    RVRegister right,
                    bool i4,
                    bool unsigned)
                {
                    RequireM(node);
                    if (!i4 && !MachineTarget.Is64Bit)
                        throw Unsupported(node, "64-bit division and remainder on RV32 require a soft-long lowering pass");

                    _owner.EmitMove(RVRegister.X28, left);
                    _owner.EmitMove(RVRegister.X29, right);
                    if (i4 && MachineTarget.Is64Bit)
                    {
                        if (unsigned)
                        {
                            EmitZeroExtend32(RVRegister.X28, RVRegister.X28);
                            EmitZeroExtend32(RVRegister.X29, RVRegister.X29);
                        }
                        else
                        {
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X28, RVRegister.X28, 0));
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X29, RVRegister.X29, 0));
                        }
                    }

                    if ((node.Flags & GenTreeFlags.DivModNoByZero) == 0)
                    {
                        string nonZero = _owner.CreateLocalLabel($"{_methodLabel}_div_nonzero_{node.LinearId}");
                        EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X29, RVRegister.X0, nonZero);
                        EmitManagedExceptionThrow(node, "DivideByZeroException");
                        _owner.DefineLabel(nonZero);
                    }

                    if (!unsigned && (node.Flags & GenTreeFlags.DivModNoOverflow) == 0)
                    {
                        string perform = _owner.CreateLocalLabel($"{_methodLabel}_div_perform_{node.LinearId}");
                        _owner.EmitLoadImmediate(RVRegister.X30, -1);
                        EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X29, RVRegister.X30, perform);
                        _owner.EmitLoadImmediate(RVRegister.X30, i4 ? int.MinValue : long.MinValue);
                        EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X28, RVRegister.X30, perform);
                        EmitManagedExceptionThrow(node, "OverflowException");
                        _owner.DefineLabel(perform);
                    }

                    bool divide = node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un;
                    RVInstrKind opcode = divide
                        ? i4
                            ? unsigned ? RVInstrKind.Divuw : RVInstrKind.Divw
                            : unsigned ? RVInstrKind.Divu : RVInstrKind.Div
                        : i4
                            ? unsigned ? RVInstrKind.Remuw : RVInstrKind.Remw
                            : unsigned ? RVInstrKind.Remu : RVInstrKind.Rem;
                    EmitIntegerR(opcode, destination, RVRegister.X28, RVRegister.X29, i4);
                }

                private void EmitCheckedIntegerBinary(
                    GenTree node,
                    RVRegister destination,
                    RVRegister left,
                    RVRegister right,
                    bool i4,
                    bool unsigned)
                {
                    RequireMForCheckedMultiply(node);
                    if (!i4 && !MachineTarget.Is64Bit)
                        throw Unsupported(node, "64-bit checked arithmetic on RV32 requires a soft-long lowering pass");

                    _owner.EmitMove(RVRegister.X28, left);
                    _owner.EmitMove(RVRegister.X29, right);
                    if (i4 && MachineTarget.Is64Bit)
                    {
                        if (unsigned)
                        {
                            EmitZeroExtend32(RVRegister.X28, RVRegister.X28);
                            EmitZeroExtend32(RVRegister.X29, RVRegister.X29);
                        }
                        else
                        {
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X28, RVRegister.X28, 0));
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X29, RVRegister.X29, 0));
                        }
                    }

                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_overflow_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_checked_done_{node.LinearId}");

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add_Ovf:
                        case BytecodeOp.Add_Ovf_Un:
                            EmitIntegerR(i4 ? RVInstrKind.Addw : RVInstrKind.Add, destination, RVRegister.X28, RVRegister.X29, i4);
                            if (unsigned)
                            {
                                if (i4 && MachineTarget.Is64Bit)
                                    EmitZeroExtend32(RVRegister.X30, destination);
                                else
                                    _owner.EmitMove(RVRegister.X30, destination);
                                EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X30, RVRegister.X28, overflow);
                            }
                            else
                            {
                                _owner.Emit(RVInstruction.R(RVInstrKind.Xor, RVRegister.X30, RVRegister.X28, destination));
                                _owner.Emit(RVInstruction.R(RVInstrKind.Xor, RVRegister.X31, RVRegister.X29, destination));
                                _owner.Emit(RVInstruction.R(RVInstrKind.And, RVRegister.X30, RVRegister.X30, RVRegister.X31));
                                EmitLongConditionalBranch(RVInstrKind.Blt, RVRegister.X30, RVRegister.X0, overflow);
                            }
                            break;

                        case BytecodeOp.Sub_Ovf:
                        case BytecodeOp.Sub_Ovf_Un:
                            EmitIntegerR(i4 ? RVInstrKind.Subw : RVInstrKind.Sub, destination, RVRegister.X28, RVRegister.X29, i4);
                            if (unsigned)
                            {
                                EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X28, RVRegister.X29, overflow);
                            }
                            else
                            {
                                _owner.Emit(RVInstruction.R(RVInstrKind.Xor, RVRegister.X30, RVRegister.X28, RVRegister.X29));
                                _owner.Emit(RVInstruction.R(RVInstrKind.Xor, RVRegister.X31, RVRegister.X28, destination));
                                _owner.Emit(RVInstruction.R(RVInstrKind.And, RVRegister.X30, RVRegister.X30, RVRegister.X31));
                                EmitLongConditionalBranch(RVInstrKind.Blt, RVRegister.X30, RVRegister.X0, overflow);
                            }
                            break;

                        case BytecodeOp.Mul_Ovf:
                        case BytecodeOp.Mul_Ovf_Un:
                            if (unsigned)
                            {
                                if (i4 && MachineTarget.Is64Bit)
                                {
                                    _owner.Emit(RVInstruction.R(RVInstrKind.Mul, RVRegister.X30, RVRegister.X28, RVRegister.X29));
                                    _owner.Emit(RVInstruction.I(RVInstrKind.Srli, RVRegister.X31, RVRegister.X30, 32));
                                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X0, overflow);
                                    _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, destination, RVRegister.X30, 0));
                                }
                                else
                                {
                                    _owner.Emit(RVInstruction.R(RVInstrKind.Mul, RVRegister.X30, RVRegister.X28, RVRegister.X29));
                                    _owner.Emit(RVInstruction.R(RVInstrKind.Mulhu, RVRegister.X31, RVRegister.X28, RVRegister.X29));
                                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X0, overflow);
                                    _owner.EmitMove(destination, RVRegister.X30);
                                }
                            }
                            else
                            {
                                _owner.Emit(RVInstruction.R(RVInstrKind.Mul, RVRegister.X30, RVRegister.X28, RVRegister.X29));
                                if (i4 && MachineTarget.Is64Bit)
                                {
                                    _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, destination, RVRegister.X30, 0));
                                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X30, destination, overflow);
                                }
                                else
                                {
                                    _owner.Emit(RVInstruction.R(RVInstrKind.Mulh, RVRegister.X31, RVRegister.X28, RVRegister.X29));
                                    _owner.Emit(RVInstruction.I(RVInstrKind.Srai, RVRegister.X28, RVRegister.X30, MachineTarget.Is64Bit ? 63 : 31));
                                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X28, overflow);
                                    _owner.EmitMove(destination, RVRegister.X30);
                                }
                            }
                            break;

                        default:
                            throw Unsupported(node, $"Unsupported checked arithmetic opcode {node.SourceOp}");
                    }

                    EmitJump(done);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(done);
                }

                private void RequireMForCheckedMultiply(GenTree node)
                {
                    if (node.SourceOp is BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un)
                        RequireM(node);
                }

                private void EmitBinaryImmediate(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    long immediate,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    if (immediate < int.MinValue || immediate > int.MaxValue)
                    {
                        _owner.EmitLoadImmediate(RVRegister.X30, immediate);
                        EmitIntegerBinary(node, destination, left, MachineRegister.X30, type, kind);
                        return;
                    }

                    bool i4 = IsI4(type, kind);
                    int value = (int)immediate;
                    RVRegister rd = ToIntegerRegister(destination);
                    RVRegister rs = ToIntegerRegister(left);

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add when FitsSignedImmediate(value, 12):
                            _owner.Emit(RVInstruction.I(i4 && MachineTarget.Is64Bit ? RVInstrKind.Addiw : RVInstrKind.Addi, rd, rs, value));
                            return;
                        case BytecodeOp.Sub when value != int.MinValue && FitsSignedImmediate(-value, 12):
                            _owner.Emit(RVInstruction.I(i4 && MachineTarget.Is64Bit ? RVInstrKind.Addiw : RVInstrKind.Addi, rd, rs, -value));
                            return;
                        case BytecodeOp.And when FitsSignedImmediate(value, 12):
                            _owner.Emit(RVInstruction.I(RVInstrKind.Andi, rd, rs, value));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Or when FitsSignedImmediate(value, 12):
                            _owner.Emit(RVInstruction.I(RVInstrKind.Ori, rd, rs, value));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Xor when FitsSignedImmediate(value, 12):
                            _owner.Emit(RVInstruction.I(RVInstrKind.Xori, rd, rs, value));
                            CanonicalizeI4(rd, i4);
                            return;
                        case BytecodeOp.Shl:
                            _owner.Emit(RVInstruction.I(i4 && MachineTarget.Is64Bit ? RVInstrKind.Slliw : RVInstrKind.Slli, rd, rs, value & (i4 ? 31 : MachineTarget.XLen - 1)));
                            return;
                        case BytecodeOp.Shr:
                            _owner.Emit(RVInstruction.I(i4 && MachineTarget.Is64Bit ? RVInstrKind.Sraiw : RVInstrKind.Srai, rd, rs, value & (i4 ? 31 : MachineTarget.XLen - 1)));
                            return;
                        case BytecodeOp.Shr_Un:
                            _owner.Emit(RVInstruction.I(i4 && MachineTarget.Is64Bit ? RVInstrKind.Srliw : RVInstrKind.Srli, rd, rs, value & (i4 ? 31 : MachineTarget.XLen - 1)));
                            return;
                    }

                    _owner.EmitLoadImmediate(RVRegister.X30, immediate);
                    EmitIntegerBinary(node, destination, left, MachineRegister.X30, type, kind);
                }

                private void EmitConversion(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister source = RequireUseRegisterForOperand(node, 0, "conversion operand");
                    RuntimeType? sourceType = OperandType(node, 0);
                    GenStackKind sourceKind = OperandStackKind(node, 0);
                    bool sourceFloat = IsFloating(sourceType, sourceKind);
                    bool sourceUnsigned = IsUnsigned(sourceType) || (node.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;
                    bool sourceI8 = IsI8(sourceType, sourceKind);
                    bool checkedConversion = (node.ConvFlags & NumericConvFlags.Checked) != 0;
                    if (checkedConversion && node.ConvKind is not (NumericConvKind.Bool or NumericConvKind.R4 or NumericConvKind.R8))
                    {
                        if (sourceFloat)
                            throw Unsupported(node, "Checked floating-point to integer conversions are not implemented");
                        EmitCheckedIntegerConversionGuard(node, source, sourceType, sourceKind, sourceUnsigned);
                    }

                    switch (node.ConvKind)
                    {
                        case NumericConvKind.Bool:
                            EmitBoolConversion(destination, source, sourceType, sourceKind);
                            return;
                        case NumericConvKind.I1:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: false, width64: false);
                            EmitIntegerNarrow(destination, sourceFloat ? destination : source, 8, signed: true);
                            return;
                        case NumericConvKind.U1:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: true, width64: false);
                            EmitIntegerNarrow(destination, sourceFloat ? destination : source, 8, signed: false);
                            return;
                        case NumericConvKind.I2:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: false, width64: false);
                            EmitIntegerNarrow(destination, sourceFloat ? destination : source, 16, signed: true);
                            return;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: true, width64: false);
                            EmitIntegerNarrow(destination, sourceFloat ? destination : source, 16, signed: false);
                            return;
                        case NumericConvKind.I4:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: false, width64: false);
                            else if (MachineTarget.Is64Bit)
                                _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, ToIntegerRegister(destination), ToIntegerRegister(source), 0));
                            else
                                _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                            return;
                        case NumericConvKind.U4:
                            if (sourceFloat)
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, unsigned: true, width64: false);
                            else
                                EmitZeroExtend32(ToIntegerRegister(destination), ToIntegerRegister(source));
                            return;
                        case NumericConvKind.I8:
                        case NumericConvKind.U8:
                            if (!MachineTarget.Is64Bit)
                                throw Unsupported(node, "64-bit conversions on RV32 require a soft-long lowering pass");
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(destination, source, sourceType, sourceKind, node.ConvKind == NumericConvKind.U8, width64: true);
                                return;
                            }
                            if (!sourceI8 && sourceUnsigned)
                                EmitZeroExtend32(ToIntegerRegister(destination), ToIntegerRegister(source));
                            else
                                _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                            return;
                        case NumericConvKind.NativeInt:
                        case NumericConvKind.NativeUInt:
                            if (sourceFloat)
                            {
                                EmitFloatToInteger(
                                    destination,
                                    source,
                                    sourceType,
                                    sourceKind,
                                    node.ConvKind == NumericConvKind.NativeUInt,
                                    width64: MachineTarget.Is64Bit);
                                return;
                            }
                            if (!MachineTarget.Is64Bit)
                            {
                                _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                                return;
                            }
                            if (!sourceI8 && sourceUnsigned)
                                EmitZeroExtend32(ToIntegerRegister(destination), ToIntegerRegister(source));
                            else
                                _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                            return;
                        case NumericConvKind.R4:
                            EmitToFloat(destination, source, sourceType, sourceKind, sourceUnsigned, single: true);
                            return;
                        case NumericConvKind.R8:
                            EmitToFloat(destination, source, sourceType, sourceKind, sourceUnsigned, single: false);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported numeric conversion {node.ConvKind}");
                    }
                }

                private void EmitCheckedIntegerConversionGuard(
                    GenTree node,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    bool sourceUnsigned)
                {
                    int targetBits;
                    bool targetUnsigned;
                    switch (node.ConvKind)
                    {
                        case NumericConvKind.I1:
                            targetBits = 8;
                            targetUnsigned = false;
                            break;
                        case NumericConvKind.U1:
                            targetBits = 8;
                            targetUnsigned = true;
                            break;
                        case NumericConvKind.I2:
                            targetBits = 16;
                            targetUnsigned = false;
                            break;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char:
                            targetBits = 16;
                            targetUnsigned = true;
                            break;
                        case NumericConvKind.I4:
                            targetBits = 32;
                            targetUnsigned = false;
                            break;
                        case NumericConvKind.U4:
                            targetBits = 32;
                            targetUnsigned = true;
                            break;
                        case NumericConvKind.I8:
                            targetBits = 64;
                            targetUnsigned = false;
                            break;
                        case NumericConvKind.U8:
                            targetBits = 64;
                            targetUnsigned = true;
                            break;
                        case NumericConvKind.NativeInt:
                            targetBits = MachineTarget.XLen;
                            targetUnsigned = false;
                            break;
                        case NumericConvKind.NativeUInt:
                            targetBits = MachineTarget.XLen;
                            targetUnsigned = true;
                            break;
                        default:
                            return;
                    }

                    if (targetBits == 64 && !MachineTarget.Is64Bit)
                        throw Unsupported(node, "64-bit checked conversions on RV32 require a soft-long lowering pass");

                    bool sourceWidth64 = IsI8(sourceType, sourceKind) ||
                        ((sourceKind is GenStackKind.NativeInt or GenStackKind.NativeUInt) && MachineTarget.Is64Bit);
                    _owner.EmitMove(RVRegister.X28, ToIntegerRegister(source));
                    if (MachineTarget.Is64Bit && !sourceWidth64)
                    {
                        if (sourceUnsigned)
                            EmitZeroExtend32(RVRegister.X28, RVRegister.X28);
                        else
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X28, RVRegister.X28, 0));
                    }

                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_conv_overflow_{node.LinearId}");
                    string valid = _owner.CreateLocalLabel($"{_methodLabel}_conv_valid_{node.LinearId}");
                    bool emittedCheck = false;

                    if (sourceUnsigned)
                    {
                        if (!targetUnsigned)
                        {
                            long maximum = targetBits == 64 ? long.MaxValue : (1L << (targetBits - 1)) - 1L;
                            _owner.EmitLoadImmediate(RVRegister.X29, maximum);
                            EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X29, RVRegister.X28, overflow);
                            emittedCheck = true;
                        }
                        else if (targetBits < MachineTarget.XLen || (MachineTarget.Is64Bit && !sourceWidth64 && targetBits < 32))
                        {
                            long maximum = targetBits == 32 ? uint.MaxValue : (1L << targetBits) - 1L;
                            _owner.EmitLoadImmediate(RVRegister.X29, maximum);
                            EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X29, RVRegister.X28, overflow);
                            emittedCheck = true;
                        }
                    }
                    else
                    {
                        if (targetUnsigned)
                        {
                            EmitLongConditionalBranch(RVInstrKind.Blt, RVRegister.X28, RVRegister.X0, overflow);
                            emittedCheck = true;
                            if (targetBits < MachineTarget.XLen)
                            {
                                long maximum = targetBits == 32 ? uint.MaxValue : (1L << targetBits) - 1L;
                                _owner.EmitLoadImmediate(RVRegister.X29, maximum);
                                EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X29, RVRegister.X28, overflow);
                            }
                        }
                        else if (targetBits < MachineTarget.XLen)
                        {
                            long minimum = -(1L << (targetBits - 1));
                            long maximum = (1L << (targetBits - 1)) - 1L;
                            _owner.EmitLoadImmediate(RVRegister.X29, minimum);
                            EmitLongConditionalBranch(RVInstrKind.Blt, RVRegister.X28, RVRegister.X29, overflow);
                            _owner.EmitLoadImmediate(RVRegister.X29, maximum);
                            EmitLongConditionalBranch(RVInstrKind.Blt, RVRegister.X29, RVRegister.X28, overflow);
                            emittedCheck = true;
                        }
                    }

                    if (!emittedCheck)
                        return;
                    EmitJump(valid);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(valid);
                }

                private void EmitBoolConversion(MachineRegister destination, MachineRegister source, RuntimeType? sourceType, GenStackKind sourceKind)
                {
                    if (!IsFloating(sourceType, sourceKind))
                    {
                        _owner.Emit(RVInstruction.R(
                            RVInstrKind.Sltu,
                            ToIntegerRegister(destination),
                            RVRegister.X0,
                            ToIntegerRegister(source)));
                        return;
                    }

                    int size = StorageSize(sourceType, sourceKind);
                    RequireFloatingExtension(size);
                    if (size <= 4)
                    {
                        _owner.Emit(RVInstruction.R(RVInstrKind.FmvWX, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                        _owner.Emit(RVInstruction.R(RVInstrKind.FeqS, ToIntegerRegister(destination), ToRegister(source), RVRegister.F29));
                    }
                    else
                    {
                        _owner.Emit(RVInstruction.R(RVInstrKind.FcvtDW, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                        _owner.Emit(RVInstruction.R(RVInstrKind.FeqD, ToIntegerRegister(destination), ToRegister(source), RVRegister.F29));
                    }
                    _owner.Emit(RVInstruction.I(RVInstrKind.Xori, ToIntegerRegister(destination), ToIntegerRegister(destination), 1));
                }

                private void EmitIntegerNarrow(MachineRegister destination, MachineRegister source, int bits, bool signed)
                {
                    int shift = MachineTarget.XLen - bits;
                    RVRegister rd = ToIntegerRegister(destination);
                    RVRegister rs = ToIntegerRegister(source);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, rd, rs, shift));
                    _owner.Emit(RVInstruction.I(signed ? RVInstrKind.Srai : RVInstrKind.Srli, rd, rd, shift));
                }

                private void EmitFloatToInteger(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    bool unsigned,
                    bool width64)
                {
                    int sourceSize = StorageSize(sourceType, sourceKind);
                    RequireFloatingExtension(sourceSize);
                    bool single = sourceSize <= 4;
                    RVInstrKind opcode = (single, width64, unsigned) switch
                    {
                        (true, false, false) => RVInstrKind.FcvtWS,
                        (true, false, true) => RVInstrKind.FcvtWuS,
                        (true, true, false) => RVInstrKind.FcvtLS,
                        (true, true, true) => RVInstrKind.FcvtLuS,
                        (false, false, false) => RVInstrKind.FcvtWD,
                        (false, false, true) => RVInstrKind.FcvtWuD,
                        (false, true, false) => RVInstrKind.FcvtLD,
                        _ => RVInstrKind.FcvtLuD,
                    };
                    _owner.Emit(new RVInstruction(
                        opcode,
                        ToIntegerRegister(destination),
                        ToRegister(source),
                        immediate: 1));
                }

                private void EmitToFloat(
                    MachineRegister destination,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind,
                    bool unsigned,
                    bool single)
                {
                    RequireFloatingExtension(single ? 4 : 8);
                    if (IsFloating(sourceType, sourceKind))
                    {
                        int sourceSize = StorageSize(sourceType, sourceKind);
                        RequireFloatingExtension(sourceSize);
                        bool sourceSingle = sourceSize <= 4;
                        if (sourceSingle == single)
                            EmitRegisterMove(destination, source, null, single ? GenStackKind.R4 : GenStackKind.R8);
                        else
                            _owner.Emit(RVInstruction.R(
                                single ? RVInstrKind.FcvtSD : RVInstrKind.FcvtDS,
                                ToRegister(destination),
                                ToRegister(source),
                                RVRegister.X0));
                        return;
                    }

                    bool source64 = IsI8(sourceType, sourceKind) || ((sourceKind is GenStackKind.NativeInt or GenStackKind.NativeUInt) && MachineTarget.Is64Bit);
                    if (source64 && !MachineTarget.Is64Bit)
                        throw new NotImplementedException("64-bit integer to floating-point conversion on RV32 is not implemented.");

                    RVInstrKind opcode = (single, source64, unsigned) switch
                    {
                        (true, false, false) => RVInstrKind.FcvtSW,
                        (true, false, true) => RVInstrKind.FcvtSWu,
                        (true, true, false) => RVInstrKind.FcvtSL,
                        (true, true, true) => RVInstrKind.FcvtSLu,
                        (false, false, false) => RVInstrKind.FcvtDW,
                        (false, false, true) => RVInstrKind.FcvtDWu,
                        (false, true, false) => RVInstrKind.FcvtDL,
                        _ => RVInstrKind.FcvtDLu,
                    };
                    _owner.Emit(RVInstruction.R(opcode, ToRegister(destination), ToIntegerRegister(source), RVRegister.X0));
                }

                private void EmitConditionalBranch(GenTree node)
                {
                    string target = LabelForTarget(node);
                    bool branchWhenTrue = node.TreeKind == GenTreeKind.BranchTrue;

                    if (node.Uses.Length == 2 && IsCompareOp(node.SourceOp))
                    {
                        EmitCompareBranch(node, target, branchWhenTrue);
                        return;
                    }

                    if (node.Uses.Length != 1)
                        throw Unsupported(node, "Conditional branch requires one condition operand");

                    RVRegister condition = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "branch condition"));
                    EmitLongConditionalBranch(
                        branchWhenTrue ? RVInstrKind.Bne : RVInstrKind.Beq,
                        condition,
                        RVRegister.X0,
                        target);
                }

                private void EmitCompareBranch(GenTree node, string target, bool branchWhenTrue)
                {
                    MachineRegister left = RequireUseRegisterForOperand(node, 0, "compare left operand");
                    MachineRegister right = RequireUseRegisterForOperand(node, 1, "compare right operand");
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);

                    if (IsFloating(type, kind))
                    {
                        int size = StorageSize(type, kind);
                        RequireFloatingExtension(size);
                        RVInstrKind compare;
                        MachineRegister a = left;
                        MachineRegister b = right;
                        switch (node.SourceOp)
                        {
                            case BytecodeOp.Ceq:
                                compare = size <= 4 ? RVInstrKind.FeqS : RVInstrKind.FeqD;
                                break;
                            case BytecodeOp.Clt:
                                compare = size <= 4 ? RVInstrKind.FltS : RVInstrKind.FltD;
                                break;
                            case BytecodeOp.Cgt:
                                compare = size <= 4 ? RVInstrKind.FltS : RVInstrKind.FltD;
                                a = right;
                                b = left;
                                break;
                            default:
                                throw Unsupported(node, $"Unsupported floating compare branch opcode {node.SourceOp}");
                        }

                        _owner.Emit(RVInstruction.R(compare, RVRegister.X31, ToRegister(a), ToRegister(b)));
                        EmitLongConditionalBranch(
                            branchWhenTrue ? RVInstrKind.Bne : RVInstrKind.Beq,
                            RVRegister.X31,
                            RVRegister.X0,
                            target);
                        return;
                    }

                    bool unsigned = IsUnsigned(type) || node.SourceOp is BytecodeOp.Clt_Un or BytecodeOp.Cgt_Un;
                    bool i4 = IsI4(type, kind);
                    RVRegister aReg = ToIntegerRegister(left);
                    RVRegister bReg = ToIntegerRegister(right);
                    RVInstrKind branch;

                    if (node.SourceOp == BytecodeOp.Ceq && i4 && MachineTarget.Is64Bit)
                    {
                        _owner.Emit(RVInstruction.R(RVInstrKind.Subw, RVRegister.X31, aReg, bReg));
                        EmitLongConditionalBranch(
                            branchWhenTrue ? RVInstrKind.Beq : RVInstrKind.Bne,
                            RVRegister.X31,
                            RVRegister.X0,
                            target);
                        return;
                    }

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Ceq:
                            branch = branchWhenTrue ? RVInstrKind.Beq : RVInstrKind.Bne;
                            break;
                        case BytecodeOp.Clt:
                        case BytecodeOp.Clt_Un:
                            branch = branchWhenTrue
                                ? (unsigned ? RVInstrKind.Bltu : RVInstrKind.Blt)
                                : (unsigned ? RVInstrKind.Bgeu : RVInstrKind.Bge);
                            break;
                        case BytecodeOp.Cgt:
                        case BytecodeOp.Cgt_Un:
                            (aReg, bReg) = (bReg, aReg);
                            branch = branchWhenTrue
                                ? (unsigned ? RVInstrKind.Bltu : RVInstrKind.Blt)
                                : (unsigned ? RVInstrKind.Bgeu : RVInstrKind.Bge);
                            break;
                        default:
                            throw Unsupported(node, $"Unsupported compare branch opcode {node.SourceOp}");
                    }

                    if (i4)
                        NormalizeI4ComparisonOperands(ref aReg, ref bReg, unsigned);

                    EmitLongConditionalBranch(branch, aReg, bReg, target);
                }

                private void EmitLongConditionalBranch(RVInstrKind branch, RVRegister left, RVRegister right, string target)
                {
                    string skip = _owner.CreateLocalLabel($"{_methodLabel}_br_skip");
                    _owner.Emit(RVInstruction.B(InvertBranch(branch), left, right, skip));
                    EmitJump(target);
                    _owner.DefineLabel(skip);
                }

                private void EmitReturn(GenTree node)
                {
                    if (_ehMethod is not null &&
                        (FuncletIndexForBlock(node.BlockId) != 0 || ReturnMustRunFinallyBeforeMethodExit(node.BlockId)))
                    {
                        EmitReturnThroughEh(node);
                        return;
                    }

                    EmitEhFramePop();
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));
                }

                private void EmitReturnThroughEh(GenTree node)
                {
                    _returnThunkNeeded = true;
                    MarkEhCallSite(node, "return");
                    _owner.EmitMaterializeAddress(_returnThunkLabel, RVRegister.X10);
                    _owner.EmitLoadImmediate(RVRegister.X11, 2);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.LeaveSymbol), link: true);
                    EmitUnreachableTrap();
                }

                private void EmitReturnThunk()
                {
                    _owner.DefineLabel(_returnThunkLabel);
                    EmitEhFramePop();
                    if (_method.StackFrame.UsesFramePointer)
                        _owner.EmitMove(RVRegister.X2, RVRegister.X8);

                    for (int i = _method.StackFrame.CalleeSavedSlots.Length - 1; i >= 0; i--)
                    {
                        StackFrameSlot slot = _method.StackFrame.CalleeSavedSlots[i];
                        EmitMemoryLoad(slot.SavedRegister, RVRegister.X2, slot.Offset, slot.Size, signed: false);
                    }

                    if (_method.StackFrame.FrameSize > 0)
                        _owner.EmitAdjustStack(_method.StackFrame.FrameSize);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));
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
                    var regions = _method.Cfg.ExceptionRegions;
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

                private void EmitVirtualCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "VirtualCall node has no runtime method");
                    if (!method.HasThis)
                        throw Unsupported(node, "VirtualCall target has no implicit this parameter");

                    MachineRegister receiverRegister = RequireVirtualCallReceiverRegister(node);
                    if (receiverRegister != MachineRegister.X10)
                        throw Unsupported(node, "VirtualCall receiver is not in the first integer argument register");

                    RVRegister receiver = ToIntegerRegister(receiverRegister);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, receiver, "virtual_call");

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
                    EmitMemoryLoad(MachineRegister.X31, receiver, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X31, vtablePointerOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(
                        MachineRegister.X31,
                        RVRegister.X30,
                        checked(method.VTableSlot * Target.PointerSize),
                        Target.PointerSize,
                        signed: false);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, RVRegister.X31, 0));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitInterfaceVirtualCall(
                    GenTree node,
                    RuntimeMethod method,
                    RVRegister receiver,
                    SafePointDraft safePoint)
                {
                    string cellLabel = _owner.CreateInterfaceDispatchCell(method);
                    string loop = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_loop_{node.LinearId}");
                    string found = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_found_{node.LinearId}");
                    string missing = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_missing_{node.LinearId}");

                    EmitMemoryLoad(MachineRegister.X28, receiver, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(cellLabel, RVRegister.X30);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X30, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X30, Target.PointerSize);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X29, RVRegister.X0, missing);

                    _owner.DefineLabel(loop);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X30, 0, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X28, found);
                    _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X30, checked(Target.PointerSize * 2));
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, -1);
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X29, RVRegister.X0, loop);

                    _owner.DefineLabel(missing);
                    EmitVirtualDispatchFailure();

                    _owner.DefineLabel(found);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X30, Target.PointerSize, Target.PointerSize, signed: false);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, RVRegister.X31, 0));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitVirtualDispatchFailure()
                    => _owner.EmitPcrelTransfer(_owner.GetVirtualDispatchFailureStubLabel(), link: false);

                private MachineRegister RequireVirtualCallReceiverRegister(GenTree node)
                {
                    for (int i = 0; i < node.Uses.Length; i++)
                    {
                        if (i < node.UseRoles.Length && node.UseRoles[i] == OperandRole.HiddenReturnBuffer)
                            continue;
                        return RequireUseRegisterForOperand(node, i, "virtual call receiver");
                    }

                    throw Unsupported(node, "VirtualCall node has no receiver ABI operand");
                }

                private void EmitNewDelegate(GenTree node)
                {
                    RuntimeType delegateType = node.RuntimeType ?? node.Type ??
                        throw Unsupported(node, "NewDelegate node has no delegate runtime type");
                    RuntimeMethod targetMethod = node.Method ??
                        throw Unsupported(node, "NewDelegate node has no target method");
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "NewDelegate requires one register result");

                    int targetOffset = _owner.FindDelegateFieldOffset(delegateType, "_target");
                    int methodPtrOffset = _owner.FindDelegateFieldOffset(delegateType, "_methodPtr");
                    int invocationListOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationList");
                    int invocationCountOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationCount");
                    bool closed = node.Uses.Length != 0;
                    string thunkLabel = _owner.GetDelegateTargetThunkLabel(delegateType, targetMethod, closed);
                    int temporarySize = closed ? AlignUp(Target.PointerSize, Target.CallFrameAlignment) : 0;

                    if (closed)
                    {
                        if (node.Uses.Length != 1)
                            throw Unsupported(node, "Closed NewDelegate requires exactly one target operand");
                        MachineRegister target = RequireUseRegister(node, 0);
                        EmitNullCheck(node, ToIntegerRegister(target), "new_delegate_target");
                        _owner.EmitAdjustStack(-temporarySize);
                        EmitMemoryStore(target, RVRegister.X2, 0, Target.PointerSize);
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(delegateType), RVRegister.X10);
                    MarkEhCallSite(node, "new_delegate");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.NewFastSymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);

                    if (closed)
                        EmitMemoryLoad(MachineRegister.X29, RVRegister.X2, 0, Target.PointerSize, signed: false);
                    else
                        _owner.EmitMove(RVRegister.X29, RVRegister.X0);
                    EmitMemoryStore(MachineRegister.X29, RVRegister.X10, targetOffset, Target.PointerSize);
                    _owner.EmitMaterializeAddress(thunkLabel, RVRegister.X30);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X10, methodPtrOffset, Target.PointerSize);
                    EmitMemoryStore(MachineRegister.X0, RVRegister.X10, invocationListOffset, Target.PointerSize);
                    _owner.EmitLoadImmediate(RVRegister.X31, 1);
                    EmitMemoryStore(MachineRegister.X31, RVRegister.X10, invocationCountOffset, Target.PointerSize);
                    _owner.EmitMove(ToIntegerRegister(node.Results[0].Register), RVRegister.X10);

                    if (temporarySize != 0)
                        _owner.EmitAdjustStack(temporarySize);
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

                    LoadDelegateInvokeOperand(node.Uses[receiverUseIndex], MachineRegister.X28, Target.PointerSize);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, RVRegister.X28, "delegate_invoke");

                    int methodPtrOffset = _owner.FindDelegateFieldOffset(delegateType, "_methodPtr");
                    int invocationListOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationList");
                    int invocationCountOffset = _owner.FindDelegateFieldOffset(delegateType, "_invocationCount");
                    int outgoingSize = AlignUp(abi.OutgoingStackSize, Math.Max(1, Target.StackSlotSize));
                    int saveOffset = outgoingSize;
                    int saveSize = AlignUp(abi.TotalSaveSize, Target.PointerSize);
                    int listOffset = checked(saveOffset + saveSize);
                    int countOffset = checked(listOffset + Target.PointerSize);
                    int indexOffset = checked(countOffset + Target.PointerSize);
                    int oldStackPointerOffset = checked(indexOffset + Target.PointerSize);
                    int frameSize = AlignUp(
                        checked(oldStackPointerOffset + Target.PointerSize),
                        Target.CallFrameAlignment);
                    int receiverSaveOffset = checked(saveOffset + abi.OrderedSlices[receiverUseIndex].SaveOffset);

                    _owner.EmitMove(RVRegister.X28, RVRegister.X2);
                    _owner.EmitAdjustStack(-frameSize);
                    EmitMemoryStore(MachineRegister.X28, RVRegister.X2, oldStackPointerOffset, Target.PointerSize);
                    for (int i = 0; i < node.Uses.Length; i++)
                        SaveDelegateInvokeOperand(node.Uses[i], abi.OrderedSlices[i], saveOffset);

                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X28, invocationCountOffset, Target.PointerSize, signed: false);
                    _owner.EmitLoadImmediate(RVRegister.X30, 1);
                    string multicastLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_multicast_{node.LinearId}");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_done_{node.LinearId}");
                    EmitLongConditionalBranch(RVInstrKind.Bltu, RVRegister.X30, RVRegister.X29, multicastLabel);

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    SafePointDraft singleSafePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "delegate_single");
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X28, methodPtrOffset, Target.PointerSize, signed: false);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, RVRegister.X31, 0));
                    _owner.DefineLabel(singleSafePoint.ReturnLabel);
                    EmitJump(doneLabel);

                    _owner.DefineLabel(multicastLabel);
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X28, invocationListOffset, Target.PointerSize, signed: false);
                    string listValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_list_valid_{node.LinearId}");
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X30, RVRegister.X0, listValidLabel);
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(listValidLabel);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X2, listOffset, Target.PointerSize);
                    EmitMemoryStore(MachineRegister.X29, RVRegister.X2, countOffset, Target.PointerSize);
                    EmitMemoryStore(MachineRegister.X0, RVRegister.X2, indexOffset, Target.PointerSize);

                    string loopLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_loop_{node.LinearId}");
                    _owner.DefineLabel(loopLabel);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X2, indexOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X2, countOffset, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Bgeu, RVRegister.X29, RVRegister.X30, doneLabel);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X2, listOffset, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X30, Target.ArrayDataOffset);
                    int pointerShift = Target.PointerSize == 8 ? 3 : 2;
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, RVRegister.X31, RVRegister.X29, pointerShift));
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, RVRegister.X30, RVRegister.X30, RVRegister.X31));
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X30, 0, Target.PointerSize, signed: false);
                    string leafValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_leaf_valid_{node.LinearId}");
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X28, RVRegister.X0, leafValidLabel);
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(leafValidLabel);
                    EmitMemoryStore(MachineRegister.X28, RVRegister.X2, receiverSaveOffset, Target.PointerSize);

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, listOffset, Target.PointerSize, signed: false);
                    SafePointDraft multicastSafePoint = PrepareSafePoint(
                        node,
                        RegisterOperand.ForRegister(MachineRegister.X28));
                    MarkEhCallSite(node, "delegate_multicast");
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X28, methodPtrOffset, Target.PointerSize, signed: false);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, RVRegister.X31, 0));
                    _owner.DefineLabel(multicastSafePoint.ReturnLabel);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X2, indexOffset, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X29, 1);
                    EmitMemoryStore(MachineRegister.X29, RVRegister.X2, indexOffset, Target.PointerSize);
                    EmitJump(loopLabel);

                    _owner.DefineLabel(doneLabel);
                    EmitMemoryLoad(MachineRegister.X28, RVRegister.X2, oldStackPointerOffset, Target.PointerSize, signed: false);
                    _owner.EmitMove(RVRegister.X2, RVRegister.X28);
                }

                private void SaveDelegateInvokeOperand(
                    RegisterOperand operand,
                    DelegateAbiSlice slice,
                    int saveBase)
                {
                    int destinationOffset = checked(saveBase + slice.SaveOffset);
                    if (operand.IsRegister)
                    {
                        EmitMemoryStore(operand.Register, RVRegister.X2, destinationOffset, slice.Size);
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw new InvalidOperationException($"Delegate ABI operand is not finalized: {operand}.");

                    _owner.EmitAddImmediate(RVRegister.X29, FrameBase(operand), EffectiveFrameOffset(operand));
                    _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X2, destinationOffset, RVRegister.X29);
                    EmitBlockCopy(null, RVRegister.X30, RVRegister.X29, slice.Size);
                }

                private void RestoreDelegateInvokeAbi(DelegateAbiBundle abi, int saveBase)
                {
                    for (int i = 0; i < abi.OrderedSlices.Length; i++)
                    {
                        DelegateAbiSlice slice = abi.OrderedSlices[i];
                        int sourceOffset = checked(saveBase + slice.SaveOffset);
                        if (slice.Location.IsRegister)
                        {
                            EmitMemoryLoad(
                                slice.Location.Register,
                                RVRegister.X2,
                                sourceOffset,
                                slice.Size,
                                signed: false);
                            continue;
                        }

                        int destinationOffset = checked(
                            slice.Location.StackSlotIndex * Target.StackSlotSize +
                            slice.Location.StackOffset);
                        _owner.EmitAddImmediate(RVRegister.X29, RVRegister.X2, sourceOffset);
                        _owner.EmitAddImmediate(RVRegister.X30, RVRegister.X2, destinationOffset, RVRegister.X29);
                        EmitBlockCopy(null, RVRegister.X30, RVRegister.X29, slice.Size);
                    }
                }

                private void LoadDelegateInvokeOperand(RegisterOperand operand, MachineRegister destination, int size)
                {
                    if (operand.IsRegister)
                    {
                        _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(operand.Register));
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw new InvalidOperationException($"Delegate receiver operand is not finalized: {operand}.");
                    EmitMemoryLoad(destination, FrameBase(operand), EffectiveFrameOffset(operand), size, signed: false);
                }

                private void EmitDelegateCombineOrRemove(GenTree node, bool remove)
                {
                    if (node.Uses.Length != 2 || node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Delegate combine/remove requires two register operands and one register result");
                    if (!node.Uses[0].IsRegister || !node.Uses[1].IsRegister)
                        throw Unsupported(node, "Delegate combine/remove operands must be registers");

                    RuntimeType delegateLayoutType = _owner.FindSystemType("MulticastDelegate");
                    RuntimeType delegateArrayType = _owner.GetDelegateInvocationListArrayType();
                    RVRegister left = ToIntegerRegister(node.Uses[0].Register);
                    RVRegister right = ToIntegerRegister(node.Uses[1].Register);
                    RVRegister result = ToIntegerRegister(node.Results[0].Register);
                    string leftNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_left_null_{node.LinearId}");
                    string rightNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_right_null_{node.LinearId}");
                    string typesMatch = _owner.CreateLocalLabel($"{_methodLabel}_delegate_types_match_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_delegate_combine_done_{node.LinearId}");

                    EmitLongConditionalBranch(RVInstrKind.Beq, left, RVRegister.X0, leftNull);
                    EmitLongConditionalBranch(RVInstrKind.Beq, right, RVRegister.X0, rightNull);
                    EmitMemoryLoad(MachineRegister.X30, left, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X31, right, 0, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X31, typesMatch);
                    if (remove)
                    {
                        _owner.EmitMove(result, left);
                        EmitJump(done);
                    }
                    else
                    {
                        EmitManagedExceptionThrow(node, "ArgumentException");
                    }

                    _owner.DefineLabel(typesMatch);
                    int frameSize = AlignUp(checked(Target.PointerSize * 3), Target.CallFrameAlignment);
                    _owner.EmitAdjustStack(-frameSize);
                    EmitMemoryStore(node.Uses[0].Register, RVRegister.X2, 0, Target.PointerSize);
                    EmitMemoryStore(node.Uses[1].Register, RVRegister.X2, Target.PointerSize, Target.PointerSize);
                    EmitMemoryStore(MachineRegister.X30, RVRegister.X2, checked(Target.PointerSize * 2), Target.PointerSize);
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    MarkEhCallSite(node, remove ? "delegate_remove" : "delegate_combine");
                    EmitMemoryLoad(MachineRegister.X10, RVRegister.X2, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X11, RVRegister.X2, Target.PointerSize, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X12, RVRegister.X2, checked(Target.PointerSize * 2), Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(delegateArrayType), RVRegister.X13);
                    _owner.EmitLoadImmediate(RVRegister.X14, _owner.FindDelegateFieldOffset(delegateLayoutType, "_target"));
                    _owner.EmitLoadImmediate(RVRegister.X15, _owner.FindDelegateFieldOffset(delegateLayoutType, "_methodPtr"));
                    _owner.EmitLoadImmediate(RVRegister.X16, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationList"));
                    _owner.EmitLoadImmediate(RVRegister.X17, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationCount"));
                    _owner.EmitPcrelTransfer(
                        _owner.ResolveExternalSymbol(remove ? RiscVRuntime.DelegateRemoveSymbol : RiscVRuntime.DelegateCombineSymbol),
                        link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.EmitMove(result, RVRegister.X10);
                    _owner.EmitAdjustStack(frameSize);
                    EmitJump(done);

                    _owner.DefineLabel(leftNull);
                    _owner.EmitMove(result, remove ? left : right);
                    EmitJump(done);
                    _owner.DefineLabel(rightNull);
                    _owner.EmitMove(result, left);
                    _owner.DefineLabel(done);
                }

                private void EmitDelegateFailFast(int code)
                {
                    _owner.EmitLoadImmediate(RVRegister.X10, code);
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.FailFastSymbol), link: false);
                }

                private void EmitIndirectFunctionPointerCall(GenTree node)
                {
                    RuntimeType signature = node.RuntimeType ?? throw Unsupported(node, "indirect call has no function pointer signature");
                    if (signature.Kind != RuntimeTypeKind.FunctionPointer || signature.FunctionPointerCallingConvention != 0)
                        throw Unsupported(node, "indirect call has an unsupported function pointer signature");

                    int targetIndex = -1;
                    for (int i = 0; i < node.UseRoles.Length; i++)
                    {
                        if (node.UseRoles[i] == OperandRole.IndirectCallTarget)
                        {
                            if (targetIndex >= 0)
                                throw Unsupported(node, "indirect call has multiple target operands");
                            targetIndex = i;
                        }
                    }
                    if (targetIndex < 0 || !node.Uses[targetIndex].IsRegister)
                        throw Unsupported(node, "indirect call has no target register");

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "calli");
                    _owner.Emit(RVInstruction.I(
                        RVInstrKind.Jalr,
                        RVRegister.X1,
                        ToIntegerRegister(node.Uses[targetIndex].Register),
                        0));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "Call node has no runtime method");
                    if (method.HasInternalCall)
                    {
                        if (RiscVRuntime.TryEvaluateIsReferenceOrContainsReferences(method, out bool containsReferences))
                        {
                            _owner.EmitLoadImmediate(RVRegister.X10, containsReferences ? 1 : 0);
                            return;
                        }
                        if (RiscVRuntime.IsGcSafePointInternalCall(method))
                        {
                            EmitFastAllocateString(node, method);
                            return;
                        }

                        MarkEhCallSite(node, "call");
                        _owner.EmitPcrelTransfer(_owner.ResolveMethodLabel(method), link: true);
                        return;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "call");
                    _owner.EmitPcrelTransfer(_owner.ResolveMethodLabel(method), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitClassInit(GenTree node)
                {
                    RuntimeType type = node.RuntimeType ?? throw Unsupported(node, "ClassInit node has no runtime type");
                    string stateLabel = _owner.GetTypeInitializationStateLabel(type);
                    string initializedLabel = _owner.CreateLocalLabel(_methodLabel + "_type_init_initialized");
                    string doneLabel = _owner.CreateLocalLabel(_methodLabel + "_type_init_done");

                    _owner.EmitMaterializeAddress(stateLabel, RVRegister.X28);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X28, 0, 4, signed: false);
                    _owner.Emit(RVInstruction.B(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, initializedLabel));
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "class_init");
                    _owner.EmitPcrelTransfer(_owner.GetTypeInitializationThunkLabel(type), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, doneLabel));

                    _owner.DefineLabel(initializedLabel);
                    _owner.Emit(new RVInstruction(RVInstrKind.Fence, immediate: 0x23));
                    _owner.DefineLabel(doneLabel);
                }

                private void EmitFastAllocateString(GenTree node, RuntimeMethod method)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMove(RVRegister.X11, RVRegister.X10);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(method.DeclaringType), RVRegister.X10);
                    MarkEhCallSite(node, "string_alloc");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.NewArraySymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitNewArray(GenTree node)
                {
                    if (node.Type is null || node.Type.Kind != RuntimeTypeKind.Array)
                        throw Unsupported(node, "NewArray node has no array runtime type");
                    if (!node.Type.IsSzArray)
                        throw Unsupported(node, "Multidimensional array allocation is not implemented");
                    if (node.Uses.Length != 1 || node.Results.Length != 1)
                        throw Unsupported(node, "NewArray must have one length operand and one result");

                    MachineRegister length = RequireUseRegisterForOperand(node, 0, "array length");
                    MachineRegister result = RequireResultRegister(node);
                    RVRegister checkedLength = ToIntegerRegister(length);
                    if (MachineTarget.Is64Bit)
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X28, checkedLength, 0));
                        checkedLength = RVRegister.X28;
                    }
                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_array_length_non_negative_{node.LinearId}");
                    EmitLongConditionalBranch(
                        RVInstrKind.Bge,
                        checkedLength,
                        RVRegister.X0,
                        nonNegative);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(nonNegative);
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);

                    _owner.EmitMove(RVRegister.X11, checkedLength);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(node.Type), RVRegister.X10);
                    MarkEhCallSite(node, "new_array");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.NewArraySymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.EmitMove(ToIntegerRegister(result), RVRegister.X10);
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
                            throw Unsupported(node, $"Unsupported array operation {node.TreeKind}");
                    }
                }

                private void EmitArrayDataReference(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array data reference requires one array use and one register result");

                    RVRegister array = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "array data reference"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);
                    EmitMemoryLoad(MachineRegister.X30, array, 0, Target.PointerSize, signed: false);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -0x18));
                    string valid = _owner.CreateLocalLabel(_methodLabel + "_array_data_valid");
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, valid);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(valid);
                    _owner.EmitAddImmediate(
                        ToIntegerRegister(node.Results[0].Register),
                        array,
                        Target.ArrayDataOffset);
                }

                private void EmitArrayElementAddressResult(GenTree node, RuntimeType elementType)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array element address requires one register result");

                    EmitArrayElementTypeCheck(node, elementType, requireExact: true);
                    EmitArrayElementAddress(node, elementType, RVRegister.X28, nullChecked: true);
                    _owner.EmitMove(ToIntegerRegister(node.Results[0].Register), RVRegister.X28);
                }

                private void EmitArrayElementLoad(GenTree node, RuntimeType elementType)
                {
                    EmitArrayElementTypeCheck(node, elementType, requireExact: false);
                    EmitArrayElementAddress(node, elementType, RVRegister.X28, nullChecked: true);
                    EmitValueFromAddress(node, elementType, node.StackKind, RVRegister.X28);
                }

                private void EmitArrayElementStore(GenTree node, RuntimeType elementType)
                {
                    int arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array-element store array");
                    int indexUseIndex = RequireCodegenUseIndexForOperand(node, 1, "array-element store index");
                    int valueUseIndex = RequireCodegenUseIndexForOperand(node, 2, "array-element store value");
                    if (!node.Uses[arrayUseIndex].IsRegister || !node.Uses[indexUseIndex].IsRegister)
                        throw Unsupported(node, "Array and index operands must be in registers");

                    RVRegister array = ToIntegerRegister(node.Uses[arrayUseIndex].Register);
                    EmitArrayElementTypeCheck(node, elementType, requireExact: false, arrayUseIndex: arrayUseIndex);
                    EmitArrayElementAddress(node, elementType, RVRegister.X28, arrayUseIndex, indexUseIndex, nullChecked: true);

                    GenStackKind kind = StackKindForType(elementType);
                    if (elementType.IsReferenceType)
                    {
                        RegisterOperand value = node.Uses[valueUseIndex];
                        if (!value.IsRegister || value.RegisterClass != RegisterClass.General)
                            throw Unsupported(node, "Reference array store value must be in an integer register");
                        EmitArrayReferenceStoreCheck(node, array, ToIntegerRegister(value.Register));
                    }

                    EmitValueToAddress(node, valueUseIndex, elementType, kind, RVRegister.X28);
                }

                private void EmitArrayElementAddress(
                    GenTree node,
                    RuntimeType elementType,
                    RVRegister destination,
                    int arrayUseIndex = -1,
                    int indexUseIndex = -1,
                    bool nullChecked = false)
                {
                    if (arrayUseIndex < 0)
                        arrayUseIndex = RequireCodegenUseIndexForOperand(node, 0, "array operand");
                    if (indexUseIndex < 0)
                        indexUseIndex = RequireCodegenUseIndexForOperand(node, 1, "array index");
                    if (!node.Uses[arrayUseIndex].IsRegister || !node.Uses[indexUseIndex].IsRegister)
                        throw Unsupported(node, "Array element address requires register operands");

                    RVRegister array = ToIntegerRegister(node.Uses[arrayUseIndex].Register);
                    RVRegister index = ToIntegerRegister(node.Uses[indexUseIndex].Register);
                    if (!nullChecked && (node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);

                    if (MachineTarget.Is64Bit && IsI4(OperandType(node, 1), OperandStackKind(node, 1)))
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X29, index, 0));
                        index = RVRegister.X29;
                    }
                    else if (index != RVRegister.X29)
                    {
                        _owner.EmitMove(RVRegister.X29, index);
                        index = RVRegister.X29;
                    }

                    if ((node.Flags & GenTreeFlags.BoundsCheckEliminated) == 0)
                    {
                        EmitMemoryLoad(MachineRegister.X30, array, Target.ArrayLengthOffset, 4, signed: true);
                        string inRange = _owner.CreateLocalLabel(_methodLabel + "_array_index_in_range");
                        EmitLongConditionalBranch(RVInstrKind.Bltu, index, RVRegister.X30, inRange);
                        EmitManagedExceptionThrow(node, "IndexOutOfRangeException");
                        _owner.DefineLabel(inRange);
                    }

                    int elementSize = ArrayElementSize(elementType);
                    if (elementSize != 1)
                    {
                        if (BitOperations.IsPow2(elementSize))
                        {
                            _owner.Emit(RVInstruction.I(
                                RVInstrKind.Slli,
                                RVRegister.X29,
                                index,
                                Log2(elementSize)));
                        }
                        else
                        {
                            RequireM(node);
                            _owner.EmitLoadImmediate(RVRegister.X31, elementSize);
                            _owner.Emit(RVInstruction.R(
                                RVInstrKind.Mul,
                                RVRegister.X29,
                                index,
                                RVRegister.X31));
                        }
                    }

                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, destination, array, RVRegister.X29));
                    _owner.EmitAddImmediate(destination, destination, Target.ArrayDataOffset);
                }

                private void EmitArrayNullCheck(GenTree node, RVRegister array)
                {
                    string nonNull = _owner.CreateLocalLabel(_methodLabel + "_array_non_null");
                    EmitLongConditionalBranch(RVInstrKind.Bne, array, RVRegister.X0, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");
                    _owner.DefineLabel(nonNull);
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

                    RVRegister array = ToIntegerRegister(node.Uses[arrayUseIndex].Register);
                    string done = _owner.CreateLocalLabel(_methodLabel + "_array_element_type_ok");
                    string fail = _owner.CreateLocalLabel(_methodLabel + "_array_element_type_fail");

                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);
                    EmitMemoryLoad(MachineRegister.X30, array, 0, Target.PointerSize, signed: false);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -0x18));
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X0, fail);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X30, 8, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(elementType), RVRegister.X29);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, done);

                    if (!requireExact && elementType.IsReferenceType)
                    {
                        EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -0x14));
                        _owner.Emit(RVInstruction.I(RVInstrKind.Sltiu, RVRegister.X31, RVRegister.X31, 5));
                        EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, fail);
                        EmitLoadedTypeAssignabilityCheck(done, fail);
                    }
                    else
                    {
                        EmitJump(fail);
                    }

                    _owner.DefineLabel(fail);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(done);
                }

                private void EmitArrayReferenceStoreCheck(GenTree node, RVRegister array, RVRegister value)
                {
                    string done = _owner.CreateLocalLabel(_methodLabel + "_array_store_type_ok");
                    string fail = _owner.CreateLocalLabel(_methodLabel + "_array_store_type_fail");

                    EmitLongConditionalBranch(RVInstrKind.Beq, value, RVRegister.X0, done);
                    EmitMemoryLoad(MachineRegister.X29, array, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X29, 8, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X30, value, 0, Target.PointerSize, signed: false);
                    EmitLoadedTypeAssignabilityCheck(done, fail);

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

                    string loop = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_loop");
                    string targetClass = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_target_class");
                    string targetInterface = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_target_interface");
                    string targetSystemArray = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_target_system_array");
                    string targetSzArray = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_target_szarray");
                    string sourceBase = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_source_base");
                    string interfaceLoop = _owner.CreateLocalLabel(_methodLabel + "_type_assignability_interface_loop");

                    _owner.DefineLabel(loop);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, success);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X29);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeClass));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, targetClass);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, elementTypeClass - elementTypeInterface));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, targetInterface);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, elementTypeInterface - elementTypeSystemArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, targetSystemArray);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, elementTypeSystemArray - elementTypeSzArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, targetSzArray);
                    EmitJump(failure);

                    _owner.DefineLabel(targetClass);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X29, methodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, success);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, failure);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, elementTypeArray - elementTypeSzArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, failure);
                    EmitJump(sourceBase);

                    _owner.DefineLabel(targetSystemArray);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, success);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, elementTypeArray - elementTypeSzArray));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, success);
                    EmitJump(failure);

                    _owner.DefineLabel(targetSzArray);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeSzArray));
                    EmitLongConditionalBranch(RVInstrKind.Bne, RVRegister.X31, RVRegister.X0, failure);
                    EmitMemoryLoad(MachineRegister.X29, RVRegister.X29, methodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X30, methodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, success);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X29);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeClass));
                    _owner.Emit(RVInstruction.I(RVInstrKind.Sltiu, RVRegister.X31, RVRegister.X31, elementTypeSzArray - elementTypeClass + 1));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, failure);
                    EmitMethodTableElementType(RVRegister.X31, RVRegister.X30);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X31, RVRegister.X31, -elementTypeClass));
                    _owner.Emit(RVInstruction.I(RVInstrKind.Sltiu, RVRegister.X31, RVRegister.X31, elementTypeSzArray - elementTypeClass + 1));
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, failure);
                    EmitJump(loop);

                    _owner.DefineLabel(targetInterface);
                    EmitMemoryLoad(MachineRegister.X31, RVRegister.X30, methodTableInterfaceMapOffset, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X31, RVRegister.X0, failure);
                    _owner.DefineLabel(interfaceLoop);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X31, 0, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X0, failure);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X29, success);
                    _owner.EmitAddImmediate(RVRegister.X31, RVRegister.X31, Target.PointerSize);
                    EmitJump(interfaceLoop);

                    _owner.DefineLabel(sourceBase);
                    EmitMemoryLoad(MachineRegister.X30, RVRegister.X30, methodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitLongConditionalBranch(RVInstrKind.Beq, RVRegister.X30, RVRegister.X0, failure);
                    EmitJump(loop);
                }

                private void EmitMethodTableElementType(RVRegister destination, RVRegister methodTable)
                {
                    EmitMemoryLoad((MachineRegister)(byte)destination, methodTable, 0, 4, signed: false);
                    _owner.Emit(RVInstruction.I(RVInstrKind.Srli, destination, destination, 26));
                    _owner.Emit(RVInstruction.I(RVInstrKind.Andi, destination, destination, 0x1f));
                }

                private void EmitValueFromAddress(
                    GenTree node,
                    RuntimeType? type,
                    GenStackKind kind,
                    RVRegister address)
                {
                    var abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);

                    if (node.Results.Length == 0)
                        return;

                    address = PreserveLoadAddress(node, address);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        EmitMultiRegisterLoadFromAddress(node, type, kind, address, segments);
                        return;
                    }

                    if (node.Results.Length != 1)
                        throw Unsupported(node, "Load result count does not match its storage ABI");

                    RegisterOperand result = node.Results[0];
                    if (IsAggregate(type, kind) && result.IsFrameSlot)
                    {
                        EmitCopyAddressToFrame(node, result, address, StorageSize(type, kind, result));
                        return;
                    }

                    if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect)
                    {
                        if (!result.IsFrameSlot)
                            throw Unsupported(node, "Stack-resident load has no frame result");
                        EmitCopyAddressToFrame(node, result, address, StorageSize(type, kind, result));
                        return;
                    }

                    RuntimeType? accessType = IsAggregate(type, kind) ? null : type;
                    GenStackKind accessKind = IsAggregate(type, kind) && segments.Length == 1
                        ? StackKindForSegment(segments[0])
                        : kind;
                    int size = segments.Length == 1 ? segments[0].Size : StorageSize(type, kind, result);
                    bool signed = accessType is not null && IsSigned(accessType, accessKind);

                    if (result.IsRegister)
                    {
                        if (IsAggregate(type, kind) && segments.Length == 1)
                            EmitAggregateSegmentLoad(result.Register, address, segments[0]);
                        else
                            EmitMemoryLoad(result.Register, address, 0, size, signed);
                        return;
                    }

                    if (result.IsFrameSlot)
                    {
                        MachineRegister scratch = result.RegisterClass == RegisterClass.Float
                            ? MachineRegister.F28
                            : MachineRegister.X30;
                        if (IsAggregate(type, kind) && segments.Length == 1)
                            EmitAggregateSegmentLoad(scratch, address, segments[0]);
                        else
                            EmitMemoryLoad(scratch, address, 0, size, signed);
                        EmitStore(result, scratch, accessType, accessKind);
                        return;
                    }

                    throw Unsupported(node, "Load result is not addressable");
                }

                private RVRegister PreserveLoadAddress(GenTree node, RVRegister address)
                {
                    bool preserve = address == RVRegister.X30 || address == RVRegister.X31;
                    if (!preserve)
                    {
                        for (int i = 0; i < node.Results.Length; i++)
                        {
                            RegisterOperand result = node.Results[i];
                            if (result.IsRegister &&
                                result.RegisterClass == RegisterClass.General &&
                                ToIntegerRegister(result.Register) == address)
                            {
                                preserve = true;
                                break;
                            }
                        }
                    }

                    return preserve ? PreserveAddress(node, address) : address;
                }

                private RVRegister PreserveStoreAddress(GenTree node, RVRegister address)
                    => address == RVRegister.X30 || address == RVRegister.X31
                        ? PreserveAddress(node, address)
                        : address;

                private RVRegister PreserveAddress(GenTree node, RVRegister address)
                {
                    RVRegister scratch = SelectAddressScratch(node, address);
                    _owner.EmitMove(scratch, address);
                    return scratch;
                }

                private static RVRegister SelectAddressScratch(GenTree node, RVRegister address)
                {
                    if (address != RVRegister.X28 && !NodeUsesIntegerRegister(node, RVRegister.X28))
                        return RVRegister.X28;
                    if (address != RVRegister.X29 && !NodeUsesIntegerRegister(node, RVRegister.X29))
                        return RVRegister.X29;
                    throw new InvalidOperationException("No RISC-V address-preservation scratch register is available.");
                }

                private static bool NodeUsesIntegerRegister(GenTree node, RVRegister register)
                {
                    for (int i = 0; i < node.Results.Length; i++)
                    {
                        RegisterOperand operand = node.Results[i];
                        if (operand.IsRegister &&
                            operand.RegisterClass == RegisterClass.General &&
                            ToIntegerRegister(operand.Register) == register)
                        {
                            return true;
                        }
                    }

                    for (int i = 0; i < node.Uses.Length; i++)
                    {
                        RegisterOperand operand = node.Uses[i];
                        if (operand.IsRegister &&
                            operand.RegisterClass == RegisterClass.General &&
                            ToIntegerRegister(operand.Register) == register)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                private void EmitValueToAddress(
                    GenTree node,
                    int firstUseIndex,
                    RuntimeType type,
                    GenStackKind kind,
                    RVRegister address)
                {
                    address = PreserveStoreAddress(node, address);

                    var abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        EmitMultiRegisterStoreToAddress(node, firstUseIndex, type, kind, address, segments);
                        return;
                    }

                    if ((uint)firstUseIndex >= (uint)node.Uses.Length)
                        throw Unsupported(node, "Store has no value operand");

                    RegisterOperand source = node.Uses[firstUseIndex];
                    if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect ||
                        (IsAggregate(type, kind) && source.IsFrameSlot))
                    {
                        if (!source.IsFrameSlot)
                            throw Unsupported(node, "Stack-resident store value has no addressable home");
                        EmitCopyFrameToAddress(node, address, source, StorageSize(type, kind, source));
                        return;
                    }

                    RuntimeType? accessType = IsAggregate(type, kind) ? null : type;
                    GenStackKind accessKind = IsAggregate(type, kind) && segments.Length == 1
                        ? StackKindForSegment(segments[0])
                        : kind;
                    int size = segments.Length == 1 ? segments[0].Size : StorageSize(type, kind, source);

                    if (source.IsRegister)
                    {
                        if (IsAggregate(type, kind) && segments.Length == 1)
                            EmitAggregateSegmentStore(source.Register, address, segments[0]);
                        else
                            EmitMemoryStore(source.Register, address, 0, size);
                        return;
                    }

                    if (source.IsFrameSlot)
                    {
                        MachineRegister scratch = source.RegisterClass == RegisterClass.Float
                            ? MachineRegister.F28
                            : MachineRegister.X30;
                        EmitLoad(scratch, source, accessType, accessKind);
                        EmitMemoryStore(scratch, address, 0, size);
                        return;
                    }

                    throw Unsupported(node, "Store value is not addressable");
                }

                private void EmitMultiRegisterLoadFromAddress(
                    GenTree node,
                    RuntimeType? type,
                    GenStackKind kind,
                    RVRegister address,
                    ImmutableArray<AbiRegisterSegment> segments)
                {
                    if (segments.Length <= 1 || node.Results.Length != segments.Length)
                        throw Unsupported(node, "Multi-register load result count does not match ABI storage segments");

                    for (int i = 0; i < segments.Length; i++)
                    {
                        AbiRegisterSegment segment = segments[i];
                        RegisterOperand destination = node.Results[i];
                        if (destination.IsRegister)
                        {
                            EmitAggregateSegmentLoad(destination.Register, address, segment);
                            continue;
                        }

                        if (destination.IsFrameSlot)
                        {
                            MachineRegister scratch = segment.RegisterClass == RegisterClass.Float
                                ? MachineRegister.F28
                                : MachineRegister.X30;
                            EmitAggregateSegmentLoad(scratch, address, segment);
                            EmitAggregateSegmentStore(
                                scratch,
                                FrameBase(destination),
                                EffectiveFrameOffset(destination),
                                segment);
                            continue;
                        }

                        throw Unsupported(node, "Multi-register load fragment has no destination");
                    }
                }

                private void EmitMultiRegisterStoreToAddress(
                    GenTree node,
                    int firstUseIndex,
                    RuntimeType type,
                    GenStackKind kind,
                    RVRegister address,
                    ImmutableArray<AbiRegisterSegment> segments)
                {
                    if (segments.Length <= 1 || firstUseIndex + segments.Length > node.Uses.Length)
                        throw Unsupported(node, "Multi-register store source count does not match ABI storage segments");

                    for (int i = 0; i < segments.Length; i++)
                    {
                        AbiRegisterSegment segment = segments[i];
                        RegisterOperand source = node.Uses[firstUseIndex + i];
                        MachineRegister value;
                        if (source.IsRegister)
                        {
                            value = source.Register;
                        }
                        else if (source.IsFrameSlot)
                        {
                            value = segment.RegisterClass == RegisterClass.Float
                                ? MachineRegister.F28
                                : MachineRegister.X30;
                            EmitAggregateSegmentLoad(
                                value,
                                FrameBase(source),
                                EffectiveFrameOffset(source),
                                segment);
                        }
                        else
                        {
                            throw Unsupported(node, "Multi-register store fragment has no source");
                        }

                        EmitAggregateSegmentStore(value, address, segment);
                    }
                }

                private void EmitAggregateSegmentLoad(
                    MachineRegister destination,
                    RVRegister baseRegister,
                    AbiRegisterSegment segment)
                {
                    EmitAggregateSegmentLoad(destination, baseRegister, segment.Offset, segment);
                }

                private void EmitAggregateSegmentLoad(
                    MachineRegister destination,
                    RVRegister baseRegister,
                    int offset,
                    AbiRegisterSegment segment)
                {
                    if (MachineRegisters.GetClass(destination) != segment.RegisterClass)
                        throw new InvalidOperationException("Aggregate load destination register class does not match its ABI segment.");

                    if (segment.RegisterClass == RegisterClass.Float)
                    {
                        EmitMemoryLoad(destination, baseRegister, offset, segment.Size, signed: false);
                        return;
                    }

                    EmitIntegerFragmentLoad(destination, baseRegister, offset, segment.Size);
                }

                private void EmitAggregateSegmentStore(
                    MachineRegister source,
                    RVRegister baseRegister,
                    AbiRegisterSegment segment)
                {
                    EmitAggregateSegmentStore(source, baseRegister, segment.Offset, segment);
                }

                private void EmitAggregateSegmentStore(
                    MachineRegister source,
                    RVRegister baseRegister,
                    int offset,
                    AbiRegisterSegment segment)
                {
                    if (MachineRegisters.GetClass(source) != segment.RegisterClass)
                        throw new InvalidOperationException("Aggregate store source register class does not match its ABI segment.");

                    if (segment.RegisterClass == RegisterClass.Float)
                    {
                        EmitMemoryStore(source, baseRegister, offset, segment.Size);
                        return;
                    }

                    EmitIntegerFragmentStore(source, baseRegister, offset, segment.Size);
                }

                private void EmitIntegerFragmentLoad(
                    MachineRegister destination,
                    RVRegister baseRegister,
                    int offset,
                    int size)
                {
                    if (size <= 0 || size > Target.GeneralRegisterSize)
                        throw new NotImplementedException($"Unsupported integer fragment load size {size}.");

                    RVRegister result = ToIntegerRegister(destination);
                    if (size == 1)
                    {
                        EmitMemoryLoad(destination, baseRegister, offset, 1, signed: false);
                        return;
                    }

                    RVRegister scratch = result == RVRegister.X31 ? RVRegister.X30 : RVRegister.X31;
                    _owner.EmitMove(result, RVRegister.X0);
                    for (int i = 0; i < size; i++)
                    {
                        EmitMemoryLoad((MachineRegister)(byte)scratch, baseRegister, checked(offset + i), 1, signed: false);
                        if (i != 0)
                            _owner.Emit(RVInstruction.I(RVInstrKind.Slli, scratch, scratch, i * 8));
                        _owner.Emit(RVInstruction.R(RVInstrKind.Or, result, result, scratch));
                    }
                }

                private void EmitIntegerFragmentStore(
                    MachineRegister source,
                    RVRegister baseRegister,
                    int offset,
                    int size)
                {
                    if (size <= 0 || size > Target.GeneralRegisterSize)
                        throw new NotImplementedException($"Unsupported integer fragment store size {size}.");

                    RVRegister value = ToIntegerRegister(source);
                    EmitMemoryStore(source, baseRegister, offset, 1);
                    if (size == 1)
                        return;

                    RVRegister scratch = value == RVRegister.X31 ? RVRegister.X30 : RVRegister.X31;
                    for (int i = 1; i < size; i++)
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Srli, scratch, value, i * 8));
                        EmitMemoryStore((MachineRegister)(byte)scratch, baseRegister, checked(offset + i), 1);
                    }
                }

                private void EmitCopyAddressToFrame(GenTree node, RegisterOperand destination, RVRegister sourceAddress, int size)
                {
                    if (!destination.IsFrameSlot)
                        throw Unsupported(node, "Block-copy destination is not a frame slot");

                    RVRegister source = PreserveBlockCopyAddress(sourceAddress);
                    _owner.EmitAddImmediate(RVRegister.X29, FrameBase(destination), EffectiveFrameOffset(destination));
                    EmitBlockCopy(node, RVRegister.X29, source, size);
                }

                private void EmitCopyFrameToAddress(GenTree node, RVRegister destinationAddress, RegisterOperand source, int size)
                {
                    if (!source.IsFrameSlot)
                        throw Unsupported(node, "Block-copy source is not a frame slot");

                    RVRegister destination = PreserveBlockCopyAddress(destinationAddress);
                    _owner.EmitAddImmediate(RVRegister.X29, FrameBase(source), EffectiveFrameOffset(source));
                    EmitBlockCopy(node, destination, RVRegister.X29, size);
                }

                private RVRegister PreserveBlockCopyAddress(RVRegister address)
                {
                    if (address == RVRegister.X28 ||
                        (address != RVRegister.X29 && address != RVRegister.X30 && address != RVRegister.X31))
                    {
                        return address;
                    }

                    _owner.EmitMove(RVRegister.X28, address);
                    return RVRegister.X28;
                }

                private void EmitBlockCopy(GenTree? node, RVRegister destination, RVRegister source, int size)
                {
                    if (size < 0)
                    {
                        if (node is not null)
                            throw Unsupported(node, "Block-copy size is negative");
                        throw new InvalidOperationException("Block-copy size is negative.");
                    }
                    if (size == 0)
                        return;

                    RVRegister boundary = SelectBlockCopyScratch(destination, source);
                    RVRegister value = SelectBlockCopyScratch(destination, source, boundary);
                    string forward = _owner.CreateLocalLabel(_methodLabel + "_block_copy_forward");
                    string done = _owner.CreateLocalLabel(_methodLabel + "_block_copy_done");

                    _owner.EmitAddImmediate(boundary, source, size, destination);
                    EmitLongConditionalBranch(RVInstrKind.Bltu, destination, source, forward);
                    EmitLongConditionalBranch(RVInstrKind.Bgeu, destination, boundary, forward);

                    for (int offset = size - 1; offset >= 0; offset--)
                    {
                        EmitMemoryLoad((MachineRegister)(byte)value, source, offset, 1, signed: false);
                        EmitMemoryStore((MachineRegister)(byte)value, destination, offset, 1);
                    }
                    EmitJump(done);

                    _owner.DefineLabel(forward);
                    for (int offset = 0; offset < size; offset++)
                    {
                        EmitMemoryLoad((MachineRegister)(byte)value, source, offset, 1, signed: false);
                        EmitMemoryStore((MachineRegister)(byte)value, destination, offset, 1);
                    }
                    _owner.DefineLabel(done);
                }

                private static RVRegister SelectBlockCopyScratch(
                    RVRegister first,
                    RVRegister second,
                    RVRegister third = RVRegister.Invalid)
                {
                    if (first != RVRegister.X31 && second != RVRegister.X31 && third != RVRegister.X31)
                        return RVRegister.X31;
                    if (first != RVRegister.X30 && second != RVRegister.X30 && third != RVRegister.X30)
                        return RVRegister.X30;
                    if (first != RVRegister.X29 && second != RVRegister.X29 && third != RVRegister.X29)
                        return RVRegister.X29;
                    if (first != RVRegister.X28 && second != RVRegister.X28 && third != RVRegister.X28)
                        return RVRegister.X28;
                    throw new InvalidOperationException("No RISC-V block-copy scratch register is available.");
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

                private void EmitNewObject(GenTree node)
                {
                    RuntimeMethod constructor = node.Method ?? throw Unsupported(node, "NewObject node has no constructor");
                    RuntimeType objectType = constructor.DeclaringType;
                    if (!constructor.HasThis)
                        throw Unsupported(node, "NewObject constructor has no implicit this parameter");
                    if (objectType.IsValueType)
                        throw Unsupported(node, "Value-type newobj must be lowered before RISC-V code generation");
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
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(objectType), RVRegister.X10);
                    MarkEhCallSite(node, "new_object_alloc");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(RiscVRuntime.NewFastSymbol), link: true);
                    _owner.DefineLabel(allocationSafePoint.ReturnLabel);
                    EmitStore(objectHome, MachineRegister.X10, objectType, GenStackKind.Ref);

                    RestoreNewObjectArguments();
                    EmitLoad(MachineRegister.X10, objectHome, objectType, GenStackKind.Ref);
                    SafePointDraft constructorSafePoint = PrepareSafePoint(node, objectHome);
                    MarkEhCallSite(node, "new_object_ctor");
                    _owner.EmitPcrelTransfer(_owner.ResolveMethodLabel(constructor), link: true);
                    _owner.DefineLabel(constructorSafePoint.ReturnLabel);
                }

                private void EmitNewStringObject(GenTree node, RuntimeMethod constructor, RegisterOperand objectHome)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    RuntimeType[] parameters = constructor.ParameterTypes;
                    string runtimeSymbol;

                    PublishGcTransition(safePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(constructor.DeclaringType), RVRegister.X10);

                    if (parameters.Length == 0)
                    {
                        _owner.EmitLoadImmediate(RVRegister.X11, 0);
                        runtimeSymbol = RiscVRuntime.NewArraySymbol;
                    }
                    else if (parameters.Length == 2 && IsCharType(parameters[0]) && IsInt32Type(parameters[1]))
                    {
                        runtimeSymbol = RiscVRuntime.NewStringFromCharSymbol;
                    }
                    else if (parameters.Length == 1 && IsCharPointerType(parameters[0]))
                    {
                        runtimeSymbol = RiscVRuntime.NewStringFromUtf16Symbol;
                    }
                    else if (parameters.Length == 1 && IsCharArrayType(parameters[0]))
                    {
                        runtimeSymbol = RiscVRuntime.NewStringFromCharArraySymbol;
                    }
                    else if (parameters.Length == 3 &&
                             IsCharArrayType(parameters[0]) &&
                             IsInt32Type(parameters[1]) &&
                             IsInt32Type(parameters[2]))
                    {
                        runtimeSymbol = RiscVRuntime.NewStringFromCharArrayRangeSymbol;
                    }
                    else
                    {
                        throw Unsupported(node, "Unsupported System.String constructor shape");
                    }

                    MarkEhCallSite(node, "new_string");
                    _owner.EmitPcrelTransfer(_owner.ResolveExternalSymbol(runtimeSymbol), link: true);
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    EmitStore(objectHome, MachineRegister.X10, constructor.DeclaringType, GenStackKind.Ref);
                }

                private static bool IsCharType(RuntimeType type)
                    => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                       StringComparer.Ordinal.Equals(type.Name, "Char");

                private static bool IsInt32Type(RuntimeType type)
                    => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                       StringComparer.Ordinal.Equals(type.Name, "Int32");

                private static bool IsCharArrayType(RuntimeType type)
                    => type.Kind == RuntimeTypeKind.Array &&
                       type.ElementType is not null &&
                       IsCharType(type.ElementType);

                private static bool IsCharPointerType(RuntimeType type)
                    => type.Kind == RuntimeTypeKind.Pointer &&
                       type.ElementType is not null &&
                       IsCharType(type.ElementType);

                private static bool IsSystemStringType(RuntimeType type)
                    => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                       StringComparer.Ordinal.Equals(type.Name, "String");

                private void EmitNullCheck(GenTree node, RVRegister value, string suffix)
                {
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_{suffix}_non_null_{node.LinearId}");
                    EmitLongConditionalBranch(RVInstrKind.Bne, value, RVRegister.X0, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");
                    _owner.DefineLabel(nonNull);
                }

                private void EmitField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Field node has no field metadata");
                    if (field.IsStatic)
                        throw Unsupported(node, "Instance field node references a static field");

                    RuntimeType fieldType = field.FieldType;
                    GenStackKind kind = StackKindForType(fieldType);
                    RVRegister instance = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "field instance"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, instance, "field");

                    switch (node.TreeKind)
                    {
                        case GenTreeKind.FieldAddr:
                            _owner.EmitAddImmediate(
                                ToIntegerRegister(RequireResultRegister(node)),
                                instance,
                                field.Offset);
                            return;

                        case GenTreeKind.Field:
                            _owner.EmitAddImmediate(RVRegister.X28, instance, field.Offset);
                            EmitValueFromAddress(node, fieldType, kind, RVRegister.X28);
                            return;

                        case GenTreeKind.StoreField:
                            _owner.EmitAddImmediate(RVRegister.X28, instance, field.Offset);
                            if (IsContainedDefaultValue(node, 1))
                            {
                                EmitStoreDefaultToAddress(MachineRegister.X28, 0, fieldType, kind);
                                return;
                            }

                            EmitValueToAddress(
                                node,
                                RequireCodegenUseIndexForOperand(node, 1, "field store value"),
                                fieldType,
                                kind,
                                RVRegister.X28);
                            return;

                        default:
                            throw Unsupported(node, $"Unsupported instance field operation {node.TreeKind}");
                    }
                }

                private void EmitStaticField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Static field node has no field metadata");
                    if (!field.IsStatic)
                        throw Unsupported(node, "Static field node references an instance field");

                    RuntimeType fieldType = field.FieldType;
                    GenStackKind kind = StackKindForType(fieldType);
                    _owner.EmitMaterializeAddress(_owner.GetStaticStorageLabel(field.DeclaringType), RVRegister.X28);
                    if (field.Offset != 0)
                        _owner.EmitAddImmediate(RVRegister.X28, RVRegister.X28, field.Offset);

                    switch (node.TreeKind)
                    {
                        case GenTreeKind.StaticFieldAddr:
                            if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                                throw Unsupported(node, "Static field address requires one register result");
                            _owner.EmitMove(ToIntegerRegister(node.Results[0].Register), RVRegister.X28);
                            return;

                        case GenTreeKind.StaticField:
                            EmitValueFromAddress(node, fieldType, kind, RVRegister.X28);
                            return;

                        case GenTreeKind.StoreStaticField:
                            if (IsContainedDefaultValue(node, 0))
                            {
                                if (IsAggregate(fieldType, kind))
                                    EmitZeroAddress(node, RVRegister.X28, StorageSize(fieldType, kind));
                                else
                                    EmitStoreDefaultToAddress(MachineRegister.X28, 0, fieldType, kind);
                                return;
                            }

                            EmitValueToAddress(
                                node,
                                RequireCodegenUseIndexForOperand(node, 0, "static field store value"),
                                fieldType,
                                kind,
                                RVRegister.X28);
                            return;

                        default:
                            throw Unsupported(node, $"Unsupported static field operation {node.TreeKind}");
                    }
                }

                private void EmitZeroAddress(GenTree node, RVRegister address, int size)
                {
                    if (size < 0)
                        throw Unsupported(node, "Static field storage size is negative");
                    for (int offset = 0; offset < size; offset++)
                        EmitMemoryStore(MachineRegister.X0, address, offset, 1);
                }

                private void EmitIndirect(GenTree node)
                {
                    RuntimeType? type = node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.StackKind;
                    if (node.TreeKind == GenTreeKind.LoadIndirect)
                    {
                        RVRegister address = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "indirect load address"));
                        if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                            EmitNullCheck(node, address, "indirect_load");
                        EmitValueFromAddress(node, type, kind, address);
                        return;
                    }

                    RuntimeType storeType = type ?? throw Unsupported(node, "Indirect store has no runtime type");
                    RVRegister destination = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "indirect store address"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitNullCheck(node, destination, "indirect_store");
                    EmitValueToAddress(
                        node,
                        RequireCodegenUseIndexForOperand(node, 1, "indirect store value"),
                        storeType,
                        kind,
                        destination);
                }

                private void EmitPointerElementAddress(GenTree node)
                {
                    if (node.Uses.Length != 2)
                        throw Unsupported(node, "Pointer element address requires base and index operands");
                    int scale = node.Int32 > 0 ? node.Int32 : Math.Max(1, node.RuntimeType?.SizeOf ?? node.Type?.SizeOf ?? 1);
                    RVRegister destination = ToIntegerRegister(RequireResultRegister(node));
                    RVRegister baseRegister = ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "pointer base"));
                    RVRegister index = ToIntegerRegister(RequireUseRegisterForOperand(node, 1, "pointer index"));
                    RuntimeType? indexType = OperandType(node, 1);
                    GenStackKind indexKind = OperandStackKind(node, 1);
                    if (MachineTarget.Is64Bit && IsI4(indexType, indexKind))
                    {
                        if (IsUnsigned(indexType))
                            EmitZeroExtend32(RVRegister.X30, index);
                        else
                            _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, RVRegister.X30, index, 0));
                        index = RVRegister.X30;
                    }

                    if (BitOperations.IsPow2(scale))
                    {
                        int shift = Log2(scale);
                        _owner.Emit(RVInstruction.I(RVInstrKind.Slli, RVRegister.X31, index, shift));
                    }
                    else
                    {
                        RequireM(node);
                        _owner.EmitLoadImmediate(RVRegister.X31, scale);
                        _owner.Emit(RVInstruction.R(RVInstrKind.Mul, RVRegister.X31, index, RVRegister.X31));
                    }
                    _owner.Emit(RVInstruction.R(RVInstrKind.Add, destination, baseRegister, RVRegister.X31));
                }

                private void EmitPointerToByRef(GenTree node)
                {
                    if (node.Uses.Length != 1)
                        throw Unsupported(node, "Pointer-to-byref conversion requires one operand");
                    EmitRegisterMove(
                        RequireResultRegister(node),
                        RequireUseRegisterForOperand(node, 0, "pointer-to-byref operand"),
                        node.Type,
                        node.StackKind);
                }

                private void EmitPointerDifference(GenTree node)
                {
                    if (node.Uses.Length != 2)
                        throw Unsupported(node, "Pointer difference requires two operands");
                    int scale = node.Int32 > 0 ? node.Int32 : 1;
                    RVRegister destination = ToIntegerRegister(RequireResultRegister(node));
                    _owner.Emit(RVInstruction.R(
                        RVInstrKind.Sub,
                        destination,
                        ToIntegerRegister(RequireUseRegisterForOperand(node, 0, "pointer difference left operand")),
                        ToIntegerRegister(RequireUseRegisterForOperand(node, 1, "pointer difference right operand"))));
                    if (scale == 1)
                        return;
                    if (BitOperations.IsPow2(scale))
                    {
                        _owner.Emit(RVInstruction.I(RVInstrKind.Srai, destination, destination, Log2(scale)));
                        return;
                    }
                    RequireM(node);
                    _owner.EmitLoadImmediate(RVRegister.X31, scale);
                    _owner.Emit(RVInstruction.R(RVInstrKind.Div, destination, destination, RVRegister.X31));
                }

                private void EmitMoveBetween(GenTree node, RegisterOperand destination, RegisterOperand source, RuntimeType? type, GenStackKind kind)
                {
                    if (destination.IsRegister && source.IsRegister)
                    {
                        EmitRegisterMove(destination.Register, source.Register, type, kind);
                        return;
                    }
                    if (destination.IsRegister && source.IsFrameSlot)
                    {
                        EmitLoad(destination.Register, source, type, kind);
                        return;
                    }
                    if (destination.IsFrameSlot && source.IsRegister)
                    {
                        EmitStore(destination, source.Register, type, kind);
                        return;
                    }
                    if (destination.IsFrameSlot && source.IsFrameSlot)
                    {
                        EmitMemoryToMemory(destination, source, type, kind);
                        return;
                    }
                    throw Unsupported(node, "Unsupported operand move shape");
                }

                private void EmitRegisterMove(MachineRegister destination, MachineRegister source, RuntimeType? type, GenStackKind kind)
                {
                    RegisterClass destinationClass = MachineRegisters.GetClass(destination);
                    RegisterClass sourceClass = MachineRegisters.GetClass(source);
                    int size = StorageSize(type, kind);

                    if (destinationClass == RegisterClass.General && sourceClass == RegisterClass.General)
                    {
                        _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source));
                        return;
                    }
                    if (destinationClass == RegisterClass.Float && sourceClass == RegisterClass.Float)
                    {
                        RequireFloatingExtension(size);
                        _owner.Emit(RVInstruction.R(
                            size <= 4 ? RVInstrKind.FsgnjS : RVInstrKind.FsgnjD,
                            ToRegister(destination),
                            ToRegister(source),
                            ToRegister(source)));
                        return;
                    }
                    if (destinationClass == RegisterClass.Float && sourceClass == RegisterClass.General)
                    {
                        RequireFloatingExtension(size);
                        if (size <= 4)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvWX, ToRegister(destination), ToIntegerRegister(source), RVRegister.X0));
                        else if (MachineTarget.Is64Bit)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvDX, ToRegister(destination), ToIntegerRegister(source), RVRegister.X0));
                        else
                            throw new NotImplementedException("64-bit integer/floating register bitcasts on RV32 are not implemented.");
                        return;
                    }
                    if (destinationClass == RegisterClass.General && sourceClass == RegisterClass.Float)
                    {
                        RequireFloatingExtension(size);
                        if (size <= 4)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvXW, ToIntegerRegister(destination), ToRegister(source), RVRegister.X0));
                        else if (MachineTarget.Is64Bit)
                            _owner.Emit(RVInstruction.R(RVInstrKind.FmvXD, ToIntegerRegister(destination), ToRegister(source), RVRegister.X0));
                        else
                            throw new NotImplementedException("64-bit integer/floating register bitcasts on RV32 are not implemented.");
                        return;
                    }

                    throw new NotImplementedException("Vector register moves are not implemented.");
                }

                private void EmitLoad(MachineRegister destination, RegisterOperand source, RuntimeType? type, GenStackKind kind)
                {
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Post-LSRA memory operands must be finalized frame slots: {source}.");
                    if (source.IsAddress)
                    {
                        EmitLoadAddress(destination, source);
                        return;
                    }

                    RVRegister baseRegister = FrameBase(source);
                    int offset = EffectiveFrameOffset(source);
                    if (IsAggregate(type, kind))
                    {
                        var abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        if (segments.Length != 1)
                            throw new InvalidOperationException("Aggregate frame load does not have a single ABI register segment.");
                        EmitAggregateSegmentLoad(destination, baseRegister, checked(offset + segments[0].Offset), segments[0]);
                        return;
                    }

                    int size = StorageSize(type, kind, source);
                    if (MachineRegisters.GetClass(destination) == RegisterClass.General &&
                        !CanMoveThroughRegister(RegisterClass.General, size) &&
                        size > 0 && size <= Target.GeneralRegisterSize)
                    {
                        EmitIntegerFragmentLoad(destination, baseRegister, offset, size);
                        return;
                    }

                    EmitMemoryLoad(destination, baseRegister, offset, size, IsSigned(type, kind));
                }

                private void EmitStore(RegisterOperand destination, MachineRegister source, RuntimeType? type, GenStackKind kind)
                {
                    if (!destination.IsFrameSlot)
                        throw new InvalidOperationException($"Post-LSRA memory operands must be finalized frame slots: {destination}.");

                    RVRegister baseRegister = FrameBase(destination);
                    int offset = EffectiveFrameOffset(destination);
                    if (IsAggregate(type, kind))
                    {
                        var abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        if (segments.Length != 1)
                            throw new InvalidOperationException("Aggregate frame store does not have a single ABI register segment.");
                        EmitAggregateSegmentStore(source, baseRegister, checked(offset + segments[0].Offset), segments[0]);
                        return;
                    }

                    int size = StorageSize(type, kind, destination);
                    if (MachineRegisters.GetClass(source) == RegisterClass.General &&
                        !CanMoveThroughRegister(RegisterClass.General, size) &&
                        size > 0 && size <= Target.GeneralRegisterSize)
                    {
                        EmitIntegerFragmentStore(source, baseRegister, offset, size);
                        return;
                    }

                    EmitMemoryStore(source, baseRegister, offset, size);
                }

                private void EmitMemoryToMemory(RegisterOperand destination, RegisterOperand source, RuntimeType? type, GenStackKind kind)
                {
                    int size = StorageSize(type, kind, destination, source);
                    bool floating = destination.RegisterClass == RegisterClass.Float || source.RegisterClass == RegisterClass.Float;
                    RegisterClass registerClass = floating ? RegisterClass.Float : RegisterClass.General;
                    if (IsAggregate(type, kind) || !CanMoveThroughRegister(registerClass, size))
                    {
                        if (!destination.IsFrameSlot || !source.IsFrameSlot)
                            throw new InvalidOperationException("Block-copy operands must be finalized frame slots.");
                        _owner.EmitAddImmediate(RVRegister.X29, FrameBase(destination), EffectiveFrameOffset(destination));
                        _owner.EmitAddImmediate(RVRegister.X28, FrameBase(source), EffectiveFrameOffset(source));
                        EmitBlockCopy(null, RVRegister.X29, RVRegister.X28, size);
                        return;
                    }

                    MachineRegister scratch = floating ? MachineRegister.F29 : MachineRegister.X31;
                    EmitLoad(scratch, source, type, kind);
                    EmitStore(destination, scratch, type, kind);
                }

                private bool CanMoveThroughRegister(RegisterClass registerClass, int size)
                {
                    if (registerClass == RegisterClass.Float)
                        return size == 4 ? MachineTarget.HasF : size == 8 && MachineTarget.HasD;
                    if (registerClass != RegisterClass.General)
                        return false;
                    return size is 1 or 2 or 4 || (size == 8 && MachineTarget.Is64Bit);
                }

                private void EmitLoadAddress(MachineRegister destination, RegisterOperand source)
                {
                    if (source.IsRegister)
                    {
                        _owner.EmitMove(ToIntegerRegister(destination), ToIntegerRegister(source.Register));
                        return;
                    }
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Address source must be a finalized frame slot: {source}.");
                    _owner.EmitAddImmediate(ToIntegerRegister(destination), FrameBase(source), EffectiveFrameOffset(source));
                }

                private void EmitLoadFromAddress(
                    MachineRegister destination,
                    MachineRegister address,
                    int offset,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    int size = StorageSize(type, kind);
                    EmitMemoryLoad(destination, ToIntegerRegister(address), offset, size, IsSigned(type, kind));
                }

                private void EmitStoreToAddress(
                    MachineRegister source,
                    MachineRegister address,
                    int offset,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    int size = StorageSize(type, kind);
                    EmitMemoryStore(source, ToIntegerRegister(address), offset, size);
                }

                private void EmitStoreDefaultToAddress(
                    MachineRegister address,
                    int offset,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    int size = StorageSize(type, kind);
                    if (IsAggregate(type, kind))
                    {
                        RVRegister baseRegister = ToIntegerRegister(address);
                        for (int i = 0; i < size; i++)
                            EmitMemoryStore(MachineRegister.X0, baseRegister, checked(offset + i), 1);
                        return;
                    }
                    if (!IsFloating(type, kind))
                    {
                        EmitMemoryStore(MachineRegister.X0, ToIntegerRegister(address), offset, size);
                        return;
                    }

                    RequireFloatingExtension(size);
                    if (size <= 4)
                        _owner.Emit(RVInstruction.R(RVInstrKind.FmvWX, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                    else if (MachineTarget.Is64Bit)
                        _owner.Emit(RVInstruction.R(RVInstrKind.FmvDX, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                    else
                        _owner.Emit(RVInstruction.R(RVInstrKind.FcvtDW, RVRegister.F29, RVRegister.X0, RVRegister.X0));
                    EmitMemoryStore(MachineRegister.F29, ToIntegerRegister(address), offset, size);
                }

                private void EmitMemoryLoad(MachineRegister destination, RVRegister baseRegister, int offset, int size, bool signed)
                {
                    if (!FitsSignedImmediate(offset, 12))
                    {
                        _owner.EmitAddImmediate(RVRegister.X31, baseRegister, offset);
                        baseRegister = RVRegister.X31;
                        offset = 0;
                    }

                    RVInstrKind opcode;
                    if (MachineRegisters.GetClass(destination) == RegisterClass.Float)
                    {
                        opcode = size switch
                        {
                            4 when MachineTarget.HasF => RVInstrKind.Flw,
                            8 when MachineTarget.HasD => RVInstrKind.Fld,
                            _ => throw new NotImplementedException("Unsupported floating-point load size or ISA extension."),
                        };
                    }
                    else
                    {
                        opcode = size switch
                        {
                            1 => signed ? RVInstrKind.Lb : RVInstrKind.Lbu,
                            2 => signed ? RVInstrKind.Lh : RVInstrKind.Lhu,
                            4 when MachineTarget.Is64Bit => signed ? RVInstrKind.Lw : RVInstrKind.Lwu,
                            4 => RVInstrKind.Lw,
                            8 when MachineTarget.Is64Bit => RVInstrKind.Ld,
                            _ => throw new NotImplementedException($"Unsupported integer load size {size}."),
                        };
                    }

                    _owner.Emit(RVInstruction.I(opcode, ToRegister(destination), baseRegister, offset));
                }

                private void EmitMemoryStore(MachineRegister source, RVRegister baseRegister, int offset, int size)
                {
                    if (!FitsSignedImmediate(offset, 12))
                    {
                        RVRegister addressScratch = ToRegister(source) == RVRegister.X31 ? RVRegister.X30 : RVRegister.X31;
                        _owner.EmitAddImmediate(addressScratch, baseRegister, offset, ToRegister(source));
                        baseRegister = addressScratch;
                        offset = 0;
                    }

                    RVInstrKind opcode;
                    if (MachineRegisters.GetClass(source) == RegisterClass.Float)
                    {
                        opcode = size switch
                        {
                            4 when MachineTarget.HasF => RVInstrKind.Fsw,
                            8 when MachineTarget.HasD => RVInstrKind.Fsd,
                            _ => throw new NotImplementedException("Unsupported floating-point store size or ISA extension."),
                        };
                    }
                    else
                    {
                        opcode = size switch
                        {
                            1 => RVInstrKind.Sb,
                            2 => RVInstrKind.Sh,
                            4 => RVInstrKind.Sw,
                            8 when MachineTarget.Is64Bit => RVInstrKind.Sd,
                            _ => throw new NotImplementedException($"Unsupported integer store size {size}."),
                        };
                    }

                    _owner.Emit(RVInstruction.S(opcode, ToRegister(source), baseRegister, offset));
                }

                private RegisterOperand FrameSlotForLocalLike(GenTree node, RuntimeType? type, GenStackKind kind, RegisterClass registerClass)
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
                        registerClass = IsFloating(type, kind) ? RegisterClass.Float : RegisterClass.General;
                    int size = slot.Size > 0 ? slot.Size : StorageSize(type, kind);
                    return RegisterOperand.ForFrameSlot(
                        registerClass,
                        slot.Kind,
                        _method.StackFrame.UsesFramePointer ? RegisterFrameBase.FramePointer : RegisterFrameBase.StackPointer,
                        slot.Index,
                        slot.Offset,
                        size);
                }

                private RegisterOperand FrameSlotForAddress(GenTree node)
                    => FrameSlotForLocalLike(node, node.RuntimeType ?? node.Type, node.StackKind, RegisterClass.General).AsAddress();

                private RVRegister FrameBase(RegisterOperand operand)
                {
                    return operand.FrameBase switch
                    {
                        RegisterFrameBase.StackPointer => RVRegister.X2,
                        RegisterFrameBase.FramePointer => RVRegister.X8,
                        RegisterFrameBase.IncomingArgumentBase => _method.StackFrame.UsesFramePointer ? RVRegister.X8 : RVRegister.X2,
                        _ => throw new InvalidOperationException($"Invalid frame base {operand.FrameBase}."),
                    };
                }

                private int EffectiveFrameOffset(RegisterOperand operand)
                    => operand.FrameBase == RegisterFrameBase.IncomingArgumentBase
                        ? checked(operand.FrameOffset + _method.StackFrame.FrameSize)
                        : operand.FrameOffset;

                private string LabelForTarget(GenTree node)
                {
                    if ((uint)node.TargetBlockId >= (uint)_blockLabels.Length)
                        throw Unsupported(node, $"Invalid branch target block {node.TargetBlockId}");
                    return _blockLabels[node.TargetBlockId];
                }

                private void EmitJump(string target)
                    => _owner.Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, target));

                private void EmitIntegerR(RVInstrKind opcode, RVRegister destination, RVRegister left, RVRegister right, bool i4)
                {
                    if (i4 && !MachineTarget.Is64Bit)
                    {
                        opcode = opcode switch
                        {
                            RVInstrKind.Addw => RVInstrKind.Add,
                            RVInstrKind.Subw => RVInstrKind.Sub,
                            RVInstrKind.Mulw => RVInstrKind.Mul,
                            RVInstrKind.Divw => RVInstrKind.Div,
                            RVInstrKind.Divuw => RVInstrKind.Divu,
                            RVInstrKind.Remw => RVInstrKind.Rem,
                            RVInstrKind.Remuw => RVInstrKind.Remu,
                            RVInstrKind.Sllw => RVInstrKind.Sll,
                            RVInstrKind.Sraw => RVInstrKind.Sra,
                            RVInstrKind.Srlw => RVInstrKind.Srl,
                            _ => opcode,
                        };
                    }
                    _owner.Emit(RVInstruction.R(opcode, destination, left, right));
                }

                private void EmitLessThan(RVRegister destination, RVRegister left, RVRegister right, bool i4, bool unsigned)
                {
                    if (i4)
                        NormalizeI4ComparisonOperands(ref left, ref right, unsigned);
                    _owner.Emit(RVInstruction.R(unsigned ? RVInstrKind.Sltu : RVInstrKind.Slt, destination, left, right));
                }

                private void NormalizeI4ComparisonOperands(ref RVRegister left, ref RVRegister right, bool unsigned)
                {
                    if (!MachineTarget.Is64Bit)
                        return;

                    if (left == RVRegister.X30)
                    {
                        NormalizeI4ComparisonOperand(RVRegister.X31, left, unsigned);
                        NormalizeI4ComparisonOperand(RVRegister.X30, right, unsigned);
                    }
                    else
                    {
                        NormalizeI4ComparisonOperand(RVRegister.X30, right, unsigned);
                        NormalizeI4ComparisonOperand(RVRegister.X31, left, unsigned);
                    }

                    left = RVRegister.X31;
                    right = RVRegister.X30;
                }

                private void NormalizeI4ComparisonOperand(RVRegister destination, RVRegister source, bool unsigned)
                {
                    if (unsigned)
                        EmitZeroExtend32(destination, source);
                    else
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, destination, source, 0));
                }

                private void EmitZeroExtend32(RVRegister destination, RVRegister source)
                {
                    if (!MachineTarget.Is64Bit)
                    {
                        _owner.EmitMove(destination, source);
                        return;
                    }
                    _owner.Emit(RVInstruction.I(RVInstrKind.Slli, destination, source, 32));
                    _owner.Emit(RVInstruction.I(RVInstrKind.Srli, destination, destination, 32));
                }

                private void CanonicalizeI4(RVRegister register, bool i4)
                {
                    if (i4 && MachineTarget.Is64Bit)
                        _owner.Emit(RVInstruction.I(RVInstrKind.Addiw, register, register, 0));
                }

                private void RequireM(GenTree node)
                {
                    if (!MachineTarget.HasM)
                        throw Unsupported(node, "The operation requires the RISC-V M extension");
                }

                private static RVInstrKind InvertBranch(RVInstrKind branch)
                {
                    return branch switch
                    {
                        RVInstrKind.Beq => RVInstrKind.Bne,
                        RVInstrKind.Bne => RVInstrKind.Beq,
                        RVInstrKind.Blt => RVInstrKind.Bge,
                        RVInstrKind.Bge => RVInstrKind.Blt,
                        RVInstrKind.Bltu => RVInstrKind.Bgeu,
                        RVInstrKind.Bgeu => RVInstrKind.Bltu,
                        _ => throw new ArgumentOutOfRangeException(nameof(branch)),
                    };
                }

                private static bool IsCompareOp(BytecodeOp op)
                    => op is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un;

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

                    var operand = node.Operands[operandIndex];
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

                private static bool IsContainedDefaultValue(GenTree node, int operandIndex)
                {
                    if ((uint)operandIndex >= (uint)node.Operands.Length)
                        return false;
                    if (node.OperandFlags.IsDefaultOrEmpty || operandIndex >= node.OperandFlags.Length ||
                        (node.OperandFlags[operandIndex] & LirOperandFlags.Contained) == 0)
                    {
                        return false;
                    }
                    return node.Operands[operandIndex].Kind == GenTreeKind.DefaultValue;
                }

                private RuntimeType? LocalLikeLoadResultType(GenTree node)
                {
                    if (!node.RegisterResults.IsDefaultOrEmpty)
                    {
                        RuntimeType? type = _method.GetValueInfo(node.RegisterResults[0]).Type;
                        if (type is not null)
                            return type;
                    }
                    return node.RuntimeType ?? node.Type ?? node.LocalDescriptor?.Type;
                }

                private GenStackKind LocalLikeLoadResultKind(GenTree node, RuntimeType? type)
                {
                    if (!node.RegisterResults.IsDefaultOrEmpty)
                        return _method.GetValueInfo(node.RegisterResults[0]).StackKind;
                    return type is not null ? StackKindForType(type) : node.StackKind;
                }

                private RuntimeType? LocalLikeStoreSourceType(GenTree node)
                {
                    if (!node.RegisterUses.IsDefaultOrEmpty)
                    {
                        RuntimeType? type = _method.GetValueInfo(node.RegisterUses[0]).Type;
                        if (type is not null)
                            return type;
                    }
                    return OperandType(node, 0) ?? node.RuntimeType ?? node.Type ?? node.LocalDescriptor?.Type;
                }

                private GenStackKind LocalLikeStoreSourceKind(GenTree node, RuntimeType? type)
                {
                    if (!node.RegisterUses.IsDefaultOrEmpty)
                        return _method.GetValueInfo(node.RegisterUses[0]).StackKind;
                    return type is not null ? StackKindForType(type) : OperandStackKind(node, 0);
                }

                private static RegisterOperand FrameSlotFragment(RegisterOperand slot, AbiRegisterSegment segment)
                {
                    if (!slot.IsFrameSlot)
                        throw new InvalidOperationException($"ABI fragment source is not a finalized frame slot: {slot}.");
                    return RegisterOperand.ForFrameSlot(
                        segment.RegisterClass,
                        slot.FrameSlotKind,
                        slot.FrameBase,
                        slot.FrameSlotIndex,
                        checked(slot.FrameOffset + segment.Offset),
                        segment.Size,
                        slot.IsAddress);
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

                private MachineRegister RequireUseRegisterForOperand(GenTree node, int operandIndex, string context)
                {
                    if (TryGetCodegenUseIndexForOperand(node, operandIndex, out int useIndex) &&
                        (uint)useIndex < (uint)node.Uses.Length &&
                        node.Uses[useIndex].IsRegister)
                    {
                        return node.Uses[useIndex].Register;
                    }

                    throw Unsupported(node, $"{context} has no register use for operand {operandIndex}");
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
                        var flags = i < flagCount ? node.OperandFlags[i] : LirOperandFlags.None;
                        if ((flags & LirOperandFlags.Contained) != 0)
                            continue;

                        var operand = node.Operands[i];
                        GenTree value = operand.RegisterResult ?? operand;
                        var operandValueKey = value.LinearValueKey;
                        int slot = FindCodegenUseSlot(node, operandValueKey, codegenUseCursor);
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
                    if (_method.ValueInfoByNode.TryGetValue(value.LinearValueKey, out var info))
                    {
                        var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind, _method.Target);
                        if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                            return MachineAbi.GetRegisterSegments(abi, _method.Target).Length;
                    }

                    return 1;
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

                private static RVRegister ToRegister(MachineRegister register)
                {
                    if (register == MachineRegister.Invalid)
                        throw new ArgumentOutOfRangeException(nameof(register));
                    return (RVRegister)(byte)register;
                }

                private static RVRegister ToIntegerRegister(MachineRegister register)
                {
                    if (MachineRegisters.GetClass(register) != RegisterClass.General)
                        throw new InvalidOperationException($"Expected an integer register, got {MachineRegisters.Format(register)}.");
                    return ToRegister(register);
                }

                private static GenStackKind StackKindForRegister(MachineRegister register)
                    => MachineRegisters.GetClass(register) == RegisterClass.Float ? GenStackKind.R8 : GenStackKind.NativeInt;

                private static GenStackKind StackKindForSegment(AbiRegisterSegment segment)
                {
                    if (segment.ContainsGcPointers)
                        return GenStackKind.Ref;
                    if (segment.RegisterClass == RegisterClass.Float)
                        return segment.Size <= 4 ? GenStackKind.R4 : GenStackKind.R8;
                    return segment.Size > 4 ? GenStackKind.I8 : GenStackKind.I4;
                }

                internal static GenStackKind StackKindForType(RuntimeType type)
                {
                    if (type.IsReferenceType)
                        return GenStackKind.Ref;
                    return type.PrimitiveKind switch
                    {
                        RuntimePrimitiveKind.Single => GenStackKind.R4,
                        RuntimePrimitiveKind.Double => GenStackKind.R8,
                        RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 => GenStackKind.I8,
                        RuntimePrimitiveKind.NativeInt => GenStackKind.NativeInt,
                        RuntimePrimitiveKind.NativeUInt => GenStackKind.NativeUInt,
                        _ => type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.FunctionPointer ? GenStackKind.Ptr :
                             type.Kind is RuntimeTypeKind.ByRef ? GenStackKind.ByRef :
                             type.IsValueType && type.PrimitiveKind == RuntimePrimitiveKind.None ? GenStackKind.Value : GenStackKind.I4,
                    };
                }

                private static bool IsFloating(RuntimeType? type, GenStackKind kind)
                    => kind is GenStackKind.R4 or GenStackKind.R8 ||
                       type?.PrimitiveKind is RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double;

                private static bool IsI4(RuntimeType? type, GenStackKind kind)
                    => kind == GenStackKind.I4 || type?.PrimitiveKind is
                        RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                        RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or
                        RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
                        RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32;

                private static bool IsI8(RuntimeType? type, GenStackKind kind)
                    => kind == GenStackKind.I8 || type?.PrimitiveKind is RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64;

                private static bool IsUnsigned(RuntimeType? type)
                    => type?.PrimitiveKind is
                        RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                        RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.UInt16 or
                        RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.UInt64 or
                        RuntimePrimitiveKind.NativeUInt;

                private static bool IsSigned(RuntimeType? type, GenStackKind kind)
                {
                    if (kind is GenStackKind.Ref or GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.NativeUInt)
                        return false;
                    if (type is null)
                        return kind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt;
                    return !IsUnsigned(type);
                }

                private static bool IsAggregate(RuntimeType? type, GenStackKind kind)
                    => kind == GenStackKind.Value ||
                       (type is not null &&
                        type.Kind != RuntimeTypeKind.FunctionPointer &&
                        type.IsValueType &&
                        type.PrimitiveKind == RuntimePrimitiveKind.None);

                private void RequireFloatingExtension(int size)
                {
                    if (size <= 4)
                    {
                        if (!MachineTarget.HasF)
                            throw new NotImplementedException("Single-precision floating-point operations require the F extension.");
                        return;
                    }

                    if (!MachineTarget.HasD)
                        throw new NotImplementedException("Double-precision floating-point operations require the D extension.");
                }

                private int StorageSize(RuntimeType? type, GenStackKind kind, params RegisterOperand[] operands)
                {
                    int size = 0;
                    foreach (var operand in operands)
                    {
                        if (operand.FrameSlotSize > size)
                            size = operand.FrameSlotSize;
                    }
                    if (type is not null && type.SizeOf > size)
                        size = type.SizeOf;
                    if (size > 0)
                        return size;
                    return kind switch
                    {
                        GenStackKind.R4 or GenStackKind.I4 => 4,
                        GenStackKind.R8 or GenStackKind.I8 => 8,
                        GenStackKind.Ref or GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Null => Target.PointerSize,
                        _ => Target.GeneralRegisterSize,
                    };
                }

                private static int Log2(int value)
                {
                    int result = 0;
                    while ((value >>= 1) != 0)
                        result++;
                    return result;
                }

                private static bool FitsSignedImmediate(long value, int bits)
                {
                    long min = -(1L << (bits - 1));
                    long max = (1L << (bits - 1)) - 1;
                    return value >= min && value <= max;
                }

                private NotImplementedException Unsupported(GenTree node, string message)
                    => new NotImplementedException(
                        $"{message}. Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}', " +
                        $"block B{node.BlockId}, node {node.LinearId}, kind {node.TreeKind}.");
            }
        }

        private sealed class TextSectionBuilder
        {
            private readonly List<RVInstruction> _instructions = new List<RVInstruction>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<RVObjectRelocation> _relocations = new List<RVObjectRelocation>();

            public string Name { get; }
            public int ByteLength => checked(_instructions.Count * 4);

            public TextSectionBuilder(string name)
            {
                Name = name;
            }

            public void DefineLabel(string label)
            {
                if (!_labels.TryAdd(label, ByteLength))
                    throw new InvalidOperationException($"Duplicate text label: {label}.");
            }

            public void Emit(RVInstruction instruction)
                => _instructions.Add(instruction);

            public void AddRelocation(int offset, string symbol, int addend, RVObjectRelocationKind kind)
                => _relocations.Add(new RVObjectRelocation(Name, offset, symbol, addend, kind));

            public RVTextSection ToSection()
                => new RVTextSection(_instructions, _labels, _relocations.ToImmutableArray());
        }

        private sealed class DataSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly List<RVObjectRelocation> _relocations = new List<RVObjectRelocation>();

            public string Name { get; }
            public RVObjectSectionKind Kind { get; }
            public int ByteLength => _data.Count;
            public int Alignment { get; private set; } = 1;

            public DataSectionBuilder(string name, RVObjectSectionKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public int Align(int alignment)
            {
                alignment = Math.Max(1, alignment);
                Alignment = Math.Max(Alignment, alignment);
                int aligned = AlignUp(ByteLength, alignment);
                while (_data.Count < aligned)
                    _data.Add(0);
                return aligned;
                static int AlignUp(int value, int alignment)
                {
                    int remainder = value % alignment;
                    return remainder == 0 ? value : checked(value + alignment - remainder);
                }
            }

            public void EmitBytes(byte[] bytes)
            {
                if (bytes is null)
                    throw new ArgumentNullException(nameof(bytes));
                _data.AddRange(bytes);
            }

            public void AddRelocation(int offset, string symbol, int addend, RVObjectRelocationKind kind)
                => _relocations.Add(new RVObjectRelocation(Name, offset, symbol, addend, kind));

            public RVDataSection ToSection()
                => new RVDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());

        }
    }
}
