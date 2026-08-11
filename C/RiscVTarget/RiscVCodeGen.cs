using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Cnidaria.Cs;
using Cnidaria.RiscV;

namespace Cnidaria.C
{
    public sealed class RiscVCodeGeneratorOptions
    {
        public static RiscVCodeGeneratorOptions Default => new RiscVCodeGeneratorOptions();

        public bool EmitStartup { get; set; } = true;
        public string EntryFunctionName { get; set; } = "main";
    }

    public sealed class RiscVCodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";
        private const string BssSectionName = ".bss";

        private static readonly MachineRegister Sp = MachineRegister.X2;
        private static readonly MachineRegister Ra = MachineRegister.X1;
        private static readonly MachineRegister GpScratch0 = MachineRegister.X5;
        private static readonly MachineRegister GpScratch1 = MachineRegister.X6;
        private static readonly MachineRegister GpScratch2 = MachineRegister.X7;
        private static readonly MachineRegister GpScratch3 = MachineRegister.X28;
        private static readonly MachineRegister GpScratch4 = MachineRegister.X29;
        private static readonly MachineRegister GpScratch5 = MachineRegister.X30;
        private static readonly MachineRegister GpScratch6 = MachineRegister.X31;
        private static readonly MachineRegister GpScratch7 = MachineRegister.X13;
        private static readonly MachineRegister GpScratch8 = MachineRegister.X10;
        private static readonly MachineRegister GpScratch9 = MachineRegister.X11;
        private static readonly MachineRegister GpScratch10 = MachineRegister.X12;
        private static readonly MachineRegister GpVectorConfigScratch = MachineRegister.X29;
        private static readonly MachineRegister FpScratch0 = MachineRegister.F0;
        private static readonly MachineRegister FpScratch1 = MachineRegister.F1;
        private static readonly MachineRegister FpScratch2 = MachineRegister.F2;
        private static readonly MachineRegister VecScratch0 = MachineRegister.V28;
        private static readonly MachineRegister VecScratch1 = MachineRegister.V29;
        private static readonly MachineRegister VecScratch2 = MachineRegister.V30;
        private static readonly MachineRegister VecScratch3 = MachineRegister.V31;

