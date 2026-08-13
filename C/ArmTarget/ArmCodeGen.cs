using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Cnidaria.Arm;

namespace Cnidaria.C
{
    public sealed class ArmCodeGeneratorOptions
    {
        public static ArmCodeGeneratorOptions Default => new ArmCodeGeneratorOptions();

        public bool EmitStartup { get; set; } = true;
        public string EntryFunctionName { get; set; } = "main";
    }

    public sealed class ArmCodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string StringSectionName = ".rodata.str1.1";
        private const string DataSectionName = ".data";
        private const string BssSectionName = ".bss";

        private readonly LirModule _module;
        private readonly FileScopeLinkageMap _fileScopeLinkage;
        private readonly TargetInfo _target;
        private readonly ArmTarget _machineTarget;
        private readonly LSRAOptions _allocationOptions;
        private readonly ArmCodeGeneratorOptions _options;
        private readonly Dictionary<FunctionSymbol, string> _functionLabels = new Dictionary<FunctionSymbol, string>();
        private readonly Dictionary<string, string> _functionLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Symbol, string> _dataLabels = new Dictionary<Symbol, string>();
        private readonly Dictionary<string, string> _dataLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _stringLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _externalLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ArmObjectSymbol> _symbols = new List<ArmObjectSymbol>();
        private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, ArmObjectSectionKind.Rodata);
        private readonly DataSectionBuilder _strings = new DataSectionBuilder(StringSectionName, ArmObjectSectionKind.Rodata);
        private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, ArmObjectSectionKind.Data);
        private readonly BssSectionBuilder _bss = new BssSectionBuilder(BssSectionName);
        private TextSectionBuilder _text = null!;
        private int _nextLocalId;

        private ArmCodeGenerator(LirModule module, LSRAOptions? allocationOptions, ArmCodeGeneratorOptions? options)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _fileScopeLinkage = FileScopeLinkageMap.Create(_module.SemanticModel);
            _target = module.SemanticModel.Compilation.Options.Target;
            if (_target.Architecture is not TargetArchitectureKind.Arm32 and not TargetArchitectureKind.Arm64)
                throw new NotSupportedException("ARM C backend requires Arm32 or Arm64 target.");
            _machineTarget = ArmTarget.FromTargetInfo(_target);
            _allocationOptions = ReserveCodeGenScratchRegisters(_target, allocationOptions ?? LSRAOptions.ForTarget(_target));
            _options = options ?? ArmCodeGeneratorOptions.Default;
        }

        private static LSRAOptions ReserveCodeGenScratchRegisters(TargetInfo target, LSRAOptions options)
        {
            var reservedGeneral = ImmutableHashSet.CreateBuilder<MachineRegister>();
            var reservedVector = ImmutableHashSet.CreateBuilder<MachineRegister>();
            if (target.Architecture == TargetArchitectureKind.Arm64)
            {
                reservedGeneral.UnionWith(new[]
                {
                    MachineRegister.X8,
                    MachineRegister.X9,
                    MachineRegister.X10,
                    MachineRegister.X11,
                    MachineRegister.X12,
                });
                reservedVector.UnionWith(new[]
                {
                    MachineRegister.V29,
                    MachineRegister.V30,
                    MachineRegister.V31,
                });
            }
            else if (target.OperatingSystem == OperatingSystemKind.Windows)
            {
                reservedGeneral.UnionWith(new[]
                {
                    MachineRegister.X7,
                    MachineRegister.X8,
                    MachineRegister.X10,
                    MachineRegister.X11,
                    MachineRegister.X12,
                });
            }
            else
            {
                reservedGeneral.UnionWith(new[]
                {
                    MachineRegister.X8,
                    MachineRegister.X9,
                    MachineRegister.X10,
                    MachineRegister.X11,
                    MachineRegister.X12,
                });
            }

            return new LSRAOptions(
                generalRegisters: options.GeneralRegisters.Where(r => !reservedGeneral.Contains(r)).ToImmutableArray(),
                floatingRegisters: ImmutableArray<MachineRegister>.Empty,
                vectorRegisters: target.Architecture == TargetArchitectureKind.Arm64
                    ? options.VectorRegisters.Where(r => !reservedVector.Contains(r)).ToImmutableArray()
                    : ImmutableArray<MachineRegister>.Empty,
                callBoundarySplitClasses: target.Architecture == TargetArchitectureKind.Arm64
                    ? ImmutableArray.Create(LirRegisterClass.General, LirRegisterClass.Address, LirRegisterClass.Vector)
                    : ImmutableArray.Create(LirRegisterClass.General, LirRegisterClass.Address),
                stackAlignment: options.StackAlignment,
                spillSlotSize: options.SpillSlotSize,
                spillSlotAlignment: options.SpillSlotAlignment,
                stackArgumentSlotSize: options.StackArgumentSlotSize);
        }

        public static ArmProgram Generate(
            LirModule module,
            LSRAOptions? allocationOptions = null,
            ArmCodeGeneratorOptions? options = null)
            => new ArmCodeGenerator(module, allocationOptions, options).Generate();

        private ArmProgram Generate()
        {
            _text = new TextSectionBuilder(TextSectionName);
            IndexFunctions();
            EmitGlobalStorage();
            foreach (var function in _module.Functions)
                EmitFunction(function);

            var selectedEntry = _functionLabelsByName.TryGetValue(_options.EntryFunctionName, out var requestedEntry)
                ? requestedEntry
                : (_functionLabels.Values.FirstOrDefault() ?? string.Empty);

            var entry = _options.EmitStartup
                ? _target.OperatingSystem switch
                {
                    OperatingSystemKind.Linux => EmitLinuxStart(selectedEntry),
                    OperatingSystemKind.Windows => EmitWindowsStart(selectedEntry),
                    _ => selectedEntry,
                }
                : selectedEntry;

            _text.RelaxBranches(_symbols, _machineTarget);
            AddSectionSymbols();
            var dataSections = ImmutableArray.CreateBuilder<ArmDataSection>();
            dataSections.Add(_rodata.ToSection());
            dataSections.Add(_strings.ToSection());
            dataSections.Add(_data.ToSection());
            dataSections.Add(_bss.ToSection());

            return new ArmProgram(
                _machineTarget,
                _text.ToSection(),
                dataSections.ToImmutable(),
                _symbols.ToImmutableArray(),
                entry);
        }

        private string EmitLinuxStart(string userEntryLabel)
        {
            var label = CreateUniqueGlobalLabel("_start");
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);

            if (_machineTarget.Is64Bit)
            {
                Emit(ArmInstruction.Binary(ArmInstrKind.Ldr, Reg(ArmRegister.X0, 8), Mem(ArmRegister.Sp, 0, 8)));
                EmitAddImmediate(ArmRegister.X1, ArmRegister.Sp, 8, 8);
                Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.X2, 8), Reg(ArmRegister.X0, 8), ArmOperand.ImmediateOperand(1)));
                Emit(ArmInstruction.Ternary(ArmInstrKind.Lsl, Reg(ArmRegister.X2, 8), Reg(ArmRegister.X2, 8), ArmOperand.ImmediateOperand(3)));
                Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.X2, 8), Reg(ArmRegister.X1, 8), Reg(ArmRegister.X2, 8)));
                if (!string.IsNullOrEmpty(userEntryLabel))
                    EmitDirectCall(userEntryLabel);
                else
                    EmitLoadImmediate(ArmRegister.X0, 0, 8);
                EmitLoadImmediate(ArmRegister.X8, 93, 8);
                Emit(ArmInstruction.Unary(ArmInstrKind.Svc, ArmOperand.ImmediateOperand(0)));
                Emit(ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand(0)));
            }
            else
            {
                Emit(ArmInstruction.Binary(ArmInstrKind.Ldr, Reg(ArmRegister.R0, 4), Mem(ArmRegister.Sp32, 0, 4)));
                EmitAddImmediate(ArmRegister.R1, ArmRegister.Sp32, 4, 4);
                Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.R2, 4), Reg(ArmRegister.R0, 4), ArmOperand.ImmediateOperand(1)));
                Emit(ArmInstruction.Ternary(ArmInstrKind.Lsl, Reg(ArmRegister.R2, 4), Reg(ArmRegister.R2, 4), ArmOperand.ImmediateOperand(2)));
                Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ArmRegister.R2, 4), Reg(ArmRegister.R1, 4), Reg(ArmRegister.R2, 4)));
                if (!string.IsNullOrEmpty(userEntryLabel))
                    EmitDirectCall(userEntryLabel);
                else
                    EmitLoadImmediate(ArmRegister.R0, 0, 4);
                EmitLoadImmediate(ArmRegister.R7, 1, 4);
                Emit(ArmInstruction.Unary(ArmInstrKind.Svc, ArmOperand.ImmediateOperand(0)));
                Emit(ArmInstruction.Unary(ArmInstrKind.Bkpt, ArmOperand.ImmediateOperand(0)));
            }

            _symbols.Add(new ArmObjectSymbol(
                label, TextSectionName, startOffset, _text.ByteLength - startOffset, ArmObjectSymbolBinding.Global, ArmObjectSymbolKind.Function));
            return label;
        }

        private string EmitWindowsStart(string userEntryLabel)
        {
            var label = CreateUniqueGlobalLabel("mainCRTStartup");
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);

            var argumentSize = _machineTarget.Is64Bit ? 8 : 4;
            EmitLoadImmediate(_machineTarget.Is64Bit ? ArmRegister.X0 : ArmRegister.R0, 0, argumentSize);
            EmitLoadImmediate(_machineTarget.Is64Bit ? ArmRegister.X1 : ArmRegister.R1, 0, argumentSize);
            EmitLoadImmediate(_machineTarget.Is64Bit ? ArmRegister.X2 : ArmRegister.R2, 0, argumentSize);
            if (!string.IsNullOrEmpty(userEntryLabel))
                EmitDirectCall(userEntryLabel);

            AddExternalSymbol("__imp_ExitProcess", ArmObjectSymbolKind.Object);
            var scratch = _machineTarget.Is64Bit ? ArmRegister.X16 : ArmRegister.R12;
            MaterializeSymbolAddress("__imp_ExitProcess", scratch);
            Emit(ArmInstruction.Binary(ArmInstrKind.Ldr, Reg(scratch, _target.PointerSize), Mem(scratch, 0, _target.PointerSize)));
            Emit(ArmInstruction.Unary(_machineTarget.Is64Bit ? ArmInstrKind.Blr : ArmInstrKind.Blx, Reg(scratch, _target.PointerSize)));
            Emit(ArmInstruction.Unary(_machineTarget.Is64Bit ? ArmInstrKind.Brk : ArmInstrKind.Bkpt, ArmOperand.ImmediateOperand(0)));

            _symbols.Add(new ArmObjectSymbol(
                label, TextSectionName, startOffset, _text.ByteLength - startOffset, ArmObjectSymbolBinding.Global, ArmObjectSymbolKind.Function));
            return label;
        }

        private void Emit(ArmInstruction instruction)
            => _text.Emit(instruction);

        private static ArmOperand Reg(ArmRegister register, int size)
            => ArmOperand.RegisterOperand(register, size);

        private static ArmOperand Mem(ArmRegister register, long displacement, int size)
            => ArmOperand.Memory(register, displacement, size);

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
            _symbols.Add(new ArmObjectSymbol(TextSectionName, TextSectionName, 0, _text.ByteLength, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
            _symbols.Add(new ArmObjectSymbol(RodataSectionName, RodataSectionName, 0, _rodata.ByteLength, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
            _symbols.Add(new ArmObjectSymbol(StringSectionName, StringSectionName, 0, _strings.ByteLength, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
            _symbols.Add(new ArmObjectSymbol(DataSectionName, DataSectionName, 0, _data.ByteLength, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
            _symbols.Add(new ArmObjectSymbol(BssSectionName, BssSectionName, 0, _bss.ByteLength, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
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
                    var binding = internalLinkage ? ArmObjectSymbolBinding.Local : ArmObjectSymbolBinding.Global;
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
            => AddExternalSymbol(CreateExternalLabel(symbol.Name), ArmObjectSymbolKind.Object);

        private void AddExternalFunctionSymbol(FunctionSymbol symbol)
            => AddExternalSymbol(CreateExternalLabel(symbol.Name), ArmObjectSymbolKind.Function);

        private void AddExternalSymbol(string name, ArmObjectSymbolKind kind)
        {
            if (_externalLabels.Add(name))
                _symbols.Add(new ArmObjectSymbol(name, string.Empty, 0, 0, ArmObjectSymbolBinding.External, kind));
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
            if (value is string text)
            {
                if (type.Type is ArrayType)
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var count = Math.Min(availableSize, checked(bytes.Length + 1));
                    for (var i = 0; i < count; i++)
                        section.EmitByte(i < bytes.Length ? bytes[i] : (byte)0);
                    return count;
                }

                if (IsPointerLike(type))
                {
                    EmitPointerRelocation(section, CreateStringLiteral(text));
                    return Math.Min(availableSize, _target.PointerSize);
                }
            }

            if (IsFloatType(type))
            {
                if (IsFloat32(type))
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    if (_target.Endianness == TargetEndianness.Big)
                        Array.Reverse(raw);
                    section.EmitBytes(raw, Math.Min(availableSize, 4));
                    return Math.Min(availableSize, 4);
                }

                var raw64 = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                if (_target.Endianness == TargetEndianness.Big)
                    Array.Reverse(raw64);
                section.EmitBytes(raw64, Math.Min(availableSize, 8));
                return Math.Min(availableSize, 8);
            }

            var size = Math.Min(Math.Max(1, _target.SizeOf(type)), availableSize);
            section.EmitInteger(ConvertIntegerConstant(value), size, _target.Endianness);
            return size;
        }

        private void EmitPointerRelocation(DataSectionBuilder section, string symbol)
        {
            var offset = section.ByteLength;
            section.EmitZero(_target.PointerSize);
            section.AddRelocation(offset, symbol, 0, ArmObjectRelocationKind.AbsolutePointer);
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
                AddExternalFunctionSymbol(function);
                return CreateExternalLabel(function.Name);
            }

            if (_dataLabels.TryGetValue(symbol, out var dataLabel))
                return dataLabel;
            if (_dataLabelsByName.TryGetValue(symbol.Name, out dataLabel))
                return dataLabel;

            AddExternalObjectSymbol(symbol);
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
            var binding = _fileScopeLinkage.IsInternal(function.Symbol) ? ArmObjectSymbolBinding.Local : ArmObjectSymbolBinding.Global;
            _symbols.Add(new ArmObjectSymbol(label, TextSectionName, startOffset, size, binding, ArmObjectSymbolKind.Function));
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
            var offset = _strings.Align(1);
            _strings.DefineSymbol(label, offset, bytes.Length + 1, ArmObjectSymbolBinding.Local, _symbols);
            _strings.EmitBytes(bytes, bytes.Length);
            _strings.EmitByte(0);
            _stringLabels.Add(text, label);
            return label;
        }

        private void EmitDirectCall(string label)
        {
            var offset = _text.ByteLength;
            Emit(ArmInstruction.Branch(ArmInstrKind.Bl, label));
            _text.AddRelocation(offset, label, 0, _machineTarget.Is64Bit ? ArmObjectRelocationKind.AArch64Call26 : ArmObjectRelocationKind.ArmCall24);
        }

        private void MaterializeSymbolAddress(string symbol, ArmRegister destination)
        {
            if (_machineTarget.Is64Bit)
            {
                var adrpOffset = _text.ByteLength;
                Emit(ArmInstruction.Binary(ArmInstrKind.Adrp, Reg(destination, 8), ArmOperand.SymbolOperand(symbol, ArmRelocationKind.Adrp)));
                _text.AddRelocation(adrpOffset, symbol, 0, ArmObjectRelocationKind.AArch64Adrp21);
                var addOffset = _text.ByteLength;
                Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(destination, 8), Reg(destination, 8), ArmOperand.ImmediateOperand(0)));
                _text.AddRelocation(addOffset, symbol, 0, ArmObjectRelocationKind.AArch64AddLow12);
            }
            else
            {
                var lowOffset = _text.ByteLength;
                Emit(ArmInstruction.Binary(ArmInstrKind.Movw, Reg(destination, 4), ArmOperand.ImmediateOperand(0)));
                _text.AddRelocation(lowOffset, symbol, 0, ArmObjectRelocationKind.ArmMovw16);
                var highOffset = _text.ByteLength;
                Emit(ArmInstruction.Binary(ArmInstrKind.Movt, Reg(destination, 4), ArmOperand.ImmediateOperand(0)));
                _text.AddRelocation(highOffset, symbol, 0, ArmObjectRelocationKind.ArmMovt16);
            }
        }

        private void EmitLoadImmediate(ArmRegister destination, long value, int size)
        {
            if (_machineTarget.Is64Bit)
            {
                var bits = size == 4 ? unchecked((uint)value) : unchecked((ulong)value);
                var width = size == 4 ? 32 : 64;
                var first = true;
                for (var shift = 0; shift < width; shift += 16)
                {
                    var part = (int)((bits >> shift) & 0xFFFF);
                    if (!first && part == 0)
                        continue;
                    Emit(ArmInstruction.Ternary(
                        first ? ArmInstrKind.Movz : ArmInstrKind.Movk,
                        Reg(destination, size),
                        ArmOperand.ImmediateOperand(part),
                        ArmOperand.ImmediateOperand(shift)));
                    first = false;
                }
                if (first)
                    Emit(ArmInstruction.Ternary(ArmInstrKind.Movz, Reg(destination, size), ArmOperand.ImmediateOperand(0), ArmOperand.ImmediateOperand(0)));
                return;
            }

            var raw = unchecked((uint)value);
            Emit(ArmInstruction.Binary(ArmInstrKind.Movw, Reg(destination, 4), ArmOperand.ImmediateOperand(raw & 0xFFFF)));
            if ((raw >> 16) != 0)
                Emit(ArmInstruction.Binary(ArmInstrKind.Movt, Reg(destination, 4), ArmOperand.ImmediateOperand(raw >> 16)));
        }

        private void EmitAddImmediate(ArmRegister destination, ArmRegister source, int immediate, int size)
        {
            if (immediate == 0)
            {
                if (destination != source)
                {
                    if (_machineTarget.Is64Bit && (destination == ArmRegister.Sp || source == ArmRegister.Sp))
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(destination, size), Reg(source, size), ArmOperand.ImmediateOperand(0)));
                    else
                        Emit(ArmInstruction.Binary(ArmInstrKind.Mov, Reg(destination, size), Reg(source, size)));
                }
                return;
            }

            var magnitude = Math.Abs((long)immediate);
            if (_machineTarget.Is64Bit && (destination == ArmRegister.Sp || source == ArmRegister.Sp))
            {
                var remaining = magnitude;
                var currentSource = source;
                while (remaining != 0)
                {
                    var part = Math.Min(remaining, 4095);
                    Emit(ArmInstruction.Ternary(
                        immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add,
                        Reg(destination, size),
                        Reg(currentSource, size),
                        ArmOperand.ImmediateOperand(part)));
                    currentSource = destination;
                    remaining -= part;
                }
                return;
            }

            if (_machineTarget.Is64Bit && magnitude <= 4095)
            {
                Emit(ArmInstruction.Ternary(
                    immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add,
                    Reg(destination, size),
                    Reg(source, size),
                    ArmOperand.ImmediateOperand(magnitude)));
                return;
            }

            if (!_machineTarget.Is64Bit && magnitude <= 255)
            {
                Emit(ArmInstruction.Ternary(
                    immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add,
                    Reg(destination, size),
                    Reg(source, size),
                    ArmOperand.ImmediateOperand(magnitude)));
                return;
            }

            var scratch = _machineTarget.Is64Bit ? ArmRegister.X17 : ArmRegister.R12;
            EmitLoadImmediate(scratch, magnitude, size);
            Emit(ArmInstruction.Ternary(
                immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add,
                Reg(destination, size),
                Reg(source, size),
                Reg(scratch, size)));
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
            => type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.Char or BuiltinTypeKind.UnsignedChar
            or BuiltinTypeKind.UnsignedShort or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;

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

        private sealed class FunctionEmissionContext
        {
            private MachineRegister Scratch0 => _owner._machineTarget.Is64Bit
                ? MachineRegister.X8
                : _owner._target.OperatingSystem == OperatingSystemKind.Windows ? MachineRegister.X7 : MachineRegister.X8;
            private MachineRegister Scratch1 => _owner._machineTarget.Is64Bit
                ? MachineRegister.X9
                : _owner._target.OperatingSystem == OperatingSystemKind.Windows ? MachineRegister.X8 : MachineRegister.X9;
            private MachineRegister Scratch2 => MachineRegister.X10;
            private MachineRegister Scratch3 => MachineRegister.X11;
            private MachineRegister Scratch4 => MachineRegister.X12;
            private MachineRegister FpScratch0 => MachineRegister.V29;
            private MachineRegister FpScratch1 => MachineRegister.V30;
            private MachineRegister FpScratch2 => MachineRegister.V31;

            private readonly ArmCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly string _functionLabel;
            private readonly IReadOnlyDictionary<LirBlock, string> _labels;
            private readonly bool _hasCalls;
            private readonly int _lrSaveOffset;
            private readonly int _scratchSaveOffset;
            private readonly int _backendTempOffset;
            private readonly int _varArgsGpSaveAreaOffset;
            private readonly int _varArgsGpSaveAreaSize;
            private readonly int _varArgsVrSaveAreaOffset;
            private readonly int _varArgsVrSaveAreaSize;
            private readonly int _totalFrameSize;
            private readonly IntegerRepresentationFact[] _integerRepresentationFacts = new IntegerRepresentationFact[32];
            private int _currentInstructionPosition;
            private LirBlock? _fallthroughBlock;
            private bool _useCallPreservationSources;

            private enum IntegerRepresentationKind : byte
            {
                Unknown,
                SignExtended,
                ZeroExtended,
            }

            private readonly struct IntegerRepresentationFact
            {
                public IntegerRepresentationFact(IntegerRepresentationKind kind, int bits)
                {
                    Kind = kind;
                    Bits = bits;
                }

                public IntegerRepresentationKind Kind { get; }
                public int Bits { get; }
                public bool IsKnown => Kind != IntegerRepresentationKind.Unknown;

                public static IntegerRepresentationFact Unknown => default;
                public static IntegerRepresentationFact SignExtended(int bits) => new IntegerRepresentationFact(IntegerRepresentationKind.SignExtended, bits);
                public static IntegerRepresentationFact ZeroExtended(int bits) => new IntegerRepresentationFact(IntegerRepresentationKind.ZeroExtended, bits);
            }

            public FunctionEmissionContext(
                ArmCodeGenerator owner,
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
                var instructions = function.Blocks.SelectMany(static b => b.Instructions).ToArray();
                _hasCalls = instructions.Any(static i => i.Kind is LirInstructionKind.Call or LirInstructionKind.InlineAssembly);
                _lrSaveOffset = _hasCalls ? AlignUp(_allocation.Frame.FrameSize, _owner._target.PointerAlignment) : -1;
                var frameSize = _allocation.Frame.FrameSize;
                if (_hasCalls)
                    frameSize = Math.Max(frameSize, checked(_lrSaveOffset + _owner._target.PointerSize));

                _scratchSaveOffset = _owner._machineTarget.Is64Bit ? -1 : AlignUp(frameSize, 4);
                if (!_owner._machineTarget.Is64Bit)
                    frameSize = checked(_scratchSaveOffset + 16);

                var maximumCallStagingSlots = instructions
                    .Where(static i => i.Kind == LirInstructionKind.Call)
                    .Select(static i => i.Operands.Length)
                    .DefaultIfEmpty(0)
                    .Max();
                var maximumParallelCopies = instructions
                    .Where(static i => i.Kind == LirInstructionKind.ParallelCopy)
                    .Select(static i => i.ParallelCopies.Length)
                    .DefaultIfEmpty(0)
                    .Max();
                var temporarySlotSize = AlignUp(_owner._target.RegisterSize, _owner._allocationOptions.SpillSlotAlignment);
                var backendTempSize = Math.Max(
                    checked(maximumCallStagingSlots * _owner._allocationOptions.StackArgumentSlotSize),
                    checked(maximumParallelCopies * temporarySlotSize));
                _backendTempOffset = AlignUp(frameSize, _owner._target.PointerAlignment);
                frameSize = checked(_backendTempOffset + backendTempSize);
                ComputeArmVarArgsSaveAreaSizes(out _varArgsGpSaveAreaSize, out _varArgsVrSaveAreaSize);
                _totalFrameSize = AlignUp(
                    checked(frameSize + _varArgsGpSaveAreaSize + _varArgsVrSaveAreaSize),
                    _allocation.Frame.FrameAlignment);
                if (UsesAapcs64VaList)
                {
                    _varArgsVrSaveAreaOffset = _varArgsVrSaveAreaSize == 0
                        ? -1
                        : checked(_totalFrameSize - _varArgsVrSaveAreaSize);
                    _varArgsGpSaveAreaOffset = _varArgsGpSaveAreaSize == 0
                        ? -1
                        : checked(_totalFrameSize - _varArgsVrSaveAreaSize - _varArgsGpSaveAreaSize);
                }
                else
                {
                    _varArgsVrSaveAreaOffset = -1;
                    _varArgsGpSaveAreaOffset = _varArgsGpSaveAreaSize == 0
                        ? -1
                        : checked(_totalFrameSize - _varArgsGpSaveAreaSize);
                }
            }

            public void EmitPrologue()
            {
                AdjustStack(-_totalFrameSize);
                SaveIncomingHiddenReturnBuffer();
                SaveIncomingVarArgs();
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderBy(static p => p.Value))
                    StoreRegister(pair.Key, pair.Value, RegisterSaveSize(pair.Key));
                if (_hasCalls)
                    StoreArmRegister(_owner._machineTarget.Is64Bit ? ArmRegister.X30 : ArmRegister.Lr, _lrSaveOffset, _owner._target.PointerSize);
                if (!_owner._machineTarget.Is64Bit)
                {
                    StoreRegister(Scratch0, _scratchSaveOffset, 4);
                    StoreRegister(Scratch1, _scratchSaveOffset + 4, 4);
                    StoreRegister(Scratch2, _scratchSaveOffset + 8, 4);
                    StoreRegister(Scratch3, _scratchSaveOffset + 12, 4);
                }
            }

            private bool UsesAapcs64VaList
                => _owner._machineTarget.Is64Bit && _owner._machineTarget.Abi == ArmAbiKind.Aapcs64;

            private void ComputeArmVarArgsSaveAreaSizes(out int gpSize, out int vrSize)
            {
                gpSize = 0;
                vrSize = 0;
                if (!_allocation.Frame.HasVarArgsPointer || _function.Symbol?.FunctionType?.IsVariadic != true)
                    return;

                var cursor = ComputeNamedArgumentCursor();
                var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                if (UsesAapcs64VaList)
                {
                    gpSize = checked(integerRegisters.Length * 8);
                    vrSize = checked(TargetRegisterInfo.VectorArgumentRegisters(_owner._target).Length * 16);
                    return;
                }

                gpSize = checked(Math.Max(0, integerRegisters.Length - cursor.Integer) * _owner._target.PointerSize);
            }

            private AbiCursor ComputeNamedArgumentCursor()
            {
                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);

                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    return cursor;

                foreach (var parameter in functionType.Parameters)
                {
                    var value = CAbi.ClassifyValue(
                        _owner._target,
                        parameter.Type,
                        isReturn: false,
                        isVariadicUnnamedArgument: false,
                        isVariadicFunction: functionType.IsVariadic);
                    _ = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                }
                return cursor;
            }

            private void SaveIncomingVarArgs()
            {
                if (!_allocation.Frame.HasVarArgsPointer || _function.Symbol?.FunctionType?.IsVariadic != true)
                    return;

                var cursor = ComputeNamedArgumentCursor();
                var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                if (_varArgsGpSaveAreaOffset >= 0)
                {
                    var firstRegister = UsesAapcs64VaList ? 0 : cursor.Integer;
                    for (var register = firstRegister; register < integerRegisters.Length; register++)
                    {
                        StoreRegister(
                            integerRegisters[register],
                            checked(_varArgsGpSaveAreaOffset + (register - firstRegister) * _owner._target.PointerSize),
                            _owner._target.PointerSize);
                    }
                }

                if (UsesAapcs64VaList && _varArgsVrSaveAreaOffset >= 0)
                {
                    var vectorRegisters = TargetRegisterInfo.VectorArgumentRegisters(_owner._target);
                    for (var register = 0; register < vectorRegisters.Length; register++)
                    {
                        var armRegister = (ArmRegister)((int)ArmRegister.V0 + ((int)vectorRegisters[register] - (int)MachineRegister.V0));
                        EmitMemoryStore(
                            armRegister,
                            StackPointerArm,
                            checked(_varArgsVrSaveAreaOffset + register * 16),
                            16);
                    }
                }

                if (UsesAapcs64VaList)
                    return;

                if (_varArgsGpSaveAreaOffset >= 0)
                    AddImmediate(Scratch0, StackPointer, _varArgsGpSaveAreaOffset);
                else
                    AddImmediate(Scratch0, StackPointer, IncomingStackOffset(cursor.Stack * _owner._allocationOptions.StackArgumentSlotSize));
                StoreRegister(Scratch0, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize);
            }

            public void EmitBlocks()
            {
                _currentInstructionPosition = 0;
                for (var blockIndex = 0; blockIndex < _function.Blocks.Length; blockIndex++)
                {
                    var block = _function.Blocks[blockIndex];
                    _fallthroughBlock = blockIndex + 1 < _function.Blocks.Length
                        ? _function.Blocks[blockIndex + 1]
                        : null;
                    ClearIntegerRepresentationFacts();
                    _owner._text.DefineLabel(LabelOf(block));
                    foreach (var instruction in block.Instructions)
                    {
                        EmitInstruction(instruction);
                        _currentInstructionPosition += 2;
                    }
                }
                _fallthroughBlock = null;
            }

            public void EmitTrap()
                => Emit(ArmInstruction.Unary(_owner._machineTarget.Is64Bit ? ArmInstrKind.Brk : ArmInstrKind.Bkpt, ArmOperand.ImmediateOperand(0)));

            private void SaveIncomingHiddenReturnBuffer()
            {
                var returnType = _function.Symbol?.FunctionType?.ReturnType;
                if (!returnType.HasValue || !CAbi.RequiresHiddenReturnBuffer(_owner._target, returnType.Value) || !_allocation.Frame.HasHiddenReturnBuffer)
                    return;

                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (location.Kind == AbiLocationKind.Register)
                {
                    StoreRegister(location.Register, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(Scratch0, IncomingStackOffset(
                        location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), _owner._target.PointerSize, false);
                    StoreRegister(Scratch0, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                }
            }

            private void EmitInstruction(LirInstruction instruction)
            {
                switch (instruction.Kind)
                {
                    case LirInstructionKind.Nop:
                        Emit(ArmInstruction.Nop());
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
                        throw Unsupported(instruction, "Unsupported LIR instruction kind.");
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
                        formatted = new InlineAsmFormattedOperand(output.Name, modifier => FormatArmAsmMemoryOperand(operand));
                    }
                    else
                    {
                        if (copyIndex >= instruction.ParallelCopies.Length)
                            throw Unsupported(instruction, "Inline assembly output register is missing from LIR.");
                        var destination = instruction.ParallelCopies[copyIndex++].Destination;
                        formatted = CreateArmAsmOutputOperand(output, destination, instruction, outputFinalizers);
                    }

                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var input in asmStatement.Inputs)
                {
                    if (operandIndex >= instruction.Operands.Length)
                        throw Unsupported(instruction, "Inline assembly input operand is missing from LIR.");

                    var operand = instruction.Operands[operandIndex++];
                    var formatted = CreateArmAsmInputOperand(input, operand, instruction);
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

            private static void AddAsmOperand(
                List<InlineAsmFormattedOperand> operands,
                Dictionary<string, int> namedOperands,
                InlineAsmFormattedOperand operand)
            {
                if (operand.Name is not null && !namedOperands.ContainsKey(operand.Name))
                    namedOperands.Add(operand.Name, operands.Count);
                operands.Add(operand);
            }

            private InlineAsmFormattedOperand CreateArmAsmOutputOperand(
                GimpleAsmOperand operand,
                LirVirtualRegister destination,
                LirInstruction instruction,
                List<Action> finalizers)
            {
                RequireScalar(destination.Type, instruction);
                var fixedRegister = TryGetArmConstraintRegister(operand.Constraint, destination.Type);
                if (fixedRegister.HasValue)
                {
                    if (operand.IsReadWrite)
                        LoadOperandIntoAs(LirOperand.ForRegister(destination), fixedRegister.Value, destination.Type, instruction);
                    finalizers.Add(() => StoreArmAsmOutput(destination, fixedRegister.Value));
                    return new InlineAsmFormattedOperand(
                        operand.Name,
                        modifier => FormatArmAsmRegister(fixedRegister.Value, destination.Type, modifier));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var register = GetWritableRegister(
                    destination,
                    PreferredScratch(destination.Type, Scratch0, FpScratch0));
                finalizers.Add(() =>
                {
                    if (!IsFloatType(destination.Type))
                        NormalizeIntegerRegister(register, destination.Type);
                    StoreWritableRegisterIfSpilled(destination, register);
                });
                return new InlineAsmFormattedOperand(
                    operand.Name,
                    modifier => FormatArmAsmRegister(register, destination.Type, modifier));
            }

            private InlineAsmFormattedOperand CreateArmAsmInputOperand(
                GimpleAsmOperand operand,
                LirOperand value,
                LirInstruction instruction)
            {
                RequireScalar(value.Type, instruction);
                var storage = InlineAsmConstraints.PreferredStorage(operand.Constraint, value.Type);
                if (storage == InlineAsmOperandStorage.Memory)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatArmAsmMemoryOperand(value));
                if (storage == InlineAsmOperandStorage.Immediate)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatArmAsmImmediate(value));

                var fixedRegister = TryGetArmConstraintRegister(operand.Constraint, value.Type);
                if (fixedRegister.HasValue)
                {
                    LoadOperandIntoAs(value, fixedRegister.Value, value.Type, instruction);
                    return new InlineAsmFormattedOperand(
                        operand.Name,
                        modifier => FormatArmAsmRegister(fixedRegister.Value, value.Type, modifier));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var register = LoadOperand(
                    value,
                    PreferredScratch(value.Type, Scratch1, FpScratch1));
                return new InlineAsmFormattedOperand(
                    operand.Name,
                    modifier => FormatArmAsmRegister(register, value.Type, modifier));
            }

            private void StoreArmAsmOutput(LirVirtualRegister destination, MachineRegister source)
            {
                var writable = GetWritableRegister(destination, source);
                if (writable != source)
                    MoveRegister(writable, source, RegisterSize(destination.Type));
                if (!IsFloatType(destination.Type))
                    NormalizeIntegerRegister(writable, destination.Type);
                StoreWritableRegisterIfSpilled(destination, writable);
            }

            private MachineRegister? TryGetArmConstraintRegister(string constraint, QualifiedType type)
            {
                var explicitRegister = InlineAsmConstraints.ExplicitRegisterName(constraint);
                if (explicitRegister is null)
                    return null;

                var registerClass = CAbi.PreferredLirRegisterClass(_owner._target, type);
                return TargetRegisterInfo.TryParseExplicitRegister(_owner._target, explicitRegister, registerClass, out var register)
                    ? register
                    : null;
            }

            private string FormatArmAsmRegister(MachineRegister register, QualifiedType type, char? modifier)
            {
                var size = RegisterSize(type);
                if (_owner._machineTarget.Is64Bit)
                {
                    if (modifier is 'w' or 'W')
                        size = 4;
                    else if (modifier is 'x' or 'X')
                        size = 8;
                }
                return ArmRegisters.Format(ToArmRegister(register), size, _owner._machineTarget);
            }

            private string FormatArmAsmMemoryOperand(LirOperand operand)
            {
                if (operand.Kind == LirOperandKind.Address && operand.Address is not null)
                {
                    var address = BuildAddress(operand.Address, Scratch0, Scratch1);
                    var register = ArmRegisters.Format(ToArm(address.BaseRegister), _owner._target.PointerSize, _owner._machineTarget);
                    return address.Offset == 0
                        ? $"[{register}]"
                        : $"[{register}, #{address.Offset.ToString(CultureInfo.InvariantCulture)}]";
                }

                var pointer = LoadOperand(operand, Scratch0);
                return $"[{ArmRegisters.Format(ToArm(pointer), _owner._target.PointerSize, _owner._machineTarget)}]";
            }

            private string FormatArmAsmImmediate(LirOperand operand)
            {
                return operand.Kind switch
                {
                    LirOperandKind.Immediate => ConvertIntegerConstant(operand.Immediate).ToString(CultureInfo.InvariantCulture),
                    LirOperandKind.Symbol when operand.Symbol is not null => _owner.GetSymbolLabel(operand.Symbol),
                    _ => ArmRegisters.Format(ToArm(LoadOperand(operand, Scratch1)), _owner._target.PointerSize, _owner._machineTarget),
                };
            }

            private void EmitInlineAssemblyText(LirInstruction instruction, string text)
            {
                try
                {
                    _owner._text.EmitAssembly(text, _owner.CreateLocalLabel(_functionLabel + "_asm"), _owner._machineTarget);
                    ClearIntegerRepresentationFacts();
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    throw Unsupported(instruction, $"Invalid ARM inline assembly: {ex.Message}");
                }
            }

            private void EmitVaStart(LirInstruction instruction)
            {
                if (UsesAapcs64VaList)
                {
                    EmitAapcs64VaStart(instruction);
                    return;
                }

                if (instruction.Operands.Length == 1)
                {
                    var ap = LoadOperand(instruction.Operands[0], Scratch0);
                    if (_allocation.Frame.HasVarArgsPointer)
                        LoadFromMemory(Scratch1, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, false);
                    else
                        LoadImmediate(Scratch1, 0, _owner._target.PointerSize);
                    StoreToMemory(Scratch1, ap, 0, _owner._target.PointerSize);
                    return;
                }

                if (instruction.Result is null)
                    return;
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                if (_allocation.Frame.HasVarArgsPointer)
                    LoadFromMemory(destination, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, false);
                else
                    LoadImmediate(destination, 0, _owner._target.PointerSize);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitAapcs64VaStart(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "AAPCS64 VaStart expects a va_list pointer.");

                var loadedAp = LoadOperand(instruction.Operands[0], Scratch4);
                if (loadedAp != Scratch4)
                    MoveRegister(Scratch4, loadedAp, _owner._target.PointerSize);
                var cursor = ComputeNamedArgumentCursor();

                AddImmediate(
                    Scratch0,
                    StackPointer,
                    IncomingStackOffset(cursor.Stack * _owner._allocationOptions.StackArgumentSlotSize));
                StoreToMemory(Scratch0, Scratch4, 0, 8);

                var gpTopOffset = checked(_varArgsGpSaveAreaOffset + _varArgsGpSaveAreaSize);
                AddImmediate(Scratch0, StackPointer, gpTopOffset);
                StoreToMemory(Scratch0, Scratch4, 8, 8);

                var vrTopOffset = checked(_varArgsVrSaveAreaOffset + _varArgsVrSaveAreaSize);
                AddImmediate(Scratch0, StackPointer, vrTopOffset);
                StoreToMemory(Scratch0, Scratch4, 16, 8);

                LoadImmediate(Scratch0, checked(cursor.Integer * 8 - _varArgsGpSaveAreaSize), 4);
                StoreToMemory(Scratch0, Scratch4, 24, 4);
                LoadImmediate(Scratch0, checked(cursor.Vector * 16 - _varArgsVrSaveAreaSize), 4);
                StoreToMemory(Scratch0, Scratch4, 28, 4);
            }

            private void EmitVaArg(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 4)
                    throw Unsupported(instruction, "VaArg expects a va_list pointer, kind, size, and alignment.");
                if (instruction.Result is null)
                    return;

                var kind = ImmediateToInt32(instruction.Operands[1]);
                var size = Math.Max(1, ImmediateToInt32(instruction.Operands[2]));
                var align = Math.Max(1, ImmediateToInt32(instruction.Operands[3]));
                if (UsesAapcs64VaList)
                    EmitAapcs64VaArg(instruction, kind, size, align);
                else
                    EmitPointerVaArg(instruction, size, align);
            }

            private void EmitPointerVaArg(LirInstruction instruction, int size, int align)
            {
                var loadedAp = LoadOperand(instruction.Operands[0], Scratch4);
                if (loadedAp != Scratch4)
                    MoveRegister(Scratch4, loadedAp, _owner._target.PointerSize);
                LoadFromMemory(Scratch1, Scratch4, 0, _owner._target.PointerSize, false);
                AlignPointerRegister(Scratch1, align);
                var destination = GetWritableRegister(instruction.Result!, Scratch0);
                MoveRegister(destination, Scratch1, _owner._target.PointerSize);
                AddImmediate(Scratch1, Scratch1, AlignUp(size, _owner._target.PointerSize));
                StoreToMemory(Scratch1, Scratch4, 0, _owner._target.PointerSize);
                StoreWritableRegisterIfSpilled(instruction.Result!, destination);
            }

            private void EmitAapcs64VaArg(LirInstruction instruction, int kind, int size, int align)
            {
                var loadedAp = LoadOperand(instruction.Operands[0], Scratch4);
                if (loadedAp != Scratch4)
                    MoveRegister(Scratch4, loadedAp, 8);

                var overflowLabel = _owner.CreateLocalLabel(_functionLabel + "_va_overflow");
                var doneLabel = _owner.CreateLocalLabel(_functionLabel + "_va_done");
                if (kind == 0 && size <= 16)
                    EmitAapcs64RegisterVaArg(Scratch4, 8, 24, AlignUp(size, 8), Math.Min(Math.Max(align, 1), 16), overflowLabel);
                else if (kind == 1 && size <= 16)
                    EmitAapcs64RegisterVaArg(Scratch4, 16, 28, 16, 16, overflowLabel);
                else
                    EmitJump(overflowLabel);

                EmitJump(doneLabel);
                _owner._text.DefineLabel(overflowLabel);
                EmitAapcs64OverflowVaArg(Scratch4, size, align);
                _owner._text.DefineLabel(doneLabel);

                var destination = GetWritableRegister(instruction.Result!, Scratch0);
                MoveRegister(destination, Scratch1, 8);
                StoreWritableRegisterIfSpilled(instruction.Result!, destination);
            }

            private void EmitAapcs64RegisterVaArg(
                MachineRegister ap,
                int topField,
                int offsetField,
                int needed,
                int align,
                string overflowLabel)
            {
                LoadFromMemory(Scratch1, ap, offsetField, 4, false);
                SignExtend32ToPointer(Scratch1);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), 8), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ge, overflowLabel);

                LoadFromMemory(Scratch2, ap, topField, 8, false);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Add,
                    Reg(ToArm(Scratch3), 8),
                    Reg(ToArm(Scratch2), 8),
                    Reg(ToArm(Scratch1), 8)));
                AlignPointerRegister(Scratch3, align);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Sub,
                    Reg(ToArm(Scratch1), 8),
                    Reg(ToArm(Scratch3), 8),
                    Reg(ToArm(Scratch2), 8)));
                AddImmediate(Scratch1, Scratch1, needed);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), 8), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Gt, overflowLabel);
                StoreToMemory(Scratch1, ap, offsetField, 4);
                MoveRegister(Scratch1, Scratch3, 8);
            }

            private void EmitAapcs64OverflowVaArg(MachineRegister ap, int size, int align)
            {
                LoadFromMemory(Scratch1, ap, 0, 8, false);
                AlignPointerRegister(Scratch1, Math.Min(Math.Max(align, 1), 16));
                MoveRegister(Scratch2, Scratch1, 8);
                AddImmediate(Scratch2, Scratch2, AlignUp(size, 8));
                StoreToMemory(Scratch2, ap, 0, 8);
            }

            private void SignExtend32ToPointer(MachineRegister register)
            {
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Lsl,
                    Reg(ToArm(register), 8),
                    Reg(ToArm(register), 8),
                    ArmOperand.ImmediateOperand(32)));
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Asr,
                    Reg(ToArm(register), 8),
                    Reg(ToArm(register), 8),
                    ArmOperand.ImmediateOperand(32)));
                InvalidateIntegerRepresentation(register);
            }

            private void AlignPointerRegister(MachineRegister register, int alignment)
            {
                if (alignment <= 1)
                    return;
                AddImmediate(register, register, alignment - 1);
                LoadImmediate(Scratch0, -alignment, _owner._target.PointerSize);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.And,
                    Reg(ToArm(register), _owner._target.PointerSize),
                    Reg(ToArm(register), _owner._target.PointerSize),
                    Reg(ToArm(Scratch0), _owner._target.PointerSize)));
                InvalidateIntegerRepresentation(register);
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                RequireScalar(instruction.Result.Type, instruction);

                var parameterIndex = FindParameterIndex(instruction.Operator);
                if (parameterIndex < 0)
                    throw Unsupported(instruction, "Cannot map parameter to function signature.");

                var functionType = _function.Symbol?.FunctionType ?? throw Unsupported(instruction, "Parameter instruction requires function type.");
                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);

                for (var i = 0; i < parameterIndex; i++)
                {
                    var preceding = CAbi.ClassifyValue(
                        _owner._target,
                        functionType.Parameters[i].Type,
                        false,
                        false,
                        functionType.IsVariadic);
                    _ = CAbi.AssignArgumentLocation(preceding, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                }

                var type = functionType.Parameters[parameterIndex].Type;
                var value = CAbi.ClassifyValue(_owner._target, type, false, false, functionType.IsVariadic);
                if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 ||
                    value.Segments[0].RegisterClass is not (AbiRegisterClass.General or AbiRegisterClass.Vector))
                    throw Unsupported(instruction, "Unsupported scalar ARM parameter class.");

                var location = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(type, Scratch0, FpScratch0));
                if (location.Kind == AbiLocationKind.Register)
                    MoveRegister(destination, location.Register, RegisterSize(type));
                else if (location.Kind == AbiLocationKind.Stack)
                    LoadFromMemory(
                        destination,
                        IncomingStackOffset(location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)),
                        SizeOf(type),
                        IsSignedIntegerType(type));
                else
                    throw Unsupported(instruction, "Invalid parameter ABI location.");

                if (!IsFloatType(type))
                    NormalizeIntegerRegister(destination, type);
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
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);

                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(instruction.Result.Type, Scratch0, FpScratch0));
                LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return;
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);

                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(instruction.Result.Type, Scratch0, FpScratch0));
                if (instruction.Result.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                {
                    if (IsFloatType(instruction.Operands[0].Type))
                    {
                        var source = LoadOperand(instruction.Operands[0], FpScratch0);
                        LoadFloatingImmediate(FpScratch1, 0.0, instruction.Operands[0].Type);
                        EmitFloatingComparisonResult(destination, source, FpScratch1, ArmCondition.Ne, RegisterSize(instruction.Operands[0].Type));
                    }
                    else
                    {
                        var source = LoadOperand(instruction.Operands[0], destination == Scratch0 ? Scratch1 : Scratch0);
                        EmitBooleanFromRegister(destination, source);
                    }
                }
                else
                {
                    LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                }
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                RequireScalar(instruction.Result.Type, instruction);
                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(instruction.Result.Type, Scratch0, FpScratch0));
                if (IsFloatType(instruction.Result.Type))
                    LoadFloatingImmediate(destination, 0.0, instruction.Result.Type);
                else
                    LoadImmediate(destination, 0, RegisterSize(instruction.Result.Type));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Unary instruction is incomplete.");
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);

                if (IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingUnary(instruction);
                    return;
                }

                var size = RegisterSize(instruction.Result.Type);
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                var source = LoadOperand(instruction.Operands[0], Scratch1);
                if (instruction.Operator != "+")
                    InvalidateIntegerRepresentation(destination);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(destination, source, size);
                        break;
                    case "-":
                        LoadImmediate(Scratch2, 0, size);
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ToArm(destination), size), Reg(ToArm(Scratch2), size), Reg(ToArm(source), size)));
                        break;
                    case "~":
                        Emit(ArmInstruction.Binary(ArmInstrKind.Mvn, Reg(ToArm(destination), size), Reg(ToArm(source), size)));
                        break;
                    case "!":
                        EmitComparisonResult(destination, source, Scratch2, ArmCondition.Eq, RegisterSize(instruction.Operands[0].Type), rightIsZero: true);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported unary operator '{instruction.Operator}'.");
                }

                RecordIntegerRegisterWrite(destination, size);
                NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitBinary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length != 2)
                    throw Unsupported(instruction, "Binary instruction is incomplete.");
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);
                RequireScalar(instruction.Operands[1].Type, instruction);

                if (IsFloatType(instruction.Operands[0].Type) || IsFloatType(instruction.Operands[1].Type))
                {
                    EmitFloatingBinary(instruction);
                    return;
                }

                if (TryEmitPointerBinary(instruction))
                    return;

                var size = Math.Max(RegisterSize(instruction.Operands[0].Type), RegisterSize(instruction.Operands[1].Type));
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                var left = LoadOperand(instruction.Operands[0], Scratch1);
                if (left != Scratch1)
                    MoveRegister(Scratch1, left, size);
                var right = LoadOperand(instruction.Operands[1], Scratch2);
                if (right != Scratch2)
                    MoveRegister(Scratch2, right, size);
                InvalidateIntegerRepresentation(destination);

                switch (instruction.Operator)
                {
                    case "+":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "-":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "*":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Mul, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "/":
                        Emit(ArmInstruction.Ternary(
                            IsSignedIntegerType(instruction.Operands[0].Type) ? ArmInstrKind.Sdiv : ArmInstrKind.Udiv,
                            Reg(ToArm(destination), size),
                            Reg(ToArm(Scratch1), size),
                            Reg(ToArm(Scratch2), size)));
                        break;
                    case "%":
                        Emit(ArmInstruction.Ternary(
                            IsSignedIntegerType(instruction.Operands[0].Type) ? ArmInstrKind.Sdiv : ArmInstrKind.Udiv,
                            Reg(ToArm(Scratch3), size),
                            Reg(ToArm(Scratch1), size),
                            Reg(ToArm(Scratch2), size)));
                        Emit(ArmInstruction.Quaternary(_owner._machineTarget.Is64Bit ? ArmInstrKind.Msub : ArmInstrKind.Mls,
                            Reg(ToArm(destination), size),
                            Reg(ToArm(Scratch3), size),
                            Reg(ToArm(Scratch2), size),
                            Reg(ToArm(Scratch1), size)));
                        break;
                    case "&":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.And, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "|":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Orr, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "^":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Eor, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case "<<":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Lsl, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        break;
                    case ">>":
                        Emit(ArmInstruction.Ternary(
                            IsSignedIntegerType(instruction.Operands[0].Type) ? ArmInstrKind.Asr : ArmInstrKind.Lsr,
                            Reg(ToArm(destination), size),
                            Reg(ToArm(Scratch1), size),
                            Reg(ToArm(Scratch2), size)));
                        break;
                    case "==":
                    case "!=":
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                        EmitComparisonResult(
                            destination,
                            Scratch1,
                            Scratch2,
                            SelectCondition(instruction.Operator, IsSignedIntegerType(instruction.Operands[0].Type)),
                            size,
                            rightIsZero: false);
                        break;
                    case "&&":
                        EmitBooleanFromRegister(Scratch3, Scratch1);
                        EmitBooleanFromRegister(Scratch4, Scratch2);
                        Emit(ArmInstruction.Ternary(ArmInstrKind.And, Reg(ToArm(destination), size), Reg(ToArm(Scratch3), size), Reg(ToArm(Scratch4), size)));
                        SetIntegerRepresentation(destination, IntegerRepresentationFact.ZeroExtended(1));
                        break;
                    case "||":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Orr, Reg(ToArm(Scratch3), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        EmitBooleanFromRegister(destination, Scratch3);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported binary operator '{instruction.Operator}'.");
                }

                RecordIntegerRegisterWrite(destination, size);
                NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitFloatingUnary(LirInstruction instruction)
            {
                var sourceType = instruction.Operands[0].Type;
                var size = RegisterSize(sourceType);
                var source = LoadOperand(instruction.Operands[0], FpScratch1);
                if (source != FpScratch1)
                    MoveRegister(FpScratch1, source, size);

                if (instruction.Operator == "!")
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    LoadFloatingImmediate(FpScratch2, 0.0, sourceType);
                    EmitFloatingComparisonResult(destination, FpScratch1, FpScratch2, ArmCondition.Eq, size);
                    NormalizeIntegerRegister(destination, instruction.Result!.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result!, destination);
                    return;
                }

                var floatingDestination = GetWritableRegister(instruction.Result!, FpScratch0);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(floatingDestination, FpScratch1, size);
                        break;
                    case "-":
                        Emit(ArmInstruction.Binary(
                            ArmInstrKind.Fneg,
                            Reg(ToArmRegister(floatingDestination), size),
                            Reg(ToArmRegister(FpScratch1), size)));
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported floating-point unary operator '{instruction.Operator}'.");
                }
                StoreWritableRegisterIfSpilled(instruction.Result!, floatingDestination);
            }

            private void EmitFloatingBinary(LirInstruction instruction)
            {
                var leftType = instruction.Operands[0].Type;
                var rightType = instruction.Operands[1].Type;
                if (!IsFloatType(leftType) || !IsFloatType(rightType) || RegisterSize(leftType) != RegisterSize(rightType))
                    throw Unsupported(instruction, "Mixed AArch64 floating-point binary operands require prior conversion.");

                var size = RegisterSize(leftType);
                var left = LoadOperand(instruction.Operands[0], FpScratch1);
                if (left != FpScratch1)
                    MoveRegister(FpScratch1, left, size);
                var right = LoadOperand(instruction.Operands[1], FpScratch2);
                if (right != FpScratch2)
                    MoveRegister(FpScratch2, right, size);

                if (instruction.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=")
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    EmitFloatingComparisonResult(
                        destination,
                        FpScratch1,
                        FpScratch2,
                        SelectFloatingCondition(instruction.Operator),
                        size);
                    NormalizeIntegerRegister(destination, instruction.Result!.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result!, destination);
                    return;
                }

                if (instruction.Operator is "&&" or "||")
                {
                    LoadFloatingImmediate(FpScratch0, 0.0, leftType);
                    EmitFloatingComparisonResult(Scratch1, FpScratch1, FpScratch0, ArmCondition.Ne, size);
                    EmitFloatingComparisonResult(Scratch2, FpScratch2, FpScratch0, ArmCondition.Ne, size);
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    Emit(ArmInstruction.Ternary(
                        instruction.Operator == "&&" ? ArmInstrKind.And : ArmInstrKind.Orr,
                        Reg(ToArm(destination), _owner._target.RegisterSize),
                        Reg(ToArm(Scratch1), _owner._target.RegisterSize),
                        Reg(ToArm(Scratch2), _owner._target.RegisterSize)));
                    NormalizeIntegerRegister(destination, instruction.Result!.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result!, destination);
                    return;
                }

                var floatingDestination = GetWritableRegister(instruction.Result!, FpScratch0);
                var opcode = instruction.Operator switch
                {
                    "+" => ArmInstrKind.Fadd,
                    "-" => ArmInstrKind.Fsub,
                    "*" => ArmInstrKind.Fmul,
                    "/" => ArmInstrKind.Fdiv,
                    _ => throw Unsupported(instruction, $"Unsupported floating-point binary operator '{instruction.Operator}'."),
                };
                Emit(ArmInstruction.Ternary(
                    opcode,
                    Reg(ToArmRegister(floatingDestination), size),
                    Reg(ToArmRegister(FpScratch1), size),
                    Reg(ToArmRegister(FpScratch2), size)));
                StoreWritableRegisterIfSpilled(instruction.Result!, floatingDestination);
            }

            private static ArmCondition SelectFloatingCondition(string op)
                => op switch
                {
                    "==" => ArmCondition.Eq,
                    "!=" => ArmCondition.Ne,
                    "<" => ArmCondition.Mi,
                    "<=" => ArmCondition.Ls,
                    ">" => ArmCondition.Gt,
                    ">=" => ArmCondition.Ge,
                    _ => throw new ArgumentOutOfRangeException(nameof(op)),
                };

            private bool TryEmitPointerBinary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return false;
                var leftType = instruction.Operands[0].Type;
                var rightType = instruction.Operands[1].Type;
                var op = instruction.Operator;
                var size = _owner._target.PointerSize;

                if (op == "+" && IsPointerLike(leftType) && IsIntegerLike(rightType))
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    InvalidateIntegerRepresentation(destination);
                    var pointer = LoadOperand(instruction.Operands[0], Scratch1);
                    if (pointer != Scratch1)
                        MoveRegister(Scratch1, pointer, size);
                    var index = LoadOperand(instruction.Operands[1], Scratch2);
                    if (index != Scratch2)
                        MoveRegister(Scratch2, index, size);
                    ScaleIndex(Scratch2, PointerScale(leftType), Scratch3);
                    Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                if (op == "+" && IsIntegerLike(leftType) && IsPointerLike(rightType))
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    InvalidateIntegerRepresentation(destination);
                    var index = LoadOperand(instruction.Operands[0], Scratch2);
                    if (index != Scratch2)
                        MoveRegister(Scratch2, index, size);
                    var pointer = LoadOperand(instruction.Operands[1], Scratch1);
                    if (pointer != Scratch1)
                        MoveRegister(Scratch1, pointer, size);
                    ScaleIndex(Scratch2, PointerScale(rightType), Scratch3);
                    Emit(ArmInstruction.Ternary(ArmInstrKind.Add, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                if (op == "-" && IsPointerLike(leftType) && IsIntegerLike(rightType))
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    InvalidateIntegerRepresentation(destination);
                    var pointer = LoadOperand(instruction.Operands[0], Scratch1);
                    if (pointer != Scratch1)
                        MoveRegister(Scratch1, pointer, size);
                    var index = LoadOperand(instruction.Operands[1], Scratch2);
                    if (index != Scratch2)
                        MoveRegister(Scratch2, index, size);
                    ScaleIndex(Scratch2, PointerScale(leftType), Scratch3);
                    Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                if (op == "-" && IsPointerLike(leftType) && IsPointerLike(rightType))
                {
                    var destination = GetWritableRegister(instruction.Result!, Scratch0);
                    InvalidateIntegerRepresentation(destination);
                    var left = LoadOperand(instruction.Operands[0], Scratch1);
                    if (left != Scratch1)
                        MoveRegister(Scratch1, left, size);
                    var right = LoadOperand(instruction.Operands[1], Scratch2);
                    if (right != Scratch2)
                        MoveRegister(Scratch2, right, size);
                    Emit(ArmInstruction.Ternary(ArmInstrKind.Sub, Reg(ToArm(destination), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                    var scale = PointerScale(leftType);
                    if (scale > 1)
                    {
                        if (IsPowerOfTwo(scale))
                        {
                            Emit(ArmInstruction.Ternary(ArmInstrKind.Asr, Reg(ToArm(destination), size), Reg(ToArm(destination), size), ArmOperand.ImmediateOperand(Log2(scale))));
                        }
                        else
                        {
                            LoadImmediate(Scratch3, scale, size);
                            Emit(ArmInstruction.Ternary(ArmInstrKind.Sdiv, Reg(ToArm(destination), size), Reg(ToArm(destination), size), Reg(ToArm(Scratch3), size)));
                        }
                    }
                    NormalizeIntegerRegister(destination, instruction.Result.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result, destination);
                    return true;
                }

                return false;
            }

            private static ArmCondition SelectCondition(string op, bool signed)
            {
                return op switch
                {
                    "==" => ArmCondition.Eq,
                    "!=" => ArmCondition.Ne,
                    "<" => signed ? ArmCondition.Lt : ArmCondition.Lo,
                    "<=" => signed ? ArmCondition.Le : ArmCondition.Ls,
                    ">" => signed ? ArmCondition.Gt : ArmCondition.Hi,
                    ">=" => signed ? ArmCondition.Ge : ArmCondition.Hs,
                    _ => throw new ArgumentOutOfRangeException(nameof(op)),
                };
            }

            private void EmitComparisonResult(MachineRegister destination, MachineRegister left, MachineRegister right, ArmCondition condition, int size, bool rightIsZero)
            {
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(left), size), rightIsZero ? ArmOperand.ImmediateOperand(0) : Reg(ToArm(right), size)));
                var trueLabel = _owner.CreateLocalLabel(_functionLabel + "_cmp_true");
                var doneLabel = _owner.CreateLocalLabel(_functionLabel + "_cmp_done");
                LoadImmediate(destination, 0, size);
                EmitConditionalJump(condition, trueLabel);
                EmitJump(doneLabel);
                _owner._text.DefineLabel(trueLabel);
                LoadImmediate(destination, 1, size);
                _owner._text.DefineLabel(doneLabel);
            }

            private void EmitFloatingComparisonResult(
                MachineRegister destination,
                MachineRegister left,
                MachineRegister right,
                ArmCondition condition,
                int size)
            {
                Emit(ArmInstruction.Binary(
                    ArmInstrKind.Fcmp,
                    Reg(ToArmRegister(left), size),
                    Reg(ToArmRegister(right), size)));
                var trueLabel = _owner.CreateLocalLabel(_functionLabel + "_fcmp_true");
                var doneLabel = _owner.CreateLocalLabel(_functionLabel + "_fcmp_done");
                LoadImmediate(destination, 0, _owner._target.RegisterSize);
                EmitConditionalJump(condition, trueLabel);
                EmitJump(doneLabel);
                _owner._text.DefineLabel(trueLabel);
                LoadImmediate(destination, 1, _owner._target.RegisterSize);
                _owner._text.DefineLabel(doneLabel);
            }

            private void EmitBooleanFromRegister(MachineRegister destination, MachineRegister source)
                => EmitComparisonResult(destination, source, Scratch4, ArmCondition.Ne, _owner._target.RegisterSize, rightIsZero: true);

            private void EmitAddressOf(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid addressof instruction.");
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                MaterializeAddress(instruction.Address, destination);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitLoad(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid load instruction.");
                RequireScalar(instruction.Result.Type, instruction);
                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(instruction.Result.Type, Scratch0, FpScratch0));
                var address = BuildAddress(instruction.Address, Scratch1, Scratch2);
                LoadFromMemory(destination, address.BaseRegister, address.Offset, SizeOf(instruction.Result.Type), IsSignedIntegerType(instruction.Result.Type));
                if (!IsFloatType(instruction.Result.Type))
                    NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null || instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Invalid store instruction.");
                RequireScalar(instruction.Address.ElementType, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);
                var source = LoadOperandAs(
                    instruction.Operands[0],
                    instruction.Address.ElementType,
                    PreferredScratch(instruction.Address.ElementType, Scratch0, FpScratch0),
                    instruction);
                var address = BuildAddress(instruction.Address, Scratch1, Scratch2);
                StoreToMemory(source, address.BaseRegister, address.Offset, Math.Min(RegisterSize(instruction.Address.ElementType), SizeOf(instruction.Address.ElementType)));
            }

            private void EmitZeroMemory(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid zeromem instruction.");
                var size = instruction.Operands.Length == 0 ? SizeOf(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
                MaterializeAddress(instruction.Address, Scratch0);
                ZeroMemory(Scratch0, size);
            }

            private void EmitCall(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Call has no callee operand.");

                var preservations = _allocation.GetCallPreservations(_currentInstructionPosition);
                SaveCallPreservations(preservations);
                _useCallPreservationSources = preservations.Length != 0;
                try
                {
                    var callee = instruction.Operands[0];
                    if (TryResolveWindowsImportCall(callee, out var importLabel))
                    {
                        MarshalCallArguments(instruction, 1);
                        _owner.AddExternalSymbol(importLabel, ArmObjectSymbolKind.Object);
                        MaterializeSymbolAddress(importLabel, Scratch0);
                        LoadFromMemory(Scratch0, Scratch0, 0, _owner._target.PointerSize, false);
                        Emit(ArmInstruction.Unary(_owner._machineTarget.Is64Bit ? ArmInstrKind.Blr : ArmInstrKind.Blx, Reg(ToArm(Scratch0), _owner._target.PointerSize)));
                    }
                    else if (TryResolveDirectCallLabel(callee, out var label))
                    {
                        MarshalCallArguments(instruction, 1);
                        EmitDirectCall(label);
                    }
                    else
                    {
                        var calleeSlot = checked(_backendTempOffset + (instruction.Operands.Length - 1) * _owner._allocationOptions.StackArgumentSlotSize);
                        var target = LoadOperand(callee, Scratch0);
                        StoreToMemory(target, calleeSlot, _owner._target.PointerSize);
                        MarshalCallArguments(instruction, 1);
                        LoadFromMemory(Scratch0, calleeSlot, _owner._target.PointerSize, false);
                        Emit(ArmInstruction.Unary(_owner._machineTarget.Is64Bit ? ArmInstrKind.Blr : ArmInstrKind.Blx, Reg(ToArm(Scratch0), _owner._target.PointerSize)));
                    }

                    ClearCallerClobberedIntegerRepresentationFacts();
                }
                finally
                {
                    _useCallPreservationSources = false;
                }

                EmitCallResult(instruction);
                RestoreCallPreservations(preservations);
            }

            private void SaveCallPreservations(ImmutableArray<CallPreservation> preservations)
            {
                foreach (var preservation in preservations)
                {
                    var size = RegisterSize(preservation.Register.Type);
                    if (preservation.UsesRegister)
                    {
                        MoveRegister(preservation.PreservationRegister, preservation.PhysicalRegister, size);
                        continue;
                    }
                    StoreToMemory(preservation.PhysicalRegister, preservation.StackOffset, size);
                }
            }

            private void RestoreCallPreservations(ImmutableArray<CallPreservation> preservations)
            {
                foreach (var preservation in preservations)
                {
                    var size = RegisterSize(preservation.Register.Type);
                    if (preservation.UsesRegister)
                    {
                        MoveRegister(preservation.PhysicalRegister, preservation.PreservationRegister, size);
                        continue;
                    }
                    LoadFromMemory(preservation.PhysicalRegister, preservation.StackOffset, size, IsSignedIntegerType(preservation.Register.Type));
                    if (!IsFloatType(preservation.Register.Type))
                        NormalizeIntegerRegister(preservation.PhysicalRegister, preservation.Register.Type);
                }
            }

            private void MarshalCallArguments(LirInstruction instruction, int startOperand)
            {
                var cursor = new AbiCursor();
                if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    throw Unsupported(instruction, "Hidden return buffers are not implemented for calls.");

                var locations = new List<(LirOperand Operand, AbiLocation Location, int ValueSize, int RegisterSize)>();
                for (var i = startOperand; i < instruction.Operands.Length; i++)
                {
                    var operand = instruction.Operands[i];
                    RequireScalar(operand.Type, instruction);
                    var argumentIndex = i - startOperand;
                    var isVariadicUnnamed = instruction.CallSignature is not null
                        && instruction.CallSignature.IsVariadic
                        && argumentIndex >= instruction.CallSignature.Parameters.Length;
                    var value = CAbi.ClassifyValue(
                        _owner._target,
                        operand.Type,
                        false,
                        isVariadicUnnamed,
                        instruction.CallSignature?.IsVariadic == true);
                    if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 ||
                        value.Segments[0].RegisterClass is not (AbiRegisterClass.General or AbiRegisterClass.Vector))
                        throw Unsupported(instruction, "Unsupported scalar ARM call argument class.");
                    var location = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    locations.Add((operand, location, Math.Max(1, value.Size), RegisterSize(operand.Type)));
                }

                var stagingBase = _backendTempOffset;

                for (var i = 0; i < locations.Count; i++)
                {
                    var source = LoadOperand(
                        locations[i].Operand,
                        PreferredScratch(locations[i].Operand.Type, Scratch0, FpScratch0));
                    StoreToMemory(source, stagingBase + i * _owner._allocationOptions.StackArgumentSlotSize, locations[i].RegisterSize);
                }

                for (var i = 0; i < locations.Count; i++)
                {
                    var item = locations[i];
                    var staged = PreferredScratch(item.Operand.Type, Scratch0, FpScratch0);
                    LoadFromMemory(staged, stagingBase + i * _owner._allocationOptions.StackArgumentSlotSize, item.RegisterSize, false);
                    if (item.Location.Kind == AbiLocationKind.Register)
                        MoveRegister(item.Location.Register, staged, item.RegisterSize);
                    else if (item.Location.Kind == AbiLocationKind.Stack)
                        StoreToMemory(
                            staged,
                            _allocation.Frame.OutgoingArgumentAreaOffset + item.Location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize),
                            Math.Min(item.ValueSize, item.RegisterSize));
                    else
                        throw Unsupported(instruction, "Invalid call argument ABI location.");
                }
            }

            private void EmitCallResult(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                RequireScalar(instruction.Result.Type, instruction);
                var value = CAbi.ClassifyValue(_owner._target, instruction.Result.Type, true, false);
                if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 ||
                    value.Segments[0].RegisterClass is not (AbiRegisterClass.General or AbiRegisterClass.Vector))
                    throw Unsupported(instruction, "Unsupported scalar ARM call result class.");
                var destination = GetWritableRegister(
                    instruction.Result,
                    PreferredScratch(instruction.Result.Type, Scratch0, FpScratch0));
                MoveRegister(destination, CAbi.ReturnRegister(value.Segments[0], 0), RegisterSize(instruction.Result.Type));
                if (!IsFloatType(instruction.Result.Type))
                    NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private bool TryResolveWindowsImportCall(LirOperand callee, out string label)
            {
                label = string.Empty;
                if (_owner._target.OperatingSystem != OperatingSystemKind.Windows ||
                    callee.Kind != LirOperandKind.Symbol ||
                    callee.Symbol is not FunctionSymbol function ||
                    _owner._functionLabels.ContainsKey(function) ||
                    _owner._functionLabelsByName.ContainsKey(function.Name))
                {
                    return false;
                }

                if (_owner._fileScopeLinkage.IsInternal(function))
                    throw new InvalidOperationException($"Undefined internal function '{function.Name}'.");

                label = $"__imp_{_owner.CreateExternalLabel(function.Name)}";
                return true;
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
                _owner.AddExternalFunctionSymbol(function);
                return true;
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 2 && IsComparisonOperator(instruction.Operator))
                {
                    EmitComparisonBranch(instruction);
                    return;
                }

                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand or two comparison operands.");
                RequireScalar(instruction.Operands[0].Type, instruction);
                var trueFallsThrough = IsFallthroughTarget(instruction.TrueTarget);
                var falseFallsThrough = IsFallthroughTarget(instruction.FalseTarget);
                if (IsFloatType(instruction.Operands[0].Type))
                {
                    var floatingCondition = LoadOperand(instruction.Operands[0], FpScratch0);
                    LoadFloatingImmediate(FpScratch1, 0.0, instruction.Operands[0].Type);
                    Emit(ArmInstruction.Binary(
                        ArmInstrKind.Fcmp,
                        Reg(ToArmRegister(floatingCondition), RegisterSize(instruction.Operands[0].Type)),
                        Reg(ToArmRegister(FpScratch1), RegisterSize(instruction.Operands[0].Type))));
                    if (trueFallsThrough && !falseFallsThrough)
                    {
                        EmitConditionalJump(ArmCondition.Eq, LabelOf(instruction.FalseTarget));
                        return;
                    }
                    EmitConditionalJump(ArmCondition.Ne, LabelOf(instruction.TrueTarget));
                    if (!falseFallsThrough)
                        EmitJump(LabelOf(instruction.FalseTarget));
                    return;
                }
                var integerCondition = LoadOperand(instruction.Operands[0], Scratch0);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(integerCondition), RegisterSize(instruction.Operands[0].Type)), ArmOperand.ImmediateOperand(0)));
                if (trueFallsThrough && !falseFallsThrough)
                {
                    EmitConditionalJump(ArmCondition.Eq, LabelOf(instruction.FalseTarget));
                    return;
                }
                EmitConditionalJump(ArmCondition.Ne, LabelOf(instruction.TrueTarget));
                if (!falseFallsThrough)
                    EmitJump(LabelOf(instruction.FalseTarget));
            }

            private void EmitComparisonBranch(LirInstruction instruction)
            {
                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                RequireScalar(left.Type, instruction);
                RequireScalar(right.Type, instruction);

                if (IsFloatType(left.Type) || IsFloatType(right.Type))
                {
                    if (!IsFloatType(left.Type) || !IsFloatType(right.Type) || RegisterSize(left.Type) != RegisterSize(right.Type))
                        throw Unsupported(instruction, "Mixed AArch64 floating-point comparison operands require prior conversion.");

                    var size = RegisterSize(left.Type);
                    var leftRegister = LoadOperand(left, FpScratch1);
                    if (leftRegister != FpScratch1)
                        MoveRegister(FpScratch1, leftRegister, size);
                    var rightRegister = LoadOperand(right, FpScratch2);
                    if (rightRegister != FpScratch2)
                        MoveRegister(FpScratch2, rightRegister, size);
                    Emit(ArmInstruction.Binary(
                        ArmInstrKind.Fcmp,
                        Reg(ToArmRegister(FpScratch1), size),
                        Reg(ToArmRegister(FpScratch2), size)));
                    EmitConditionalJump(SelectFloatingCondition(instruction.Operator), LabelOf(instruction.TrueTarget));
                }
                else
                {
                    var size = Math.Max(RegisterSize(left.Type), RegisterSize(right.Type));
                    var leftRegister = LoadOperand(left, Scratch1);
                    if (leftRegister != Scratch1)
                        MoveRegister(Scratch1, leftRegister, size);
                    var rightRegister = LoadOperand(right, Scratch2);
                    if (rightRegister != Scratch2)
                        MoveRegister(Scratch2, rightRegister, size);
                    Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                    EmitConditionalJump(SelectCondition(instruction.Operator, IsSignedIntegerType(left.Type)), LabelOf(instruction.TrueTarget));
                }

                if (!IsFallthroughTarget(instruction.FalseTarget))
                    EmitJump(LabelOf(instruction.FalseTarget));
            }

            private static bool IsComparisonOperator(string op)
                => op is "==" or "!=" or "<" or "<=" or ">" or ">=";

            private void EmitSwitch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Switch expects one key operand.");
                RequireScalar(instruction.Operands[0].Type, instruction);
                var size = RegisterSize(instruction.Operands[0].Type);
                var key = LoadOperand(instruction.Operands[0], Scratch0);
                if (key != Scratch0)
                    MoveRegister(Scratch0, key, size);
                foreach (var @case in instruction.SwitchCases)
                {
                    LoadImmediate(Scratch1, ImmediateToInt64(@case.Value), size);
                    Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch0), size), Reg(ToArm(Scratch1), size)));
                    EmitConditionalJump(ArmCondition.Eq, LabelOf(@case.Target));
                }
                if (!IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 0 && !IsVoid(instruction.Operands[0].Type))
                {
                    var operand = instruction.Operands[0];
                    RequireScalar(operand.Type, instruction);
                    var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
                    var value = CAbi.ClassifyValue(_owner._target, returnType, true, false);
                    if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 ||
                        value.Segments[0].RegisterClass is not (AbiRegisterClass.General or AbiRegisterClass.Vector))
                        throw Unsupported(instruction, "Unsupported scalar ARM return class.");
                    LoadOperandIntoAs(operand, CAbi.ReturnRegister(value.Segments[0], 0), returnType, instruction);
                }

                EmitEpilogue();
                Emit(ArmInstruction.Unary(
                    _owner._machineTarget.Is64Bit ? ArmInstrKind.Ret : ArmInstrKind.Bx,
                    Reg(_owner._machineTarget.Is64Bit ? ArmRegister.X30 : ArmRegister.Lr,
                    _owner._target.PointerSize)));
            }

            private void EmitEpilogue()
            {
                if (!_owner._machineTarget.Is64Bit)
                {
                    LoadRegister(Scratch3, _scratchSaveOffset + 12, 4);
                    LoadRegister(Scratch2, _scratchSaveOffset + 8, 4);
                    LoadRegister(Scratch1, _scratchSaveOffset + 4, 4);
                    LoadRegister(Scratch0, _scratchSaveOffset, 4);
                }
                if (_hasCalls)
                    LoadArmRegister(_owner._machineTarget.Is64Bit ? ArmRegister.X30 : ArmRegister.Lr, _lrSaveOffset, _owner._target.PointerSize);
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderByDescending(static p => p.Value))
                    LoadRegister(pair.Key, pair.Value, RegisterSaveSize(pair.Key));
                AdjustStack(_totalFrameSize);
            }

            private void EmitParallelCopy(LirInstruction instruction)
            {
                var copies = instruction.ParallelCopies.Where(copy => !ReferencesSamePhysicalStorage(copy.Source, copy.Destination)).ToArray();
                if (copies.Length == 0)
                    return;
                foreach (var copy in copies)
                {
                    RequireScalar(copy.Destination.Type, instruction);
                    RequireScalar(copy.Source.Type, instruction);
                }

                if (copies.Length == 1)
                {
                    var destination = GetWritableRegister(
                        copies[0].Destination,
                        PreferredScratch(copies[0].Destination.Type, Scratch0, FpScratch0));
                    LoadOperandIntoAs(copies[0].Source, destination, copies[0].Destination.Type, instruction);
                    StoreWritableRegisterIfSpilled(copies[0].Destination, destination);
                    return;
                }

                var cursor = 0;
                foreach (var copy in copies)
                {
                    var size = RegisterSize(copy.Destination.Type);
                    var source = LoadOperandAs(
                        copy.Source,
                        copy.Destination.Type,
                        PreferredScratch(copy.Destination.Type, Scratch0, FpScratch0),
                        instruction);
                    StoreToMemory(source, _backendTempOffset + cursor, size);
                    cursor += AlignUp(size, _owner._allocationOptions.SpillSlotAlignment);
                }

                cursor = 0;
                foreach (var copy in copies)
                {
                    var size = RegisterSize(copy.Destination.Type);
                    var destination = GetWritableRegister(
                        copy.Destination,
                        PreferredScratch(copy.Destination.Type, Scratch0, FpScratch0));
                    LoadFromMemory(destination, _backendTempOffset + cursor, size, IsSignedIntegerType(copy.Destination.Type));
                    if (!IsFloatType(copy.Destination.Type))
                        NormalizeIntegerRegister(destination, copy.Destination.Type);
                    StoreWritableRegisterIfSpilled(copy.Destination, destination);
                    cursor += AlignUp(size, _owner._allocationOptions.SpillSlotAlignment);
                }
            }

            private bool ReferencesSamePhysicalStorage(LirOperand source, LirVirtualRegister destination)
            {
                if (source.Kind != LirOperandKind.Register || source.Register is null)
                    return false;
                var sourceAllocation = _allocation[source.Register];
                var destinationAllocation = _allocation[destination];
                if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
                    return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;
                return sourceAllocation.IsSpilled && destinationAllocation.IsSpilled && sourceAllocation.StackOffset == destinationAllocation.StackOffset;
            }

            private MachineRegister LoadOperandAs(LirOperand operand, QualifiedType targetType, MachineRegister scratch, LirInstruction instruction)
            {
                RequireScalar(operand.Type, instruction);
                RequireScalar(targetType, instruction);

                if (IsFloatType(targetType))
                {
                    if (!IsVectorRegister(scratch))
                        throw Unsupported(instruction, "Floating-point conversion requires a vector scratch register.");
                    if (IsFloatType(operand.Type))
                    {
                        if (RegisterSize(operand.Type) != RegisterSize(targetType))
                            throw Unsupported(instruction, "AArch64 float/double precision conversion is not implemented.");
                        return LoadOperand(operand, scratch);
                    }

                    var integerSource = LoadOperand(operand, Scratch1);
                    Emit(ArmInstruction.Binary(
                        IsUnsignedIntegerType(operand.Type) || IsPointerLike(operand.Type) ? ArmInstrKind.Ucvtf : ArmInstrKind.Scvtf,
                        Reg(ToArmRegister(scratch), RegisterSize(targetType)),
                        Reg(ToArm(integerSource), RegisterSize(operand.Type))));
                    return scratch;
                }

                if (IsFloatType(operand.Type))
                {
                    if (IsVectorRegister(scratch))
                        throw Unsupported(instruction, "Floating-to-integer conversion requires a general scratch register.");
                    var floatingSource = LoadOperand(operand, FpScratch1);
                    Emit(ArmInstruction.Binary(
                        IsUnsignedIntegerType(targetType) || IsPointerLike(targetType) ? ArmInstrKind.Fcvtzu : ArmInstrKind.Fcvtzs,
                        Reg(ToArm(scratch), RegisterSize(targetType)),
                        Reg(ToArmRegister(floatingSource), RegisterSize(operand.Type))));
                    NormalizeIntegerRegister(scratch, targetType);
                    return scratch;
                }

                var source = LoadOperand(operand, scratch);
                if (!NeedsIntegerConversion(operand.Type, targetType) || IntegerRepresentationSatisfies(GetIntegerRepresentation(source), targetType))
                    return source;
                if (source != scratch)
                    MoveRegister(scratch, source, RegisterSize(targetType));
                NormalizeIntegerRegister(scratch, targetType);
                return scratch;
            }

            private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
            {
                var scratch = IsFloatType(targetType)
                    ? destination == FpScratch0 ? FpScratch1 : FpScratch0
                    : destination == Scratch0 ? Scratch1 : Scratch0;
                var source = LoadOperandAs(operand, targetType, scratch, instruction);
                MoveRegister(destination, source, RegisterSize(targetType));
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
                            LoadFloatingImmediate(preferred, operand.Immediate, operand.Type);
                            return preferred;
                        }
                        LoadImmediate(preferred, ConvertIntegerConstant(operand.Immediate), RegisterSize(operand.Type));
                        NormalizeIntegerRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        LoadFromMemory(preferred, offset, SizeOf(operand.Type), IsSignedIntegerType(operand.Type));
                        if (!IsFloatType(operand.Type))
                            NormalizeIntegerRegister(preferred, operand.Type);
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
                        if (IsFloatType(operand.Type))
                            LoadFloatingImmediate(preferred, 0.0, operand.Type);
                        else
                            LoadImmediate(preferred, 0, RegisterSize(operand.Type));
                        return preferred;
                    default:
                        throw new NotSupportedException($"Cannot load LIR operand kind {operand.Kind} into a register.");
                }
            }

            private MachineRegister LoadVirtualRegister(LirVirtualRegister register, MachineRegister preferred)
            {
                RequireScalar(register.Type, null);
                var allocation = _allocation[register];
                if (!allocation.IsSpilled)
                {
                    if (_useCallPreservationSources &&
                        _allocation.TryGetCallPreservation(_currentInstructionPosition, register, out var preservation))
                    {
                        if (preservation.UsesRegister)
                            return preservation.PreservationRegister;
                        LoadFromMemory(preferred, preservation.StackOffset, SizeOf(register.Type), IsSignedIntegerType(register.Type));
                        if (!IsFloatType(register.Type))
                            NormalizeIntegerRegister(preferred, register.Type);
                        return preferred;
                    }
                    return allocation.PhysicalRegister;
                }
                LoadFromMemory(preferred, allocation.StackOffset, SizeOf(register.Type), IsSignedIntegerType(register.Type));
                if (!IsFloatType(register.Type))
                    NormalizeIntegerRegister(preferred, register.Type);
                return preferred;
            }

            private MachineRegister GetWritableRegister(LirVirtualRegister register, MachineRegister scratch)
            {
                RequireScalar(register.Type, null);
                var allocation = _allocation[register];
                return allocation.IsSpilled ? scratch : allocation.PhysicalRegister;
            }

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, MachineRegister source)
            {
                var allocation = _allocation[register];
                if (allocation.IsSpilled)
                    StoreToMemory(source, allocation.StackOffset, Math.Min(RegisterSize(register.Type), SizeOf(register.Type)));
            }

            private void LoadFloatingImmediate(MachineRegister destination, object? value, QualifiedType type)
            {
                RequireScalar(type, null);
                if (!IsVectorRegister(destination))
                    throw new NotSupportedException("Floating-point values require an AArch64 vector register.");

                if (IsFloat32(type))
                {
                    var raw = BitConverter.SingleToInt32Bits(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    LoadImmediate(Scratch0, raw, 4);
                    MoveRegister(destination, Scratch0, 4);
                    return;
                }

                var raw64 = BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                LoadImmediate(Scratch0, raw64, 8);
                MoveRegister(destination, Scratch0, 8);
            }

            private void MaterializeAddress(LirAddress address, MachineRegister destination)
            {
                var parts = BuildAddress(address, destination, destination == Scratch2 ? Scratch3 : Scratch2);
                if (parts.BaseRegister != destination || parts.Offset != 0)
                    AddImmediate(destination, parts.BaseRegister, parts.Offset);
            }

            private AddressParts BuildAddress(LirAddress address, MachineRegister scratchBase, MachineRegister scratchIndex)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        if (address.StackSlot is null || !_allocation.Frame.StackSlotOffsets.TryGetValue(address.StackSlot, out var stackOffset))
                            throw new InvalidOperationException("Missing stack slot offset.");
                        return new AddressParts(StackPointer, stackOffset);
                    case LirAddressKind.Symbol:
                        if (address.Symbol is null)
                            throw new InvalidOperationException("Symbol address has no symbol.");
                        MaterializeSymbolAddress(_owner.GetSymbolLabel(address.Symbol), scratchBase);
                        return new AddressParts(scratchBase, 0);
                    case LirAddressKind.Indirect:
                        if (address.BaseOperand is null)
                            throw new InvalidOperationException("Indirect address has no base operand.");
                        return new AddressParts(LoadOperand(address.BaseOperand, scratchBase), 0);
                    case LirAddressKind.Field:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Field address has no base address.");
                        var fieldBase = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        return new AddressParts(fieldBase.BaseRegister, checked(fieldBase.Offset + address.Displacement));
                    case LirAddressKind.Element:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Element address has no base address.");
                        var elementBase = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        if (address.Index is null)
                            return elementBase;
                        var index = LoadOperand(address.Index, scratchIndex);
                        if (index != scratchIndex)
                            MoveRegister(scratchIndex, index, _owner._target.RegisterSize);
                        var scaleScratch = SelectAddressScratch(ToArm(scratchIndex), ToArm(scratchBase));
                        ScaleIndex(scratchIndex, Math.Max(1, address.Scale), scaleScratch);
                        if (elementBase.Offset != 0)
                        {
                            AddImmediate(scratchBase, elementBase.BaseRegister, elementBase.Offset);
                            elementBase = new AddressParts(scratchBase, 0);
                        }
                        InvalidateIntegerRepresentation(scratchBase);
                        Emit(ArmInstruction.Ternary(
                            ArmInstrKind.Add,
                            Reg(ToArm(scratchBase), _owner._target.PointerSize),
                            Reg(ToArm(elementBase.BaseRegister),
                            _owner._target.PointerSize),
                            Reg(ToArm(scratchIndex),
                            _owner._target.PointerSize)));
                        return new AddressParts(scratchBase, 0);
                    default:
                        throw new NotSupportedException($"Unsupported LIR address kind {address.Kind}.");
                }
            }

            private void ScaleIndex(MachineRegister index, int scale, MachineRegister scratch)
            {
                if (scale == 1)
                    return;
                if (IsPowerOfTwo(scale))
                {
                    InvalidateIntegerRepresentation(index);
                    Emit(ArmInstruction.Ternary(
                        ArmInstrKind.Lsl,
                        Reg(ToArm(index),
                        _owner._target.PointerSize),
                        Reg(ToArm(index),
                        _owner._target.PointerSize),
                        ArmOperand.ImmediateOperand(Log2(scale))));
                    return;
                }
                LoadImmediate(scratch, scale, _owner._target.PointerSize);
                InvalidateIntegerRepresentation(index);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Mul,
                    Reg(ToArm(index),
                    _owner._target.PointerSize),
                    Reg(ToArm(index),
                    _owner._target.PointerSize),
                    Reg(ToArm(scratch),
                    _owner._target.PointerSize)));
            }

            private void CopyMemory(MachineRegister destination, MachineRegister source, int size)
            {
                if (size <= 0 || destination == source)
                    return;

                const int inlineThreshold = 64;
                if (size <= inlineThreshold)
                {
                    EmitInlineMemoryCopy(destination, source, size);
                    return;
                }

                PrepareBlockCopyPointers(destination, source);
                LoadImmediate(Scratch1, size, _owner._target.RegisterSize);
                LoadImmediate(Scratch0, _owner._target.RegisterSize - 1, _owner._target.RegisterSize);

                var byteLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_byte_loop");
                var alignLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_align_loop");
                var wordLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_word_loop");
                var done = _owner.CreateLocalLabel(_functionLabel + "_memcpy_done");
                var registerSize = _owner._target.RegisterSize;

                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Eor,
                    Reg(ToArm(Scratch2), registerSize),
                    Reg(ToArm(Scratch3), registerSize),
                    Reg(ToArm(Scratch4), registerSize)));
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.And,
                    Reg(ToArm(Scratch2), registerSize),
                    Reg(ToArm(Scratch2), registerSize),
                    Reg(ToArm(Scratch0), registerSize)));
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch2), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, byteLoop);

                _owner._text.DefineLabel(alignLoop);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.And,
                    Reg(ToArm(Scratch2), registerSize),
                    Reg(ToArm(Scratch3), registerSize),
                    Reg(ToArm(Scratch0), registerSize)));
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch2), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Eq, wordLoop);
                LoadFromMemory(Scratch2, Scratch4, 0, 1, false);
                StoreToMemory(Scratch2, Scratch3, 0, 1);
                AddImmediate(Scratch4, Scratch4, 1);
                AddImmediate(Scratch3, Scratch3, 1);
                AddImmediate(Scratch1, Scratch1, -1);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, alignLoop);
                EmitJump(done);

                _owner._text.DefineLabel(wordLoop);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(registerSize)));
                EmitConditionalJump(ArmCondition.Lo, byteLoop);
                LoadFromMemory(Scratch2, Scratch4, 0, registerSize, false);
                StoreToMemory(Scratch2, Scratch3, 0, registerSize);
                AddImmediate(Scratch4, Scratch4, registerSize);
                AddImmediate(Scratch3, Scratch3, registerSize);
                AddImmediate(Scratch1, Scratch1, -registerSize);
                EmitJump(wordLoop);

                _owner._text.DefineLabel(byteLoop);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Eq, done);
                LoadFromMemory(Scratch2, Scratch4, 0, 1, false);
                StoreToMemory(Scratch2, Scratch3, 0, 1);
                AddImmediate(Scratch4, Scratch4, 1);
                AddImmediate(Scratch3, Scratch3, 1);
                AddImmediate(Scratch1, Scratch1, -1);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, byteLoop);

                _owner._text.DefineLabel(done);
            }

            private void PrepareBlockCopyPointers(MachineRegister destination, MachineRegister source)
            {
                if (destination == Scratch4 && source == Scratch3)
                {
                    MoveRegister(Scratch2, Scratch3, _owner._target.RegisterSize);
                    MoveRegister(Scratch3, Scratch4, _owner._target.RegisterSize);
                    MoveRegister(Scratch4, Scratch2, _owner._target.RegisterSize);
                    return;
                }

                if (source == Scratch3)
                {
                    MoveRegister(Scratch4, source, _owner._target.RegisterSize);
                    MoveRegister(Scratch3, destination, _owner._target.RegisterSize);
                    return;
                }

                MoveRegister(Scratch3, destination, _owner._target.RegisterSize);
                MoveRegister(Scratch4, source, _owner._target.RegisterSize);
            }

            private void EmitInlineMemoryCopy(MachineRegister destination, MachineRegister source, int size)
            {
                var offset = 0;
                while (size - offset >= _owner._target.RegisterSize)
                {
                    LoadFromMemory(Scratch2, source, offset, _owner._target.RegisterSize, false);
                    StoreToMemory(Scratch2, destination, offset, _owner._target.RegisterSize);
                    offset += _owner._target.RegisterSize;
                }
                while (size - offset >= 4)
                {
                    LoadFromMemory(Scratch2, source, offset, 4, false);
                    StoreToMemory(Scratch2, destination, offset, 4);
                    offset += 4;
                }
                while (size - offset >= 2)
                {
                    LoadFromMemory(Scratch2, source, offset, 2, false);
                    StoreToMemory(Scratch2, destination, offset, 2);
                    offset += 2;
                }
                while (offset < size)
                {
                    LoadFromMemory(Scratch2, source, offset, 1, false);
                    StoreToMemory(Scratch2, destination, offset, 1);
                    offset++;
                }
            }

            private void ZeroMemory(MachineRegister destination, int size)
            {
                if (size <= 0)
                    return;

                const int inlineThreshold = 64;
                if (size <= inlineThreshold)
                {
                    EmitInlineZeroMemory(destination, size);
                    return;
                }

                var pointer = destination == Scratch3 ? Scratch4 : Scratch3;
                MoveRegister(pointer, destination, _owner._target.RegisterSize);
                LoadImmediate(Scratch1, size, _owner._target.RegisterSize);
                LoadImmediate(Scratch0, _owner._target.RegisterSize - 1, _owner._target.RegisterSize);
                var registerSize = _owner._target.RegisterSize;

                var alignLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_align_loop");
                var wordLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_word_loop");
                var byteLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_byte_loop");
                var done = _owner.CreateLocalLabel(_functionLabel + "_memzero_done");

                _owner._text.DefineLabel(alignLoop);
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.And,
                    Reg(ToArm(Scratch2), registerSize),
                    Reg(ToArm(pointer), registerSize),
                    Reg(ToArm(Scratch0), registerSize)));
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch2), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Eq, wordLoop);
                LoadImmediate(Scratch2, 0, registerSize);
                StoreToMemory(Scratch2, pointer, 0, 1);
                AddImmediate(pointer, pointer, 1);
                AddImmediate(Scratch1, Scratch1, -1);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, alignLoop);
                EmitJump(done);

                _owner._text.DefineLabel(wordLoop);
                LoadImmediate(Scratch0, 0, registerSize);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(registerSize)));
                EmitConditionalJump(ArmCondition.Lo, byteLoop);
                StoreToMemory(Scratch0, pointer, 0, registerSize);
                AddImmediate(pointer, pointer, registerSize);
                AddImmediate(Scratch1, Scratch1, -registerSize);
                EmitJump(wordLoop);

                _owner._text.DefineLabel(byteLoop);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Eq, done);
                StoreToMemory(Scratch0, pointer, 0, 1);
                AddImmediate(pointer, pointer, 1);
                AddImmediate(Scratch1, Scratch1, -1);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(Scratch1), registerSize), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, byteLoop);

                _owner._text.DefineLabel(done);
            }

            private void EmitInlineZeroMemory(MachineRegister destination, int size)
            {
                LoadImmediate(Scratch1, 0, _owner._target.RegisterSize);
                var offset = 0;
                while (size - offset >= _owner._target.RegisterSize)
                {
                    StoreToMemory(Scratch1, destination, offset, _owner._target.RegisterSize);
                    offset += _owner._target.RegisterSize;
                }
                while (size - offset >= 4)
                {
                    StoreToMemory(Scratch1, destination, offset, 4);
                    offset += 4;
                }
                while (size - offset >= 2)
                {
                    StoreToMemory(Scratch1, destination, offset, 2);
                    offset += 2;
                }
                while (offset < size)
                {
                    StoreToMemory(Scratch1, destination, offset, 1);
                    offset++;
                }
            }

            private void StoreRegister(MachineRegister source, int offset, int size)
                => StoreArmRegister(ToArmRegister(source), offset, size);

            private void LoadRegister(MachineRegister destination, int offset, int size)
                => LoadArmRegister(ToArmRegister(destination), offset, size);

            private void StoreArmRegister(ArmRegister source, int offset, int size)
                => EmitMemoryStore(source, StackPointerArm, offset, size);

            private void LoadArmRegister(ArmRegister destination, int offset, int size)
                => EmitMemoryLoad(destination, StackPointerArm, offset, size, false);

            private void LoadFromMemory(MachineRegister destination, int offset, int size, bool signed)
            {
                EmitMemoryLoad(ToArmRegister(destination), StackPointerArm, offset, size, signed);
                if (!IsVectorRegister(destination))
                    SetIntegerRepresentation(destination, RepresentationForLoad(size, signed));
            }

            private void LoadFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size, bool signed)
            {
                EmitMemoryLoad(ToArmRegister(destination), ToArmRegister(baseRegister), offset, size, signed);
                if (!IsVectorRegister(destination))
                    SetIntegerRepresentation(destination, RepresentationForLoad(size, signed));
            }

            private void StoreToMemory(MachineRegister source, int offset, int size)
                => EmitMemoryStore(ToArmRegister(source), StackPointerArm, offset, size);

            private void StoreToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => EmitMemoryStore(ToArmRegister(source), ToArmRegister(baseRegister), offset, size);

            private void EmitMemoryLoad(ArmRegister destination, ArmRegister baseRegister, int offset, int size, bool signed)
            {
                if (CanEncodeMemoryOffset(offset, size, signed))
                {
                    Emit(ArmInstruction.Binary(
                        LoadOpcode(size, signed),
                        Reg(destination, ArmRegisters.IsVector(destination) ? size : RegisterOperandSize(size, signed)),
                        Mem(baseRegister, offset, size)));
                    return;
                }

                var addressScratch = SelectAddressScratch(baseRegister, ArmRegister.Invalid);
                AddImmediate(addressScratch, FromArm(baseRegister), offset);
                Emit(ArmInstruction.Binary(
                    LoadOpcode(size, signed),
                    Reg(destination, ArmRegisters.IsVector(destination) ? size : RegisterOperandSize(size, signed)),
                    Mem(ToArm(addressScratch), 0, size)));
            }

            private void EmitMemoryStore(ArmRegister source, ArmRegister baseRegister, int offset, int size)
            {
                if (CanEncodeMemoryOffset(offset, size))
                {
                    Emit(ArmInstruction.Binary(StoreOpcode(size), Reg(source, size), Mem(baseRegister, offset, size)));
                    return;
                }

                var addressScratch = SelectAddressScratch(baseRegister, source);
                AddImmediate(addressScratch, FromArm(baseRegister), offset);
                Emit(ArmInstruction.Binary(StoreOpcode(size), Reg(source, size), Mem(ToArm(addressScratch), 0, size)));
            }

            private MachineRegister SelectAddressScratch(ArmRegister firstAvoid, ArmRegister secondAvoid)
            {
                var candidates = new[] { Scratch4, Scratch3, Scratch2, Scratch1, Scratch0 };
                foreach (var candidate in candidates)
                {
                    var arm = ToArm(candidate);
                    if (arm != firstAvoid && arm != secondAvoid)
                        return candidate;
                }
                throw new InvalidOperationException("No ARM address scratch register is available.");
            }

            private bool CanEncodeMemoryOffset(int offset, int size, bool signed = false)
            {
                if (_owner._machineTarget.Is64Bit)
                    return offset >= -256 && offset <= 255 || offset >= 0 && offset % size == 0 && offset / size <= 4095;
                return size switch
                {
                    1 when signed => offset >= -255 && offset <= 255,
                    1 or 4 => offset >= -4095 && offset <= 4095,
                    2 => offset >= -255 && offset <= 255,
                    _ => false,
                };
            }

            private static ArmInstrKind LoadOpcode(int size, bool signed)
            {
                return size switch
                {
                    1 => signed ? ArmInstrKind.Ldrsb : ArmInstrKind.Ldrb,
                    2 => signed ? ArmInstrKind.Ldrsh : ArmInstrKind.Ldrh,
                    4 or 8 or 16 => ArmInstrKind.Ldr,
                    _ => throw new NotSupportedException($"Unsupported scalar load size {size}."),
                };
            }

            private static ArmInstrKind StoreOpcode(int size)
            {
                return size switch
                {
                    1 => ArmInstrKind.Strb,
                    2 => ArmInstrKind.Strh,
                    4 or 8 or 16 => ArmInstrKind.Str,
                    _ => throw new NotSupportedException($"Unsupported scalar store size {size}."),
                };
            }

            private int RegisterOperandSize(int memorySize, bool signed)
            {
                if (_owner._machineTarget.Is64Bit && signed && memorySize < 4)
                    return _owner._target.RegisterSize;
                return memorySize == 8 ? 8 : 4;
            }

            private void LoadImmediate(MachineRegister destination, long value, int size)
            {
                _owner.EmitLoadImmediate(ToArm(destination), value, size);
                SetIntegerRepresentation(destination, RepresentationForImmediate(value, size));
            }

            private void MaterializeSymbolAddress(string symbol, MachineRegister destination)
            {
                _owner.MaterializeSymbolAddress(symbol, ToArm(destination));
                InvalidateIntegerRepresentation(destination);
            }

            private void AddImmediate(MachineRegister destination, MachineRegister source, int immediate)
            {
                var sourceRepresentation = GetIntegerRepresentation(source);
                if (_owner._machineTarget.Is64Bit)
                {
                    _owner.EmitAddImmediate(ToArm(destination), ToArm(source), immediate, _owner._target.PointerSize);
                    SetIntegerRepresentation(destination, immediate == 0 ? sourceRepresentation : IntegerRepresentationFact.Unknown);
                    return;
                }

                InvalidateIntegerRepresentation(destination);
                var magnitude = Math.Abs((long)immediate);
                if (magnitude <= 255)
                {
                    _owner.EmitAddImmediate(ToArm(destination), ToArm(source), immediate, _owner._target.PointerSize);
                    return;
                }

                var temporary = SelectAddressScratch(ToArm(destination), ToArm(source));
                LoadImmediate(temporary, magnitude, _owner._target.PointerSize);
                Emit(ArmInstruction.Ternary(
                    immediate < 0 ? ArmInstrKind.Sub : ArmInstrKind.Add,
                    Reg(ToArm(destination), _owner._target.PointerSize),
                    Reg(ToArm(source), _owner._target.PointerSize),
                    Reg(ToArm(temporary), _owner._target.PointerSize)));
            }

            private void AdjustStack(int delta)
            {
                if (delta == 0)
                    return;
                _owner.EmitAddImmediate(StackPointerArm, StackPointerArm, delta, _owner._target.PointerSize);
            }

            private void MoveRegister(MachineRegister destination, MachineRegister source, int size)
            {
                if (destination == source)
                    return;
                if (IsVectorRegister(destination) || IsVectorRegister(source))
                {
                    if (!_owner._machineTarget.Is64Bit)
                        throw new NotSupportedException("AArch32 scalar vector moves are not implemented.");
                    Emit(ArmInstruction.Binary(
                        ArmInstrKind.Fmov,
                        Reg(ToArmRegister(destination), size),
                        Reg(ToArmRegister(source), size)));
                    if (!IsVectorRegister(destination))
                        InvalidateIntegerRepresentation(destination);
                    return;
                }
                var moveSize = _owner._machineTarget.Is64Bit ? _owner._target.RegisterSize : size;
                Emit(ArmInstruction.Binary(ArmInstrKind.Mov, Reg(ToArm(destination), moveSize), Reg(ToArm(source), moveSize)));
                SetIntegerRepresentation(destination, GetIntegerRepresentation(source));
            }

            private void NormalizeIntegerRegister(MachineRegister register, QualifiedType type)
            {
                var required = CanonicalIntegerRepresentation(type);
                if (!required.IsKnown || IntegerRepresentationSatisfies(GetIntegerRepresentation(register), required))
                    return;

                var registerBits = _owner._target.RegisterSize * 8;
                var shift = registerBits - required.Bits;
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Lsl,
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    ArmOperand.ImmediateOperand(shift)));
                Emit(ArmInstruction.Ternary(
                    required.Kind == IntegerRepresentationKind.ZeroExtended ? ArmInstrKind.Lsr : ArmInstrKind.Asr,
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    ArmOperand.ImmediateOperand(shift)));
                SetIntegerRepresentation(register, required);
            }

            private void EmitDirectCall(string label)
                => _owner.EmitDirectCall(label);

            private void EmitJump(string label)
            {
                var offset = _owner._text.ByteLength;
                Emit(ArmInstruction.Branch(ArmInstrKind.B, label));
                _owner._text.AddRelocation(
                    offset,
                    label,
                    0,
                    _owner._machineTarget.Is64Bit ? ArmObjectRelocationKind.AArch64Branch26 : ArmObjectRelocationKind.ArmBranch24);
            }

            private void EmitConditionalJump(ArmCondition condition, string label)
            {
                var offset = _owner._text.ByteLength;
                Emit(ArmInstruction.Branch(ArmInstrKind.B, label, condition));
                _owner._text.AddRelocation(
                    offset,
                    label,
                    0,
                    _owner._machineTarget.Is64Bit ? ArmObjectRelocationKind.AArch64ConditionalBranch19 : ArmObjectRelocationKind.ArmBranch24);
            }

            private void Emit(ArmInstruction instruction)
                => _owner._text.Emit(instruction);

            private string LabelOf(LirBlock? block)
            {
                if (block is null || !_labels.TryGetValue(block, out var label))
                    throw new InvalidOperationException("Missing label for LIR block.");
                return label;
            }

            private bool IsFallthroughTarget(LirBlock? target)
                => target is not null && ReferenceEquals(target, _fallthroughBlock);

            private int IncomingStackOffset(int abiStackOffset)
                => checked(_totalFrameSize + abiStackOffset);

            private int SizeOf(QualifiedType type)
                => Math.Max(1, _owner._target.SizeOf(type));

            private int RegisterSize(QualifiedType type)
            {
                var size = SizeOf(type);
                if (size > _owner._target.RegisterSize)
                    throw new NotSupportedException("Scalar value is wider than an ARM machine register.");
                if (IsFloatType(type))
                    return size;
                return _owner._machineTarget.Is64Bit && size > 4 ? 8 : 4;
            }

            private bool IsSupportedFloatingScalar(QualifiedType type)
                => _owner._machineTarget.Is64Bit && (IsFloat32(type) || IsFloat64(type));

            private MachineRegister PreferredScratch(QualifiedType type, MachineRegister general, MachineRegister floating)
                => IsFloatType(type) ? floating : general;

            private int PointerScale(QualifiedType type)
            {
                if (type.Type is PointerType pointer)
                    return Math.Max(1, _owner._target.SizeOf(pointer.PointeeType));
                if (type.Type is ArrayType array)
                    return Math.Max(1, _owner._target.SizeOf(array.ElementType));
                return 1;
            }

            private int RegisterSaveSize(MachineRegister register)
                => TargetRegisterInfo.RegisterSaveSize(_owner._target, register, _owner._allocationOptions.SpillSlotSize);

            private void RequireScalar(QualifiedType type, LirInstruction? instruction)
            {
                var supportedFloat = IsSupportedFloatingScalar(type);
                if ((IsFloatType(type) && !supportedFloat) || IsAggregateType(type) || SizeOf(type) > _owner._target.RegisterSize)
                {
                    if (instruction is null)
                        throw new NotSupportedException("The ARM backend supports integer and pointer scalars no wider than one machine register and AArch64 float/double scalars.");
                    throw Unsupported(instruction, "The ARM backend supports integer and pointer scalars no wider than one machine register and AArch64 float/double scalars.");
                }
            }

            private void RecordIntegerRegisterWrite(MachineRegister register, int size)
            {
                if (GetIntegerRepresentation(register).IsKnown)
                    return;
                if (_owner._machineTarget.Is64Bit && size <= 4)
                    SetIntegerRepresentation(register, IntegerRepresentationFact.ZeroExtended(32));
            }

            private IntegerRepresentationFact CanonicalIntegerRepresentation(QualifiedType type)
            {
                if (!IsIntegerLike(type) || IsPointerLike(type))
                    return IntegerRepresentationFact.Unknown;
                var registerBits = _owner._target.RegisterSize * 8;
                var bits = Math.Min(registerBits, SizeOf(type) * 8);
                if (bits >= registerBits)
                    return IntegerRepresentationFact.Unknown;
                return IsUnsignedIntegerType(type)
                    ? IntegerRepresentationFact.ZeroExtended(bits)
                    : IntegerRepresentationFact.SignExtended(bits);
            }

            private bool IntegerRepresentationSatisfies(IntegerRepresentationFact actual, QualifiedType type)
            {
                var required = CanonicalIntegerRepresentation(type);
                return !required.IsKnown || IntegerRepresentationSatisfies(actual, required);
            }

            private static bool IntegerRepresentationSatisfies(IntegerRepresentationFact actual, IntegerRepresentationFact required)
            {
                if (!required.IsKnown)
                    return true;
                if (!actual.IsKnown)
                    return false;
                if (required.Kind == IntegerRepresentationKind.ZeroExtended)
                    return actual.Kind == IntegerRepresentationKind.ZeroExtended && actual.Bits <= required.Bits;
                if (actual.Kind == IntegerRepresentationKind.SignExtended)
                    return actual.Bits <= required.Bits;
                return actual.Kind == IntegerRepresentationKind.ZeroExtended && actual.Bits < required.Bits;
            }

            private IntegerRepresentationFact GetIntegerRepresentation(MachineRegister register)
            {
                var index = (int)register - (int)MachineRegister.X0;
                return index >= 0 && index < _integerRepresentationFacts.Length
                    ? _integerRepresentationFacts[index]
                    : IntegerRepresentationFact.Unknown;
            }

            private void SetIntegerRepresentation(MachineRegister register, IntegerRepresentationFact fact)
            {
                var index = (int)register - (int)MachineRegister.X0;
                if (index >= 0 && index < _integerRepresentationFacts.Length)
                    _integerRepresentationFacts[index] = fact;
            }

            private void InvalidateIntegerRepresentation(MachineRegister register)
                => SetIntegerRepresentation(register, IntegerRepresentationFact.Unknown);

            private void ClearIntegerRepresentationFacts()
                => Array.Clear(_integerRepresentationFacts, 0, _integerRepresentationFacts.Length);

            private void ClearCallerClobberedIntegerRepresentationFacts()
            {
                if (_owner._machineTarget.Is64Bit)
                {
                    for (var i = 0; i <= 18; i++)
                        _integerRepresentationFacts[i] = IntegerRepresentationFact.Unknown;
                    return;
                }

                for (var i = 0; i <= 3; i++)
                    _integerRepresentationFacts[i] = IntegerRepresentationFact.Unknown;
                _integerRepresentationFacts[12] = IntegerRepresentationFact.Unknown;
            }

            private IntegerRepresentationFact RepresentationForLoad(int size, bool signed)
            {
                var registerBits = _owner._target.RegisterSize * 8;
                var bits = Math.Min(registerBits, Math.Max(1, size) * 8);
                if (bits >= registerBits)
                    return IntegerRepresentationFact.Unknown;
                if (_owner._machineTarget.Is64Bit && size == 4)
                    return IntegerRepresentationFact.ZeroExtended(32);
                return signed ? IntegerRepresentationFact.SignExtended(bits) : IntegerRepresentationFact.ZeroExtended(bits);
            }

            private IntegerRepresentationFact RepresentationForImmediate(long value, int size)
            {
                if (_owner._machineTarget.Is64Bit && size <= 4)
                    return RepresentationForUnsignedConstant(unchecked((uint)value));
                if (!_owner._machineTarget.Is64Bit)
                    value = unchecked((int)(uint)value);
                return RepresentationForConstant(value);
            }

            private static IntegerRepresentationFact RepresentationForUnsignedConstant(uint value)
            {
                if (value <= 1)
                    return IntegerRepresentationFact.ZeroExtended(1);
                if (value <= byte.MaxValue)
                    return IntegerRepresentationFact.ZeroExtended(8);
                if (value <= ushort.MaxValue)
                    return IntegerRepresentationFact.ZeroExtended(16);
                return IntegerRepresentationFact.ZeroExtended(32);
            }

            private static IntegerRepresentationFact RepresentationForConstant(long value)
            {
                if (value >= 0)
                {
                    if (value <= 1)
                        return IntegerRepresentationFact.ZeroExtended(1);
                    if (value <= byte.MaxValue)
                        return IntegerRepresentationFact.ZeroExtended(8);
                    if (value <= ushort.MaxValue)
                        return IntegerRepresentationFact.ZeroExtended(16);
                    if ((ulong)value <= uint.MaxValue)
                        return IntegerRepresentationFact.ZeroExtended(32);
                    return IntegerRepresentationFact.Unknown;
                }

                if (value >= sbyte.MinValue)
                    return IntegerRepresentationFact.SignExtended(8);
                if (value >= short.MinValue)
                    return IntegerRepresentationFact.SignExtended(16);
                if (value >= int.MinValue)
                    return IntegerRepresentationFact.SignExtended(32);
                return IntegerRepresentationFact.Unknown;
            }

            private bool NeedsIntegerConversion(QualifiedType source, QualifiedType destination)
            {
                if ((!IsIntegerLike(source) && !IsPointerLike(source)) || (!IsIntegerLike(destination) && !IsPointerLike(destination)))
                    return false;
                if (IsPointerLike(destination))
                    return false;

                var required = CanonicalIntegerRepresentation(destination);
                if (!required.IsKnown)
                    return false;
                return !IntegerRepresentationSatisfies(CanonicalIntegerRepresentation(source), required);
            }

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

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

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

            private MachineRegister StackPointer => _owner._machineTarget.Is64Bit ? MachineRegister.X31 : MachineRegister.X13;

            private ArmRegister StackPointerArm => _owner._machineTarget.Is64Bit ? ArmRegister.Sp : ArmRegister.Sp32;

            private static bool IsVectorRegister(MachineRegister register)
                => register >= MachineRegister.V0 && register <= MachineRegister.V31;

            private ArmRegister ToArmRegister(MachineRegister register)
            {
                if (IsVectorRegister(register))
                {
                    if (!_owner._machineTarget.Is64Bit)
                        throw new NotSupportedException("AArch32 vector register mapping is not implemented.");
                    return (ArmRegister)((int)ArmRegister.V0 + ((int)register - (int)MachineRegister.V0));
                }
                return ToArm(register);
            }

            private ArmRegister ToArm(MachineRegister register)
            {
                if (register < MachineRegister.X0 || register > MachineRegister.X31)
                    throw new NotSupportedException("Expected a general-purpose machine register.");
                var index = (int)register - (int)MachineRegister.X0;
                if (_owner._machineTarget.Is64Bit)
                    return index == 31 ? ArmRegister.Sp : (ArmRegister)((int)ArmRegister.X0 + index);
                if (index > 15)
                    throw new NotSupportedException("AArch32 register index is out of range.");
                return (ArmRegister)((int)ArmRegister.R0 + index);
            }

            private MachineRegister FromArm(ArmRegister register)
            {
                if (_owner._machineTarget.Is64Bit)
                {
                    if (register == ArmRegister.Sp)
                        return MachineRegister.X31;
                    if (register >= ArmRegister.X0 && register <= ArmRegister.X30)
                        return (MachineRegister)((int)MachineRegister.X0 + ((int)register - (int)ArmRegister.X0));
                }
                else if (register >= ArmRegister.R0 && register <= ArmRegister.R15)
                {
                    return (MachineRegister)((int)MachineRegister.X0 + ((int)register - (int)ArmRegister.R0));
                }
                throw new NotSupportedException("Unsupported ARM register mapping.");
            }

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException($"{message} Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");

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
            private readonly List<ArmInstruction> _instructions = new List<ArmInstruction>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<ArmObjectRelocation> _relocations = new List<ArmObjectRelocation>();

            public string Name { get; }
            public int ByteLength => checked(_instructions.Count * 4);

            public TextSectionBuilder(string name)
            {
                Name = name;
            }

            public void DefineLabel(string label)
            {
                if (!_labels.ContainsKey(label))
                    _labels.Add(label, ByteLength);
            }

            public void Emit(ArmInstruction instruction)
                => _instructions.Add(instruction);

            public void EmitAssembly(string text, string labelPrefix, ArmTarget target)
            {
                var program = ArmAssembler.Assemble(text ?? string.Empty, target);
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
                    Emit(RewriteInlineLabelReferences(instruction, renamedLabels));
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

            private static ArmInstruction RewriteInlineLabelReferences(
                ArmInstruction instruction,
                IReadOnlyDictionary<string, string> renamedLabels)
                => instruction
                    .WithOperand0(RewriteInlineLabelOperand(instruction.Operand0, renamedLabels))
                    .WithOperand1(RewriteInlineLabelOperand(instruction.Operand1, renamedLabels))
                    .WithOperand2(RewriteInlineLabelOperand(instruction.Operand2, renamedLabels))
                    .WithOperand3(RewriteInlineLabelOperand(instruction.Operand3, renamedLabels));

            private static ArmOperand RewriteInlineLabelOperand(
                ArmOperand operand,
                IReadOnlyDictionary<string, string> renamedLabels)
                => operand.Symbol is not null && renamedLabels.TryGetValue(operand.Symbol, out var replacement)
                    ? operand.WithSymbol(replacement, operand.RelocationKind, operand.Addend)
                    : operand;

            public void AddRelocation(int offset, string symbol, long addend, ArmObjectRelocationKind kind)
                => _relocations.Add(new ArmObjectRelocation(Name, offset, symbol, addend, kind));

            public void RelaxBranches(List<ArmObjectSymbol> symbols, ArmTarget target)
            {
                while (true)
                {
                    var relaxations = new BranchRelaxationKind[_instructions.Count];
                    var extraInstructions = new int[_instructions.Count];
                    var relaxationCount = 0;
                    for (var i = 0; i < _instructions.Count; i++)
                    {
                        var instruction = _instructions[i];
                        if (!TryGetBranchTarget(instruction, target, out var branchTarget))
                            continue;
                        if (!_labels.TryGetValue(branchTarget.Symbol!, out var targetOffset))
                            continue;

                        var pc = checked((long)i * 4);
                        var address = checked((long)targetOffset + branchTarget.Addend);
                        var relaxation = SelectRelaxation(instruction, target, checked(address - pc));
                        if (relaxation == BranchRelaxationKind.None)
                            continue;

                        relaxations[i] = relaxation;
                        extraInstructions[i] = relaxation == BranchRelaxationKind.ConditionalViaBranch ? 1 : 2;
                        relaxationCount++;
                    }

                    if (relaxationCount == 0)
                        return;

                    var prefix = new int[_instructions.Count + 1];
                    for (var i = 0; i < _instructions.Count; i++)
                        prefix[i + 1] = checked(prefix[i] + extraInstructions[i]);

                    int RemapOffset(int offset)
                    {
                        if ((offset & 3) != 0)
                            throw new InvalidOperationException("ARM text offset is not instruction-aligned.");
                        var instructionIndex = offset / 4;
                        if ((uint)instructionIndex > (uint)_instructions.Count)
                            throw new InvalidOperationException("ARM text offset is outside the instruction stream.");
                        return checked(offset + prefix[instructionIndex] * 4);
                    }

                    var rewritten = new List<ArmInstruction>(checked(_instructions.Count + prefix[_instructions.Count]));
                    var generatedLabels = new List<KeyValuePair<string, int>>();
                    var generatedRelocations = new List<ArmObjectRelocation>();
                    for (var i = 0; i < _instructions.Count; i++)
                    {
                        var instruction = _instructions[i];
                        var relaxation = relaxations[i];
                        if (relaxation == BranchRelaxationKind.None)
                        {
                            rewritten.Add(instruction);
                            continue;
                        }

                        if (!TryGetBranchTarget(instruction, target, out var branchTarget))
                            throw new InvalidOperationException("Missing ARM branch target during relaxation.");

                        var rewrittenOffset = checked(rewritten.Count * 4);
                        switch (relaxation)
                        {
                            case BranchRelaxationKind.ConditionalViaBranch:
                                {
                                    var skipLabel = CreateRelaxationLabel();
                                    rewritten.Add(InvertConditionalBranch(instruction, skipLabel));
                                    rewritten.Add(CreateUnconditionalBranch(branchTarget));
                                    generatedLabels.Add(new KeyValuePair<string, int>(skipLabel, checked(rewrittenOffset + 8)));
                                    generatedRelocations.Add(new ArmObjectRelocation(
                                        Name,
                                        checked(rewrittenOffset + 4),
                                        branchTarget.Symbol!,
                                        branchTarget.Addend,
                                        target.Is64Bit ? ArmObjectRelocationKind.AArch64Branch26 : ArmObjectRelocationKind.ArmBranch24));
                                    break;
                                }
                            case BranchRelaxationKind.LongBranch:
                                EmitLongBranch(rewritten, generatedRelocations, rewrittenOffset, branchTarget, target, false);
                                break;
                            case BranchRelaxationKind.LongCall:
                                EmitLongBranch(rewritten, generatedRelocations, rewrittenOffset, branchTarget, target, true);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    foreach (var label in _labels.Keys.ToArray())
                        _labels[label] = RemapOffset(_labels[label]);
                    foreach (var pair in generatedLabels)
                        _labels.Add(pair.Key, pair.Value);

                    var oldRelocations = _relocations.ToArray();
                    _relocations.Clear();
                    foreach (var relocation in oldRelocations)
                    {
                        var instructionIndex = relocation.Offset / 4;
                        if ((uint)instructionIndex < (uint)relaxations.Length &&
                            relaxations[instructionIndex] != BranchRelaxationKind.None &&
                            IsRelaxedBranchRelocation(relocation.Kind))
                        {
                            continue;
                        }

                        _relocations.Add(new ArmObjectRelocation(
                            relocation.SectionName,
                            RemapOffset(relocation.Offset),
                            relocation.SymbolName,
                            relocation.Addend,
                            relocation.Kind));
                    }
                    _relocations.AddRange(generatedRelocations);

                    for (var i = 0; i < symbols.Count; i++)
                    {
                        var symbol = symbols[i];
                        if (!string.Equals(symbol.SectionName, Name, StringComparison.Ordinal))
                            continue;
                        var start = RemapOffset(symbol.Offset);
                        var end = RemapOffset(checked(symbol.Offset + symbol.Size));
                        symbols[i] = new ArmObjectSymbol(
                            symbol.Name,
                            symbol.SectionName,
                            start,
                            checked(end - start),
                            symbol.Binding,
                            symbol.Kind,
                            symbol.IsTentative);
                    }

                    _instructions.Clear();
                    _instructions.AddRange(rewritten);
                }
            }

            private static BranchRelaxationKind SelectRelaxation(ArmInstruction instruction, ArmTarget target, long displacement)
            {
                if (target.Is64Bit)
                {
                    if (instruction.Opcode == ArmInstrKind.B && instruction.Condition != ArmCondition.Al)
                        return FitsSignedScaled(displacement, 19) ? BranchRelaxationKind.None : BranchRelaxationKind.ConditionalViaBranch;
                    if (instruction.Opcode is ArmInstrKind.Cbz or ArmInstrKind.Cbnz)
                        return FitsSignedScaled(displacement, 19) ? BranchRelaxationKind.None : BranchRelaxationKind.ConditionalViaBranch;
                    if (instruction.Opcode is ArmInstrKind.Tbz or ArmInstrKind.Tbnz)
                        return FitsSignedScaled(displacement, 14) ? BranchRelaxationKind.None : BranchRelaxationKind.ConditionalViaBranch;
                    if (instruction.Opcode == ArmInstrKind.B)
                        return FitsSignedScaled(displacement, 26) ? BranchRelaxationKind.None : BranchRelaxationKind.LongBranch;
                    if (instruction.Opcode == ArmInstrKind.Bl)
                        return FitsSignedScaled(displacement, 26) ? BranchRelaxationKind.None : BranchRelaxationKind.LongCall;
                    return BranchRelaxationKind.None;
                }

                if (instruction.Opcode is not (ArmInstrKind.B or ArmInstrKind.Bl))
                    return BranchRelaxationKind.None;
                var encodedDisplacement = checked(displacement - 8);
                if (FitsSignedScaled(encodedDisplacement, 24))
                    return BranchRelaxationKind.None;
                if (instruction.Opcode == ArmInstrKind.Bl)
                    return BranchRelaxationKind.LongCall;
                return instruction.Condition == ArmCondition.Al
                    ? BranchRelaxationKind.LongBranch
                    : BranchRelaxationKind.ConditionalViaBranch;
            }

            private static bool TryGetBranchTarget(ArmInstruction instruction, ArmTarget target, out ArmOperand targetOperand)
            {
                targetOperand = default;
                if (target.Is64Bit)
                {
                    if (instruction.Opcode is ArmInstrKind.B or ArmInstrKind.Bl)
                        targetOperand = instruction.Operand0;
                    else if (instruction.Opcode is ArmInstrKind.Cbz or ArmInstrKind.Cbnz)
                        targetOperand = instruction.Operand1;
                    else if (instruction.Opcode is ArmInstrKind.Tbz or ArmInstrKind.Tbnz)
                        targetOperand = instruction.Operand2;
                    else
                        return false;
                }
                else
                {
                    if (instruction.Opcode is not (ArmInstrKind.B or ArmInstrKind.Bl))
                        return false;
                    targetOperand = instruction.Operand0;
                }

                return targetOperand.Symbol is not null;
            }

            private static ArmInstruction InvertConditionalBranch(ArmInstruction instruction, string skipLabel)
            {
                if (instruction.Opcode == ArmInstrKind.B)
                    return new ArmInstruction(
                        ArmInstrKind.B,
                        ArmOperand.SymbolOperand(skipLabel, ArmRelocationKind.ConditionalBranch),
                        condition: InvertCondition(instruction.Condition));

                if (instruction.Opcode is ArmInstrKind.Cbz or ArmInstrKind.Cbnz)
                    return new ArmInstruction(
                        instruction.Opcode == ArmInstrKind.Cbz ? ArmInstrKind.Cbnz : ArmInstrKind.Cbz,
                        instruction.Operand0,
                        ArmOperand.SymbolOperand(skipLabel, ArmRelocationKind.CompareBranch));

                if (instruction.Opcode is ArmInstrKind.Tbz or ArmInstrKind.Tbnz)
                    return new ArmInstruction(
                        instruction.Opcode == ArmInstrKind.Tbz ? ArmInstrKind.Tbnz : ArmInstrKind.Tbz,
                        instruction.Operand0,
                        instruction.Operand1,
                        ArmOperand.SymbolOperand(skipLabel, ArmRelocationKind.TestBranch));

                throw new InvalidOperationException("ARM branch is not conditional.");
            }

            private static ArmInstruction CreateUnconditionalBranch(ArmOperand targetOperand)
                => new ArmInstruction(
                    ArmInstrKind.B,
                    ArmOperand.SymbolOperand(targetOperand.Symbol!, ArmRelocationKind.Branch, targetOperand.Addend));

            private void EmitLongBranch(
                List<ArmInstruction> instructions,
                List<ArmObjectRelocation> relocations,
                int offset,
                ArmOperand targetOperand,
                ArmTarget target,
                bool call)
            {
                if (target.Is64Bit)
                {
                    var scratch = ArmOperand.RegisterOperand(ArmRegister.X12, 8);
                    instructions.Add(ArmInstruction.Binary(
                        ArmInstrKind.Adrp,
                        scratch,
                        ArmOperand.SymbolOperand(targetOperand.Symbol!, ArmRelocationKind.Adrp, targetOperand.Addend)));
                    instructions.Add(ArmInstruction.Ternary(
                        ArmInstrKind.Add,
                        scratch,
                        scratch,
                        ArmOperand.ImmediateOperand(0)));
                    instructions.Add(ArmInstruction.Unary(call ? ArmInstrKind.Blr : ArmInstrKind.Br, scratch));
                    relocations.Add(new ArmObjectRelocation(
                        Name,
                        offset,
                        targetOperand.Symbol!,
                        targetOperand.Addend,
                        ArmObjectRelocationKind.AArch64Adrp21));
                    relocations.Add(new ArmObjectRelocation(
                        Name,
                        checked(offset + 4),
                        targetOperand.Symbol!,
                        targetOperand.Addend,
                        ArmObjectRelocationKind.AArch64AddLow12));
                    return;
                }

                var armScratch = ArmOperand.RegisterOperand(ArmRegister.R12, 4);
                instructions.Add(ArmInstruction.Binary(ArmInstrKind.Movw, armScratch, ArmOperand.ImmediateOperand(0)));
                instructions.Add(ArmInstruction.Binary(ArmInstrKind.Movt, armScratch, ArmOperand.ImmediateOperand(0)));
                instructions.Add(ArmInstruction.Unary(call ? ArmInstrKind.Blx : ArmInstrKind.Bx, armScratch));
                relocations.Add(new ArmObjectRelocation(
                    Name,
                    offset,
                    targetOperand.Symbol!,
                    targetOperand.Addend,
                    ArmObjectRelocationKind.ArmMovw16));
                relocations.Add(new ArmObjectRelocation(
                    Name,
                    checked(offset + 4),
                    targetOperand.Symbol!,
                    targetOperand.Addend,
                    ArmObjectRelocationKind.ArmMovt16));
            }

            private string CreateRelaxationLabel()
            {
                while (true)
                {
                    var label = $"__arm_relax_skip_{_nextRelaxationLabelId++}";
                    if (!_labels.ContainsKey(label))
                        return label;
                }
            }

            private static bool FitsSignedScaled(long displacement, int bits)
            {
                if ((displacement & 3) != 0)
                    return false;
                var scaled = displacement >> 2;
                var minimum = -(1L << (bits - 1));
                var maximum = (1L << (bits - 1)) - 1;
                return scaled >= minimum && scaled <= maximum;
            }

            private static ArmCondition InvertCondition(ArmCondition condition)
                => condition switch
                {
                    ArmCondition.Eq => ArmCondition.Ne,
                    ArmCondition.Ne => ArmCondition.Eq,
                    ArmCondition.Cs => ArmCondition.Cc,
                    ArmCondition.Cc => ArmCondition.Cs,
                    ArmCondition.Mi => ArmCondition.Pl,
                    ArmCondition.Pl => ArmCondition.Mi,
                    ArmCondition.Vs => ArmCondition.Vc,
                    ArmCondition.Vc => ArmCondition.Vs,
                    ArmCondition.Hi => ArmCondition.Ls,
                    ArmCondition.Ls => ArmCondition.Hi,
                    ArmCondition.Ge => ArmCondition.Lt,
                    ArmCondition.Lt => ArmCondition.Ge,
                    ArmCondition.Gt => ArmCondition.Le,
                    ArmCondition.Le => ArmCondition.Gt,
                    ArmCondition.Al => ArmCondition.Nv,
                    ArmCondition.Nv => ArmCondition.Al,
                    _ => throw new ArgumentOutOfRangeException(nameof(condition)),
                };

            private static bool IsRelaxedBranchRelocation(ArmObjectRelocationKind kind)
                => kind is ArmObjectRelocationKind.ArmBranch24 or
                    ArmObjectRelocationKind.ArmCall24 or
                    ArmObjectRelocationKind.AArch64Branch26 or
                    ArmObjectRelocationKind.AArch64Call26 or
                    ArmObjectRelocationKind.AArch64ConditionalBranch19 or
                    ArmObjectRelocationKind.AArch64CompareBranch19 or
                    ArmObjectRelocationKind.AArch64TestBranch14;

            private enum BranchRelaxationKind : byte
            {
                None,
                ConditionalViaBranch,
                LongBranch,
                LongCall,
            }

            private int _nextRelaxationLabelId;

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
                var aligned = AlignUp(ByteLength, alignment);
                EmitZero(aligned - ByteLength);
                return aligned;
            }

            public void DefineSymbol(string name, int offset, int size, ArmObjectSymbolBinding binding, List<ArmObjectSymbol> symbols)
                => symbols.Add(new ArmObjectSymbol(name, Name, offset, size, binding, ArmObjectSymbolKind.Object));

            public void AddRelocation(int offset, string symbol, long addend, ArmObjectRelocationKind kind)
                => _relocations.Add(new ArmObjectRelocation(Name, offset, symbol, addend, kind));

            public void EmitByte(byte value)
                => _data.Add(value);

            public void EmitBytes(byte[] bytes, int count)
            {
                count = Math.Min(count, bytes.Length);
                for (var i = 0; i < count; i++)
                    _data.Add(bytes[i]);
            }

            public void EmitZero(int count)
            {
                for (var i = 0; i < count; i++)
                    _data.Add(0);
            }

            public void EmitInteger(long value, int size, TargetEndianness endianness)
            {
                var raw = unchecked((ulong)value);
                if (endianness == TargetEndianness.Little)
                {
                    for (var i = 0; i < size; i++)
                        _data.Add((byte)(raw >> (i * 8)));
                }
                else
                {
                    for (var i = size - 1; i >= 0; i--)
                        _data.Add((byte)(raw >> (i * 8)));
                }
            }

            public ArmDataSection ToSection()
                => new ArmDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());

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
                ArmObjectSymbolBinding binding,
                List<ArmObjectSymbol> symbols,
                bool isTentative = false)
                => symbols.Add(new ArmObjectSymbol(name, Name, offset, size, binding, ArmObjectSymbolKind.Object, isTentative));

            public ArmDataSection ToSection()
                => new ArmDataSection(Name, ArmObjectSectionKind.Bss, Alignment, ImmutableArray<byte>.Empty, ByteLength, ImmutableArray<ArmObjectRelocation>.Empty);

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
