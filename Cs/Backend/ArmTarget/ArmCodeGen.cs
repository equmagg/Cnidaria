using Cnidaria.Arm;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    public sealed class ArmCodeGeneratorOptions
    {
        public static ArmCodeGeneratorOptions Default => new ArmCodeGeneratorOptions();

        public int EntryMethodId { get; set; } = -1;
        public bool EmitStartup { get; set; } = true;
        public bool MarkMethodsCodeGenerated { get; set; } = true;
        public bool EmbedRuntime { get; set; } = true;
        public bool TrimRuntime { get; set; } = true;
        public Func<RuntimeMethod, string>? InternalCallSymbolResolver { get; set; }
        public Func<RuntimeMethod, string>? ExternalSymbolResolver { get; set; }
    }

    internal static class ArmCodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";

        public static ArmProgram Build(
            GenTreeProgram program,
            ArmCodeGeneratorOptions? options = null,
            TargetInfo? target = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= ArmCodeGeneratorOptions.Default;
            target ??= program.Target;
            if (target.Architecture != TargetArchitectureKind.Arm64)
                throw new ArgumentException("ARM code generation requires an Arm64 target.", nameof(target));
            if (target.Endianness != TargetEndianness.Little)
                throw new NotImplementedException("Big-endian ARM code generation is not implemented.");

            var allocatedTarget = program.Target;
            if (allocatedTarget.Architecture != target.Architecture ||
                allocatedTarget.PointerSize != target.PointerSize ||
                allocatedTarget.GeneralRegisterSize != target.GeneralRegisterSize)
            {
                throw new ArgumentException("ARM code generation target is ABI-incompatible with the LSRA input.", nameof(target));
            }

            return new Generator(program, target, ArmTarget.FromTargetInfo(target), options).Generate();
        }

        private static ArmRegister ToArm(MachineRegister register)
        {
            int index = (int)register;
            if (index >= (int)MachineRegister.X0 && index <= (int)MachineRegister.X30)
                return (ArmRegister)((int)ArmRegister.X0 + index);
            if (register == MachineRegister.X31)
                return ArmRegister.Sp;
            if (index >= (int)MachineRegister.F0 && index <= (int)MachineRegister.F31)
                return (ArmRegister)((int)ArmRegister.V0 + (index - (int)MachineRegister.F0));
            throw new ArgumentOutOfRangeException(nameof(register), $"Register {MachineRegisters.Format(register)} has no ARM64 encoding.");
        }

        private sealed class Generator
        {
            private readonly GenTreeProgram _program;
            private readonly TargetInfo _target;
            private readonly ArmTarget _machineTarget;
            private readonly ArmCodeGeneratorOptions _options;
            private readonly TextSectionBuilder _text = new TextSectionBuilder(TextSectionName);
            private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, ArmObjectSectionKind.Rodata);
            private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, ArmObjectSectionKind.Data);
            private readonly List<ArmObjectSymbol> _symbols = new List<ArmObjectSymbol>();
            private readonly Dictionary<int, string> _methodLabels = new Dictionary<int, string>();
            private readonly Dictionary<int, GenTreeMethod> _methodsById = new Dictionary<int, GenTreeMethod>();
            private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _externalSymbols = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> _typeDescriptorLabels = new Dictionary<int, string>();
            private readonly List<TypeDescriptorDraft> _typeDescriptors = new List<TypeDescriptorDraft>();
            private readonly Dictionary<string, string> _stringLiteralLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<StringLiteralDraft> _stringLiterals = new List<StringLiteralDraft>();
            private readonly Dictionary<int, StaticExceptionDraft> _staticExceptionsByTypeId = new Dictionary<int, StaticExceptionDraft>();
            private readonly List<StaticExceptionDraft> _staticExceptions = new List<StaticExceptionDraft>();
            private readonly List<SafePointDraft> _safePoints = new List<SafePointDraft>();
            private readonly Dictionary<int, StaticStorageDraft> _staticStorageByTypeId = new Dictionary<int, StaticStorageDraft>();
            private readonly Dictionary<int, TypeInitializationThunkDraft> _typeInitializationThunksByTypeId = new Dictionary<int, TypeInitializationThunkDraft>();
            private readonly List<TypeInitializationThunkDraft> _typeInitializationThunks = new List<TypeInitializationThunkDraft>();
            private readonly List<StaticRootDraft> _staticRoots = new List<StaticRootDraft>();
            private readonly List<InterfaceDispatchCellDraft> _interfaceDispatchCells = new List<InterfaceDispatchCellDraft>();
            private readonly Dictionary<int, string> _unboxingStubLabels = new Dictionary<int, string>();
            private readonly List<UnboxingStubDraft> _unboxingStubs = new List<UnboxingStubDraft>();
            private readonly Dictionary<int, string> _virtualDispatchMethodLabels = new Dictionary<int, string>();
            private string? _virtualDispatchFailureStubLabel;
            private bool _virtualDispatchMetadataPrepared;
            private readonly Dictionary<int, EhMethodDraft> _ehMethodsByMethodId = new Dictionary<int, EhMethodDraft>();
            private readonly List<EhMethodDraft> _ehMethods = new List<EhMethodDraft>();
            private readonly Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft> _delegateTargetThunksByKey =
                new Dictionary<DelegateTargetThunkKey, DelegateTargetThunkDraft>();
            private readonly List<DelegateTargetThunkDraft> _delegateTargetThunks = new List<DelegateTargetThunkDraft>();
            private bool _metadataSealed;
            private int _nextLocalLabel;

            public Generator(GenTreeProgram program, TargetInfo target, ArmTarget machineTarget, ArmCodeGeneratorOptions options)
            {
                _program = program;
                _target = target;
                _machineTarget = machineTarget;
                _options = options;
            }

            public TargetInfo Target => _target;
            public bool EmbedsRuntime => _options.EmbedRuntime;

            public ArmProgram Generate()
            {
                IndexMethods();
                GenTreeMethod entryMethod = SelectEntryMethod();
                string entryLabel = _methodLabels[entryMethod.RuntimeMethod.MethodId];

                foreach (var method in _program.Methods)
                    EmitMethod(method);

                RuntimeMethod? entryTypeInitializer = SelectEntryTypeInitializer(entryMethod);
                if (entryTypeInitializer is not null)
                    _ = GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType);

                EmitTypeInitializationThunks();
                EmitDelegateTargetThunks();
                if (_options.EmbedRuntime)
                    EmitEhTransferHelper();

                PrepareVirtualDispatchMetadata();
                EmitUnboxingStubs();
                EmitVirtualDispatchFailureStub();
                RuntimeMetadataLabels metadata = EmitRuntimeMetadata();

                if (_options.EmitStartup)
                    entryLabel = EmitStartup(entryMethod, entryLabel, metadata, entryTypeInitializer);

                _symbols.Add(new ArmObjectSymbol(
                    TextSectionName, TextSectionName, 0, _text.ByteLength,
                    ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));

                var dataSections = ImmutableArray.CreateBuilder<ArmDataSection>(2);
                if (_rodata.ByteLength != 0)
                {
                    dataSections.Add(_rodata.ToSection());
                    _symbols.Add(new ArmObjectSymbol(
                        RodataSectionName, RodataSectionName, 0, _rodata.ByteLength,
                        ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
                }
                if (_data.ByteLength != 0)
                {
                    dataSections.Add(_data.ToSection());
                    _symbols.Add(new ArmObjectSymbol(
                        DataSectionName, DataSectionName, 0, _data.ByteLength,
                        ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
                }

                var result = new ArmProgram(
                    _machineTarget,
                    _text.ToSection(),
                    dataSections.ToImmutable(),
                    _symbols.ToImmutableArray(),
                    entryLabel);

                if (_options.EmbedRuntime && _target.OperatingSystem is OperatingSystemKind.Linux or OperatingSystemKind.Windows)
                {
                    ArmProgram runtime = ArmRuntime.GetObject(_target);
                    if (_options.TrimRuntime)
                        runtime = ArmRuntime.Trim(runtime, CollectUndefinedSymbols(result));
                    result = ArmObjectComposer.Compose(result, runtime);
                }

                return result;
            }

            private static List<string> CollectUndefinedSymbols(ArmProgram managed)
            {
                var defined = new HashSet<string>(StringComparer.Ordinal);
                foreach (var label in managed.Text.Labels)
                    defined.Add(label.Key);
                foreach (var symbol in managed.Symbols)
                {
                    if (symbol.Binding != ArmObjectSymbolBinding.External && symbol.Name.Length != 0)
                        defined.Add(symbol.Name);
                }

                var undefined = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                void Add(string name)
                {
                    if (name.Length != 0 && !defined.Contains(name) && seen.Add(name))
                        undefined.Add(name);
                }

                foreach (var symbol in managed.Symbols)
                {
                    if (symbol.Binding == ArmObjectSymbolBinding.External)
                        Add(symbol.Name);
                }
                foreach (var relocation in managed.Text.Relocations)
                    Add(relocation.SymbolName);
                foreach (var section in managed.DataSections)
                {
                    foreach (var relocation in section.Relocations)
                        Add(relocation.SymbolName);
                }

                return undefined;
            }

            private void IndexMethods()
            {
                foreach (var method in _program.Methods)
                {
                    if (method.Phase < GenTreeMethodPhase.RegisterAllocated)
                        throw new InvalidOperationException("ARM code generation requires LSRA-annotated LIR.");

                    int methodId = method.RuntimeMethod.MethodId;
                    if (!_methodsById.TryAdd(methodId, method))
                        throw new InvalidOperationException($"Duplicate method in ARM code generation input: M{methodId}.");

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
                                if (_program.TypeSystem is null)
                                    throw new InvalidOperationException("ARM code generation requires a runtime type system for exception metadata.");
                                catchTypeLabel = GetTypeDescriptorLabel(_program.TypeSystem.ResolveTypeInMethodContext(
                                    method.RuntimeMethod.BodyModule ?? method.Module,
                                    region.CatchTypeToken,
                                    method.RuntimeMethod));
                            }
                            break;
                        case CfgExceptionRegionKind.Finally:
                            kind = 3;
                            break;
                        case CfgExceptionRegionKind.Fault:
                            kind = 4;
                            break;
                        default:
                            throw new NotSupportedException(
                                $"Exception region kind {region.Kind} is not supported in method M{method.RuntimeMethod.MethodId} '{method.RuntimeMethod.Name}'.");
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
                    throw new InvalidOperationException("Cannot generate an ARM program without methods.");

                if (_options.EntryMethodId >= 0)
                {
                    if (_methodsById.TryGetValue(_options.EntryMethodId, out var selected))
                        return selected;
                    throw new InvalidOperationException($"Entry method M{_options.EntryMethodId} is not present in the generated program.");
                }

                GenTreeMethod? firstStaticParameterless = null;
                foreach (var method in _program.Methods)
                {
                    var runtimeMethod = method.RuntimeMethod;
                    if (runtimeMethod.IsStatic && runtimeMethod.ParameterTypes.Length == 0 && firstStaticParameterless is null)
                        firstStaticParameterless = method;
                    if (runtimeMethod.IsStatic && StringComparer.Ordinal.Equals(runtimeMethod.Name, "Main"))
                        return method;
                    if (StringComparer.Ordinal.Equals(runtimeMethod.Name, "<Main>$"))
                        return method;
                }

                return firstStaticParameterless ?? _program.Methods[0];
            }

            private void EmitMethod(GenTreeMethod method)
            {
                string label = _methodLabels[method.RuntimeMethod.MethodId];
                int startOffset = _text.ByteLength;
                _text.DefineLabel(label);

                new MethodEmitter(this, method, label).Emit();

                _symbols.Add(new ArmObjectSymbol(
                    label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Global, ArmObjectSymbolKind.Function));

                if (_options.MarkMethodsCodeGenerated)
                    method.SetPhase(GenTreeMethodPhase.CodeGenerated);
            }

            // A type whose initializer the entry method would otherwise never trigger still owes it
            private RuntimeMethod? SelectEntryTypeInitializer(GenTreeMethod entryMethod)
            {
                RuntimeMethod method = entryMethod.RuntimeMethod;
                if (!_options.EmitStartup ||
                    StringComparer.Ordinal.Equals(method.Name, ".cctor") ||
                    method.DeclaringType.IsBeforeFieldInit ||
                    !(method.IsStatic || method.DeclaringType.IsValueType || StringComparer.Ordinal.Equals(method.Name, ".ctor")))
                {
                    return null;
                }

                RuntimeMethod? initializer = FindTypeInitializer(method.DeclaringType);
                if (initializer is not null)
                    _ = GetTypeInitializationStateLabel(method.DeclaringType);
                return initializer;
            }

            private string EmitStartup(
                GenTreeMethod entryMethod,
                string entryMethodLabel,
                RuntimeMetadataLabels metadata,
                RuntimeMethod? entryTypeInitializer)
            {
                RuntimeMethod runtimeMethod = entryMethod.RuntimeMethod;
                if (!runtimeMethod.IsStatic)
                    throw new NotImplementedException("Startup for an instance entry method is not implemented.");
                if (runtimeMethod.ParameterTypes.Length > 1 ||
                    (runtimeMethod.ParameterTypes.Length == 1 && !IsStringArray(runtimeMethod.ParameterTypes[0])))
                {
                    throw new NotImplementedException("Startup currently supports only parameterless or string[] managed entry methods.");
                }
                if (_target.OperatingSystem is not OperatingSystemKind.None and not OperatingSystemKind.Linux)
                    throw new NotImplementedException($"Startup for {_target.OperatingSystem} is not implemented.");

                string label = CreateUniqueGlobalLabel("_start");
                int startOffset = _text.ByteLength;
                _text.DefineLabel(label);

                EmitMove(ArmRegister.X29, ArmRegister.Xzr, 8);
                EmitMove(ArmRegister.X0, ArmRegister.Sp, 8);

                // A logical instruction reads register 31 as zero, so the stack pointer has to travel
                // through a general register to be aligned
                EmitMove(ArmRegister.X16, ArmRegister.Sp, 8);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.And,
                    Reg(ArmRegister.X16, 8),
                    Reg(ArmRegister.X16, 8),
                    ArmOperand.ImmediateOperand(-_target.CallFrameAlignment)));
                EmitMove(ArmRegister.Sp, ArmRegister.X16, 8);

                if (_options.EmbedRuntime && _target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitMaterializeAddress(metadata.SafePointTableLabel, ArmRegister.X1);
                    EmitLoadImmediate(ArmRegister.X2, metadata.SafePointCount, 8);
                    EmitMaterializeAddress(metadata.TypeInfoTableLabel, ArmRegister.X3);
                    EmitLoadImmediate(ArmRegister.X4, metadata.TypeInfoCount, 8);
                    EmitMaterializeAddress(metadata.StaticRootTableLabel, ArmRegister.X5);
                    EmitLoadImmediate(ArmRegister.X6, metadata.StaticRootCount, 8);
                    EmitCall(ResolveExternalSymbol(ArmRuntime.InitializeSymbol));
                    if (entryTypeInitializer is not null)
                        EmitCall(GetTypeInitializationThunkLabel(entryTypeInitializer.DeclaringType));
                }

                if (runtimeMethod.ParameterTypes.Length == 1)
                    EmitLoadImmediate(ArmRegister.X0, 0, 8);
                EmitCall(entryMethodLabel);

                if (IsVoid(runtimeMethod.ReturnType))
                    EmitLoadImmediate(ArmRegister.X0, 0, 8);

                if (_target.OperatingSystem == OperatingSystemKind.Linux)
                {
                    EmitLoadImmediate(ArmRegister.X8, 93, 8);
                    Emit(ArmInstruction.Unary(ArmInstrKind.Svc, ArmOperand.ImmediateOperand(0)));
                }

                Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(0)));
                _symbols.Add(new ArmObjectSymbol(
                    label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Global, ArmObjectSymbolKind.Function));
                return label;
            }

            // The runtime resumes a handler through this helper: it reloads the frame it unwound to
            // and jumps, so it never returns
            private void EmitEhTransferHelper()
            {
                string label = ArmRuntime.EhTransferSymbol;
                if (!_usedLabels.Add(label))
                    throw new InvalidOperationException($"Duplicate ARM EH transfer symbol: {label}.");

                int startOffset = _text.ByteLength;
                _text.DefineLabel(label);
                EmitMove(ArmRegister.X16, ArmRegister.X0, 8);
                EmitMove(ArmRegister.X17, ArmRegister.X1, 8);

                var generalRegisters = RegisterInfo.AllocatableGeneralRegisters(_target);
                for (int i = 0; i < generalRegisters.Length; i++)
                {
                    ArmRegister register = ToArm(generalRegisters[i]);
                    if (register == ArmRegister.X2)
                        continue;
                    Emit(ArmInstruction.Binary(
                        ArmInstrKind.Ldr,
                        Reg(register, 8),
                        Mem(ArmRegister.X2, (byte)generalRegisters[i] * 8, 8)));
                }

                var floatRegisters = RegisterInfo.AllocatableFloatingRegisters(_target);
                for (int i = 0; i < floatRegisters.Length; i++)
                {
                    int index = (byte)floatRegisters[i] - (byte)MachineRegister.F0;
                    Emit(ArmInstruction.Binary(
                        ArmInstrKind.Ldr,
                        Reg(ToArm(floatRegisters[i]), 8),
                        Mem(ArmRegister.X2, 256 + index * 8, 8)));
                }

                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Ldr,
                    Reg(ArmRegister.X2, 8),
                    Mem(ArmRegister.X2, (byte)MachineRegister.X2 * 8, 8)));
                EmitMove(ArmRegister.Sp, ArmRegister.X16, 8);
                EmitMove(ArmRegister.X29, ArmRegister.X16, 8);
                Emit(ArmInstruction.Unary(ArmInstrKind.Br, Reg(ArmRegister.X17, 8)));

                _symbols.Add(new ArmObjectSymbol(
                    label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Global, ArmObjectSymbolKind.Function));
            }

            public string ResolveMethodLabel(RuntimeMethod method)
            {
                if (method.HasInternalCall)
                {
                    string? internalCallLabel = _options.InternalCallSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(internalCallLabel))
                        internalCallLabel = ArmRuntime.ResolveInternalCall(method);
                    return ResolveExternalSymbol(internalCallLabel);
                }

                if (method.IsExtern)
                {
                    string? externalLabel = _options.ExternalSymbolResolver?.Invoke(method);
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = method.DllImportData?.EntryPointName;
                    if (string.IsNullOrWhiteSpace(externalLabel))
                        externalLabel = method.Name;
                    return ResolveExternalSymbol(externalLabel);
                }

                if (_methodLabels.TryGetValue(method.MethodId, out string? label))
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
                    _symbols.Add(new ArmObjectSymbol(
                        label, string.Empty, 0, 0, ArmObjectSymbolBinding.External, ArmObjectSymbolKind.Function));
                }

                return label;
            }

            public string ResolveExternalObjectSymbol(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("External symbol name is empty.", nameof(label));

                if (_externalSymbols.Add(label))
                {
                    _symbols.Add(new ArmObjectSymbol(
                        label, string.Empty, 0, 0, ArmObjectSymbolBinding.External, ArmObjectSymbolKind.Object));
                }

                return label;
            }

            public string AddConstantData(byte[] bytes, int alignment, string prefix)
            {
                string label = CreateLocalLabel(prefix);
                int offset = _rodata.Align(alignment);
                _rodata.EmitBytes(bytes);
                _symbols.Add(new ArmObjectSymbol(
                    label, RodataSectionName, offset, bytes.Length,
                    ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Object));
                return label;
            }

            public string CreateLocalLabel(string prefix)
            {
                string baseName = ".L" + SanitizeSymbolName(prefix);
                string candidate;
                do
                {
                    candidate = $"{baseName}_{++_nextLocalLabel}";
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
                    candidate = $"{baseName}_{++suffix}";
                return candidate;
            }

            public void Emit(ArmInstruction instruction) => _text.Emit(instruction);

            public void DefineLabel(string label) => _text.DefineLabel(label);

            public void EmitCall(string symbol)
            {
                int offset = _text.ByteLength;
                Emit(ArmInstruction.Branch(ArmInstrKind.Bl, symbol));
                _text.AddRelocation(offset, symbol, 0, ArmObjectRelocationKind.AArch64Call26);
            }

            public void EmitJump(string symbol)
            {
                int offset = _text.ByteLength;
                Emit(ArmInstruction.Branch(ArmInstrKind.B, symbol));
                _text.AddRelocation(offset, symbol, 0, ArmObjectRelocationKind.AArch64Branch26);
            }

            public void EmitConditionalJump(ArmCondition condition, string symbol)
            {
                int offset = _text.ByteLength;
                Emit(ArmInstruction.Branch(ArmInstrKind.B, symbol, condition));
                _text.AddRelocation(offset, symbol, 0, ArmObjectRelocationKind.AArch64ConditionalBranch19);
            }

            public void EmitMaterializeAddress(string symbol, ArmRegister destination)
            {
                int adrpOffset = _text.ByteLength;
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Adrp,
                    Reg(destination, 8),
                    ArmOperand.SymbolOperand(symbol, ArmRelocationKind.Adrp)));
                _text.AddRelocation(adrpOffset, symbol, 0, ArmObjectRelocationKind.AArch64Adrp21);

                int addOffset = _text.ByteLength;
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Add,
                    Reg(destination, 8),
                    Reg(destination, 8),
                    ArmOperand.ImmediateOperand(0)));
                _text.AddRelocation(addOffset, symbol, 0, ArmObjectRelocationKind.AArch64AddLow12);
            }

            public void EmitMove(ArmRegister destination, ArmRegister source, int size)
            {
                if (destination == source)
                    return;

                if (destination == ArmRegister.Sp || source == ArmRegister.Sp)
                {
                    Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(destination, size), Reg(source, size), ArmOperand.ImmediateOperand(0)));
                    return;
                }

                Emit(ArmInstruction.Binary(ArmInstrKind.Mov, Reg(destination, size), Reg(source, size)));
            }

            public void EmitLoadImmediate(ArmRegister destination, long value, int size)
            {
                ulong bits = size == 4 ? unchecked((uint)value) : unchecked((ulong)value);
                int width = size == 4 ? 32 : 64;

                // movn covers the negatives that would otherwise need a chunk apiece
                ulong inverted = width == 32 ? ~bits & 0xFFFFFFFFUL : ~bits;
                if (NonZeroChunks(inverted, width) == 1)
                {
                    for (int shift = 0; shift < width; shift += 16)
                    {
                        ushort part = (ushort)((inverted >> shift) & 0xFFFF);
                        if (part == 0)
                            continue;
                        Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Movn,
                            Reg(destination, size),
                            ArmOperand.ImmediateOperand(part),
                            ArmOperand.ImmediateOperand(shift)));
                        return;
                    }
                }

                bool first = true;
                for (int shift = 0; shift < width; shift += 16)
                {
                    ushort part = (ushort)((bits >> shift) & 0xFFFF);
                    if (part == 0 && !first)
                        continue;
                    Emit(ArmInstruction.Ternary(
                        first ? ArmInstrKind.Movz : ArmInstrKind.Movk,
                        Reg(destination, size),
                        ArmOperand.ImmediateOperand(part),
                        ArmOperand.ImmediateOperand(shift)));
                    first = false;
                }

                if (first)
                {
                    Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Movz,
                        Reg(destination, size),
                        ArmOperand.ImmediateOperand(0),
                        ArmOperand.ImmediateOperand(0)));
                }
            }

            private static int NonZeroChunks(ulong value, int width)
            {
                int count = 0;
                for (int shift = 0; shift < width; shift += 16)
                {
                    if (((value >> shift) & 0xFFFF) != 0)
                        count++;
                }
                return count;
            }

            public void EmitAddImmediate(ArmRegister destination, ArmRegister source, long immediate, ArmRegister scratch)
            {
                if (immediate == 0)
                {
                    EmitMove(destination, source, 8);
                    return;
                }

                long magnitude = Math.Abs(immediate);
                ArmInstrKind opcode = immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add;
                if (magnitude <= 4095)
                {
                    Emit(ArmInstruction.Ternary(opcode, Reg(destination, 8), Reg(source, 8), ArmOperand.ImmediateOperand(magnitude)));
                    return;
                }

                if ((magnitude & 0xFFF) == 0 && (magnitude >> 12) <= 4095)
                {
                    Emit(ArmInstruction.Ternary(opcode, Reg(destination, 8), Reg(source, 8), ArmOperand.ImmediateOperand(magnitude)));
                    return;
                }

                EmitLoadImmediate(scratch, magnitude, 8);
                Emit(ArmInstruction.Ternary(opcode, Reg(destination, 8), Reg(source, 8), Reg(scratch, 8)));
            }

            public void EmitAdjustStack(int delta)
            {
                if (delta == 0)
                    return;

                long remaining = Math.Abs((long)delta);
                ArmInstrKind opcode = delta < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add;
                while (remaining != 0)
                {
                    long part = Math.Min(remaining, 4095);
                    Emit(ArmInstruction.Ternary(
                        opcode, Reg(ArmRegister.Sp, 8), Reg(ArmRegister.Sp, 8), ArmOperand.ImmediateOperand(part)));
                    remaining -= part;
                }
            }

            public static ArmOperand Reg(ArmRegister register, int size)
                => ArmOperand.RegisterOperand(register, size);

            public static ArmOperand Mem(ArmRegister baseRegister, long displacement, int size)
                => ArmOperand.Memory(baseRegister, displacement, size);

            private static bool IsStringArray(RuntimeType type)
                => type.Kind == RuntimeTypeKind.Array &&
                   type.IsSzArray &&
                   type.ElementType is not null &&
                   StringComparer.Ordinal.Equals(type.ElementType.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(type.ElementType.Name, "String");

            private static bool IsVoid(RuntimeType? type)
                => type is null || type.PrimitiveKind == RuntimePrimitiveKind.Void ||
                   (StringComparer.Ordinal.Equals(type.Namespace, "System") && StringComparer.Ordinal.Equals(type.Name, "Void"));

            private static string FormatMethodSymbol(RuntimeMethod method)
                => $"{method.DeclaringType.Namespace}_{method.DeclaringType.Name}_{method.Name}_M{method.MethodId}";

            private static string SanitizeSymbolName(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return string.Empty;

                var chars = new char[name.Length];
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    chars[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
                }
                return new string(chars);
            }

            private readonly struct RuntimeMetadataLabels
            {
                public readonly string SafePointTableLabel;
                public readonly int SafePointCount;
                public readonly string TypeInfoTableLabel;
                public readonly int TypeInfoCount;
                public readonly string StaticRootTableLabel;
                public readonly int StaticRootCount;

                public RuntimeMetadataLabels(
                    string safePointTableLabel,
                    int safePointCount,
                    string typeInfoTableLabel,
                    int typeInfoCount,
                    string staticRootTableLabel,
                    int staticRootCount)
                {
                    SafePointTableLabel = safePointTableLabel;
                    SafePointCount = safePointCount;
                    TypeInfoTableLabel = typeInfoTableLabel;
                    TypeInfoCount = typeInfoCount;
                    StaticRootTableLabel = staticRootTableLabel;
                    StaticRootCount = staticRootCount;
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

                public override bool Equals(object? obj) => obj is DelegateTargetThunkKey other && Equals(other);

                public override int GetHashCode() => HashCode.Combine(DelegateTypeId, TargetMethodId, Closed);
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

            internal readonly struct DelegateAbiSlice
            {
                public readonly AbiArgumentLocation Location;
                public readonly RegisterClass RegisterClass;
                public readonly int ValueOffset;
                public readonly int Size;
                public readonly int SaveOffset;

                public bool IsGeneralWord => RegisterClass == RegisterClass.General && Size == 4;

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

            internal readonly struct DelegateAbiEntity
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

            internal readonly struct DelegateAbiBundle
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

                public EhClauseDraft(CfgExceptionRegion region, int kind, string? catchTypeLabel, int parentLocalIndex)
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

            internal readonly struct SafePointRootDraft
            {
                public readonly int FrameOffset;
                public readonly RegisterGcRootKind Kind;

                public SafePointRootDraft(int frameOffset, RegisterGcRootKind kind)
                {
                    FrameOffset = frameOffset;
                    Kind = kind;
                }
            }

            internal sealed class SafePointDraft
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

            public SafePointDraft AddSafePoint(
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

            public string GetTypeDescriptorLabel(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_typeDescriptorLabels.TryGetValue(type.TypeId, out string? existing))
                    return existing;
                if (_metadataSealed || _virtualDispatchMetadataPrepared)
                    throw new InvalidOperationException("A MethodTable was requested after the ARM runtime metadata was written.");
                if (type.Kind == RuntimeTypeKind.TypeParam)
                    throw new NotSupportedException("Open generic parameters do not have standalone MethodTables.");

                _program.TypeSystem?.EnsureConstructedMembers(type);
                _program.TypeSystem?.EnsureVirtualTable(type);
                string label = CreateLocalLabel($"type_{type.TypeId}");
                _typeDescriptorLabels.Add(type.TypeId, label);

                var fields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                var componentFields = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                if (type.Kind == RuntimeTypeKind.Array)
                {
                    AppendTypedGcFields(
                        componentFields,
                        0,
                        type.ElementType ?? throw new InvalidOperationException("Array runtime type has no element type."));
                }
                else if (type.IsValueType)
                {
                    AppendTypedGcFields(fields, _target.ManagedObjectHeaderSize, type);
                }
                else
                {
                    AppendObjectGcFields(fields, type);
                }

                ImmutableArray<RuntimeType> interfaces = CollectImplementedInterfaces(type);
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

            // A vtable slot that cannot be resolved to real code still needs an address, and the stub it
            // gets is the one that reports the hole instead of jumping into nothing
            private void PrepareVirtualDispatchMetadata()
            {
                RuntimeTypeSystem? typeSystem = _program.TypeSystem;
                if (_interfaceDispatchCells.Count != 0 && typeSystem is null)
                    throw new InvalidOperationException("Interface dispatch metadata requires a runtime type system.");

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
                        RuntimeType receiverType = _typeDescriptors[t].Type;
                        if (receiverType.Kind == RuntimeTypeKind.Interface ||
                            (!receiverType.IsReferenceType && !receiverType.IsValueType))
                        {
                            continue;
                        }

                        RuntimeMethod? target = typeSystem!.ResolveVirtualMethod(cell.DeclaredMethod, receiverType);
                        if (target is null)
                            continue;
                        entries.Add(new InterfaceDispatchEntryDraft(
                            _typeDescriptors[t].Label,
                            GetVirtualDispatchTargetLabel(target)));
                    }
                    cell.Entries = entries.ToImmutable();
                }

                _virtualDispatchMetadataPrepared = true;
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
                            internalCallLabel = ArmRuntime.ResolveInternalCall(target);
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

            public string GetVirtualDispatchFailureStubLabel()
                => _virtualDispatchFailureStubLabel ??= CreateLocalLabel("virtual_dispatch_failure");

            // A virtual slot on a value type is entered with a boxed receiver, so the payload address is
            // what the target actually wants
            private void EmitUnboxingStubs()
            {
                for (int i = 0; i < _unboxingStubs.Count; i++)
                {
                    UnboxingStubDraft stub = _unboxingStubs[i];
                    int startOffset = _text.ByteLength;
                    _text.DefineLabel(stub.Label);
                    EmitAddImmediate(ArmRegister.X0, ArmRegister.X0, _target.ManagedObjectHeaderSize, ArmRegister.X16);
                    EmitJump(stub.TargetLabel);
                    _symbols.Add(new ArmObjectSymbol(
                        stub.Label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                        ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Function));
                }
            }

            private void EmitVirtualDispatchFailureStub()
            {
                if (_virtualDispatchFailureStubLabel is null)
                    return;

                int startOffset = _text.ByteLength;
                _text.DefineLabel(_virtualDispatchFailureStubLabel);
                if (_options.EmbedRuntime)
                {
                    EmitLoadImmediate(ArmRegister.X0, 151, 8);
                    EmitCall(ResolveExternalSymbol(ArmRuntime.FailFastSymbol));
                }
                Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(1)));

                _symbols.Add(new ArmObjectSymbol(
                    _virtualDispatchFailureStubLabel, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Function));
            }

            public string GetDelegateTargetThunkLabel(RuntimeType delegateType, RuntimeMethod targetMethod, bool closed)
            {
                RuntimeMethod invokeMethod = ResolveDelegateInvoke(delegateType);
                var key = new DelegateTargetThunkKey(delegateType.TypeId, targetMethod.MethodId, closed);
                if (_delegateTargetThunksByKey.TryGetValue(key, out DelegateTargetThunkDraft? existing))
                    return existing.Label;

                string label = CreateLocalLabel($"delegate_thunk_M{targetMethod.MethodId}");
                var draft = new DelegateTargetThunkDraft(delegateType, invokeMethod, targetMethod, closed, label);
                _delegateTargetThunksByKey.Add(key, draft);
                _delegateTargetThunks.Add(draft);
                return label;
            }

            public DelegateAbiBundle GetDelegateInvokeAbi(RuntimeMethod invokeMethod)
                => BuildDelegateAbiBundle(invokeMethod);

            public int FindDelegateFieldOffset(RuntimeType delegateType, string fieldName)
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

                throw new InvalidOperationException($"Delegate type '{delegateType}' has no field '{fieldName}'.");
            }

            public RuntimeType FindSystemType(string name)
            {
                if (_program.TypeSystem is null)
                    throw new InvalidOperationException("ARM code generation requires a runtime type system for delegate metadata.");

                RuntimeType[] knownTypes = _program.TypeSystem.SnapshotKnownTypes();
                for (int i = 0; i < knownTypes.Length; i++)
                {
                    if (StringComparer.Ordinal.Equals(knownTypes[i].Namespace, "System") &&
                        StringComparer.Ordinal.Equals(knownTypes[i].Name, name))
                    {
                        return knownTypes[i];
                    }
                }

                throw new TypeLoadException($"Runtime type 'System.{name}' is required by ARM64 delegate lowering.");
            }

            public RuntimeType GetDelegateInvocationListArrayType()
            {
                if (_program.TypeSystem is null)
                    throw new InvalidOperationException("ARM code generation requires a runtime type system for delegate metadata.");
                return _program.TypeSystem.GetArrayType(FindSystemType("Delegate"));
            }

            private static RuntimeMethod ResolveDelegateInvoke(RuntimeType delegateType)
            {
                for (int i = 0; i < delegateType.Methods.Length; i++)
                {
                    RuntimeMethod method = delegateType.Methods[i];
                    if (method.HasThis && StringComparer.Ordinal.Equals(method.Name, "Invoke"))
                        return method;
                }

                throw new InvalidOperationException($"Delegate type '{delegateType}' has no Invoke method.");
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

            // A delegate call crosses two signatures, so both are laid out as save slots the thunk can
            // shuffle between without needing a register of its own for every argument
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
                        type, ref general, ref floating, ref stack, entityBase, ref maxStackSlot);
                    arguments.Add(new DelegateAbiEntity(type, entityBase, slices));
                    ordered.AddRange(slices);
                    int entitySize = Math.Max(_target.PointerSize, Math.Max(1, type.SizeOf));
                    for (int sliceIndex = 0; sliceIndex < slices.Length; sliceIndex++)
                        entitySize = Math.Max(entitySize, checked(slices[sliceIndex].ValueOffset + slices[sliceIndex].Size));
                    saveCursor = AlignUp(checked(entityBase + entitySize), _target.PointerSize);
                }

                if (hiddenInsertion == logicalCount)
                    hidden = AddHiddenReturnBuffer();

                int outgoingStackSize = maxStackSlot < 0 ? 0 : checked((maxStackSlot + 1) * _target.StackSlotSize);
                return new DelegateAbiBundle(
                    method, hidden, arguments.ToImmutable(), ordered.ToImmutable(), saveCursor, outgoingStackSize);

                DelegateAbiEntity AddHiddenReturnBuffer()
                {
                    int entityBase = saveCursor;
                    AbiArgumentLocation location = MachineAbi.AssignScalarArgumentLocation(
                        RegisterClass.General, _target.PointerSize, ref general, ref floating, ref stack, _target);
                    var slice = new DelegateAbiSlice(location, RegisterClass.General, 0, _target.PointerSize, entityBase);
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
                AbiValueInfo abi = MachineAbi.AdjustArgumentAbiForRegisterAvailability(
                    MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), isReturn: false, target: _target),
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
                            registerClass, size, ref general, ref floating, ref stack, _target);
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
                                location, segment.RegisterClass, segment.Offset, segment.Size,
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
                        AbiArgumentLocation location = AbiArgumentLocation.ForStack(RegisterClass.General, stackSlot, 0, size);
                        maxStackSlot = Math.Max(maxStackSlot, MachineAbi.LastStackSlotIndex(location, _target));
                        result.Add(new DelegateAbiSlice(location, RegisterClass.General, 0, size, saveBase));
                        return result.ToImmutable();
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported delegate ABI passing kind {abi.PassingKind}.");
                }
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

                int outgoingSize = AlignUp(
                    Math.Max(incoming.OutgoingStackSize, target.OutgoingStackSize), Math.Max(1, _target.StackSlotSize));
                int savedFramePointerOffset = outgoingSize;
                int savedReturnAddressOffset = checked(savedFramePointerOffset + _target.PointerSize);
                int incomingSaveOffset = AlignUp(
                    checked(savedReturnAddressOffset + _target.PointerSize), _target.PointerSize);
                int targetSaveOffset = AlignUp(
                    checked(incomingSaveOffset + incoming.TotalSaveSize), _target.PointerSize);
                int frameSize = AlignUp(
                    checked(targetSaveOffset + target.TotalSaveSize), _target.CallFrameAlignment);

                int startOffset = _text.ByteLength;
                _text.DefineLabel(thunk.Label);
                EmitAdjustStack(-frameSize);
                EmitDelegateMemoryStore(MachineRegister.X29, ArmRegister.Sp, savedFramePointerOffset, _target.PointerSize);
                EmitDelegateMemoryStore(MachineRegister.X30, ArmRegister.Sp, savedReturnAddressOffset, _target.PointerSize);
                EmitMove(ArmRegister.X29, ArmRegister.Sp, 8);

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

                SafePointDraft safePoint = AddSafePoint(
                    thunk.Label,
                    CreateLocalLabel(thunk.Label + "_gc_return"),
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    roots.ToImmutable());
                EmitMaterializeAddress(ResolveExternalObjectSymbol(ArmRuntime.CurrentSafePointSymbol), ArmRegister.X16);
                EmitMaterializeAddress(safePoint.DescriptorLabel, ArmRegister.X17);
                EmitDelegateMemoryStore(MachineRegister.X17, ArmRegister.X16, 0, _target.PointerSize);
                EmitMaterializeAddress(ResolveExternalObjectSymbol(ArmRuntime.CurrentFramePointerSymbol), ArmRegister.X16);
                EmitDelegateMemoryStore(MachineRegister.X29, ArmRegister.X16, 0, _target.PointerSize);
                EmitCall(ResolveMethodLabel(thunk.TargetMethod));
                DefineLabel(safePoint.ReturnLabel);

                EmitMove(ArmRegister.Sp, ArmRegister.X29, 8);
                EmitDelegateMemoryLoad(MachineRegister.X30, ArmRegister.Sp, savedReturnAddressOffset, _target.PointerSize, signed: false);
                EmitDelegateMemoryLoad(MachineRegister.X29, ArmRegister.Sp, savedFramePointerOffset, _target.PointerSize, signed: false);
                EmitAdjustStack(frameSize);
                Emit(ArmInstruction.Unary(ArmInstrKind.Ret, Reg(ArmRegister.X30, 8)));

                _symbols.Add(new ArmObjectSymbol(
                    thunk.Label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Function));
            }

            private void ValidateDelegateTargetThunk(
                DelegateTargetThunkDraft thunk,
                DelegateAbiBundle incoming,
                DelegateAbiBundle target)
            {
                if ((incoming.HiddenReturnBuffer is not null) != (target.HiddenReturnBuffer is not null))
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
                        incomingReturn, targetReturn, _target, requireMatchingRegisterClasses: true))
                {
                    throw new InvalidOperationException(
                        $"Delegate Invoke and target M{thunk.TargetMethod.MethodId} use incompatible return ABIs.");
                }

                int expectedTargetArgumentCount = checked(incoming.LogicalArguments.Length - 1 + (thunk.Closed ? 1 : 0));
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
                        EmitDelegateMemoryStore(slice.Location.Register, ArmRegister.X29, destinationOffset, slice.Size);
                        continue;
                    }

                    int sourceOffset = checked(
                        frameSize + slice.Location.StackSlotIndex * _target.StackSlotSize + slice.Location.StackOffset);
                    EmitDelegateCopyMemory(ArmRegister.X29, sourceOffset, ArmRegister.X29, destinationOffset, slice.Size);
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
                            slice.Location.Register, ArmRegister.X29, sourceOffset, slice.Size, signed: slice.IsGeneralWord);
                        continue;
                    }

                    int destinationOffset = checked(
                        slice.Location.StackSlotIndex * _target.StackSlotSize + slice.Location.StackOffset);
                    EmitDelegateCopyMemory(ArmRegister.X29, sourceOffset, ArmRegister.X29, destinationOffset, slice.Size);
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

                for (int i = 0; i < target.LogicalArguments.Length; i++)
                {
                    DelegateAbiEntity destination = target.LogicalArguments[i];
                    if (thunk.Closed && i == 0)
                    {
                        if (incoming.LogicalArguments.Length == 0 || incoming.LogicalArguments[0].Slices.Length == 0)
                            throw new InvalidOperationException($"Delegate target thunk for M{thunk.TargetMethod.MethodId} has no delegate receiver.");
                        DelegateAbiSlice delegateReceiver = incoming.LogicalArguments[0].Slices[0];
                        EmitDelegateMemoryLoad(
                            MachineRegister.X16,
                            ArmRegister.X29,
                            checked(incomingSaveBase + delegateReceiver.SaveOffset),
                            _target.PointerSize,
                            signed: false);
                        EmitDelegateMemoryLoad(
                            MachineRegister.X17,
                            ArmRegister.X16,
                            FindDelegateFieldOffset(thunk.DelegateType, "_target"),
                            _target.PointerSize,
                            signed: false);
                        StoreRegisterToDelegateSavedEntity(MachineRegister.X17, destination, targetSaveBase);
                        continue;
                    }

                    int incomingArgumentIndex = checked(1 + i - (thunk.Closed ? 1 : 0));
                    if ((uint)incomingArgumentIndex >= (uint)incoming.LogicalArguments.Length)
                    {
                        throw new InvalidOperationException(
                            $"Delegate target thunk argument mismatch for M{thunk.TargetMethod.MethodId}.");
                    }
                    CopyDelegateSavedEntity(
                        incoming.LogicalArguments[incomingArgumentIndex], incomingSaveBase, destination, targetSaveBase);
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
                    if (sourceSlice.ValueOffset != destinationSlice.ValueOffset || sourceSlice.Size != destinationSlice.Size)
                        throw new InvalidOperationException("Delegate argument ABI slice layout mismatch.");

                    EmitDelegateCopyMemory(
                        ArmRegister.X29,
                        checked(sourceSaveBase + sourceSlice.SaveOffset),
                        ArmRegister.X29,
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
                    ArmRegister.X29,
                    checked(destinationSaveBase + slice.SaveOffset),
                    Math.Min(_target.PointerSize, slice.Size));
            }

            private void EmitDelegateCopyMemory(
                ArmRegister sourceBase,
                int sourceOffset,
                ArmRegister destinationBase,
                int destinationOffset,
                int size)
            {
                int copied = 0;
                while (copied < size)
                {
                    int remaining = size - copied;
                    int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                    EmitDelegateMemoryLoad(
                        MachineRegister.X16, sourceBase, checked(sourceOffset + copied), chunk, signed: false);
                    EmitDelegateMemoryStore(
                        MachineRegister.X16, destinationBase, checked(destinationOffset + copied), chunk);
                    copied += chunk;
                }
            }

            private void EmitDelegateMemoryLoad(
                MachineRegister destination,
                ArmRegister baseRegister,
                int offset,
                int size,
                bool signed)
            {
                ArmRegister target = ToArm(destination);
                if (!CanEncodeDisplacement(offset, size))
                {
                    ArmRegister scratch = target == ArmRegister.X17 ? ArmRegister.X16 : ArmRegister.X17;
                    EmitAddImmediate(scratch, baseRegister, offset, scratch);
                    baseRegister = scratch;
                    offset = 0;
                }

                if (ArmRegisters.IsVector(target))
                {
                    Emit(ArmInstruction.Binary(ArmInstrKind.Ldr, Reg(target, size), Mem(baseRegister, offset, size)));
                    return;
                }

                (ArmInstrKind opcode, int operandSize) = size switch
                {
                    1 => (signed ? ArmInstrKind.Ldrsb : ArmInstrKind.Ldrb, signed ? 8 : 4),
                    2 => (signed ? ArmInstrKind.Ldrsh : ArmInstrKind.Ldrh, signed ? 8 : 4),
                    4 => (signed ? ArmInstrKind.Ldrsw : ArmInstrKind.Ldr, signed ? 8 : 4),
                    8 => (ArmInstrKind.Ldr, 8),
                    _ => throw new NotImplementedException($"Unsupported delegate load size {size}."),
                };
                Emit(ArmInstruction.Binary(opcode, Reg(target, operandSize), Mem(baseRegister, offset, size)));
            }

            private void EmitDelegateMemoryStore(MachineRegister source, ArmRegister baseRegister, int offset, int size)
            {
                ArmRegister value = ToArm(source);
                if (!CanEncodeDisplacement(offset, size))
                {
                    ArmRegister scratch = value == ArmRegister.X17 ? ArmRegister.X16 : ArmRegister.X17;
                    EmitAddImmediate(scratch, baseRegister, offset, scratch);
                    baseRegister = scratch;
                    offset = 0;
                }

                if (ArmRegisters.IsVector(value))
                {
                    Emit(ArmInstruction.Binary(ArmInstrKind.Str, Reg(value, size), Mem(baseRegister, offset, size)));
                    return;
                }

                ArmInstrKind opcode = size switch
                {
                    1 => ArmInstrKind.Strb,
                    2 => ArmInstrKind.Strh,
                    4 or 8 => ArmInstrKind.Str,
                    _ => throw new NotImplementedException($"Unsupported delegate store size {size}."),
                };
                Emit(ArmInstruction.Binary(opcode, Reg(value, size is 1 or 2 ? 4 : size), Mem(baseRegister, offset, size)));
            }

            private static bool CanEncodeDisplacement(int offset, int size)
                => (offset >= -256 && offset <= 255) || (offset >= 0 && offset % size == 0 && offset / size <= 4095);

            public string GetStaticStorageLabel(RuntimeType type)
                => GetOrCreateStaticStorage(type).StorageLabel;

            private StaticStorageDraft GetOrCreateStaticStorage(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_staticStorageByTypeId.TryGetValue(type.TypeId, out StaticStorageDraft? existing))
                    return existing;
                if (type.StaticFields.Length != 0 && type.StaticSize <= 0)
                    throw new InvalidOperationException($"Static layout was not computed for T{type.TypeId} '{type.Namespace}.{type.Name}'.");

                string label = CreateLocalLabel($"statics_{type.TypeId}");
                int size = Math.Max(1, type.StaticSize);
                int offset = _data.Align(Math.Max(1, type.StaticAlign));
                _data.EmitBytes(new byte[size]);
                AddDataSymbol(label, DataSectionName, offset, size);

                var storage = new StaticStorageDraft(type, label);
                _staticStorageByTypeId.Add(type.TypeId, storage);

                var roots = ImmutableArray.CreateBuilder<TypeGcFieldDraft>();
                for (int i = 0; i < type.StaticFields.Length; i++)
                    AppendTypedGcFields(roots, type.StaticFields[i].Offset, type.StaticFields[i].FieldType);
                for (int i = 0; i < roots.Count; i++)
                    _staticRoots.Add(new StaticRootDraft(label, roots[i].Offset, roots[i].Kind));

                return storage;
            }

            // 1 uninitialized, 2 running, 0 done: a running state seen again is the initializer recursing
            public string GetTypeInitializationStateLabel(RuntimeType type)
            {
                StaticStorageDraft storage = GetOrCreateStaticStorage(type);
                if (storage.InitializationStateLabel is not null)
                    return storage.InitializationStateLabel;

                string label = CreateLocalLabel($"type_init_{type.TypeId}");
                int offset = _data.Align(4);
                _data.EmitBytes(BitConverter.GetBytes(1));
                AddDataSymbol(label, DataSectionName, offset, 4);
                storage.InitializationStateLabel = label;
                return label;
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

            public string GetTypeInitializationThunkLabel(RuntimeType type)
            {
                if (type is null)
                    throw new ArgumentNullException(nameof(type));
                if (_typeInitializationThunksByTypeId.TryGetValue(type.TypeId, out TypeInitializationThunkDraft? existing))
                    return existing.Label;

                RuntimeMethod initializer = FindTypeInitializer(type) ??
                    throw new InvalidOperationException($"Type T{type.TypeId} '{type}' has no static initializer.");
                var draft = new TypeInitializationThunkDraft(
                    initializer,
                    GetTypeInitializationStateLabel(type),
                    CreateUniqueGlobalLabel($"__cctor_thunk_T{type.TypeId}"));
                _typeInitializationThunksByTypeId.Add(type.TypeId, draft);
                _typeInitializationThunks.Add(draft);
                return draft.Label;
            }

            private void EmitTypeInitializationThunks()
            {
                for (int i = 0; i < _typeInitializationThunks.Count; i++)
                    EmitTypeInitializationThunk(_typeInitializationThunks[i]);
            }

            private void EmitTypeInitializationThunk(TypeInitializationThunkDraft thunk)
            {
                int startOffset = _text.ByteLength;
                _text.DefineLabel(thunk.Label);

                int frameSize = AlignUp(checked(_target.PointerSize * 2), _target.CallFrameAlignment);
                const int savedReturnAddressOffset = 0;
                int savedFramePointerOffset = _target.PointerSize;
                EmitAdjustStack(-frameSize);
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Str, Reg(ArmRegister.X30, 8), Mem(ArmRegister.Sp, savedReturnAddressOffset, 8)));
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Str, Reg(ArmRegister.X29, 8), Mem(ArmRegister.Sp, savedFramePointerOffset, 8)));
                EmitMove(ArmRegister.X29, ArmRegister.Sp, 8);

                string doneLabel = CreateLocalLabel(thunk.Label + "_done");
                string invalidStateLabel = CreateLocalLabel(thunk.Label + "_invalid_state");

                EmitMaterializeAddress(thunk.StateLabel, ArmRegister.X16);
                Emit(ArmInstruction.Binary(ArmInstrKind.Ldr, Reg(ArmRegister.X17, 4), Mem(ArmRegister.X16, 0, 4)));
                EmitCompareBranch(ArmInstrKind.Cbz, ArmRegister.X17, 4, doneLabel);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ArmRegister.X17, 4), ArmOperand.ImmediateOperand(2)));
                EmitConditionalJump(ArmCondition.Eq, doneLabel);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ArmRegister.X17, 4), ArmOperand.ImmediateOperand(1)));
                EmitConditionalJump(ArmCondition.Ne, invalidStateLabel);

                EmitLoadImmediate(ArmRegister.X17, 2, 4);
                Emit(ArmInstruction.Binary(ArmInstrKind.Str, Reg(ArmRegister.X17, 4), Mem(ArmRegister.X16, 0, 4)));
                string returnLabel = CreateLocalLabel(thunk.Label + "_gc_return");
                AddSafePoint(
                    thunk.Label,
                    returnLabel,
                    savedFramePointerOffset,
                    savedReturnAddressOffset,
                    ImmutableArray<SafePointRootDraft>.Empty);
                EmitCall(ResolveMethodLabel(thunk.Initializer));
                DefineLabel(returnLabel);
                EmitMaterializeAddress(thunk.StateLabel, ArmRegister.X16);
                Emit(ArmInstruction.Binary(ArmInstrKind.Str, Reg(ArmRegister.Xzr, 4), Mem(ArmRegister.X16, 0, 4)));
                EmitJump(doneLabel);

                DefineLabel(invalidStateLabel);
                EmitLoadImmediate(ArmRegister.X0, 150, 8);
                EmitCall(ResolveExternalSymbol(ArmRuntime.FailFastSymbol));
                Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(1)));

                DefineLabel(doneLabel);
                EmitMove(ArmRegister.Sp, ArmRegister.X29, 8);
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Ldr, Reg(ArmRegister.X30, 8), Mem(ArmRegister.Sp, savedReturnAddressOffset, 8)));
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Ldr, Reg(ArmRegister.X29, 8), Mem(ArmRegister.Sp, savedFramePointerOffset, 8)));
                EmitAdjustStack(frameSize);
                Emit(ArmInstruction.Unary(ArmInstrKind.Ret, Reg(ArmRegister.X30, 8)));

                _symbols.Add(new ArmObjectSymbol(
                    thunk.Label, TextSectionName, startOffset, _text.ByteLength - startOffset,
                    ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Function));
            }

            private void EmitCompareBranch(ArmInstrKind opcode, ArmRegister register, int size, string label)
            {
                int offset = _text.ByteLength;
                Emit(new ArmInstruction(
                    opcode, Reg(register, size), ArmOperand.SymbolOperand(label, ArmRelocationKind.CompareBranch)));
                _text.AddRelocation(offset, label, 0, ArmObjectRelocationKind.AArch64CompareBranch19);
            }

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                int remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            public string GetStringLiteralLabel(RuntimeType type, string text)
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

            public string GetStaticExceptionObjectLabel(string @namespace, string name)
            {
                if (_program.TypeSystem is null)
                    throw new InvalidOperationException("ARM code generation requires a runtime type system for exception metadata.");

                RuntimeType? type = null;
                RuntimeType[] knownTypes = _program.TypeSystem.SnapshotKnownTypes();
                for (int i = 0; i < knownTypes.Length; i++)
                {
                    if (StringComparer.Ordinal.Equals(knownTypes[i].Namespace, @namespace) &&
                        StringComparer.Ordinal.Equals(knownTypes[i].Name, name))
                    {
                        type = knownTypes[i];
                        break;
                    }
                }

                if (type is null)
                    throw new TypeLoadException($"Runtime type '{@namespace}.{name}' is required by ARM64 lowering.");
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

            private RuntimeMetadataLabels EmitRuntimeMetadata()
            {
                int pointerSize = _target.PointerSize;

                for (int i = 0; i < _ehMethods.Count; i++)
                {
                    EhMethodDraft method = _ehMethods[i];
                    AddDataSymbol(
                        method.ClausesLabel,
                        _rodata.Align(pointerSize),
                        checked(method.Clauses.Length * pointerSize * 12));
                    for (int c = 0; c < method.Clauses.Length; c++)
                    {
                        EhClauseDraft clause = method.Clauses[c];
                        if (clause.TryStartLabel is null || clause.TryEndLabel is null ||
                            clause.HandlerStartLabel is null || clause.HandlerEndLabel is null)
                        {
                            throw new InvalidOperationException(
                                $"ARM EH native ranges were not bound for method M{method.Method.RuntimeMethod.MethodId}.");
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

                    AddDataSymbol(method.InfoLabel, _rodata.Align(pointerSize), pointerSize * 2);
                    EmitNative(method.Clauses.Length);
                    EmitPointer(method.ClausesLabel);
                }

                for (int i = 0; i < _typeDescriptors.Count; i++)
                {
                    TypeDescriptorDraft descriptor = _typeDescriptors[i];
                    if (descriptor.Fields.Length != 0)
                    {
                        descriptor.FieldsLabel = CreateLocalLabel(descriptor.Label + "_fields");
                        AddDataSymbol(
                            descriptor.FieldsLabel,
                            _rodata.Align(pointerSize),
                            checked(descriptor.Fields.Length * pointerSize * 2));
                        for (int f = 0; f < descriptor.Fields.Length; f++)
                        {
                            EmitNative(descriptor.Fields[f].Offset);
                            EmitNative(ToRuntimeRootKind(descriptor.Fields[f].Kind));
                        }
                    }
                    if (descriptor.ComponentFields.Length != 0)
                    {
                        descriptor.ComponentFieldsLabel = CreateLocalLabel(descriptor.Label + "_component_fields");
                        AddDataSymbol(
                            descriptor.ComponentFieldsLabel,
                            _rodata.Align(pointerSize),
                            checked(descriptor.ComponentFields.Length * pointerSize * 2));
                        for (int f = 0; f < descriptor.ComponentFields.Length; f++)
                        {
                            EmitNative(descriptor.ComponentFields[f].Offset);
                            EmitNative(ToRuntimeRootKind(descriptor.ComponentFields[f].Kind));
                        }
                    }
                    if (descriptor.VTableTargets.Length != 0)
                    {
                        descriptor.VTableLabel = CreateLocalLabel(descriptor.Label + "_vtable");
                        AddDataSymbol(
                            descriptor.VTableLabel,
                            _rodata.Align(pointerSize),
                            checked(descriptor.VTableTargets.Length * pointerSize));
                        for (int slot = 0; slot < descriptor.VTableTargets.Length; slot++)
                            EmitPointer(descriptor.VTableTargets[slot]);
                    }
                    if (descriptor.Interfaces.Length != 0)
                    {
                        descriptor.InterfacesLabel = CreateLocalLabel(descriptor.Label + "_interfaces");
                        AddDataSymbol(
                            descriptor.InterfacesLabel,
                            _rodata.Align(pointerSize),
                            checked((descriptor.Interfaces.Length + 1) * pointerSize));
                        for (int iface = 0; iface < descriptor.Interfaces.Length; iface++)
                            EmitPointer(GetTypeDescriptorLabel(descriptor.Interfaces[iface]));
                        EmitNative(0);
                    }
                }

                for (int i = 0; i < _typeDescriptors.Count; i++)
                {
                    TypeDescriptorDraft descriptor = _typeDescriptors[i];
                    bool isString = IsSystemStringType(descriptor.Type);
                    bool isArray = descriptor.Type.Kind == RuntimeTypeKind.Array;
                    int componentSize = isString ? 2 : isArray ? GetArrayComponentSize(descriptor.Type) : 0;
                    int baseSize = isString
                        ? checked(_target.SyncBlockSize + _target.StringFirstCharOffset + 2)
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

                    AddDataSymbol(descriptor.Label, _rodata.Align(pointerSize), checked(16 + pointerSize * 3));
                    EmitUInt32(ComputeMethodTableFlags(descriptor, componentSize));
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
                    AddDataSymbol(
                        cell.Label,
                        _rodata.Align(pointerSize),
                        checked((3 + cell.Entries.Length * 2) * pointerSize));
                    EmitNative(cell.Entries.Length);
                    for (int entry = 0; entry < cell.Entries.Length; entry++)
                    {
                        EmitPointer(cell.Entries[entry].ReceiverTypeLabel);
                        EmitPointer(cell.Entries[entry].TargetLabel);
                    }
                    EmitNative(0);
                    EmitNative(0);
                }

                string typeInfoTableLabel = CreateLocalLabel("gc_type_infos");
                AddDataSymbol(
                    typeInfoTableLabel,
                    _rodata.Align(pointerSize),
                    _typeDescriptors.Count == 0 ? pointerSize : checked(_typeDescriptors.Count * pointerSize * 6));
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
                    _rodata.Align(pointerSize);
                    EmitNative(0);
                    AddDataSymbol(
                        literal.Label,
                        _rodata.ByteLength,
                        checked(_target.StringFirstCharOffset + chars.Length + 2));
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
                    AddDataSymbol(exception.ObjectLabel, _rodata.ByteLength, objectSize);
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
                    AddDataSymbol(
                        safePoint.RootsLabel,
                        _rodata.Align(pointerSize),
                        checked(safePoint.Roots.Length * pointerSize * 2));
                    for (int r = 0; r < safePoint.Roots.Length; r++)
                    {
                        EmitNative(safePoint.Roots[r].FrameOffset);
                        EmitNative(ToRuntimeRootKind(safePoint.Roots[r].Kind));
                    }
                }

                string safePointTableLabel = CreateLocalLabel("gc_safe_points");
                AddDataSymbol(
                    safePointTableLabel,
                    _rodata.Align(pointerSize),
                    _safePoints.Count == 0 ? pointerSize : checked(_safePoints.Count * pointerSize * 5));
                if (_safePoints.Count == 0)
                {
                    EmitNative(0);
                }
                else
                {
                    for (int i = 0; i < _safePoints.Count; i++)
                    {
                        SafePointDraft safePoint = _safePoints[i];
                        AddDataSymbol(safePoint.DescriptorLabel, _rodata.ByteLength, pointerSize * 5);
                        EmitPointer(safePoint.ReturnLabel);
                        EmitNative(safePoint.SavedFramePointerOffset);
                        EmitNative(safePoint.SavedReturnAddressOffset);
                        EmitNative(safePoint.Roots.Length);
                        EmitPointer(safePoint.RootsLabel);
                    }
                }

                string staticRootTableLabel = CreateLocalLabel("gc_static_roots");
                AddDataSymbol(
                    staticRootTableLabel,
                    _rodata.Align(pointerSize),
                    _staticRoots.Count == 0 ? pointerSize : checked(_staticRoots.Count * pointerSize * 2));
                if (_staticRoots.Count == 0)
                {
                    EmitNative(0);
                }
                else
                {
                    for (int i = 0; i < _staticRoots.Count; i++)
                    {
                        EmitPointer(_staticRoots[i].StorageLabel, _staticRoots[i].Offset);
                        EmitNative(ToRuntimeRootKind(_staticRoots[i].Kind));
                    }
                }

                _metadataSealed = true;
                return new RuntimeMetadataLabels(
                    safePointTableLabel,
                    _safePoints.Count,
                    typeInfoTableLabel,
                    _typeDescriptors.Count,
                    staticRootTableLabel,
                    _staticRoots.Count);
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
                RuntimeType elementType = arrayType.ElementType ??
                    throw new InvalidOperationException("Array runtime type has no element type.");
                if (elementType.IsReferenceType ||
                    elementType.Kind is RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                {
                    return _target.PointerSize;
                }
                return Math.Max(1, elementType.SizeOf);
            }

            private static uint ComputeMethodTableFlags(TypeDescriptorDraft descriptor, int componentSize)
            {
                const uint parameterizedKind = 0x00020000u;
                const uint hasPointers = 0x01000000u;
                const uint hasComponentSize = 0x80000000u;

                uint flags = GetMethodTableElementType(descriptor.Type) << 26;
                if (descriptor.Type.Kind is RuntimeTypeKind.Array or RuntimeTypeKind.Pointer or
                    RuntimeTypeKind.FunctionPointer or RuntimeTypeKind.ByRef)
                {
                    flags |= parameterizedKind;
                }
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
                        if (!type.InstanceFields[i].IsStatic)
                        {
                            primitiveKind = type.InstanceFields[i].FieldType.PrimitiveKind;
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

            private static bool IsSystemStringType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(type.Name, "String");

            private static bool IsSystemArrayType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   StringComparer.Ordinal.Equals(type.Name, "Array");

            private static bool IsNullableType(RuntimeType type)
                => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
                   type.Name.StartsWith("Nullable", StringComparison.Ordinal);

            private void AddDataSymbol(string label, int offset, int size)
                => AddDataSymbol(label, RodataSectionName, offset, size);

            private void AddDataSymbol(string label, string section, int offset, int size)
                => _symbols.Add(new ArmObjectSymbol(
                    label, section, offset, size, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Object));

            private void EmitPointer(string? symbol, int addend = 0)
            {
                int offset = _rodata.ByteLength;
                _rodata.EmitBytes(new byte[_target.PointerSize]);
                if (symbol is not null)
                    _rodata.AddRelocation(offset, symbol, addend, ArmObjectRelocationKind.AbsolutePointer);
            }

            private void EmitNative(long value)
                => _rodata.EmitBytes(_target.PointerSize == 8
                    ? BitConverter.GetBytes(value)
                    : BitConverter.GetBytes(checked((int)value)));

            private void EmitInt32(int value) => _rodata.EmitBytes(BitConverter.GetBytes(value));

            private void EmitUInt32(uint value) => _rodata.EmitBytes(BitConverter.GetBytes(value));

            private void EmitUInt16(ushort value) => _rodata.EmitBytes(BitConverter.GetBytes(value));

            private static int ToRuntimeRootKind(RegisterGcRootKind kind)
                => kind == RegisterGcRootKind.ObjectReference ? 0 : 1;

            private sealed class MethodEmitter
            {
                private readonly Generator _owner;
                private readonly GenTreeMethod _method;
                private readonly string _methodLabel;
                private readonly string[] _blockLabels;
                private readonly string[] _blockEndLabels;
                private readonly EhMethodDraft? _ehMethod;
                private readonly string _returnThunkLabel;
                private bool _ehFrameRegistered;
                private bool _returnThunkNeeded;
                private readonly Dictionary<int, int> _nodePositions;
                private readonly List<GcPollStub> _gcPollStubs = new List<GcPollStub>();
                private readonly Dictionary<(int Offset, int Length), string> _staticDataLabels =
                    new Dictionary<(int Offset, int Length), string>();
                private int _nextBlockId = -1;

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

                private const ArmRegister Scratch0 = ArmRegister.X16;
                private const ArmRegister Scratch1 = ArmRegister.X17;

                private readonly struct GcPollStub
                {
                    public readonly GenTree Node;
                    public readonly string SlowLabel;
                    public readonly string ContinuationLabel;

                    public GcPollStub(GenTree node, string slowLabel, string continuationLabel)
                    {
                        Node = node;
                        SlowLabel = slowLabel;
                        ContinuationLabel = continuationLabel;
                    }
                }

                private TargetInfo Target => _owner.Target;

                public void Emit()
                {
                    var order = _method.LinearBlockOrder;
                    for (int i = 0; i < order.Length; i++)
                    {
                        int blockId = order[i];
                        _nextBlockId = i + 1 < order.Length ? order[i + 1] : -1;
                        var block = _method.Blocks[blockId];
                        int firstBodyNode = blockId == 0 ? GenTreeLirKinds.PrologPrefixLength(block.LinearNodes) : 0;
                        for (int n = 0; n < firstBodyNode; n++)
                            EmitNode(block.LinearNodes[n]);
                        _owner.DefineLabel(_blockLabels[blockId]);
                        for (int n = firstBodyNode; n < block.LinearNodes.Length; n++)
                            EmitNode(block.LinearNodes[n]);
                        EmitFallthroughFixup(blockId, _nextBlockId);
                        _owner.DefineLabel(_blockEndLabels[blockId]);
                    }
                    _nextBlockId = -1;
                    EmitGcPollStubs();
                    BindEhNativeRanges();
                    if (_returnThunkNeeded)
                        EmitReturnThunk();
                    if (_ehMethod is not null && !_ehFrameRegistered)
                        throw Unsupported(_method.LinearNodes[0], "Method has EH metadata but no registered establisher frame");
                }

                private static Dictionary<int, int> BuildNodePositions(GenTreeMethod method)
                {
                    var result = new Dictionary<int, int>();
                    int position = 0;
                    var order = method.LinearBlockOrder;
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

                private void EmitFallthroughFixup(int blockId, int nextBlockId)
                {
                    var successors = _method.Cfg.Blocks[blockId].Successors;
                    foreach (var edge in successors)
                    {
                        if (edge.Kind == CfgEdgeKind.FallThrough && edge.ToBlockId != nextBlockId)
                        {
                            _owner.EmitJump(_blockLabels[edge.ToBlockId]);
                            return;
                        }
                    }
                }

                private void EmitNode(GenTree node)
                {
                    switch (node.TreeKind)
                    {
                        case GenTreeKind.Nop:
                        case GenTreeKind.Eval:
                            return;
                        case GenTreeKind.GcPoll:
                            EmitGcPoll(node);
                            return;
                        case GenTreeKind.Copy:
                        case GenTreeKind.Reload:
                        case GenTreeKind.Spill:
                            EmitMoveNode(node);
                            return;
                        case GenTreeKind.StackFrameOp:
                            EmitFrameOperation(node);
                            return;
                        case GenTreeKind.ConstI4:
                            _owner.EmitLoadImmediate(GeneralRegister(RequireResultRegister(node)), node.Int32, 8);
                            return;
                        case GenTreeKind.ConstI8:
                            _owner.EmitLoadImmediate(GeneralRegister(RequireResultRegister(node)), node.Int64, 8);
                            return;
                        case GenTreeKind.ConstNull:
                            _owner.EmitMove(GeneralRegister(RequireResultRegister(node)), ArmRegister.Xzr, 8);
                            return;
                        case GenTreeKind.ConstString:
                            _owner.EmitMaterializeAddress(
                                _owner.GetStringLiteralLabel(RequireRuntimeType(node), node.Text ?? string.Empty),
                                GeneralRegister(RequireResultRegister(node)));
                            return;
                        case GenTreeKind.ConstR4Bits:
                            EmitFloatConstant(node, 4, BitConverter.GetBytes(node.Int32));
                            return;
                        case GenTreeKind.ConstR8Bits:
                            EmitFloatConstant(node, 8, BitConverter.GetBytes(node.Int64));
                            return;
                        case GenTreeKind.StaticData:
                            EmitStaticData(node);
                            return;
                        case GenTreeKind.PointerElementAddr:
                            EmitPointerElementAddress(node);
                            return;
                        case GenTreeKind.PointerDiff:
                            EmitPointerDifference(node);
                            return;
                        case GenTreeKind.StackAlloc:
                            EmitStackAlloc(node);
                            return;
                        case GenTreeKind.SizeOf:
                            _owner.EmitLoadImmediate(GeneralRegister(RequireResultRegister(node)), RequireRuntimeType(node).SizeOf, 8);
                            return;
                        case GenTreeKind.DefaultValue:
                            EmitDefaultValue(node);
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
                            _owner.EmitMaterializeAddress(
                                _owner.ResolveMethodLabel(node.Method ?? throw Unsupported(node, "function pointer node has no runtime method")),
                                GeneralRegister(RequireResultRegister(node)));
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
                            {
                                EmitLeave(node);
                                return;
                            }
                            if (node.TargetBlockId != _nextBlockId)
                                _owner.EmitJump(LabelForTarget(node));
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
                        case GenTreeKind.BranchTrue:
                        case GenTreeKind.BranchFalse:
                            EmitConditionalBranch(node);
                            return;
                        case GenTreeKind.Return:
                            EmitReturn(node);
                            return;
                        case GenTreeKind.Intrinsic:
                            EmitIntrinsic(node);
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
                        case GenTreeKind.NewDelegate:
                            EmitNewDelegate(node);
                            return;
                        case GenTreeKind.DelegateInvoke:
                            EmitDelegateInvoke(node);
                            return;
                        case GenTreeKind.DelegateCombine:
                            EmitDelegateCombineOrRemove(node, remove: false);
                            return;
                        case GenTreeKind.DelegateRemove:
                            EmitDelegateCombineOrRemove(node, remove: true);
                            return;
                        case GenTreeKind.NullCheck:
                            EmitNullCheck(node);
                            return;
                        case GenTreeKind.NewObject:
                            EmitNewObject(node);
                            return;
                        case GenTreeKind.NewArray:
                            EmitNewArray(node);
                            return;
                        case GenTreeKind.Field:
                        case GenTreeKind.FieldAddr:
                        case GenTreeKind.StoreField:
                            EmitField(node);
                            return;
                        case GenTreeKind.LoadIndirect:
                        case GenTreeKind.StoreIndirect:
                            EmitIndirect(node);
                            return;
                        case GenTreeKind.ClassInit:
                            EmitClassInit(node);
                            return;
                        case GenTreeKind.StaticField:
                        case GenTreeKind.StaticFieldAddr:
                        case GenTreeKind.StoreStaticField:
                            EmitStaticField(node);
                            return;
                        case GenTreeKind.Box:
                            EmitBox(node);
                            return;
                        case GenTreeKind.UnboxAny:
                            EmitUnboxAny(node);
                            return;
                        case GenTreeKind.CastClass:
                            EmitRuntimeTypeCheck(node, throwOnFailure: true);
                            return;
                        case GenTreeKind.IsInst:
                            EmitRuntimeTypeCheck(node, throwOnFailure: false);
                            return;
                        case GenTreeKind.ArrayLength:
                        case GenTreeKind.ArrayElement:
                        case GenTreeKind.ArrayElementAddr:
                        case GenTreeKind.StoreArrayElement:
                        case GenTreeKind.ArrayDataRef:
                            EmitArray(node);
                            return;
                        default:
                            throw Unsupported(node, $"GenTree kind {node.TreeKind} is not implemented in the ARM64 code generator");
                    }
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
                            _owner.EmitMove(ArmRegister.X29, ArmRegister.Sp, 8);
                            if (_ehMethod is not null && !_ehFrameRegistered)
                                EmitEhFramePush(_blockLabels[node.BlockId]);
                            return;
                        case FrameOperation.RestoreStackPointerFromFramePointer:
                            _owner.EmitMove(ArmRegister.Sp, ArmRegister.X29, 8);
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
                        case FrameOperation.EnterFuncletFrame:
                        case FrameOperation.LeaveFuncletFrame:
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
                        default:
                            throw Unsupported(node, $"Frame operation {node.FrameOperation} is not implemented");
                    }
                }

                private void EmitMoveNode(GenTree node)
                {
                    if (node.Results.Length != 1 || node.Uses.Length != 1)
                        throw Unsupported(node, "Move requires one source and one destination");

                    RegisterOperand destination = node.Results[0];
                    RegisterOperand source = node.Uses[0];
                    RuntimeType? type = ValueType(node);
                    GenStackKind kind = ValueStackKind(node);
                    NormalizeScalarizedAggregateMove(node.Results[0], node.Uses[0], ref type, ref kind);

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
                            EmitLoadAddress(MachineRegister.X16, source);
                            EmitStore(destination, MachineRegister.X16, null, GenStackKind.Ptr);
                            return;
                        default:
                            throw Unsupported(node, $"Unknown LSRA move kind {node.MoveKind}");
                    }
                }

                private void NormalizeScalarizedAggregateMove(
                    RegisterOperand destination,
                    RegisterOperand source,
                    ref RuntimeType? type,
                    ref GenStackKind kind)
                {
                    if (!IsAggregate(type, kind))
                        return;

                    int destinationSize = destination.FrameSlotSize;
                    int sourceSize = source.FrameSlotSize;
                    if (destinationSize > 0 && sourceSize > 0 && destinationSize != sourceSize)
                        return;

                    int size = Math.Max(destinationSize, sourceSize);
                    if (size <= 0 || destination.RegisterClass != source.RegisterClass)
                        return;

                    if (destination.RegisterClass == RegisterClass.General)
                    {
                        if (size > Target.GeneralRegisterSize)
                            return;
                        type = null;
                        kind = GenStackKind.NativeUInt;
                        return;
                    }

                    if (destination.RegisterClass == RegisterClass.Float && size is 4 or 8)
                    {
                        type = null;
                        kind = size == 4 ? GenStackKind.R4 : GenStackKind.R8;
                    }
                }

                private static bool IsAggregate(RuntimeType? type, GenStackKind kind)
                    => kind == GenStackKind.Value ||
                       (type is not null &&
                        type.Kind != RuntimeTypeKind.FunctionPointer &&
                        type.IsValueType &&
                        type.PrimitiveKind == RuntimePrimitiveKind.None);

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
                        if (node.Results.Length != node.Uses.Length)
                            throw Unsupported(node, "Multi-register local has mismatched source and destination fragment counts");

                        var moveSegments = LocalLikeSegments(node, node.Results.Length);
                        for (int i = 0; i < moveSegments.Length; i++)
                            EmitMoveBetween(node, node.Results[i], node.Uses[i], null, StackKindForSegment(moveSegments[i]));
                        return;
                    }

                    if (isLoad && node.Uses.Length == 0 && node.Results.Length == 1)
                    {
                        RegisterOperand home = FrameSlotForLocalLike(node, type, kind, node.Results[0].RegisterClass);
                        if (node.Results[0].IsRegister)
                            EmitLoad(node.Results[0].Register, home, type, kind);
                        else
                            EmitMemoryToMemory(node.Results[0], home, type, kind);
                        return;
                    }

                    if (isStore && node.Results.Length == 0 && node.Uses.Length == 1)
                    {
                        RegisterOperand home = FrameSlotForLocalLike(node, type, kind, node.Uses[0].RegisterClass);
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
                    RegisterOperand slot = LocalLikeHome(node);
                    var segments = LocalLikeSegments(node, node.Results.Length);
                    for (int i = 0; i < segments.Length; i++)
                    {
                        RegisterOperand source = FrameSlotFragment(slot, segments[i]);
                        RegisterOperand destination = node.Results[i];
                        if (destination.IsRegister)
                            EmitLoad(destination.Register, source, null, StackKindForSegment(segments[i]));
                        else
                            EmitMemoryToMemory(destination, source, null, StackKindForSegment(segments[i]));
                    }
                }

                private void EmitLocalLikeMultiStore(GenTree node)
                {
                    RegisterOperand slot = LocalLikeHome(node);
                    var segments = LocalLikeSegments(node, node.Uses.Length);
                    for (int i = 0; i < segments.Length; i++)
                    {
                        RegisterOperand destination = FrameSlotFragment(slot, segments[i]);
                        RegisterOperand source = node.Uses[i];
                        if (source.IsRegister)
                            EmitStore(destination, source.Register, null, StackKindForSegment(segments[i]));
                        else
                            EmitMemoryToMemory(destination, source, null, StackKindForSegment(segments[i]));
                    }
                }

                private RegisterOperand LocalLikeHome(GenTree node)
                    => FrameSlotForLocalLike(
                        node,
                        node.LocalDescriptor?.Type ?? node.RuntimeType ?? node.Type,
                        node.LocalDescriptor?.StackKind ?? node.StackKind,
                        RegisterClass.General);

                private ImmutableArray<AbiRegisterSegment> LocalLikeSegments(GenTree node, int fragmentCount)
                {
                    RuntimeType? type = node.RuntimeType ?? node.Type ?? node.LocalDescriptor?.Type;
                    GenStackKind kind = node.LocalDescriptor?.StackKind ?? node.StackKind;
                    AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    var segments = MachineAbi.GetRegisterSegments(abi, Target);
                    if (segments.Length != fragmentCount)
                        throw Unsupported(node, "Multi-register local fragment count does not match its storage ABI");
                    return segments;
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

                private static GenStackKind StackKindForSegment(AbiRegisterSegment segment)
                {
                    if (segment.ContainsGcPointers)
                        return GenStackKind.Ref;
                    if (segment.RegisterClass == RegisterClass.Float)
                        return segment.Size <= 4 ? GenStackKind.R4 : GenStackKind.R8;
                    return segment.Size > 4 ? GenStackKind.I8 : GenStackKind.I4;
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
                    throw Unsupported(node, "Address tree has an invalid operand shape");
                }

                private void EmitStaticData(GenTree node)
                {
                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));
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
                    if (sourceOffset < 0 || sourceLength < 0 || sourceOffset > staticData.Length ||
                        sourceLength > staticData.Length - sourceOffset)
                    {
                        throw Unsupported(node, "Invalid static data blob range");
                    }

                    if (sourceLength == 0)
                    {
                        _owner.EmitMove(destination, ArmRegister.Xzr, 8);
                        return;
                    }

                    var key = (sourceOffset, sourceLength);
                    if (!_staticDataLabels.TryGetValue(key, out string? label))
                    {
                        label = _owner.AddConstantData(
                            staticData.AsSpan().Slice(sourceOffset, sourceLength).ToArray(),
                            8,
                            $"static_data_M{_method.RuntimeMethod.MethodId}");
                        _staticDataLabels.Add(key, label);
                    }

                    _owner.EmitMaterializeAddress(label, destination);
                }

                private void EmitPointerElementAddress(GenTree node)
                {
                    if (node.Uses.Length is < 1 or > 2)
                        throw Unsupported(node, "Pointer element address requires base and index operands");

                    int scale = node.Int32 > 0 ? node.Int32 : Math.Max(1, node.RuntimeType?.SizeOf ?? node.Type?.SizeOf ?? 1);
                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));
                    ArmRegister baseRegister = GeneralRegister(RequireUseRegisterForOperand(node, 0, "pointer base"));
                    RuntimeType? indexType = OperandType(node, 1);
                    GenStackKind indexKind = OperandStackKind(node, 1);

                    if (TryGetContainedIntegerImmediate(node, 1, out long immediateIndex))
                    {
                        if (IsI4(indexType, indexKind))
                        {
                            immediateIndex = IsUnsigned(indexType)
                                ? unchecked((uint)(int)immediateIndex)
                                : unchecked((int)immediateIndex);
                        }
                        _owner.EmitAddImmediate(destination, baseRegister, unchecked(immediateIndex * scale), Scratch0);
                        return;
                    }

                    ArmRegister index = GeneralRegister(RequireUseRegisterForOperand(node, 1, "pointer index"));
                    if (IsI4(indexType, indexKind))
                    {
                        // A narrow index reaches the address as its own width, not as whatever the register held
                        EmitBitfieldMove(IsSigned(indexType, indexKind), Scratch0, index, 32);
                        index = Scratch0;
                    }

                    if ((scale & (scale - 1)) == 0)
                    {
                        if (scale == 1)
                        {
                            _owner.EmitMove(Scratch1, index, 8);
                        }
                        else
                        {
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Lsl, Reg(Scratch1, 8), Reg(index, 8), ArmOperand.ImmediateOperand(Log2(scale))));
                        }
                    }
                    else
                    {
                        _owner.EmitLoadImmediate(Scratch1, scale, 8);
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Mul, Reg(Scratch1, 8), Reg(index, 8), Reg(Scratch1, 8)));
                    }

                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(destination, 8), Reg(baseRegister, 8), Reg(Scratch1, 8)));
                }

                private void EmitPointerDifference(GenTree node)
                {
                    if (node.Uses.Length != 2)
                        throw Unsupported(node, "Pointer difference requires two operands");

                    int scale = node.Int32 > 0 ? node.Int32 : 1;
                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Sub,
                        Reg(destination, 8),
                        Reg(GeneralRegister(RequireUseRegisterForOperand(node, 0, "pointer difference left operand")), 8),
                        Reg(GeneralRegister(RequireUseRegisterForOperand(node, 1, "pointer difference right operand")), 8)));
                    if (scale == 1)
                        return;

                    if ((scale & (scale - 1)) == 0)
                    {
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Asr, Reg(destination, 8), Reg(destination, 8), ArmOperand.ImmediateOperand(Log2(scale))));
                        return;
                    }

                    _owner.EmitLoadImmediate(Scratch0, scale, 8);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Sdiv, Reg(destination, 8), Reg(destination, 8), Reg(Scratch0, 8)));
                }

                // The stack pointer cannot be an operand of a shifted-register subtract, so the new value
                // is formed in a scratch register and moved back
                private void EmitStackAlloc(GenTree node)
                {
                    if (node.Int32 <= 0)
                        throw Unsupported(node, "Stack allocation element size must be positive");

                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));
                    int alignment = Target.CallFrameAlignment;

                    if (node.Operands.Length == 0)
                    {
                        if (node.Int64 <= 0)
                            throw Unsupported(node, "Constant stack allocation byte count must be positive");
                        ulong mask = (ulong)alignment - 1;
                        ulong aligned = ((ulong)node.Int64 + mask) & ~mask;
                        if (aligned > int.MaxValue)
                            throw Unsupported(node, "Constant stack allocation byte count is too large");
                        _owner.EmitAdjustStack(-(int)aligned);
                        _owner.EmitMove(destination, ArmRegister.Sp, 8);
                        return;
                    }

                    if (node.Operands.Length != 1)
                        throw Unsupported(node, "Stack allocation must have zero or one operand");

                    ArmRegister count = GeneralRegister(RequireUseRegisterForOperand(node, 0, "stack allocation count"));
                    EmitBitfieldMove(sign: true, Scratch1, count, 32);

                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_stackalloc_nonnegative_{node.LinearId}");
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 8), ArmOperand.ImmediateOperand(0)));
                    _owner.EmitConditionalJump(ArmCondition.Ge, nonNegative);
                    EmitStackAllocFailure();
                    _owner.DefineLabel(nonNegative);

                    if (node.Int32 != 1)
                    {
                        if ((node.Int32 & (node.Int32 - 1)) == 0)
                        {
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Lsl,
                                Reg(Scratch1, 8),
                                Reg(Scratch1, 8),
                                ArmOperand.ImmediateOperand(Log2(node.Int32))));
                        }
                        else
                        {
                            _owner.EmitLoadImmediate(Scratch0, node.Int32, 8);
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Mul, Reg(Scratch1, 8), Reg(Scratch1, 8), Reg(Scratch0, 8)));
                        }
                    }

                    _owner.EmitAddImmediate(Scratch1, Scratch1, alignment - 1, Scratch0);
                    _owner.EmitLoadImmediate(Scratch0, -alignment, 8);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.And, Reg(Scratch1, 8), Reg(Scratch1, 8), Reg(Scratch0, 8)));
                    _owner.EmitMove(Scratch0, ArmRegister.Sp, 8);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Sub, Reg(Scratch0, 8), Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitMove(ArmRegister.Sp, Scratch0, 8);
                    _owner.EmitMove(destination, ArmRegister.Sp, 8);
                }

                private void EmitStackAllocFailure()
                {
                    if (_owner.EmbedsRuntime)
                    {
                        _owner.EmitLoadImmediate(ArmRegister.X0, 134, 8);
                        _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.FailFastSymbol));
                    }
                    EmitUnreachableTrap();
                }

                private void EmitDefaultValue(GenTree node)
                {
                    if (node.Results.Length == 0)
                        throw Unsupported(node, "Default value requires a destination");

                    RuntimeType? type = node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.StackKind;
                    if (node.Results.Length == 1)
                    {
                        EmitZeroOperand(node, node.Results[0], type, kind);
                        return;
                    }

                    var segments = MachineAbi.GetRegisterSegments(MachineAbi.ClassifyStorageValue(type, kind, Target), Target);
                    if (segments.Length != node.Results.Length)
                        throw Unsupported(node, "Default value fragment count does not match its storage ABI");
                    for (int i = 0; i < segments.Length; i++)
                        EmitZeroOperand(node, node.Results[i], null, StackKindForSegment(segments[i]));
                }

                private void EmitFloatConstant(GenTree node, int size, byte[] bytes)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    if (MachineRegisters.GetClass(destination) != RegisterClass.Float)
                        throw Unsupported(node, "Floating constant result is not in a floating-point register");

                    string label = _owner.AddConstantData(bytes, size, size == 4 ? "f32" : "f64");
                    _owner.EmitMaterializeAddress(label, ArmRegister.X16);
                    EmitMemoryLoad(ToArm(destination), ArmRegister.X16, 0, size, signed: false);
                }

                private void EmitUnary(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister source = RequireUseRegisterForOperand(node, 0, "unary operand");
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);

                    if (IsFloating(type, kind))
                    {
                        if (node.SourceOp != BytecodeOp.Neg)
                            throw Unsupported(node, $"Unsupported floating unary opcode {node.SourceOp}");
                        int floatSize = StorageSize(type, kind);
                        _owner.Emit(ArmInstruction.Binary(
                            ArmInstrKind.Fneg, Reg(ToArm(destination), floatSize), Reg(ToArm(source), floatSize)));
                        return;
                    }

                    int size = OperationSize(type, kind);
                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Neg:
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Sub,
                                Reg(GeneralRegister(destination), size),
                                Reg(ArmRegister.Xzr, size),
                                Reg(GeneralRegister(source), size)));
                            return;
                        case BytecodeOp.Not:
                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Mvn,
                                Reg(GeneralRegister(destination), size),
                                Reg(GeneralRegister(source), size)));
                            return;
                        case BytecodeOp.FnPtrToPtr:
                        case BytecodeOp.PtrToFnPtr:
                        case BytecodeOp.PtrToByRef:
                            _owner.EmitMove(GeneralRegister(destination), GeneralRegister(source), 8);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported unary opcode {node.SourceOp}");
                    }
                }

                private void EmitBinary(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister left = RequireUseRegisterForOperand(node, 0, "binary left operand");
                    RuntimeType? type = OperandType(node, 0);
                    GenStackKind kind = OperandStackKind(node, 0);

                    if (IsFloating(type, kind))
                    {
                        MachineRegister floatRight = RequireUseRegisterForOperand(node, 1, "binary right operand");
                        EmitFloatingBinary(node, destination, left, floatRight, StorageSize(type, kind));
                        return;
                    }

                    ArmRegister rightRegister;
                    if (TryGetContainedIntegerImmediate(node, 1, out long immediate))
                    {
                        if (TryEmitIntegerBinaryImmediate(node, destination, left, immediate, type, kind))
                            return;
                        _owner.EmitLoadImmediate(ArmRegister.X16, immediate, OperationSize(type, kind));
                        rightRegister = ArmRegister.X16;
                    }
                    else
                    {
                        rightRegister = GeneralRegister(RequireUseRegisterForOperand(node, 1, "binary right operand"));
                    }

                    EmitIntegerBinary(node, GeneralRegister(destination), GeneralRegister(left), rightRegister, type, kind);
                }

                private bool TryEmitIntegerBinaryImmediate(
                    GenTree node,
                    MachineRegister destination,
                    MachineRegister left,
                    long immediate,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    int size = OperationSize(type, kind);
                    ArmRegister rd = GeneralRegister(destination);
                    ArmRegister rn = GeneralRegister(left);

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add when IsAddSubImmediate(immediate):
                        case BytecodeOp.Sub when IsAddSubImmediate(-immediate):
                            _owner.Emit(ArmInstruction.Ternary(
                                node.SourceOp == BytecodeOp.Add ? ArmInstrKind.Add : ArmInstrKind.Sub,
                                Reg(rd, size), Reg(rn, size), ArmOperand.ImmediateOperand(immediate)));
                            return true;
                        case BytecodeOp.Shl:
                        case BytecodeOp.Shr:
                        case BytecodeOp.Shr_Un:
                            {
                                long amount = immediate & (size * 8 - 1);
                                bool unsigned = IsUnsigned(type) || node.SourceOp == BytecodeOp.Shr_Un;
                                ArmInstrKind opcode = node.SourceOp == BytecodeOp.Shl
                                    ? ArmInstrKind.Lsl
                                    : unsigned ? ArmInstrKind.Lsr : ArmInstrKind.Asr;
                                _owner.Emit(ArmInstruction.Ternary(
                                    opcode, Reg(rd, size), Reg(rn, size), ArmOperand.ImmediateOperand(amount)));
                                return true;
                            }
                        default:
                            return false;
                    }
                }

                private static bool IsAddSubImmediate(long value)
                {
                    if (value < 0)
                        return false;
                    if (value <= 0xFFF)
                        return true;
                    return (value & 0xFFF) == 0 && (value >> 12) <= 0xFFF;
                }

                private void EmitIntegerBinary(
                    GenTree node,
                    ArmRegister destination,
                    ArmRegister left,
                    ArmRegister right,
                    RuntimeType? type,
                    GenStackKind kind)
                {
                    int size = OperationSize(type, kind);
                    bool unsigned = IsUnsigned(type) ||
                        node.SourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un or BytecodeOp.Shr_Un or
                            BytecodeOp.Clt_Un or BytecodeOp.Cgt_Un;

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add:
                            EmitThreeAddress(ArmInstrKind.Add, destination, left, right, size);
                            return;
                        case BytecodeOp.Sub:
                            EmitThreeAddress(ArmInstrKind.Sub, destination, left, right, size);
                            return;
                        case BytecodeOp.Mul:
                            EmitThreeAddress(ArmInstrKind.Mul, destination, left, right, size);
                            return;
                        case BytecodeOp.And:
                            EmitThreeAddress(ArmInstrKind.And, destination, left, right, size);
                            return;
                        case BytecodeOp.Or:
                            EmitThreeAddress(ArmInstrKind.Orr, destination, left, right, size);
                            return;
                        case BytecodeOp.Xor:
                            EmitThreeAddress(ArmInstrKind.Eor, destination, left, right, size);
                            return;
                        case BytecodeOp.Shl:
                            EmitThreeAddress(ArmInstrKind.Lsl, destination, left, right, size);
                            return;
                        case BytecodeOp.Shr:
                        case BytecodeOp.Shr_Un:
                            EmitThreeAddress(unsigned ? ArmInstrKind.Lsr : ArmInstrKind.Asr, destination, left, right, size);
                            return;
                        case BytecodeOp.Div:
                        case BytecodeOp.Div_Un:
                            EmitDivide(node, destination, left, right, size, unsigned);
                            return;
                        case BytecodeOp.Rem:
                        case BytecodeOp.Rem_Un:
                            EmitRemainder(node, destination, left, right, size, unsigned);
                            return;
                        case BytecodeOp.Ceq:
                        case BytecodeOp.Clt:
                        case BytecodeOp.Clt_Un:
                        case BytecodeOp.Cgt:
                        case BytecodeOp.Cgt_Un:
                            _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(left, size), Reg(right, size)));
                            EmitConditionSet(destination, IntegerCondition(node.SourceOp, unsigned));
                            return;
                        case BytecodeOp.Add_Ovf:
                        case BytecodeOp.Add_Ovf_Un:
                        case BytecodeOp.Sub_Ovf:
                        case BytecodeOp.Sub_Ovf_Un:
                        case BytecodeOp.Mul_Ovf:
                        case BytecodeOp.Mul_Ovf_Un:
                            EmitCheckedIntegerBinary(node, destination, left, right, size);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported integer binary opcode {node.SourceOp}");
                    }
                }

                // The flags an add or a subtract already sets are the overflow answer; a multiply has to
                // compare the wide product against the narrow one it kept
                private void EmitCheckedIntegerBinary(
                    GenTree node,
                    ArmRegister destination,
                    ArmRegister left,
                    ArmRegister right,
                    int size)
                {
                    bool unsigned = node.SourceOp is BytecodeOp.Add_Ovf_Un or BytecodeOp.Sub_Ovf_Un or BytecodeOp.Mul_Ovf_Un;
                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_overflow_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_checked_done_{node.LinearId}");

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add_Ovf:
                        case BytecodeOp.Add_Ovf_Un:
                        case BytecodeOp.Sub_Ovf:
                        case BytecodeOp.Sub_Ovf_Un:
                        {
                            bool subtract = node.SourceOp is BytecodeOp.Sub_Ovf or BytecodeOp.Sub_Ovf_Un;
                            _owner.Emit(ArmInstruction.Ternary(
                                subtract ? ArmInstrKind.Subs : ArmInstrKind.Adds,
                                Reg(destination, size),
                                Reg(left, size),
                                Reg(right, size)));
                            ArmCondition condition = !unsigned
                                ? ArmCondition.Vs
                                : subtract ? ArmCondition.Lo : ArmCondition.Hs;
                            _owner.EmitConditionalJump(condition, overflow);
                            break;
                        }

                        default:
                            EmitCheckedMultiply(node, destination, left, right, size, unsigned, overflow);
                            break;
                    }

                    _owner.EmitJump(done);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(done);
                }

                private void EmitCheckedMultiply(
                    GenTree node,
                    ArmRegister destination,
                    ArmRegister left,
                    ArmRegister right,
                    int size,
                    bool unsigned,
                    string overflow)
                {
                    ArmRegister product = InternalGeneralRegister(node, 0);
                    if (size == 4)
                    {
                        // A 32 bit product is exact in 64 bits, so it overflows when it will not narrow back
                        EmitBitfieldMove(!unsigned, Scratch0, left, 32);
                        EmitBitfieldMove(!unsigned, Scratch1, right, 32);
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Mul, Reg(product, 8), Reg(Scratch0, 8), Reg(Scratch1, 8)));
                        EmitBitfieldMove(!unsigned, Scratch0, product, 32);
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(product, 8), Reg(Scratch0, 8)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, overflow);
                        _owner.EmitMove(destination, product, 4);
                        return;
                    }

                    _owner.Emit(ArmInstruction.Ternary(
                        unsigned ? ArmInstrKind.Umulh : ArmInstrKind.Smulh,
                        Reg(Scratch0, 8),
                        Reg(left, 8),
                        Reg(right, 8)));
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Mul, Reg(product, 8), Reg(left, 8), Reg(right, 8)));
                    if (unsigned)
                    {
                        EmitCompareBranch(ArmInstrKind.Cbnz, Scratch0, 8, overflow);
                    }
                    else
                    {
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Asr, Reg(Scratch1, 8), Reg(product, 8), ArmOperand.ImmediateOperand(63)));
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, overflow);
                    }
                    _owner.EmitMove(destination, product, 8);
                }

                private void EmitThreeAddress(ArmInstrKind opcode, ArmRegister destination, ArmRegister left, ArmRegister right, int size)
                    => _owner.Emit(ArmInstruction.Ternary(opcode, Reg(destination, size), Reg(left, size), Reg(right, size)));

                private void EmitDivide(GenTree node, ArmRegister destination, ArmRegister left, ArmRegister right, int size, bool unsigned)
                {
                    EmitDivideGuards(node, left, right, size, unsigned);
                    EmitThreeAddress(unsigned ? ArmInstrKind.Udiv : ArmInstrKind.Sdiv, destination, left, right, size);
                }

                private void EmitRemainder(GenTree node, ArmRegister destination, ArmRegister left, ArmRegister right, int size, bool unsigned)
                {
                    EmitDivideGuards(node, left, right, size, unsigned);
                    ArmRegister quotient = InternalGeneralRegister(node, 0);
                    EmitThreeAddress(unsigned ? ArmInstrKind.Udiv : ArmInstrKind.Sdiv, quotient, left, right, size);
                    _owner.Emit(ArmInstruction.Quaternary(
                        ArmInstrKind.Msub,
                        Reg(destination, size),
                        Reg(quotient, size),
                        Reg(right, size),
                        Reg(left, size)));
                }

                private void EmitDivideGuards(GenTree node, ArmRegister dividend, ArmRegister divisor, int size, bool unsigned)
                {
                    // The divide instruction returns zero rather than trapping, so the checks are explicit
                    if ((node.Flags & GenTreeFlags.DivModNoByZero) == 0)
                    {
                        string ok = _owner.CreateLocalLabel($"{_methodLabel}_div_nonzero_{node.LinearId}");
                        int offset = OffsetOfNextInstruction();
                        _owner.Emit(new ArmInstruction(
                            ArmInstrKind.Cbnz,
                            Reg(divisor, size),
                            ArmOperand.SymbolOperand(ok, ArmRelocationKind.CompareBranch)));
                        AddRelocation(offset, ok, ArmObjectRelocationKind.AArch64CompareBranch19);
                        EmitTrap();
                        _owner.DefineLabel(ok);
                    }

                    if (!unsigned && (node.Flags & GenTreeFlags.DivModNoOverflow) == 0)
                    {
                        string ok = _owner.CreateLocalLabel($"{_methodLabel}_div_nooverflow_{node.LinearId}");
                        _owner.EmitLoadImmediate(ArmRegister.X17, -1, size);
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(divisor, size), Reg(ArmRegister.X17, size)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, ok);
                        _owner.EmitLoadImmediate(ArmRegister.X17, size == 4 ? int.MinValue : long.MinValue, size);
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(dividend, size), Reg(ArmRegister.X17, size)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, ok);
                        EmitTrap();
                        _owner.DefineLabel(ok);
                    }
                }

                private void EmitFloatingBinary(GenTree node, MachineRegister destination, MachineRegister left, MachineRegister right, int size)
                {
                    ArmRegister a = ToArm(left);
                    ArmRegister b = ToArm(right);

                    switch (node.SourceOp)
                    {
                        case BytecodeOp.Add:
                        case BytecodeOp.Sub:
                        case BytecodeOp.Mul:
                        case BytecodeOp.Div:
                            {
                                ArmInstrKind opcode = node.SourceOp switch
                                {
                                    BytecodeOp.Add => ArmInstrKind.Fadd,
                                    BytecodeOp.Sub => ArmInstrKind.Fsub,
                                    BytecodeOp.Mul => ArmInstrKind.Fmul,
                                    _ => ArmInstrKind.Fdiv,
                                };
                                _owner.Emit(ArmInstruction.Ternary(
                                    opcode, Reg(ToArm(destination), size), Reg(a, size), Reg(b, size)));
                                return;
                            }
                        case BytecodeOp.Ceq:
                        case BytecodeOp.Clt:
                        case BytecodeOp.Cgt:
                            _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Fcmp, Reg(a, size), Reg(b, size)));
                            EmitConditionSet(
                                GeneralRegister(destination),
                                node.SourceOp switch
                                {
                                    BytecodeOp.Ceq => ArmCondition.Eq,
                                    BytecodeOp.Clt => ArmCondition.Mi,
                                    _ => ArmCondition.Gt,
                                });
                            return;
                        case BytecodeOp.Rem:
                        {
                            ArmRegister argument0 = ToArm(RegisterInfo.GetFloatArgumentRegister(Target, 0));
                            ArmRegister argument1 = ToArm(RegisterInfo.GetFloatArgumentRegister(Target, 1));
                            if (b != argument0)
                            {
                                EmitFloatRegisterMove(argument0, a, size);
                                EmitFloatRegisterMove(argument1, b, size);
                            }
                            else if (a != argument1)
                            {
                                EmitFloatRegisterMove(argument1, b, size);
                                EmitFloatRegisterMove(argument0, a, size);
                            }
                            else
                            {
                                EmitFloatRegisterMove(ArmRegister.V31, a, size);
                                EmitFloatRegisterMove(argument0, b, size);
                                EmitFloatRegisterMove(argument1, ArmRegister.V31, size);
                            }
                            MarkEhCallSite(node, "float_rem");
                            _owner.EmitCall(_owner.ResolveExternalSymbol(size == 4
                                ? ArmRuntime.FloatingRemainderSingleSymbol
                                : ArmRuntime.FloatingRemainderDoubleSymbol));
                            EmitFloatRegisterMove(
                                ToArm(destination), ToArm(RegisterInfo.GetFloatReturnRegister(Target, 0)), size);
                            return;
                        }
                        default:
                            throw Unsupported(node, $"Unsupported floating binary opcode {node.SourceOp}");
                    }
                }

                private void EmitFloatRegisterMove(ArmRegister destination, ArmRegister source, int size)
                {
                    if (destination != source)
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Fmov, Reg(destination, size), Reg(source, size)));
                }

                private void EmitConversion(GenTree node)
                {
                    MachineRegister destination = RequireResultRegister(node);
                    MachineRegister source = RequireUseRegisterForOperand(node, 0, "conversion operand");
                    RuntimeType? sourceType = OperandType(node, 0);
                    GenStackKind sourceKind = OperandStackKind(node, 0);

                    if ((node.ConvFlags & NumericConvFlags.Checked) != 0 &&
                        node.ConvKind is not (NumericConvKind.Bool or NumericConvKind.R4 or NumericConvKind.R8))
                    {
                        if (IsFloating(sourceType, sourceKind))
                            throw Unsupported(node, "Checked floating-point to integer conversions are not implemented");
                        EmitCheckedConversionGuard(node, source, sourceType, sourceKind);
                    }

                    bool sourceFloating = IsFloating(sourceType, sourceKind);
                    switch (node.ConvKind)
                    {
                        case NumericConvKind.R4:
                        case NumericConvKind.R8:
                            {
                                int destinationSize = node.ConvKind == NumericConvKind.R4 ? 4 : 8;
                                if (sourceFloating)
                                {
                                    int sourceSize = StorageSize(sourceType, sourceKind);
                                    if (destinationSize != sourceSize)
                                    {
                                        EmitFloatPrecisionConvert(ToArm(destination), ToArm(source), destinationSize);
                                        return;
                                    }
                                    if (destination != source)
                                    {
                                        _owner.Emit(ArmInstruction.Binary(
                                            ArmInstrKind.Fmov,
                                            Reg(ToArm(destination), destinationSize),
                                            Reg(ToArm(source), sourceSize)));
                                    }
                                    return;
                                }
                                _owner.Emit(ArmInstruction.Binary(
                                    IsUnsigned(sourceType) ? ArmInstrKind.Ucvtf : ArmInstrKind.Scvtf,
                                    Reg(ToArm(destination), destinationSize),
                                    Reg(GeneralRegister(source), OperationSize(sourceType, sourceKind))));
                                return;
                            }
                        case NumericConvKind.I1:
                        case NumericConvKind.U1:
                        case NumericConvKind.I2:
                        case NumericConvKind.U2:
                        case NumericConvKind.I4:
                        case NumericConvKind.U4:
                        case NumericConvKind.I8:
                        case NumericConvKind.U8:
                        case NumericConvKind.Char:
                        case NumericConvKind.NativeInt:
                        case NumericConvKind.NativeUInt:
                            {
                                if (sourceFloating)
                                {
                                    _owner.Emit(ArmInstruction.Binary(
                                        IsUnsignedConversion(node.ConvKind) ? ArmInstrKind.Fcvtzu : ArmInstrKind.Fcvtzs,
                                        Reg(GeneralRegister(destination), ConversionSize(node.ConvKind)),
                                        Reg(ToArm(source), StorageSize(sourceType, sourceKind))));
                                    return;
                                }
                                EmitIntegerNarrowing(node, GeneralRegister(destination), GeneralRegister(source));
                                return;
                            }
                        case NumericConvKind.Bool:
                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Cmp, Reg(GeneralRegister(source), 8), ArmOperand.ImmediateOperand(0)));
                            EmitConditionSet(GeneralRegister(destination), ArmCondition.Ne);
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported conversion kind {node.ConvKind}");
                    }
                }

                // A checked narrowing keeps the value only when reading it back at the target width and
                // signedness gives the same number
                private void EmitCheckedConversionGuard(
                    GenTree node,
                    MachineRegister source,
                    RuntimeType? sourceType,
                    GenStackKind sourceKind)
                {
                    (int targetBits, bool targetUnsigned) = node.ConvKind switch
                    {
                        NumericConvKind.I1 => (8, false),
                        NumericConvKind.U1 => (8, true),
                        NumericConvKind.I2 => (16, false),
                        NumericConvKind.U2 or NumericConvKind.Char => (16, true),
                        NumericConvKind.I4 => (32, false),
                        NumericConvKind.U4 => (32, true),
                        NumericConvKind.I8 or NumericConvKind.NativeInt => (64, false),
                        NumericConvKind.U8 or NumericConvKind.NativeUInt => (64, true),
                        _ => (0, false),
                    };
                    if (targetBits == 0)
                        return;

                    bool sourceUnsigned = IsUnsigned(sourceType) || (node.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;
                    bool negativeIsOverflow = !sourceUnsigned && targetUnsigned;
                    bool highBitIsOverflow = sourceUnsigned && !targetUnsigned && targetBits == 64;
                    bool checksWidth = targetBits < 64;
                    if (!negativeIsOverflow && !highBitIsOverflow && !checksWidth)
                        return;

                    bool sourceWidth64 = !IsI4(sourceType, sourceKind);
                    if (sourceWidth64)
                        _owner.EmitMove(Scratch0, GeneralRegister(source), 8);
                    else
                        EmitBitfieldMove(!sourceUnsigned, Scratch0, GeneralRegister(source), 32);

                    string overflow = _owner.CreateLocalLabel($"{_methodLabel}_conv_overflow_{node.LinearId}");
                    string valid = _owner.CreateLocalLabel($"{_methodLabel}_conv_valid_{node.LinearId}");

                    if (negativeIsOverflow || highBitIsOverflow)
                    {
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), ArmOperand.ImmediateOperand(0)));
                        _owner.EmitConditionalJump(ArmCondition.Lt, overflow);
                    }

                    if (checksWidth)
                    {
                        EmitBitfieldMove(!targetUnsigned, Scratch1, Scratch0, targetBits);
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, overflow);
                    }

                    _owner.EmitJump(valid);
                    _owner.DefineLabel(overflow);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(valid);
                }

                private void EmitIntegerNarrowing(GenTree node, ArmRegister destination, ArmRegister source)
                {
                    switch (node.ConvKind)
                    {
                        case NumericConvKind.I1:
                            EmitBitfieldMove(sign: true, destination, source, 8);
                            return;
                        case NumericConvKind.U1:
                            EmitBitfieldMove(sign: false, destination, source, 8);
                            return;
                        case NumericConvKind.I2:
                            EmitBitfieldMove(sign: true, destination, source, 16);
                            return;
                        case NumericConvKind.U2:
                            EmitBitfieldMove(sign: false, destination, source, 16);
                            return;
                        case NumericConvKind.I4:
                            EmitBitfieldMove(sign: true, destination, source, 32);
                            return;
                        case NumericConvKind.U4:
                            EmitBitfieldMove(sign: false, destination, source, 32);
                            return;
                        default:
                            _owner.EmitMove(destination, source, 8);
                            return;
                    }
                }

                // sbfm and ubfm have no mnemonic in the instruction model, so the extension encodes directly
                private void EmitBitfieldMove(bool sign, ArmRegister destination, ArmRegister source, int bits)
                {
                    uint word = (sign ? 0x93400000u : 0xD3400000u) |
                        ((uint)(bits - 1) << 10) |
                        ((uint)A64Index(source) << 5) |
                        (uint)A64Index(destination);
                    _owner.Emit(ArmInstruction.Raw(word));
                }

                private void EmitConditionSet(ArmRegister destination, ArmCondition condition)
                {
                    // cset is csinc with the inverted condition and both sources reading as zero
                    uint inverted = (uint)condition ^ 1u;
                    uint word = 0x9A9F07E0u | (inverted << 12) | (uint)A64Index(destination);
                    _owner.Emit(ArmInstruction.Raw(word));
                }

                private static ArmCondition IntegerCondition(BytecodeOp op, bool unsigned)
                    => op switch
                    {
                        BytecodeOp.Ceq => ArmCondition.Eq,
                        BytecodeOp.Clt => unsigned ? ArmCondition.Lo : ArmCondition.Lt,
                        BytecodeOp.Clt_Un => ArmCondition.Lo,
                        BytecodeOp.Cgt => unsigned ? ArmCondition.Hi : ArmCondition.Gt,
                        BytecodeOp.Cgt_Un => ArmCondition.Hi,
                        _ => throw new ArgumentOutOfRangeException(nameof(op)),
                    };

                private void EmitConditionalBranch(GenTree node)
                {
                    string target = LabelForTarget(node);
                    bool branchWhenTrue = node.TreeKind == GenTreeKind.BranchTrue;

                    if (node.Operands.Length == 2 && IsCompareOp(node.SourceOp))
                    {
                        MachineRegister left = RequireUseRegisterForOperand(node, 0, "compare left operand");
                        RuntimeType? type = OperandType(node, 0);
                        GenStackKind kind = OperandStackKind(node, 0);

                        if (IsFloating(type, kind))
                        {
                            MachineRegister right = RequireUseRegisterForOperand(node, 1, "compare right operand");
                            int floatSize = StorageSize(type, kind);
                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Fcmp, Reg(ToArm(left), floatSize), Reg(ToArm(right), floatSize)));
                            ArmCondition floatCondition = node.SourceOp switch
                            {
                                BytecodeOp.Ceq => ArmCondition.Eq,
                                BytecodeOp.Clt => ArmCondition.Mi,
                                BytecodeOp.Cgt => ArmCondition.Gt,
                                _ => throw Unsupported(node, $"Unsupported floating compare branch opcode {node.SourceOp}"),
                            };
                            _owner.EmitConditionalJump(branchWhenTrue ? floatCondition : Invert(floatCondition), target);
                            return;
                        }

                        bool unsigned = IsUnsigned(type) || node.SourceOp is BytecodeOp.Clt_Un or BytecodeOp.Cgt_Un;
                        int size = OperationSize(type, kind);
                        if (TryGetContainedIntegerImmediate(node, 1, out long immediate) && IsAddSubImmediate(immediate))
                        {
                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Cmp, Reg(GeneralRegister(left), size), ArmOperand.ImmediateOperand(immediate)));
                        }
                        else
                        {
                            ArmRegister rightRegister;
                            if (TryGetContainedIntegerImmediate(node, 1, out immediate))
                            {
                                _owner.EmitLoadImmediate(ArmRegister.X16, immediate, size);
                                rightRegister = ArmRegister.X16;
                            }
                            else
                            {
                                rightRegister = GeneralRegister(RequireUseRegisterForOperand(node, 1, "compare right operand"));
                            }

                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Cmp, Reg(GeneralRegister(left), size), Reg(rightRegister, size)));
                        }

                        ArmCondition condition = IntegerCondition(node.SourceOp, unsigned);
                        _owner.EmitConditionalJump(branchWhenTrue ? condition : Invert(condition), target);
                        return;
                    }

                    if (node.Uses.Length != 1)
                        throw Unsupported(node, "Conditional branch requires one condition operand");

                    ArmRegister conditionRegister = GeneralRegister(RequireUseRegisterForOperand(node, 0, "branch condition"));
                    int offset = OffsetOfNextInstruction();
                    _owner.Emit(new ArmInstruction(
                        branchWhenTrue ? ArmInstrKind.Cbnz : ArmInstrKind.Cbz,
                        Reg(conditionRegister, 8),
                        ArmOperand.SymbolOperand(target, ArmRelocationKind.CompareBranch)));
                    AddRelocation(offset, target, ArmObjectRelocationKind.AArch64CompareBranch19);
                }

                private static ArmCondition Invert(ArmCondition condition)
                    => (ArmCondition)((uint)condition ^ 1u);

                private void EmitReturn(GenTree node)
                {
                    if (node.Uses.Length != 0 && MachineAbi.RequiresHiddenReturnBuffer(_method.RuntimeMethod, Target))
                    {
                        EmitHiddenReturnBufferCopy(node);
                        return;
                    }

                    if (_ehMethod is not null &&
                        (FuncletIndexForBlock(node.BlockId) != 0 || ReturnMustRunFinallyBeforeMethodExit(node.BlockId)))
                    {
                        EmitReturnThroughEh(node);
                        return;
                    }

                    EmitEhFramePop();
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Ret, Reg(ArmRegister.X30, 8)));
                }

                private void EmitHiddenReturnBufferCopy(GenTree node)
                {
                    if (node.Uses.Length != 1 || !node.Uses[0].IsFrameSlot)
                        throw Unsupported(node, "Hidden return-buffer copy requires one frame-resident source");
                    if (!_method.StackFrame.TryGetArgumentSlot(_method.ArgTypes.Length, out StackFrameSlot hiddenSlot))
                        throw Unsupported(node, "Hidden return-buffer home is missing from the finalized frame");

                    RegisterOperand hiddenHome = RegisterOperand.ForFrameSlot(
                        RegisterClass.General,
                        hiddenSlot.Kind,
                        _method.StackFrame.UsesFramePointer ? RegisterFrameBase.FramePointer : RegisterFrameBase.StackPointer,
                        hiddenSlot.Index,
                        hiddenSlot.Offset,
                        hiddenSlot.Size);
                    ArmRegister buffer = GeneralRegister(RegisterInfo.GetIntegerReturnRegister(Target, 0));
                    EmitMemoryLoad(
                        buffer, FrameBase(hiddenHome), EffectiveFrameOffset(hiddenHome), Target.PointerSize, signed: false);

                    RuntimeType? returnType = _method.RuntimeMethod.ReturnType;
                    int size = StorageSize(returnType, MachineAbi.StackKindForType(returnType), node.Uses[0]);
                    _owner.EmitAddImmediate(Scratch0, FrameBase(node.Uses[0]), EffectiveFrameOffset(node.Uses[0]), Scratch0);
                    EmitBlockCopy(Scratch1, buffer, 0, Scratch0, 0, size);
                    EmitEhFramePop();
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Ret, Reg(ArmRegister.X30, 8)));
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

                // A return that owes a finally leaves through the dispatcher, which comes back to a thunk
                // that tears the frame down for real
                private void EmitReturnThroughEh(GenTree node)
                {
                    _returnThunkNeeded = true;
                    MarkEhCallSite(node, "return");
                    _owner.EmitMaterializeAddress(_returnThunkLabel, ArmRegister.X0);
                    _owner.EmitLoadImmediate(ArmRegister.X1, 2, 8);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.LeaveSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitReturnThunk()
                {
                    _owner.DefineLabel(_returnThunkLabel);
                    EmitEhFramePop();
                    if (_method.StackFrame.UsesFramePointer)
                        _owner.EmitMove(ArmRegister.Sp, ArmRegister.X29, 8);

                    for (int i = _method.StackFrame.CalleeSavedSlots.Length - 1; i >= 0; i--)
                    {
                        StackFrameSlot slot = _method.StackFrame.CalleeSavedSlots[i];
                        EmitMemoryLoad(ToArm(slot.SavedRegister), ArmRegister.Sp, slot.Offset, slot.Size, signed: false);
                    }

                    if (_method.StackFrame.FrameSize > 0)
                        _owner.EmitAdjustStack(_method.StackFrame.FrameSize);
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Ret, Reg(ArmRegister.X30, 8)));
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

                // RhEhFrame is three pointers, and the dispatcher finds the active one by the frame count
                private void EmitEhFrameAddress(ArmRegister countValue, ArmRegister destination)
                {
                    _owner.EmitLoadImmediate(Scratch0, checked(Target.PointerSize * 3), 8);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Mul, Reg(Scratch0, 8), Reg(countValue, 8), Reg(Scratch0, 8)));
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.EhFramesSymbol), destination);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(destination, 8), Reg(destination, 8), Reg(Scratch0, 8)));
                }

                private void EmitEhFramePush(string currentIpLabel)
                {
                    if (_ehMethod is null)
                        return;

                    string countSymbol = _owner.ResolveExternalObjectSymbol(ArmRuntime.EhFrameCountSymbol);
                    string capacityAvailable = _owner.CreateLocalLabel(_methodLabel + "_eh_frame_capacity");

                    _owner.EmitMaterializeAddress(countSymbol, Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, Target.PointerSize, signed: false);
                    _owner.EmitLoadImmediate(ArmRegister.X30, 4096, 8);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 8), Reg(ArmRegister.X30, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Lo, capacityAvailable);
                    _owner.EmitLoadImmediate(ArmRegister.X0, 150, 8);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.FailFastSymbol));
                    EmitUnreachableTrap();
                    _owner.DefineLabel(capacityAvailable);

                    EmitEhFrameAddress(Scratch1, ArmRegister.X30);
                    _owner.EmitMaterializeAddress(_ehMethod.InfoLabel, Scratch0);
                    EmitMemoryStore(Scratch0, ArmRegister.X30, 0, Target.PointerSize);
                    EmitMemoryStore(ArmRegister.X29, ArmRegister.X30, Target.PointerSize, Target.PointerSize);
                    _owner.EmitMaterializeAddress(currentIpLabel, Scratch0);
                    EmitMemoryStore(Scratch0, ArmRegister.X30, checked(Target.PointerSize * 2), Target.PointerSize);
                    _owner.EmitAddImmediate(Scratch1, Scratch1, 1, Scratch0);
                    _owner.EmitMaterializeAddress(countSymbol, Scratch0);
                    EmitMemoryStore(Scratch1, Scratch0, 0, Target.PointerSize);
                    _ehFrameRegistered = true;
                }

                private void EmitEhSetCurrentIp(string currentIpLabel)
                {
                    if (_ehMethod is null)
                        return;
                    if (!_ehFrameRegistered)
                        throw new InvalidOperationException(
                            $"Method M{_method.RuntimeMethod.MethodId} updates EH state before establishing its frame.");

                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.EhFrameCountSymbol), Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(Scratch1, Scratch1, -1, Scratch0);
                    EmitEhFrameAddress(Scratch1, ArmRegister.X30);
                    _owner.EmitMaterializeAddress(currentIpLabel, Scratch0);
                    EmitMemoryStore(Scratch0, ArmRegister.X30, checked(Target.PointerSize * 2), Target.PointerSize);
                }

                private void EmitEhFramePop()
                {
                    if (_ehMethod is null)
                        return;

                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.EhFrameCountSymbol), Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(Scratch1, Scratch1, -1, Scratch1);
                    EmitMemoryStore(Scratch1, Scratch0, 0, Target.PointerSize);
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

                // The dispatcher resumes a handler with the registers the protected code was holding
                private void EmitEhSaveRegisterContext()
                {
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.EhFrameCountSymbol), Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(Scratch1, Scratch1, -1, Scratch0);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Lsl, Reg(Scratch1, 8), Reg(Scratch1, 8), ArmOperand.ImmediateOperand(9)));
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.EhRegisterContextsSymbol), Scratch0);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(Scratch1, 8), Reg(Scratch1, 8), Reg(Scratch0, 8)));

                    var generalRegisters = RegisterInfo.AllocatableGeneralRegisters(Target);
                    for (int i = 0; i < generalRegisters.Length; i++)
                    {
                        EmitMemoryStore(
                            ToArm(generalRegisters[i]), Scratch1, (byte)generalRegisters[i] * 8, Target.GeneralRegisterSize);
                    }

                    var floatRegisters = RegisterInfo.AllocatableFloatingRegisters(Target);
                    for (int i = 0; i < floatRegisters.Length; i++)
                    {
                        int index = (byte)floatRegisters[i] - (byte)MachineRegister.F0;
                        EmitMemoryStore(ToArm(floatRegisters[i]), Scratch1, 256 + index * 8, 8);
                    }
                }

                private void EmitExceptionObject(GenTree node)
                {
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.CurrentExceptionSymbol), Scratch0);
                    EmitMemoryLoad(
                        GeneralRegister(RequireResultRegister(node)), Scratch0, 0, Target.PointerSize, signed: false);
                }

                private void EmitThrow(GenTree node)
                {
                    ArmRegister exception = GeneralRegister(RequireUseRegisterForOperand(node, 0, "exception object"));
                    MarkEhCallSite(node, "throw");
                    _owner.EmitMove(ArmRegister.X0, exception, 8);
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_throw_non_null_{node.LinearId}");
                    EmitCompareBranch(ArmInstrKind.Cbnz, ArmRegister.X0, 8, nonNull);
                    _owner.EmitMaterializeAddress(
                        _owner.GetStaticExceptionObjectLabel("System", "NullReferenceException"), ArmRegister.X0);
                    _owner.DefineLabel(nonNull);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.ThrowSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitRethrow(GenTree node)
                {
                    MarkEhCallSite(node, "rethrow");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.RethrowSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitLeave(GenTree node)
                {
                    MarkEhCallSite(node, "leave");
                    _owner.EmitMaterializeAddress(LabelForTarget(node), ArmRegister.X0);
                    _owner.EmitLoadImmediate(ArmRegister.X1, 1, 8);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.LeaveSymbol));
                    EmitUnreachableTrap();
                }

                private void EmitEndFinally(GenTree node)
                {
                    MarkEhCallSite(node, "endfinally");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.EndFinallySymbol));
                    EmitUnreachableTrap();
                }

                private void EmitUnreachableTrap()
                    => _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(1)));

                private void EmitIntrinsic(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "Intrinsic has no runtime method");
                    if (!RuntimeIntrinsics.TryResolve(method, Target, out RuntimeIntrinsicInfo intrinsic) ||
                        intrinsic.Id != node.IntrinsicId)
                    {
                        throw Unsupported(node, $"Unrecognized runtime intrinsic {node.IntrinsicId}");
                    }

                    switch (intrinsic.Id)
                    {
                        case RuntimeIntrinsicId.InterlockedCompareExchange:
                            EmitInterlocked(node, intrinsic.CompareExchange.Size, InterlockedKind.CompareExchange);
                            return;
                        case RuntimeIntrinsicId.InterlockedExchangeAdd:
                            EmitInterlocked(node, intrinsic.ExchangeAdd.Size, InterlockedKind.ExchangeAdd);
                            return;
                        case RuntimeIntrinsicId.InterlockedExchange:
                            EmitInterlocked(node, intrinsic.Exchange.Size, InterlockedKind.Exchange);
                            return;
                        case RuntimeIntrinsicId.MemoryBarrier:
                            EmitDataMemoryBarrier();
                            return;
                        default:
                            throw Unsupported(node, $"Unsupported runtime intrinsic {intrinsic.Id}");
                    }
                }

                private enum InterlockedKind : byte
                {
                    CompareExchange,
                    Exchange,
                    ExchangeAdd,
                }

                private void EmitDataMemoryBarrier()
                    => _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Dmb, ArmOperand.ImmediateOperand(0xb)));

                // The node uses the call ABI and kills the caller-saved set, so an argument register past
                // its own operands is free to carry the exclusive store status
                private void EmitInterlocked(GenTree node, int size, InterlockedKind kind)
                {
                    if (size is not (1 or 2 or 4 or 8))
                        throw Unsupported(node, $"Unsupported interlocked access size {size}");

                    int operandCount = kind == InterlockedKind.CompareExchange ? 3 : 2;
                    if (node.Uses.Length != operandCount)
                        throw Unsupported(node, "Interlocked intrinsic operand count does not match its ABI");
                    for (int i = 0; i < operandCount; i++)
                    {
                        if (!node.Uses[i].IsRegister)
                            throw Unsupported(node, "Interlocked intrinsic requires scalar ABI register operands");
                    }

                    ArmRegister location = GeneralRegister(node.Uses[0].Register);
                    ArmRegister value = GeneralRegister(node.Uses[1].Register);
                    ArmRegister result = GeneralRegister(RegisterInfo.GetIntegerReturnRegister(Target, 0));
                    ArmRegister status = GeneralRegister(RegisterInfo.GetIntegerArgumentRegister(Target, 7));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitObjectNullCheck(node, location);

                    _owner.EmitMove(Scratch0, location, 8);
                    EmitDataMemoryBarrier();

                    int operandSize = size < 4 ? 4 : size;
                    ArmInstrKind loadOpcode = size switch
                    {
                        1 => ArmInstrKind.Ldaxrb,
                        2 => ArmInstrKind.Ldaxrh,
                        _ => ArmInstrKind.Ldaxr,
                    };
                    ArmInstrKind storeOpcode = size switch
                    {
                        1 => ArmInstrKind.Stlxrb,
                        2 => ArmInstrKind.Stlxrh,
                        _ => ArmInstrKind.Stlxr,
                    };

                    string retry = _owner.CreateLocalLabel($"{_methodLabel}_interlocked_retry_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_interlocked_done_{node.LinearId}");
                    _owner.DefineLabel(retry);
                    _owner.Emit(ArmInstruction.Binary(
                        loadOpcode, Reg(result, operandSize), Mem(Scratch0, 0, size)));

                    ArmRegister stored = value;
                    if (kind == InterlockedKind.CompareExchange)
                    {
                        ArmRegister comparand = GeneralRegister(node.Uses[2].Register);
                        _owner.Emit(ArmInstruction.Binary(
                            ArmInstrKind.Cmp, Reg(result, operandSize), Reg(comparand, operandSize)));
                        _owner.EmitConditionalJump(ArmCondition.Ne, done);
                    }
                    else if (kind == InterlockedKind.ExchangeAdd)
                    {
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Add, Reg(Scratch1, operandSize), Reg(result, operandSize), Reg(value, operandSize)));
                        stored = Scratch1;
                    }

                    _owner.Emit(ArmInstruction.Ternary(
                        storeOpcode, Reg(status, 4), Reg(stored, operandSize), Mem(Scratch0, 0, size)));
                    EmitCompareBranch(ArmInstrKind.Cbnz, status, 4, retry);

                    _owner.DefineLabel(done);
                    EmitDataMemoryBarrier();
                }

                private void EmitCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "Call node has no runtime method");
                    if (method.HasInternalCall)
                    {
                        if (ArmRuntime.TryEvaluateIsReferenceOrContainsReferences(method, out bool containsReferences))
                        {
                            _owner.EmitLoadImmediate(ArmRegister.X0, containsReferences ? 1 : 0, 8);
                            return;
                        }
                        if (ArmRuntime.IsAllocateNewArrayInternalCall(method))
                        {
                            EmitAllocateNewArray(node, method);
                            return;
                        }
                        if (ArmRuntime.IsGcSafePointInternalCall(method))
                        {
                            EmitFastAllocateString(node, method);
                            return;
                        }
                    }

                    MarkEhCallSite(node, "call");
                    _owner.EmitCall(_owner.ResolveMethodLabel(method));
                }

                private void EmitVirtualCall(GenTree node)
                {
                    RuntimeMethod method = node.Method ?? throw Unsupported(node, "VirtualCall node has no runtime method");
                    if (!method.HasThis)
                        throw Unsupported(node, "VirtualCall target has no implicit this parameter");

                    MachineRegister receiverRegister = RequireVirtualCallReceiverRegister(node);
                    if (receiverRegister != RegisterInfo.GetIntegerArgumentRegister(Target, 0))
                        throw Unsupported(node, "VirtualCall receiver is not in the first integer argument register");

                    ArmRegister receiver = GeneralRegister(receiverRegister);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitObjectNullCheck(node, receiver);

                    if (method.DeclaringType.Kind != RuntimeTypeKind.Interface &&
                        (method.DeclaringType.IsValueType || method.VTableSlot < 0))
                    {
                        EmitCall(node);
                        return;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "virtual_call");
                    if (method.DeclaringType.Kind == RuntimeTypeKind.Interface)
                    {
                        EmitInterfaceVirtualCall(node, method, receiver, safePoint);
                        return;
                    }

                    if (method.VTableSlot < 0)
                        throw Unsupported(node, "Class virtual call target has no vtable slot");

                    int vtablePointerOffset = checked(16 + Target.PointerSize * 2);
                    EmitMemoryLoad(Scratch0, receiver, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch0, Scratch0, vtablePointerOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(
                        Scratch0,
                        Scratch0,
                        checked(method.VTableSlot * Target.PointerSize),
                        Target.PointerSize,
                        signed: false);
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Blr, Reg(Scratch0, 8)));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                // The link register is dead until the dispatch is made, which is the third register the
                // walk needs beside the two the generator keeps out of allocation
                private void EmitInterfaceVirtualCall(
                    GenTree node,
                    RuntimeMethod method,
                    ArmRegister receiver,
                    SafePointDraft safePoint)
                {
                    string cellLabel = _owner.CreateInterfaceDispatchCell(method);
                    string loop = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_loop_{node.LinearId}");
                    string found = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_found_{node.LinearId}");
                    string missing = _owner.CreateLocalLabel($"{_methodLabel}_interface_dispatch_missing_{node.LinearId}");

                    EmitMemoryLoad(Scratch0, receiver, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(cellLabel, ArmRegister.X30);
                    _owner.EmitAddImmediate(ArmRegister.X30, ArmRegister.X30, Target.PointerSize, ArmRegister.X30);

                    _owner.DefineLabel(loop);
                    EmitMemoryLoad(Scratch1, ArmRegister.X30, 0, Target.PointerSize, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, Scratch1, 8, missing);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 8), Reg(Scratch0, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, found);
                    _owner.EmitAddImmediate(
                        ArmRegister.X30, ArmRegister.X30, checked(Target.PointerSize * 2), ArmRegister.X30);
                    _owner.EmitJump(loop);

                    _owner.DefineLabel(missing);
                    _owner.EmitJump(_owner.GetVirtualDispatchFailureStubLabel());

                    _owner.DefineLabel(found);
                    EmitMemoryLoad(Scratch0, ArmRegister.X30, Target.PointerSize, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Blr, Reg(Scratch0, 8)));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

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
                    RuntimeMethod targetMethod = node.Method ?? throw Unsupported(node, "NewDelegate node has no target method");
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
                        if (node.Uses.Length != 1 || !node.Uses[0].IsRegister)
                            throw Unsupported(node, "Closed NewDelegate requires exactly one register target operand");
                        ArmRegister target = GeneralRegister(node.Uses[0].Register);
                        EmitObjectNullCheck(node, target);
                        _owner.EmitAdjustStack(-temporarySize);
                        EmitMemoryStore(target, ArmRegister.Sp, 0, Target.PointerSize);
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(delegateType), ArmRegister.X0);
                    MarkEhCallSite(node, "new_delegate");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewFastSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);

                    if (closed)
                        EmitMemoryLoad(Scratch0, ArmRegister.Sp, 0, Target.PointerSize, signed: false);
                    else
                        _owner.EmitMove(Scratch0, ArmRegister.Xzr, 8);
                    EmitMemoryStore(Scratch0, ArmRegister.X0, targetOffset, Target.PointerSize);
                    _owner.EmitMaterializeAddress(thunkLabel, Scratch1);
                    EmitMemoryStore(Scratch1, ArmRegister.X0, methodPtrOffset, Target.PointerSize);
                    EmitMemoryStore(ArmRegister.Xzr, ArmRegister.X0, invocationListOffset, Target.PointerSize);
                    _owner.EmitLoadImmediate(Scratch0, 1, 8);
                    EmitMemoryStore(Scratch0, ArmRegister.X0, invocationCountOffset, Target.PointerSize);
                    _owner.EmitMove(GeneralRegister(node.Results[0].Register), ArmRegister.X0, 8);

                    if (temporarySize != 0)
                        _owner.EmitAdjustStack(temporarySize);
                }

                // The delegate ABI is re-formed on a private frame so the invocation list can be walked
                // with the arguments intact for every leaf
                private void EmitDelegateInvoke(GenTree node)
                {
                    RuntimeMethod invokeMethod = node.Method ?? throw Unsupported(node, "DelegateInvoke node has no Invoke method");
                    RuntimeType delegateType = invokeMethod.DeclaringType;
                    DelegateAbiBundle abi = _owner.GetDelegateInvokeAbi(invokeMethod);
                    if (abi.OrderedSlices.Length != node.Uses.Length)
                        throw Unsupported(node, "DelegateInvoke ABI slice count does not match its operands");

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

                    LoadDelegateInvokeOperand(node, node.Uses[receiverUseIndex], Scratch0, Target.PointerSize);
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitObjectNullCheck(node, Scratch0);

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
                    int frameSize = AlignUp(checked(oldStackPointerOffset + Target.PointerSize), Target.CallFrameAlignment);
                    int receiverSaveOffset = checked(saveOffset + abi.OrderedSlices[receiverUseIndex].SaveOffset);

                    _owner.EmitMove(Scratch0, ArmRegister.Sp, 8);
                    _owner.EmitAdjustStack(-frameSize);
                    EmitMemoryStore(Scratch0, ArmRegister.Sp, oldStackPointerOffset, Target.PointerSize);
                    for (int i = 0; i < node.Uses.Length; i++)
                        SaveDelegateInvokeOperand(node, node.Uses[i], abi.OrderedSlices[i], saveOffset, frameSize);

                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch1, Scratch0, invocationCountOffset, Target.PointerSize, signed: false);
                    string multicastLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_multicast_{node.LinearId}");
                    string doneLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_done_{node.LinearId}");
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 8), ArmOperand.ImmediateOperand(1)));
                    _owner.EmitConditionalJump(ArmCondition.Hi, multicastLabel);

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    SafePointDraft singleSafePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "delegate_single");
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch0, Scratch0, methodPtrOffset, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Blr, Reg(Scratch0, 8)));
                    _owner.DefineLabel(singleSafePoint.ReturnLabel);
                    _owner.EmitJump(doneLabel);

                    _owner.DefineLabel(multicastLabel);
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch0, Scratch0, invocationListOffset, Target.PointerSize, signed: false);
                    string listValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_list_valid_{node.LinearId}");
                    EmitCompareBranch(ArmInstrKind.Cbnz, Scratch0, 8, listValidLabel);
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(listValidLabel);
                    EmitMemoryStore(Scratch0, ArmRegister.Sp, listOffset, Target.PointerSize);
                    EmitMemoryStore(Scratch1, ArmRegister.Sp, countOffset, Target.PointerSize);
                    EmitMemoryStore(ArmRegister.Xzr, ArmRegister.Sp, indexOffset, Target.PointerSize);

                    string loopLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_loop_{node.LinearId}");
                    _owner.DefineLabel(loopLabel);
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, indexOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch1, ArmRegister.Sp, countOffset, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Hs, doneLabel);
                    EmitMemoryLoad(Scratch1, ArmRegister.Sp, listOffset, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(Scratch1, Scratch1, Target.ArrayDataOffset, Scratch1);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Lsl, Reg(Scratch0, 8), Reg(Scratch0, 8), ArmOperand.ImmediateOperand(Log2(Target.PointerSize))));
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(Scratch1, 8), Reg(Scratch1, 8), Reg(Scratch0, 8)));
                    EmitMemoryLoad(Scratch0, Scratch1, 0, Target.PointerSize, signed: false);
                    string leafValidLabel = _owner.CreateLocalLabel($"{_methodLabel}_delegate_leaf_valid_{node.LinearId}");
                    EmitCompareBranch(ArmInstrKind.Cbnz, Scratch0, 8, leafValidLabel);
                    EmitDelegateFailFast(152);
                    _owner.DefineLabel(leafValidLabel);
                    EmitMemoryStore(Scratch0, ArmRegister.Sp, receiverSaveOffset, Target.PointerSize);

                    RestoreDelegateInvokeAbi(abi, saveOffset);
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, listOffset, Target.PointerSize, signed: false);
                    SafePointDraft multicastSafePoint = PrepareSafePoint(
                        node, RegisterOperand.ForRegister(MachineRegisterOf(Scratch0)));
                    MarkEhCallSite(node, "delegate_multicast");
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, receiverSaveOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch0, Scratch0, methodPtrOffset, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Blr, Reg(Scratch0, 8)));
                    _owner.DefineLabel(multicastSafePoint.ReturnLabel);
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, indexOffset, Target.PointerSize, signed: false);
                    _owner.EmitAddImmediate(Scratch0, Scratch0, 1, Scratch1);
                    EmitMemoryStore(Scratch0, ArmRegister.Sp, indexOffset, Target.PointerSize);
                    _owner.EmitJump(loopLabel);

                    _owner.DefineLabel(doneLabel);
                    EmitMemoryLoad(Scratch0, ArmRegister.Sp, oldStackPointerOffset, Target.PointerSize, signed: false);
                    _owner.EmitMove(ArmRegister.Sp, Scratch0, 8);
                }

                private void SaveDelegateInvokeOperand(
                    GenTree node,
                    RegisterOperand operand,
                    DelegateAbiSlice slice,
                    int saveBase,
                    int frameSize)
                {
                    int destinationOffset = checked(saveBase + slice.SaveOffset);
                    if (operand.IsRegister)
                    {
                        EmitMemoryStore(ToArm(operand.Register), ArmRegister.Sp, destinationOffset, slice.Size);
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw Unsupported(node, "Delegate ABI operand is not finalized");

                    // The caller frame moved under the private frame, so its slots are that much further up
                    ArmRegister frameBase = FrameBase(operand);
                    int sourceOffset = EffectiveFrameOffset(operand);
                    if (frameBase == ArmRegister.Sp)
                        sourceOffset = checked(sourceOffset + frameSize);
                    _owner.EmitAddImmediate(Scratch0, frameBase, sourceOffset, Scratch0);
                    _owner.EmitAddImmediate(Scratch1, ArmRegister.Sp, destinationOffset, Scratch1);
                    EmitBlockCopy(ArmRegister.X30, Scratch1, 0, Scratch0, 0, slice.Size);
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
                                ToArm(slice.Location.Register),
                                ArmRegister.Sp,
                                sourceOffset,
                                slice.Size,
                                signed: slice.IsGeneralWord);
                            continue;
                        }

                        int destinationOffset = checked(
                            slice.Location.StackSlotIndex * Target.StackSlotSize + slice.Location.StackOffset);
                        _owner.EmitAddImmediate(Scratch0, ArmRegister.Sp, sourceOffset, Scratch0);
                        _owner.EmitAddImmediate(Scratch1, ArmRegister.Sp, destinationOffset, Scratch1);
                        EmitBlockCopy(ArmRegister.X30, Scratch1, 0, Scratch0, 0, slice.Size);
                    }
                }

                private void LoadDelegateInvokeOperand(GenTree node, RegisterOperand operand, ArmRegister destination, int size)
                {
                    if (operand.IsRegister)
                    {
                        _owner.EmitMove(destination, GeneralRegister(operand.Register), 8);
                        return;
                    }
                    if (!operand.IsFrameSlot)
                        throw Unsupported(node, "Delegate receiver operand is not finalized");
                    EmitMemoryLoad(destination, FrameBase(operand), EffectiveFrameOffset(operand), size, signed: false);
                }

                private void EmitDelegateFailFast(int code)
                {
                    if (_owner.EmbedsRuntime)
                    {
                        _owner.EmitLoadImmediate(ArmRegister.X0, code, 8);
                        _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.FailFastSymbol));
                    }
                    EmitUnreachableTrap();
                }

                private void EmitDelegateCombineOrRemove(GenTree node, bool remove)
                {
                    if (node.Uses.Length != 2 || node.Results.Length != 1 || !node.Results[0].IsRegister ||
                        !node.Uses[0].IsRegister || !node.Uses[1].IsRegister)
                    {
                        throw Unsupported(node, "Delegate combine/remove requires two register operands and one register result");
                    }

                    RuntimeType delegateLayoutType = _owner.FindSystemType("MulticastDelegate");
                    RuntimeType delegateArrayType = _owner.GetDelegateInvocationListArrayType();
                    ArmRegister left = GeneralRegister(node.Uses[0].Register);
                    ArmRegister right = GeneralRegister(node.Uses[1].Register);
                    ArmRegister result = GeneralRegister(node.Results[0].Register);
                    string leftNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_left_null_{node.LinearId}");
                    string rightNull = _owner.CreateLocalLabel($"{_methodLabel}_delegate_right_null_{node.LinearId}");
                    string typesMatch = _owner.CreateLocalLabel($"{_methodLabel}_delegate_types_match_{node.LinearId}");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_delegate_combine_done_{node.LinearId}");

                    EmitCompareBranch(ArmInstrKind.Cbz, left, 8, leftNull);
                    EmitCompareBranch(ArmInstrKind.Cbz, right, 8, rightNull);
                    EmitMemoryLoad(Scratch0, left, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch1, right, 0, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, typesMatch);
                    if (remove)
                    {
                        _owner.EmitMove(result, left, 8);
                        _owner.EmitJump(done);
                    }
                    else
                    {
                        EmitManagedExceptionThrow(node, "ArgumentException");
                    }

                    _owner.DefineLabel(typesMatch);
                    int frameSize = AlignUp(checked(Target.PointerSize * 3), Target.CallFrameAlignment);
                    _owner.EmitAdjustStack(-frameSize);
                    EmitMemoryStore(left, ArmRegister.Sp, 0, Target.PointerSize);
                    EmitMemoryStore(right, ArmRegister.Sp, Target.PointerSize, Target.PointerSize);
                    EmitMemoryStore(Scratch0, ArmRegister.Sp, checked(Target.PointerSize * 2), Target.PointerSize);
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    MarkEhCallSite(node, remove ? "delegate_remove" : "delegate_combine");
                    EmitMemoryLoad(ArmRegister.X0, ArmRegister.Sp, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(ArmRegister.X1, ArmRegister.Sp, Target.PointerSize, Target.PointerSize, signed: false);
                    EmitMemoryLoad(ArmRegister.X2, ArmRegister.Sp, checked(Target.PointerSize * 2), Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(delegateArrayType), ArmRegister.X3);
                    _owner.EmitLoadImmediate(ArmRegister.X4, _owner.FindDelegateFieldOffset(delegateLayoutType, "_target"), 8);
                    _owner.EmitLoadImmediate(ArmRegister.X5, _owner.FindDelegateFieldOffset(delegateLayoutType, "_methodPtr"), 8);
                    _owner.EmitLoadImmediate(ArmRegister.X6, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationList"), 8);
                    _owner.EmitLoadImmediate(ArmRegister.X7, _owner.FindDelegateFieldOffset(delegateLayoutType, "_invocationCount"), 8);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(
                        remove ? ArmRuntime.DelegateRemoveSymbol : ArmRuntime.DelegateCombineSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.EmitMove(result, ArmRegister.X0, 8);
                    _owner.EmitAdjustStack(frameSize);
                    _owner.EmitJump(done);

                    _owner.DefineLabel(leftNull);
                    _owner.EmitMove(result, remove ? left : right, 8);
                    _owner.EmitJump(done);
                    _owner.DefineLabel(rightNull);
                    _owner.EmitMove(result, left, 8);
                    _owner.DefineLabel(done);
                }

                private void EmitIndirectCall(GenTree node)
                {
                    if (node.Uses.Length == 0 || !node.Uses[^1].IsRegister)
                        throw Unsupported(node, "Indirect call target is not in a register");

                    ArmRegister target = GeneralRegister(node.Uses[^1].Register);
                    MarkEhCallSite(node, "indirect_call");
                    _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Blr, Reg(target, 8)));
                }

                private void EmitNullCheck(GenTree node)
                {
                    if (node.Uses.Length != 1 || !node.Uses[0].IsRegister)
                        throw Unsupported(node, "Null check requires a register operand");

                    string ok = _owner.CreateLocalLabel($"{_methodLabel}_notnull_{node.LinearId}");
                    int offset = OffsetOfNextInstruction();
                    _owner.Emit(new ArmInstruction(
                        ArmInstrKind.Cbnz,
                        Reg(GeneralRegister(node.Uses[0].Register), 8),
                        ArmOperand.SymbolOperand(ok, ArmRelocationKind.CompareBranch)));
                    AddRelocation(offset, ok, ArmObjectRelocationKind.AArch64CompareBranch19);
                    EmitTrap();
                    _owner.DefineLabel(ok);
                }

                private void EmitGcPoll(GenTree node)
                {
                    if (!_owner.EmbedsRuntime)
                        return;

                    string slowLabel = _owner.CreateLocalLabel($"{_methodLabel}_gc_poll_slow_{node.LinearId}");
                    string continuationLabel = _owner.CreateLocalLabel($"{_methodLabel}_gc_poll_continue_{node.LinearId}");
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.GcPollRequestedSymbol),
                        Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, 4, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbnz, Scratch1, 4, slowLabel);
                    _owner.DefineLabel(continuationLabel);
                    _gcPollStubs.Add(new GcPollStub(node, slowLabel, continuationLabel));
                }

                // The hot path is promised its caller-saved registers survive, so the stub keeps the
                // ones still holding a live value and puts the register roots back afterwards
                private void EmitGcPollStubs()
                {
                    if (_gcPollStubs.Count == 0)
                        return;

                    string endLabel = _owner.CreateLocalLabel(_methodLabel + "_gc_poll_stubs_end");
                    _owner.EmitJump(endLabel);
                    for (int i = 0; i < _gcPollStubs.Count; i++)
                    {
                        GcPollStub stub = _gcPollStubs[i];
                        _owner.DefineLabel(stub.SlowLabel);
                        var saveLocations = BuildGcPollSaveLocations(stub.Node, out int saveAreaSize);
                        if (saveAreaSize != 0)
                            _owner.EmitAdjustStack(-saveAreaSize);
                        for (int l = 0; l < saveLocations.Count; l++)
                            EmitMemoryStore(ToArm(saveLocations[l].Register), ArmRegister.Sp, saveLocations[l].Offset, saveLocations[l].Size);

                        SafePointDraft safePoint = PrepareSafePoint(stub.Node);
                        PublishGcTransition(safePoint);
                        MarkEhGcPollCallSite(stub.Node);
                        _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.GcPollSymbol));
                        _owner.DefineLabel(safePoint.ReturnLabel);

                        for (int l = saveLocations.Count - 1; l >= 0; l--)
                            EmitMemoryLoad(ToArm(saveLocations[l].Register), ArmRegister.Sp, saveLocations[l].Offset, saveLocations[l].Size, signed: false);
                        if (saveAreaSize != 0)
                            _owner.EmitAdjustStack(saveAreaSize);
                        ReloadGcRegisterRoots(stub.Node);
                        _owner.EmitJump(stub.ContinuationLabel);
                    }
                    _owner.DefineLabel(endLabel);
                }

                private void MarkEhGcPollCallSite(GenTree node)
                {
                    if (_ehMethod is null)
                        return;
                    if ((uint)node.LinearBlockId >= (uint)_blockLabels.Length)
                        throw Unsupported(node, "GC poll has no containing block for EH state publication");
                    EmitEhSetCurrentIp(_blockLabels[node.LinearBlockId]);
                }

                private List<(MachineRegister Register, int Offset, int Size)> BuildGcPollSaveLocations(
                    GenTree node,
                    out int saveAreaSize)
                {
                    ulong live = node.SafePointLiveRegisters;
                    ImmutableArray<MachineRegister> registers = RegisterInfo.CallerSavedScalarRegisters(Target);
                    int offset = checked(RegisterInfo.MinimumOutgoingArgumentSlots(Target) * Target.PointerSize);
                    var locations = new List<(MachineRegister Register, int Offset, int Size)>();
                    for (int i = 0; i < registers.Length; i++)
                    {
                        MachineRegister register = registers[i];
                        if (RegisterInfo.IsReserved(Target, register) || (live & MachineRegisters.MaskOf(register)) == 0)
                            continue;
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, RegisterInfo.RegisterSaveAlignment(Target, register));
                        locations.Add((register, offset, size));
                        offset = checked(offset + size);
                    }

                    saveAreaSize = locations.Count == 0
                        ? 0
                        : AlignUp(offset, Math.Max(Target.PointerSize, Target.CallFrameAlignment));
                    return locations;
                }

                private void ReloadGcRegisterRoots(GenTree node)
                {
                    var liveRoots = CollectLiveGcRoots(node);
                    for (int i = 0; i < liveRoots.Count; i++)
                    {
                        RegisterGcLiveRoot root = liveRoots[i];
                        if (!root.Location.IsRegister)
                            continue;
                        if (root.Offset != 0 || root.Location.RegisterClass != RegisterClass.General)
                            throw Unsupported(node, "Register GC root has an unsupported shape");
                        EmitMemoryLoad(
                            GeneralRegister(root.Location.Register),
                            ArmRegister.X29,
                            checked(_method.StackFrame.GcSpillAreaOffset + i * Target.PointerSize),
                            Target.PointerSize,
                            signed: false);
                    }
                }

                private List<RegisterGcLiveRoot> CollectLiveGcRoots(GenTree node)
                {
                    if (!_nodePositions.TryGetValue(node.LinearId, out int position))
                        throw Unsupported(node, "GC safe point has no final LIR position");

                    var liveRoots = new List<RegisterGcLiveRoot>();
                    for (int i = 0; i < _method.GcLiveRanges.Length; i++)
                    {
                        RegisterGcLiveRange range = _method.GcLiveRanges[i];
                        if (range.FuncletIndex != 0 || range.StartPosition > position || position >= range.EndPosition)
                            continue;
                        if (!ContainsRootCell(liveRoots, range.Root))
                            liveRoots.Add(range.Root);
                    }
                    return liveRoots;
                }

                private static bool ContainsRootCell(List<RegisterGcLiveRoot> roots, RegisterGcLiveRoot candidate)
                {
                    for (int i = 0; i < roots.Count; i++)
                    {
                        if (roots[i].RootKind == candidate.RootKind &&
                            roots[i].Offset == candidate.Offset &&
                            roots[i].Location.Equals(candidate.Location))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                private SafePointDraft PrepareSafePoint(GenTree node, RegisterOperand additionalRoot = default)
                {
                    if (!_method.StackFrame.UsesFramePointer)
                        throw Unsupported(node, "GC safe point requires a frame pointer");
                    if (!_method.StackFrame.TryGetCalleeSavedSlot(RegisterInfo.FramePointer(Target), out StackFrameSlot savedFramePointer) ||
                        !_method.StackFrame.TryGetCalleeSavedSlot(RegisterInfo.ReturnAddress(Target), out StackFrameSlot savedReturnAddress))
                    {
                        throw Unsupported(node, "GC safe point requires saved frame-pointer and return-address slots");
                    }

                    var liveRoots = CollectLiveGcRoots(node);
                    int rootCount = checked(liveRoots.Count + (additionalRoot.IsNone ? 0 : 1));
                    if (rootCount > _method.StackFrame.GcRootSpillSlotCount)
                    {
                        throw Unsupported(
                            node,
                            $"GC spill area has {_method.StackFrame.GcRootSpillSlotCount} slots but {rootCount} roots are live");
                    }

                    var roots = ImmutableArray.CreateBuilder<SafePointRootDraft>(rootCount);
                    for (int i = 0; i < liveRoots.Count; i++)
                    {
                        SpillGcRoot(node, liveRoots[i], i);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + i * Target.PointerSize),
                            liveRoots[i].RootKind));
                    }

                    if (!additionalRoot.IsNone)
                    {
                        SpillGcRootLocation(node, additionalRoot, RegisterGcRootKind.ObjectReference, 0, liveRoots.Count);
                        roots.Add(new SafePointRootDraft(
                            checked(_method.StackFrame.GcSpillAreaOffset + liveRoots.Count * Target.PointerSize),
                            RegisterGcRootKind.ObjectReference));
                    }

                    return _owner.AddSafePoint(
                        _methodLabel,
                        _owner.CreateLocalLabel(_methodLabel + "_gc_return"),
                        savedFramePointer.Offset,
                        savedReturnAddress.Offset,
                        roots.ToImmutable());
                }

                private void SpillGcRoot(GenTree node, RegisterGcLiveRoot root, int rootIndex)
                    => SpillGcRootLocation(node, root.Location, root.RootKind, root.Offset, rootIndex);

                private void SpillGcRootLocation(
                    GenTree node,
                    RegisterOperand location,
                    RegisterGcRootKind rootKind,
                    int cellOffset,
                    int rootIndex)
                {
                    ArmRegister source;
                    if (location.IsRegister)
                    {
                        if (cellOffset != 0 || location.RegisterClass != RegisterClass.General)
                            throw Unsupported(node, "Register GC root has an unsupported shape");
                        source = GeneralRegister(location.Register);
                    }
                    else if (location.IsFrameSlot)
                    {
                        EmitMemoryLoad(
                            Scratch0,
                            FrameBase(location),
                            checked(EffectiveFrameOffset(location) + cellOffset),
                            Target.PointerSize,
                            signed: false);
                        source = Scratch0;
                    }
                    else
                    {
                        throw Unsupported(node, "GC root location is not final");
                    }

                    EmitMemoryStore(
                        source,
                        ArmRegister.X29,
                        checked(_method.StackFrame.GcSpillAreaOffset + rootIndex * Target.PointerSize),
                        Target.PointerSize);
                }

                private void PublishGcTransition(SafePointDraft safePoint)
                {
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.CurrentSafePointSymbol),
                        Scratch0);
                    _owner.EmitMaterializeAddress(safePoint.DescriptorLabel, Scratch1);
                    EmitMemoryStore(Scratch1, Scratch0, 0, Target.PointerSize);
                    _owner.EmitMaterializeAddress(
                        _owner.ResolveExternalObjectSymbol(ArmRuntime.CurrentFramePointerSymbol),
                        Scratch0);
                    EmitMemoryStore(ArmRegister.X29, Scratch0, 0, Target.PointerSize);
                }

                private void EmitManagedExceptionThrow(GenTree node, string exceptionTypeName)
                {
                    MarkEhCallSite(node, "implicit_throw");
                    _owner.EmitMaterializeAddress(
                        _owner.GetStaticExceptionObjectLabel("System", exceptionTypeName),
                        ArmRegister.X0);
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.ThrowSymbol));
                    EmitTrap();
                }

                private int NewObjectArgumentSaveOffset
                    => AlignUp(
                        checked(TypeOperationScratchOffset + TypeOperationScratchSize),
                        Math.Max(Target.PointerSize, RegisterInfo.AbiFloatingRegisterSize(Target)));

                // The allocator is called with the constructor arguments already in place, so they go to
                // the transition area and come back once the object exists
                private void EmitNewObjectArgumentSpills(bool save)
                {
                    int offset = NewObjectArgumentSaveOffset;
                    for (int i = 1; ; i++)
                    {
                        MachineRegister register = RegisterInfo.GetIntegerArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, RegisterInfo.RegisterSaveAlignment(Target, register));
                        if (save)
                            EmitMemoryStore(ToArm(register), ArmRegister.X29, offset, size);
                        else
                            EmitMemoryLoad(ToArm(register), ArmRegister.X29, offset, size, signed: false);
                        offset = checked(offset + size);
                    }

                    for (int i = 0; ; i++)
                    {
                        MachineRegister register = RegisterInfo.GetFloatArgumentRegister(Target, i);
                        if (register == MachineRegister.Invalid)
                            break;
                        int size = RegisterInfo.RegisterSaveSize(Target, register);
                        offset = AlignUp(offset, RegisterInfo.RegisterSaveAlignment(Target, register));
                        if (save)
                            EmitMemoryStore(ToArm(register), ArmRegister.X29, offset, size);
                        else
                            EmitMemoryLoad(ToArm(register), ArmRegister.X29, offset, size, signed: false);
                        offset = checked(offset + size);
                    }

                    if (save && offset > _method.StackFrame.GcSpillAreaOffset + _method.StackFrame.GcSpillAreaSize)
                        throw new InvalidOperationException("GC transition area is smaller than the ARM64 argument register save set.");
                }

                private void EmitAllocateNewArray(GenTree node, RuntimeMethod method)
                {
                    RuntimeType arrayType = method.ReturnType;
                    if (arrayType.Kind != RuntimeTypeKind.Array || !arrayType.IsSzArray)
                        throw Unsupported(node, "RhAllocateNewArray return type is not an SZ array");

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMove(ArmRegister.X1, ArmRegister.X0, 8);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(arrayType), ArmRegister.X0);
                    MarkEhCallSite(node, "allocate_uninitialized_array");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewUninitializedArraySymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitFastAllocateString(GenTree node, RuntimeMethod method)
                {
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMove(ArmRegister.X1, ArmRegister.X0, 8);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(method.DeclaringType), ArmRegister.X0);
                    MarkEhCallSite(node, "string_alloc");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewArraySymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                }

                private void EmitNewObject(GenTree node)
                {
                    RuntimeMethod constructor = node.Method ?? throw Unsupported(node, "NewObject node has no constructor");
                    RuntimeType objectType = constructor.DeclaringType;
                    if (!constructor.HasThis)
                        throw Unsupported(node, "NewObject constructor has no implicit this parameter");
                    if (objectType.IsValueType)
                        throw Unsupported(node, "Value-type newobj must be lowered before ARM64 code generation");
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
                    EmitNewObjectArgumentSpills(save: true);
                    PublishGcTransition(allocationSafePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(objectType), ArmRegister.X0);
                    MarkEhCallSite(node, "new_object_alloc");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewFastSymbol));
                    _owner.DefineLabel(allocationSafePoint.ReturnLabel);
                    EmitStore(objectHome, RegisterInfo.GetIntegerReturnRegister(Target, 0), objectType, GenStackKind.Ref);

                    EmitNewObjectArgumentSpills(save: false);
                    EmitLoad(RegisterInfo.GetIntegerArgumentRegister(Target, 0), objectHome, objectType, GenStackKind.Ref);
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
                    _owner.EmitMaterializeAddress(
                        _owner.GetTypeDescriptorLabel(constructor.DeclaringType), ArmRegister.X0);

                    if (parameters.Length == 0)
                    {
                        _owner.EmitLoadImmediate(ArmRegister.X1, 0, 8);
                        runtimeSymbol = ArmRuntime.NewArraySymbol;
                    }
                    else if (parameters.Length == 2 && IsCharType(parameters[0]) && IsInt32Type(parameters[1]))
                    {
                        runtimeSymbol = ArmRuntime.NewStringFromCharSymbol;
                    }
                    else if (parameters.Length == 1 && IsCharArrayType(parameters[0]))
                    {
                        runtimeSymbol = ArmRuntime.NewStringFromCharArraySymbol;
                    }
                    else if (parameters.Length == 3 &&
                             IsCharArrayType(parameters[0]) &&
                             IsInt32Type(parameters[1]) &&
                             IsInt32Type(parameters[2]))
                    {
                        runtimeSymbol = ArmRuntime.NewStringFromCharArrayRangeSymbol;
                    }
                    else
                    {
                        throw Unsupported(node, "Unsupported System.String constructor shape");
                    }

                    MarkEhCallSite(node, "new_string");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(runtimeSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    EmitStore(
                        objectHome,
                        RegisterInfo.GetIntegerReturnRegister(Target, 0),
                        constructor.DeclaringType,
                        GenStackKind.Ref);
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

                private void EmitNewArray(GenTree node)
                {
                    RuntimeType arrayType = node.Type ?? throw Unsupported(node, "NewArray node has no array runtime type");
                    if (arrayType.Kind != RuntimeTypeKind.Array)
                        throw Unsupported(node, "NewArray node has no array runtime type");
                    if (!arrayType.IsSzArray)
                        throw Unsupported(node, "Multidimensional array allocation is not implemented");
                    if (node.Uses.Length != 1 || node.Results.Length != 1)
                        throw Unsupported(node, "NewArray must have one length operand and one result");

                    ArmRegister length = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array length"));
                    MachineRegister result = RequireResultRegister(node);

                    string nonNegative = _owner.CreateLocalLabel($"{_methodLabel}_array_length_non_negative_{node.LinearId}");
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(length, 4), ArmOperand.ImmediateOperand(0)));
                    _owner.EmitConditionalJump(ArmCondition.Ge, nonNegative);
                    EmitManagedExceptionThrow(node, "OverflowException");
                    _owner.DefineLabel(nonNegative);

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);

                    _owner.EmitMove(ArmRegister.X1, length, 4);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(arrayType), ArmRegister.X0);
                    MarkEhCallSite(node, "new_array");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewArraySymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.EmitMove(GeneralRegister(result), ArmRegister.X0, 8);
                }

                private void EmitArray(GenTree node)
                {
                    if (node.TreeKind == GenTreeKind.ArrayLength)
                    {
                        EmitArrayLength(node);
                        return;
                    }
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

                private void EmitArrayLength(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array length requires one array use and one register result");

                    ArmRegister array = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array length"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);
                    EmitMemoryLoad(
                        GeneralRegister(node.Results[0].Register),
                        array,
                        Target.ArrayLengthOffset,
                        4,
                        signed: true);
                }

                private void EmitArrayDataReference(GenTree node)
                {
                    if (node.Uses.Length != 1 || node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array data reference requires one array use and one register result");

                    ArmRegister array = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array data reference"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);

                    EmitMemoryLoad(Scratch0, array, 0, Target.PointerSize, signed: false);
                    EmitMethodTableElementType(Scratch1, Scratch0);
                    string valid = _owner.CreateLocalLabel($"{_methodLabel}_array_data_valid");
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 4), ArmOperand.ImmediateOperand(ElementTypeSzArray)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, valid);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(valid);
                    _owner.EmitAddImmediate(
                        GeneralRegister(node.Results[0].Register),
                        array,
                        Target.ArrayDataOffset,
                        Scratch0);
                }

                private void EmitArrayElementLoad(GenTree node, RuntimeType elementType)
                {
                    ArmRegister array = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array operand"));
                    ArmRegister index = GeneralRegister(RequireUseRegisterForOperand(node, 1, "array index"));
                    EmitArrayElementAccessChecks(node, elementType, array, requireExact: false);

                    ArmRegister address = ValueAddressRegister(node, elementType, node.StackKind);
                    EmitArrayElementAddress(node, elementType, address, array, index);
                    EmitValueFromAddress(node, elementType, node.StackKind, address);
                }

                private void EmitArrayElementAddressResult(GenTree node, RuntimeType elementType)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw Unsupported(node, "Array element address requires one register result");

                    ArmRegister array = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array operand"));
                    ArmRegister index = GeneralRegister(RequireUseRegisterForOperand(node, 1, "array index"));
                    EmitArrayElementAccessChecks(node, elementType, array, requireExact: true);
                    EmitArrayElementAddress(node, elementType, Scratch0, array, index);
                    _owner.EmitMove(GeneralRegister(node.Results[0].Register), Scratch0, 8);
                }

                private void EmitArrayElementStore(GenTree node, RuntimeType elementType)
                {
                    if (node.Uses.Length < 3)
                        throw Unsupported(node, "Array-element store requires array, index and value operands");

                    ArmRegister array = GeneralRegister(RequireUseRegisterForOperand(node, 0, "array-element store array"));
                    ArmRegister index = GeneralRegister(RequireUseRegisterForOperand(node, 1, "array-element store index"));
                    RegisterOperand value = node.Uses[2];

                    EmitArrayElementAccessChecks(node, elementType, array, requireExact: false);
                    if (elementType.IsReferenceType)
                    {
                        if (!value.IsRegister || value.RegisterClass != RegisterClass.General)
                            throw Unsupported(node, "Reference array store value must be in an integer register");
                        EmitArrayReferenceStoreCheck(node, array, GeneralRegister(value.Register));
                    }

                    GenStackKind valueKind = OperandStackKind(node, 2);
                    ArmRegister address = ValueAddressRegister(node, elementType, valueKind);
                    EmitArrayElementAddress(node, elementType, address, array, index);
                    EmitValueToAddress(node, value, elementType, valueKind, address);
                }

                private void EmitArrayNullCheck(GenTree node, ArmRegister array)
                {
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_array_non_null");
                    EmitCompareBranch(ArmInstrKind.Cbnz, array, 8, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");
                    _owner.DefineLabel(nonNull);
                }

                private void EmitArrayElementAddress(
                    GenTree node,
                    RuntimeType elementType,
                    ArmRegister destination,
                    ArmRegister array,
                    ArmRegister index)
                {
                    if ((node.Flags & GenTreeFlags.BoundsCheckEliminated) == 0)
                    {
                        EmitMemoryLoad(Scratch0, array, Target.ArrayLengthOffset, 4, signed: true);
                        string inRange = _owner.CreateLocalLabel($"{_methodLabel}_array_index_in_range");
                        if (node.HasBoundsCheckIndexOverride)
                        {
                            _owner.EmitLoadImmediate(Scratch1, node.BoundsCheckIndexOverride, 4);
                            _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 4), Reg(Scratch0, 4)));
                        }
                        else
                        {
                            _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(index, 4), Reg(Scratch0, 4)));
                        }
                        _owner.EmitConditionalJump(ArmCondition.Lo, inRange);
                        EmitManagedExceptionThrow(node, "IndexOutOfRangeException");
                        _owner.DefineLabel(inRange);
                    }

                    // The index is a checked non-negative int, so widening it is a plain zero extension
                    _owner.EmitMove(Scratch1, index, 4);
                    int elementSize = ArrayElementSize(elementType);
                    if (elementSize != 1)
                    {
                        if ((elementSize & (elementSize - 1)) == 0)
                        {
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Lsl,
                                Reg(Scratch1, 8),
                                Reg(Scratch1, 8),
                                ArmOperand.ImmediateOperand(Log2(elementSize))));
                        }
                        else
                        {
                            _owner.EmitLoadImmediate(Scratch0, elementSize, 8);
                            _owner.Emit(ArmInstruction.Ternary(
                                ArmInstrKind.Mul, Reg(Scratch1, 8), Reg(Scratch1, 8), Reg(Scratch0, 8)));
                        }
                    }

                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Add, Reg(destination, 8), Reg(array, 8), Reg(Scratch1, 8)));
                    _owner.EmitAddImmediate(destination, destination, Target.ArrayDataOffset, Scratch1);
                }

                /// <summary>Emits the null check every array access owes, and the element type check only where covariance can break it</summary>
                private void EmitArrayElementAccessChecks(GenTree node, RuntimeType elementType, ArmRegister array, bool requireExact)
                {
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitArrayNullCheck(node, array);

                    if (!GenTree.RequiresArrayElementTypeCheck(node.Kind, elementType))
                        return;

                    ArmRegister target = InternalGeneralRegister(node, 0);
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_ok");
                    string fail = _owner.CreateLocalLabel($"{_methodLabel}_array_element_type_fail");

                    EmitMemoryLoad(Scratch0, array, 0, Target.PointerSize, signed: false);
                    EmitMethodTableElementType(Scratch1, Scratch0);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch1, 4), ArmOperand.ImmediateOperand(ElementTypeSzArray)));
                    _owner.EmitConditionalJump(ArmCondition.Ne, fail);
                    EmitMemoryLoad(Scratch0, Scratch0, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(elementType), target);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(target, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, done);

                    if (!requireExact && elementType.IsReferenceType)
                    {
                        EmitMethodTableElementType(Scratch1, Scratch0);
                        _owner.Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Sub, Reg(Scratch1, 4), Reg(Scratch1, 4), ArmOperand.ImmediateOperand(ElementTypeClass)));
                        _owner.Emit(ArmInstruction.Binary(
                            ArmInstrKind.Cmp, Reg(Scratch1, 4), ArmOperand.ImmediateOperand(ElementTypeSzArray - ElementTypeClass)));
                        _owner.EmitConditionalJump(ArmCondition.Hi, fail);
                        EmitLoadedTypeAssignabilityCheck(target, Scratch0, Scratch1, done, fail);
                    }
                    else
                    {
                        _owner.EmitJump(fail);
                    }

                    _owner.DefineLabel(fail);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(done);
                }

                private void EmitArrayReferenceStoreCheck(GenTree node, ArmRegister array, ArmRegister value)
                {
                    ArmRegister target = InternalGeneralRegister(node, 0);
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_ok");
                    string fail = _owner.CreateLocalLabel($"{_methodLabel}_array_store_type_fail");

                    EmitCompareBranch(ArmInstrKind.Cbz, value, 8, done);
                    EmitMemoryLoad(target, array, 0, Target.PointerSize, signed: false);
                    EmitMemoryLoad(target, target, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(Scratch0, value, 0, Target.PointerSize, signed: false);
                    EmitLoadedTypeAssignabilityCheck(target, Scratch0, Scratch1, done, fail);

                    _owner.DefineLabel(fail);
                    EmitManagedExceptionThrow(node, "ArrayTypeMismatchException");
                    _owner.DefineLabel(done);
                }

                private const int MethodTableRelatedTypeOffset = 8;
                private const int ElementTypeClass = 0x14;
                private const int ElementTypeInterface = 0x15;
                private const int ElementTypeSystemArray = 0x16;
                private const int ElementTypeArray = 0x17;
                private const int ElementTypeSzArray = 0x18;

                private void EmitLoadedTypeAssignabilityCheck(
                    ArmRegister target,
                    ArmRegister source,
                    ArmRegister temp,
                    string success,
                    string failure)
                {
                    int methodTableInterfaceMapOffset = checked(16 + Target.PointerSize);
                    string loop = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_loop");
                    string targetClass = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_class");
                    string targetInterface = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_interface");
                    string targetSystemArray = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_system_array");
                    string targetSzArray = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_target_szarray");
                    string sourceBase = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_source_base");
                    string interfaceLoop = _owner.CreateLocalLabel($"{_methodLabel}_type_assignability_interface_loop");

                    void CompareElementType(ArmRegister methodTable, int elementType, ArmCondition condition, string label)
                    {
                        EmitMethodTableElementType(temp, methodTable);
                        _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(elementType)));
                        _owner.EmitConditionalJump(condition, label);
                    }

                    _owner.DefineLabel(loop);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(source, 8), Reg(target, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, success);
                    EmitMethodTableElementType(temp, target);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeClass)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, targetClass);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeInterface)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, targetInterface);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeSystemArray)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, targetSystemArray);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeSzArray)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, targetSzArray);
                    _owner.EmitJump(failure);

                    _owner.DefineLabel(targetClass);
                    EmitMemoryLoad(temp, target, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, temp, 8, success);
                    CompareElementType(source, ElementTypeArray, ArmCondition.Eq, failure);
                    CompareElementType(source, ElementTypeSzArray, ArmCondition.Eq, failure);
                    _owner.EmitJump(sourceBase);

                    _owner.DefineLabel(targetSystemArray);
                    CompareElementType(source, ElementTypeArray, ArmCondition.Eq, success);
                    CompareElementType(source, ElementTypeSzArray, ArmCondition.Eq, success);
                    _owner.EmitJump(failure);

                    _owner.DefineLabel(targetSzArray);
                    CompareElementType(source, ElementTypeSzArray, ArmCondition.Ne, failure);
                    EmitMemoryLoad(target, target, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitMemoryLoad(source, source, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(source, 8), Reg(target, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, success);
                    EmitReferenceElementTypeGuard(target, temp, failure);
                    EmitReferenceElementTypeGuard(source, temp, failure);
                    _owner.EmitJump(loop);

                    _owner.DefineLabel(targetInterface);
                    EmitMemoryLoad(temp, source, methodTableInterfaceMapOffset, Target.PointerSize, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, temp, 8, failure);
                    _owner.DefineLabel(interfaceLoop);
                    EmitMemoryLoad(source, temp, 0, Target.PointerSize, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, source, 8, failure);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(source, 8), Reg(target, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, success);
                    _owner.EmitAddImmediate(temp, temp, Target.PointerSize, temp);
                    _owner.EmitJump(interfaceLoop);

                    _owner.DefineLabel(sourceBase);
                    EmitMemoryLoad(source, source, MethodTableRelatedTypeOffset, Target.PointerSize, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, source, 8, failure);
                    _owner.EmitJump(loop);
                }

                private void EmitReferenceElementTypeGuard(ArmRegister methodTable, ArmRegister temp, string failure)
                {
                    EmitMethodTableElementType(temp, methodTable);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Sub, Reg(temp, 4), Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeClass)));
                    _owner.Emit(ArmInstruction.Binary(
                        ArmInstrKind.Cmp, Reg(temp, 4), ArmOperand.ImmediateOperand(ElementTypeSzArray - ElementTypeClass)));
                    _owner.EmitConditionalJump(ArmCondition.Hi, failure);
                }

                private void EmitMethodTableElementType(ArmRegister destination, ArmRegister methodTable)
                {
                    EmitMemoryLoad(destination, methodTable, 0, 4, signed: false);
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Lsr, Reg(destination, 4), Reg(destination, 4), ArmOperand.ImmediateOperand(26)));
                    _owner.Emit(ArmInstruction.Ternary(
                        ArmInstrKind.And, Reg(destination, 4), Reg(destination, 4), ArmOperand.ImmediateOperand(0x1f)));
                }

                private void EmitValueFromAddress(GenTree node, RuntimeType? type, GenStackKind kind, ArmRegister address)
                {
                    if (node.Results.Length == 0)
                        return;

                    AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        if (node.Results.Length != segments.Length)
                            throw Unsupported(node, "Multi-register load result count does not match its storage ABI");
                        for (int i = 0; i < segments.Length; i++)
                        {
                            RegisterOperand fragment = node.Results[i];
                            if (fragment.IsRegister)
                            {
                                EmitMemoryLoad(ToArm(fragment.Register), address, segments[i].Offset, segments[i].Size, signed: false);
                                continue;
                            }

                            ArmRegister scratch = FragmentScratch(node, segments[i], address);
                            EmitMemoryLoad(scratch, address, segments[i].Offset, segments[i].Size, signed: false);
                            EmitFragmentToFrame(node, scratch, fragment, segments[i].Size);
                        }
                        return;
                    }

                    if (node.Results.Length != 1)
                        throw Unsupported(node, "Load result count does not match its storage ABI");

                    RegisterOperand result = node.Results[0];
                    int size = StorageSize(type, kind, result);
                    if (result.IsRegister)
                    {
                        if (size is not (1 or 2 or 4 or 8))
                            throw Unsupported(node, $"Unsupported element load size {size}");
                        EmitMemoryLoad(ToArm(result.Register), address, 0, size, IsSigned(type, kind));
                        return;
                    }
                    if (!result.IsFrameSlot)
                        throw Unsupported(node, "Load result is not addressable");

                    // A scalar is read before its address register is needed again, so it can be relayed
                    // through the other scratch register
                    if (size is 1 or 2 or 4 or 8)
                    {
                        ArmRegister relay = address == Scratch1 ? Scratch0 : Scratch1;
                        EmitMemoryLoad(relay, address, 0, size, IsSigned(type, kind));
                        EmitStore(result, MachineRegisterOf(relay), type, kind);
                        return;
                    }

                    RequireBlockCopyAddress(node, address);
                    _owner.EmitAddImmediate(Scratch0, FrameBase(result), EffectiveFrameOffset(result), Scratch0);
                    EmitBlockCopy(Scratch1, Scratch0, 0, address, 0, size);
                }

                private void EmitValueToAddress(
                    GenTree node,
                    RegisterOperand value,
                    RuntimeType? type,
                    GenStackKind kind,
                    ArmRegister address)
                {
                    AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi, Target);
                        int firstUse = node.Uses.Length - segments.Length;
                        if (firstUse < 0)
                            throw Unsupported(node, "Multi-register store source count does not match its storage ABI");
                        for (int i = 0; i < segments.Length; i++)
                        {
                            RegisterOperand fragment = node.Uses[firstUse + i];
                            if (fragment.IsRegister)
                            {
                                EmitMemoryStore(ToArm(fragment.Register), address, segments[i].Offset, segments[i].Size);
                                continue;
                            }

                            ArmRegister scratch = FragmentScratch(node, segments[i], address);
                            EmitFragmentFromFrame(node, scratch, fragment, segments[i].Size);
                            EmitMemoryStore(scratch, address, segments[i].Offset, segments[i].Size);
                        }
                        return;
                    }

                    int size = StorageSize(type, kind, value);
                    if (value.IsRegister)
                    {
                        if (size is not (1 or 2 or 4 or 8))
                            throw Unsupported(node, $"Unsupported element store size {size}");
                        EmitMemoryStore(ToArm(value.Register), address, 0, size);
                        return;
                    }
                    if (!value.IsFrameSlot)
                        throw Unsupported(node, "Array-element store value is not addressable");

                    if (size is 1 or 2 or 4 or 8)
                    {
                        ArmRegister relay = address == Scratch1 ? Scratch0 : Scratch1;
                        _owner.EmitAddImmediate(relay, FrameBase(value), EffectiveFrameOffset(value), relay);
                        EmitMemoryLoad(relay, relay, 0, size, signed: false);
                        EmitMemoryStore(relay, address, 0, size);
                        return;
                    }

                    RequireBlockCopyAddress(node, address);
                    _owner.EmitAddImmediate(Scratch0, FrameBase(value), EffectiveFrameOffset(value), Scratch0);
                    EmitBlockCopy(Scratch1, address, 0, Scratch0, 0, size);
                }

                // Both scratch registers are taken by the address and the fragment, so a frame slot the
                // generator cannot reach with a plain displacement has nowhere to form its address
                private ArmRegister FragmentScratch(GenTree node, AbiRegisterSegment segment, ArmRegister address)
                {
                    if (address == Scratch1)
                        throw Unsupported(node, "Multi-register fragment has no free scratch register");
                    return segment.RegisterClass == RegisterClass.Float ? ArmRegister.V31 : Scratch1;
                }

                private void EmitFragmentToFrame(GenTree node, ArmRegister source, RegisterOperand destination, int size)
                {
                    if (!destination.IsFrameSlot)
                        throw Unsupported(node, "Multi-register fragment has no destination");
                    int offset = EffectiveFrameOffset(destination);
                    if (!CanEncodeMemoryOffset(offset, size))
                        throw Unsupported(node, "Multi-register fragment frame slot is out of displacement range");
                    EmitMemoryStore(source, FrameBase(destination), offset, size);
                }

                private void EmitFragmentFromFrame(GenTree node, ArmRegister destination, RegisterOperand source, int size)
                {
                    if (!source.IsFrameSlot)
                        throw Unsupported(node, "Multi-register fragment has no source");
                    int offset = EffectiveFrameOffset(source);
                    if (!CanEncodeMemoryOffset(offset, size))
                        throw Unsupported(node, "Multi-register fragment frame slot is out of displacement range");
                    EmitMemoryLoad(destination, FrameBase(source), offset, size, signed: false);
                }

                private static bool IsContainedDefaultValue(GenTree node, int operandIndex)
                    => (uint)operandIndex < (uint)node.Operands.Length &&
                       node.Operands[operandIndex].Kind == GenTreeKind.DefaultValue &&
                       !node.OperandFlags.IsDefaultOrEmpty &&
                       operandIndex < node.OperandFlags.Length &&
                       (node.OperandFlags[operandIndex] & LirOperandFlags.Contained) != 0;

                private void EmitZeroToAddress(ArmRegister address, int size)
                {
                    for (int written = 0; written < size;)
                    {
                        int remaining = size - written;
                        int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                        EmitMemoryStore(ArmRegister.Xzr, address, written, chunk);
                        written += chunk;
                    }
                }

                private void EmitBlockCopy(
                    ArmRegister scratch,
                    ArmRegister destinationBase,
                    int destinationOffset,
                    ArmRegister sourceBase,
                    int sourceOffset,
                    int size)
                {
                    for (int offset = 0; offset < size;)
                    {
                        int remaining = size - offset;
                        int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                        EmitMemoryLoad(scratch, sourceBase, checked(sourceOffset + offset), chunk, signed: false);
                        EmitMemoryStore(scratch, destinationBase, checked(destinationOffset + offset), chunk);
                        offset += chunk;
                    }
                }

                // A value the generator has to copy block-wise occupies both scratch registers, so its
                // address comes from an allocated internal register instead
                private ArmRegister ValueAddressRegister(GenTree node, RuntimeType? type, GenStackKind kind)
                    => UsesBlockCopy(type, kind) ? InternalGeneralRegister(node, 0) : Scratch0;

                private bool UsesBlockCopy(RuntimeType? type, GenStackKind kind)
                {
                    AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, kind, Target);
                    return abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect;
                }

                private static MachineRegister MachineRegisterOf(ArmRegister register)
                    => register == ArmRegister.X16 ? MachineRegister.X16 : MachineRegister.X17;

                private void RequireBlockCopyAddress(GenTree node, ArmRegister address)
                {
                    if (address == Scratch0 || address == Scratch1)
                        throw Unsupported(node, "Block-copied value has no allocated address register");
                }

                private void EmitField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Field node has no field metadata");
                    if (field.IsStatic)
                        throw Unsupported(node, "Instance field node references a static field");
                    if (node.TreeKind == GenTreeKind.StoreField && TryEmitPromotedFieldDefinition(node, field))
                        return;

                    RuntimeType fieldType = field.FieldType;
                    GenStackKind kind = MachineAbi.StackKindForType(fieldType);
                    ArmRegister instance = GeneralRegister(RequireUseRegisterForOperand(node, 0, "field instance"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitObjectNullCheck(node, instance);

                    if (node.TreeKind == GenTreeKind.FieldAddr)
                    {
                        _owner.EmitAddImmediate(
                            GeneralRegister(RequireResultRegister(node)), instance, field.Offset, Scratch0);
                        return;
                    }

                    ArmRegister address = ValueAddressRegister(node, fieldType, kind);
                    _owner.EmitAddImmediate(address, instance, field.Offset, address);
                    if (node.TreeKind == GenTreeKind.Field)
                    {
                        EmitValueFromAddress(node, fieldType, kind, address);
                        return;
                    }

                    if (IsContainedDefaultValue(node, 1))
                    {
                        EmitZeroToAddress(address, StorageSize(fieldType, kind));
                        return;
                    }
                    if (node.Uses.Length < 2)
                        throw Unsupported(node, "Field store has no value operand");
                    EmitValueToAddress(node, node.Uses[1], fieldType, kind, address);
                }

                // A field of a struct the optimizer promoted into locals is a plain move, not an access
                private bool TryEmitPromotedFieldDefinition(GenTree node, RuntimeField field)
                {
                    if (!node.SsaStoreTargetName.HasValue || node.LocalDescriptor is not { IsStructField: true })
                        return false;
                    if (node.Results.Length == 0 && node.Uses.Length == 0)
                        return true;

                    RuntimeType fieldType = field.FieldType;
                    GenStackKind kind = MachineAbi.StackKindForType(fieldType);
                    if (node.Uses.Length == 0 &&
                        node.Operands.Length > 1 &&
                        node.Operands[1].Kind == GenTreeKind.DefaultValue)
                    {
                        for (int i = 0; i < node.Results.Length; i++)
                            EmitZeroOperand(node, node.Results[i], fieldType, kind);
                        return true;
                    }

                    if (node.Results.Length != node.Uses.Length)
                        throw Unsupported(node, "Promoted field definition has mismatched fragment counts");
                    for (int i = 0; i < node.Results.Length; i++)
                        EmitMoveBetween(node, node.Results[i], node.Uses[i], fieldType, kind);
                    return true;
                }

                private void EmitIndirect(GenTree node)
                {
                    RuntimeType? type = node.RuntimeType ?? node.Type;
                    GenStackKind kind = node.StackKind;
                    ArmRegister pointer = GeneralRegister(RequireUseRegisterForOperand(node, 0, "indirection pointer"));
                    if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                        EmitObjectNullCheck(node, pointer);

                    if (node.TreeKind == GenTreeKind.LoadIndirect)
                    {
                        ArmRegister address = ValueAddressRegister(node, type, kind);
                        _owner.EmitMove(address, pointer, 8);
                        EmitValueFromAddress(node, type, kind, address);
                        return;
                    }

                    if (node.Uses.Length < 2)
                        throw Unsupported(node, "Indirect store has no value operand");
                    RuntimeType? valueType = OperandType(node, 1) ?? type;
                    GenStackKind valueKind = OperandStackKind(node, 1);
                    ArmRegister storeAddress = ValueAddressRegister(node, valueType, valueKind);
                    _owner.EmitMove(storeAddress, pointer, 8);
                    EmitValueToAddress(node, node.Uses[1], valueType, valueKind, storeAddress);
                }

                private void EmitClassInit(GenTree node)
                {
                    RuntimeType type = node.RuntimeType ?? throw Unsupported(node, "ClassInit node has no runtime type");
                    string initialized = _owner.CreateLocalLabel($"{_methodLabel}_type_init_initialized");

                    _owner.EmitMaterializeAddress(_owner.GetTypeInitializationStateLabel(type), Scratch0);
                    EmitMemoryLoad(Scratch1, Scratch0, 0, 4, signed: false);
                    EmitCompareBranch(ArmInstrKind.Cbz, Scratch1, 4, initialized);
                    SafePointDraft safePoint = PrepareSafePoint(node);
                    MarkEhCallSite(node, "class_init");
                    _owner.EmitCall(_owner.GetTypeInitializationThunkLabel(type));
                    _owner.DefineLabel(safePoint.ReturnLabel);
                    _owner.DefineLabel(initialized);
                }

                private void EmitStaticField(GenTree node)
                {
                    RuntimeField field = node.Field ?? throw Unsupported(node, "Static field node has no field metadata");
                    if (!field.IsStatic)
                        throw Unsupported(node, "Static field node references an instance field");

                    RuntimeType fieldType = field.FieldType;
                    GenStackKind kind = MachineAbi.StackKindForType(fieldType);
                    string storageLabel = _owner.GetStaticStorageLabel(field.DeclaringType);

                    if (node.TreeKind == GenTreeKind.StaticFieldAddr)
                    {
                        ArmRegister result = GeneralRegister(RequireResultRegister(node));
                        _owner.EmitMaterializeAddress(storageLabel, result);
                        _owner.EmitAddImmediate(result, result, field.Offset, Scratch0);
                        return;
                    }

                    ArmRegister address = ValueAddressRegister(node, fieldType, kind);
                    _owner.EmitMaterializeAddress(storageLabel, address);
                    _owner.EmitAddImmediate(address, address, field.Offset, address == Scratch1 ? Scratch0 : Scratch1);

                    if (node.TreeKind == GenTreeKind.StaticField)
                    {
                        EmitValueFromAddress(node, fieldType, kind, address);
                        return;
                    }

                    if (IsContainedDefaultValue(node, 0))
                    {
                        EmitZeroToAddress(address, StorageSize(fieldType, kind));
                        return;
                    }
                    if (node.Uses.Length == 0)
                        throw Unsupported(node, "Static field store has no value operand");
                    EmitValueToAddress(node, node.Uses[0], fieldType, kind, address);
                }

                private void EmitBox(GenTree node)
                {
                    RuntimeType boxedType = BoxSourceRuntimeType(node);
                    GenStackKind boxedKind = BoxSourceStackKind(node, boxedType);
                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));

                    if (boxedType.IsReferenceType)
                    {
                        _owner.EmitMove(destination, GeneralRegister(RequireUseRegisterForOperand(node, 0, "box source")), 8);
                        return;
                    }
                    if (!boxedType.IsValueType)
                        throw Unsupported(node, "Box source must be a value type or an instantiated reference type");

                    int scratchOffset = TypeOperationScratchOffset;
                    if (TypeOperationScratchSize < Math.Max(1, boxedType.SizeOf))
                        throw Unsupported(node, "Type-operation scratch area is smaller than the box source");
                    if (node.Uses.Length == 0)
                        throw Unsupported(node, "Box has no source operand");

                    ArmRegister address = ValueAddressRegister(node, boxedType, boxedKind);
                    _owner.EmitAddImmediate(address, ArmRegister.X29, scratchOffset, address == Scratch1 ? Scratch0 : Scratch1);
                    EmitValueToAddress(node, node.Uses[0], boxedType, boxedKind, address);

                    RuntimeType allocationType = boxedType;
                    int sourceOffset = 0;
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_box_done");
                    if (TryGetNullableInfo(boxedType, out RuntimeType underlyingType, out RuntimeField hasValueField, out RuntimeField valueField))
                    {
                        string hasValue = _owner.CreateLocalLabel($"{_methodLabel}_box_nullable_has_value");
                        EmitMemoryLoad(
                            Scratch0,
                            ArmRegister.X29,
                            checked(scratchOffset + hasValueField.Offset),
                            Math.Max(1, hasValueField.FieldType.SizeOf),
                            signed: false);
                        EmitCompareBranch(ArmInstrKind.Cbnz, Scratch0, 4, hasValue);
                        _owner.EmitMove(destination, ArmRegister.Xzr, 8);
                        _owner.EmitJump(done);
                        _owner.DefineLabel(hasValue);
                        allocationType = underlyingType;
                        sourceOffset = valueField.Offset;
                    }

                    SafePointDraft safePoint = PrepareSafePoint(node);
                    PublishGcTransition(safePoint);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(allocationType), ArmRegister.X0);
                    MarkEhCallSite(node, "box");
                    _owner.EmitCall(_owner.ResolveExternalSymbol(ArmRuntime.NewFastSymbol));
                    _owner.DefineLabel(safePoint.ReturnLabel);

                    EmitBlockCopy(
                        Scratch1,
                        ArmRegister.X0,
                        Target.ManagedObjectHeaderSize,
                        ArmRegister.X29,
                        checked(scratchOffset + sourceOffset),
                        Math.Max(1, allocationType.SizeOf));
                    _owner.EmitMove(destination, ArmRegister.X0, 8);
                    _owner.DefineLabel(done);
                }

                private void EmitUnboxAny(GenTree node)
                {
                    RuntimeType targetType = RequireRuntimeType(node);
                    if (!targetType.IsValueType)
                    {
                        EmitRuntimeTypeCheck(node, throwOnFailure: true);
                        return;
                    }

                    ArmRegister source = GeneralRegister(RequireUseRegisterForOperand(node, 0, "unbox source"));
                    if (TryGetNullableInfo(targetType, out RuntimeType underlyingType, out RuntimeField hasValueField, out RuntimeField valueField))
                    {
                        EmitNullableUnboxAny(node, targetType, underlyingType, hasValueField, valueField, source);
                        return;
                    }

                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_unbox_non_null");
                    string typeMatch = _owner.CreateLocalLabel($"{_methodLabel}_unbox_type_match");
                    EmitCompareBranch(ArmInstrKind.Cbnz, source, 8, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");

                    _owner.DefineLabel(nonNull);
                    EmitMemoryLoad(Scratch0, source, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(targetType), Scratch1);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, typeMatch);
                    EmitManagedExceptionThrow(node, "InvalidCastException");

                    _owner.DefineLabel(typeMatch);
                    ArmRegister address = ValueAddressRegister(node, targetType, node.StackKind);
                    _owner.EmitAddImmediate(address, source, Target.ManagedObjectHeaderSize, address);
                    EmitValueFromAddress(node, targetType, node.StackKind, address);
                }

                // A boxed Nullable<T> is the underlying value or nothing, so the result is rebuilt in the frame
                private void EmitNullableUnboxAny(
                    GenTree node,
                    RuntimeType nullableType,
                    RuntimeType underlyingType,
                    RuntimeField hasValueField,
                    RuntimeField valueField,
                    ArmRegister source)
                {
                    int scratchOffset = TypeOperationScratchOffset;
                    int nullableSize = Math.Max(1, nullableType.SizeOf);
                    if (TypeOperationScratchSize < nullableSize)
                        throw Unsupported(node, "Type-operation scratch area is smaller than the nullable result");

                    string nullValue = _owner.CreateLocalLabel($"{_methodLabel}_unbox_nullable_null");
                    string directValue = _owner.CreateLocalLabel($"{_methodLabel}_unbox_nullable_direct");
                    string underlyingValue = _owner.CreateLocalLabel($"{_methodLabel}_unbox_nullable_underlying");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_unbox_nullable_done");

                    EmitCompareBranch(ArmInstrKind.Cbz, source, 8, nullValue);
                    EmitMemoryLoad(Scratch0, source, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(nullableType), Scratch1);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, directValue);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(underlyingType), Scratch1);
                    _owner.Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(Scratch0, 8), Reg(Scratch1, 8)));
                    _owner.EmitConditionalJump(ArmCondition.Eq, underlyingValue);
                    EmitManagedExceptionThrow(node, "InvalidCastException");

                    _owner.DefineLabel(nullValue);
                    EmitDefaultValue(node);
                    _owner.EmitJump(done);

                    _owner.DefineLabel(directValue);
                    ArmRegister directAddress = ValueAddressRegister(node, nullableType, node.StackKind);
                    _owner.EmitAddImmediate(directAddress, source, Target.ManagedObjectHeaderSize, directAddress);
                    EmitValueFromAddress(node, nullableType, node.StackKind, directAddress);
                    _owner.EmitJump(done);

                    _owner.DefineLabel(underlyingValue);
                    for (int written = 0; written < nullableSize;)
                    {
                        int chunk = nullableSize - written >= 8 ? 8 : nullableSize - written >= 4 ? 4 : nullableSize - written >= 2 ? 2 : 1;
                        EmitMemoryStore(ArmRegister.Xzr, ArmRegister.X29, checked(scratchOffset + written), chunk);
                        written += chunk;
                    }
                    _owner.EmitLoadImmediate(Scratch0, 1, 4);
                    EmitMemoryStore(
                        Scratch0,
                        ArmRegister.X29,
                        checked(scratchOffset + hasValueField.Offset),
                        Math.Max(1, hasValueField.FieldType.SizeOf));
                    EmitBlockCopy(
                        Scratch1,
                        ArmRegister.X29,
                        checked(scratchOffset + valueField.Offset),
                        source,
                        Target.ManagedObjectHeaderSize,
                        Math.Max(1, underlyingType.SizeOf));
                    ArmRegister resultAddress = ValueAddressRegister(node, nullableType, node.StackKind);
                    _owner.EmitAddImmediate(
                        resultAddress,
                        ArmRegister.X29,
                        scratchOffset,
                        resultAddress == Scratch1 ? Scratch0 : Scratch1);
                    EmitValueFromAddress(node, nullableType, node.StackKind, resultAddress);
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
                    => !node.RegisterUses.IsDefaultOrEmpty
                        ? _method.GetValueInfo(node.RegisterUses[0]).StackKind
                        : MachineAbi.StackKindForType(boxedType);

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
                            if (type?.IsValueType == true)
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

                private void EmitRuntimeTypeCheck(GenTree node, bool throwOnFailure)
                {
                    RuntimeType targetType = RequireRuntimeType(node);
                    if (throwOnFailure && !targetType.IsReferenceType)
                        throw Unsupported(node, "CastClass target must be a reference type");

                    ArmRegister source = GeneralRegister(RequireUseRegisterForOperand(node, 0, "type-check source"));
                    ArmRegister destination = GeneralRegister(RequireResultRegister(node));
                    ArmRegister target = InternalGeneralRegister(node, 0);
                    string success = _owner.CreateLocalLabel($"{_methodLabel}_type_check_success");
                    string failure = _owner.CreateLocalLabel($"{_methodLabel}_type_check_failure");
                    string done = _owner.CreateLocalLabel($"{_methodLabel}_type_check_done");

                    EmitCompareBranch(ArmInstrKind.Cbz, source, 8, success);
                    EmitMemoryLoad(Scratch0, source, 0, Target.PointerSize, signed: false);
                    _owner.EmitMaterializeAddress(_owner.GetTypeDescriptorLabel(targetType), target);
                    EmitLoadedTypeAssignabilityCheck(target, Scratch0, Scratch1, success, failure);

                    _owner.DefineLabel(failure);
                    if (throwOnFailure)
                    {
                        EmitManagedExceptionThrow(node, "InvalidCastException");
                    }
                    else
                    {
                        _owner.EmitMove(destination, ArmRegister.Xzr, 8);
                        _owner.EmitJump(done);
                    }

                    _owner.DefineLabel(success);
                    _owner.EmitMove(destination, source, 8);
                    _owner.DefineLabel(done);
                }

                private void EmitObjectNullCheck(GenTree node, ArmRegister reference)
                {
                    string nonNull = _owner.CreateLocalLabel($"{_methodLabel}_non_null");
                    EmitCompareBranch(ArmInstrKind.Cbnz, reference, 8, nonNull);
                    EmitManagedExceptionThrow(node, "NullReferenceException");
                    _owner.DefineLabel(nonNull);
                }

                private void EmitZeroOperand(GenTree node, RegisterOperand destination, RuntimeType? type, GenStackKind kind)
                {
                    if (destination.IsRegister)
                    {
                        if (destination.RegisterClass == RegisterClass.Float)
                            EmitFmovFromGeneral(destination.Register, ArmRegister.Xzr, StorageSize(type, kind));
                        else
                            _owner.EmitMove(GeneralRegister(destination.Register), ArmRegister.Xzr, 8);
                        return;
                    }
                    if (!destination.IsFrameSlot)
                        throw Unsupported(node, "Zeroed operand is not addressable");

                    int size = StorageSize(type, kind, destination);
                    ArmRegister frameBase = FrameBase(destination);
                    int offset = EffectiveFrameOffset(destination);
                    for (int written = 0; written < size;)
                    {
                        int remaining = size - written;
                        int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                        EmitMemoryStore(ArmRegister.Xzr, frameBase, checked(offset + written), chunk);
                        written += chunk;
                    }
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

                private void EmitCompareBranch(ArmInstrKind opcode, ArmRegister register, int size, string label)
                {
                    int offset = OffsetOfNextInstruction();
                    _owner.Emit(new ArmInstruction(
                        opcode,
                        Reg(register, size),
                        ArmOperand.SymbolOperand(label, ArmRelocationKind.CompareBranch)));
                    AddRelocation(offset, label, ArmObjectRelocationKind.AArch64CompareBranch19);
                }

                private static int Log2(int value)
                {
                    int result = 0;
                    while ((value >>= 1) != 0)
                        result++;
                    return result;
                }

                private static int AlignUp(int value, int alignment)
                {
                    if (alignment <= 1)
                        return value;
                    int remainder = value % alignment;
                    return remainder == 0 ? value : checked(value + alignment - remainder);
                }

                private void EmitTrap()
                    => _owner.Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(1)));

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
                        _owner.EmitMove(GeneralRegister(destination), GeneralRegister(source), 8);
                        return;
                    }
                    if (destinationClass == RegisterClass.Float && sourceClass == RegisterClass.Float)
                    {
                        if (destination != source)
                        {
                            _owner.Emit(ArmInstruction.Binary(
                                ArmInstrKind.Fmov, Reg(ToArm(destination), size), Reg(ToArm(source), size)));
                        }
                        return;
                    }
                    if (destinationClass == RegisterClass.Float)
                    {
                        EmitFmovFromGeneral(destination, GeneralRegister(source), size);
                        return;
                    }

                    EmitFmovToGeneral(GeneralRegister(destination), source, size);
                }

                // fcvt has no mnemonic in the instruction model, so the precision change encodes directly
                private void EmitFloatPrecisionConvert(ArmRegister destination, ArmRegister source, int destinationSize)
                {
                    uint word = destinationSize == 8 ? 0x1E22C000u : 0x1E624000u;
                    _owner.Emit(ArmInstruction.Raw(word | ((uint)A64Index(source) << 5) | (uint)A64Index(destination)));
                }

                private void EmitFmovFromGeneral(MachineRegister destination, ArmRegister source, int size)
                {
                    uint word = size <= 4
                        ? 0x1E270000u | ((uint)A64Index(source) << 5) | (uint)A64Index(ToArm(destination))
                        : 0x9E670000u | ((uint)A64Index(source) << 5) | (uint)A64Index(ToArm(destination));
                    _owner.Emit(ArmInstruction.Raw(word));
                }

                private void EmitFmovToGeneral(ArmRegister destination, MachineRegister source, int size)
                {
                    uint word = size <= 4
                        ? 0x1E260000u | ((uint)A64Index(ToArm(source)) << 5) | (uint)A64Index(destination)
                        : 0x9E660000u | ((uint)A64Index(ToArm(source)) << 5) | (uint)A64Index(destination);
                    _owner.Emit(ArmInstruction.Raw(word));
                }

                private void EmitLoad(MachineRegister destination, RegisterOperand source, RuntimeType? type, GenStackKind kind)
                {
                    if (source.IsAddress)
                    {
                        EmitLoadAddress(destination, source);
                        return;
                    }
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Post-LSRA memory operands must be finalized frame slots: {source}.");

                    int size = StorageSize(type, kind, source);
                    EmitMemoryLoad(ToArm(destination), FrameBase(source), EffectiveFrameOffset(source), size, IsSigned(type, kind));
                }

                private void EmitStore(RegisterOperand destination, MachineRegister source, RuntimeType? type, GenStackKind kind)
                {
                    if (!destination.IsFrameSlot)
                        throw new InvalidOperationException($"Post-LSRA memory operands must be finalized frame slots: {destination}.");

                    int size = StorageSize(type, kind, destination);
                    EmitMemoryStore(ToArm(source), FrameBase(destination), EffectiveFrameOffset(destination), size);
                }

                private void EmitMemoryToMemory(RegisterOperand destination, RegisterOperand source, RuntimeType? type, GenStackKind kind)
                {
                    int size = StorageSize(type, kind, destination, source);
                    bool floating = destination.RegisterClass == RegisterClass.Float || source.RegisterClass == RegisterClass.Float;
                    ArmRegister scratch = floating ? ArmRegister.V31 : ArmRegister.X16;

                    if (!floating && size is not (1 or 2 or 4 or 8))
                    {
                        EmitFrameToFrameCopy(destination, source, size);
                        return;
                    }

                    EmitMemoryLoad(scratch, FrameBase(source), EffectiveFrameOffset(source), size, IsSigned(type, kind));
                    EmitMemoryStore(scratch, FrameBase(destination), EffectiveFrameOffset(destination), size);
                }

                private void EmitFrameToFrameCopy(RegisterOperand destination, RegisterOperand source, int size)
                {
                    ArmRegister destinationBase = FrameBase(destination);
                    ArmRegister sourceBase = FrameBase(source);
                    int destinationOffset = EffectiveFrameOffset(destination);
                    int sourceOffset = EffectiveFrameOffset(source);

                    for (int offset = 0; offset < size;)
                    {
                        int remaining = size - offset;
                        int chunk = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
                        EmitMemoryLoad(ArmRegister.X16, sourceBase, checked(sourceOffset + offset), chunk, signed: false);
                        EmitMemoryStore(ArmRegister.X16, destinationBase, checked(destinationOffset + offset), chunk);
                        offset += chunk;
                    }
                }

                private void EmitLoadAddress(MachineRegister destination, RegisterOperand source)
                {
                    if (source.IsRegister)
                    {
                        _owner.EmitMove(GeneralRegister(destination), GeneralRegister(source.Register), 8);
                        return;
                    }
                    if (!source.IsFrameSlot)
                        throw new InvalidOperationException($"Address source must be a finalized frame slot: {source}.");

                    ArmRegister result = GeneralRegister(destination);
                    ArmRegister scratch = result == ArmRegister.X17 ? ArmRegister.X16 : ArmRegister.X17;
                    _owner.EmitAddImmediate(result, FrameBase(source), EffectiveFrameOffset(source), scratch);
                }

                private void EmitMemoryLoad(ArmRegister destination, ArmRegister baseRegister, int offset, int size, bool signed)
                {
                    if (!CanEncodeMemoryOffset(offset, size))
                    {
                        ArmRegister addressScratch = destination == ArmRegister.X17 ? ArmRegister.X16 : ArmRegister.X17;
                        _owner.EmitAddImmediate(addressScratch, baseRegister, offset, addressScratch);
                        baseRegister = addressScratch;
                        offset = 0;
                    }

                    // A plain ldr takes its access width from the register operand, so the two must agree
                    bool vector = ArmRegisters.IsVector(destination);
                    ArmInstrKind opcode;
                    int operandSize;
                    if (vector)
                    {
                        opcode = ArmInstrKind.Ldr;
                        operandSize = size;
                    }
                    else
                    {
                        switch (size)
                        {
                            case 1:
                                opcode = signed ? ArmInstrKind.Ldrsb : ArmInstrKind.Ldrb;
                                operandSize = signed ? 8 : 4;
                                break;
                            case 2:
                                opcode = signed ? ArmInstrKind.Ldrsh : ArmInstrKind.Ldrh;
                                operandSize = signed ? 8 : 4;
                                break;
                            case 4:
                                opcode = signed ? ArmInstrKind.Ldrsw : ArmInstrKind.Ldr;
                                operandSize = signed ? 8 : 4;
                                break;
                            case 8:
                                opcode = ArmInstrKind.Ldr;
                                operandSize = 8;
                                break;
                            default:
                                throw new NotImplementedException($"Unsupported ARM64 load size {size}.");
                        }
                    }

                    _owner.Emit(ArmInstruction.Binary(opcode, Reg(destination, operandSize), Mem(baseRegister, offset, size)));
                }

                private void EmitMemoryStore(ArmRegister source, ArmRegister baseRegister, int offset, int size)
                {
                    if (!CanEncodeMemoryOffset(offset, size))
                    {
                        ArmRegister addressScratch = source == ArmRegister.X17 ? ArmRegister.X16 : ArmRegister.X17;
                        _owner.EmitAddImmediate(addressScratch, baseRegister, offset, addressScratch);
                        baseRegister = addressScratch;
                        offset = 0;
                    }

                    ArmInstrKind opcode = size switch
                    {
                        1 => ArmInstrKind.Strb,
                        2 => ArmInstrKind.Strh,
                        4 or 8 => ArmInstrKind.Str,
                        _ => throw new NotImplementedException($"Unsupported ARM64 store size {size}."),
                    };

                    _owner.Emit(ArmInstruction.Binary(
                        opcode, Reg(source, size is 1 or 2 ? 4 : size), Mem(baseRegister, offset, size)));
                }

                private static bool CanEncodeMemoryOffset(int offset, int size)
                    => (offset >= -256 && offset <= 255) || (offset >= 0 && offset % size == 0 && offset / size <= 4095);

                private int OffsetOfNextInstruction() => _owner._text.ByteLength;

                private void AddRelocation(int offset, string symbol, ArmObjectRelocationKind kind)
                    => _owner._text.AddRelocation(offset, symbol, 0, kind);

                private ArmRegister InternalGeneralRegister(GenTree node, int index)
                {
                    var registers = node.LsraInfo.InternalRegisters;
                    int seen = 0;
                    for (int i = 0; i < registers.Length; i++)
                    {
                        if (MachineRegisters.GetClass(registers[i].Register) != RegisterClass.General)
                            continue;
                        if (seen++ == index)
                            return GeneralRegister(registers[i].Register);
                    }

                    throw Unsupported(node, $"No internal general register {index} was allocated");
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

                private ArmRegister FrameBase(RegisterOperand operand)
                    => operand.FrameBase switch
                    {
                        RegisterFrameBase.StackPointer => ArmRegister.Sp,
                        RegisterFrameBase.FramePointer => ArmRegister.X29,
                        RegisterFrameBase.IncomingArgumentBase => _method.StackFrame.UsesFramePointer ? ArmRegister.X29 : ArmRegister.Sp,
                        _ => throw new InvalidOperationException($"Invalid frame base {operand.FrameBase}."),
                    };

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
                        _ => Target.PointerSize,
                    };
                }

                private int OperationSize(RuntimeType? type, GenStackKind kind)
                    => IsI4(type, kind) ? 4 : 8;

                private NotImplementedException Unsupported(GenTree node, string message)
                    => new NotImplementedException($"{message}. {Describe()}, block B{node.BlockId}, node {node.LinearId}, kind {node.TreeKind}.");

                private string Describe()
                    => $"Method M{_method.RuntimeMethod.MethodId} '{_method.RuntimeMethod.Name}'";

                private static bool IsCompareOp(BytecodeOp op)
                    => op is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un;

                private static bool IsUnsignedConversion(NumericConvKind kind)
                    => kind is NumericConvKind.U1 or NumericConvKind.U2 or NumericConvKind.U4 or NumericConvKind.U8 or
                        NumericConvKind.Char or NumericConvKind.NativeUInt;

                private static int ConversionSize(NumericConvKind kind)
                    => kind is NumericConvKind.I8 or NumericConvKind.U8 or NumericConvKind.NativeInt or NumericConvKind.NativeUInt ? 8 : 4;

                private static RuntimeType RequireRuntimeType(GenTree node)
                    => node.RuntimeType ?? node.Type ?? throw new InvalidOperationException("GenTree has no runtime type.");

                private static MachineRegister RequireResultRegister(GenTree node)
                {
                    if (node.Results.Length != 1 || !node.Results[0].IsRegister)
                        throw new InvalidOperationException($"GenTree requires one register result: {node.TreeKind}.");
                    return node.Results[0].Register;
                }

                private MachineRegister RequireUseRegisterForOperand(GenTree node, int operandIndex, string context)
                {
                    if ((uint)operandIndex < (uint)node.Uses.Length && node.Uses[operandIndex].IsRegister)
                        return node.Uses[operandIndex].Register;
                    throw Unsupported(node, $"{context} has no register use for operand {operandIndex}");
                }

                private static RuntimeType? OperandType(GenTree node, int operandIndex)
                {
                    if ((uint)operandIndex < (uint)node.RegisterUses.Length)
                        return node.RegisterUses[operandIndex].RuntimeType ?? node.RegisterUses[operandIndex].Type;
                    if ((uint)operandIndex < (uint)node.Operands.Length)
                        return node.Operands[operandIndex].RuntimeType ?? node.Operands[operandIndex].Type;
                    return node.RuntimeType ?? node.Type;
                }

                private static GenStackKind OperandStackKind(GenTree node, int operandIndex)
                {
                    if ((uint)operandIndex < (uint)node.RegisterUses.Length)
                        return node.RegisterUses[operandIndex].StackKind;
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

                private static GenStackKind StackKindForRegister(MachineRegister register)
                    => MachineRegisters.GetClass(register) == RegisterClass.Float ? GenStackKind.R8 : GenStackKind.NativeInt;

                private static bool IsFloating(RuntimeType? type, GenStackKind kind)
                    => kind is GenStackKind.R4 or GenStackKind.R8 ||
                       type?.PrimitiveKind is RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double;

                private static bool IsI4(RuntimeType? type, GenStackKind kind)
                    => kind == GenStackKind.I4 || type?.PrimitiveKind is
                        RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                        RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or
                        RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
                        RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32;

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

                private static ArmRegister GeneralRegister(MachineRegister register)
                {
                    if (MachineRegisters.GetClass(register) != RegisterClass.General)
                        throw new InvalidOperationException($"Expected an integer register, got {MachineRegisters.Format(register)}.");
                    return ToArm(register);
                }

                private static int A64Index(ArmRegister register)
                {
                    if (register == ArmRegister.Sp || register == ArmRegister.Xzr)
                        return 31;
                    if (register >= ArmRegister.X0 && register <= ArmRegister.X30)
                        return register - ArmRegister.X0;
                    if (register >= ArmRegister.V0 && register <= ArmRegister.V31)
                        return register - ArmRegister.V0;
                    throw new ArgumentOutOfRangeException(nameof(register));
                }
            }
        }

        private sealed class TextSectionBuilder
        {
            private readonly List<ArmInstruction> _instructions = new List<ArmInstruction>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<ArmObjectRelocation> _relocations = new List<ArmObjectRelocation>();

            public string Name { get; }
            public int ByteLength => checked(_instructions.Count * 4);

            public TextSectionBuilder(string name) => Name = name;

            public void DefineLabel(string label)
            {
                if (!_labels.TryAdd(label, ByteLength))
                    throw new InvalidOperationException($"Duplicate text label: {label}.");
            }

            public void Emit(ArmInstruction instruction) => _instructions.Add(instruction);

            public void AddRelocation(int offset, string symbol, int addend, ArmObjectRelocationKind kind)
                => _relocations.Add(new ArmObjectRelocation(Name, offset, symbol, addend, kind));

            public ArmTextSection ToSection()
                => new ArmTextSection(_instructions, _labels, _relocations.ToImmutableArray());
        }

        private sealed class DataSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly List<ArmObjectRelocation> _relocations = new List<ArmObjectRelocation>();

            public string Name { get; }
            public ArmObjectSectionKind Kind { get; }
            public int ByteLength => _data.Count;
            public int Alignment { get; private set; } = 1;

            public DataSectionBuilder(string name, ArmObjectSectionKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public int Align(int alignment)
            {
                alignment = Math.Max(1, alignment);
                Alignment = Math.Max(Alignment, alignment);
                int remainder = ByteLength % alignment;
                int aligned = remainder == 0 ? ByteLength : checked(ByteLength + alignment - remainder);
                while (_data.Count < aligned)
                    _data.Add(0);
                return aligned;
            }

            public void EmitBytes(byte[] bytes)
            {
                if (bytes is null)
                    throw new ArgumentNullException(nameof(bytes));
                _data.AddRange(bytes);
            }

            public void AddRelocation(int offset, string symbol, int addend, ArmObjectRelocationKind kind)
                => _relocations.Add(new ArmObjectRelocation(Name, offset, symbol, addend, kind));

            public ArmDataSection ToSection()
                => new ArmDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());
        }
    }
}