        private readonly LirModule _module;
        private readonly FileScopeLinkageMap _fileScopeLinkage;
        private readonly TargetInfo _target;
        private readonly RVTarget _machineTarget;
        private readonly LSRAOptions _allocationOptions;
        private readonly RiscVCodeGeneratorOptions _options;
        private readonly Dictionary<FunctionSymbol, string> _functionLabels = new Dictionary<FunctionSymbol, string>();
        private readonly Dictionary<string, string> _functionLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Symbol, string> _dataLabels = new Dictionary<Symbol, string>();
        private readonly Dictionary<string, string> _dataLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _stringLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RVObjectSymbol> _symbols = new List<RVObjectSymbol>();
        private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, RVObjectSectionKind.Rodata);
        private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, RVObjectSectionKind.Data);
        private readonly BssSectionBuilder _bss = new BssSectionBuilder(BssSectionName);
        private TextSectionBuilder _text = null!;
        private int _nextLocalId;

        private RiscVCodeGenerator(
            LirModule module,
            LSRAOptions? allocationOptions,
            RiscVCodeGeneratorOptions? options)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _fileScopeLinkage = FileScopeLinkageMap.Create(_module.SemanticModel);
            _target = module.SemanticModel.Compilation.Options.Target;
            if (_target.Architecture is not TargetArchitectureKind.RiscV32 and not TargetArchitectureKind.RiscV64)
                throw new NotSupportedException("RISC-V C backend requires RiscV32 or RiscV64 target.");
            _machineTarget = RVTarget.FromTargetInfo(_target);
            _allocationOptions = allocationOptions ?? LSRAOptions.ForTarget(_target);
            _options = options ?? RiscVCodeGeneratorOptions.Default;
        }

        public static RiscVProgram Generate(
            LirModule module,
            LSRAOptions? allocationOptions = null,
            RiscVCodeGeneratorOptions? options = null)
            => new RiscVCodeGenerator(module, allocationOptions, options).Generate();

        private RiscVProgram Generate()
        {
            _text = new TextSectionBuilder(TextSectionName);
            IndexFunctions();
            EmitGlobalStorage();
            foreach (var function in _module.Functions)
                EmitFunction(function);

            var selectedEntry = _functionLabelsByName.TryGetValue(_options.EntryFunctionName, out var entryLabel)
                ? entryLabel
                : (_functionLabels.Values.FirstOrDefault() ?? string.Empty);
            var entry = IsLinuxExecutableTarget && _options.EmitStartup
                ? EmitLinuxRuntime(selectedEntry)
                : selectedEntry;

            AddSectionSymbols();
            var dataSections = ImmutableArray.CreateBuilder<RVDataSection>();
            dataSections.Add(_rodata.ToSection());
            dataSections.Add(_data.ToSection());
            dataSections.Add(_bss.ToSection());

            return new RiscVProgram(
                _machineTarget,
                _text.ToSection(),
                dataSections.ToImmutable(),
                _symbols.ToImmutableArray(),
                entry);
        }

        private bool IsLinuxExecutableTarget
            => _target.OperatingSystem == OperatingSystemKind.Linux && _target.IsRiscV;

        private string EmitLinuxRuntime(string userEntryLabel)
            => EmitLinuxStart(userEntryLabel);

        private string EmitLinuxStart(string userEntryLabel)
        {
            var label = CreateUniqueGlobalLabel("_start");
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);

            Emit(RVInstruction.I(_machineTarget.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw, RVRegister.X10, RVRegister.X2, 0));
            Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X11, RVRegister.X2, _target.PointerSize));
            Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X12, RVRegister.X10, 1));
            Emit(RVInstruction.I(RVInstrKind.Slli, RVRegister.X12, RVRegister.X12, _target.PointerSize == 8 ? 3 : 2));
            Emit(RVInstruction.R(RVInstrKind.Add, RVRegister.X12, RVRegister.X12, RVRegister.X11));
            Emit(RVInstruction.I(RVInstrKind.Andi, RVRegister.X2, RVRegister.X2, -16));
            if (!string.IsNullOrEmpty(userEntryLabel))
                EmitCall(userEntryLabel);
            else
                Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X10, RVRegister.X0, 0));
            Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X17, RVRegister.X0, 93));
            Emit(new RVInstruction(RVInstrKind.Ecall));
            Emit(new RVInstruction(RVInstrKind.Ebreak));

            _symbols.Add(new RVObjectSymbol(label, TextSectionName, startOffset, _text.ByteLength - startOffset, RVObjectSymbolBinding.Global, RVObjectSymbolKind.Function));
            return label;
        }

        private void EmitCall(string label)
        {
            var offset = _text.ByteLength;
            Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X1, label));
            _text.AddRelocation(offset, label, 0, RVObjectRelocationKind.Jal20);
        }

        private void Emit(RVInstruction instruction)
            => _text.Emit(instruction);

        private void IndexFunctions()
        {
            foreach (var function in _module.Functions)
            {
                var symbol = function.Symbol;
                if (symbol is null || _functionLabels.ContainsKey(symbol))
                    continue;
                if (_functionLabelsByName.ContainsKey(symbol.Name))
                    throw new InvalidOperationException($"Duplicate definition of function '{symbol.Name}'.");

                var label = CreateUniqueGlobalLabel(symbol.Name);
                _functionLabels.Add(symbol, label);
                _functionLabelsByName.Add(symbol.Name, label);
            }
        }

        private void AddSectionSymbols()
        {
            _symbols.Add(new RVObjectSymbol(TextSectionName, TextSectionName, 0, _text.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(RodataSectionName, RodataSectionName, 0, _rodata.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(DataSectionName, DataSectionName, 0, _data.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(BssSectionName, BssSectionName, 0, _bss.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
        }

        private void EmitGlobalStorage()
        {
            var groups = new Dictionary<string, List<LirGlobal>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var global in _module.Globals)
            {
                if (global.Symbol is null ||
                    global.StorageClass == StorageClass.Typedef ||
                    global.Symbol is TypeAliasSymbol ||
                    global.Symbol is FunctionSymbol ||
                    global.Type.Type is FunctionType)
                {
                    continue;
                }

                if (!groups.TryGetValue(global.Symbol.Name, out var declarations))
                {
                    declarations = new List<LirGlobal>();
                    groups.Add(global.Symbol.Name, declarations);
                    order.Add(global.Symbol.Name);
                }
                declarations.Add(global);
            }

            foreach (var name in order)
            {
                var declarations = groups[name];
                var internalLinkage = _fileScopeLinkage.IsInternal(declarations[0].Symbol!);
                LirGlobal? strongDefinition = null;
                LirGlobal? tentativeDefinition = null;
                var tentativeSize = -1;

                foreach (var declaration in declarations)
                {
                    if (declaration.Initializer is not null)
                    {
                        if (strongDefinition is not null)
                            throw new InvalidOperationException($"Duplicate definition of global object '{name}'.");
                        strongDefinition = declaration;
                        continue;
                    }

                    if (declaration.StorageClass == StorageClass.Extern)
                        continue;

                    var size = GetGlobalStorageSize(declaration.Type);
                    if (tentativeDefinition is null || size > tentativeSize)
                    {
                        tentativeDefinition = declaration;
                        tentativeSize = size;
                    }
                }

                var definition = strongDefinition ?? tentativeDefinition;
                if (definition is null)
                {
                    if (internalLinkage)
                        throw new InvalidOperationException($"Undefined internal object '{name}'.");
                    AddExternalObjectSymbol(declarations[0].Symbol!);
                    continue;
                }

                var label = CreateUniqueGlobalLabel(name);
                _dataLabelsByName.Add(name, label);
                foreach (var declaration in declarations)
                {
                    if (declaration.Symbol is not null)
                        _dataLabels[declaration.Symbol] = label;
                }

                {
                    var size = GetGlobalStorageSize(definition.Type);
                    var alignment = Math.Max(1, _target.AlignOf(definition.Type));
                    var binding = internalLinkage ? RVObjectSymbolBinding.Local : RVObjectSymbolBinding.Global;
                    if (strongDefinition is null)
                    {
                        var offset = _bss.Allocate(size, alignment);
                        _bss.DefineSymbol(
                            label,
                            offset,
                            size,
                            binding,
                            _symbols,
                            isTentative: !internalLinkage);
                        continue;
                    }

                    var section = IsReadOnlyGlobal(definition) ? _rodata : _data;
                    var symbolOffset = section.Align(alignment);
                    section.DefineSymbol(label, symbolOffset, size, binding, _symbols);
                    var bytes = EmitInitializer(section, definition.Type, definition.Initializer!, size);
                    if (bytes < size)
                        section.EmitZero(size - bytes);
                }
            }
        }

        private int GetGlobalStorageSize(QualifiedType type)
            => type.Type is ArrayType { Length: null } incompleteArray
                ? Math.Max(1, _target.SizeOf(incompleteArray.ElementType))
                : Math.Max(1, _target.SizeOf(type));

        private static bool IsReadOnlyGlobal(LirGlobal global)
            => (global.Type.Qualifiers & TypeQualifiers.Const) != 0;

        private void AddExternalObjectSymbol(Symbol symbol)
        {
            var name = CreateExternalLabel(symbol.Name);
            _symbols.Add(new RVObjectSymbol(name, string.Empty, 0, 0, RVObjectSymbolBinding.External, RVObjectSymbolKind.Object));
        }

        private int EmitInitializer(DataSectionBuilder section, QualifiedType type, GimpleInitializer initializer, int availableSize)
        {
            if (initializer is GimpleExpressionInitializer expressionInitializer)
                return EmitExpressionInitializer(section, type, expressionInitializer.Expression, availableSize);

            if (initializer is GimpleInitializerList list)
                return EmitInitializerList(section, type, list, availableSize);

            section.EmitZero(availableSize);
            return availableSize;
        }

        private int EmitInitializerList(DataSectionBuilder section, QualifiedType type, GimpleInitializerList list, int availableSize)
        {
            var start = section.ByteLength;
            if (type.Type is ArrayType array)
            {
                var elementType = array.ElementType;
                var elementSize = Math.Max(1, _target.SizeOf(elementType));
                foreach (var item in list.Items)
                {
                    if (section.ByteLength - start >= availableSize)
                        break;
                    var used = EmitInitializer(section, elementType, item.Initializer, Math.Min(elementSize, availableSize - (section.ByteLength - start)));
                    if (used < elementSize && section.ByteLength - start < availableSize)
                        section.EmitZero(Math.Min(elementSize - used, availableSize - (section.ByteLength - start)));
                }
            }
            else
            {
                foreach (var item in list.Items)
                {
                    if (section.ByteLength - start >= availableSize)
                        break;
                    var itemType = item.Initializer.TargetType;
                    var itemSize = Math.Max(1, _target.SizeOf(itemType));
                    EmitInitializer(section, itemType, item.Initializer, Math.Min(itemSize, availableSize - (section.ByteLength - start)));
                }
            }

            return section.ByteLength - start;
        }

        private int EmitExpressionInitializer(DataSectionBuilder section, QualifiedType type, GimpleValue expression, int availableSize)
        {
            if (expression is GimpleConstantValue constant)
                return EmitConstantInitializer(section, type, constant.Value, availableSize);

            if (expression is GimpleSymbolValue symbolValue)
            {
                EmitPointerRelocation(section, GetSymbolLabel(symbolValue.Symbol));
                return Math.Min(availableSize, _target.PointerSize);
            }

            if (expression is GimpleAddressOfExpression addressOf && addressOf.Target is GimpleSymbolValue addressSymbol)
            {
                EmitPointerRelocation(section, GetSymbolLabel(addressSymbol.Symbol));
                return Math.Min(availableSize, _target.PointerSize);
            }

            section.EmitZero(availableSize);
            return availableSize;
        }

        private int EmitConstantInitializer(DataSectionBuilder section, QualifiedType type, object? value, int availableSize)
        {
            if (value is string text && type.Type is ArrayType)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var count = Math.Min(availableSize, checked(bytes.Length + 1));
                for (var i = 0; i < count; i++)
                    section.EmitByte(i < bytes.Length ? bytes[i] : (byte)0);
                return count;
            }

            if (IsFloatType(type))
            {
                if (IsFloat32(type))
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    section.EmitBytes(raw, Math.Min(availableSize, 4));
                    return Math.Min(availableSize, 4);
                }

                var raw64 = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                section.EmitBytes(raw64, Math.Min(availableSize, 8));
                return Math.Min(availableSize, 8);
            }

            var size = Math.Min(Math.Max(1, _target.SizeOf(type)), availableSize);
            var integer = ConvertIntegerConstant(value);
            section.EmitInteger(integer, size, _target.Endianness);
            return size;
        }

        private void EmitPointerRelocation(DataSectionBuilder section, string symbol)
        {
            var offset = section.ByteLength;
            section.EmitZero(_target.PointerSize);
            section.AddRelocation(offset, symbol, 0, _target.PointerSize == 8 ? RVObjectRelocationKind.Absolute64 : RVObjectRelocationKind.Absolute32);
        }

        private string GetSymbolLabel(Symbol symbol)
        {
            if (symbol is FunctionSymbol function)
            {
                if (_functionLabels.TryGetValue(function, out var functionLabel))
                    return functionLabel;
                if (_functionLabelsByName.TryGetValue(function.Name, out functionLabel))
                    return functionLabel;
                if (_fileScopeLinkage.IsInternal(function))
                    throw new InvalidOperationException($"Undefined internal function '{function.Name}'.");
                return CreateExternalLabel(function.Name);
            }

            if (_dataLabels.TryGetValue(symbol, out var dataLabel))
                return dataLabel;
            if (_dataLabelsByName.TryGetValue(symbol.Name, out dataLabel))
                return dataLabel;

            return CreateExternalLabel(symbol.Name);
        }

        private void EmitFunction(LirFunction function)
        {
            if (function.Symbol is null || !_functionLabels.TryGetValue(function.Symbol, out var label))
                throw new NotSupportedException("Cannot emit anonymous functions to object code.");

            var allocation = LinearScanRegisterAllocator.Allocate(function, _target, _allocationOptions);
            var blockLabels = new Dictionary<LirBlock, string>();
            foreach (var block in function.Blocks)
                blockLabels.Add(block, CreateLocalLabel($"{label}_{block.Name}"));

            var context = new FunctionEmissionContext(this, function, allocation, label, blockLabels);
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);
            context.EmitPrologue();
            context.EmitBlocks();
            context.EmitTrap();
            var size = _text.ByteLength - startOffset;
            var binding = _fileScopeLinkage.IsInternal(function.Symbol) ? RVObjectSymbolBinding.Local : RVObjectSymbolBinding.Global;
            _symbols.Add(new RVObjectSymbol(label, TextSectionName, startOffset, size, binding, RVObjectSymbolKind.Function));
        }

        private string CreateUniqueGlobalLabel(string name)
        {
            var baseName = SanitizeSymbolName(name);
            if (baseName.Length == 0)
                baseName = "sym";
            var candidate = baseName;
            var suffix = 0;
            while (!_usedLabels.Add(candidate))
                candidate = $"{baseName}_{(++suffix)}";
            return candidate;
        }

        private string CreateExternalLabel(string name)
        {
            var label = SanitizeSymbolName(name);
            return label.Length == 0 ? "extern" : label;
        }

        private string CreateLocalLabel(string prefix)
        {
            var baseName = $".L{SanitizeSymbolName(prefix)}";
            var candidate = baseName;
            while (!_usedLabels.Add(candidate))
                candidate = $"{baseName}_{(++_nextLocalId)}";
            return candidate;
        }

        private string CreateStringLiteral(string text)
        {
            if (_stringLabels.TryGetValue(text, out var existing))
                return existing;

            var label = CreateLocalLabel("str");
            var bytes = Encoding.UTF8.GetBytes(text);
            var offset = _rodata.Align(1);
            _rodata.DefineSymbol(label, offset, bytes.Length + 1, RVObjectSymbolBinding.Local, _symbols);
            _rodata.EmitBytes(bytes, bytes.Length);
            _rodata.EmitByte(0);
            _stringLabels.Add(text, label);
            return label;
        }

        private static string SanitizeSymbolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var sb = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '$' || ch == '.')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            if (sb.Length != 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        private static long ConvertIntegerConstant(object? value)
        {
            return value switch
            {
                null => 0,
                bool b => b ? 1 : 0,
                byte b => b,
                sbyte s => s,
                short s => s,
                ushort u => u,
                int i => i,
                uint u => u,
                long l => l,
                ulong u when u <= long.MaxValue => (long)u,
                ulong u => unchecked((long)u),
                char c => c,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            };
        }

        private static bool IsSignedIntegerType(QualifiedType type)
        {
            if (type.Type is EnumType)
                return true;
            return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.SignedChar or BuiltinTypeKind.Short
                or BuiltinTypeKind.Int or BuiltinTypeKind.Long or BuiltinTypeKind.LongLong;
        }

        private static bool IsUnsignedIntegerType(QualifiedType type)
        {
            return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.Char or BuiltinTypeKind.UnsignedChar
                or BuiltinTypeKind.UnsignedShort or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;
        }

        private static bool IsIntegerLike(QualifiedType type)
            => (type.Type.Kind is TypeKind.Builtin or TypeKind.Enum) && !IsFloatType(type) && !IsVoid(type);

        private static bool IsVoid(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

        private static bool IsPointerLike(QualifiedType type)
            => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

        private static bool IsAggregateType(QualifiedType type)
            => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array;

        private static bool IsFloatType(QualifiedType type)
            => IsFloat32(type) || IsFloat64(type) || IsLongDouble(type);

        private static bool IsFloat32(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float };

        private static bool IsFloat64(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Double };

        private static bool IsLongDouble(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.LongDouble };

        private static bool IsRiscVVectorType(QualifiedType type)
            => type.Type is RVVectorType;

        private static bool IsRiscVVectorIntrinsicCall(LirInstruction instruction)
        {
            if (instruction.Kind != LirInstructionKind.Call || instruction.Operands.Length == 0)
                return false;

            var callee = instruction.Operands[0];
            return callee.Kind == LirOperandKind.Symbol &&
                callee.Symbol is FunctionSymbol function &&
                function.Name.StartsWith("__riscv_v", StringComparison.Ordinal);
        }

        private sealed class FunctionEmissionContext
        {
            private readonly RiscVCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly string _functionLabel;
            private readonly IReadOnlyDictionary<LirBlock, string> _labels;
            private readonly bool _hasCalls;
            private readonly int _raSaveOffset;
            private readonly int _riscVVarArgsSaveAreaOffset;
            private readonly int _riscVVarArgsSaveAreaSize;
            private readonly int _totalFrameSize;

            public FunctionEmissionContext(
                RiscVCodeGenerator owner,
                LirFunction function,
                AllocationResult allocation,
                string functionLabel,
                IReadOnlyDictionary<LirBlock, string> labels)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _function = function ?? throw new ArgumentNullException(nameof(function));
                _allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
                _functionLabel = functionLabel ?? string.Empty;
                _labels = labels ?? throw new ArgumentNullException(nameof(labels));
                _hasCalls = function.Blocks.SelectMany(static b => b.Instructions).Any(static i =>
                    i.Kind == LirInstructionKind.InlineAssembly ||
                    i.Kind == LirInstructionKind.Call && !IsRiscVVectorIntrinsicCall(i));
                _raSaveOffset = _hasCalls ? AlignUp(_allocation.Frame.FrameSize, _owner._target.PointerAlignment) : -1;
                _riscVVarArgsSaveAreaSize = ComputeRiscVVarArgsSaveAreaSize();
                var baseFrameSize = _allocation.Frame.FrameSize;
                if (_hasCalls)
                    baseFrameSize = Math.Max(baseFrameSize, checked(_raSaveOffset + _owner._target.PointerSize));
                _totalFrameSize = AlignUp(checked(baseFrameSize + _riscVVarArgsSaveAreaSize), _allocation.Frame.FrameAlignment);
                _riscVVarArgsSaveAreaOffset = _riscVVarArgsSaveAreaSize == 0 ? -1 : checked(_totalFrameSize - _riscVVarArgsSaveAreaSize);
            }

            public void EmitPrologue()
            {
                AdjustStack(-_totalFrameSize);
                SaveIncomingVarArgsPointer();
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderBy(static p => p.Value))
                    StoreRegister(pair.Key, Sp, pair.Value, RegisterSaveSize(pair.Key));
                if (_hasCalls)
                    StoreRegister(Ra, Sp, _raSaveOffset, _owner._target.PointerSize);
                SaveIncomingHiddenReturnBuffer();
            }

            public void EmitBlocks()
            {
                foreach (var block in _function.Blocks)
                {
                    _owner._text.DefineLabel(LabelOf(block));
                    foreach (var instruction in block.Instructions)
                        EmitInstruction(instruction);
                }
            }

            public void EmitTrap()
                => Emit(new RVInstruction(RVInstrKind.Ebreak));

            private int ComputeRiscVVarArgsSaveAreaSize()
            {
                if (!_allocation.Frame.HasVarArgsPointer || !_owner._target.IsRiscV || _function.Symbol?.FunctionType?.IsVariadic != true)
                    return 0;

                var cursor = ComputeNamedArgumentCursor();
                var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                var remainingRegisters = Math.Max(0, integerRegisters.Length - cursor.Integer);
                return checked(remainingRegisters * _owner._allocationOptions.StackArgumentSlotSize);
            }

            private AbiCursor ComputeNamedArgumentCursor()
            {
                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var functionType = _function.Symbol?.FunctionType;
                if (functionType is not null)
                {
                    foreach (var parameter in functionType.Parameters)
                    {
                        var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                        _ = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    }
                }
                return cursor;
            }

            private void SaveIncomingVarArgsPointer()
            {
                if (!_allocation.Frame.HasVarArgsPointer)
                    return;

                var cursor = ComputeNamedArgumentCursor();
                if (_riscVVarArgsSaveAreaOffset >= 0)
                {
                    var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                    for (var register = cursor.Integer; register < integerRegisters.Length; register++)
                        StoreRegister(integerRegisters[register], Sp, checked(_riscVVarArgsSaveAreaOffset + (register - cursor.Integer) * _owner._allocationOptions.StackArgumentSlotSize), _owner._target.PointerSize);
                    AddImmediate(GpScratch0, Sp, _riscVVarArgsSaveAreaOffset);
                }
                else
                {
                    AddImmediate(GpScratch0, Sp, IncomingStackOffset(cursor.Stack * _owner._allocationOptions.StackArgumentSlotSize));
                }
                StoreRegister(GpScratch0, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize);
            }

            private void SaveIncomingHiddenReturnBuffer()
            {
                var returnType = _function.Symbol?.FunctionType?.ReturnType;
                if (!returnType.HasValue || !CAbi.RequiresHiddenReturnBuffer(_owner._target, returnType.Value))
                    return;

                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (!_allocation.Frame.HasHiddenReturnBuffer)
                    return;

                if (location.Kind == AbiLocationKind.Register)
                {
                    StoreRegister(location.Register, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(GpScratch0, Sp, IncomingStackOffset(location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), _owner._target.PointerSize, signed: false);
                    StoreRegister(GpScratch0, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                }
            }

            private void EmitInstruction(LirInstruction instruction)
            {
                switch (instruction.Kind)
                {
                    case LirInstructionKind.Nop:
                        EmitNop();
                        break;
                    case LirInstructionKind.Parameter:
                        EmitParameter(instruction);
                        break;
                    case LirInstructionKind.Copy:
                    case LirInstructionKind.Constant:
                        EmitCopyLike(instruction);
                        break;
                    case LirInstructionKind.Cast:
                    case LirInstructionKind.Convert:
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
                    case LirInstructionKind.VaArg:
                        EmitVaArg(instruction);
                        break;
                    case LirInstructionKind.InlineAssembly:
                        EmitInlineAssembly(instruction);
                        break;
                    case LirInstructionKind.Jump:
                        if (!IsFallthroughTarget(instruction.Target))
                            EmitJump(LabelOf(instruction.Target));
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
                        EmitTrap();
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported LIR instruction kind: {instruction.Kind}.");
                }
            }

            private void EmitInlineAssembly(LirInstruction instruction)
            {
                if (instruction.SourceStatement is not GimpleAsmStatement asmStatement)
                {
                    EmitInlineAssemblyText(instruction, instruction.Operator);
                    return;
                }

                var operands = new List<InlineAsmFormattedOperand>();
                var namedOperands = new Dictionary<string, int>(StringComparer.Ordinal);
                var labels = new List<string>();
                var namedLabels = new Dictionary<string, int>(StringComparer.Ordinal);
                var outputFinalizers = new List<Action>();
                var operandIndex = 0;
                var copyIndex = 0;

                foreach (var output in asmStatement.Outputs)
                {
                    InlineAsmFormattedOperand formatted;
                    var storage = output.Target is null
                        ? InlineAsmOperandStorage.Register
                        : InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type);
                    if (storage == InlineAsmOperandStorage.Memory)
                    {
                        if (operandIndex >= instruction.Operands.Length)
                            throw Unsupported(instruction, "Inline assembly output operand is missing from LIR.");
                        var operand = instruction.Operands[operandIndex++];
                        formatted = new InlineAsmFormattedOperand(output.Name, modifier => FormatRiscVAsmMemoryOperand(operand));
                    }
                    else
                    {
                        if (copyIndex >= instruction.ParallelCopies.Length)
                            throw Unsupported(instruction, "Inline assembly output register is missing from LIR.");
                        var destination = instruction.ParallelCopies[copyIndex++].Destination;
                        formatted = CreateRiscVAsmOutputOperand(output, destination, instruction, outputFinalizers);
                    }

                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var input in asmStatement.Inputs)
                {
                    if (operandIndex >= instruction.Operands.Length)
                        throw Unsupported(instruction, "Inline assembly input operand is missing from LIR.");

                    var operand = instruction.Operands[operandIndex++];
                    var formatted = CreateRiscVAsmInputOperand(input, operand, instruction);
                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var label in asmStatement.GotoLabels)
                {
                    if (operandIndex >= instruction.Operands.Length
                        || instruction.Operands[operandIndex].Kind != LirOperandKind.Label
                        || instruction.Operands[operandIndex].Label is null)
                        throw Unsupported(instruction, "Inline assembly goto label is missing from LIR.");

                    var text = LabelOf(instruction.Operands[operandIndex++].Label!);
                    if (label.Symbol is not null && !namedLabels.ContainsKey(label.Symbol.Name))
                        namedLabels.Add(label.Symbol.Name, labels.Count);
                    if (!namedLabels.ContainsKey(label.Name))
                        namedLabels.Add(label.Name, labels.Count);
                    labels.Add(text);
                }

                var expanded = InlineAsmTemplateExpander.Expand(
                    asmStatement.Text,
                    operands,
                    namedOperands,
                    labels,
                    namedLabels,
                    _owner.CreateLocalLabel(_functionLabel + "_asm_id"));
                EmitInlineAssemblyText(instruction, expanded);

                foreach (var finalize in outputFinalizers)
                    finalize();

                if (asmStatement.IsGoto && instruction.Target is not null && !IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
            }

            private void AddAsmOperand(List<InlineAsmFormattedOperand> operands, Dictionary<string, int> namedOperands, InlineAsmFormattedOperand operand)
            {
                if (operand.Name is not null && !namedOperands.ContainsKey(operand.Name))
                    namedOperands.Add(operand.Name, operands.Count);
                operands.Add(operand);
            }

            private InlineAsmFormattedOperand CreateRiscVAsmOutputOperand(
                GimpleAsmOperand operand, LirVirtualRegister destination, LirInstruction instruction, List<Action> finalizers)
            {
                var fixedRegister = TryGetRiscVConstraintRegister(operand.Constraint, destination.Type);
                if (fixedRegister.HasValue)
                {
                    if (operand.IsReadWrite)
                        LoadOperandIntoAs(LirOperand.ForRegister(destination), fixedRegister.Value, destination.Type, instruction);
                    finalizers.Add(() => StoreRiscVAsmOutput(destination, fixedRegister.Value));
                    return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(fixedRegister.Value)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var register = GetWritableRegister(destination, PreferredScratch(destination.Type, GpScratch0, FpScratch0, VecScratch0));
                finalizers.Add(() => StoreWritableRegisterIfSpilled(destination, register));
                return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(register)));
            }

            private InlineAsmFormattedOperand CreateRiscVAsmInputOperand(GimpleAsmOperand operand, LirOperand value, LirInstruction instruction)
            {
                var storage = InlineAsmConstraints.PreferredStorage(operand.Constraint, value.Type);
                if (storage == InlineAsmOperandStorage.Memory)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatRiscVAsmMemoryOperand(value));
                if (storage == InlineAsmOperandStorage.Immediate)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatRiscVAsmImmediate(value));

                var fixedRegister = TryGetRiscVConstraintRegister(operand.Constraint, value.Type);
                if (fixedRegister.HasValue)
                {
                    LoadOperandIntoAs(value, fixedRegister.Value, value.Type, instruction);
                    return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(fixedRegister.Value)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var preferred = PreferredScratch(value.Type, GpScratch1, FpScratch1, VecScratch1);
                return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(LoadOperand(value, preferred))));
            }

            private void StoreRiscVAsmOutput(LirVirtualRegister destination, MachineRegister source)
            {
                var writable = GetWritableRegister(destination, source);
                if (writable != source)
                    MoveRegister(writable, source);
                StoreWritableRegisterIfSpilled(destination, writable);
            }

            private MachineRegister? TryGetRiscVConstraintRegister(string constraint, QualifiedType type)
            {
                var explicitRegister = InlineAsmConstraints.ExplicitRegisterName(constraint);
                if (explicitRegister is null)
                    return null;

                var registerClass = CAbi.PreferredLirRegisterClass(_owner._target, type);
                if (TargetRegisterInfo.TryParseExplicitRegister(_owner._target, explicitRegister, registerClass, out var register))
                    return register;
                return null;
            }

            private string FormatRiscVAsmMemoryOperand(LirOperand operand)
            {
                if (operand.Kind == LirOperandKind.Address && operand.Address is not null)
                {
                    var address = BuildAddress(operand.Address, GpScratch0, GpScratch1);
                    return $"{address.Offset}({RVRegisters.Format(ToRegister(address.BaseRegister))})";
                }

                var register = LoadOperand(operand, GpScratch0);
                return $"0({RVRegisters.Format(ToRegister(register))})";
            }

            private string FormatRiscVAsmImmediate(LirOperand operand)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Immediate:
                        return ConvertIntegerConstant(operand.Immediate).ToString(CultureInfo.InvariantCulture);
                    case LirOperandKind.Symbol when operand.Symbol is not null:
                        return _owner.GetSymbolLabel(operand.Symbol);
                    default:
                        return RVRegisters.Format(ToAnyRegister(LoadOperand(operand, GpScratch1)));
                }
            }


            private void EmitInlineAssemblyText(LirInstruction instruction, string text)
            {
                try
                {
                    _owner._text.EmitAssembly(text, _owner.CreateLocalLabel(_functionLabel + "_asm"), _owner._machineTarget);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
                {
                    throw Unsupported(instruction, $"Invalid RISC-V inline assembly: {ex.Message}");
                }
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var parameterIndex = FindParameterIndex(instruction.Operator);
                if (parameterIndex < 0)
                    throw Unsupported(instruction, "Cannot map parameter to function signature.");

                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    throw Unsupported(instruction, "Parameter instruction requires function type.");

                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);

                for (var i = 0; i < parameterIndex; i++)
                {
                    var precedingParameter = functionType.Parameters[i];
                    var precedingValue = CAbi.ClassifyValue(_owner._target, precedingParameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                    _ = CAbi.AssignArgumentLocation(precedingValue, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                }

                var type = functionType.Parameters[parameterIndex].Type;
                var value = CAbi.ClassifyValue(_owner._target, type, isReturn: false, isVariadicUnnamedArgument: false);

                if (value.PassingKind == AbiPassingKind.Indirect)
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    if (loc.Kind == AbiLocationKind.Register)
                    {
                        CopyMemory(destinationAddress, loc.Register, value.Size);
                    }
                    else if (loc.Kind == AbiLocationKind.Stack)
                    {
                        LoadFromMemory(
                            GpScratch1,
                            Sp,
                            IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)),
                            _owner._target.PointerSize,
                            signed: false);
                        CopyMemory(destinationAddress, GpScratch1, value.Size);
                    }
                    else
                    {
                        throw Unsupported(instruction, "Invalid indirect parameter ABI location.");
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                        if (loc.Kind == AbiLocationKind.Register)
                        {
                            StoreRawBitsToAddress(loc.Register, destinationAddress, segment.Offset, segment.Size);
                        }
                        else if (loc.Kind == AbiLocationKind.Stack)
                        {
                            LoadRawBitsFromMemory(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), segment.Size);
                            StoreRawBitsToAddress(GpScratch1, destinationAddress, segment.Offset, segment.Size);
                        }
                        else
                        {
                            throw Unsupported(instruction, "Invalid multi-register parameter ABI location.");
                        }
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    AddImmediate(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)));
                    CopyMemory(destinationAddress, GpScratch1, value.Size);
                    return;
                }

                var scalarLocation = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var destination = GetWritableRegister(instruction.Result, PreferredScratch(type, GpScratch0, FpScratch0, VecScratch0));
                if (scalarLocation.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(destination, scalarLocation.Register);
                }
                else if (scalarLocation.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(destination, Sp, IncomingStackOffset(
                        scalarLocation.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), SizeOfRegisterType(type), IsSignedIntegerType(type));
                    NormalizeScalarRegister(destination, type);
                }
                else
                {
                    throw Unsupported(instruction, "Invalid scalar parameter ABI location.");
                }

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private int FindParameterIndex(string name)
            {
                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    return -1;

                for (var i = 0; i < functionType.Parameters.Length; i++)
                {
                    if (string.Equals(functionType.Parameters[i].Name, name, StringComparison.Ordinal))
                        return i;
                }

                return -1;
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

                if (RequiresStackBackedScalar(instruction.Result.Type) || RequiresStackBackedScalar(instruction.Operands[0].Type))
                {
                    EmitStackBackedScalarCopy(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type, GpScratch0, FpScratch0, VecScratch0));
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
                    ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type));
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type));
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type, GpScratch0, FpScratch0, VecScratch0));
                if (IsRiscVVectorType(instruction.Result.Type))
                    ZeroVectorRegister(destination);
                else if (IsFloatType(instruction.Result.Type) && UsesHardwareFloating(instruction.Result.Type))
                    LoadFloatingImmediate(destination, 0.0, instruction.Result.Type);
                else
                    MoveRegister(destination, MachineRegister.X0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Unary instruction has no result.");
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Unary instruction has no operand.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingUnary(instruction);
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Operands[0].Type) && instruction.Operator == "!")
                {
                    EmitStackBackedScalarIsZero(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerUnary(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type) || RequiresSoftwareScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type),
                        "Unary operation for scalar wider than one machine register is not implemented yet.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var src = LoadOperand(instruction.Operands[0], GpScratch1);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(dst, src);
                        break;
                    case "-":
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), RVRegister.X0, ToRegister(src)));
                        break;
                    case "~":
                        EmitImm(RVInstrKind.Xori, dst, src, -1);
                        break;
                    case "!":
                        EmitImm(RVInstrKind.Sltiu, dst, src, 1);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported unary operator '{instruction.Operator}'.");
                }

                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private bool TryEmitSoftwareIntegerUnary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return false;
                if (!IsRv32WideInteger(instruction.Result.Type) && !IsRv32WideInteger(instruction.Operands[0].Type))
                    return false;

                if (!IsRv32WideInteger(instruction.Result.Type) || !IsRv32WideInteger(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectConversionHelper(instruction.Operands[0].Type, instruction.Result.Type),
                        "Unary operation requires an explicit wide integer conversion.");

                LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                switch (instruction.Operator)
                {
                    case "+":
                        break;
                    case "-":
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), RVRegister.X0, ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), RVRegister.X0, ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), RVRegister.X0, ToRegister(GpScratch1)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        break;
                    case "~":
                        EmitImm(RVInstrKind.Xori, GpScratch0, GpScratch0, -1);
                        EmitImm(RVInstrKind.Xori, GpScratch1, GpScratch1, -1);
                        break;
                    default:
                        return false;
                }

                StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                return true;
            }

            private bool TryEmitSoftwareIntegerBinary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length != 2)
                    return false;
                if (!IsRv32WideInteger(instruction.Result.Type) && !IsRv32WideInteger(instruction.Operands[0].Type) && !IsRv32WideInteger(instruction.Operands[1].Type))
                    return false;

                var op = instruction.Operator;
                if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
                {
                    EmitWideIntegerRelation(instruction);
                    return true;
                }

                if (!IsRv32WideInteger(instruction.Result.Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type),
                        "Wide integer binary operation requires a wide integer result.");

                switch (op)
                {
                    case "+":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    case "-":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        StoreToMemory(GpScratch0, Sp, _allocation.Frame.FloatingImmediateTempOffset, 4);
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        LoadFromMemory(GpScratch2, Sp, _allocation.Frame.FloatingImmediateTempOffset, 4, signed: false);
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), ToRegister(GpScratch2), ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    case "&":
                    case "|":
                    case "^":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        var opcode = op == "&" ? RVInstrKind.And : op == "|" ? RVInstrKind.Or : RVInstrKind.Xor;
                        Emit(RVInstruction.R(opcode, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(opcode, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    case "<<":
                    case ">>":
                        EmitWideIntegerShift(instruction);
                        return true;
                    case "*":
                        EmitWideIntegerMultiply(instruction);
                        return true;
                    case "/":
                    case "%":
                        EmitWideIntegerDivide(instruction, instruction.Operator == "%");
                        return true;
                    default:
                        return false;
                }
            }

            private void EmitWideIntegerShift(LirInstruction instruction)
            {
                LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                LoadWideIntegerLowWord(instruction.Operands[1], GpScratch2, instruction);
                EmitImm(RVInstrKind.Andi, GpScratch2, GpScratch2, 63);

                var largeShift = _owner.CreateLocalLabel(_functionLabel + "_i64_shift_large");
                var done = _owner.CreateLocalLabel(_functionLabel + "_i64_shift_done");
                EmitBranch(RVInstrKind.Beq, GpScratch2, MachineRegister.X0, done);
                EmitImm(RVInstrKind.Andi, GpScratch3, GpScratch2, 32);
                EmitBranch(RVInstrKind.Bne, GpScratch3, MachineRegister.X0, largeShift);

                LoadImmediate(GpScratch3, 32);
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch3), ToRegister(GpScratch3), ToRegister(GpScratch2)));
                if (instruction.Operator == "<<")
                {
                    MoveRegister(GpScratch4, GpScratch0);
                    Emit(RVInstruction.R(RVInstrKind.Srl, ToRegister(GpScratch4), ToRegister(GpScratch4), ToRegister(GpScratch3)));
                    Emit(RVInstruction.R(RVInstrKind.Sll, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch4)));
                    Emit(RVInstruction.R(RVInstrKind.Sll, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                }
                else
                {
                    MoveRegister(GpScratch4, GpScratch1);
                    Emit(RVInstruction.R(RVInstrKind.Sll, ToRegister(GpScratch4), ToRegister(GpScratch4), ToRegister(GpScratch3)));
                    Emit(RVInstruction.R(RVInstrKind.Srl, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch4)));
                    Emit(RVInstruction.R(IsSignedIntegerType(instruction.Operands[0].Type) ? RVInstrKind.Sra : RVInstrKind.Srl,
                        ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                }
                EmitJump(done);

                _owner._text.DefineLabel(largeShift);
                EmitImm(RVInstrKind.Andi, GpScratch2, GpScratch2, 31);
                if (instruction.Operator == "<<")
                {
                    Emit(RVInstruction.R(RVInstrKind.Sll, ToRegister(GpScratch1), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                    MoveRegister(GpScratch0, MachineRegister.X0);
                }
                else if (IsSignedIntegerType(instruction.Operands[0].Type))
                {
                    Emit(RVInstruction.R(RVInstrKind.Sra, ToRegister(GpScratch0), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                    EmitShiftImmediate(RVInstrKind.Srai, GpScratch1, GpScratch1, 31);
                }
                else
                {
                    Emit(RVInstruction.R(RVInstrKind.Srl, ToRegister(GpScratch0), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                    MoveRegister(GpScratch1, MachineRegister.X0);
                }

                _owner._text.DefineLabel(done);
                StoreWideIntegerResult(instruction.Result!, GpScratch0, GpScratch1);
            }

            private void EmitWideIntegerMultiply(LirInstruction instruction)
            {
                LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                MoveRegister(GpScratch4, MachineRegister.X0);
                MoveRegister(GpScratch5, MachineRegister.X0);

                var loop = _owner.CreateLocalLabel(_functionLabel + "_i64_mul_loop");
                var skipAdd = _owner.CreateLocalLabel(_functionLabel + "_i64_mul_skip");
                var done = _owner.CreateLocalLabel(_functionLabel + "_i64_mul_done");
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch6), ToRegister(GpScratch2), ToRegister(GpScratch3)));
                EmitBranch(RVInstrKind.Beq, GpScratch6, MachineRegister.X0, done);

                _owner._text.DefineLabel(loop);
                EmitImm(RVInstrKind.Andi, GpScratch6, GpScratch2, 1);
                EmitBranch(RVInstrKind.Beq, GpScratch6, MachineRegister.X0, skipAdd);
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch6), ToRegister(GpScratch4), ToRegister(GpScratch0)));
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch4), ToRegister(GpScratch6), ToRegister(GpScratch4)));
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch1)));
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch4)));
                MoveRegister(GpScratch4, GpScratch6);

                _owner._text.DefineLabel(skipAdd);
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch6, GpScratch0, 31);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch0, GpScratch0, 1);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch1, GpScratch1, 1);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch6)));
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch6, GpScratch3, 31);
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch2, GpScratch2, 1);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch2), ToRegister(GpScratch2), ToRegister(GpScratch6)));
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch3, GpScratch3, 1);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch6), ToRegister(GpScratch2), ToRegister(GpScratch3)));
                EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, loop);

                _owner._text.DefineLabel(done);
                StoreWideIntegerResult(instruction.Result!, GpScratch4, GpScratch5);
            }

            private void EmitWideIntegerDivide(LirInstruction instruction, bool wantRemainder)
            {
                LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);

                var divisorReady = _owner.CreateLocalLabel(_functionLabel + "_i64_div_divisor_ready");
                var dividendReady = _owner.CreateLocalLabel(_functionLabel + "_i64_div_dividend_ready");
                var divisorAbsReady = _owner.CreateLocalLabel(_functionLabel + "_i64_div_divisor_abs_ready");
                var loop = _owner.CreateLocalLabel(_functionLabel + "_i64_div_loop");
                var subtract = _owner.CreateLocalLabel(_functionLabel + "_i64_div_subtract");
                var skipSubtract = _owner.CreateLocalLabel(_functionLabel + "_i64_div_skip_subtract");
                var quotientSignReady = _owner.CreateLocalLabel(_functionLabel + "_i64_div_quotient_sign_ready");
                var remainderSignReady = _owner.CreateLocalLabel(_functionLabel + "_i64_div_remainder_sign_ready");
                var zeroResult = _owner.CreateLocalLabel(_functionLabel + "_i64_div_zero");
                var done = _owner.CreateLocalLabel(_functionLabel + "_i64_div_done");

                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch7), ToRegister(GpScratch2), ToRegister(GpScratch3)));
                EmitBranch(RVInstrKind.Bne, GpScratch7, MachineRegister.X0, divisorReady);
                Emit(new RVInstruction(RVInstrKind.Ebreak));
                EmitJump(zeroResult);

                _owner._text.DefineLabel(divisorReady);
                if (IsSignedIntegerType(instruction.Operands[0].Type))
                {
                    EmitShiftImmediate(RVInstrKind.Srai, GpScratch9, GpScratch1, 31);
                    MoveRegister(GpScratch10, GpScratch9);
                    EmitShiftImmediate(RVInstrKind.Srai, GpScratch7, GpScratch3, 31);
                    Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch9), ToRegister(GpScratch9), ToRegister(GpScratch7)));
                    EmitBranch(RVInstrKind.Bge, GpScratch1, MachineRegister.X0, dividendReady);
                    Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch8), RVRegister.X0, ToRegister(GpScratch0)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), RVRegister.X0, ToRegister(GpScratch0)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), RVRegister.X0, ToRegister(GpScratch1)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch8)));
                    _owner._text.DefineLabel(dividendReady);
                    EmitBranch(RVInstrKind.Bge, GpScratch3, MachineRegister.X0, divisorAbsReady);
                    Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch8), RVRegister.X0, ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch2), RVRegister.X0, ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch3), RVRegister.X0, ToRegister(GpScratch3)));
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch3), ToRegister(GpScratch3), ToRegister(GpScratch8)));
                    _owner._text.DefineLabel(divisorAbsReady);
                }
                else
                {
                    MoveRegister(GpScratch9, MachineRegister.X0);
                    MoveRegister(GpScratch10, MachineRegister.X0);
                }

                MoveRegister(GpScratch4, MachineRegister.X0);
                MoveRegister(GpScratch5, MachineRegister.X0);
                LoadImmediate(GpScratch6, 64);

                _owner._text.DefineLabel(loop);
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch7, GpScratch1, 31);
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch8, GpScratch0, 31);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch0, GpScratch0, 1);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch1, GpScratch1, 1);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch8)));
                EmitShiftImmediate(RVInstrKind.Srli, GpScratch8, GpScratch4, 31);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch4, GpScratch4, 1);
                EmitShiftImmediate(RVInstrKind.Slli, GpScratch5, GpScratch5, 1);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch8)));
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch4), ToRegister(GpScratch4), ToRegister(GpScratch7)));
                EmitBranch(RVInstrKind.Bltu, GpScratch5, GpScratch3, skipSubtract);
                EmitBranch(RVInstrKind.Bltu, GpScratch3, GpScratch5, subtract);
                EmitBranch(RVInstrKind.Bltu, GpScratch4, GpScratch2, skipSubtract);

                _owner._text.DefineLabel(subtract);
                MoveRegister(GpScratch7, GpScratch4);
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch4), ToRegister(GpScratch4), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch7), ToRegister(GpScratch7), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch3)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch7)));
                EmitImm(RVInstrKind.Ori, GpScratch0, GpScratch0, 1);

                _owner._text.DefineLabel(skipSubtract);
                EmitImm(RVInstrKind.Addi, GpScratch6, GpScratch6, -1);
                EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, loop);

                EmitBranch(RVInstrKind.Beq, GpScratch9, MachineRegister.X0, quotientSignReady);
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch7), RVRegister.X0, ToRegister(GpScratch0)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), RVRegister.X0, ToRegister(GpScratch0)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), RVRegister.X0, ToRegister(GpScratch1)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch7)));
                _owner._text.DefineLabel(quotientSignReady);

                EmitBranch(RVInstrKind.Beq, GpScratch10, MachineRegister.X0, remainderSignReady);
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch7), RVRegister.X0, ToRegister(GpScratch4)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch4), RVRegister.X0, ToRegister(GpScratch4)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch5), RVRegister.X0, ToRegister(GpScratch5)));
                Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch5), ToRegister(GpScratch5), ToRegister(GpScratch7)));
                _owner._text.DefineLabel(remainderSignReady);

                if (wantRemainder)
                    StoreWideIntegerResult(instruction.Result!, GpScratch4, GpScratch5);
                else
                    StoreWideIntegerResult(instruction.Result!, GpScratch0, GpScratch1);
                EmitJump(done);

                _owner._text.DefineLabel(zeroResult);
                MoveRegister(GpScratch0, MachineRegister.X0);
                MoveRegister(GpScratch1, MachineRegister.X0);
                StoreWideIntegerResult(instruction.Result!, GpScratch0, GpScratch1);
                _owner._text.DefineLabel(done);
            }

            private void EmitWideIntegerRelation(LirInstruction instruction)
            {
                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                var op = instruction.Operator;
                var signed = IsSignedIntegerType(left.Type) || IsSignedIntegerType(right.Type);
                var destination = GetWritableRegister(instruction.Result!, GpScratch0);

                if (op == "==" || op == "!=")
                {
                    LoadWideIntegerOperand(left, GpScratch0, GpScratch1, instruction);
                    LoadWideIntegerOperand(right, GpScratch2, GpScratch3, instruction);
                    Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch1)));
                    if (op == "==")
                        EmitImm(RVInstrKind.Sltiu, destination, GpScratch0, 1);
                    else
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(destination), RVRegister.X0, ToRegister(GpScratch0)));
                }
                else
                {
                    if (op == ">" || op == "<=")
                        EmitWideIntegerLessThan(right, left, destination, signed, instruction);
                    else
                        EmitWideIntegerLessThan(left, right, destination, signed, instruction);

                    if (op == "<=" || op == ">=")
                        EmitImm(RVInstrKind.Xori, destination, destination, 1);
                }

                StoreWritableRegisterIfSpilled(instruction.Result!, destination);
            }

            private void EmitWideIntegerLessThan(LirOperand left, LirOperand right, MachineRegister destination, bool signed, LirInstruction instruction)
            {
                LoadWideIntegerOperand(left, GpScratch0, GpScratch1, instruction);
                LoadWideIntegerOperand(right, GpScratch2, GpScratch3, instruction);
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch2), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                EmitImm(RVInstrKind.Sltiu, GpScratch2, GpScratch2, 1);
                Emit(RVInstruction.R(RVInstrKind.And, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(signed ? RVInstrKind.Slt : RVInstrKind.Sltu, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(destination), ToRegister(GpScratch1), ToRegister(GpScratch0)));
            }

            private bool TryEmitSoftwareIntegerConvert(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return false;

                var source = instruction.Operands[0];
                var sourceWide = IsRv32WideInteger(source.Type);
                var destinationWide = IsRv32WideInteger(instruction.Result.Type);
                if (!sourceWide && !destinationWide)
                    return false;

                if ((!IsIntegerLike(source.Type) && !IsPointerLike(source.Type)) || (!IsIntegerLike(instruction.Result.Type) && !IsPointerLike(instruction.Result.Type)))
                    return false;

                if (destinationWide)
                {
                    if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                    {
                        var address = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                        ZeroMemory(address, SizeOf(instruction.Result.Type));
                        return true;
                    }

                    if (sourceWide)
                    {
                        EmitStackBackedScalarCopy(instruction.Result, source, instruction);
                        return true;
                    }

                    var low = LoadOperand(source, GpScratch0);
                    if (low != GpScratch0)
                        MoveRegister(GpScratch0, low);
                    NormalizeIntegerRegister(GpScratch0, source.Type);
                    if (IsSignedIntegerType(source.Type))
                        EmitShiftImmediate(RVInstrKind.Srai, GpScratch1, GpScratch0, 31);
                    else
                        MoveRegister(GpScratch1, MachineRegister.X0);
                    StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                    return true;
                }

                if (sourceWide)
                {
                    if (instruction.Result.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                    {
                        var destination = GetWritableRegister(instruction.Result, GpScratch0);
                        var nonZero = EmitStackBackedScalarNonZero(source, instruction);
                        MoveRegister(destination, nonZero);
                        StoreWritableRegisterIfSpilled(instruction.Result, destination);
                        return true;
                    }
                    {
                        var destination = GetWritableRegister(instruction.Result, GpScratch0);
                        LoadWideIntegerLowWord(source, destination, instruction);
                        NormalizeIntegerRegister(destination, instruction.Result.Type);
                        StoreWritableRegisterIfSpilled(instruction.Result, destination);
                        return true;
                    }
                }

                return false;
            }

            private void LoadWideIntegerOperand(LirOperand operand, MachineRegister low, MachineRegister high, LirInstruction instruction)
            {
                if (SizeOf(operand.Type) <= _owner._target.RegisterSize && !IsRv32WideInteger(operand.Type))
                {
                    var scalar = LoadOperand(operand, low);
                    if (scalar != low)
                        MoveRegister(low, scalar);
                    NormalizeIntegerRegister(low, operand.Type);
                    if (IsSignedIntegerType(operand.Type))
                        EmitShiftImmediate(RVInstrKind.Srai, high, low, 31);
                    else
                        MoveRegister(high, MachineRegister.X0);
                    return;
                }

                var address = MaterializeScalarStorageAddress(operand, GpScratch3, instruction);
                LoadFromMemory(low, address, WideIntegerLowOffset(), 4, signed: false);
                LoadFromMemory(high, address, WideIntegerHighOffset(), 4, signed: false);
            }

            private void LoadWideIntegerLowWord(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                if (SizeOf(operand.Type) <= _owner._target.RegisterSize && !IsRv32WideInteger(operand.Type))
                {
                    var scalar = LoadOperand(operand, destination);
                    if (scalar != destination)
                        MoveRegister(destination, scalar);
                    return;
                }

                var address = MaterializeScalarStorageAddress(operand, GpScratch3, instruction);
                LoadFromMemory(destination, address, WideIntegerLowOffset(), 4, signed: false);
            }

            private void StoreWideIntegerResult(LirVirtualRegister destination, MachineRegister low, MachineRegister high)
            {
                var address = MaterializeVirtualRegisterStorageAddress(destination, GpScratch3);
                StoreToMemory(low, address, WideIntegerLowOffset(), 4);
                StoreToMemory(high, address, WideIntegerHighOffset(), 4);
            }

            private int WideIntegerLowOffset()
                => _owner._target.Endianness == TargetEndianness.Little ? 0 : 4;

            private int WideIntegerHighOffset()
                => _owner._target.Endianness == TargetEndianness.Little ? 4 : 0;

            private bool IsRv32WideInteger(QualifiedType type)
                => _owner._target.Is32Bit && IsIntegerLike(type) && SizeOf(type) == 8;

            private void EmitFloatingUnary(LirInstruction instruction)
            {
                var operandType = instruction.Operands[0].Type;
                if (IsLongDouble(operandType) || IsLongDouble(instruction.Result!.Type))
                    throw HelperRequired(instruction, SelectFloatingHelper(instruction.Operator, operandType), "long double operation requires a runtime helper.");
                RequireFloatingHardware(operandType, instruction);
                if (instruction.Operator == "!")
                {
                    var dst = GetWritableRegister(instruction.Result!, GpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    LoadFloatingImmediate(FpScratch2, 0.0, operandType);
                    EmitFloatingCompare(IsFloat32(operandType) ? RVInstrKind.FeqS : RVInstrKind.FeqD, dst, src, FpScratch2);
                    StoreWritableRegisterIfSpilled(instruction.Result!, dst);
                    return;
                }

                if (!IsFloatType(instruction.Result!.Type) || !IsFloatType(operandType))
                    throw Unsupported(instruction, "Unsupported floating-point unary conversion shape.");

                var destination = GetWritableRegister(instruction.Result, FpScratch0);
                var source = LoadOperand(instruction.Operands[0], FpScratch1);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(destination, source);
                        break;
                    case "-":
                        EmitFloatingR(IsFloat32(operandType) ? RVInstrKind.FsgnjnS : RVInstrKind.FsgnjnD, destination, source, source);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported floating-point unary operator '{instruction.Operator}'.");
                }

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitBinary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Binary instruction has no result.");
                if (instruction.Operands.Length != 2)
                    throw Unsupported(instruction, "Binary instruction expects two operands.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type) || IsFloatType(instruction.Operands[1].Type))
                {
                    EmitFloatingBinary(instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerBinary(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type)
                    || RequiresSoftwareScalar(instruction.Operands[0].Type)
                    || RequiresSoftwareScalar(instruction.Operands[1].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type),
                        "Binary operation for scalar wider than one machine register is not implemented yet.");

                if (TryEmitPointerBinary(instruction))
                    return;

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var left = LoadOperand(instruction.Operands[0], GpScratch1);
                var right = LoadOperand(instruction.Operands[1], GpScratch2);
                EmitIntegerBinary(instruction, dst, left, right);
                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitFloatingBinary(LirInstruction instruction)
            {
                var lhsType = instruction.Operands[0].Type;
                var rhsType = instruction.Operands[1].Type;
                var floatType = SelectFloatingOperationType(lhsType, rhsType, instruction.Result!.Type);
                if (IsLongDouble(floatType))
                    throw HelperRequired(instruction, SelectFloatingHelper(instruction.Operator, floatType), "long double operation requires a runtime helper.");
                RequireFloatingHardware(floatType, instruction);

                var left = LoadOperandAsFloating(instruction.Operands[0], floatType, FpScratch1, instruction);
                var right = LoadOperandAsFloating(instruction.Operands[1], floatType, FpScratch2, instruction);
                var op = instruction.Operator;

                if (op is "+" or "-" or "*" or "/")
                {
                    if (!IsFloatType(instruction.Result.Type))
                        throw Unsupported(instruction, "Floating arithmetic result must be floating-point.");
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    EmitFloatingR(SelectFloatingArithmeticOpcode(op, floatType), dst, left, right);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    EmitFloatingRelation(op, floatType, dst, left, right);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                throw Unsupported(instruction, $"Unsupported floating-point binary operator '{op}'.");
            }

            private QualifiedType SelectFloatingOperationType(QualifiedType left, QualifiedType right, QualifiedType result)
            {
                if (IsLongDouble(left) || IsLongDouble(right) || IsLongDouble(result))
                    return IsLongDouble(left) ? left : IsLongDouble(right) ? right : result;
                if (IsFloat64(left) || IsFloat64(right) || IsFloat64(result))
                    return IsFloat64(left) ? left : IsFloat64(right) ? right : result;
                if (IsFloat32(left) || IsFloat32(right) || IsFloat32(result))
                    return IsFloat32(left) ? left : IsFloat32(right) ? right : result;
                throw new InvalidOperationException("Expected at least one floating-point operand.");
            }

            private MachineRegister LoadOperandAsFloating(LirOperand operand, QualifiedType floatType, MachineRegister destination, LirInstruction instruction)
            {
                if (IsFloatType(operand.Type))
                {
                    var source = LoadOperand(operand, destination);
                    if (SameBuiltinFloatingType(operand.Type, floatType))
                        return source;
                    if (source != destination)
                        MoveRegister(destination, source);
                    EmitFloatingPrecisionConversion(destination, destination, operand.Type, floatType, instruction);
                    return destination;
                }

                if (IsIntegerLike(operand.Type))
                {
                    var source = LoadOperand(operand, GpScratch0);
                    EmitIntegerToFloat(destination, source, operand.Type, floatType, instruction);
                    return destination;
                }

                throw Unsupported(instruction, "Cannot convert operand to floating-point for binary operation.");
            }

            private void EmitFloatingRelation(string op, QualifiedType type, MachineRegister destination, MachineRegister left, MachineRegister right)
            {
                switch (op)
                {
                    case "==":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, destination, left, right);
                        return;
                    case "!=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, destination, left, right);
                        EmitImm(RVInstrKind.Xori, destination, destination, 1);
                        return;
                    case "<":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FltS : RVInstrKind.FltD, destination, left, right);
                        return;
                    case ">":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FltS : RVInstrKind.FltD, destination, right, left);
                        return;
                    case "<=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FleS : RVInstrKind.FleD, destination, left, right);
                        return;
                    case ">=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FleS : RVInstrKind.FleD, destination, right, left);
                        return;
                    default:
                        throw new InvalidOperationException("Invalid floating-point relation operator.");
                }
            }

            private static RVInstrKind SelectFloatingArithmeticOpcode(string op, QualifiedType type)
            {
                var single = IsFloat32(type);
                return op switch
                {
                    "+" => single ? RVInstrKind.FaddS : RVInstrKind.FaddD,
                    "-" => single ? RVInstrKind.FsubS : RVInstrKind.FsubD,
                    "*" => single ? RVInstrKind.FmulS : RVInstrKind.FmulD,
                    "/" => single ? RVInstrKind.FdivS : RVInstrKind.FdivD,
                    _ => throw new InvalidOperationException("Invalid floating-point arithmetic operator."),
                };
            }

            private bool TryEmitPointerBinary(LirInstruction instruction)
            {
                var lhsType = instruction.Operands[0].Type;
                var rhsType = instruction.Operands[1].Type;
                var resultType = instruction.Result!.Type;
                var op = instruction.Operator;

                if (op == "+" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "+" && IsIntegerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var index = LoadOperand(instruction.Operands[0], GpScratch2);
                    var ptr = LoadOperand(instruction.Operands[1], GpScratch1);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(rhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "-" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "-" && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), ToRegister(left), ToRegister(right)));
                    var scale = PointerScale(lhsType);
                    if (scale > 1)
                        DivideRegisterByScale(dst, scale, GpScratch3);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if ((op == "==" || op == "!=") && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitEquality(dst, left, right, op == "==");
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                return false;
            }

            private void EmitIntegerBinary(LirInstruction instruction, MachineRegister dst, MachineRegister left, MachineRegister right)
            {
                var signed = IsSignedIntegerType(instruction.Operands[0].Type) || IsSignedIntegerType(instruction.Operands[1].Type);
                var wordOp = _owner._target.Is64Bit && Math.Max(SizeOf(instruction.Operands[0].Type), SizeOf(instruction.Operands[1].Type)) <= 4;
                switch (instruction.Operator)
                {
                    case "+": Emit(RVInstruction.R(wordOp ? RVInstrKind.Addw : RVInstrKind.Add, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "-": Emit(RVInstruction.R(wordOp ? RVInstrKind.Subw : RVInstrKind.Sub, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "*": RequireM(instruction); Emit(RVInstruction.R(wordOp ? RVInstrKind.Mulw : RVInstrKind.Mul, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "/":
                        RequireM(instruction); Emit(RVInstruction.R(signed
                        ? (wordOp ? RVInstrKind.Divw : RVInstrKind.Div)
                        : (wordOp ? RVInstrKind.Divuw : RVInstrKind.Divu), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "%":
                        RequireM(instruction); Emit(RVInstruction.R(signed
                        ? (wordOp ? RVInstrKind.Remw : RVInstrKind.Rem)
                        : (wordOp ? RVInstrKind.Remuw : RVInstrKind.Remu), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "&": Emit(RVInstruction.R(RVInstrKind.And, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "|": Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "^": Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "<<": Emit(RVInstruction.R(wordOp ? RVInstrKind.Sllw : RVInstrKind.Sll, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case ">>":
                        Emit(RVInstruction.R(signed
                        ? (wordOp ? RVInstrKind.Sraw : RVInstrKind.Sra)
                        : (wordOp ? RVInstrKind.Srlw : RVInstrKind.Srl), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "==": EmitEquality(dst, left, right, equal: true); return;
                    case "!=": EmitEquality(dst, left, right, equal: false); return;
                    case "<": EmitLessThan(dst, left, right, signed); return;
                    case ">": EmitLessThan(dst, right, left, signed); return;
                    case "<=": EmitLessThan(dst, right, left, signed); EmitImm(RVInstrKind.Xori, dst, dst, 1); return;
                    case ">=": EmitLessThan(dst, left, right, signed); EmitImm(RVInstrKind.Xori, dst, dst, 1); return;
                    default: throw Unsupported(instruction, $"Unsupported binary operator '{instruction.Operator}'.");
                }
            }

            private void EmitEquality(MachineRegister dst, MachineRegister left, MachineRegister right, bool equal)
            {
                Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(dst), ToRegister(left), ToRegister(right)));
                if (equal)
                    EmitImm(RVInstrKind.Sltiu, dst, dst, 1);
                else
                    Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(dst), RVRegister.X0, ToRegister(dst)));
            }

            private void EmitLessThan(MachineRegister dst, MachineRegister left, MachineRegister right, bool signed)
                => Emit(RVInstruction.R(signed ? RVInstrKind.Slt : RVInstrKind.Sltu, ToRegister(dst), ToRegister(left), ToRegister(right)));

            private void RequireM(LirInstruction instruction)
            {
                if (!_owner._machineTarget.HasM)
                    throw Unsupported(instruction, "Integer multiply/divide/remainder requires M extension.");
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Conversion instruction has no source operand.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingOrMixedConvert(instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerConvert(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type) || RequiresSoftwareScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectConversionHelper(instruction.Operands[0].Type, instruction.Result.Type),
                        "Scalar conversion wider than one machine register is not implemented yet.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var src = LoadOperand(instruction.Operands[0], GpScratch1);
                MoveRegister(dst, src);
                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitFloatingOrMixedConvert(LirInstruction instruction)
            {
                var srcType = instruction.Operands[0].Type;
                var dstType = instruction.Result!.Type;

                if (IsLongDouble(srcType) || IsLongDouble(dstType))
                    throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType), "long double conversion requires a runtime helper.");

                if (IsFloatType(srcType) && IsFloatType(dstType))
                {
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    if (SameBuiltinFloatingType(srcType, dstType))
                        MoveRegister(dst, src);
                    else
                        EmitFloatingPrecisionConversion(dst, src, srcType, dstType, instruction);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if ((IsIntegerLike(srcType) || IsPointerLike(srcType)) && IsFloatType(dstType))
                {
                    if (RequiresSoftwareScalar(srcType))
                        throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType),
                            "Integer-to-floating conversion from scalar wider than one machine register is not implemented yet.");
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    var src = LoadOperand(instruction.Operands[0], GpScratch1);
                    EmitIntegerToFloat(dst, src, srcType, dstType, instruction);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if (IsFloatType(srcType) && (IsIntegerLike(dstType) || IsPointerLike(dstType)))
                {
                    if (RequiresSoftwareScalar(dstType))
                        throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType),
                            "Floating-to-integer conversion to scalar wider than one machine register is not implemented yet.");
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    EmitFloatToInteger(dst, src, srcType, dstType, instruction);
                    NormalizeIntegerRegister(dst, dstType);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                throw Unsupported(instruction, "Unsupported mixed floating-point conversion.");
            }

            private void EmitFloatingPrecisionConversion(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(destinationType, instruction);
                RequireFloatingHardware(sourceType, instruction);
                if (IsFloat32(sourceType) && IsFloat64(destinationType))
                {
                    EmitFloatingConvert(RVInstrKind.FcvtDS, destination, source);
                    return;
                }
                if (IsFloat64(sourceType) && IsFloat32(destinationType))
                {
                    EmitFloatingConvert(RVInstrKind.FcvtSD, destination, source);
                    return;
                }
                throw Unsupported(instruction, "Unsupported floating-point precision conversion.");
            }

            private void EmitIntegerToFloat(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(destinationType, instruction);
                var unsigned = IsUnsignedIntegerType(sourceType) || IsPointerLike(sourceType);
                var sourceSize = IsPointerLike(sourceType) ? _owner._target.PointerSize : SizeOf(sourceType);
                RVInstrKind opcode;
                if (IsFloat32(destinationType))
                    opcode = sourceSize <= 4 ? (unsigned ? RVInstrKind.FcvtSWu : RVInstrKind.FcvtSW) : (unsigned ? RVInstrKind.FcvtSLu : RVInstrKind.FcvtSL);
                else if (IsFloat64(destinationType))
                    opcode = sourceSize <= 4 ? (unsigned ? RVInstrKind.FcvtDWu : RVInstrKind.FcvtDW) : (unsigned ? RVInstrKind.FcvtDLu : RVInstrKind.FcvtDL);
                else
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "long double conversion requires a runtime helper.");

                if (sourceSize > _owner._target.RegisterSize)
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType),
                        "Integer-to-floating conversion from scalar wider than one machine register is not implemented yet.");
                EmitFloatingConvertFromInteger(opcode, destination, source);
            }

            private void EmitFloatToInteger(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(sourceType, instruction);
                var unsigned = IsUnsignedIntegerType(destinationType) || IsPointerLike(destinationType);
                var destinationSize = IsPointerLike(destinationType) ? _owner._target.PointerSize : SizeOf(destinationType);
                RVInstrKind opcode;
                if (IsFloat32(sourceType))
                    opcode = destinationSize <= 4 ? (unsigned ? RVInstrKind.FcvtWuS : RVInstrKind.FcvtWS) : (unsigned ? RVInstrKind.FcvtLuS : RVInstrKind.FcvtLS);
                else if (IsFloat64(sourceType))
                    opcode = destinationSize <= 4 ? (unsigned ? RVInstrKind.FcvtWuD : RVInstrKind.FcvtWD) : (unsigned ? RVInstrKind.FcvtLuD : RVInstrKind.FcvtLD);
                else
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "long double conversion requires a runtime helper.");

                if (destinationSize > _owner._target.RegisterSize)
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType),
                        "Floating-to-integer conversion to scalar wider than one machine register is not implemented yet.");
                EmitFloatingConvertToInteger(opcode, destination, source);
            }

            private void EmitAddressOf(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid addressof instruction.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
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
                    CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type));
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    var sourceAddress = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                    if (sourceAddress.Offset != 0)
                    {
                        AddImmediate(GpScratch1, sourceAddress.BaseRegister, sourceAddress.Offset);
                        CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type));
                    }
                    else
                    {
                        CopyMemory(destinationAddress, sourceAddress.BaseRegister, SizeOf(instruction.Result.Type));
                    }
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type, GpScratch0, FpScratch0, VecScratch0));
                var address = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                LoadFromMemory(dst, address.BaseRegister, address.Offset, SizeOfRegisterType(instruction.Result.Type), IsSignedIntegerType(instruction.Result.Type));
                NormalizeScalarRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid store instruction.");
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Store instruction has no source operand.");

                var storeType = instruction.Address.ElementType;
                if (IsAggregateType(storeType))
                {
                    MaterializeAddress(instruction.Address, GpScratch0);
                    MaterializeOperandStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                    CopyMemory(GpScratch0, GpScratch1, SizeOf(storeType));
                    return;
                }

                if (RequiresStackBackedScalar(storeType))
                {
                    var destinationAddress = BuildAddress(instruction.Address, GpScratch0, GpScratch2);
                    var sourceAddress = MaterializeScalarStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                    if (destinationAddress.Offset != 0)
                    {
                        AddImmediate(GpScratch0, destinationAddress.BaseRegister, destinationAddress.Offset);
                        CopyMemory(GpScratch0, sourceAddress, SizeOf(storeType));
                    }
                    else
                    {
                        CopyMemory(destinationAddress.BaseRegister, sourceAddress, SizeOf(storeType));
                    }
                    return;
                }

                var src = LoadOperandAs(instruction.Operands[0], storeType, PreferredScratch(storeType, GpScratch0, FpScratch0, VecScratch0), instruction);
                var address = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                StoreToMemory(src, address.BaseRegister, address.Offset, Math.Min(SizeOfRegisterType(storeType), SizeOf(storeType)));
            }

            private void EmitZeroMemory(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid zeromem instruction.");
                var size = instruction.Operands.Length == 0 ? SizeOf(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
                MaterializeAddress(instruction.Address, GpScratch0);
                ZeroMemory(GpScratch0, size);
            }

            private void EmitCall(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Call has no callee operand.");
                if (TryEmitRiscVVectorIntrinsic(instruction))
                    return;

                MarshalCallArguments(instruction, 1);
                var callee = instruction.Operands[0];
                if (TryResolveDirectCallLabel(callee, out var label))
                {
                    EmitCall(label);
                }
                else
                {
                    var target = LoadOperand(callee, GpScratch0);
                    Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, ToRegister(target), 0));
                }

                EmitCallResult(instruction);
            }

            private bool TryEmitRiscVVectorIntrinsic(LirInstruction instruction)
            {
                if (!IsRiscVVectorIntrinsicCall(instruction))
                    return false;

                if (!_owner._machineTarget.HasV)
                    throw Unsupported(instruction, "RISC-V vector intrinsic requires the V extension.");

                var function = (FunctionSymbol)instruction.Operands[0].Symbol!;
                var name = function.Name.Substring("__riscv_".Length);
                if (!TryGetRiscVVectorElementWidth(name, out var elementWidth))
                    throw Unsupported(instruction, $"Cannot determine the vector element width for intrinsic '{function.Name}'.");

                if (name.StartsWith("vsetvlmax_", StringComparison.Ordinal))
                {
                    RequireIntrinsicOperandCount(instruction, 1);
                    if (instruction.Result is null)
                        throw Unsupported(instruction, "vsetvlmax intrinsic has no result.");
                    var destination = GetWritableRegister(instruction.Result, GpScratch0);
                    EmitVectorConfiguration(destination, MachineRegister.X0, elementWidth);
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                if (name.StartsWith("vsetvl_", StringComparison.Ordinal))
                {
                    RequireIntrinsicOperandCount(instruction, 2);
                    if (instruction.Result is null)
                        throw Unsupported(instruction, "vsetvl intrinsic has no result.");
                    var avl = LoadOperand(instruction.Operands[1], GpScratch1);
                    var destination = GetWritableRegister(instruction.Result, GpScratch0);
                    EmitVectorConfiguration(destination, avl, elementWidth);
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                var firstSeparator = name.IndexOf('_');
                var secondSeparator = firstSeparator < 0 ? -1 : name.IndexOf('_', firstSeparator + 1);
                if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1)
                    throw Unsupported(instruction, $"Invalid RISC-V vector intrinsic name '{function.Name}'.");

                var mnemonic = name.Substring(0, firstSeparator);
                var form = name.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1);
                ValidateVectorFloatingPointSupport(instruction, name);

                if (mnemonic.StartsWith("vle", StringComparison.Ordinal))
                {
                    RequireIntrinsicOperandCount(instruction, 3);
                    if (instruction.Result is null)
                        throw Unsupported(instruction, "Vector load intrinsic has no result.");
                    var address = LoadOperand(instruction.Operands[1], GpScratch0);
                    var vl = LoadOperand(instruction.Operands[2], GpScratch1);
                    var destination = GetWritableRegister(instruction.Result, VecScratch0);
                    EmitVectorConfiguration(MachineRegister.X0, vl, elementWidth);
                    Emit(RVInstruction.Vl(VectorLoadOpcode(elementWidth), ToVectorRegister(destination), ToRegister(address)));
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                if (mnemonic.StartsWith("vse", StringComparison.Ordinal))
                {
                    RequireIntrinsicOperandCount(instruction, 4);
                    var address = LoadOperand(instruction.Operands[1], GpScratch0);
                    var source = LoadOperand(instruction.Operands[2], VecScratch0);
                    var vl = LoadOperand(instruction.Operands[3], GpScratch1);
                    EmitVectorConfiguration(MachineRegister.X0, vl, elementWidth);
                    Emit(RVInstruction.Vs(VectorStoreOpcode(elementWidth), ToVectorRegister(source), ToRegister(address)));
                    return true;
                }

                var opcode = VectorIntrinsicOpcode($"{mnemonic}_{form}");
                var accumulator = mnemonic is "vmadd" or "vnmsub" or "vmacc" or "vnmsac";
                if (accumulator)
                    EmitVectorAccumulatorIntrinsic(instruction, opcode, form, elementWidth);
                else
                    EmitVectorOrdinaryIntrinsic(instruction, opcode, form, elementWidth);
                return true;
            }

            private void EmitVectorOrdinaryIntrinsic(LirInstruction instruction, RVInstrKind opcode, string form, int elementWidth)
            {
                RequireIntrinsicOperandCount(instruction, 4);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Vector intrinsic has no result.");

                var vs2 = LoadOperand(instruction.Operands[1], VecScratch1);
                MachineRegister second;
                if (form == "vv")
                    second = LoadOperand(instruction.Operands[2], VecScratch2);
                else if (form == "vf")
                    second = LoadOperand(instruction.Operands[2], FpScratch0);
                else if (form == "vx")
                    second = LoadVectorIntegerScalarOperand(instruction.Operands[2], GpScratch0, instruction);
                else
                    throw Unsupported(instruction, $"Unsupported vector intrinsic operand form '{form}'.");
                var vl = LoadOperand(instruction.Operands[3], GpScratch1);

                EmitVectorConfiguration(MachineRegister.X0, vl, elementWidth);
                if (form == "vv")
                    Emit(RVInstruction.Vv(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToVectorRegister(second)));
                else if (form == "vf")
                    Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToFloatRegister(second)));
                else
                    Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToRegister(second)));

                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                MoveVectorRegister(destination, VecScratch0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitVectorAccumulatorIntrinsic(LirInstruction instruction, RVInstrKind opcode, string form, int elementWidth)
            {
                RequireIntrinsicOperandCount(instruction, 5);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Vector accumulator intrinsic has no result.");

                var oldDestination = LoadOperand(instruction.Operands[1], VecScratch1);
                MachineRegister vs1;
                if (form == "vv")
                    vs1 = LoadOperand(instruction.Operands[2], VecScratch2);
                else if (form == "vx")
                    vs1 = LoadVectorIntegerScalarOperand(instruction.Operands[2], GpScratch0, instruction);
                else
                    throw Unsupported(instruction, $"Unsupported vector accumulator operand form '{form}'.");
                var vs2 = LoadOperand(instruction.Operands[3], VecScratch3);
                var vl = LoadOperand(instruction.Operands[4], GpScratch1);

                MoveVectorRegister(VecScratch0, oldDestination);
                EmitVectorConfiguration(MachineRegister.X0, vl, elementWidth);
                if (form == "vv")
                    Emit(RVInstruction.Vv(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToVectorRegister(vs1)));
                else
                    Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToRegister(vs1)));

                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                MoveVectorRegister(destination, VecScratch0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private MachineRegister LoadVectorIntegerScalarOperand(LirOperand operand, MachineRegister scratch, LirInstruction instruction)
            {
                if (IsRv32WideInteger(operand.Type))
                {
                    LoadWideIntegerLowWord(operand, scratch, instruction);
                    return scratch;
                }
                return LoadOperand(operand, scratch);
            }

            private void ValidateVectorFloatingPointSupport(LirInstruction instruction, string name)
            {
                if (name.Contains("_f32m1", StringComparison.Ordinal) && !_owner._machineTarget.HasF)
                    throw Unsupported(instruction, "32-bit floating-point vector intrinsic requires the F extension.");
                if (name.Contains("_f64m1", StringComparison.Ordinal) && !_owner._machineTarget.HasD)
                    throw Unsupported(instruction, "64-bit floating-point vector intrinsic requires the D extension.");
            }

            private static bool TryGetRiscVVectorElementWidth(string name, out int elementWidth)
            {
                if (name.Contains("64m1", StringComparison.Ordinal))
                    elementWidth = 64;
                else if (name.Contains("32m1", StringComparison.Ordinal))
                    elementWidth = 32;
                else if (name.Contains("16m1", StringComparison.Ordinal))
                    elementWidth = 16;
                else if (name.Contains("8m1", StringComparison.Ordinal))
                    elementWidth = 8;
                else
                {
                    elementWidth = 0;
                    return false;
                }
                return true;
            }

            private void RequireIntrinsicOperandCount(LirInstruction instruction, int count)
            {
                if (instruction.Operands.Length != count)
                    throw Unsupported(instruction, "RISC-V vector intrinsic has an invalid operand count.");
            }

            private void EmitVectorConfiguration(MachineRegister destination, MachineRegister avl, int elementWidth)
                => Emit(RVInstruction.Vsetvli(ToRegister(destination), ToRegister(avl), VectorTypeImmediate(elementWidth)));

            private void EmitVectorMaxConfiguration(int elementWidth)
                => EmitVectorConfiguration(GpVectorConfigScratch, MachineRegister.X0, elementWidth);

            private static int VectorTypeImmediate(int elementWidth)
            {
                var vsew = elementWidth switch
                {
                    8 => 0,
                    16 => 1,
                    32 => 2,
                    64 => 3,
                    _ => throw new ArgumentOutOfRangeException(nameof(elementWidth)),
                };
                return (vsew << 3) | (1 << 6) | (1 << 7);
            }

            private static RVInstrKind VectorLoadOpcode(int elementWidth)
                => elementWidth switch
                {
                    8 => RVInstrKind.Vle8V,
                    16 => RVInstrKind.Vle16V,
                    32 => RVInstrKind.Vle32V,
                    64 => RVInstrKind.Vle64V,
                    _ => throw new ArgumentOutOfRangeException(nameof(elementWidth)),
                };

            private static RVInstrKind VectorStoreOpcode(int elementWidth)
                => elementWidth switch
                {
                    8 => RVInstrKind.Vse8V,
                    16 => RVInstrKind.Vse16V,
                    32 => RVInstrKind.Vse32V,
                    64 => RVInstrKind.Vse64V,
                    _ => throw new ArgumentOutOfRangeException(nameof(elementWidth)),
                };

            private static RVInstrKind VectorIntrinsicOpcode(string key)
                => key switch
                {
                    "vadd_vv" => RVInstrKind.VaddVv,
                    "vadd_vx" => RVInstrKind.VaddVx,
                    "vand_vv" => RVInstrKind.VandVv,
                    "vand_vx" => RVInstrKind.VandVx,
                    "vdiv_vv" => RVInstrKind.VdivVv,
                    "vdiv_vx" => RVInstrKind.VdivVx,
                    "vdivu_vv" => RVInstrKind.VdivuVv,
                    "vdivu_vx" => RVInstrKind.VdivuVx,
                    "vfadd_vf" => RVInstrKind.VfaddVf,
                    "vfadd_vv" => RVInstrKind.VfaddVv,
                    "vfdiv_vf" => RVInstrKind.VfdivVf,
                    "vfdiv_vv" => RVInstrKind.VfdivVv,
                    "vfmax_vf" => RVInstrKind.VfmaxVf,
                    "vfmax_vv" => RVInstrKind.VfmaxVv,
                    "vfmin_vf" => RVInstrKind.VfminVf,
                    "vfmin_vv" => RVInstrKind.VfminVv,
                    "vfmul_vf" => RVInstrKind.VfmulVf,
                    "vfmul_vv" => RVInstrKind.VfmulVv,
                    "vfrdiv_vf" => RVInstrKind.VfrdivVf,
                    "vfrsub_vf" => RVInstrKind.VfrsubVf,
                    "vfsgnj_vf" => RVInstrKind.VfsgnjVf,
                    "vfsgnj_vv" => RVInstrKind.VfsgnjVv,
                    "vfsgnjn_vf" => RVInstrKind.VfsgnjnVf,
                    "vfsgnjn_vv" => RVInstrKind.VfsgnjnVv,
                    "vfsgnjx_vf" => RVInstrKind.VfsgnjxVf,
                    "vfsgnjx_vv" => RVInstrKind.VfsgnjxVv,
                    "vfsub_vf" => RVInstrKind.VfsubVf,
                    "vfsub_vv" => RVInstrKind.VfsubVv,
                    "vmacc_vv" => RVInstrKind.VmaccVv,
                    "vmacc_vx" => RVInstrKind.VmaccVx,
                    "vmadd_vv" => RVInstrKind.VmaddVv,
                    "vmadd_vx" => RVInstrKind.VmaddVx,
                    "vmax_vv" => RVInstrKind.VmaxVv,
                    "vmax_vx" => RVInstrKind.VmaxVx,
                    "vmaxu_vv" => RVInstrKind.VmaxuVv,
                    "vmaxu_vx" => RVInstrKind.VmaxuVx,
                    "vmfeq_vf" => RVInstrKind.VmfeqVf,
                    "vmfeq_vv" => RVInstrKind.VmfeqVv,
                    "vmfge_vf" => RVInstrKind.VmfgeVf,
                    "vmfgt_vf" => RVInstrKind.VmfgtVf,
                    "vmfle_vf" => RVInstrKind.VmfleVf,
                    "vmfle_vv" => RVInstrKind.VmfleVv,
                    "vmflt_vf" => RVInstrKind.VmfltVf,
                    "vmflt_vv" => RVInstrKind.VmfltVv,
                    "vmfne_vf" => RVInstrKind.VmfneVf,
                    "vmfne_vv" => RVInstrKind.VmfneVv,
                    "vmin_vv" => RVInstrKind.VminVv,
                    "vmin_vx" => RVInstrKind.VminVx,
                    "vminu_vv" => RVInstrKind.VminuVv,
                    "vminu_vx" => RVInstrKind.VminuVx,
                    "vmseq_vv" => RVInstrKind.VmseqVv,
                    "vmseq_vx" => RVInstrKind.VmseqVx,
                    "vmsgt_vx" => RVInstrKind.VmsgtVx,
                    "vmsgtu_vx" => RVInstrKind.VmsgtuVx,
                    "vmsle_vv" => RVInstrKind.VmsleVv,
                    "vmsle_vx" => RVInstrKind.VmsleVx,
                    "vmsleu_vv" => RVInstrKind.VmsleuVv,
                    "vmsleu_vx" => RVInstrKind.VmsleuVx,
                    "vmslt_vv" => RVInstrKind.VmsltVv,
                    "vmslt_vx" => RVInstrKind.VmsltVx,
                    "vmsltu_vv" => RVInstrKind.VmsltuVv,
                    "vmsltu_vx" => RVInstrKind.VmsltuVx,
                    "vmsne_vv" => RVInstrKind.VmsneVv,
                    "vmsne_vx" => RVInstrKind.VmsneVx,
                    "vmul_vv" => RVInstrKind.VmulVv,
                    "vmul_vx" => RVInstrKind.VmulVx,
                    "vmulh_vv" => RVInstrKind.VmulhVv,
                    "vmulh_vx" => RVInstrKind.VmulhVx,
                    "vmulhsu_vv" => RVInstrKind.VmulhsuVv,
                    "vmulhsu_vx" => RVInstrKind.VmulhsuVx,
                    "vmulhu_vv" => RVInstrKind.VmulhuVv,
                    "vmulhu_vx" => RVInstrKind.VmulhuVx,
                    "vnmsac_vv" => RVInstrKind.VnmsacVv,
                    "vnmsac_vx" => RVInstrKind.VnmsacVx,
                    "vnmsub_vv" => RVInstrKind.VnmsubVv,
                    "vnmsub_vx" => RVInstrKind.VnmsubVx,
                    "vor_vv" => RVInstrKind.VorVv,
                    "vor_vx" => RVInstrKind.VorVx,
                    "vrem_vv" => RVInstrKind.VremVv,
                    "vrem_vx" => RVInstrKind.VremVx,
                    "vremu_vv" => RVInstrKind.VremuVv,
                    "vremu_vx" => RVInstrKind.VremuVx,
                    "vrgather_vv" => RVInstrKind.VrgatherVv,
                    "vrgather_vx" => RVInstrKind.VrgatherVx,
                    "vrsub_vx" => RVInstrKind.VrsubVx,
                    "vsll_vv" => RVInstrKind.VsllVv,
                    "vsll_vx" => RVInstrKind.VsllVx,
                    "vsra_vv" => RVInstrKind.VsraVv,
                    "vsra_vx" => RVInstrKind.VsraVx,
                    "vsrl_vv" => RVInstrKind.VsrlVv,
                    "vsrl_vx" => RVInstrKind.VsrlVx,
                    "vsub_vv" => RVInstrKind.VsubVv,
                    "vsub_vx" => RVInstrKind.VsubVx,
                    "vxor_vv" => RVInstrKind.VxorVv,
                    "vxor_vx" => RVInstrKind.VxorVx,
                    _ => throw new NotSupportedException($"Unsupported vector intrinsic opcode '{key}'."),
                };

            private void MarshalCallArguments(LirInstruction instruction, int startOperand)
            {
                var cursor = new AbiCursor();
                MarshalHiddenReturnBufferArgument(instruction, ref cursor);
                for (var i = startOperand; i < instruction.Operands.Length; i++)
                    MarshalCallArgument(instruction, instruction.Operands[i], ref cursor, i - startOperand);
            }

            private void MarshalHiddenReturnBufferArgument(LirInstruction instruction, ref AbiCursor cursor)
            {
                if (instruction.Result is null || !CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    return;

                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var address = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                StoreArgumentValue(address, location, _owner._target.PointerSize);
            }

            private void MarshalCallArgument(LirInstruction instruction, LirOperand operand, ref AbiCursor cursor, int sourceArgumentIndex)
            {
                var isVariadicUnnamed = instruction.CallSignature is not null && instruction.CallSignature.IsVariadic && sourceArgumentIndex >= instruction.CallSignature.Parameters.Length;
                var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: false, isVariadicUnnamed);
                if (value.PassingKind == AbiPassingKind.Indirect)
                {
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    StoreArgumentValue(GpScratch0, loc, _owner._target.PointerSize);
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    MachineRegister storageBase;
                    if (IsAggregateType(operand.Type))
                    {
                        MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                        storageBase = GpScratch0;
                    }
                    else
                    {
                        storageBase = MaterializeScalarBitsAddress(operand, GpScratch0, instruction);
                    }
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                        LoadRawBitsFromMemory(GpScratch1, storageBase, segment.Offset, segment.Size);
                        StoreArgumentValue(GpScratch1, loc, segment.Size);
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(operand.Type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    AddImmediate(GpScratch1, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize));
                    CopyMemory(GpScratch1, GpScratch0, value.Size);
                    return;
                }

                var scalarLoc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var segmentClass = value.Segments.Length != 0 ? value.Segments[0].RegisterClass : AbiRegisterClass.General;
                var source = LoadOperandForArgument(operand, segmentClass, scalarLoc, instruction);
                StoreArgumentValue(source, scalarLoc, Math.Min(SizeOfRegisterType(operand.Type), Math.Max(1, value.Size)));
            }

            private MachineRegister MaterializeScalarBitsAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                if (RequiresStackBackedScalar(operand.Type))
                    return MaterializeScalarStorageAddress(operand, destination, instruction);

                var offset = _allocation.Frame.FloatingImmediateTempOffset;
                var source = LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0);
                StoreToMemory(source, Sp, offset, Math.Min(SizeOfRegisterType(operand.Type), SizeOf(operand.Type)));
                AddImmediate(destination, Sp, offset);
                return destination;
            }

            private MachineRegister LoadOperandForArgument(LirOperand operand, AbiRegisterClass registerClass, AbiLocation location, LirInstruction instruction)
            {
                if (!IsFloatType(operand.Type))
                    return LoadOperand(operand, GpScratch0);

                if (registerClass == AbiRegisterClass.Floating || location.Kind == AbiLocationKind.Stack)
                    return LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0);

                return LoadFloatingOperandBitsToInteger(operand, instruction);
            }

            private MachineRegister LoadFloatingOperandBitsToInteger(LirOperand operand, LirInstruction instruction)
            {
                if (!UsesHardwareFloating(operand.Type))
                    return LoadOperand(operand, GpScratch0);

                var source = LoadOperand(operand, FpScratch0);
                if (IsFloat32(operand.Type))
                {
                    EmitFloatingMoveToInteger(RVInstrKind.FmvXW, GpScratch0, source);
                    return GpScratch0;
                }

                if (IsFloat64(operand.Type) && _owner._target.Is64Bit)
                {
                    EmitFloatingMoveToInteger(RVInstrKind.FmvXD, GpScratch0, source);
                    return GpScratch0;
                }

                throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type),
                    "Floating-point argument bit move to an integer ABI register requires a runtime helper.");
            }

            private void StoreArgumentValue(MachineRegister source, AbiLocation location, int size)
            {
                if (location.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(location.Register, source);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    StoreToMemory(source, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), size);
                    return;
                }

                throw new InvalidOperationException("Invalid argument ABI location.");
            }

            private void EmitCallResult(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var value = CAbi.ClassifyValue(_owner._target, instruction.Result.Type, isReturn: true, isVariadicUnnamedArgument: false);
                if (value.PassingKind == AbiPassingKind.Indirect)
                    return;

                if (IsAggregateType(instruction.Result.Type))
                {
                    if (value.PassingKind == AbiPassingKind.Indirect)
                        return;

                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size);
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size);
                    }
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, value.Segments.Length != 0 && value.Segments[0].RegisterClass == AbiRegisterClass.Floating ? FpScratch0 : GpScratch0);
                MoveRegister(dst, value.Segments.Length != 0 && value.Segments[0].RegisterClass == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10);
                NormalizeScalarRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private bool TryResolveDirectCallLabel(LirOperand callee, out string label)
            {
                label = string.Empty;
                if (callee.Kind != LirOperandKind.Symbol || callee.Symbol is not FunctionSymbol function)
                    return false;

                if (_owner._functionLabels.TryGetValue(function, out label!))
                    return true;
                if (_owner._functionLabelsByName.TryGetValue(function.Name, out label!))
                    return true;
                if (_owner._fileScopeLinkage.IsInternal(function))
                    throw new InvalidOperationException($"Undefined internal function '{function.Name}'.");

                label = _owner.CreateExternalLabel(function.Name);
                _owner._symbols.Add(new RVObjectSymbol(label, string.Empty, 0, 0, RVObjectSymbolBinding.External, RVObjectSymbolKind.Function));
                return true;
            }

            private void EmitVaStart(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 1)
                {
                    var ap = LoadOperand(instruction.Operands[0], GpScratch0);
                    if (_allocation.Frame.HasVarArgsPointer)
                        LoadFromMemory(GpScratch1, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, signed: false);
                    else
                        MoveRegister(GpScratch1, MachineRegister.X0);
                    StoreRegister(GpScratch1, ap, 0, _owner._target.PointerSize);
                    return;
                }

                if (instruction.Result is null)
                    return;
                var destination = GetWritableRegister(instruction.Result, GpScratch0);
                if (_allocation.Frame.HasVarArgsPointer)
                    LoadFromMemory(destination, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, signed: false);
                else
                    MoveRegister(destination, MachineRegister.X0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitVaArg(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 4)
                    throw Unsupported(instruction, "VaArg expects a va_list pointer, kind, size, and alignment.");
                if (instruction.Result is null)
                    return;

                var size = Math.Max(1, ImmediateToInt32(instruction.Operands[2]));
                var align = Math.Max(1, ImmediateToInt32(instruction.Operands[3]));
                var ap = LoadOperand(instruction.Operands[0], GpScratch0);
                LoadFromMemory(GpScratch1, ap, 0, _owner._target.PointerSize, signed: false);
                AlignPointerRegister(GpScratch1, align);
                var destination = GetWritableRegister(instruction.Result, GpScratch0);
                MoveRegister(destination, GpScratch1);
                AddImmediate(GpScratch1, GpScratch1, AlignUp(size, _owner._target.PointerSize));
                StoreRegister(GpScratch1, ap, 0, _owner._target.PointerSize);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void AlignPointerRegister(MachineRegister register, int alignment)
            {
                if (alignment <= 1)
                    return;
                AddImmediate(register, register, alignment - 1);
                LoadImmediate(GpScratch2, -alignment);
                Emit(RVInstruction.R(RVInstrKind.And, ToRegister(register), ToRegister(register), ToRegister(GpScratch2)));
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand.");
                var trueFallsThrough = IsFallthroughTarget(instruction.TrueTarget);
                var falseFallsThrough = IsFallthroughTarget(instruction.FalseTarget);
                MachineRegister cond;
                if (IsFloatType(instruction.Operands[0].Type))
                {
                    if (IsLongDouble(instruction.Operands[0].Type))
                        throw HelperRequired(instruction, SelectFloatingHelper("!", instruction.Operands[0].Type), "long double branch condition requires a runtime helper.");
                    RequireFloatingHardware(instruction.Operands[0].Type, instruction);
                    var fcond = LoadOperand(instruction.Operands[0], FpScratch0);
                    LoadFloatingImmediate(FpScratch1, 0.0, instruction.Operands[0].Type);
                    EmitFloatingCompare(IsFloat32(instruction.Operands[0].Type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, GpScratch0, fcond, FpScratch1);
                    EmitImm(RVInstrKind.Xori, GpScratch0, GpScratch0, 1);
                    cond = GpScratch0;
                }
                else if (RequiresStackBackedScalar(instruction.Operands[0].Type))
                {
                    cond = EmitStackBackedScalarNonZero(instruction.Operands[0], instruction);
                }
                else
                {
                    cond = LoadOperand(instruction.Operands[0], GpScratch0);
                }

                if (trueFallsThrough && !falseFallsThrough)
                {
                    EmitBranch(RVInstrKind.Beq, cond, MachineRegister.X0, LabelOf(instruction.FalseTarget));
                    return;
                }

                EmitBranch(RVInstrKind.Bne, cond, MachineRegister.X0, LabelOf(instruction.TrueTarget));
                if (!falseFallsThrough)
                    EmitJump(LabelOf(instruction.FalseTarget));
            }

            private void EmitStackBackedScalarIsZero(LirVirtualRegister destinationRegister, LirOperand operand, LirInstruction instruction)
            {
                var destination = GetWritableRegister(destinationRegister, GpScratch0);
                var nonZero = EmitStackBackedScalarNonZero(operand, instruction);
                EmitImm(RVInstrKind.Sltiu, destination, nonZero, 1);
                StoreWritableRegisterIfSpilled(destinationRegister, destination);
            }

            private MachineRegister EmitStackBackedScalarNonZero(LirOperand operand, LirInstruction instruction)
            {
                var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch1, instruction);
                MoveRegister(GpScratch0, MachineRegister.X0);
                var size = SizeOf(operand.Type);
                var registerSize = Math.Max(1, _owner._target.RegisterSize);
                for (var offset = 0; offset < size; offset += registerSize)
                {
                    var segmentSize = Math.Min(registerSize, size - offset);
                    LoadRawBitsFromMemory(GpScratch2, sourceAddress, offset, segmentSize);
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                }
                return GpScratch0;
            }

            private void EmitSwitch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Switch expects one key operand.");
                if (IsRv32WideInteger(instruction.Operands[0].Type))
                {
                    LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                    foreach (var @case in instruction.SwitchCases)
                    {
                        var next = _owner.CreateLocalLabel(_functionLabel + "_i64_switch_next");
                        var value = unchecked((ulong)ImmediateToInt64(@case.Value));
                        LoadImmediate(GpScratch2, unchecked((int)value));
                        LoadImmediate(GpScratch3, unchecked((int)(value >> 32)));
                        EmitBranch(RVInstrKind.Bne, GpScratch1, GpScratch3, next);
                        EmitBranch(RVInstrKind.Beq, GpScratch0, GpScratch2, LabelOf(@case.Target));
                        _owner._text.DefineLabel(next);
                    }

                    if (!IsFallthroughTarget(instruction.Target))
                        EmitJump(LabelOf(instruction.Target));
                    return;
                }
                if (RequiresStackBackedScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Operands[0].Type), "Switch on scalar wider than one machine register is not implemented yet.");

                var key = LoadOperand(instruction.Operands[0], GpScratch0);
                foreach (var @case in instruction.SwitchCases)
                {
                    LoadImmediate(GpScratch1, ImmediateToInt64(@case.Value));
                    EmitBranch(RVInstrKind.Beq, key, GpScratch1, LabelOf(@case.Target));
                }

                if (!IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0 || IsVoid(instruction.Operands[0].Type))
                {
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                var operand = instruction.Operands[0];
                if (IsAggregateType(operand.Type))
                {
                    var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: true, isVariadicUnnamedArgument: false);
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    if (value.PassingKind == AbiPassingKind.Indirect)
                    {
                        MaterializeIncomingHiddenReturnBufferAddress(GpScratch1);
                        CopyMemory(GpScratch1, GpScratch0, value.Size);
                        EmitEpilogue();
                        EmitReturnInstruction();
                        return;
                    }

                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), GpScratch0, segment.Offset, segment.Size);
                    }

                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
                var returnAbi = CAbi.ClassifyValue(_owner._target, returnType, isReturn: true, isVariadicUnnamedArgument: false);
                if (returnAbi.PassingKind == AbiPassingKind.Indirect)
                {
                    var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch0, instruction);
                    MaterializeIncomingHiddenReturnBufferAddress(GpScratch1);
                    CopyMemory(GpScratch1, sourceAddress, returnAbi.Size);
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                if (returnAbi.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch0, instruction);
                    for (var i = 0; i < returnAbi.Segments.Length; i++)
                    {
                        var segment = returnAbi.Segments[i];
                        LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), sourceAddress, segment.Offset, segment.Size);
                    }
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }
                LoadOperandIntoAs(operand, returnAbi.Segments.Length != 0 && returnAbi.Segments[0].RegisterClass == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10, returnType, instruction);
                EmitEpilogue();
                EmitReturnInstruction();
            }

            private void EmitEpilogue()
            {
                if (_hasCalls)
                    LoadFromMemory(Ra, Sp, _raSaveOffset, _owner._target.PointerSize, signed: false);
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderByDescending(static p => p.Value))
                    LoadFromMemory(pair.Key, Sp, pair.Value, RegisterSaveSize(pair.Key), signed: false);
                AdjustStack(_totalFrameSize);
            }

            private void EmitParallelCopy(LirInstruction instruction)
            {
                if (instruction.ParallelCopies.Length == 0)
                    return;

                var copies = instruction.ParallelCopies.Where(RequiresPhysicalParallelCopy).ToArray();
                if (copies.Length == 0)
                    return;

                if (copies.Length == 1)
                {
                    EmitDirectParallelCopy(copies[0], instruction);
                    return;
                }

                if (CanEmitDirectParallelCopies(copies))
                {
                    foreach (var copy in copies)
                        EmitDirectParallelCopy(copy, instruction);
                    return;
                }

                if (_allocation.Frame.ParallelCopyTempSize == 0)
                    throw Unsupported(instruction, "Parallel copy requires a temporary frame area.");

                var tempOffset = _allocation.Frame.ParallelCopyTempOffset;
                var tempCursor = 0;
                foreach (var copy in copies)
                {
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                    {
                        var sourceAddress = IsAggregateType(copy.Destination.Type)
                            ? MaterializeAnyStorageAddress(copy.Source, GpScratch0, instruction)
                            : MaterializeScalarStorageAddress(copy.Source, GpScratch0, instruction);
                        AddImmediate(GpScratch1, Sp, tempOffset + tempCursor);
                        CopyMemory(GpScratch1, sourceAddress, SizeOf(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOf(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                    else
                    {
                        var source = LoadOperand(copy.Source, PreferredScratch(copy.Source.Type, GpScratch0, FpScratch0, VecScratch0));
                        StoreToMemory(source, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                }

                tempCursor = 0;
                foreach (var copy in copies)
                {
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                    {
                        var dest = MaterializeVirtualRegisterStorageAddress(copy.Destination, GpScratch0);
                        AddImmediate(GpScratch1, Sp, tempOffset + tempCursor);
                        CopyMemory(dest, GpScratch1, SizeOf(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOf(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                    else
                    {
                        var destination = GetWritableRegister(copy.Destination, PreferredScratch(copy.Destination.Type, GpScratch0, FpScratch0, VecScratch0));
                        LoadFromMemory(destination, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type), IsSignedIntegerType(copy.Destination.Type));
                        NormalizeScalarRegister(destination, copy.Destination.Type);
                        StoreWritableRegisterIfSpilled(copy.Destination, destination);
                        tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                }
            }

            private bool RequiresPhysicalParallelCopy(LirParallelCopy copy)
            {
                if (copy.Destination.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                    return false;
                if (copy.Source.Kind is LirOperandKind.Void or LirOperandKind.None)
                    return false;
                if (copy.Source.Kind == LirOperandKind.Register && copy.Source.Register is { RegisterClass: LirRegisterClass.Void or LirRegisterClass.Memory })
                    return false;
                return !ReferencesSamePhysicalStorage(copy.Source, copy.Destination);
            }

            private bool CanEmitDirectParallelCopies(IReadOnlyList<LirParallelCopy> copies)
            {
                foreach (var copy in copies)
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                        return false;
                return !HasPhysicalStorageClobber(copies);
            }

            private bool HasPhysicalStorageClobber(IReadOnlyList<LirParallelCopy> copies)
            {
                for (var i = 0; i < copies.Count; i++)
                {
                    var destination = copies[i].Destination;
                    var hasDestinationRegister = TryGetPhysicalRegister(destination, out var destinationRegister);
                    var hasDestinationStackOffset = TryGetStackOffset(destination, out var destinationStackOffset);
                    if (!hasDestinationRegister && !hasDestinationStackOffset)
                        continue;

                    for (var j = 0; j < copies.Count; j++)
                    {
                        if (i == j && ReferencesSamePhysicalStorage(copies[j].Source, destination))
                            continue;

                        if (hasDestinationRegister && TryGetPhysicalRegister(copies[j].Source, out var sourceRegister) && sourceRegister == destinationRegister)
                            return true;
                        if (hasDestinationStackOffset && TryGetStackOffset(copies[j].Source, out var sourceStackOffset) && sourceStackOffset == destinationStackOffset)
                            return true;
                    }
                }

                return false;
            }

            private void EmitDirectParallelCopy(LirParallelCopy copy, LirInstruction instruction)
            {
                if (!RequiresPhysicalParallelCopy(copy))
                    return;
                if (IsAggregateType(copy.Destination.Type))
                {
                    EmitAggregateCopyToRegisterStorage(copy.Destination, copy.Source, instruction);
                    return;
                }

                if (RequiresStackBackedScalar(copy.Destination.Type))
                {
                    EmitStackBackedScalarCopy(copy.Destination, copy.Source, instruction);
                    return;
                }

                var destination = GetWritableRegister(copy.Destination, PreferredScratch(copy.Destination.Type, GpScratch0, FpScratch0, VecScratch0));
                LoadOperandIntoAs(copy.Source, destination, copy.Destination.Type, instruction);
                StoreWritableRegisterIfSpilled(copy.Destination, destination);
            }

            private bool TryGetPhysicalRegister(LirVirtualRegister register, out MachineRegister physicalRegister)
            {
                physicalRegister = default;
                if (!_allocation.TryGetAllocation(register, out var allocation) || allocation.IsSpilled)
                    return false;
                physicalRegister = allocation.PhysicalRegister;
                return true;
            }

            private bool TryGetPhysicalRegister(LirOperand operand, out MachineRegister physicalRegister)
            {
                physicalRegister = default;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;
                return TryGetPhysicalRegister(operand.Register, out physicalRegister);
            }

            private bool TryGetStackOffset(LirVirtualRegister register, out int stackOffset)
            {
                stackOffset = 0;
                if (!_allocation.TryGetAllocation(register, out var allocation) || !allocation.IsSpilled)
                    return false;
                stackOffset = allocation.StackOffset;
                return true;
            }

            private bool TryGetStackOffset(LirOperand operand, out int stackOffset)
            {
                stackOffset = 0;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;
                return TryGetStackOffset(operand.Register, out stackOffset);
            }

            private bool ReferencesSamePhysicalStorage(LirOperand source, LirVirtualRegister destination)
            {
                if (source.Kind != LirOperandKind.Register || source.Register is null)
                    return false;
                if (!_allocation.TryGetAllocation(source.Register, out var sourceAllocation) || !_allocation.TryGetAllocation(destination, out var destinationAllocation))
                    return false;
                if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
                    return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;
                if (sourceAllocation.IsSpilled && destinationAllocation.IsSpilled)
                    return sourceAllocation.StackOffset == destinationAllocation.StackOffset;
                return false;
            }

            private MachineRegister LoadOperandAs(LirOperand operand, QualifiedType targetType, MachineRegister scratch, LirInstruction instruction)
            {
                if (IsRiscVVectorType(targetType))
                {
                    if (!IsRiscVVectorType(operand.Type))
                        throw Unsupported(instruction, "Implicit scalar-to-vector load is not supported.");
                    return LoadOperand(operand, IsVectorRegister(scratch) ? scratch : VecScratch0);
                }

                if (IsRiscVVectorType(operand.Type))
                    throw Unsupported(instruction, "Implicit vector-to-scalar load is not supported.");

                if (IsFloatType(targetType))
                {
                    if (!UsesHardwareFloating(targetType) && !IsFloatRegister(scratch))
                    {
                        if (IsFloatType(operand.Type) && SameBuiltinFloatingType(operand.Type, targetType))
                            return LoadOperand(operand, scratch);
                        throw HelperRequired(instruction, SelectConversionHelper(operand.Type, targetType), "Software floating-point conversion requires a runtime helper.");
                    }
                    var destination = IsFloatRegister(scratch) ? scratch : FpScratch0;
                    return LoadOperandAsFloating(operand, targetType, destination, instruction);
                }

                if (IsFloatType(operand.Type))
                    throw Unsupported(instruction, "Implicit floating-point to non-floating load is not supported.");

                var source = LoadOperand(operand, scratch);
                if (!TypesNeedIntegerConversion(operand.Type, targetType))
                    return source;
                if (source != scratch)
                    MoveRegister(scratch, source);
                NormalizeIntegerRegister(scratch, targetType);
                return scratch;
            }

            private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
            {
                var scratch = IsRiscVVectorType(targetType)
                    ? (destination == VecScratch0 ? VecScratch1 : VecScratch0)
                    : IsFloatType(targetType) && (UsesHardwareFloating(targetType) || IsFloatRegister(destination))
                        ? (destination == FpScratch0 ? FpScratch1 : FpScratch0)
                        : (destination == GpScratch0 ? GpScratch1 : GpScratch0);
                var source = LoadOperandAs(operand, targetType, scratch, instruction);
                MoveRegister(destination, source);
                NormalizeScalarRegister(destination, targetType);
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
                        if (operand.Immediate is string text)
                        {
                            MaterializeSymbolAddress(_owner.CreateStringLiteral(text), preferred);
                            return preferred;
                        }
                        if (IsFloatType(operand.Type))
                        {
                            if (!UsesHardwareFloating(operand.Type) && !IsFloatRegister(preferred))
                            {
                                LoadFloatingImmediateBits(preferred, operand.Immediate, operand.Type);
                                return preferred;
                            }
                            var destination = IsFloatRegister(preferred) ? preferred : FpScratch0;
                            LoadFloatingImmediate(destination, operand.Immediate, operand.Type);
                            return destination;
                        }
                        LoadImmediate(preferred, ConvertIntegerConstant(operand.Immediate));
                        NormalizeIntegerRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        if (IsRiscVVectorType(operand.Type) && !IsVectorRegister(preferred))
                            preferred = VecScratch0;
                        else if (UsesHardwareFloating(operand.Type) && !IsFloatRegister(preferred))
                            preferred = FpScratch0;
                        LoadFromMemory(preferred, Sp, offset, SizeOfRegisterType(operand.Type), IsSignedIntegerType(operand.Type));
                        NormalizeScalarRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, preferred);
                        return preferred;
                    case LirOperandKind.Symbol:
                        if (operand.Symbol is null)
                            throw new InvalidOperationException("Symbol operand has no symbol.");
                        MaterializeSymbolAddress(_owner.GetSymbolLabel(operand.Symbol), preferred);
                        return preferred;
                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (IsRiscVVectorType(operand.Type))
                        {
                            if (!IsVectorRegister(preferred))
                                preferred = VecScratch0;
                            ZeroVectorRegister(preferred);
                        }
                        else if (IsFloatRegister(preferred) && IsFloatType(operand.Type))
                            LoadFloatingImmediate(preferred, 0.0, operand.Type);
                        else
                            MoveRegister(preferred, MachineRegister.X0);
                        return preferred;
                    default:
                        throw new NotSupportedException($"Cannot load LIR operand kind {operand.Kind} into a register.");
                }
            }

            private MachineRegister LoadVirtualRegister(LirVirtualRegister register, MachineRegister preferred)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException($"Virtual register {register.Name} cannot be loaded as a single scalar register.");
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;
                if (IsRiscVVectorType(register.Type) && !IsVectorRegister(preferred))
                    preferred = VecScratch0;
                else if (UsesHardwareFloating(register.Type) && !IsFloatRegister(preferred))
                    preferred = FpScratch0;
                LoadFromMemory(preferred, Sp, alloc.StackOffset, SizeOfRegisterType(register.Type), IsSignedIntegerType(register.Type));
                NormalizeScalarRegister(preferred, register.Type);
                return preferred;
            }

            private MachineRegister GetWritableRegister(LirVirtualRegister register, MachineRegister scratch)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException($"Virtual register {register.Name} must be accessed through its storage address.");
                var alloc = _allocation[register];
                if (alloc.IsSpilled && IsRiscVVectorType(register.Type) && !IsVectorRegister(scratch))
                    scratch = VecScratch0;
                else if (alloc.IsSpilled && UsesHardwareFloating(register.Type) && !IsFloatRegister(scratch))
                    scratch = FpScratch0;
                return alloc.IsSpilled ? scratch : alloc.PhysicalRegister;
            }

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, MachineRegister source)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException($"Virtual register {register.Name} must be stored with a block copy.");
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return;
                StoreToMemory(source, Sp, alloc.StackOffset, Math.Min(SizeOfRegisterType(register.Type), SizeOf(register.Type)));
            }

            private MachineRegister MaterializeVirtualRegisterStorageAddress(LirVirtualRegister register, MachineRegister destination)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    throw new NotSupportedException($"Virtual register {register.Name} must be stack-backed.");
                AddImmediate(destination, Sp, alloc.StackOffset);
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
                        AddImmediate(destination, Sp, offset);
                        return;
                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, destination);
                        return;
                    case LirOperandKind.Immediate:
                        if (operand.Immediate is string text)
                        {
                            MaterializeSymbolAddress(_owner.CreateStringLiteral(text), destination);
                            return;
                        }
                        break;
                }
                throw Unsupported(instruction, $"Cannot materialize storage address for operand kind {operand.Kind}.");
            }

            private MachineRegister MaterializeAnyStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                MaterializeOperandStorageAddress(operand, destination, instruction);
                return destination;
            }

            private MachineRegister MaterializeScalarStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                var size = SizeOf(operand.Type);
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        if (RequiresStackBackedScalar(operand.Type))
                            return MaterializeVirtualRegisterStorageAddress(operand.Register, destination);
                        break;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var stackOffset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        AddImmediate(destination, Sp, stackOffset);
                        return destination;
                    case LirOperandKind.Immediate:
                        if (size > _allocation.Frame.FloatingImmediateTempSize)
                            throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Immediate scalar storage materialization requires a runtime helper.");
                        AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                        StoreImmediateScalarToMemory(operand, destination, 0, size, instruction);
                        return destination;
                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (size > _allocation.Frame.FloatingImmediateTempSize)
                            throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Undefined scalar storage materialization requires a runtime helper.");
                        AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                        ZeroMemory(destination, size);
                        return destination;
                }

                if (size > _allocation.Frame.FloatingImmediateTempSize)
                    throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Scalar storage materialization requires a runtime helper.");

                var source = LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch2);
                AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                StoreToMemory(source, destination, 0, Math.Min(SizeOfRegisterType(operand.Type), size));
                return destination;
            }

            private void EmitStackBackedScalarCopy(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, GpScratch0);
                var size = SizeOf(destination.Type);
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    ZeroMemory(destinationAddress, size);
                    return;
                }

                if (source.Kind == LirOperandKind.Immediate)
                {
                    StoreImmediateScalarToMemory(source, destinationAddress, 0, size, instruction);
                    return;
                }

                if (SizeOf(source.Type) != size)
                    throw Unsupported(instruction, "Stack-backed scalar copy requires equal source and destination sizes.");

                var sourceAddress = MaterializeScalarStorageAddress(source, GpScratch1, instruction);
                CopyMemory(destinationAddress, sourceAddress, size);
            }

            private void StoreImmediateScalarToMemory(LirOperand operand, MachineRegister baseRegister, int offset, int size, LirInstruction instruction)
            {
                var bytes = GetImmediateScalarBytes(operand, size, instruction);
                for (var i = 0; i < size; i++)
                {
                    LoadImmediate(GpScratch2, i < bytes.Length ? bytes[i] : 0);
                    StoreToMemory(GpScratch2, baseRegister, checked(offset + i), 1);
                }
            }

            private byte[] GetImmediateScalarBytes(LirOperand operand, int size, LirInstruction instruction)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new InvalidOperationException("Expected immediate operand.");

                if (IsFloatType(operand.Type))
                {
                    if (IsFloat32(operand.Type))
                        return AdjustConstantBytes(BitConverter.GetBytes(Convert.ToSingle(operand.Immediate, CultureInfo.InvariantCulture)), size);
                    if (IsFloat64(operand.Type))
                        return AdjustConstantBytes(BitConverter.GetBytes(Convert.ToDouble(operand.Immediate, CultureInfo.InvariantCulture)), size);
                    throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "long double immediate materialization requires a runtime helper.");
                }

                return AdjustConstantBytes(BitConverter.GetBytes(ConvertIntegerConstant(operand.Immediate)), size);
            }

            private byte[] AdjustConstantBytes(byte[] bytes, int size)
            {
                size = Math.Max(1, size);
                var result = new byte[size];
                if (_owner._target.Endianness == TargetEndianness.Little)
                {
                    for (var i = 0; i < size && i < bytes.Length; i++)
                        result[i] = bytes[i];
                    return result;
                }

                var ordered = (byte[])bytes.Clone();
                Array.Reverse(ordered);
                for (var i = 0; i < size; i++)
                {
                    var source = ordered.Length - size + i;
                    result[i] = source >= 0 && source < ordered.Length ? ordered[source] : (byte)0;
                }
                return result;
            }

            private void EmitAggregateCopyToRegisterStorage(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, GpScratch0);
                var size = SizeOf(destination.Type);
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    ZeroMemory(destinationAddress, size);
                    return;
                }
                MaterializeOperandStorageAddress(source, GpScratch1, instruction);
                CopyMemory(destinationAddress, GpScratch1, size);
            }

            private void MaterializeIncomingHiddenReturnBufferAddress(MachineRegister destination)
            {
                if (_allocation.Frame.HasHiddenReturnBuffer)
                {
                    LoadFromMemory(destination, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize, signed: false);
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
                    LoadFromMemory(
                        destination,
                        Sp,
                        IncomingStackOffset(location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)),
                        _owner._target.PointerSize,
                        signed: false);
                    return;
                }

                throw new InvalidOperationException("Invalid hidden return buffer ABI location.");
            }

            private void MaterializeAddress(LirAddress address, MachineRegister destination)
            {
                var built = BuildAddress(address, destination, destination == GpScratch1 ? GpScratch2 : GpScratch1);
                if (built.Offset == 0 && built.BaseRegister == destination)
                    return;
                AddImmediate(destination, built.BaseRegister, built.Offset);
            }

            private AddressParts BuildAddress(LirAddress address, MachineRegister scratchBase, MachineRegister scratchIndex)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        if (address.StackSlot is null)
                            throw new InvalidOperationException("Stack slot address has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(address.StackSlot, out var slotOffset))
                            throw new InvalidOperationException($"Missing stack slot offset for {address.StackSlot.Name}.");
                        return new AddressParts(Sp, slotOffset);
                    case LirAddressKind.Symbol:
                        if (address.Symbol is null)
                            throw new InvalidOperationException("Symbol address has no symbol.");
                        MaterializeSymbolAddress(_owner.GetSymbolLabel(address.Symbol), scratchBase);
                        return new AddressParts(scratchBase, 0);
                    case LirAddressKind.Indirect:
                        if (address.BaseOperand is null)
                            throw new InvalidOperationException("Indirect address has no base operand.");
                        return new AddressParts(LoadOperand(address.BaseOperand, scratchBase), address.Displacement);
                    case LirAddressKind.Element:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Element address has no base address.");
                        var baseAddress = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        if (baseAddress.Offset != 0)
                            AddImmediate(scratchBase, baseAddress.BaseRegister, baseAddress.Offset);
                        else if (baseAddress.BaseRegister != scratchBase)
                            MoveRegister(scratchBase, baseAddress.BaseRegister);
                        if (address.Index is not null)
                        {
                            var index = LoadOperand(address.Index, scratchIndex);
                            MoveRegister(scratchIndex, index);
                            ScaleIndex(scratchIndex, address.Scale, scratchIndex == GpScratch3 ? GpScratch2 : GpScratch3);
                            Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(scratchBase), ToRegister(scratchBase), ToRegister(scratchIndex)));
                        }
                        return new AddressParts(scratchBase, address.Displacement);
                    case LirAddressKind.Field:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Field address has no base address.");
                        var fieldBase = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        return new AddressParts(fieldBase.BaseRegister, checked(fieldBase.Offset + address.Displacement));
                    default:
                        throw new NotSupportedException($"Unsupported LIR address kind {address.Kind}.");
                }
            }

            private void ScaleIndex(MachineRegister index, int scale, MachineRegister scratch)
            {
                if (scale <= 1)
                    return;
                if (IsPowerOfTwo(scale))
                {
                    EmitShiftImmediate(RVInstrKind.Slli, index, index, Log2(scale));
                    return;
                }
                if (!_owner._machineTarget.HasM)
                    throw new NotSupportedException("Non power-of-two pointer scale requires M extension.");
                LoadImmediate(scratch, scale);
                Emit(RVInstruction.R(RVInstrKind.Mul, ToRegister(index), ToRegister(index), ToRegister(scratch)));
            }

            private void DivideRegisterByScale(MachineRegister register, int scale, MachineRegister scratch)
            {
                if (scale <= 1)
                    return;
                if (IsPowerOfTwo(scale))
                {
                    EmitShiftImmediate(RVInstrKind.Srai, register, register, Log2(scale));
                    return;
                }
                if (!_owner._machineTarget.HasM)
                    throw new NotSupportedException("Non power-of-two pointer difference scale requires M extension.");
                LoadImmediate(scratch, scale);
                Emit(RVInstruction.R(RVInstrKind.Div, ToRegister(register), ToRegister(register), ToRegister(scratch)));
            }

            private void CopyMemory(MachineRegister destination, MachineRegister source, int size)
            {
                for (var i = 0; i < Math.Max(0, size); i++)
                {
                    LoadFromMemory(GpScratch2, source, i, 1, signed: false);
                    StoreToMemory(GpScratch2, destination, i, 1);
                }
            }

            private void ZeroMemory(MachineRegister destination, int size)
            {
                for (var i = 0; i < Math.Max(0, size); i++)
                    StoreToMemory(MachineRegister.X0, destination, i, 1);
            }

            private void LoadRawBitsFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size)
                => LoadFromMemory(destination, baseRegister, offset, RawStorageSize(size), signed: false);

            private void StoreRawBitsToAddress(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => StoreToMemory(source, baseRegister, offset, RawStorageSize(size));

            private static int RawStorageSize(int size)
            {
                if (size <= 1)
                    return 1;
                if (size <= 2)
                    return 2;
                if (size <= 4)
                    return 4;
                return 8;
            }


            private void StoreRegister(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => StoreToMemory(source, baseRegister, offset, size);

            private void LoadFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size, bool signed)
            {
                if (IsVectorRegister(destination))
                {
                    LoadVectorFromMemory(destination, baseRegister, offset, size);
                    return;
                }
                var opcode = IsFloatRegister(destination) ? FloatingLoadOpcode(size) : LoadOpcode(size, signed);
                EmitMemory(opcode, destination, baseRegister, offset, isStore: false);
            }

            private void StoreToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
            {
                if (IsVectorRegister(source))
                {
                    StoreVectorToMemory(source, baseRegister, offset, size);
                    return;
                }
                var opcode = IsFloatRegister(source) ? FloatingStoreOpcode(size) : StoreOpcode(size);
                EmitMemory(opcode, source, baseRegister, offset, isStore: true);
            }

            private void LoadVectorFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size)
            {
                if (size != TargetRegisterInfo.VectorRegisterSize(_owner._target))
                    throw new NotSupportedException("Vector spills require one complete vector register.");
                var address = baseRegister;
                if (offset != 0)
                {
                    AddImmediate(GpScratch3, baseRegister, offset);
                    address = GpScratch3;
                }
                EmitVectorMaxConfiguration(8);
                Emit(RVInstruction.Vl(RVInstrKind.Vle8V, ToVectorRegister(destination), ToRegister(address)));
            }

            private void StoreVectorToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
            {
                if (size != TargetRegisterInfo.VectorRegisterSize(_owner._target))
                    throw new NotSupportedException("Vector spills require one complete vector register.");
                var address = baseRegister;
                if (offset != 0)
                {
                    AddImmediate(GpScratch3, baseRegister, offset);
                    address = GpScratch3;
                }
                EmitVectorMaxConfiguration(8);
                Emit(RVInstruction.Vs(RVInstrKind.Vse8V, ToVectorRegister(source), ToRegister(address)));
            }

            private void EmitMemory(RVInstrKind opcode, MachineRegister valueRegister, MachineRegister baseRegister, int offset, bool isStore)
            {
                if (FitsSignedImmediate(offset, 12))
                {
                    if (isStore)
                        Emit(RVInstruction.S(opcode, ToAnyRegister(valueRegister), ToRegister(baseRegister), offset));
                    else
                        Emit(RVInstruction.I(opcode, ToAnyRegister(valueRegister), ToRegister(baseRegister), offset));
                    return;
                }

                AddImmediate(GpScratch3, baseRegister, offset);
                if (isStore)
                    Emit(RVInstruction.S(opcode, ToAnyRegister(valueRegister), ToRegister(GpScratch3), 0));
                else
                    Emit(RVInstruction.I(opcode, ToAnyRegister(valueRegister), ToRegister(GpScratch3), 0));
            }

            private RVInstrKind LoadOpcode(int size, bool signed)
            {
                return size switch
                {
                    1 => signed ? RVInstrKind.Lb : RVInstrKind.Lbu,
                    2 => signed ? RVInstrKind.Lh : RVInstrKind.Lhu,
                    4 => !signed && _owner._target.Is64Bit ? RVInstrKind.Lwu : RVInstrKind.Lw,
                    8 when _owner._target.Is64Bit => RVInstrKind.Ld,
                    _ => throw new NotSupportedException($"Unsupported store size {size}."),
                };
            }

            private RVInstrKind StoreOpcode(int size)
            {
                return size switch
                {
                    1 => RVInstrKind.Sb,
                    2 => RVInstrKind.Sh,
                    4 => RVInstrKind.Sw,
                    8 when _owner._target.Is64Bit => RVInstrKind.Sd,
                    _ => throw new NotSupportedException($"Unsupported store size {size}."),
                };
            }

            private RVInstrKind FloatingLoadOpcode(int size)
            {
                return size switch
                {
                    4 when _owner._machineTarget.HasF => RVInstrKind.Flw,
                    8 when _owner._machineTarget.HasD => RVInstrKind.Fld,
                    _ => throw new NotSupportedException($"Unsupported floating-pointstore size {size}."),
                };
            }

            private RVInstrKind FloatingStoreOpcode(int size)
            {
                return size switch
                {
                    4 when _owner._machineTarget.HasF => RVInstrKind.Fsw,
                    8 when _owner._machineTarget.HasD => RVInstrKind.Fsd,
                    _ => throw new NotSupportedException($"Unsupported floating-pointstore size {size}."),
                };
            }

            private void LoadFloatingImmediateBits(MachineRegister destination, object? value, QualifiedType type)
            {
                if (IsFloat32(type))
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    LoadImmediate(destination, BitConverter.ToInt32(raw, 0));
                    return;
                }

                if (IsFloat64(type) && _owner._target.Is64Bit)
                {
                    var raw = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    LoadImmediate(destination, BitConverter.ToInt64(raw, 0));
                    return;
                }

                throw new NotSupportedException("Floating-point immediate requires a runtime helper.");
            }

            private void LoadFloatingImmediate(MachineRegister destination, object? value, QualifiedType type)
            {
                RequireFloatingHardware(type, null);
                var size = IsFloat32(type) ? 4 : IsFloat64(type) ? 8 : throw new NotSupportedException("long double immediate requires a runtime helper.");
                var label = _owner.CreateLocalLabel(size == 4 ? "f32" : "f64");
                var offset = _owner._rodata.Align(size);
                _owner._rodata.DefineSymbol(label, offset, size, RVObjectSymbolBinding.Local, _owner._symbols);
                if (size == 4)
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    _owner._rodata.EmitBytes(raw, 4);
                }
                else
                {
                    var raw = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    _owner._rodata.EmitBytes(raw, 8);
                }
                MaterializeSymbolAddress(label, GpScratch3);
                LoadFromMemory(destination, GpScratch3, 0, size, signed: false);
            }

            private void EmitFloatingR(RVInstrKind opcode, MachineRegister destination, MachineRegister left, MachineRegister right)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToFloatRegister(left), ToFloatRegister(right)));

            private void EmitFloatingCompare(RVInstrKind opcode, MachineRegister destination, MachineRegister left, MachineRegister right)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(left), ToFloatRegister(right)));

            private void EmitFloatingConvertFromInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToRegister(source), RVRegister.X0));

            private void EmitFloatingConvertToInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingConvert(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingMoveToInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingMoveFromInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToRegister(source), RVRegister.X0));

            private void RequireFloatingHardware(QualifiedType type, LirInstruction? instruction)
            {
                if (IsFloat32(type) && _owner._machineTarget.HasF)
                    return;
                if (IsFloat64(type) && _owner._machineTarget.HasD)
                    return;
                var message = "Floating-point operation requires a hardware floating-point extension or a runtime helper.";
                if (instruction is null)
                    throw new NotSupportedException(message);
                throw HelperRequired(instruction, SelectFloatingHelper(string.Empty, type), message);
            }

            private void LoadImmediate(MachineRegister destination, long value)
            {
                if (value == 0)
                {
                    MoveRegister(destination, MachineRegister.X0);
                    return;
                }

                if (FitsSignedImmediate(value, 12))
                {
                    EmitImm(RVInstrKind.Addi, destination, MachineRegister.X0, (int)value);
                    return;
                }

                if (value >= int.MinValue && value <= int.MaxValue)
                {
                    var hi = (int)((value + 0x800L) >> 12);
                    var lo = (int)(value - ((long)hi << 12));
                    Emit(RVInstruction.U(RVInstrKind.Lui, ToRegister(destination), hi));
                    if (lo != 0)
                        EmitImm(RVInstrKind.Addi, destination, destination, lo);
                    return;
                }

                if (!_owner._target.Is64Bit)
                    throw new OverflowException("Immediate does not fit RV32 register.");

                var label = _owner.CreateLocalLabel("i64");
                var offset = _owner._rodata.Align(8);
                _owner._rodata.DefineSymbol(label, offset, 8, RVObjectSymbolBinding.Local, _owner._symbols);
                _owner._rodata.EmitInteger(value, 8, _owner._target.Endianness);
                MaterializeSymbolAddress(label, destination);
                LoadFromMemory(destination, destination, 0, 8, signed: false);
            }

            private void MaterializeSymbolAddress(string symbol, MachineRegister destination)
            {
                var hiOffset = _owner._text.ByteLength;
                var auipc = new RVInstruction(RVInstrKind.Auipc, ToRegister(destination), symbol: symbol, relocationKind: RVRelocationKind.AbsoluteUpper20);
                Emit(auipc);
                _owner._text.AddRelocation(hiOffset, symbol, 0, RVObjectRelocationKind.PcrelHi20);
                var loOffset = _owner._text.ByteLength;
                var addi = new RVInstruction(RVInstrKind.Addi, ToRegister(destination), ToRegister(destination), RVRegister.Invalid, 0, symbol, RVRelocationKind.AbsoluteLow12);
                Emit(addi);
                _owner._text.AddRelocation(loOffset, symbol, 0, RVObjectRelocationKind.PcrelLo12I);
            }

            private void AddImmediate(MachineRegister destination, MachineRegister source, int immediate)
            {
                if (immediate == 0)
                {
                    MoveRegister(destination, source);
                    return;
                }

                if (FitsSignedImmediate(immediate, 12))
                {
                    EmitImm(RVInstrKind.Addi, destination, source, immediate);
                    return;
                }

                LoadImmediate(destination, immediate);
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(destination), ToRegister(source), ToRegister(destination)));
            }

            private void AdjustStack(int delta)
            {
                if (delta == 0)
                    return;
                if (FitsSignedImmediate(delta, 12))
                {
                    EmitImm(RVInstrKind.Addi, Sp, Sp, delta);
                    return;
                }

                LoadImmediate(GpScratch0, Math.Abs((long)delta));
                Emit(RVInstruction.R(delta < 0 ? RVInstrKind.Sub : RVInstrKind.Add, ToRegister(Sp), ToRegister(Sp), ToRegister(GpScratch0)));
            }

            private MachineRegister PreferredScratch(QualifiedType type, MachineRegister general, MachineRegister floating, MachineRegister vector)
            {
                if (IsRiscVVectorType(type))
                    return vector;
                return UsesHardwareFloating(type) ? floating : general;
            }


            private void MoveVectorRegister(MachineRegister destination, MachineRegister source)
            {
                if (destination == source)
                    return;
                EmitVectorMaxConfiguration(8);
                Emit(RVInstruction.Vx(RVInstrKind.VaddVx, ToVectorRegister(destination), ToVectorRegister(source), RVRegister.X0));
            }

            private void ZeroVectorRegister(MachineRegister destination)
            {
                EmitVectorMaxConfiguration(8);
                Emit(RVInstruction.Vv(RVInstrKind.VxorVv, ToVectorRegister(destination), ToVectorRegister(destination), ToVectorRegister(destination)));
            }

            private void MoveRegister(MachineRegister destination, MachineRegister source)
            {
                if (destination == source)
                    return;
                if (IsVectorRegister(destination) || IsVectorRegister(source))
                {
                    if (!IsVectorRegister(destination) || !IsVectorRegister(source))
                        throw new NotSupportedException("Cross-class register move requires an explicit conversion or bitcast.");
                    MoveVectorRegister(destination, source);
                    return;
                }
                if (IsFloatRegister(destination) || IsFloatRegister(source))
                {
                    if (!IsFloatRegister(destination) || !IsFloatRegister(source))
                        throw new NotSupportedException("Cross-class register move requires an explicit conversion or bitcast.");
                    EmitFloatingR(_owner._machineTarget.HasD ? RVInstrKind.FsgnjD : RVInstrKind.FsgnjS, destination, source, source);
                    return;
                }
                EmitImm(RVInstrKind.Addi, destination, source, 0);
            }

            private void EmitImm(RVInstrKind opcode, MachineRegister destination, MachineRegister source, int immediate)
            {
                if (!FitsSignedImmediate(immediate, 12))
                    throw new ArgumentOutOfRangeException(nameof(immediate));
                Emit(RVInstruction.I(opcode, ToRegister(destination), ToRegister(source), immediate));
            }

            private void EmitShiftImmediate(RVInstrKind opcode, MachineRegister destination, MachineRegister source, int amount)
                => Emit(new RVInstruction(opcode, ToRegister(destination), ToRegister(source), RVRegister.Invalid, amount));

            private void NormalizeScalarRegister(MachineRegister register, QualifiedType type)
            {
                if (IsRiscVVectorType(type) || IsFloatType(type))
                    return;
                NormalizeIntegerRegister(register, type);
            }

            private void NormalizeIntegerRegister(MachineRegister register, QualifiedType type)
            {
                if (!IsIntegerLike(type) && !IsPointerLike(type))
                    return;
                if (IsPointerLike(type))
                {
                    if (_owner._target.Is32Bit)
                        EmitShiftPair(register, 0, unsignedShift: true);
                    return;
                }

                var size = SizeOf(type);
                if (_owner._target.Is64Bit)
                {
                    if (size >= 8)
                        return;
                    if (size == 4)
                    {
                        if (IsUnsignedIntegerType(type))
                        {
                            EmitShiftImmediate(RVInstrKind.Slli, register, register, 32);
                            EmitShiftImmediate(RVInstrKind.Srli, register, register, 32);
                        }
                        else
                        {
                            EmitImm(RVInstrKind.Addiw, register, register, 0);
                        }
                        return;
                    }

                    var shift = 64 - size * 8;
                    EmitShiftImmediate(RVInstrKind.Slli, register, register, shift);
                    EmitShiftImmediate(IsUnsignedIntegerType(type) ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift);
                    return;
                }

                if (size >= 4)
                    return;
                var shift32 = 32 - size * 8;
                EmitShiftImmediate(RVInstrKind.Slli, register, register, shift32);
                EmitShiftImmediate(IsUnsignedIntegerType(type) ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift32);
            }

            private void EmitShiftPair(MachineRegister register, int shift, bool unsignedShift)
            {
                if (shift == 0)
                    return;
                EmitShiftImmediate(RVInstrKind.Slli, register, register, shift);
                EmitShiftImmediate(unsignedShift ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift);
            }

            private void EmitCall(string label)
            {
                var offset = _owner._text.ByteLength;
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X1, label));
                _owner._text.AddRelocation(offset, label, 0, RVObjectRelocationKind.Jal20);
            }

            private void EmitJump(string label)
            {
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, label));
            }

            private void EmitBranch(RVInstrKind opcode, MachineRegister left, MachineRegister right, string label)
            {
                Emit(RVInstruction.B(opcode, ToRegister(left), ToRegister(right), label));
            }

            private void EmitReturnInstruction()
                => Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

            private void EmitNop()
                => Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X0, RVRegister.X0, 0));

            private void Emit(RVInstruction instruction)
                => _owner._text.Emit(instruction);

            private string LabelOf(LirBlock? block)
            {
                if (block is null || !_labels.TryGetValue(block, out var label))
                    throw new InvalidOperationException("Missing label for LIR block.");
                return label;
            }

            private bool IsFallthroughTarget(LirBlock? target)
                => false;

            private int IncomingStackOffset(int abiStackOffset)
                => checked(_totalFrameSize + abiStackOffset);

            private int SizeOf(QualifiedType type)
                => Math.Max(1, _owner._target.SizeOf(type));

            private int SizeOfRegisterType(QualifiedType type)
            {
                if (IsRiscVVectorType(type))
                    return TargetRegisterInfo.VectorRegisterSize(_owner._target);
                if (IsPointerLike(type))
                    return _owner._target.PointerSize;
                if (IsFloatType(type))
                    return Math.Max(1, SizeOf(type));
                return Math.Min(Math.Max(1, SizeOf(type)), _owner._target.RegisterSize);
            }

            private int PointerScale(QualifiedType pointerType)
            {
                if (pointerType.Type is PointerType pointer)
                    return Math.Max(1, _owner._target.SizeOf(pointer.PointeeType));
                if (pointerType.Type is ArrayType array)
                    return Math.Max(1, _owner._target.SizeOf(array.ElementType));
                return 1;
            }

            private int RegisterSaveSize(MachineRegister register)
            {
                if (IsVectorRegister(register))
                    return TargetRegisterInfo.VectorRegisterSize(_owner._target);
                if (IsFloatRegister(register))
                    return Math.Max(4, CAbi.RiscVAbiFloatingRegisterSize(_owner._target));
                return Math.Max(1, _owner._target.RegisterSize);
            }

            private static bool TypesNeedIntegerConversion(QualifiedType source, QualifiedType destination)
                => (IsIntegerLike(source) || IsPointerLike(source)) && (IsIntegerLike(destination) || IsPointerLike(destination));

            private bool RequiresSoftwareScalar(QualifiedType type)
            {
                if (IsAggregateType(type) || IsPointerLike(type) || IsRiscVVectorType(type))
                    return false;
                if (IsFloatType(type))
                    return !UsesHardwareFloating(type) && SizeOf(type) > _owner._target.RegisterSize;
                return SizeOf(type) > _owner._target.RegisterSize;
            }

            private bool RequiresStackBackedScalar(QualifiedType type)
                => !IsAggregateType(type) && !IsPointerLike(type) && !IsRiscVVectorType(type) && !UsesHardwareFloating(type) && SizeOf(type) > _owner._target.RegisterSize;

            private bool RequiresBlockCopyStorage(QualifiedType type)
                => IsAggregateType(type) || RequiresStackBackedScalar(type);

            private bool UsesHardwareFloating(QualifiedType type)
                => CAbi.UsesHardwareFloatingRegister(_owner._target, type, isVariadicUnnamedArgument: false);

            private NotImplementedException HelperRequired(LirInstruction instruction, RiscVRuntimeHelperKind helper, string message)
                => new NotImplementedException(
                    $"{message} Required helper: {helper}. Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");

            private RiscVRuntimeHelperKind SelectScalarMoveHelper(QualifiedType type)
            {
                if (_owner._target.Is32Bit && IsIntegerLike(type) && SizeOf(type) == 8)
                    return IsUnsignedIntegerType(type) ? RiscVRuntimeHelperKind.UInt64Move : RiscVRuntimeHelperKind.Int64Move;
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private RiscVRuntimeHelperKind SelectConversionHelper(QualifiedType source, QualifiedType destination)
            {
                if (IsLongDouble(source) || IsLongDouble(destination))
                    return RiscVRuntimeHelperKind.LongDoubleConvert;
                if (_owner._target.Is32Bit && ((IsIntegerLike(source) && SizeOf(source) == 8) || (IsIntegerLike(destination) && SizeOf(destination) == 8)))
                    return RiscVRuntimeHelperKind.Int64Convert;
                if (IsFloatType(source) || IsFloatType(destination))
                    return RiscVRuntimeHelperKind.SoftFloatConvert;
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private RiscVRuntimeHelperKind SelectFloatingHelper(string op, QualifiedType type)
            {
                if (IsLongDouble(type))
                    return RiscVRuntimeHelperKind.LongDoubleArithmetic;
                if (IsFloat32(type))
                    return op switch
                    {
                        "+" => RiscVRuntimeHelperKind.Float32Add,
                        "-" => RiscVRuntimeHelperKind.Float32Sub,
                        "*" => RiscVRuntimeHelperKind.Float32Mul,
                        "/" => RiscVRuntimeHelperKind.Float32Div,
                        _ => RiscVRuntimeHelperKind.SoftFloatOperation,
                    };
                if (IsFloat64(type))
                    return op switch
                    {
                        "+" => RiscVRuntimeHelperKind.Float64Add,
                        "-" => RiscVRuntimeHelperKind.Float64Sub,
                        "*" => RiscVRuntimeHelperKind.Float64Mul,
                        "/" => RiscVRuntimeHelperKind.Float64Div,
                        _ => RiscVRuntimeHelperKind.SoftFloatOperation,
                    };
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private static bool SameBuiltinFloatingType(QualifiedType left, QualifiedType right)
                => (IsFloat32(left) && IsFloat32(right)) || (IsFloat64(left) && IsFloat64(right)) || (IsLongDouble(left) && IsLongDouble(right));

            private static bool FitsSignedImmediate(long value, int bits)
            {
                var min = -(1L << (bits - 1));
                var max = (1L << (bits - 1)) - 1;
                return value >= min && value <= max;
            }

            private static bool IsPowerOfTwo(int value)
                => value > 0 && (value & (value - 1)) == 0;

            private static int Log2(int value)
            {
                var result = 0;
                while (value > 1)
                {
                    result++;
                    value >>= 1;
                }
                return result;
            }

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            private static RVRegister ToAnyRegister(MachineRegister register)
            {
                if (register >= MachineRegister.X0 && register <= MachineRegister.X31)
                    return (RVRegister)(int)register;
                if (register >= MachineRegister.F0 && register <= MachineRegister.F31)
                    return (RVRegister)(int)register;
                if (register >= MachineRegister.V0 && register <= MachineRegister.V31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Unsupported machine register for instruction emission.");
            }

            private static RVRegister ToRegister(MachineRegister register)
            {
                if (register >= MachineRegister.X0 && register <= MachineRegister.X31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Expected an integer register.");
            }

            private static RVRegister ToFloatRegister(MachineRegister register)
            {
                if (register >= MachineRegister.F0 && register <= MachineRegister.F31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Expected a floating-point register.");
            }

            private static RVRegister ToVectorRegister(MachineRegister register)
            {
                if (register >= MachineRegister.V0 && register <= MachineRegister.V31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Expected a vector register.");
            }

            private static bool IsFloatRegister(MachineRegister register)
                => register >= MachineRegister.F0 && register <= MachineRegister.F31;

            private static bool IsVectorRegister(MachineRegister register)
                => register >= MachineRegister.V0 && register <= MachineRegister.V31;

            private static int ImmediateToInt32(LirOperand operand)
            {
                var value = ImmediateToInt64(operand);
                if (value < int.MinValue || value > int.MaxValue)
                    throw new OverflowException("Immediate does not fit Int32.");
                return (int)value;
            }

            private static long ImmediateToInt64(LirOperand operand)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new InvalidOperationException("Expected immediate operand.");
                return ConvertIntegerConstant(operand.Immediate);
            }

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException($"{message} Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");

            private enum RiscVRuntimeHelperKind
            {
                Unsupported,
                Int64Move,
                UInt64Move,
                Int64Convert,
                SoftFloatOperation,
                SoftFloatConvert,
                Float32Add,
                Float32Sub,
                Float32Mul,
                Float32Div,
                Float64Add,
                Float64Sub,
                Float64Mul,
                Float64Div,
                LongDoubleArithmetic,
                LongDoubleConvert,
            }

            private readonly struct AddressParts
            {
                public MachineRegister BaseRegister { get; }
                public int Offset { get; }

                public AddressParts(MachineRegister baseRegister, int offset)
                {
                    BaseRegister = baseRegister;
                    Offset = offset;
                }
            }
        }

        private sealed class TextSectionBuilder
        {
            private readonly List<RVInstruction> _instructions = new List<RVInstruction>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<RVObjectRelocation> _relocations = new List<RVObjectRelocation>();

            public string Name { get; }
            public string CurrentLabel { get; private set; } = string.Empty;
            public int ByteLength => checked(_instructions.Count * 4);

            public TextSectionBuilder(string name)
            {
                Name = name;
            }

            public void DefineLabel(string label)
            {
                if (!_labels.ContainsKey(label))
                    _labels.Add(label, ByteLength);
                CurrentLabel = label;
            }

            public void Emit(RVInstruction instruction)
            {
                _instructions.Add(instruction);
                CurrentLabel = string.Empty;
            }

            public void EmitAssembly(string text, string labelPrefix, RVTarget target)
            {
                var program = RiscVAssembler.Assemble(text ?? string.Empty, target);
                var renamedLabels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var label in program.Text.Labels.Keys)
                    renamedLabels[label] = $"{labelPrefix}_{SanitizeSymbolName(label)}";

                var labelsByOffset = new Dictionary<int, List<string>>();
                foreach (var pair in program.Text.Labels)
                {
                    if (!labelsByOffset.TryGetValue(pair.Value, out var labels))
                    {
                        labels = new List<string>();
                        labelsByOffset.Add(pair.Value, labels);
                    }

                    labels.Add(renamedLabels[pair.Key]);
                }

                var offset = 0;
                foreach (var instruction in program.Text.Instructions)
                {
                    DefineInlineLabels(labelsByOffset, offset);
                    Emit(RewriteInlineLabelReference(instruction, renamedLabels));
                    offset = checked(offset + 4);
                }

                DefineInlineLabels(labelsByOffset, offset);
            }

            private void DefineInlineLabels(Dictionary<int, List<string>> labelsByOffset, int offset)
            {
                if (!labelsByOffset.TryGetValue(offset, out var labels))
                    return;

                foreach (var label in labels)
                    DefineLabel(label);
            }

            private static RVInstruction RewriteInlineLabelReference(
                RVInstruction instruction,
                IReadOnlyDictionary<string, string> renamedLabels)
            {
                return instruction.Symbol is not null && renamedLabels.TryGetValue(instruction.Symbol, out var replacement)
                    ? instruction.WithSymbol(replacement, instruction.RelocationKind)
                    : instruction;
            }

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
                var aligned = AlignUp(ByteLength, alignment);
                EmitZero(aligned - ByteLength);
                return ByteLength;
            }

            public void DefineSymbol(string name, int offset, int size, RVObjectSymbolBinding binding, List<RVObjectSymbol> symbols)
                => symbols.Add(new RVObjectSymbol(name, Name, offset, size, binding, RVObjectSymbolKind.Object));

            public void AddRelocation(int offset, string symbol, int addend, RVObjectRelocationKind kind)
                => _relocations.Add(new RVObjectRelocation(Name, offset, symbol, addend, kind));

            public void EmitByte(byte value)
                => _data.Add(value);

            public void EmitBytes(byte[] bytes, int count)
            {
                for (var i = 0; i < count && i < bytes.Length; i++)
                    _data.Add(bytes[i]);
            }

            public void EmitZero(int count)
            {
                for (var i = 0; i < count; i++)
                    _data.Add(0);
            }

            public void EmitInteger(long value, int size, TargetEndianness endianness)
            {
                var bytes = BitConverter.GetBytes(value);
                if (endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                if (endianness == TargetEndianness.Little)
                {
                    for (var i = 0; i < size; i++)
                        _data.Add(i < bytes.Length ? bytes[i] : (byte)0);
                }
                else
                {
                    for (var i = bytes.Length - size; i < bytes.Length; i++)
                        _data.Add(i >= 0 ? bytes[i] : (byte)0);
                }
            }

            public RVDataSection ToSection()
                => new RVDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }
        }

        private sealed class BssSectionBuilder
        {
            public string Name { get; }
            public int ByteLength { get; private set; }
            public int Alignment { get; private set; } = 1;

            public BssSectionBuilder(string name)
            {
                Name = name;
            }

            public int Allocate(int size, int alignment)
            {
                alignment = Math.Max(1, alignment);
                Alignment = Math.Max(Alignment, alignment);
                ByteLength = AlignUp(ByteLength, alignment);
                var offset = ByteLength;
                ByteLength = checked(ByteLength + Math.Max(0, size));
                return offset;
            }

            public void DefineSymbol(
                string name,
                int offset,
                int size,
                RVObjectSymbolBinding binding,
                List<RVObjectSymbol> symbols,
                bool isTentative = false)
                => symbols.Add(new RVObjectSymbol(name, Name, offset, size, binding, RVObjectSymbolKind.Object, isTentative));

            public RVDataSection ToSection()
                => new RVDataSection(Name, RVObjectSectionKind.Bss, Alignment, ImmutableArray<byte>.Empty, ByteLength, ImmutableArray<RVObjectRelocation>.Empty);

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }
        }
    }
}
