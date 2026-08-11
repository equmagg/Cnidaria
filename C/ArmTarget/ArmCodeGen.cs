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
                vectorRegisters: ImmutableArray<MachineRegister>.Empty,
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

            private readonly ArmCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly string _functionLabel;
            private readonly IReadOnlyDictionary<LirBlock, string> _labels;
            private readonly bool _hasCalls;
            private readonly int _lrSaveOffset;
            private readonly int _scratchSaveOffset;
            private readonly int _backendTempOffset;
            private readonly int _totalFrameSize;

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
                _totalFrameSize = AlignUp(frameSize, _allocation.Frame.FrameAlignment);
            }

            public void EmitPrologue()
            {
                AdjustStack(-_totalFrameSize);
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
                    case LirInstructionKind.Jump:
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
                    case LirInstructionKind.VaStart:
                    case LirInstructionKind.VaArg:
                    case LirInstructionKind.InlineAssembly:
                        throw Unsupported(instruction, "Instruction is not implemented by the minimal ARM backend.");
                    default:
                        throw Unsupported(instruction, "Unsupported LIR instruction kind.");
                }
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
                    var preceding = CAbi.ClassifyValue(_owner._target, functionType.Parameters[i].Type, false, false);
                    _ = CAbi.AssignArgumentLocation(preceding, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                }

                var type = functionType.Parameters[parameterIndex].Type;
                var value = CAbi.ClassifyValue(_owner._target, type, false, false);
                if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 || value.Segments[0].RegisterClass != AbiRegisterClass.General)
                    throw Unsupported(instruction, "Only scalar general-register parameters are implemented.");

                var location = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var destination = GetWritableRegister(instruction.Result, Scratch0);
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

                var destination = GetWritableRegister(instruction.Result, Scratch0);
                LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return;
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);

                var destination = GetWritableRegister(instruction.Result, Scratch0);
                if (instruction.Result.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                {
                    var source = LoadOperand(instruction.Operands[0], destination == Scratch0 ? Scratch1 : Scratch0);
                    EmitBooleanFromRegister(destination, source);
                }
                else
                {
                    LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                    NormalizeIntegerRegister(destination, instruction.Result.Type);
                }
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                RequireScalar(instruction.Result.Type, instruction);
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                LoadImmediate(destination, 0, RegisterSize(instruction.Result.Type));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Unary instruction is incomplete.");
                RequireScalar(instruction.Result.Type, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);

                var size = RegisterSize(instruction.Result.Type);
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                var source = LoadOperand(instruction.Operands[0], Scratch1);
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
                        break;
                    case "||":
                        Emit(ArmInstruction.Ternary(ArmInstrKind.Orr, Reg(ToArm(Scratch3), size), Reg(ToArm(Scratch1), size), Reg(ToArm(Scratch2), size)));
                        EmitBooleanFromRegister(destination, Scratch3);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported binary operator '{instruction.Operator}'.");
                }

                NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

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
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                var address = BuildAddress(instruction.Address, Scratch1, Scratch2);
                LoadFromMemory(destination, address.BaseRegister, address.Offset, SizeOf(instruction.Result.Type), IsSignedIntegerType(instruction.Result.Type));
                NormalizeIntegerRegister(destination, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null || instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Invalid store instruction.");
                RequireScalar(instruction.Address.ElementType, instruction);
                RequireScalar(instruction.Operands[0].Type, instruction);
                var source = LoadOperandAs(instruction.Operands[0], instruction.Address.ElementType, Scratch0, instruction);
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

                EmitCallResult(instruction);
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
                    var value = CAbi.ClassifyValue(_owner._target, operand.Type, false, isVariadicUnnamed);
                    if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 || value.Segments[0].RegisterClass != AbiRegisterClass.General)
                        throw Unsupported(instruction, "Only scalar general-register call arguments are implemented.");
                    var location = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    locations.Add((operand, location, Math.Max(1, value.Size), RegisterSize(operand.Type)));
                }

                var stagingBase = _backendTempOffset;

                for (var i = 0; i < locations.Count; i++)
                {
                    var source = LoadOperand(locations[i].Operand, Scratch0);
                    StoreToMemory(source, stagingBase + i * _owner._allocationOptions.StackArgumentSlotSize, locations[i].RegisterSize);
                }

                for (var i = 0; i < locations.Count; i++)
                {
                    var item = locations[i];
                    LoadFromMemory(Scratch0, stagingBase + i * _owner._allocationOptions.StackArgumentSlotSize, item.RegisterSize, false);
                    if (item.Location.Kind == AbiLocationKind.Register)
                        MoveRegister(item.Location.Register, Scratch0, item.RegisterSize);
                    else if (item.Location.Kind == AbiLocationKind.Stack)
                        StoreToMemory(
                            Scratch0,
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
                if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 || value.Segments[0].RegisterClass != AbiRegisterClass.General)
                    throw Unsupported(instruction, "Only scalar general-register call results are implemented.");
                var destination = GetWritableRegister(instruction.Result, Scratch0);
                MoveRegister(destination, CAbi.ReturnRegister(value.Segments[0], 0), RegisterSize(instruction.Result.Type));
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
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand.");
                RequireScalar(instruction.Operands[0].Type, instruction);
                var condition = LoadOperand(instruction.Operands[0], Scratch0);
                Emit(ArmInstruction.Binary(ArmInstrKind.Cmp, Reg(ToArm(condition), RegisterSize(instruction.Operands[0].Type)), ArmOperand.ImmediateOperand(0)));
                EmitConditionalJump(ArmCondition.Ne, LabelOf(instruction.TrueTarget));
                EmitJump(LabelOf(instruction.FalseTarget));
            }

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
                    if (value.PassingKind != AbiPassingKind.Scalar || value.Segments.Length != 1 || value.Segments[0].RegisterClass != AbiRegisterClass.General)
                        throw Unsupported(instruction, "Only scalar general-register returns are implemented.");
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
                    var destination = GetWritableRegister(copies[0].Destination, Scratch0);
                    LoadOperandIntoAs(copies[0].Source, destination, copies[0].Destination.Type, instruction);
                    StoreWritableRegisterIfSpilled(copies[0].Destination, destination);
                    return;
                }

                var cursor = 0;
                foreach (var copy in copies)
                {
                    var size = RegisterSize(copy.Destination.Type);
                    var source = LoadOperandAs(copy.Source, copy.Destination.Type, Scratch0, instruction);
                    StoreToMemory(source, _backendTempOffset + cursor, size);
                    cursor += AlignUp(size, _owner._allocationOptions.SpillSlotAlignment);
                }

                cursor = 0;
                foreach (var copy in copies)
                {
                    var size = RegisterSize(copy.Destination.Type);
                    var destination = GetWritableRegister(copy.Destination, Scratch0);
                    LoadFromMemory(destination, _backendTempOffset + cursor, size, IsSignedIntegerType(copy.Destination.Type));
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
                var source = LoadOperand(operand, scratch);
                if (source != scratch && NeedsIntegerConversion(operand.Type, targetType))
                {
                    MoveRegister(scratch, source, RegisterSize(targetType));
                    source = scratch;
                }
                if (NeedsIntegerConversion(operand.Type, targetType))
                    NormalizeIntegerRegister(source, targetType);
                return source;
            }

            private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
            {
                var scratch = destination == Scratch0 ? Scratch1 : Scratch0;
                var source = LoadOperandAs(operand, targetType, scratch, instruction);
                MoveRegister(destination, source, RegisterSize(targetType));
                NormalizeIntegerRegister(destination, targetType);
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
                            throw new NotSupportedException("Floating-point constants are not implemented by the minimal ARM backend.");
                        LoadImmediate(preferred, ConvertIntegerConstant(operand.Immediate), RegisterSize(operand.Type));
                        NormalizeIntegerRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException($"Missing stack slot offset for {operand.StackSlot.Name}.");
                        LoadFromMemory(preferred, offset, SizeOf(operand.Type), IsSignedIntegerType(operand.Type));
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
                    return allocation.PhysicalRegister;
                LoadFromMemory(preferred, allocation.StackOffset, SizeOf(register.Type), IsSignedIntegerType(register.Type));
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
                => StoreArmRegister(ToArm(source), offset, size);

            private void LoadRegister(MachineRegister destination, int offset, int size)
                => LoadArmRegister(ToArm(destination), offset, size);

            private void StoreArmRegister(ArmRegister source, int offset, int size)
                => EmitMemoryStore(source, StackPointerArm, offset, size);

            private void LoadArmRegister(ArmRegister destination, int offset, int size)
                => EmitMemoryLoad(destination, StackPointerArm, offset, size, false);

            private void LoadFromMemory(MachineRegister destination, int offset, int size, bool signed)
                => EmitMemoryLoad(ToArm(destination), StackPointerArm, offset, size, signed);

            private void LoadFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size, bool signed)
                => EmitMemoryLoad(ToArm(destination), ToArm(baseRegister), offset, size, signed);

            private void StoreToMemory(MachineRegister source, int offset, int size)
                => EmitMemoryStore(ToArm(source), StackPointerArm, offset, size);

            private void StoreToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => EmitMemoryStore(ToArm(source), ToArm(baseRegister), offset, size);

            private void EmitMemoryLoad(ArmRegister destination, ArmRegister baseRegister, int offset, int size, bool signed)
            {
                if (CanEncodeMemoryOffset(offset, size, signed))
                {
                    Emit(ArmInstruction.Binary(LoadOpcode(size, signed), Reg(destination, RegisterOperandSize(size, signed)), Mem(baseRegister, offset, size)));
                    return;
                }

                var addressScratch = SelectAddressScratch(baseRegister, ArmRegister.Invalid);
                AddImmediate(addressScratch, FromArm(baseRegister), offset);
                Emit(ArmInstruction.Binary(LoadOpcode(size, signed), Reg(destination, RegisterOperandSize(size, signed)), Mem(ToArm(addressScratch), 0, size)));
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
                    4 or 8 => ArmInstrKind.Ldr,
                    _ => throw new NotSupportedException($"Unsupported scalar load size {size}."),
                };
            }

            private static ArmInstrKind StoreOpcode(int size)
            {
                return size switch
                {
                    1 => ArmInstrKind.Strb,
                    2 => ArmInstrKind.Strh,
                    4 or 8 => ArmInstrKind.Str,
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
                => _owner.EmitLoadImmediate(ToArm(destination), value, size);

            private void MaterializeSymbolAddress(string symbol, MachineRegister destination)
                => _owner.MaterializeSymbolAddress(symbol, ToArm(destination));

            private void AddImmediate(MachineRegister destination, MachineRegister source, int immediate)
            {
                if (_owner._machineTarget.Is64Bit)
                {
                    _owner.EmitAddImmediate(ToArm(destination), ToArm(source), immediate, _owner._target.PointerSize);
                    return;
                }

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
                Emit(ArmInstruction.Binary(ArmInstrKind.Mov, Reg(ToArm(destination), size), Reg(ToArm(source), size)));
            }

            private void NormalizeIntegerRegister(MachineRegister register, QualifiedType type)
            {
                if (!IsIntegerLike(type) && !IsPointerLike(type))
                    return;
                if (IsPointerLike(type))
                    return;

                var size = SizeOf(type);
                var registerBits = _owner._target.RegisterSize * 8;
                var valueBits = Math.Min(registerBits, size * 8);
                if (valueBits >= registerBits)
                    return;
                var shift = registerBits - valueBits;
                Emit(ArmInstruction.Ternary(
                    ArmInstrKind.Lsl,
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    ArmOperand.ImmediateOperand(shift)));
                Emit(ArmInstruction.Ternary(
                    IsUnsignedIntegerType(type) ? ArmInstrKind.Lsr : ArmInstrKind.Asr,
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    Reg(ToArm(register),
                    _owner._target.RegisterSize),
                    ArmOperand.ImmediateOperand(shift)));
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

            private int IncomingStackOffset(int abiStackOffset)
                => checked(_totalFrameSize + abiStackOffset);

            private int SizeOf(QualifiedType type)
                => Math.Max(1, _owner._target.SizeOf(type));

            private int RegisterSize(QualifiedType type)
            {
                var size = SizeOf(type);
                if (size > _owner._target.RegisterSize)
                    throw new NotSupportedException("Scalar value is wider than an ARM machine register.");
                return _owner._machineTarget.Is64Bit && size > 4 ? 8 : 4;
            }

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
                if (IsFloatType(type) || IsAggregateType(type) || SizeOf(type) > _owner._target.RegisterSize)
                {
                    if (instruction is null)
                        throw new NotSupportedException("The minimal ARM backend supports only integer and pointer scalars no wider than one machine register.");
                    throw Unsupported(instruction, "The minimal ARM backend supports only integer and pointer scalars no wider than one machine register.");
                }
            }

            private static bool NeedsIntegerConversion(QualifiedType source, QualifiedType destination)
                => source.Type != destination.Type || source.Qualifiers != destination.Qualifiers;

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

            public void AddRelocation(int offset, string symbol, long addend, ArmObjectRelocationKind kind)
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
