using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Cnidaria.Cs;
using Cnidaria.RiscV;

namespace Cnidaria.C;

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
    private const string StringSectionName = ".rodata.str1.1";
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
    private static readonly MachineRegister MaskRegister = MachineRegister.V0;
    private const int VectorRegisterCount = 32;
    private const int VectorScratchGroups = 3;
    // The rodata fallback costs auipc, addi and a load, so a chain up to that length is never worse
    private const int WideImmediateInstructionBudget = 4;
    private const int RoundTowardZero = 1;

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
    // String literals live in their own section: they are interned while a global is mid-emission into .rodata
    private readonly DataSectionBuilder _strings = new DataSectionBuilder(StringSectionName, RVObjectSectionKind.Rodata);
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
        _allocationOptions = ReserveCodeGenScratchRegisters(allocationOptions ?? LSRAOptions.ForTarget(_target));
        _options = options ?? RiscVCodeGeneratorOptions.Default;
    }

    private static LSRAOptions ReserveCodeGenScratchRegisters(LSRAOptions options)
    {
        var reservedGeneral = ImmutableHashSet.CreateBuilder<MachineRegister>();
        reservedGeneral.UnionWith(new[]
        {
            MachineRegister.X5,
            MachineRegister.X6,
            MachineRegister.X7,
            MachineRegister.X28,
            MachineRegister.X29,
            MachineRegister.X30,
            MachineRegister.X31,
        });
        var reservedFloating = ImmutableHashSet.Create(
            MachineRegister.F0,
            MachineRegister.F1,
            MachineRegister.F2);
        return new LSRAOptions(
            generalRegisters: options.GeneralRegisters.Where(r => !reservedGeneral.Contains(r)).ToImmutableArray(),
            floatingRegisters: options.FloatingRegisters.Where(r => !reservedFloating.Contains(r)).ToImmutableArray(),
            vectorRegisters: options.VectorRegisters,
            callBoundarySplitClasses: ImmutableArray.Create(LirRegisterClass.General, LirRegisterClass.Address, LirRegisterClass.Floating),
            stackAlignment: options.StackAlignment,
            spillSlotSize: options.SpillSlotSize,
            spillSlotAlignment: options.SpillSlotAlignment,
            stackArgumentSlotSize: options.StackArgumentSlotSize);
    }

    /// <summary>Holds back the scratch groups the vector emitters need, sized for the widest group in the function</summary>
    private static LSRAOptions ReserveVectorScratchRegisters(LSRAOptions options, LirFunction function, int groups)
    {
        if (options.VectorRegisters.IsDefaultOrEmpty)
            return options;

        var group = MaxVectorGroupRegisters(function);
        var scratchBase = (int)MachineRegister.V0 + VectorRegisterCount - groups * group;
        var needsMask = FunctionNeedsVectorMask(function);
        var allocatable = options.VectorRegisters
            .Where(register => (int)register < scratchBase && (register != MaskRegister || !needsMask))
            .ToImmutableArray();

        return new LSRAOptions(
            generalRegisters: options.GeneralRegisters,
            floatingRegisters: options.FloatingRegisters,
            vectorRegisters: allocatable,
            callBoundarySplitClasses: options.CallBoundarySplitClasses,
            stackAlignment: options.StackAlignment,
            spillSlotSize: options.SpillSlotSize,
            spillSlotAlignment: options.SpillSlotAlignment,
            stackArgumentSlotSize: options.StackArgumentSlotSize);
    }

    /// <summary>Scratch groups the emitters can demand: one, unless a tuple or a non-register vector operand needs all three</summary>
    private static int MinimumVectorScratchGroups(LirFunction function)
    {
        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (!IsRiscVVectorIntrinsicCall(instruction))
                {
                    if (VectorOperandNeedsMaterializing(instruction))
                        return VectorScratchGroups;
                    continue;
                }

                var name = ((FunctionSymbol)instruction.Operands[0].Symbol!).Name;
                if (name.StartsWith("__riscv_vcreate_", StringComparison.Ordinal) ||
                    name.StartsWith("__riscv_vget_", StringComparison.Ordinal) ||
                    name.StartsWith("__riscv_vset_", StringComparison.Ordinal))
                {
                    return VectorScratchGroups;
                }

                if (VectorOperandNeedsMaterializing(instruction))
                    return VectorScratchGroups;
            }
        }

        return 1;
    }

    private static bool VectorOperandNeedsMaterializing(LirInstruction instruction)
    {
        foreach (var operand in instruction.Operands)
        {
            if (operand.Type.Type is RVVectorType && operand.Kind != LirOperandKind.Register)
                return true;
        }

        return false;
    }

    private static bool HasSpilledVectorRegister(AllocationResult allocation)
    {
        foreach (var register in allocation.Function.VirtualRegisters)
        {
            if (register.RegisterClass == LirRegisterClass.Vector && allocation[register].IsSpilled)
                return true;
        }

        return false;
    }

    private static int MaxVectorGroupRegisters(LirFunction function)
    {
        var group = 1;
        foreach (var register in function.VirtualRegisters)
        {
            if (register.Type.Type is RVVectorType vector)
                group = Math.Max(group, vector.RegisterCount);
        }

        return group;
    }

    private static bool FunctionNeedsVectorMask(LirFunction function)
    {
        foreach (var register in function.VirtualRegisters)
        {
            if (register.Type.Type is RVVectorType { IsMask: true })
                return true;
        }

        return function.Blocks.SelectMany(static b => b.Instructions).Any(static i => i.Kind == LirInstructionKind.InlineAssembly);
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

        _text.RelaxBranches(_symbols);
        AddSectionSymbols();
        var dataSections = ImmutableArray.CreateBuilder<RVDataSection>();
        dataSections.Add(_rodata.ToSection());
        dataSections.Add(_strings.ToSection());
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
        _symbols.Add(new RVObjectSymbol(StringSectionName, StringSectionName, 0, _strings.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
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
            var nextIndex = 0L;
            foreach (var item in list.Items)
            {
                if (section.ByteLength - start >= availableSize)
                    break;
                // An element a designator skipped past stays zero
                if (item.ElementIndex > nextIndex)
                {
                    section.EmitZero((int)Math.Min((item.ElementIndex - nextIndex) * elementSize, availableSize - (section.ByteLength - start)));
                    nextIndex = item.ElementIndex;
                    if (section.ByteLength - start >= availableSize)
                        break;
                }

                var used = EmitInitializer(section, elementType, item.Initializer, Math.Min(elementSize, availableSize - (section.ByteLength - start)));
                if (used < elementSize && section.ByteLength - start < availableSize)
                    section.EmitZero(Math.Min(elementSize - used, availableSize - (section.ByteLength - start)));
                nextIndex++;
            }
        }
        else if (!list.IsByteImage && type.Type is TagType tag)
        {
            // Members are written in order, so the gaps between their offsets need zeroes
            var fields = tag.Symbol.Fields;
            var index = 0;
            foreach (var item in list.Items)
            {
                if (section.ByteLength - start >= availableSize)
                    break;
                // An unnamed bit-field takes no initializer
                while (index < fields.Length && fields[index].IsBitField && fields[index].Name.Length == 0)
                    index++;
                if (index >= fields.Length)
                    break;

                var field = fields[index++];
                var fieldOffset = tag.Symbol.TagKind == TagKind.Union ? 0 : _target.GetFieldPlacement(field).ByteOffset;
                var written = section.ByteLength - start;
                if (fieldOffset > written)
                {
                    section.EmitZero(Math.Min(fieldOffset - written, availableSize - written));
                    written = section.ByteLength - start;
                }

                // Bit-fields sharing a storage unit are emitted once
                if (fieldOffset < written)
                    continue;

                var fieldSize = Math.Max(1, _target.SizeOf(field.Type));
                var used = EmitInitializer(section, field.Type, item.Initializer, Math.Min(fieldSize, availableSize - written));
                if (used < fieldSize && section.ByteLength - start < availableSize)
                    section.EmitZero(Math.Min(fieldSize - used, availableSize - (section.ByteLength - start)));

                if (tag.Symbol.TagKind == TagKind.Union)
                    break;
            }

            // Zero-fill the tail up to the object size
            if (section.ByteLength - start < availableSize)
                section.EmitZero(availableSize - (section.ByteLength - start));
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

        // Array decay and element or field access all resolve to a symbol plus a constant offset
        if (GimpleStaticAddress.TryResolve(expression, _target, out var addressSymbol, out var addend))
        {
            EmitPointerRelocation(section, GetSymbolLabel(addressSymbol), addend);
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

    // Absolute code addresses: the image is linked at a fixed base, so no runtime relocation is needed
    private string CreateJumpTable(IReadOnlyList<string> targetLabels)
    {
        var label = CreateLocalLabel("jump_table");
        var entrySize = _target.PointerSize;
        var offset = _rodata.Align(entrySize);
        _rodata.DefineSymbol(label, offset, targetLabels.Count * entrySize, RVObjectSymbolBinding.Local, _symbols);
        foreach (var targetLabel in targetLabels)
            EmitPointerRelocation(_rodata, targetLabel);
        return label;
    }

    private void EmitPointerRelocation(DataSectionBuilder section, string symbol, long addend = 0)
    {
        var offset = section.ByteLength;
        section.EmitZero(_target.PointerSize);
        section.AddRelocation(offset, symbol, checked((int)addend), _target.PointerSize == 8 ? RVObjectRelocationKind.Absolute64 : RVObjectRelocationKind.Absolute32);
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

        // Allocate with the smaller reservation first; the full set only if a vector spilled
        var scratchGroups = MinimumVectorScratchGroups(function);
        var allocationOptions = ReserveVectorScratchRegisters(_allocationOptions, function, scratchGroups);
        var allocation = LinearScanRegisterAllocator.Allocate(function, _target, allocationOptions);
        if (scratchGroups != VectorScratchGroups && HasSpilledVectorRegister(allocation))
        {
            scratchGroups = VectorScratchGroups;
            allocationOptions = ReserveVectorScratchRegisters(_allocationOptions, function, scratchGroups);
            allocation = LinearScanRegisterAllocator.Allocate(function, _target, allocationOptions);
        }

        var blockLabels = new Dictionary<LirBlock, string>();
        foreach (var block in function.Blocks)
            blockLabels.Add(block, CreateLocalLabel($"{label}_{block.Name}"));

        var context = new FunctionEmissionContext(this, function, allocation, allocationOptions, label, blockLabels, scratchGroups);
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
        var offset = _strings.Align(1);
        _strings.DefineSymbol(label, offset, bytes.Length + 1, RVObjectSymbolBinding.Local, _symbols);
        _strings.EmitBytes(bytes, bytes.Length);
        _strings.EmitByte(0);
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

    /// <summary>Names the element width and length multiplier one vector operation runs at</summary>
    private readonly struct VectorShape
    {
        public VectorShape(int elementWidth, int lengthMultiplierLog2, bool isFloating)
        {
            ElementWidth = elementWidth;
            LengthMultiplierLog2 = lengthMultiplierLog2;
            IsFloating = isFloating;
        }

        public int ElementWidth { get; }
        public int LengthMultiplierLog2 { get; }
        public bool IsFloating { get; }
        public int GroupRegisters => LengthMultiplierLog2 <= 0 ? 1 : 1 << LengthMultiplierLog2;
    }

    /// <summary>Carries the RVV tail and mask policies an intrinsic name asks for</summary>
    private readonly struct VectorPolicy
    {
        private VectorPolicy(bool masked, bool keepsDestination, bool tailAgnostic, bool maskAgnostic)
        {
            Masked = masked;
            KeepsDestination = keepsDestination;
            TailAgnostic = tailAgnostic;
            MaskAgnostic = maskAgnostic;
        }

        public bool Masked { get; }
        public bool KeepsDestination { get; }
        public bool TailAgnostic { get; }
        public bool MaskAgnostic { get; }

        public static VectorPolicy Agnostic => new VectorPolicy(false, false, true, true);

        /// <summary>Strips a policy suffix; without one an operation is unmasked and both tail and mask are agnostic</summary>
        public static VectorPolicy Parse(ref string name)
        {
            if (TryStrip(ref name, "_tumu"))
                return new VectorPolicy(true, true, false, false);
            if (TryStrip(ref name, "_tum"))
                return new VectorPolicy(true, true, false, true);
            if (TryStrip(ref name, "_tu"))
                return new VectorPolicy(false, true, false, true);
            if (TryStrip(ref name, "_mu"))
                return new VectorPolicy(true, true, true, false);
            if (TryStrip(ref name, "_m"))
                return new VectorPolicy(true, false, true, true);
            return Agnostic;
        }

        private static bool TryStrip(ref string name, string suffix)
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
                return false;
            name = name.Substring(0, name.Length - suffix.Length);
            return true;
        }
    }

    private sealed class FunctionEmissionContext
    {
        private readonly RiscVCodeGenerator _owner;
        private readonly LirFunction _function;
        private readonly AllocationResult _allocation;
        private readonly LSRAOptions _allocationOptions;
        private readonly int _vectorScratchGroup;
        private readonly int _vectorScratchGroups;
        private readonly string _functionLabel;
        private readonly IReadOnlyDictionary<LirBlock, string> _labels;
        private readonly Dictionary<LirBlock, LirSelectDiamond> _selectDiamonds = new();
        private readonly HashSet<LirBlock> _foldedSelectArms = new();
        private readonly HashSet<LirVirtualRegister> _narrowStoreOnlyValues = new();
        private LirBlock? _currentBlock;
        private int _currentBlockIndex;
        private readonly bool _hasCalls;
        private readonly int _raSaveOffset;
        private readonly int _riscVVarArgsSaveAreaOffset;
        private readonly int _riscVVarArgsSaveAreaSize;
        private readonly int _totalFrameSize;
        private readonly IntegerRepresentationFact[] _integerRepresentationFacts = new IntegerRepresentationFact[32];
        private RVRegister _vectorConfigLength = RVRegister.Invalid;
        private RVRegister _vectorConfigResult = RVRegister.Invalid;
        private int _vectorConfigType = -1;
        private readonly HashSet<MachineRegister> _callOperandRegisters = new HashSet<MachineRegister>();
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

        private MachineRegister VecScratch0 => (MachineRegister)((int)MachineRegister.V0 + VectorRegisterCount - _vectorScratchGroups * _vectorScratchGroup);
        // Groups past the first exist only where the full set was reserved
        private MachineRegister VectorScratchAt(int index)
        {
            if (index >= _vectorScratchGroups)
                throw new InvalidOperationException($"Vector scratch group {index} was not reserved for '{_functionLabel}'.");
            return (MachineRegister)((int)VecScratch0 + index * _vectorScratchGroup);
        }

        /// <summary>Loads an operand, resolving the scratch group lazily so it is demanded only when used</summary>
        private MachineRegister LoadOperandOrVectorScratch(LirOperand operand, int scratchIndex)
        {
            if (operand.Kind == LirOperandKind.Register && operand.Register is not null)
            {
                var allocation = _allocation[operand.Register];
                if (!allocation.IsSpilled)
                    return allocation.PhysicalRegister;
            }

            return LoadOperand(operand, VectorScratchAt(scratchIndex));
        }

        public FunctionEmissionContext(
            RiscVCodeGenerator owner,
            LirFunction function,
            AllocationResult allocation,
            LSRAOptions allocationOptions,
            string functionLabel,
            IReadOnlyDictionary<LirBlock, string> labels,
            int vectorScratchGroups)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
            _allocationOptions = allocationOptions ?? throw new ArgumentNullException(nameof(allocationOptions));
            _vectorScratchGroup = MaxVectorGroupRegisters(function);
            _vectorScratchGroups = vectorScratchGroups;
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
            FindSelectDiamonds();
            FindNarrowStoreOnlyValues();
            _currentInstructionPosition = 0;
            for (var blockIndex = 0; blockIndex < _function.Blocks.Length; blockIndex++)
            {
                var block = _function.Blocks[blockIndex];
                _fallthroughBlock = blockIndex + 1 < _function.Blocks.Length
                    ? _function.Blocks[blockIndex + 1]
                    : null;
                ClearIntegerRepresentationFacts();
                _currentBlock = block;
                _currentBlockIndex = blockIndex;
                _owner._text.DefineLabel(LabelOf(block));
                if (_foldedSelectArms.Contains(block))
                {
                    _currentInstructionPosition += block.Instructions.Length * 2;
                    continue;
                }

                foreach (var instruction in block.Instructions)
                {
                    EmitInstruction(instruction);
                    _currentInstructionPosition += 2;
                }
            }
            _fallthroughBlock = null;
        }

        /// <summary>Marks the narrow values whose every use is a store that keeps only their own width</summary>
        private void FindNarrowStoreOnlyValues()
        {
            var candidates = new HashSet<LirVirtualRegister>();
            foreach (var register in _function.VirtualRegisters)
            {
                if (!IsIntegerLike(register.Type) || IsPointerLike(register.Type) || IsFloatType(register.Type) ||
                    IsAggregateType(register.Type) || RequiresSoftwareScalar(register.Type) ||
                    RequiresStackBackedScalar(register.Type))
                {
                    continue;
                }

                var bits = SizeOf(register.Type) * 8;
                if (bits > 0 && bits < _owner._target.RegisterSize * 8)
                    candidates.Add(register);
            }

            if (candidates.Count == 0)
                return;

            foreach (var block in _function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    var valueIndex = NarrowStoreValueOperandIndex(instruction);
                    for (var i = 0; i < instruction.Operands.Length; i++)
                    {
                        if (i == valueIndex)
                            continue;
                        if (instruction.Operands[i].Register is { } used)
                            candidates.Remove(used);
                    }

                    foreach (var copy in instruction.ParallelCopies)
                    {
                        if (copy.Source.Register is { } copied)
                            candidates.Remove(copied);
                    }

                    RemoveAddressRegisters(instruction.Address, candidates);
                }
            }

            _narrowStoreOnlyValues.UnionWith(candidates);
        }

        // Reports the operand a store keeps only the low bytes of, or -1 when the store reads any wider
        private int NarrowStoreValueOperandIndex(LirInstruction instruction)
        {
            if (instruction.Kind != LirInstructionKind.Store || instruction.Address is null ||
                instruction.Operands.Length == 0)
            {
                return -1;
            }

            var storeType = instruction.Address.ElementType;
            if (!IsIntegerLike(storeType) || IsPointerLike(storeType) || IsFloatType(storeType) ||
                IsAggregateType(storeType) || RequiresSoftwareScalar(storeType) || RequiresStackBackedScalar(storeType))
            {
                return -1;
            }

            var value = instruction.Operands[0];
            if (value.Register is null)
                return -1;

            var storeBits = Math.Min(SizeOfRegisterType(storeType), SizeOf(storeType)) * 8;
            return storeBits > 0 && storeBits <= SizeOf(value.Register.Type) * 8 ? 0 : -1;
        }

        private static void RemoveAddressRegisters(LirAddress? address, HashSet<LirVirtualRegister> candidates)
        {
            while (address is not null)
            {
                if (address.BaseOperand?.Register is { } baseRegister)
                    candidates.Remove(baseRegister);
                if (address.Index?.Register is { } indexRegister)
                    candidates.Remove(indexRegister);
                address = address.BaseAddress;
            }
        }

        // Zicond turns a conditional expression into two masked moves and an or, with no branch at all
        private void FindSelectDiamonds()
        {
            if (!_owner._machineTarget.HasZicond)
                return;

            foreach (var pair in LirSelect.FindDiamonds(_function, _owner._target))
            {
                if (!IsSelectableCondition(pair.Value.Branch))
                    continue;
                _selectDiamonds.Add(pair.Key, pair.Value);
                _foldedSelectArms.Add(pair.Value.TrueArm);
                _foldedSelectArms.Add(pair.Value.FalseArm);
            }
        }

        private bool IsSelectableCondition(LirInstruction branch)
        {
            foreach (var operand in branch.Operands)
            {
                if (IsFloatType(operand.Type) || IsRv32WideInteger(operand.Type) || RequiresStackBackedScalar(operand.Type))
                    return false;
            }

            return branch.Operands.Length == 1 ||
                (branch.Operands.Length == 2 && IsComparisonOperator(branch.Operator));
        }

        private bool TryEmitSelect(LirInstruction instruction)
        {
            if (_currentBlock is null ||
                !_selectDiamonds.TryGetValue(_currentBlock, out var diamond) ||
                !ReferenceEquals(diamond.Branch, instruction))
            {
                return false;
            }

            var condition = EmitSelectCondition(instruction);
            var destination = GetWritableRegister(diamond.Destination, GpScratch0);

            var trueValue = LoadOperand(diamond.TrueValue, GpScratch1);
            Emit(RVInstruction.R(RVInstrKind.CzeroEqz, ToRegister(GpScratch1), ToRegister(trueValue), ToRegister(condition)));
            var falseValue = LoadOperand(diamond.FalseValue, GpScratch2);
            Emit(RVInstruction.R(RVInstrKind.CzeroNez, ToRegister(GpScratch2), ToRegister(falseValue), ToRegister(condition)));
            Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(destination), ToRegister(GpScratch1), ToRegister(GpScratch2)));

            SetIntegerRepresentation(destination, IntegerRepresentationFact.Unknown);
            NormalizeIntegerRegister(destination, diamond.Destination.Type);
            StoreWritableRegisterIfSpilled(diamond.Destination, destination);
            if (!FallsThroughToJoin(diamond.Join))
                EmitJump(LabelOf(diamond.Join));
            return true;
        }

        // czero reads the whole register, so the condition only has to be zero or non-zero
        private MachineRegister EmitSelectCondition(LirInstruction branch)
        {
            if (branch.Operands.Length == 1)
                return LoadIntegerBranchOperand(branch.Operands[0], GpScratch3);

            var left = LoadOperand(branch.Operands[0], GpScratch1);
            var right = LoadOperand(branch.Operands[1], GpScratch2);
            var signed = IsSignedIntegerType(branch.Operands[0].Type) || IsSignedIntegerType(branch.Operands[1].Type);
            switch (branch.Operator)
            {
                case "==": EmitEquality(GpScratch3, left, right, equal: true); break;
                case "!=": EmitEquality(GpScratch3, left, right, equal: false); break;
                case "<": EmitLessThan(GpScratch3, left, right, signed); break;
                case ">": EmitLessThan(GpScratch3, right, left, signed); break;
                case "<=": EmitLessThan(GpScratch3, right, left, signed); EmitImm(RVInstrKind.Xori, GpScratch3, GpScratch3, 1); break;
                default: EmitLessThan(GpScratch3, left, right, signed); EmitImm(RVInstrKind.Xori, GpScratch3, GpScratch3, 1); break;
            }

            return GpScratch3;
        }

        // The folded arms emit nothing, so the join is still reached by falling off the end of this block
        private bool FallsThroughToJoin(LirBlock join)
        {
            for (var index = _currentBlockIndex + 1; index < _function.Blocks.Length; index++)
            {
                var block = _function.Blocks[index];
                if (ReferenceEquals(block, join))
                    return true;
                if (!_foldedSelectArms.Contains(block))
                    return false;
            }

            return false;
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
            return checked(remainingRegisters * _allocationOptions.StackArgumentSlotSize);
        }

        private AbiCursor ComputeNamedArgumentCursor()
        {
            var cursor = new AbiCursor();
            if (_allocation.Frame.HasHiddenReturnBuffer)
                _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _allocationOptions.StackArgumentSlotSize);
            var functionType = _function.Symbol?.FunctionType;
            if (functionType is not null)
            {
                foreach (var parameter in functionType.Parameters)
                {
                    var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                    _ = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
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
                    StoreRegister(integerRegisters[register], Sp, checked(_riscVVarArgsSaveAreaOffset + (register - cursor.Integer) * _allocationOptions.StackArgumentSlotSize), _owner._target.PointerSize);
                AddImmediate(GpScratch0, Sp, _riscVVarArgsSaveAreaOffset);
            }
            else
            {
                AddImmediate(GpScratch0, Sp, IncomingStackOffset(cursor.Stack * _allocationOptions.StackArgumentSlotSize));
            }
            StoreRegister(GpScratch0, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize);
        }

        private void SaveIncomingHiddenReturnBuffer()
        {
            var returnType = _function.Symbol?.FunctionType?.ReturnType;
            if (!returnType.HasValue || !CAbi.RequiresHiddenReturnBuffer(_owner._target, returnType.Value))
                return;

            var cursor = new AbiCursor();
            var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _allocationOptions.StackArgumentSlotSize);
            if (!_allocation.Frame.HasHiddenReturnBuffer)
                return;

            if (location.Kind == AbiLocationKind.Register)
            {
                StoreRegister(location.Register, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                return;
            }

            if (location.Kind == AbiLocationKind.Stack)
            {
                LoadFromMemory(GpScratch0, Sp, IncomingStackOffset(location.StackByteOffset(_allocationOptions.StackArgumentSlotSize)), _owner._target.PointerSize, signed: false);
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
            var outputBindings = new List<RiscVAsmRegisterBinding>();
            var inputBindings = new List<RiscVAsmRegisterBinding>();
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
                    var binding = new RiscVAsmRegisterBinding(output, destination, null);
                    outputBindings.Add(binding);
                    formatted = new InlineAsmFormattedOperand(
                        output.Name,
                        modifier => RVRegisters.Format(ToAnyRegister(binding.Register)));
                }

                AddAsmOperand(operands, namedOperands, formatted);
            }

            foreach (var input in asmStatement.Inputs)
            {
                if (operandIndex >= instruction.Operands.Length)
                    throw Unsupported(instruction, "Inline assembly input operand is missing from LIR.");

                var value = instruction.Operands[operandIndex++];
                var storage = InlineAsmConstraints.PreferredStorage(input.Constraint, value.Type);
                InlineAsmFormattedOperand formatted;
                if (storage == InlineAsmOperandStorage.Memory)
                {
                    formatted = new InlineAsmFormattedOperand(input.Name, modifier => FormatRiscVAsmMemoryOperand(value));
                }
                else if (storage == InlineAsmOperandStorage.Immediate)
                {
                    formatted = new InlineAsmFormattedOperand(input.Name, modifier => FormatRiscVAsmImmediate(value));
                }
                else
                {
                    var binding = new RiscVAsmRegisterBinding(input, null, value);
                    inputBindings.Add(binding);
                    formatted = new InlineAsmFormattedOperand(
                        input.Name,
                        modifier => RVRegisters.Format(ToAnyRegister(binding.Register)));
                }
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

            var unavailable = GetRiscVAsmUnavailableRegisters(asmStatement, outputBindings, inputBindings, instruction);
            ResolveRiscVAsmMatchingOperands(outputBindings, inputBindings, instruction);
            AllocateRiscVAsmRegisters(outputBindings, inputBindings, unavailable, instruction);
            EmitRiscVAsmInputMoves(outputBindings, inputBindings, instruction);

            var expanded = InlineAsmTemplateExpander.Expand(
                asmStatement.Text,
                operands,
                namedOperands,
                labels,
                namedLabels,
                _owner.CreateLocalLabel(_functionLabel + "_asm_id"));
            EmitInlineAssemblyText(instruction, expanded);

            EmitRiscVAsmOutputMoves(outputBindings, instruction);

            if (asmStatement.IsGoto && instruction.Target is not null && !IsFallthroughTarget(instruction.Target))
                EmitJump(LabelOf(instruction.Target));
        }

        private void AddAsmOperand(List<InlineAsmFormattedOperand> operands, Dictionary<string, int> namedOperands, InlineAsmFormattedOperand operand)
        {
            if (operand.Name is not null && !namedOperands.ContainsKey(operand.Name))
                namedOperands.Add(operand.Name, operands.Count);
            operands.Add(operand);
        }

        private void ResolveRiscVAsmMatchingOperands(
            IReadOnlyList<RiscVAsmRegisterBinding> outputs,
            IReadOnlyList<RiscVAsmRegisterBinding> inputs,
            LirInstruction instruction)
        {
            var namedOutputs = new Dictionary<string, RiscVAsmRegisterBinding>(StringComparer.Ordinal);
            foreach (var output in outputs)
            {
                if (output.Operand.Name is not null && !namedOutputs.ContainsKey(output.Operand.Name))
                    namedOutputs.Add(output.Operand.Name, output);
            }

            foreach (var input in inputs)
            {
                var matching = InlineAsmConstraints.MatchingOperand(input.Operand.Constraint);
                if (matching is null)
                    continue;

                RiscVAsmRegisterBinding? output = null;
                if (int.TryParse(matching, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    if ((uint)index < (uint)outputs.Count)
                        output = outputs[index];
                }
                else
                {
                    namedOutputs.TryGetValue(matching, out output);
                }

                if (output is null)
                    throw Unsupported(instruction, $"Inline assembly matching constraint '{input.Operand.Constraint}' does not name a register output.");
                if (RiscVAsmRegisterClass(input.Type) != RiscVAsmRegisterClass(output.Type))
                    throw Unsupported(instruction, "Inline assembly matching operands use incompatible register classes.");

                input.MatchingOutput = output;
            }
        }

        private HashSet<MachineRegister> GetRiscVAsmUnavailableRegisters(
            GimpleAsmStatement asmStatement,
            IReadOnlyList<RiscVAsmRegisterBinding> outputs,
            IReadOnlyList<RiscVAsmRegisterBinding> inputs,
            LirInstruction instruction)
        {
            var result = new HashSet<MachineRegister>();
            foreach (var clobber in asmStatement.Clobbers)
            {
                if (string.Equals(clobber, "memory", StringComparison.Ordinal) ||
                    string.Equals(clobber, "cc", StringComparison.Ordinal) ||
                    string.Equals(clobber, "redzone", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryParseRiscVAsmRegister(clobber, out var register))
                    throw Unsupported(instruction, $"Invalid or unsupported RISC-V inline assembly clobber '{clobber}'.");
                result.Add(register);
            }

            // A hard-coded register in the template is unavailable to a generic operand unless
            // that register is itself named by an explicit operand constraint. This mirrors the
            // LSRA's conservative template scan and prevents a generic operand from silently
            // aliasing a scratch register used directly by the template.
            var explicitOperands = new HashSet<MachineRegister>();
            foreach (var binding in outputs.Concat(inputs))
            {
                var fixedRegister = TryGetRiscVConstraintRegister(binding.Operand.Constraint, binding.Type);
                if (fixedRegister.HasValue)
                    explicitOperands.Add(fixedRegister.Value);
            }

            foreach (var register in RiscVAsmTemplateRegisters(asmStatement.Text))
            {
                if (!explicitOperands.Contains(register))
                    result.Add(register);
            }

            return result;
        }

        private IEnumerable<MachineRegister> RiscVAsmTemplateRegisters(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var isIdentifier = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_');
                if (isIdentifier)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start < 0)
                    continue;

                var token = text.Substring(start, i - start);
                if (TryParseRiscVAsmRegister(token, out var register))
                    yield return register;
                start = -1;
            }
        }

        private bool TryParseRiscVAsmRegister(string text, out MachineRegister register)
        {
            foreach (var registerClass in new[]
            {
                LirRegisterClass.General,
                LirRegisterClass.Address,
                LirRegisterClass.Floating,
                LirRegisterClass.Vector,
            })
            {
                if (TargetRegisterInfo.TryParseExplicitRegister(_owner._target, text, registerClass, out register))
                    return true;
            }

            register = MachineRegister.Invalid;
            return false;
        }

        private void AllocateRiscVAsmRegisters(
            IReadOnlyList<RiscVAsmRegisterBinding> outputs,
            IReadOnlyList<RiscVAsmRegisterBinding> inputs,
            HashSet<MachineRegister> unavailable,
            LirInstruction instruction)
        {
            var used = new HashSet<MachineRegister>();

            foreach (var output in outputs)
            {
                var fixedRegister = TryGetRiscVConstraintRegister(output.Operand.Constraint, output.Type);
                if (!fixedRegister.HasValue)
                    continue;
                AssignRiscVAsmRegister(output, fixedRegister.Value, used, unavailable, instruction, "output");
            }

            foreach (var input in inputs)
            {
                if (input.MatchingOutput is not null)
                    continue;
                var fixedRegister = TryGetRiscVConstraintRegister(input.Operand.Constraint, input.Type);
                if (!fixedRegister.HasValue)
                    continue;
                AssignRiscVAsmRegister(input, fixedRegister.Value, used, unavailable, instruction, "input");
            }

            foreach (var output in outputs)
            {
                if (output.Register != MachineRegister.Invalid)
                    continue;
                var selected = SelectRiscVAsmRegister(output.Type, used, unavailable);
                if (selected == MachineRegister.Invalid)
                    throw Unsupported(instruction, $"Cannot satisfy RISC-V inline assembly output constraint '{output.Operand.Constraint}'.");
                AssignRiscVAsmRegister(output, selected, used, unavailable, instruction, "output");
            }

            foreach (var input in inputs)
            {
                if (input.MatchingOutput is null)
                    continue;
                input.Register = input.MatchingOutput.Register;
            }

            foreach (var input in inputs)
            {
                if (input.Register != MachineRegister.Invalid)
                    continue;
                var selected = SelectRiscVAsmRegister(input.Type, used, unavailable);
                if (selected == MachineRegister.Invalid)
                    throw Unsupported(instruction, $"Cannot satisfy RISC-V inline assembly input constraint '{input.Operand.Constraint}'.");
                AssignRiscVAsmRegister(input, selected, used, unavailable, instruction, "input");
            }
        }

        private void AssignRiscVAsmRegister(
            RiscVAsmRegisterBinding binding,
            MachineRegister register,
            HashSet<MachineRegister> used,
            HashSet<MachineRegister> unavailable,
            LirInstruction instruction,
            string kind)
        {
            if (unavailable.Contains(register))
                throw Unsupported(instruction, $"RISC-V inline assembly {kind} register {RVRegisters.Format(ToAnyRegister(register))} is clobbered or used directly by the template.");
            if (!used.Add(register))
                throw Unsupported(instruction, $"RISC-V inline assembly operands require incompatible values in {RVRegisters.Format(ToAnyRegister(register))}.");
            binding.Register = register;
        }

        private MachineRegister SelectRiscVAsmRegister(
            QualifiedType type,
            HashSet<MachineRegister> used,
            HashSet<MachineRegister> unavailable)
        {
            foreach (var register in RiscVAsmRegisterCandidates(type))
            {
                if (!used.Contains(register) && !unavailable.Contains(register))
                    return register;
            }
            return MachineRegister.Invalid;
        }

        private IEnumerable<MachineRegister> RiscVAsmRegisterCandidates(QualifiedType type)
        {
            var allocated = RiscVAsmRegisterClass(type) switch
            {
                LirRegisterClass.Floating => _allocationOptions.FloatingRegisters,
                LirRegisterClass.Vector => _allocationOptions.VectorRegisters,
                _ => _allocationOptions.GeneralRegisters,
            };

            // Generic asm operands are deliberately kept out of the code generator's t0/t1/etc.
            // scratch registers: formatting memory operands and materializing large stack offsets
            // may use those scratches before the asm executes. The LSRA reserves the requested
            // register class at an asm site, so caller-saved allocatable registers are free here.
            foreach (var register in allocated)
            {
                if (!TargetRegisterInfo.IsCalleeSaved(_owner._target, register))
                    yield return register;
            }
        }

        private LirRegisterClass RiscVAsmRegisterClass(QualifiedType type)
        {
            var registerClass = CAbi.PreferredLirRegisterClass(_owner._target, type);
            return registerClass == LirRegisterClass.Address ? LirRegisterClass.General : registerClass;
        }

        private void EmitRiscVAsmInputMoves(
            IReadOnlyList<RiscVAsmRegisterBinding> outputs,
            IReadOnlyList<RiscVAsmRegisterBinding> inputs,
            LirInstruction instruction)
        {
            var moves = new List<RiscVAsmInputMove>();
            foreach (var output in outputs)
            {
                if (output.Operand.IsReadWrite && output.Output is not null)
                    AddRiscVAsmInputMove(moves, output.Register, LirOperand.ForRegister(output.Output), output.Type, instruction);
            }
            foreach (var input in inputs)
            {
                if (input.Input is not null)
                    AddRiscVAsmInputMove(moves, input.Register, input.Input, input.Type, instruction);
            }
            EmitRiscVAsmParallelInputMoves(moves, instruction);
        }

        private void AddRiscVAsmInputMove(
            List<RiscVAsmInputMove> moves,
            MachineRegister destination,
            LirOperand source,
            QualifiedType type,
            LirInstruction instruction)
        {
            foreach (var existing in moves)
            {
                if (existing.Destination != destination)
                    continue;
                if (SameRiscVAsmInputValue(existing.SourceOperand, source))
                    return;
                throw Unsupported(instruction, $"Inline assembly requires multiple values in {RVRegisters.Format(ToAnyRegister(destination))}.");
            }

            moves.Add(new RiscVAsmInputMove(destination, source, type));
        }

        private static bool SameRiscVAsmInputValue(LirOperand? left, LirOperand right)
        {
            if (left is null || left.Kind != right.Kind)
                return false;
            if (left.Kind == LirOperandKind.Register)
                return ReferenceEquals(left.Register, right.Register);
            if (left.Kind == LirOperandKind.Immediate)
                return Equals(left.Immediate, right.Immediate) && left.Type.Equals(right.Type);
            return ReferenceEquals(left, right);
        }

        private void EmitRiscVAsmParallelInputMoves(List<RiscVAsmInputMove> moves, LirInstruction instruction)
        {
            foreach (var move in moves)
            {
                if (move.SourceOperand is not null && TryGetPhysicalRegister(move.SourceOperand, out var sourceRegister))
                {
                    move.SourceRegister = sourceRegister;
                    move.SourceOperand = null;
                }
            }

            for (var i = moves.Count - 1; i >= 0; i--)
            {
                if (moves[i].HasRegisterSource && moves[i].SourceRegister == moves[i].Destination)
                    moves.RemoveAt(i);
            }

            while (moves.Count != 0)
            {
                var emitted = false;
                for (var i = 0; i < moves.Count; i++)
                {
                    var destination = moves[i].Destination;
                    var isSource = moves.Any(other => other.HasRegisterSource && other.SourceRegister == destination);
                    if (isSource)
                        continue;

                    EmitRiscVAsmInputMove(moves[i], instruction);
                    moves.RemoveAt(i);
                    emitted = true;
                    break;
                }
                if (emitted)
                    continue;

                var cycleMove = moves.FirstOrDefault(static move => move.HasRegisterSource);
                if (cycleMove is null)
                    throw Unsupported(instruction, "Cannot resolve RISC-V inline assembly input operands.");
                SpillRiscVAsmInputCycle(moves, cycleMove.SourceRegister, cycleMove.Type, instruction);
            }
        }

        private void SpillRiscVAsmInputCycle(
            List<RiscVAsmInputMove> moves,
            MachineRegister source,
            QualifiedType type,
            LirInstruction instruction)
        {
            RequireRiscVAsmTemp(type, instruction);
            StoreToMemory(source, Sp, _allocation.Frame.ParallelCopyTempOffset, SizeOfRegisterType(type));
            foreach (var move in moves)
            {
                if (!move.HasRegisterSource || move.SourceRegister != source)
                    continue;
                move.SourceRegister = MachineRegister.Invalid;
                move.UsesTemp = true;
            }
        }

        private void EmitRiscVAsmInputMove(RiscVAsmInputMove move, LirInstruction instruction)
        {
            if (move.UsesTemp)
            {
                LoadFromMemory(move.Destination, Sp, _allocation.Frame.ParallelCopyTempOffset, SizeOfRegisterType(move.Type), IsSignedIntegerType(move.Type));
                NormalizeScalarRegister(move.Destination, move.Type);
                return;
            }
            if (move.HasRegisterSource)
            {
                MoveRegister(move.Destination, move.SourceRegister, VectorGroupOf(move.Type));
                return;
            }
            if (move.SourceOperand is null)
                throw Unsupported(instruction, "RISC-V inline assembly input move has no source.");
            LoadOperandIntoAs(move.SourceOperand, move.Destination, move.Type, instruction);
        }

        private void EmitRiscVAsmOutputMoves(IReadOnlyList<RiscVAsmRegisterBinding> outputs, LirInstruction instruction)
        {
            var moves = new List<RiscVAsmRegisterMove>();
            foreach (var output in outputs)
            {
                if (output.Output is null)
                    continue;

                NormalizeScalarRegister(output.Register, output.Type);
                if (!TryGetPhysicalRegister(output.Output, out var destination))
                {
                    StoreWritableRegisterIfSpilled(output.Output, output.Register);
                    continue;
                }
                if (destination != output.Register)
                    moves.Add(new RiscVAsmRegisterMove(destination, output.Register, output.Type));
            }

            EmitRiscVAsmParallelRegisterMoves(moves, instruction);
        }

        private void EmitRiscVAsmParallelRegisterMoves(List<RiscVAsmRegisterMove> moves, LirInstruction instruction)
        {
            while (moves.Count != 0)
            {
                var emitted = false;
                for (var i = 0; i < moves.Count; i++)
                {
                    var destination = moves[i].Destination;
                    if (moves.Any(move => move.Source == destination))
                        continue;
                    MoveRegister(destination, moves[i].Source, VectorGroupOf(moves[i].Type));
                    moves.RemoveAt(i);
                    emitted = true;
                    break;
                }
                if (emitted)
                    continue;

                var source = moves[0].Source;
                var type = moves[0].Type;
                RequireRiscVAsmTemp(type, instruction);
                StoreToMemory(source, Sp, _allocation.Frame.ParallelCopyTempOffset, SizeOfRegisterType(type));
                var tempConsumers = new List<RiscVAsmRegisterMove>();
                for (var i = moves.Count - 1; i >= 0; i--)
                {
                    if (moves[i].Source != source)
                        continue;
                    tempConsumers.Add(moves[i]);
                    moves.RemoveAt(i);
                }
                EmitRiscVAsmParallelRegisterMoves(moves, instruction);
                foreach (var move in tempConsumers)
                {
                    LoadFromMemory(move.Destination, Sp, _allocation.Frame.ParallelCopyTempOffset, SizeOfRegisterType(move.Type), IsSignedIntegerType(move.Type));
                    NormalizeScalarRegister(move.Destination, move.Type);
                }
            }
        }

        private void RequireRiscVAsmTemp(QualifiedType type, LirInstruction instruction)
        {
            var required = AlignUp(
                Math.Max(_allocationOptions.SpillSlotSize, SizeOfRegisterType(type)),
                _allocationOptions.SpillSlotAlignment);
            if (_allocation.Frame.ParallelCopyTempSize < required)
                throw Unsupported(instruction, "RISC-V inline assembly parallel-copy spill slot is too small.");
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

        private sealed class RiscVAsmRegisterBinding
        {
            public GimpleAsmOperand Operand { get; }
            public LirVirtualRegister? Output { get; }
            public LirOperand? Input { get; }
            public QualifiedType Type => Output?.Type ?? Input!.Type;
            public RiscVAsmRegisterBinding? MatchingOutput { get; set; }
            public MachineRegister Register { get; set; }

            public RiscVAsmRegisterBinding(GimpleAsmOperand operand, LirVirtualRegister? output, LirOperand? input)
            {
                Operand = operand ?? throw new ArgumentNullException(nameof(operand));
                Output = output;
                Input = input;
                Register = MachineRegister.Invalid;
            }
        }

        private sealed class RiscVAsmInputMove
        {
            public MachineRegister Destination { get; }
            public LirOperand? SourceOperand { get; set; }
            public MachineRegister SourceRegister { get; set; }
            public QualifiedType Type { get; }
            public bool UsesTemp { get; set; }
            public bool HasRegisterSource => SourceRegister != MachineRegister.Invalid;

            public RiscVAsmInputMove(MachineRegister destination, LirOperand sourceOperand, QualifiedType type)
            {
                Destination = destination;
                SourceOperand = sourceOperand ?? throw new ArgumentNullException(nameof(sourceOperand));
                SourceRegister = MachineRegister.Invalid;
                Type = type;
            }
        }

        private readonly struct RiscVAsmRegisterMove
        {
            public MachineRegister Destination { get; }
            public MachineRegister Source { get; }
            public QualifiedType Type { get; }

            public RiscVAsmRegisterMove(MachineRegister destination, MachineRegister source, QualifiedType type)
            {
                Destination = destination;
                Source = source;
                Type = type;
            }
        }

        private void EmitInlineAssemblyText(LirInstruction instruction, string text)
        {
            try
            {
                _owner._text.EmitAssembly(text, _owner.CreateLocalLabel(_functionLabel + "_asm"), _owner._machineTarget);
                ClearIntegerRepresentationFacts();
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
                _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _allocationOptions.StackArgumentSlotSize);

            for (var i = 0; i < parameterIndex; i++)
            {
                var precedingParameter = functionType.Parameters[i];
                var precedingValue = CAbi.ClassifyValue(_owner._target, precedingParameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                _ = CAbi.AssignArgumentLocation(precedingValue, ref cursor, _allocationOptions.StackArgumentSlotSize);
            }

            var type = functionType.Parameters[parameterIndex].Type;
            var value = CAbi.ClassifyValue(_owner._target, type, isReturn: false, isVariadicUnnamedArgument: false);

            if (value.PassingKind == AbiPassingKind.Indirect)
            {
                var loc = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                if (loc.Kind == AbiLocationKind.Register)
                {
                    CopyMemory(destinationAddress, loc.Register, value.Size, BlockAlignment(type));
                }
                else if (loc.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(
                        GpScratch1,
                        Sp,
                        IncomingStackOffset(loc.StackByteOffset(_allocationOptions.StackArgumentSlotSize)),
                        _owner._target.PointerSize,
                        signed: false);
                    CopyMemory(destinationAddress, GpScratch1, value.Size, BlockAlignment(type));
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
                    var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _allocationOptions.StackArgumentSlotSize);
                    if (loc.Kind == AbiLocationKind.Register)
                    {
                        StoreRawBitsToAddress(loc.Register, destinationAddress, segment.Offset, segment.Size, BlockAlignment(type));
                    }
                    else if (loc.Kind == AbiLocationKind.Stack)
                    {
                        LoadRawBitsFromMemory(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_allocationOptions.StackArgumentSlotSize)), segment.Size, _owner._target.RegisterSize);
                        StoreRawBitsToAddress(GpScratch1, destinationAddress, segment.Offset, segment.Size, BlockAlignment(type));
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
                var loc = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                AddImmediate(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_allocationOptions.StackArgumentSlotSize)));
                CopyMemory(destinationAddress, GpScratch1, value.Size, BlockAlignment(type));
                return;
            }

            var scalarLocation = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
            var destination = GetWritableRegister(instruction.Result, PreferredScratch(type, GpScratch0, FpScratch0, VecScratch0));
            if (scalarLocation.Kind == AbiLocationKind.Register)
            {
                if (!IsFloatRegister(scalarLocation.Register) && !IsVectorRegister(scalarLocation.Register))
                    SetIntegerRepresentation(scalarLocation.Register, AbiScalarRepresentation(type));
                MoveRegister(destination, scalarLocation.Register, VectorGroupOf(type));
            }
            else if (scalarLocation.Kind == AbiLocationKind.Stack)
            {
                LoadFromMemory(destination, Sp, IncomingStackOffset(
                    scalarLocation.StackByteOffset(_allocationOptions.StackArgumentSlotSize)), SizeOfRegisterType(type), IsSignedIntegerType(type));
            }
            else
            {
                throw Unsupported(instruction, "Invalid scalar parameter ABI location.");
            }

            NormalizeResultRegister(destination, instruction.Result);
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
                ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
                return;
            }

            if (RequiresStackBackedScalar(instruction.Result.Type))
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
                return;
            }

            var destination = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type, GpScratch0, FpScratch0, VecScratch0));
            if (IsRiscVVectorType(instruction.Result.Type))
                ZeroVectorRegister(destination, VectorGroupOf(instruction.Result.Type));
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
                    ZeroMemory(address, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
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
            EmitIntegerBinary(instruction, dst);
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
                if (TryGetScaledPointerImmediate(instruction.Operands[1], PointerScale(lhsType), negate: false, out var offset))
                {
                    EmitImm(RVInstrKind.Addi, dst, ptr, offset);
                }
                else
                {
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    var scaled = ScaleIndex(GpScratch2, index, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(scaled)));
                }
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
                return true;
            }

            if (op == "+" && IsIntegerLike(lhsType) && IsPointerLike(rhsType))
            {
                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var ptr = LoadOperand(instruction.Operands[1], GpScratch1);
                if (TryGetScaledPointerImmediate(instruction.Operands[0], PointerScale(rhsType), negate: false, out var offset))
                {
                    EmitImm(RVInstrKind.Addi, dst, ptr, offset);
                }
                else
                {
                    var index = LoadOperand(instruction.Operands[0], GpScratch2);
                    var scaled = ScaleIndex(GpScratch2, index, PointerScale(rhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(scaled)));
                }
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
                return true;
            }

            if (op == "-" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
            {
                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                if (TryGetScaledPointerImmediate(instruction.Operands[1], PointerScale(lhsType), negate: true, out var offset))
                {
                    EmitImm(RVInstrKind.Addi, dst, ptr, offset);
                }
                else
                {
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    var scaled = ScaleIndex(GpScratch2, index, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), ToRegister(ptr), ToRegister(scaled)));
                }
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
                if (TryEmitPointerEqualityImmediate(instruction, dst))
                {
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }
                var left = LoadOperand(instruction.Operands[0], GpScratch1);
                var right = LoadOperand(instruction.Operands[1], GpScratch2);
                EmitEquality(dst, left, right, op == "==");
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
                return true;
            }

            return false;
        }

        private bool TryGetScaledPointerImmediate(LirOperand operand, int scale, bool negate, out int immediate)
        {
            if (!IsIntegerImmediate(operand))
            {
                immediate = 0;
                return false;
            }

            var operationBits = _owner._target.RegisterSize * 8;
            var bits = GetIntegerImmediateBits(operand, operationBits);
            var scaled = MaskIntegerBits(unchecked(bits * (ulong)scale), operationBits);
            if (negate)
                scaled = MaskIntegerBits(unchecked(0UL - scaled), operationBits);
            return TryEncodeSignedImmediate12(scaled, operationBits, out immediate);
        }

        private bool TryEmitPointerEqualityImmediate(LirInstruction instruction, MachineRegister dst)
        {
            var leftOperand = instruction.Operands[0];
            var rightOperand = instruction.Operands[1];
            if (!IsIntegerImmediate(rightOperand) && IsIntegerImmediate(leftOperand))
                (leftOperand, rightOperand) = (rightOperand, leftOperand);
            if (!IsIntegerImmediate(rightOperand)
                || !TryGetSignedImmediate12(rightOperand, _owner._target.RegisterSize * 8, out var immediate))
                return false;

            EmitEqualityImmediate(dst, LoadOperand(leftOperand, GpScratch1), immediate, instruction.Operator == "==");
            return true;
        }

        private void EmitIntegerBinary(LirInstruction instruction, MachineRegister dst)
        {
            if (TryEmitIntegerBinaryImmediate(instruction, dst))
                return;

            var left = LoadOperand(instruction.Operands[0], GpScratch1);
            var right = LoadOperand(instruction.Operands[1], GpScratch2);
            EmitIntegerBinaryRegisters(instruction, dst, left, right);
        }

        private bool TryEmitIntegerBinaryImmediate(LirInstruction instruction, MachineRegister dst)
        {
            var leftOperand = instruction.Operands[0];
            var rightOperand = instruction.Operands[1];
            var op = instruction.Operator;

            if (op is "*" or "/" or "%" && TryEmitPowerOfTwoArithmetic(instruction, dst, leftOperand))
                return true;
            if (op is "/" or "%" && TryEmitMagicDivide(instruction, dst, leftOperand))
                return true;

            if (rightOperand.Kind != LirOperandKind.Immediate && leftOperand.Kind == LirOperandKind.Immediate)
            {
                if (op is "+" or "&" or "|" or "^" or "==" or "!=")
                {
                    (leftOperand, rightOperand) = (rightOperand, leftOperand);
                }
                else
                {
                    var swappedRelation = op switch
                    {
                        "<" => ">",
                        "<=" => ">=",
                        ">" => "<",
                        ">=" => "<=",
                        _ => null,
                    };
                    if (swappedRelation is null)
                        return false;
                    (leftOperand, rightOperand) = (rightOperand, leftOperand);
                    op = swappedRelation;
                }
            }

            if (!IsIntegerImmediate(rightOperand))
                return false;

            var signed = IsSignedIntegerType(instruction.Operands[0].Type) || IsSignedIntegerType(instruction.Operands[1].Type);
            var wordOp = _owner._target.Is64Bit && Math.Max(SizeOf(instruction.Operands[0].Type), SizeOf(instruction.Operands[1].Type)) <= 4;
            var operationBits = wordOp ? 32 : _owner._target.RegisterSize * 8;

            switch (op)
            {
                case "+":
                    if (!TryGetSignedImmediate12(rightOperand, operationBits, out var addImmediate))
                        return false;
                    EmitImm(wordOp ? RVInstrKind.Addiw : RVInstrKind.Addi, dst, LoadOperand(leftOperand, GpScratch1), addImmediate);
                    return true;
                case "-":
                    if (!TryGetNegatedSignedImmediate12(rightOperand, operationBits, out var subtractImmediate))
                        return false;
                    EmitImm(wordOp ? RVInstrKind.Addiw : RVInstrKind.Addi, dst, LoadOperand(leftOperand, GpScratch1), subtractImmediate);
                    return true;
                case "&":
                case "|":
                case "^":
                    if (!TryGetSignedImmediate12(rightOperand, operationBits, out var bitwiseImmediate))
                        return false;
                    var bitwiseOpcode = op == "&" ? RVInstrKind.Andi : op == "|" ? RVInstrKind.Ori : RVInstrKind.Xori;
                    EmitImm(bitwiseOpcode, dst, LoadOperand(leftOperand, GpScratch1), bitwiseImmediate);
                    return true;
                case "<<":
                case ">>":
                    var shiftBits = wordOp ? 32 : _owner._target.RegisterSize * 8;
                    var shiftAmount = GetShiftImmediate(rightOperand, shiftBits);
                    // A shift takes no usual arithmetic conversions: only the left operand decides
                    // whether the right shift is arithmetic
                    var shiftSigned = IsSignedIntegerType(instruction.Operands[0].Type);
                    var shiftOpcode = op == "<<"
                        ? (wordOp ? RVInstrKind.Slliw : RVInstrKind.Slli)
                        : shiftSigned
                            ? (wordOp ? RVInstrKind.Sraiw : RVInstrKind.Srai)
                            : (wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli);
                    EmitShiftImmediate(shiftOpcode, dst, LoadOperand(leftOperand, GpScratch1), shiftAmount);
                    return true;
                case "==":
                case "!=":
                    if (!TryGetSignedImmediate12(rightOperand, _owner._target.RegisterSize * 8, out var equalityImmediate))
                        return false;
                    EmitEqualityImmediate(dst, LoadOperand(leftOperand, GpScratch1), equalityImmediate, op == "==");
                    return true;
                case "<":
                case ">=":
                    if (!TryGetSignedImmediate12(rightOperand, _owner._target.RegisterSize * 8, out var relationImmediate))
                        return false;
                    EmitImm(signed ? RVInstrKind.Slti : RVInstrKind.Sltiu, dst, LoadOperand(leftOperand, GpScratch1), relationImmediate);
                    if (op == ">=")
                        EmitImm(RVInstrKind.Xori, dst, dst, 1);
                    return true;
                case "<=":
                case ">":
                    if (!TryGetSignedImmediate12(rightOperand, _owner._target.RegisterSize * 8, out var boundImmediate)
                        || !TryGetComparisonSuccessorImmediate(boundImmediate, signed, out var successorImmediate))
                        return false;
                    EmitImm(signed ? RVInstrKind.Slti : RVInstrKind.Sltiu, dst, LoadOperand(leftOperand, GpScratch1), successorImmediate);
                    if (op == ">")
                        EmitImm(RVInstrKind.Xori, dst, dst, 1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Replaces a division by a constant with a multiply by its reciprocal</summary>
        private bool TryEmitMagicDivide(LirInstruction instruction, MachineRegister dst, LirOperand leftOperand)
        {
            if (instruction.Operator is not "/" and not "%")
                return false;
            if (!LirStrengthReduction.TryGetMagicDivisor(instruction, _owner._target, out var magic))
                return false;

            RequireM(instruction);
            var signed = IsSignedIntegerType(instruction.Operands[0].Type);
            var wordOp = _owner._target.Is64Bit && SizeOf(instruction.Operands[0].Type) <= 4;
            var bits = wordOp ? 32 : _owner._target.RegisterSize * 8;

            var dividend = LoadOperand(leftOperand, GpScratch1);
            if (dividend != GpScratch1)
            {
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(dividend), RVRegister.X0));
                dividend = GpScratch1;
            }

            var quotient = GpScratch2;
            LoadImmediate(GpScratch3, magic.Multiplier);
            if (wordOp)
            {
                // Both halves already sit widened in a full register, so one 64-bit multiply holds the product
                Emit(RVInstruction.R(RVInstrKind.Mul, ToRegister(quotient), ToRegister(dividend), ToRegister(GpScratch3)));
                EmitShiftImmediate(signed ? RVInstrKind.Srai : RVInstrKind.Srli, quotient, quotient, 32);
            }
            else
            {
                Emit(RVInstruction.R(signed ? RVInstrKind.Mulh : RVInstrKind.Mulhu, ToRegister(quotient), ToRegister(dividend), ToRegister(GpScratch3)));
            }

            var add = wordOp ? RVInstrKind.Addw : RVInstrKind.Add;
            var sub = wordOp ? RVInstrKind.Subw : RVInstrKind.Sub;
            if (signed)
            {
                if (magic.AddDividend)
                    Emit(RVInstruction.R(add, ToRegister(quotient), ToRegister(quotient), ToRegister(dividend)));
                else if (magic.SubtractDividend)
                    Emit(RVInstruction.R(sub, ToRegister(quotient), ToRegister(quotient), ToRegister(dividend)));
                if (magic.Shift != 0)
                    EmitShiftImmediate(wordOp ? RVInstrKind.Sraiw : RVInstrKind.Srai, quotient, quotient, magic.Shift);
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, GpScratch3, quotient, bits - 1);
                Emit(RVInstruction.R(add, ToRegister(quotient), ToRegister(quotient), ToRegister(GpScratch3)));
            }
            else if (magic.AddDividend)
            {
                Emit(RVInstruction.R(sub, ToRegister(GpScratch3), ToRegister(dividend), ToRegister(quotient)));
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, GpScratch3, GpScratch3, 1);
                Emit(RVInstruction.R(add, ToRegister(quotient), ToRegister(quotient), ToRegister(GpScratch3)));
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, quotient, quotient, magic.Shift - 1);
            }
            else if (magic.Shift != 0)
            {
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, quotient, quotient, magic.Shift);
            }

            if (instruction.Operator == "%")
            {
                LoadImmediate(GpScratch3, ConvertIntegerConstant(instruction.Operands[1].Immediate));
                Emit(RVInstruction.R(wordOp ? RVInstrKind.Mulw : RVInstrKind.Mul, ToRegister(quotient), ToRegister(quotient), ToRegister(GpScratch3)));
                Emit(RVInstruction.R(sub, ToRegister(dst), ToRegister(dividend), ToRegister(quotient)));
                return true;
            }

            Emit(RVInstruction.R(add, ToRegister(dst), ToRegister(quotient), RVRegister.X0));
            return true;
        }

        private bool TryEmitPowerOfTwoArithmetic(LirInstruction instruction, MachineRegister dst, LirOperand leftOperand)
        {
            var wordOp = _owner._target.Is64Bit && SizeOf(instruction.Operands[0].Type) <= 4;
            var bits = wordOp ? 32 : _owner._target.RegisterSize * 8;

            if (instruction.Operator == "*")
            {
                if (!LirStrengthReduction.TryGetPowerOfTwoFactor(instruction, _owner._target, out var factorShift))
                    return false;
                EmitShiftImmediate(wordOp ? RVInstrKind.Slliw : RVInstrKind.Slli, dst, LoadOperand(leftOperand, GpScratch1), factorShift);
                return true;
            }

            if (!LirStrengthReduction.TryGetPowerOfTwoDivisor(instruction, _owner._target, out var shift, out var negated))
                return false;

            var wantRemainder = instruction.Operator == "%";
            var dividend = LoadOperand(leftOperand, GpScratch1);
            if (!IsSignedIntegerType(instruction.Operands[0].Type))
            {
                if (!wantRemainder)
                    EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, dst, dividend, shift);
                else
                    EmitKeepLowBits(dst, dividend, shift, wordOp, bits);
                return true;
            }

            // The bias is 2^k - 1 for a negative dividend and zero otherwise, so the shift truncates toward zero
            if (shift == 1)
            {
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, GpScratch2, dividend, bits - 1);
            }
            else
            {
                EmitShiftImmediate(wordOp ? RVInstrKind.Sraiw : RVInstrKind.Srai, GpScratch2, dividend, bits - 1);
                EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, GpScratch2, GpScratch2, bits - shift);
            }

            Emit(RVInstruction.R(wordOp ? RVInstrKind.Addw : RVInstrKind.Add, ToRegister(GpScratch2), ToRegister(dividend), ToRegister(GpScratch2)));
            if (wantRemainder)
            {
                EmitClearLowBits(GpScratch2, GpScratch2, shift, wordOp, bits);
                Emit(RVInstruction.R(wordOp ? RVInstrKind.Subw : RVInstrKind.Sub, ToRegister(dst), ToRegister(dividend), ToRegister(GpScratch2)));
                return true;
            }

            EmitShiftImmediate(wordOp ? RVInstrKind.Sraiw : RVInstrKind.Srai, dst, GpScratch2, shift);
            if (negated)
                Emit(RVInstruction.R(wordOp ? RVInstrKind.Subw : RVInstrKind.Sub, ToRegister(dst), RVRegister.X0, ToRegister(dst)));
            return true;
        }

        // andi reaches twelve signed bits, and past that a pair of shifts still beats materializing the mask
        private void EmitKeepLowBits(MachineRegister destination, MachineRegister source, int shift, bool wordOp, int bits)
        {
            var mask = (1L << shift) - 1;
            if (FitsSignedImmediate(mask, 12))
            {
                EmitImm(RVInstrKind.Andi, destination, source, (int)mask);
                return;
            }
            EmitShiftImmediate(wordOp ? RVInstrKind.Slliw : RVInstrKind.Slli, destination, source, bits - shift);
            EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, destination, destination, bits - shift);
        }

        private void EmitClearLowBits(MachineRegister destination, MachineRegister source, int shift, bool wordOp, int bits)
        {
            var mask = -(1L << shift);
            if (FitsSignedImmediate(mask, 12))
            {
                EmitImm(RVInstrKind.Andi, destination, source, (int)mask);
                return;
            }
            EmitShiftImmediate(wordOp ? RVInstrKind.Srliw : RVInstrKind.Srli, destination, source, shift);
            EmitShiftImmediate(wordOp ? RVInstrKind.Slliw : RVInstrKind.Slli, destination, destination, shift);
        }

        private void EmitIntegerBinaryRegisters(LirInstruction instruction, MachineRegister dst, MachineRegister left, MachineRegister right)
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
                    // Only the left operand decides whether the right shift is arithmetic
                    Emit(RVInstruction.R(IsSignedIntegerType(instruction.Operands[0].Type)
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

        private void EmitEqualityImmediate(MachineRegister dst, MachineRegister left, int immediate, bool equal)
        {
            if (immediate == 0)
            {
                if (equal)
                    EmitImm(RVInstrKind.Sltiu, dst, left, 1);
                else
                    Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(dst), RVRegister.X0, ToRegister(left)));
                return;
            }

            EmitImm(RVInstrKind.Xori, dst, left, immediate);
            if (equal)
                EmitImm(RVInstrKind.Sltiu, dst, dst, 1);
            else
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(dst), RVRegister.X0, ToRegister(dst)));
        }

        private static bool IsIntegerImmediate(LirOperand operand)
            => operand.Kind == LirOperandKind.Immediate && operand.Immediate is not string && !IsFloatType(operand.Type);

        private bool IsZeroIntegerImmediate(LirOperand operand)
            => IsIntegerImmediate(operand) && GetIntegerImmediateBits(operand, 64) == 0;

        private bool TryGetSignedImmediate12(LirOperand operand, int operationBits, out int immediate)
            => TryEncodeSignedImmediate12(GetIntegerImmediateBits(operand, operationBits), operationBits, out immediate);

        private bool TryGetNegatedSignedImmediate12(LirOperand operand, int operationBits, out int immediate)
        {
            var bits = GetIntegerImmediateBits(operand, operationBits);
            var negated = MaskIntegerBits(unchecked(0UL - bits), operationBits);
            return TryEncodeSignedImmediate12(negated, operationBits, out immediate);
        }

        private ulong GetIntegerImmediateBits(LirOperand operand, int operationBits)
        {
            if (!IsIntegerImmediate(operand))
                throw new InvalidOperationException("Expected integer immediate operand.");

            var registerBits = _owner._target.RegisterSize * 8;
            var raw = unchecked((ulong)ConvertIntegerConstant(operand.Immediate));
            if (IsIntegerLike(operand.Type))
            {
                var typeBits = checked(SizeOf(operand.Type) * 8);
                if (typeBits > 0 && typeBits < registerBits)
                {
                    var typeMask = (1UL << typeBits) - 1;
                    raw &= typeMask;
                    if (IsSignedIntegerType(operand.Type) && (raw & (1UL << (typeBits - 1))) != 0)
                        raw |= ~typeMask;
                }
            }

            return MaskIntegerBits(raw, operationBits);
        }

        private int GetShiftImmediate(LirOperand operand, int operationBits)
            => (int)(GetIntegerImmediateBits(operand, operationBits) & (ulong)(operationBits - 1));

        private static bool TryEncodeSignedImmediate12(ulong bits, int operationBits, out int immediate)
        {
            bits = MaskIntegerBits(bits, operationBits);
            var low = (int)(bits & 0xfffUL);
            immediate = (low & 0x800) != 0 ? low - 0x1000 : low;
            return MaskIntegerBits(unchecked((ulong)(long)immediate), operationBits) == bits;
        }

        private static bool TryGetComparisonSuccessorImmediate(int immediate, bool signed, out int successor)
        {
            if (!signed && immediate == -1)
            {
                successor = 0;
                return false;
            }

            var candidate = (long)immediate + 1;
            if (!FitsSignedImmediate(candidate, 12))
            {
                successor = 0;
                return false;
            }

            successor = (int)candidate;
            return true;
        }

        private static ulong MaskIntegerBits(ulong value, int bits)
            => bits >= 64 ? value : value & ((1UL << bits) - 1);

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
            NormalizeResultRegister(dst, instruction.Result);
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
                CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
                return;
            }

            if (RequiresStackBackedScalar(instruction.Result.Type))
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                var sourceAddress = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                if (sourceAddress.Offset != 0)
                {
                    AddImmediate(GpScratch1, sourceAddress.BaseRegister, sourceAddress.Offset);
                    CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
                }
                else
                {
                    CopyMemory(destinationAddress, sourceAddress.BaseRegister, SizeOf(instruction.Result.Type), BlockAlignment(instruction.Result.Type));
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
                CopyMemory(GpScratch0, GpScratch1, SizeOf(storeType), BlockAlignment(storeType));
                return;
            }

            if (RequiresStackBackedScalar(storeType))
            {
                var destinationAddress = BuildAddress(instruction.Address, GpScratch0, GpScratch2);
                var sourceAddress = MaterializeScalarStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                if (destinationAddress.Offset != 0)
                {
                    AddImmediate(GpScratch0, destinationAddress.BaseRegister, destinationAddress.Offset);
                    CopyMemory(GpScratch0, sourceAddress, SizeOf(storeType), BlockAlignment(storeType));
                }
                else
                {
                    CopyMemory(destinationAddress.BaseRegister, sourceAddress, SizeOf(storeType), BlockAlignment(storeType));
                }
                return;
            }

            var storeSize = Math.Min(SizeOfRegisterType(storeType), SizeOf(storeType));
            var scratch = PreferredScratch(storeType, GpScratch0, FpScratch0, VecScratch0);
            var src = TryLoadOperandForStore(instruction.Operands[0], storeType, storeSize, scratch, out var narrowed)
                ? narrowed
                : LoadOperandAs(instruction.Operands[0], storeType, scratch, instruction);
            var address = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
            StoreToMemory(src, address.BaseRegister, address.Offset, storeSize);
        }

        /// <summary>Loads a value widened only as far as the store reads, since it keeps no more</summary>
        private bool TryLoadOperandForStore(
            LirOperand operand,
            QualifiedType storeType,
            int storeSize,
            MachineRegister scratch,
            out MachineRegister source)
        {
            source = default;
            if (!IsIntegerLike(operand.Type) || IsPointerLike(operand.Type) || IsFloatType(operand.Type))
                return false;
            if (!IsIntegerLike(storeType) || IsPointerLike(storeType) || IsFloatType(storeType))
                return false;
            if (RequiresSoftwareScalar(operand.Type) || RequiresSoftwareScalar(storeType))
                return false;
            if (storeSize <= 0 || storeSize > _owner._target.RegisterSize)
                return false;

            source = ExtendOperandToWidth(LoadOperand(operand, scratch), operand.Type, storeSize * 8, scratch);
            return true;
        }

        private void EmitZeroMemory(LirInstruction instruction)
        {
            if (instruction.Address is null)
                throw Unsupported(instruction, "Invalid zeromem instruction.");
            var size = instruction.Operands.Length == 0 ? SizeOf(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
            MaterializeAddress(instruction.Address, GpScratch0);
            ZeroMemory(GpScratch0, size, BlockAlignment(instruction.Address.ElementType));
        }

        private void EmitCall(LirInstruction instruction)
        {
            if (instruction.Operands.Length == 0)
                throw Unsupported(instruction, "Call has no callee operand.");
            if (TryEmitRiscVVectorIntrinsic(instruction))
                return;

            var preservations = _allocation.GetCallPreservations(_currentInstructionPosition);
            SaveCallPreservations(preservations);
            _useCallPreservationSources = preservations.Length != 0;
            try
            {
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
                if (preservation.UsesRegister)
                {
                    MoveRegister(preservation.PreservationRegister, preservation.PhysicalRegister, VectorGroupOf(preservation.Register.Type));
                    continue;
                }

                var size = Math.Min(SizeOfRegisterType(preservation.Register.Type), SizeOf(preservation.Register.Type));
                StoreToMemory(preservation.PhysicalRegister, Sp, preservation.StackOffset, size);
            }
        }

        private void RestoreCallPreservations(ImmutableArray<CallPreservation> preservations)
        {
            foreach (var preservation in preservations)
            {
                if (preservation.UsesRegister)
                {
                    MoveRegister(preservation.PhysicalRegister, preservation.PreservationRegister, VectorGroupOf(preservation.Register.Type));
                    continue;
                }

                var size = Math.Min(SizeOfRegisterType(preservation.Register.Type), SizeOf(preservation.Register.Type));
                LoadFromMemory(preservation.PhysicalRegister, Sp, preservation.StackOffset, size, IsSignedIntegerType(preservation.Register.Type));
                NormalizeScalarRegister(preservation.PhysicalRegister, preservation.Register.Type);
            }
        }

        private bool TryEmitRiscVVectorIntrinsic(LirInstruction instruction)
        {
            if (!IsRiscVVectorIntrinsicCall(instruction))
                return false;

            if (!_owner._machineTarget.HasV)
                throw Unsupported(instruction, "RISC-V vector intrinsic requires the V extension.");

            var function = (FunctionSymbol)instruction.Operands[0].Symbol!;
            var name = function.Name.Substring("__riscv_".Length);

            // A reshaping intrinsic renames a value rather than computing one
            if (name.StartsWith("vreinterpret_", StringComparison.Ordinal) ||
                name.StartsWith("vlmul_ext_", StringComparison.Ordinal) ||
                name.StartsWith("vlmul_trunc_", StringComparison.Ordinal) ||
                name.StartsWith("vundefined_", StringComparison.Ordinal))
            {
                EmitVectorReshapeIntrinsic(instruction);
                return true;
            }

            if (!TryGetRiscVVectorShape(name, out var shape))
                throw Unsupported(instruction, $"Cannot determine the vector shape for intrinsic '{function.Name}'.");
            ValidateVectorFloatingPointSupport(instruction, shape);

            if (name.StartsWith("vsetvlmax_", StringComparison.Ordinal))
            {
                RequireIntrinsicOperandCount(instruction, 1);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "vsetvlmax intrinsic has no result.");
                var destination = GetWritableRegister(instruction.Result, GpScratch0);
                EmitVectorConfiguration(destination, MachineRegister.X0, shape);
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
                EmitVectorConfiguration(destination, avl, shape);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return true;
            }

            if (name.StartsWith("vmv_v_", StringComparison.Ordinal))
            {
                EmitVectorMoveIntrinsic(instruction, name, shape);
                return true;
            }

            if (name.StartsWith("vmv_x_s_", StringComparison.Ordinal))
            {
                EmitVectorExtractIntrinsic(instruction, shape);
                return true;
            }

            if (name.StartsWith("vmv_s_x_", StringComparison.Ordinal))
            {
                EmitVectorInsertIntrinsic(instruction, shape);
                return true;
            }

            if (name.StartsWith("vfmv_f_s_", StringComparison.Ordinal))
            {
                RequireIntrinsicOperandCount(instruction, 2);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "vfmv.f.s intrinsic has no result.");
                var element = LoadOperand(instruction.Operands[1], VecScratch0);
                var scalar = GetWritableRegister(instruction.Result, FpScratch0);
                EmitVectorMaxConfiguration(shape);
                Emit(RVInstruction.Vx(RVInstrKind.VfmvFS, ToFloatRegister(scalar), ToVectorRegister(element), RVRegister.X0));
                StoreWritableRegisterIfSpilled(instruction.Result, scalar);
                return true;
            }

            if (name.StartsWith("vfmv_s_f_", StringComparison.Ordinal) || name.StartsWith("vfmv_v_f_", StringComparison.Ordinal))
            {
                RequireIntrinsicOperandCount(instruction, 3);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Float vector move intrinsic has no result.");
                var scalar = LoadOperand(instruction.Operands[1], FpScratch0);
                var vl = LoadOperand(instruction.Operands[2], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                var splat = name[5] == 's' ? RVInstrKind.VfmvSF : RVInstrKind.VfmvVF;
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vx(splat, ToVectorRegister(destination), RVRegister.V0, ToFloatRegister(scalar)));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return true;
            }

            var policy = VectorPolicy.Parse(ref name);

            // The extension spellings the vector API defines in terms of an add or a shift
            if (name.StartsWith("vwcvt_x_x_v_", StringComparison.Ordinal) ||
                name.StartsWith("vwcvtu_x_x_v_", StringComparison.Ordinal) ||
                name.StartsWith("vncvt_x_x_w_", StringComparison.Ordinal))
            {
                var head = name.Substring(0, name.IndexOf('_'));
                EmitVectorUnaryIntrinsic(instruction, head switch
                {
                    "vwcvt" => RVInstrKind.VwaddVx,
                    "vwcvtu" => RVInstrKind.VwadduVx,
                    _ => RVInstrKind.VnsrlWx,
                }, head, "v", head == "vncvt" ? shape : NarrowVectorShape(instruction, shape), policy, VectorAliasOperand.Zero);
                return true;
            }

            // A float conversion spells its direction over several pieces, so it splits differently
            bool wideningConvert = name.StartsWith("vfwcvt_", StringComparison.Ordinal);
            bool narrowingConvert = name.StartsWith("vfncvt_", StringComparison.Ordinal);
            if (wideningConvert || narrowingConvert || name.StartsWith("vfcvt_", StringComparison.Ordinal))
            {
                var tail = name.LastIndexOf(narrowingConvert ? "_w_" : "_v_", StringComparison.Ordinal);
                if (tail < 0)
                    throw Unsupported(instruction, $"Invalid RISC-V vector intrinsic name '{function.Name}'.");
                EmitVectorUnaryIntrinsic(instruction, VectorIntrinsicOpcode(name.Substring(0, tail + 2)), "vfcvt", "v",
                    wideningConvert ? NarrowVectorShape(instruction, shape) : shape, policy);
                return true;
            }

            var firstSeparator = name.IndexOf('_');
            var secondSeparator = firstSeparator < 0 ? -1 : name.IndexOf('_', firstSeparator + 1);
            if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1)
                throw Unsupported(instruction, $"Invalid RISC-V vector intrinsic name '{function.Name}'.");

            var mnemonic = name.Substring(0, firstSeparator);
            var form = name.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1);

            if (mnemonic.Length > 4 && mnemonic.StartsWith("vle", StringComparison.Ordinal) &&
                mnemonic.EndsWith("ff", StringComparison.Ordinal))
            {
                EmitVectorFaultOnlyFirstIntrinsic(instruction, mnemonic, shape);
                return true;
            }

            if (IsVectorMemoryMnemonic(mnemonic, "vle"))
            {
                RequireIntrinsicOperandCount(instruction, 3);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Vector load intrinsic has no result.");
                var address = LoadOperand(instruction.Operands[1], GpScratch0);
                var vl = LoadOperand(instruction.Operands[2], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vl(VectorLoadOpcode(shape.ElementWidth), ToVectorRegister(destination), ToRegister(address)));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return true;
            }

            if (IsVectorMemoryMnemonic(mnemonic, "vlse") || IsVectorMemoryMnemonic(mnemonic, "vsse"))
            {
                EmitVectorStridedIntrinsic(instruction, mnemonic[1] == 'l', shape);
                return true;
            }

            if (mnemonic.Contains("seg", StringComparison.Ordinal))
            {
                EmitVectorSegmentIntrinsic(instruction, mnemonic, shape);
                return true;
            }

            if (mnemonic is "vcreate" or "vget" or "vset")
            {
                EmitVectorTupleIntrinsic(instruction, mnemonic);
                return true;
            }

            if (mnemonic.StartsWith("vluxei", StringComparison.Ordinal) || mnemonic.StartsWith("vloxei", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vsuxei", StringComparison.Ordinal) || mnemonic.StartsWith("vsoxei", StringComparison.Ordinal))
            {
                EmitVectorIndexedIntrinsic(instruction, mnemonic, mnemonic[1] == 'l', shape);
                return true;
            }

            if (IsWholeRegisterMnemonic(mnemonic))
            {
                EmitVectorWholeRegisterIntrinsic(instruction, mnemonic);
                return true;
            }

            if (mnemonic == "vlm" || mnemonic == "vsm")
            {
                EmitVectorMaskMemoryIntrinsic(instruction, mnemonic == "vlm", shape);
                return true;
            }

            if (IsVectorMemoryMnemonic(mnemonic, "vse"))
            {
                RequireIntrinsicOperandCount(instruction, 4);
                var address = LoadOperand(instruction.Operands[1], GpScratch0);
                var source = LoadOperand(instruction.Operands[2], VecScratch0);
                var vl = LoadOperand(instruction.Operands[3], GpScratch1);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vs(VectorStoreOpcode(shape.ElementWidth), ToVectorRegister(source), ToRegister(address)));
                return true;
            }

            if (TryEmitVectorAliasIntrinsic(instruction, mnemonic, form, shape, policy))
                return true;

            var opcode = VectorIntrinsicOpcode($"{mnemonic}_{form}");
            // A widening operation names its destination but runs at the shape of its sources
            var configuration = IsWideningVectorMnemonic(mnemonic)
                ? NarrowVectorShape(instruction, shape)
                : shape;
            if (form is "vvm" or "vxm" or "vim" or "vfm")
                EmitVectorMergeIntrinsic(instruction, opcode, mnemonic, form, shape);
            else if (form is "v" or "vf2" or "vf4" or "vf8" or "m")
                EmitVectorUnaryIntrinsic(instruction, opcode, mnemonic, form, shape, policy);
            else if (form == "vm")
                EmitVectorCompressIntrinsic(instruction, opcode, shape);
            else if (mnemonic == "vslideup")
                EmitVectorSlideUpIntrinsic(instruction, opcode, form, shape);
            else if (IsVectorAccumulator(mnemonic))
                EmitVectorAccumulatorIntrinsic(instruction, opcode, form, configuration);
            else
                EmitVectorArithmeticIntrinsic(instruction, opcode, mnemonic, form, shape, policy, configuration);
            return true;
        }

        /// <summary>An operation whose third operand is the destination it accumulates onto</summary>
        private static bool IsVectorAccumulator(string mnemonic)
            => mnemonic is "vmadd" or "vnmsub" or "vmacc" or "vnmsac"
                or "vfmadd" or "vfnmadd" or "vfmsub" or "vfnmsub"
                or "vfmacc" or "vfnmacc" or "vfmsac" or "vfnmsac"
                or "vwmacc" or "vwmaccu" or "vwmaccsu" or "vwmaccus"
                or "vfwmacc" or "vfwnmacc" or "vfwmsac" or "vfwnmsac";

        /// <summary>A widening operation names its destination but runs at the shape of its sources</summary>
        private static bool IsWideningVectorMnemonic(string mnemonic)
            => (mnemonic.StartsWith("vw", StringComparison.Ordinal) && !mnemonic.StartsWith("vwred", StringComparison.Ordinal)) ||
                (mnemonic.StartsWith("vfw", StringComparison.Ordinal) && !mnemonic.StartsWith("vfwred", StringComparison.Ordinal));

        private static bool IsWholeRegisterMnemonic(string mnemonic)
            => mnemonic.Length > 4 && mnemonic[0] == 'v' && mnemonic[1] == 'l' && char.IsAsciiDigit(mnemonic[2]) &&
                    mnemonic[3] == 'r' && mnemonic[4] == 'e' ||
                mnemonic.Length == 4 && mnemonic[0] == 'v' && mnemonic[1] == 's' && char.IsAsciiDigit(mnemonic[2]) &&
                    mnemonic[3] == 'r';

        private VectorShape NarrowVectorShape(LirInstruction instruction, VectorShape shape)
        {
            if (shape.ElementWidth <= 8 || shape.LengthMultiplierLog2 <= -3)
                throw Unsupported(instruction, "A widening vector intrinsic has no narrower source shape.");
            return new VectorShape(shape.ElementWidth / 2, shape.LengthMultiplierLog2 - 1, shape.IsFloating);
        }

        /// <summary>vmv.v.x, vmv.v.v and vmv.v.i take no second source: the encoding reads v0 as their vs2.</summary>
        private void EmitVectorMoveIntrinsic(LirInstruction instruction, string name, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 3);
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector move intrinsic has no result.");

            var vl = LoadOperand(instruction.Operands[2], GpScratch1);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            if (name.StartsWith("vmv_v_i_", StringComparison.Ordinal))
            {
                var immediate = ImmediateToInt32(instruction.Operands[1]);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vi(RVInstrKind.VmvVi, ToVectorRegister(destination), RVRegister.V0, immediate));
            }
            else if (name.StartsWith("vmv_v_v_", StringComparison.Ordinal))
            {
                var source = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vv(RVInstrKind.VmvVv, ToVectorRegister(destination), RVRegister.V0, ToVectorRegister(source)));
            }
            else
            {
                var source = LoadVectorIntegerScalarOperand(instruction.Operands[1], GpScratch0, instruction);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vx(RVInstrKind.VmvVx, ToVectorRegister(destination), RVRegister.V0, ToRegister(source)));
            }
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>vmv.x.s reads element zero into an integer register whatever vl says.</summary>
        private void EmitVectorExtractIntrinsic(LirInstruction instruction, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 2);
            if (instruction.Result is null)
                throw Unsupported(instruction, "vmv.x.s intrinsic has no result.");

            var source = LoadOperand(instruction.Operands[1], VecScratch0);
            var destination = GetWritableRegister(instruction.Result, GpScratch0);
            EmitVectorMaxConfiguration(shape);
            Emit(RVInstruction.Vx(RVInstrKind.VmvXs, ToRegister(destination), ToVectorRegister(source), RVRegister.X0));
            SetIntegerRepresentation(destination, IntegerRepresentationFact.SignExtended(Math.Min(shape.ElementWidth, _owner._target.RegisterSize * 8)));
            NormalizeScalarRegister(destination, instruction.Result.Type);
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>vmv.s.x writes element zero; every other element of the group is a tail element.</summary>
        private void EmitVectorInsertIntrinsic(LirInstruction instruction, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 3);
            if (instruction.Result is null)
                throw Unsupported(instruction, "vmv.s.x intrinsic has no result.");

            var source = LoadVectorIntegerScalarOperand(instruction.Operands[1], GpScratch0, instruction);
            var vl = LoadOperand(instruction.Operands[2], GpScratch1);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            Emit(RVInstruction.Vx(RVInstrKind.VmvSx, ToVectorRegister(destination), RVRegister.V0, ToRegister(source)));
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>A merge reads its mask from v0 and writes every element, so it is never masked off.</summary>
        private void EmitVectorMergeIntrinsic(LirInstruction instruction, RVInstrKind opcode, string mnemonic, string form, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 5);
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector merge intrinsic has no result.");

            var vs2 = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
            var second = form == "vvm"
                ? LoadOperandOrVectorScratch(instruction.Operands[2], 2)
                : form == "vxm"
                    ? LoadVectorIntegerScalarOperand(instruction.Operands[2], GpScratch0, instruction)
                    : form == "vfm"
                        ? LoadOperand(instruction.Operands[2], FpScratch0)
                        : MachineRegister.Invalid;
            var immediate = form == "vim" ? ImmediateToInt32(instruction.Operands[2]) : 0;
            var mask = LoadOperand(instruction.Operands[3], MaskRegister);
            var vl = LoadOperand(instruction.Operands[4], GpScratch1);

            MoveVectorRegister(MaskRegister, mask);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            var written = RequiresIsolatedVectorDestination(mnemonic) ? VecScratch0 : destination;
            Emit(form switch
            {
                "vvm" => RVInstruction.Vv(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToVectorRegister(second), unmasked: false),
                "vfm" => RVInstruction.Vx(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToFloatRegister(second), unmasked: false),
                "vxm" => RVInstruction.Vx(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToRegister(second), unmasked: false),
                _ => RVInstruction.Vi(opcode, ToVectorRegister(written), ToVectorRegister(vs2), immediate, unmasked: false),
            });
            if (written != destination)
                MoveVectorRegister(destination, written, VectorGroupOf(instruction.Result.Type));
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        private void EmitVectorArithmeticIntrinsic(LirInstruction instruction, RVInstrKind opcode, string mnemonic, string form, VectorShape shape, VectorPolicy policy, VectorShape configuration,
            bool swapped = false)
        {
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector intrinsic has no result.");

            var index = 1;
            var mask = MachineRegister.Invalid;
            if (policy.Masked)
                mask = LoadOperand(instruction.Operands[index++], MaskRegister);
            var undisturbed = MachineRegister.Invalid;
            if (policy.KeepsDestination)
                undisturbed = LoadOperand(instruction.Operands[index++], VecScratch0);

            var vs2 = LoadOperandOrVectorScratch(instruction.Operands[index++], 1);
            MachineRegister second;
            if (form is "vv" or "vs" or "mm")
                second = LoadOperandOrVectorScratch(instruction.Operands[index], 2);
            else if (form is "vf" or "wf")
                second = LoadOperand(instruction.Operands[index], FpScratch0);
            else if (form == "vx")
                second = LoadVectorIntegerScalarOperand(instruction.Operands[index], GpScratch0, instruction);
            else if (form == "wv")
                second = LoadOperandOrVectorScratch(instruction.Operands[index], 2);
            else if (form == "wx")
                second = LoadVectorIntegerScalarOperand(instruction.Operands[index], GpScratch0, instruction);
            else if (form is "vi" or "wi")
                second = MachineRegister.Invalid;
            else
                throw Unsupported(instruction, $"Unsupported vector intrinsic operand form '{form}'.");
            var immediate = form is "vi" or "wi" ? ImmediateToInt32(instruction.Operands[index]) : 0;
            index++;
            var vl = LoadOperand(instruction.Operands[index++], GpScratch1);
            RequireIntrinsicOperandCount(instruction, index);

            if (policy.Masked)
                MoveVectorRegister(MaskRegister, mask);

            var group = VectorGroupOf(instruction.Result.Type);
            var isolated = policy.KeepsDestination || RequiresIsolatedVectorDestination(mnemonic);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            var written = isolated ? VecScratch0 : destination;
            if (policy.KeepsDestination)
                MoveVectorRegister(written, undisturbed, group);

            EmitVectorConfiguration(MachineRegister.X0, vl, configuration, policy);
            var unmasked = !policy.Masked;
            if (swapped)
                Emit(RVInstruction.Vv(opcode, ToVectorRegister(written), ToVectorRegister(second), ToVectorRegister(vs2), unmasked));
            else if (form is "vv" or "vs" or "wv" or "mm")
                Emit(RVInstruction.Vv(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToVectorRegister(second), unmasked));
            else if (form is "vf" or "wf")
                Emit(RVInstruction.Vx(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToFloatRegister(second), unmasked));
            else if (form is "vi" or "wi")
                Emit(RVInstruction.Vi(opcode, ToVectorRegister(written), ToVectorRegister(vs2), immediate, unmasked));
            else
                Emit(RVInstruction.Vx(opcode, ToVectorRegister(written), ToVectorRegister(vs2), ToRegister(second), unmasked));

            if (written != destination)
                MoveVectorRegister(destination, written, VectorGroupOf(instruction.Result.Type));
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        private void EmitVectorAccumulatorIntrinsic(LirInstruction instruction, RVInstrKind opcode, string form, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 5);
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector accumulator intrinsic has no result.");

            var oldDestination = LoadOperand(instruction.Operands[1], VecScratch0);
            MachineRegister vs1;
            if (form == "vv")
                vs1 = LoadOperandOrVectorScratch(instruction.Operands[2], 1);
            else if (form == "vf")
                vs1 = LoadOperand(instruction.Operands[2], FpScratch0);
            else if (form == "vx")
                vs1 = LoadVectorIntegerScalarOperand(instruction.Operands[2], GpScratch0, instruction);
            else
                throw Unsupported(instruction, $"Unsupported vector accumulator operand form '{form}'.");
            var vs2 = LoadOperandOrVectorScratch(instruction.Operands[3], 2);
            var vl = LoadOperand(instruction.Operands[4], GpScratch1);

            var group = VectorGroupOf(instruction.Result.Type);
            MoveVectorRegister(VecScratch0, oldDestination, group);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            if (form == "vv")
                Emit(RVInstruction.Vv(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToVectorRegister(vs1)));
            else if (form == "vf")
                Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToFloatRegister(vs1)));
            else
                Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToRegister(vs1)));

            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            MoveVectorRegister(destination, VecScratch0, group);
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>The spellings the vector API defines in terms of another instruction</summary>
        private bool TryEmitVectorAliasIntrinsic(LirInstruction instruction, string mnemonic, string form, VectorShape shape, VectorPolicy policy)
        {
            // A comparison the other way round is the same instruction reading its sources swapped
            if (form == "vv")
            {
                var reversed = mnemonic switch
                {
                    "vmsgt" => RVInstrKind.VmsltVv,
                    "vmsgtu" => RVInstrKind.VmsltuVv,
                    "vmsge" => RVInstrKind.VmsleVv,
                    "vmsgeu" => RVInstrKind.VmsleuVv,
                    "vmfgt" => RVInstrKind.VmfltVv,
                    "vmfge" => RVInstrKind.VmfleVv,
                    _ => default(RVInstrKind?),
                };
                if (reversed is not null)
                {
                    EmitVectorArithmeticIntrinsic(instruction, reversed.Value, mnemonic, form, shape, policy, shape, swapped: true);
                    return true;
                }
            }

            // A signed comparison with no encoding of its own is the opposite one, negated
            if (form == "vx" && mnemonic is "vmsge" or "vmsgeu")
            {
                EmitVectorArithmeticIntrinsic(instruction, mnemonic == "vmsge" ? RVInstrKind.VmsltVx : RVInstrKind.VmsltuVx,
                    mnemonic, form, shape, policy, shape);
                var negated = ToVectorRegister(GetWritableRegister(instruction.Result!, VecScratch0));
                Emit(RVInstruction.Vv(RVInstrKind.VmnandMm, negated, negated, negated));
                return true;
            }

            (RVInstrKind Opcode, VectorAliasOperand Second, bool Widening)? alias = (mnemonic, form) switch
            {
                ("vneg", "v") => (RVInstrKind.VrsubVx, VectorAliasOperand.Zero, false),
                ("vnot", "v") => (RVInstrKind.VxorVi, VectorAliasOperand.AllOnes, false),
                ("vfneg", "v") => (RVInstrKind.VfsgnjnVv, VectorAliasOperand.SameSource, false),
                ("vfabs", "v") => (RVInstrKind.VfsgnjxVv, VectorAliasOperand.SameSource, false),
                ("vmmv", "m") => (RVInstrKind.VmandMm, VectorAliasOperand.SameSource, false),
                ("vmnot", "m") => (RVInstrKind.VmnandMm, VectorAliasOperand.SameSource, false),
                ("vmclr", "m") => (RVInstrKind.VmxorMm, VectorAliasOperand.SameSource, false),
                ("vmset", "m") => (RVInstrKind.VmxnorMm, VectorAliasOperand.SameSource, false),
                _ => null,
            };
            if (alias is null)
                return false;

            EmitVectorUnaryIntrinsic(instruction, alias.Value.Opcode, mnemonic, form,
                alias.Value.Widening ? NarrowVectorShape(instruction, shape) : shape, policy, alias.Value.Second);
            return true;
        }

        /// <summary>A segment access moves one field of every element before the next one</summary>
        private void EmitVectorSegmentIntrinsic(LirInstruction instruction, string mnemonic, VectorShape shape)
        {
            bool load = mnemonic[1] == 'l';
            bool strided = mnemonic.StartsWith("vlsseg", StringComparison.Ordinal) || mnemonic.StartsWith("vssseg", StringComparison.Ordinal);
            bool indexed = mnemonic.Contains("ei", StringComparison.Ordinal);
            bool faultOnlyFirst = mnemonic.EndsWith("ff", StringComparison.Ordinal);
            var opcode = VectorIntrinsicOpcode(mnemonic + "_v");

            var address = LoadOperand(instruction.Operands[1], GpScratch0);
            var second = strided
                ? LoadOperand(instruction.Operands[2], GpVectorConfigScratch)
                : indexed
                    ? LoadOperandOrVectorScratch(instruction.Operands[2], 1)
                    : MachineRegister.Invalid;
            var index = strided || indexed || faultOnlyFirst ? 3 : 2;
            var report = faultOnlyFirst ? LoadOperand(instruction.Operands[2], GpScratch2) : MachineRegister.Invalid;

            if (load)
            {
                RequireIntrinsicOperandCount(instruction, index + 1);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Segment vector load intrinsic has no result.");
                var vl = LoadOperand(instruction.Operands[index], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(strided
                    ? RVInstruction.Vls(opcode, ToVectorRegister(VecScratch0), ToRegister(address), ToRegister(second))
                    : indexed
                        ? RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(second), ToRegister(address))
                        : RVInstruction.Vl(opcode, ToVectorRegister(VecScratch0), ToRegister(address)));
                if (faultOnlyFirst)
                {
                    Emit(new RVInstruction(RVInstrKind.Csrrs, ToRegister(GpScratch3), RVRegister.X0, RVRegister.Invalid, (int)RVCsr.VL));
                    Emit(RVInstruction.S(_owner._target.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                        ToRegister(GpScratch3), ToRegister(report), 0));
                    _vectorConfigType = -1;
                }
                if (VecScratch0 != destination)
                    MoveVectorRegister(destination, VecScratch0, VectorGroupOf(instruction.Result.Type));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return;
            }

            RequireIntrinsicOperandCount(instruction, index + 2);
            var source = LoadOperand(instruction.Operands[index], VecScratch0);
            var length = LoadOperand(instruction.Operands[index + 1], GpScratch1);
            EmitVectorConfiguration(MachineRegister.X0, length, shape);
            Emit(strided
                ? RVInstruction.Vls(opcode, ToVectorRegister(source), ToRegister(address), ToRegister(second))
                : indexed
                    ? RVInstruction.Vx(opcode, ToVectorRegister(source), ToVectorRegister(second), ToRegister(address))
                    : RVInstruction.Vs(opcode, ToVectorRegister(source), ToRegister(address)));
        }

        /// <summary>Building a tuple, reading a field out of one and writing a field into one</summary>
        private void EmitVectorTupleIntrinsic(LirInstruction instruction, string mnemonic)
        {
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector tuple intrinsic has no result.");

            if (mnemonic == "vget")
            {
                RequireIntrinsicOperandCount(instruction, 3);
                var tuple = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
                var field = VectorGroupOf(instruction.Result.Type);
                var slot = ImmediateToInt32(instruction.Operands[2]);
                var picked = GetWritableRegister(instruction.Result, VecScratch0);
                MoveVectorRegister(picked, (MachineRegister)((int)tuple + slot * field), field);
                StoreWritableRegisterIfSpilled(instruction.Result, picked);
                return;
            }

            // A source may live where the tuple is going, so the tuple is built in the scratch group first
            var registers = VectorGroupOf(instruction.Result.Type);
            if (mnemonic == "vcreate")
            {
                var field = VectorGroupOf(instruction.Operands[1].Type);
                RequireIntrinsicOperandCount(instruction, 1 + registers / field);
                for (var i = 1; i < instruction.Operands.Length; i++)
                {
                    var source = LoadOperandOrVectorScratch(instruction.Operands[i], 1);
                    MoveVectorRegister((MachineRegister)((int)VecScratch0 + (i - 1) * field), source, field);
                }
            }
            else
            {
                RequireIntrinsicOperandCount(instruction, 4);
                var whole = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
                MoveVectorRegister(VecScratch0, whole, registers);
                var written = LoadOperandOrVectorScratch(instruction.Operands[3], 1);
                var width = VectorGroupOf(instruction.Operands[3].Type);
                MoveVectorRegister((MachineRegister)((int)VecScratch0 + ImmediateToInt32(instruction.Operands[2]) * width), written, width);
            }

            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            MoveVectorRegister(destination, VecScratch0, registers);
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>An indexed access reads its byte offsets out of a second vector</summary>
        private void EmitVectorIndexedIntrinsic(LirInstruction instruction, string mnemonic, bool load, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, load ? 4 : 5);
            var opcode = VectorIntrinsicOpcode(mnemonic + "_v");
            var address = LoadOperand(instruction.Operands[1], GpScratch0);
            var indices = LoadOperandOrVectorScratch(instruction.Operands[2], 1);
            if (load)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Indexed vector load intrinsic has no result.");
                var vl = LoadOperand(instruction.Operands[3], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(indices), ToRegister(address)));
                if (VecScratch0 != destination)
                    MoveVectorRegister(destination, VecScratch0, VectorGroupOf(instruction.Result.Type));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return;
            }

            var source = LoadOperand(instruction.Operands[3], VecScratch0);
            var length = LoadOperand(instruction.Operands[4], GpScratch1);
            EmitVectorConfiguration(MachineRegister.X0, length, shape);
            Emit(RVInstruction.Vx(opcode, ToVectorRegister(source), ToVectorRegister(indices), ToRegister(address)));
        }

        /// <summary>A fault only first load shortens vl itself, and hands the new one back</summary>
        private void EmitVectorFaultOnlyFirstIntrinsic(LirInstruction instruction, string mnemonic, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 4);
            if (instruction.Result is null)
                throw Unsupported(instruction, "Fault only first vector load intrinsic has no result.");

            var address = LoadOperand(instruction.Operands[1], GpScratch0);
            var report = LoadOperand(instruction.Operands[2], GpScratch2);
            var vl = LoadOperand(instruction.Operands[3], GpScratch1);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            Emit(RVInstruction.Vl(VectorIntrinsicOpcode(mnemonic + "_v"), ToVectorRegister(destination), ToRegister(address)));
            Emit(new RVInstruction(RVInstrKind.Csrrs, ToRegister(GpScratch3), RVRegister.X0, RVRegister.Invalid, (int)RVCsr.VL));
            Emit(RVInstruction.S(_owner._target.Is64Bit ? RVInstrKind.Sd : RVInstrKind.Sw,
                ToRegister(GpScratch3), ToRegister(report), 0));
            // The load decided vl for itself, so what the cache remembers no longer holds
            _vectorConfigType = -1;
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>A whole register access moves a fixed count of registers and reads no vtype</summary>
        private void EmitVectorWholeRegisterIntrinsic(LirInstruction instruction, string mnemonic)
        {
            if (mnemonic[1] == 'l')
            {
                RequireIntrinsicOperandCount(instruction, 2);
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Whole register vector load intrinsic has no result.");
                var address = LoadOperand(instruction.Operands[1], GpScratch0);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                Emit(RVInstruction.Vl(VectorIntrinsicOpcode(mnemonic + "_v"), ToVectorRegister(destination), ToRegister(address)));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return;
            }

            RequireIntrinsicOperandCount(instruction, 3);
            var target = LoadOperand(instruction.Operands[1], GpScratch0);
            var source = LoadOperand(instruction.Operands[2], VecScratch0);
            Emit(RVInstruction.Vs(VectorIntrinsicOpcode(mnemonic + "_v"), ToVectorRegister(source), ToRegister(target)));
        }

        /// <summary>A reshaping intrinsic renames registers: it reads one value and calls it another</summary>
        private void EmitVectorReshapeIntrinsic(LirInstruction instruction)
        {
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector reshaping intrinsic has no result.");

            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            if (instruction.Operands.Length > 1)
            {
                var source = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
                var carried = Math.Min(VectorGroupOf(instruction.Result.Type), VectorGroupOf(instruction.Operands[1].Type));
                MoveVectorRegister(destination, source, carried);
            }
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }




        private static bool IsVectorMemoryMnemonic(string mnemonic, string prefix)
            => mnemonic.Length > prefix.Length && mnemonic.StartsWith(prefix, StringComparison.Ordinal) &&
                char.IsAsciiDigit(mnemonic[prefix.Length]);

        /// <summary>The unary forms, whose one source may be shaped unlike the destination</summary>
        /// <summary>What an aliased operation puts where the instruction it stands for reads its second source</summary>
        private enum VectorAliasOperand
        {
            None,
            Zero,
            AllOnes,
            SameSource,
        }

        private void EmitVectorUnaryIntrinsic(LirInstruction instruction, RVInstrKind opcode, string mnemonic, string form, VectorShape shape, VectorPolicy policy,
            VectorAliasOperand second = VectorAliasOperand.None)
        {
            if (instruction.Result is null)
                throw Unsupported(instruction, "Vector unary intrinsic has no result.");

            var index = 1;
            var mask = MachineRegister.Invalid;
            if (policy.Masked)
                mask = LoadOperand(instruction.Operands[index++], MaskRegister);
            var undisturbed = MachineRegister.Invalid;
            if (policy.KeepsDestination)
                undisturbed = LoadOperandOrVectorScratch(instruction.Operands[index++], 1);
            var source = mnemonic is "vid" or "vmclr" or "vmset"
                ? MachineRegister.Invalid
                : LoadOperandOrVectorScratch(instruction.Operands[index++], 2);
            var vl = LoadOperand(instruction.Operands[index++], GpScratch1);
            RequireIntrinsicOperandCount(instruction, index);

            if (policy.Masked)
                MoveVectorRegister(MaskRegister, mask);

            // A count over a mask lands in an integer register rather than a vector one
            if (form == "m" && mnemonic is "vcpop" or "vfirst")
            {
                var counted = GetWritableRegister(instruction.Result, GpScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape, policy);
                Emit(RVInstruction.Vx(opcode, ToRegister(counted), ToVectorRegister(source), RVRegister.X0, !policy.Masked));
                NormalizeScalarRegister(counted, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, counted);
                return;
            }

            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            var written = VecScratch0;
            if (policy.KeepsDestination)
                MoveVectorRegister(written, undisturbed, VectorGroupOf(instruction.Result.Type));
            EmitVectorConfiguration(MachineRegister.X0, vl, shape, policy);
            var sourceRegister = source == MachineRegister.Invalid ? RVRegister.V0 : ToVectorRegister(source);
            Emit(second switch
            {
                VectorAliasOperand.Zero => RVInstruction.Vx(opcode, ToVectorRegister(written), sourceRegister, RVRegister.X0, !policy.Masked),
                VectorAliasOperand.AllOnes => RVInstruction.Vi(opcode, ToVectorRegister(written), sourceRegister, -1, !policy.Masked),
                VectorAliasOperand.SameSource => RVInstruction.Vv(opcode, ToVectorRegister(written), sourceRegister, sourceRegister, !policy.Masked),
                _ => RVInstruction.Vv(opcode, ToVectorRegister(written), sourceRegister, RVRegister.V0, !policy.Masked),
            });
            if (written != destination)
                MoveVectorRegister(destination, written, VectorGroupOf(instruction.Result.Type));
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>vcompress packs the elements its own mask operand names, which is never v0</summary>
        private void EmitVectorCompressIntrinsic(LirInstruction instruction, RVInstrKind opcode, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 4);
            if (instruction.Result is null)
                throw Unsupported(instruction, "vcompress intrinsic has no result.");

            var vs2 = LoadOperandOrVectorScratch(instruction.Operands[1], 1);
            var selector = LoadOperandOrVectorScratch(instruction.Operands[2], 2);
            var vl = LoadOperand(instruction.Operands[3], GpScratch1);
            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            Emit(RVInstruction.Vv(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToVectorRegister(selector)));
            if (VecScratch0 != destination)
                MoveVectorRegister(destination, VecScratch0, VectorGroupOf(instruction.Result.Type));
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>A slide up leaves the elements below the offset alone, so it carries its destination</summary>
        private void EmitVectorSlideUpIntrinsic(LirInstruction instruction, RVInstrKind opcode, string form, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, 5);
            if (instruction.Result is null)
                throw Unsupported(instruction, "vslideup intrinsic has no result.");

            var undisturbed = LoadOperand(instruction.Operands[1], VecScratch0);
            var vs2 = LoadOperandOrVectorScratch(instruction.Operands[2], 1);
            var offset = form == "vx"
                ? LoadVectorIntegerScalarOperand(instruction.Operands[3], GpScratch0, instruction)
                : MachineRegister.Invalid;
            var immediate = form == "vi" ? ImmediateToInt32(instruction.Operands[3]) : 0;
            var vl = LoadOperand(instruction.Operands[4], GpScratch1);

            var group = VectorGroupOf(instruction.Result.Type);
            MoveVectorRegister(VecScratch0, undisturbed, group);
            EmitVectorConfiguration(MachineRegister.X0, vl, shape);
            Emit(form == "vx"
                ? RVInstruction.Vx(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), ToRegister(offset))
                : RVInstruction.Vi(opcode, ToVectorRegister(VecScratch0), ToVectorRegister(vs2), immediate));

            var destination = GetWritableRegister(instruction.Result, VecScratch0);
            MoveVectorRegister(destination, VecScratch0, group);
            StoreWritableRegisterIfSpilled(instruction.Result, destination);
        }

        /// <summary>A strided access steps by a byte count the second register carries</summary>
        private void EmitVectorStridedIntrinsic(LirInstruction instruction, bool load, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, load ? 4 : 5);
            var address = LoadOperand(instruction.Operands[1], GpScratch0);
            var stride = LoadOperand(instruction.Operands[2], GpVectorConfigScratch);
            if (load)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Strided vector load intrinsic has no result.");
                var vl = LoadOperand(instruction.Operands[3], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vls(VectorStridedLoadOpcode(shape.ElementWidth), ToVectorRegister(destination), ToRegister(address), ToRegister(stride)));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return;
            }

            var source = LoadOperand(instruction.Operands[3], VecScratch0);
            var length = LoadOperand(instruction.Operands[4], GpScratch1);
            EmitVectorConfiguration(MachineRegister.X0, length, shape);
            Emit(RVInstruction.Vls(VectorStridedStoreOpcode(shape.ElementWidth), ToVectorRegister(source), ToRegister(address), ToRegister(stride)));
        }

        /// <summary>A mask access moves one bit per element and reads no mask of its own</summary>
        private void EmitVectorMaskMemoryIntrinsic(LirInstruction instruction, bool load, VectorShape shape)
        {
            RequireIntrinsicOperandCount(instruction, load ? 3 : 4);
            var address = LoadOperand(instruction.Operands[1], GpScratch0);
            if (load)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "vlm intrinsic has no result.");
                var vl = LoadOperand(instruction.Operands[2], GpScratch1);
                var destination = GetWritableRegister(instruction.Result, VecScratch0);
                EmitVectorConfiguration(MachineRegister.X0, vl, shape);
                Emit(RVInstruction.Vl(RVInstrKind.VlmV, ToVectorRegister(destination), ToRegister(address)));
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
                return;
            }

            var source = LoadOperand(instruction.Operands[2], VecScratch0);
            var length = LoadOperand(instruction.Operands[3], GpScratch1);
            EmitVectorConfiguration(MachineRegister.X0, length, shape);
            Emit(RVInstruction.Vs(RVInstrKind.VsmV, ToVectorRegister(source), ToRegister(address)));
        }

        /// <summary>An operation whose destination is not shaped like its sources may not overlap them</summary>
        private static bool RequiresIsolatedVectorDestination(string mnemonic)
        {
            if (mnemonic is "vnmsub" or "vnmsac")
                return false;
            return mnemonic.StartsWith("vms", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vmf", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vred", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vfred", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vn", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vw", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vfw", StringComparison.Ordinal) ||
                mnemonic.StartsWith("vrgather", StringComparison.Ordinal) ||
                mnemonic is "vmadc" or "vzext" or "vsext";
        }

        private static RVInstrKind VectorStridedLoadOpcode(int elementWidth)
            => elementWidth switch
            {
                8 => RVInstrKind.Vlse8V,
                16 => RVInstrKind.Vlse16V,
                32 => RVInstrKind.Vlse32V,
                64 => RVInstrKind.Vlse64V,
                _ => throw new ArgumentOutOfRangeException(nameof(elementWidth)),
            };

        private static RVInstrKind VectorStridedStoreOpcode(int elementWidth)
            => elementWidth switch
            {
                8 => RVInstrKind.Vsse8V,
                16 => RVInstrKind.Vsse16V,
                32 => RVInstrKind.Vsse32V,
                64 => RVInstrKind.Vsse64V,
                _ => throw new ArgumentOutOfRangeException(nameof(elementWidth)),
            };

        private MachineRegister LoadVectorIntegerScalarOperand(LirOperand operand, MachineRegister scratch, LirInstruction instruction)
        {
            if (IsRv32WideInteger(operand.Type))
            {
                LoadWideIntegerLowWord(operand, scratch, instruction);
                return scratch;
            }
            return LoadOperand(operand, scratch);
        }

        private void ValidateVectorFloatingPointSupport(LirInstruction instruction, VectorShape shape)
        {
            if (!shape.IsFloating)
                return;
            if (shape.ElementWidth == 32 && !_owner._machineTarget.HasF)
                throw Unsupported(instruction, "32-bit floating-point vector intrinsic requires the F extension.");
            if (shape.ElementWidth == 64 && !_owner._machineTarget.HasD)
                throw Unsupported(instruction, "64-bit floating-point vector intrinsic requires the D extension.");
        }

        /// <summary>Reads the element width and length multiplier out of an intrinsic name such as vadd_vv_i32m2</summary>
        private static bool TryGetRiscVVectorShape(string name, out VectorShape shape)
        {
            for (var i = 1; i < name.Length - 1; i++)
            {
                if (name[i] != 'm' || !char.IsDigit(name[i - 1]))
                    continue;

                var digits = i;
                while (digits > 0 && char.IsDigit(name[digits - 1]))
                    digits--;
                var elementWidth = int.Parse(name.AsSpan(digits, i - digits), provider: System.Globalization.CultureInfo.InvariantCulture);
                if (elementWidth is not (8 or 16 or 32 or 64))
                    continue;

                var scaleIndex = i + 1;
                var fractional = name[scaleIndex] == 'f';
                if (fractional)
                    scaleIndex++;
                if (scaleIndex >= name.Length || !char.IsDigit(name[scaleIndex]))
                    continue;
                var scale = name[scaleIndex] - '0';
                if (scale is not (1 or 2 or 4 or 8) || (fractional && scale == 1))
                    continue;

                var log2 = System.Numerics.BitOperations.Log2((uint)scale);
                shape = new VectorShape(elementWidth, fractional ? -log2 : log2, digits > 0 && name[digits - 1] == 'f');
                return true;
            }

            // A mask intrinsic names the ratio it works at rather than an element width
            var mask = name.LastIndexOf("_b", StringComparison.Ordinal);
            if (mask > 0 && int.TryParse(name.AsSpan(mask + 2), provider: System.Globalization.CultureInfo.InvariantCulture, out var ratio) &&
                ratio is 1 or 2 or 4 or 8 or 16 or 32 or 64)
            {
                shape = new VectorShape(8, 3 - System.Numerics.BitOperations.Log2((uint)ratio), false);
                return true;
            }

            shape = default;
            return false;
        }

        private void RequireIntrinsicOperandCount(LirInstruction instruction, int count)
        {
            if (instruction.Operands.Length != count)
                throw Unsupported(instruction, $"RISC-V vector intrinsic '{((FunctionSymbol)instruction.Operands[0].Symbol!).Name}' takes {instruction.Operands.Length - 1} arguments where {count - 1} were expected.");
        }

        private void EmitVectorConfiguration(MachineRegister destination, MachineRegister avl, VectorShape shape)
            => EmitVectorConfiguration(destination, avl, shape, VectorPolicy.Agnostic);

        /// <summary>A repeated configuration is dead: vl and vtype already hold what it would set</summary>
        private void EmitVectorConfiguration(MachineRegister destination, MachineRegister avl, VectorShape shape, VectorPolicy policy)
        {
            var vectorType = VectorTypeImmediate(shape, policy);
            var result = ToRegister(destination);
            var length = ToRegister(avl);
            if (vectorType == _vectorConfigType &&
                (result == RVRegister.X0
                    ? length == _vectorConfigLength || length == _vectorConfigResult
                    : result == _vectorConfigResult && length == _vectorConfigLength))
            {
                return;
            }

            Emit(RVInstruction.Vsetvli(result, length, vectorType));
            _vectorConfigType = vectorType;
            _vectorConfigLength = length;
            _vectorConfigResult = result;
        }

        private void EmitVectorMaxConfiguration(VectorShape shape)
            => EmitVectorConfiguration(GpVectorConfigScratch, MachineRegister.X0, shape);

        private static int VectorTypeImmediate(VectorShape shape, VectorPolicy policy)
        {
            var vsew = shape.ElementWidth switch
            {
                8 => 0,
                16 => 1,
                32 => 2,
                64 => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
            return (shape.LengthMultiplierLog2 & 7)
                | (vsew << 3)
                | (policy.TailAgnostic ? 1 << 6 : 0)
                | (policy.MaskAgnostic ? 1 << 7 : 0);
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

        /// <summary>An intrinsic names its instruction, with underscores where the mnemonic has dots</summary>
        private static RVInstrKind VectorIntrinsicOpcode(string key)
            => RVEncodedInstructions.TryGetOpcode(key.Replace('_', '.'), out var opcode)
                ? opcode
                : throw new NotSupportedException($"Unsupported vector intrinsic opcode '{key}'.");

        private void MarshalCallArguments(LirInstruction instruction, int startOperand)
        {
            var cursor = new AbiCursor();
            CollectCallOperandRegisters(instruction);
            MarshalHiddenReturnBufferArgument(instruction, ref cursor);
            for (var i = startOperand; i < instruction.Operands.Length; i++)
                MarshalCallArgument(instruction, instruction.Operands[i], ref cursor, i - startOperand);
            _callOperandRegisters.Clear();
        }

        /// <summary>
        /// Records the physical registers the call reads, so a materialized argument can be built
        /// straight in its ABI register without clobbering a value another argument still needs.
        /// </summary>
        private void CollectCallOperandRegisters(LirInstruction instruction)
        {
            _callOperandRegisters.Clear();
            foreach (var operand in instruction.Operands)
            {
                if (TryGetPhysicalRegister(operand, out var physicalRegister))
                    _callOperandRegisters.Add(physicalRegister);
            }
        }

        /// <summary>Picks the ABI register itself as the materialization target when that is safe</summary>
        private MachineRegister PreferredArgumentScratch(LirOperand operand, AbiLocation location, MachineRegister fallback)
        {
            if (operand.Kind == LirOperandKind.Register ||
                location.Kind != AbiLocationKind.Register ||
                _callOperandRegisters.Contains(location.Register) ||
                IsFloatRegister(location.Register) != IsFloatRegister(fallback) ||
                IsVectorRegister(location.Register) != IsVectorRegister(fallback))
            {
                return fallback;
            }

            return location.Register;
        }

        private void MarshalHiddenReturnBufferArgument(LirInstruction instruction, ref AbiCursor cursor)
        {
            if (instruction.Result is null || !CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                return;

            var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _allocationOptions.StackArgumentSlotSize);
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
                var loc = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
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
                    var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _allocationOptions.StackArgumentSlotSize);
                    LoadRawBitsFromMemory(GpScratch1, storageBase, segment.Offset, segment.Size, BlockAlignment(operand.Type));
                    StoreArgumentValue(GpScratch1, loc, segment.Size);
                }
                return;
            }

            if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(operand.Type))
            {
                var loc = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
                MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                AddImmediate(GpScratch1, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + loc.StackByteOffset(_allocationOptions.StackArgumentSlotSize));
                CopyMemory(GpScratch1, GpScratch0, value.Size, BlockAlignment(operand.Type));
                return;
            }

            var scalarLoc = CAbi.AssignArgumentLocation(value, ref cursor, _allocationOptions.StackArgumentSlotSize);
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
                return LoadOperand(operand, PreferredArgumentScratch(operand, location, GpScratch0));

            if (registerClass == AbiRegisterClass.Floating || location.Kind == AbiLocationKind.Stack)
            {
                var scratch = UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0;
                return LoadOperand(operand, PreferredArgumentScratch(operand, location, scratch));
            }

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
                StoreToMemory(source, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + location.StackByteOffset(_allocationOptions.StackArgumentSlotSize), size);
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
                    StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size, BlockAlignment(instruction.Result.Type));
                }
                return;
            }

            if (value.PassingKind == AbiPassingKind.MultiRegister)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                for (var i = 0; i < value.Segments.Length; i++)
                {
                    var segment = value.Segments[i];
                    StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size, BlockAlignment(instruction.Result.Type));
                }
                return;
            }

            var returnsFloating = value.Segments.Length != 0 && value.Segments[0].RegisterClass == AbiRegisterClass.Floating;
            var dst = GetWritableRegister(instruction.Result, returnsFloating ? FpScratch0 : GpScratch0);
            if (!returnsFloating)
                SetIntegerRepresentation(MachineRegister.X10, AbiScalarRepresentation(instruction.Result.Type));
            MoveRegister(dst, returnsFloating ? MachineRegister.F10 : MachineRegister.X10);
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
            if (TryEmitSelect(instruction))
                return;

            if (instruction.Operands.Length == 2 && IsComparisonOperator(instruction.Operator))
            {
                EmitComparisonBranch(instruction);
                return;
            }

            if (instruction.Operands.Length != 1)
                throw Unsupported(instruction, "Branch expects one condition operand or two comparison operands.");
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
                cond = LoadIntegerBranchOperand(instruction.Operands[0], GpScratch0);
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

        private void EmitComparisonBranch(LirInstruction instruction)
        {
            var left = instruction.Operands[0];
            var right = instruction.Operands[1];

            if (IsFloatType(left.Type) || IsFloatType(right.Type))
            {
                if (IsLongDouble(left.Type) || IsLongDouble(right.Type))
                    throw HelperRequired(instruction, SelectFloatingHelper(instruction.Operator, IsLongDouble(left.Type) ? left.Type : right.Type), "long double comparison branch requires a runtime helper.");
                var floatType = SelectFloatingOperationType(left.Type, right.Type, IsFloatType(left.Type) ? left.Type : right.Type);
                RequireFloatingHardware(floatType, instruction);
                var leftRegister = LoadOperandAsFloating(left, floatType, FpScratch1, instruction);
                var rightRegister = LoadOperandAsFloating(right, floatType, FpScratch2, instruction);
                EmitFloatingRelation(instruction.Operator, floatType, GpScratch0, leftRegister, rightRegister);
                if (PrefersInvertedBranch(instruction))
                {
                    EmitBranch(RVInstrKind.Beq, GpScratch0, MachineRegister.X0, LabelOf(instruction.FalseTarget));
                    return;
                }

                EmitBranch(RVInstrKind.Bne, GpScratch0, MachineRegister.X0, LabelOf(instruction.TrueTarget));
                if (!IsFallthroughTarget(instruction.FalseTarget))
                    EmitJump(LabelOf(instruction.FalseTarget));
                return;
            }

            if (IsRv32WideInteger(left.Type) || IsRv32WideInteger(right.Type))
            {
                EmitWideComparisonBranch(instruction);
                return;
            }

            if (RequiresStackBackedScalar(left.Type) || RequiresStackBackedScalar(right.Type))
                throw HelperRequired(instruction, SelectScalarMoveHelper(left.Type), "Comparison branch on a scalar wider than one machine register is not implemented yet.");

            var leftRegisterInteger = LoadIntegerBranchOperand(left, GpScratch1);
            var rightRegisterInteger = LoadIntegerBranchOperand(right, GpScratch2);
            var signed = IsSignedIntegerType(left.Type) || IsSignedIntegerType(right.Type);
            var opcode = SelectIntegerBranchOpcode(instruction.Operator, signed, out var swapOperands);
            var first = swapOperands ? rightRegisterInteger : leftRegisterInteger;
            var second = swapOperands ? leftRegisterInteger : rightRegisterInteger;

            if (PrefersInvertedBranch(instruction))
            {
                EmitBranch(InvertBranchOpcode(opcode), first, second, LabelOf(instruction.FalseTarget));
                return;
            }

            EmitBranch(opcode, first, second, LabelOf(instruction.TrueTarget));
            if (!IsFallthroughTarget(instruction.FalseTarget))
                EmitJump(LabelOf(instruction.FalseTarget));
        }

        /// <summary>Reports whether the true target falls through, so branching on the negated condition removes the jump</summary>
        private bool PrefersInvertedBranch(LirInstruction instruction)
            => IsFallthroughTarget(instruction.TrueTarget) && !IsFallthroughTarget(instruction.FalseTarget);

        private static RVInstrKind InvertBranchOpcode(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.Beq => RVInstrKind.Bne,
                RVInstrKind.Bne => RVInstrKind.Beq,
                RVInstrKind.Blt => RVInstrKind.Bge,
                RVInstrKind.Bge => RVInstrKind.Blt,
                RVInstrKind.Bltu => RVInstrKind.Bgeu,
                RVInstrKind.Bgeu => RVInstrKind.Bltu,
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };

        private void EmitWideComparisonBranch(LirInstruction instruction)
        {
            var left = instruction.Operands[0];
            var right = instruction.Operands[1];
            LoadWideIntegerBranchOperand(left, GpScratch0, GpScratch1, instruction, out var leftLow, out var leftHigh);
            LoadWideIntegerBranchOperand(right, GpScratch2, GpScratch3, instruction, out var rightLow, out var rightHigh);
            var trueLabel = LabelOf(instruction.TrueTarget);
            var falseLabel = LabelOf(instruction.FalseTarget);

            if (instruction.Operator == "==")
            {
                EmitBranch(RVInstrKind.Bne, leftHigh, rightHigh, falseLabel);
                EmitBranch(RVInstrKind.Beq, leftLow, rightLow, trueLabel);
            }
            else if (instruction.Operator == "!=")
            {
                EmitBranch(RVInstrKind.Bne, leftHigh, rightHigh, trueLabel);
                EmitBranch(RVInstrKind.Bne, leftLow, rightLow, trueLabel);
            }
            else
            {
                var signed = IsSignedIntegerType(left.Type) || IsSignedIntegerType(right.Type);
                var highLess = signed ? RVInstrKind.Blt : RVInstrKind.Bltu;
                switch (instruction.Operator)
                {
                    case "<":
                        EmitBranch(highLess, leftHigh, rightHigh, trueLabel);
                        EmitBranch(highLess, rightHigh, leftHigh, falseLabel);
                        EmitBranch(RVInstrKind.Bltu, leftLow, rightLow, trueLabel);
                        break;
                    case "<=":
                        EmitBranch(highLess, leftHigh, rightHigh, trueLabel);
                        EmitBranch(highLess, rightHigh, leftHigh, falseLabel);
                        EmitBranch(RVInstrKind.Bgeu, rightLow, leftLow, trueLabel);
                        break;
                    case ">":
                        EmitBranch(highLess, rightHigh, leftHigh, trueLabel);
                        EmitBranch(highLess, leftHigh, rightHigh, falseLabel);
                        EmitBranch(RVInstrKind.Bltu, rightLow, leftLow, trueLabel);
                        break;
                    case ">=":
                        EmitBranch(highLess, rightHigh, leftHigh, trueLabel);
                        EmitBranch(highLess, leftHigh, rightHigh, falseLabel);
                        EmitBranch(RVInstrKind.Bgeu, leftLow, rightLow, trueLabel);
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported comparison branch operator '{instruction.Operator}'.");
                }
            }

            if (!IsFallthroughTarget(instruction.FalseTarget))
                EmitJump(falseLabel);
        }

        private MachineRegister LoadIntegerBranchOperand(LirOperand operand, MachineRegister preferred)
        {
            if (IsZeroIntegerImmediate(operand))
                return MachineRegister.X0;
            return LoadOperand(operand, preferred);
        }

        private void LoadWideIntegerBranchOperand(
            LirOperand operand,
            MachineRegister lowScratch,
            MachineRegister highScratch,
            LirInstruction instruction,
            out MachineRegister low,
            out MachineRegister high)
        {
            if (IsZeroIntegerImmediate(operand))
            {
                low = MachineRegister.X0;
                high = MachineRegister.X0;
                return;
            }

            LoadWideIntegerOperand(operand, lowScratch, highScratch, instruction);
            low = lowScratch;
            high = highScratch;
        }

        private static RVInstrKind SelectIntegerBranchOpcode(string op, bool signed, out bool swapOperands)
        {
            swapOperands = false;
            switch (op)
            {
                case "==": return RVInstrKind.Beq;
                case "!=": return RVInstrKind.Bne;
                case "<": return signed ? RVInstrKind.Blt : RVInstrKind.Bltu;
                case ">=": return signed ? RVInstrKind.Bge : RVInstrKind.Bgeu;
                case ">":
                    swapOperands = true;
                    return signed ? RVInstrKind.Blt : RVInstrKind.Bltu;
                case "<=":
                    swapOperands = true;
                    return signed ? RVInstrKind.Bge : RVInstrKind.Bgeu;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        private static bool IsComparisonOperator(string op)
            => op is "==" or "!=" or "<" or "<=" or ">" or ">=";

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
                LoadRawBitsFromMemory(GpScratch2, sourceAddress, offset, segmentSize, _owner._target.RegisterSize);
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
                LoadWideIntegerBranchOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction, out var keyLow, out var keyHigh);
                foreach (var @case in instruction.SwitchCases)
                {
                    var next = _owner.CreateLocalLabel(_functionLabel + "_i64_switch_next");
                    var value = unchecked((ulong)ImmediateToInt64(@case.Value));
                    var lowValue = unchecked((int)value);
                    var highValue = unchecked((int)(value >> 32));
                    var lowRegister = lowValue == 0 ? MachineRegister.X0 : GpScratch2;
                    var highRegister = highValue == 0 ? MachineRegister.X0 : GpScratch3;
                    if (lowRegister != MachineRegister.X0)
                        LoadImmediate(lowRegister, lowValue);
                    if (highRegister != MachineRegister.X0)
                        LoadImmediate(highRegister, highValue);
                    EmitBranch(RVInstrKind.Bne, keyHigh, highRegister, next);
                    EmitBranch(RVInstrKind.Beq, keyLow, lowRegister, LabelOf(@case.Target));
                    _owner._text.DefineLabel(next);
                }

                if (!IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
                return;
            }
            if (RequiresStackBackedScalar(instruction.Operands[0].Type))
                throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Operands[0].Type), "Switch on scalar wider than one machine register is not implemented yet.");

            if (TryEmitSwitchTable(instruction, SizeOf(instruction.Operands[0].Type)))
                return;

            var key = LoadIntegerBranchOperand(instruction.Operands[0], GpScratch0);
            foreach (var @case in instruction.SwitchCases)
            {
                var caseValue = ImmediateToInt64(@case.Value);
                var caseRegister = caseValue == 0 ? MachineRegister.X0 : GpScratch1;
                if (caseRegister != MachineRegister.X0)
                    LoadImmediate(caseRegister, caseValue);
                EmitBranch(RVInstrKind.Beq, key, caseRegister, LabelOf(@case.Target));
            }

            if (!IsFallthroughTarget(instruction.Target))
                EmitJump(LabelOf(instruction.Target));
        }

        private bool TryEmitSwitchTable(LirInstruction instruction, int size)
        {
            // A narrower selector would leave the bits above it undefined in the index register
            if (size < 4)
                return false;

            var values = new long[instruction.SwitchCases.Length];
            for (var i = 0; i < values.Length; i++)
                values[i] = ImmediateToInt64(instruction.SwitchCases[i].Value);
            if (!LirJumpTable.TryPlan(instruction.SwitchCases, values, instruction.Target,
                    size, IsSignedIntegerType(instruction.Operands[0].Type), out var plan))
                return false;

            var targetLabels = new string[plan.Targets.Length];
            for (var i = 0; i < targetLabels.Length; i++)
            {
                if (!_labels.TryGetValue(plan.Targets[i], out var targetLabel))
                    return false;
                targetLabels[i] = targetLabel;
            }

            var index = LoadIntegerBranchOperand(instruction.Operands[0], GpScratch0);
            if (plan.Minimum != 0)
            {
                var bias = unchecked(-plan.Minimum);
                if (FitsSignedImmediate(bias, 12))
                {
                    EmitImm(RVInstrKind.Addi, GpScratch0, index, (int)bias);
                }
                else
                {
                    LoadImmediate(GpScratch1, plan.Minimum);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), ToRegister(index), ToRegister(GpScratch1)));
                }
                index = GpScratch0;
                SetIntegerRepresentation(GpScratch0, IntegerRepresentationFact.Unknown);
            }

            LoadImmediate(GpScratch1, plan.Targets.Length);
            EmitBranch(RVInstrKind.Bgeu, index, GpScratch1, LabelOf(instruction.Target));

            var entryShift = _owner._target.PointerSize == 8 ? 3 : 2;
            MaterializeSymbolAddress(_owner.CreateJumpTable(targetLabels), GpScratch1);
            Emit(RVInstruction.I(RVInstrKind.Slli, ToRegister(GpScratch2), ToRegister(index), entryShift));
            Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
            Emit(RVInstruction.I(
                _owner._target.PointerSize == 8 ? RVInstrKind.Ld : RVInstrKind.Lw,
                ToRegister(GpScratch2),
                ToRegister(GpScratch1),
                0));
            Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, ToRegister(GpScratch2), 0));
            SetIntegerRepresentation(GpScratch2, IntegerRepresentationFact.Unknown);
            return true;
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
                    CopyMemory(GpScratch1, GpScratch0, value.Size, BlockAlignment(operand.Type));
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                for (var i = 0; i < value.Segments.Length; i++)
                {
                    var segment = value.Segments[i];
                    LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), GpScratch0, segment.Offset, segment.Size, BlockAlignment(operand.Type));
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
                CopyMemory(GpScratch1, sourceAddress, returnAbi.Size, BlockAlignment(returnType));
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
                    LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), sourceAddress, segment.Offset, segment.Size, BlockAlignment(returnType));
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

            // Copies that read what another one writes only need an order, not a detour through
            // the frame, and one exists unless the reads and writes form a cycle
            if (!HasBlockCopyParallelCopy(copies) &&
                _allocation.TryOrderParallelCopies(copies, out var ordered))
            {
                foreach (var copy in ordered)
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
                    CopyMemory(GpScratch1, sourceAddress, SizeOf(copy.Destination.Type), BlockAlignment(copy.Destination.Type));
                    tempCursor += AlignUp(SizeOf(copy.Destination.Type), _allocationOptions.SpillSlotAlignment);
                }
                else
                {
                    var source = LoadOperand(copy.Source, PreferredScratch(copy.Source.Type, GpScratch0, FpScratch0, VecScratch0));
                    StoreToMemory(source, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type));
                    tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _allocationOptions.SpillSlotAlignment);
                }
            }

            tempCursor = 0;
            foreach (var copy in copies)
            {
                if (RequiresBlockCopyStorage(copy.Destination.Type))
                {
                    var dest = MaterializeVirtualRegisterStorageAddress(copy.Destination, GpScratch0);
                    AddImmediate(GpScratch1, Sp, tempOffset + tempCursor);
                    CopyMemory(dest, GpScratch1, SizeOf(copy.Destination.Type), BlockAlignment(copy.Destination.Type));
                    tempCursor += AlignUp(SizeOf(copy.Destination.Type), _allocationOptions.SpillSlotAlignment);
                }
                else
                {
                    var destination = GetWritableRegister(copy.Destination, PreferredScratch(copy.Destination.Type, GpScratch0, FpScratch0, VecScratch0));
                    LoadFromMemory(destination, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type), IsSignedIntegerType(copy.Destination.Type));
                    NormalizeScalarRegister(destination, copy.Destination.Type);
                    StoreWritableRegisterIfSpilled(copy.Destination, destination);
                    tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _allocationOptions.SpillSlotAlignment);
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
            => !HasBlockCopyParallelCopy(copies) && !HasPhysicalStorageClobber(copies);

        private bool HasBlockCopyParallelCopy(IReadOnlyList<LirParallelCopy> copies)
        {
            foreach (var copy in copies)
                if (RequiresBlockCopyStorage(copy.Destination.Type))
                    return true;
            return false;
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
            if (!TypesNeedIntegerConversion(operand.Type, targetType) || IntegerRepresentationSatisfies(GetIntegerRepresentation(source), targetType))
                return source;
            if (source != scratch)
                MoveRegister(scratch, source);
            NormalizeIntegerRegister(scratch, targetType);
            return scratch;
        }

        private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
        {
            var source = LoadOperandAs(operand, targetType, destination, instruction);
            MoveRegister(destination, source, VectorGroupOf(targetType));
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
                        ZeroVectorRegister(preferred, VectorGroupOf(operand.Type));
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
            {
                if (_useCallPreservationSources &&
                    _allocation.TryGetCallPreservation(_currentInstructionPosition, register, out var preservation))
                {
                    if (preservation.UsesRegister)
                        return preservation.PreservationRegister;

                    if (IsRiscVVectorType(register.Type) && !IsVectorRegister(preferred))
                        preferred = VecScratch0;
                    else if (UsesHardwareFloating(register.Type) && !IsFloatRegister(preferred))
                        preferred = FpScratch0;
                    var size = Math.Min(SizeOfRegisterType(register.Type), SizeOf(register.Type));
                    LoadFromMemory(preferred, Sp, preservation.StackOffset, size, IsSignedIntegerType(register.Type));
                    NormalizeScalarRegister(preferred, register.Type);
                    return preferred;
                }

                return alloc.PhysicalRegister;
            }
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
            if (register.HomeSlot is { } home)
            {
                AddImmediate(destination, Sp, _allocation.Frame.StackSlotOffsets[home]);
                return destination;
            }

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
                    ZeroMemory(destination, size, BlockAlignment(operand.Type));
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
                ZeroMemory(destinationAddress, size, BlockAlignment(destination.Type));
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
            CopyMemory(destinationAddress, sourceAddress, size, BlockAlignment(destination.Type));
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
                ZeroMemory(destinationAddress, size, BlockAlignment(destination.Type));
                return;
            }
            MaterializeOperandStorageAddress(source, GpScratch1, instruction);
            CopyMemory(destinationAddress, GpScratch1, size, BlockAlignment(destination.Type));
        }

        private void MaterializeIncomingHiddenReturnBufferAddress(MachineRegister destination)
        {
            if (_allocation.Frame.HasHiddenReturnBuffer)
            {
                LoadFromMemory(destination, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize, signed: false);
                return;
            }

            var cursor = new AbiCursor();
            var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _allocationOptions.StackArgumentSlotSize);
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
                    IncomingStackOffset(location.StackByteOffset(_allocationOptions.StackArgumentSlotSize)),
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
                    var elementScale = Math.Max(1, address.Scale);
                    if (address.Index is null)
                        return new AddressParts(baseAddress.BaseRegister, checked(baseAddress.Offset + address.Displacement));
                    if (IsIntegerImmediate(address.Index) &&
                        TryGetConstantElementOffset(
                            address.Index,
                            elementScale,
                            checked(baseAddress.Offset + address.Displacement),
                            out var constantOffset))
                    {
                        return new AddressParts(baseAddress.BaseRegister, constantOffset);
                    }

                    var elementBase = baseAddress.BaseRegister;
                    if (baseAddress.Offset != 0)
                    {
                        AddImmediate(scratchBase, elementBase, baseAddress.Offset);
                        elementBase = scratchBase;
                    }

                    // The add reads both sources, so neither the base nor the index needs a copy
                    var index = LoadOperand(address.Index, scratchIndex);
                    var scaled = ScaleIndex(scratchIndex, index, address.Scale, scratchIndex == GpScratch3 ? GpScratch2 : GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(scratchBase), ToRegister(elementBase), ToRegister(scaled)));
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

        // A constant index is displacement, not arithmetic; an offset past the load field is widened at emission
        private static bool TryGetConstantElementOffset(LirOperand index, int scale, int baseOffset, out int offset)
        {
            offset = 0;
            var scaled = ImmediateToInt64(index) * scale + baseOffset;
            if (scaled < int.MinValue || scaled > int.MaxValue)
                return false;
            offset = (int)scaled;
            return true;
        }

        // Reports where the scaled index landed, which is the index itself when nothing had to scale
        private MachineRegister ScaleIndex(MachineRegister destination, MachineRegister index, int scale, MachineRegister scratch)
        {
            if (scale <= 1)
                return index;
            if (IsPowerOfTwo(scale))
            {
                EmitShiftImmediate(RVInstrKind.Slli, destination, index, Log2(scale));
                return destination;
            }
            if (!_owner._machineTarget.HasM)
                throw new NotSupportedException("Non power-of-two pointer scale requires M extension.");
            LoadImmediate(scratch, scale);
            Emit(RVInstruction.R(RVInstrKind.Mul, ToRegister(destination), ToRegister(index), ToRegister(scratch)));
            return destination;
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

        private void CopyMemory(MachineRegister destination, MachineRegister source, int size, int alignment)
        {
            if (size <= 0 || destination == source)
                return;

            const int inlineThreshold = 16;
            var accessSize = BlockAccessSize(alignment);
            if (size <= inlineThreshold && (accessSize > 1 || size <= 4))
            {
                EmitInlineMemoryCopy(destination, source, size, accessSize);
                return;
            }

            if (accessSize > 1)
            {
                PrepareBlockCopyPointers(destination, source);
                var bulkSize = size - size % accessSize;
                if (bulkSize != 0)
                {
                    LoadImmediate(GpScratch6, bulkSize);
                    var loop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_known_loop");
                    _owner._text.DefineLabel(loop);
                    LoadFromMemory(GpScratch2, GpScratch5, 0, accessSize, signed: false);
                    StoreToMemory(GpScratch2, GpScratch4, 0, accessSize);
                    AddImmediate(GpScratch5, GpScratch5, accessSize);
                    AddImmediate(GpScratch4, GpScratch4, accessSize);
                    AddImmediate(GpScratch6, GpScratch6, -accessSize);
                    EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, loop);
                }

                var tailSize = size - bulkSize;
                if (tailSize != 0)
                    EmitInlineMemoryCopy(GpScratch4, GpScratch5, tailSize, accessSize);
                return;
            }

            PrepareBlockCopyPointers(destination, source);
            LoadImmediate(GpScratch6, size);
            LoadImmediate(GpScratch0, _owner._target.RegisterSize);

            var byteLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_byte_loop");
            var alignLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_align_loop");
            var wordLoop = _owner.CreateLocalLabel(_functionLabel + "_memcpy_word_loop");
            var done = _owner.CreateLocalLabel(_functionLabel + "_memcpy_done");
            var wordSize = _owner._target.RegisterSize;
            var alignmentMask = wordSize - 1;

            Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch3), ToRegister(GpScratch4), ToRegister(GpScratch5)));
            EmitImm(RVInstrKind.Andi, GpScratch3, GpScratch3, alignmentMask);
            EmitBranch(RVInstrKind.Bne, GpScratch3, MachineRegister.X0, byteLoop);

            _owner._text.DefineLabel(alignLoop);
            EmitImm(RVInstrKind.Andi, GpScratch3, GpScratch4, alignmentMask);
            EmitBranch(RVInstrKind.Beq, GpScratch3, MachineRegister.X0, wordLoop);
            LoadFromMemory(GpScratch2, GpScratch5, 0, 1, signed: false);
            StoreToMemory(GpScratch2, GpScratch4, 0, 1);
            AddImmediate(GpScratch5, GpScratch5, 1);
            AddImmediate(GpScratch4, GpScratch4, 1);
            AddImmediate(GpScratch6, GpScratch6, -1);
            EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, alignLoop);
            EmitJump(done);

            _owner._text.DefineLabel(wordLoop);
            EmitBranch(RVInstrKind.Bltu, GpScratch6, GpScratch0, byteLoop);
            LoadFromMemory(GpScratch2, GpScratch5, 0, wordSize, signed: false);
            StoreToMemory(GpScratch2, GpScratch4, 0, wordSize);
            AddImmediate(GpScratch5, GpScratch5, wordSize);
            AddImmediate(GpScratch4, GpScratch4, wordSize);
            AddImmediate(GpScratch6, GpScratch6, -wordSize);
            EmitJump(wordLoop);

            _owner._text.DefineLabel(byteLoop);
            EmitBranch(RVInstrKind.Beq, GpScratch6, MachineRegister.X0, done);
            LoadFromMemory(GpScratch2, GpScratch5, 0, 1, signed: false);
            StoreToMemory(GpScratch2, GpScratch4, 0, 1);
            AddImmediate(GpScratch5, GpScratch5, 1);
            AddImmediate(GpScratch4, GpScratch4, 1);
            AddImmediate(GpScratch6, GpScratch6, -1);
            EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, byteLoop);

            _owner._text.DefineLabel(done);
        }

        private void PrepareBlockCopyPointers(MachineRegister destination, MachineRegister source)
        {
            if (destination == GpScratch5 && source == GpScratch4)
            {
                MoveRegister(GpScratch6, GpScratch4);
                MoveRegister(GpScratch4, GpScratch5);
                MoveRegister(GpScratch5, GpScratch6);
                return;
            }

            if (source == GpScratch4)
            {
                MoveRegister(GpScratch5, source);
                MoveRegister(GpScratch4, destination);
                return;
            }

            MoveRegister(GpScratch4, destination);
            MoveRegister(GpScratch5, source);
        }

        private void EmitInlineMemoryCopy(MachineRegister destination, MachineRegister source, int size, int maxAccessSize)
        {
            var offset = 0;
            for (var accessSize = maxAccessSize; accessSize >= 1; accessSize >>= 1)
            {
                while (size - offset >= accessSize)
                {
                    LoadFromMemory(GpScratch2, source, offset, accessSize, signed: false);
                    StoreToMemory(GpScratch2, destination, offset, accessSize);
                    offset += accessSize;
                }
            }
        }

        private void ZeroMemory(MachineRegister destination, int size, int alignment)
        {
            if (size <= 0)
                return;

            const int inlineThreshold = 16;
            var accessSize = BlockAccessSize(alignment);
            if (size <= inlineThreshold && (accessSize > 1 || size <= 4))
            {
                EmitInlineZeroMemory(destination, size, accessSize);
                return;
            }

            if (accessSize > 1)
            {
                var pointer = destination == GpScratch4 ? GpScratch5 : GpScratch4;
                MoveRegister(pointer, destination);
                var bulkSize = size - size % accessSize;
                if (bulkSize != 0)
                {
                    LoadImmediate(GpScratch6, bulkSize);
                    var loop = _owner.CreateLocalLabel(_functionLabel + "_memzero_known_loop");
                    _owner._text.DefineLabel(loop);
                    StoreToMemory(MachineRegister.X0, pointer, 0, accessSize);
                    AddImmediate(pointer, pointer, accessSize);
                    AddImmediate(GpScratch6, GpScratch6, -accessSize);
                    EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, loop);
                }

                var tailSize = size - bulkSize;
                if (tailSize != 0)
                    EmitInlineZeroMemory(pointer, tailSize, accessSize);
                return;
            }

            var dynamicPointer = destination == GpScratch4 ? GpScratch5 : GpScratch4;
            MoveRegister(dynamicPointer, destination);
            LoadImmediate(GpScratch6, size);
            LoadImmediate(GpScratch0, _owner._target.RegisterSize);

            var alignLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_align_loop");
            var wordLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_word_loop");
            var byteLoop = _owner.CreateLocalLabel(_functionLabel + "_memzero_byte_loop");
            var done = _owner.CreateLocalLabel(_functionLabel + "_memzero_done");
            var wordSize = _owner._target.RegisterSize;
            var alignmentMask = wordSize - 1;

            _owner._text.DefineLabel(alignLoop);
            EmitImm(RVInstrKind.Andi, GpScratch3, dynamicPointer, alignmentMask);
            EmitBranch(RVInstrKind.Beq, GpScratch3, MachineRegister.X0, wordLoop);
            StoreToMemory(MachineRegister.X0, dynamicPointer, 0, 1);
            AddImmediate(dynamicPointer, dynamicPointer, 1);
            AddImmediate(GpScratch6, GpScratch6, -1);
            EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, alignLoop);
            EmitJump(done);

            _owner._text.DefineLabel(wordLoop);
            EmitBranch(RVInstrKind.Bltu, GpScratch6, GpScratch0, byteLoop);
            StoreToMemory(MachineRegister.X0, dynamicPointer, 0, wordSize);
            AddImmediate(dynamicPointer, dynamicPointer, wordSize);
            AddImmediate(GpScratch6, GpScratch6, -wordSize);
            EmitJump(wordLoop);

            _owner._text.DefineLabel(byteLoop);
            EmitBranch(RVInstrKind.Beq, GpScratch6, MachineRegister.X0, done);
            StoreToMemory(MachineRegister.X0, dynamicPointer, 0, 1);
            AddImmediate(dynamicPointer, dynamicPointer, 1);
            AddImmediate(GpScratch6, GpScratch6, -1);
            EmitBranch(RVInstrKind.Bne, GpScratch6, MachineRegister.X0, byteLoop);

            _owner._text.DefineLabel(done);
        }

        private void EmitInlineZeroMemory(MachineRegister destination, int size, int maxAccessSize)
        {
            var offset = 0;
            for (var accessSize = maxAccessSize; accessSize >= 1; accessSize >>= 1)
            {
                while (size - offset >= accessSize)
                {
                    StoreToMemory(MachineRegister.X0, destination, offset, accessSize);
                    offset += accessSize;
                }
            }
        }

        private int BlockAccessSize(int alignment)
        {
            var accessSize = _owner._target.RegisterSize;
            alignment = Math.Max(1, alignment);
            while (accessSize > 1 && alignment % accessSize != 0)
                accessSize >>= 1;
            return accessSize;
        }

        /// <summary>Moves one ABI segment in pieces no wider than the aggregate's own alignment allows</summary>
        private void LoadRawBitsFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size, int alignment)
        {
            var width = RawStorageSize(size);
            var step = Math.Min(width, BlockAccessSize(alignment));
            if (step >= width)
            {
                LoadFromMemory(destination, baseRegister, offset, width, signed: false);
                return;
            }

            var piece = destination == GpScratch2 ? GpScratch3 : GpScratch2;
            LoadFromMemory(destination, baseRegister, offset, step, signed: false);
            for (var done = step; done < width; done += step)
            {
                LoadFromMemory(piece, baseRegister, offset + done, step, signed: false);
                EmitShiftImmediate(RVInstrKind.Slli, piece, piece, done * 8);
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(destination), ToRegister(destination), ToRegister(piece)));
            }
        }

        private void StoreRawBitsToAddress(MachineRegister source, MachineRegister baseRegister, int offset, int size, int alignment)
        {
            var width = RawStorageSize(size);
            var step = Math.Min(width, BlockAccessSize(alignment));
            if (step >= width)
            {
                StoreToMemory(source, baseRegister, offset, width);
                return;
            }

            var piece = source == GpScratch2 ? GpScratch3 : GpScratch2;
            StoreToMemory(source, baseRegister, offset, step);
            for (var done = step; done < width; done += step)
            {
                EmitShiftImmediate(RVInstrKind.Srli, piece, source, done * 8);
                StoreToMemory(piece, baseRegister, offset + done, step);
            }
        }

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
            var shape = ByteVectorShape(VectorSpillGroup(size), destination, destination);
            var address = VectorMemoryAddress(baseRegister, offset);
            EmitVectorMaxConfiguration(shape);
            Emit(RVInstruction.Vl(RVInstrKind.Vle8V, ToVectorRegister(destination), ToRegister(address)));
        }

        private void StoreVectorToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
        {
            var shape = ByteVectorShape(VectorSpillGroup(size), source, source);
            var address = VectorMemoryAddress(baseRegister, offset);
            EmitVectorMaxConfiguration(shape);
            Emit(RVInstruction.Vs(RVInstrKind.Vse8V, ToVectorRegister(source), ToRegister(address)));
        }

        private int VectorSpillGroup(int size)
        {
            var unit = TargetRegisterInfo.VectorRegisterSize(_owner._target);
            if (unit <= 0 || size <= 0 || size % unit != 0)
                throw new NotSupportedException("Vector spills require whole vector registers.");
            return size / unit;
        }

        private MachineRegister VectorMemoryAddress(MachineRegister baseRegister, int offset)
        {
            if (offset == 0)
                return baseRegister;
            AddImmediate(GpScratch3, baseRegister, offset);
            return GpScratch3;
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

        // C truncates toward zero, so the conversion has to name that rounding mode rather than inherit one
        private void EmitFloatingConvertToInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
            => Emit(new RVInstruction(opcode, ToRegister(destination), ToFloatRegister(source), RVRegister.X0, RoundTowardZero));

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
            var representation = RepresentationForConstant(value);
            if (value == 0)
            {
                MoveRegister(destination, MachineRegister.X0);
                SetIntegerRepresentation(destination, representation);
                return;
            }

            if (FitsSignedImmediate(value, 12))
            {
                EmitImm(RVInstrKind.Addi, destination, MachineRegister.X0, (int)value);
                SetIntegerRepresentation(destination, representation);
                return;
            }

            if (value >= int.MinValue && value <= int.MaxValue)
            {
                var hi = (int)((value + 0x800L) >> 12);
                var lo = (int)(value - ((long)hi << 12));
                Emit(RVInstruction.U(RVInstrKind.Lui, ToRegister(destination), hi));
                // lui sign extends what it loads, so the addition has to close over 32 bits and extend again
                if (lo != 0)
                    EmitImm(_owner._target.Is64Bit ? RVInstrKind.Addiw : RVInstrKind.Addi, destination, destination, lo);
                SetIntegerRepresentation(destination, representation);
                return;
            }

            if (!_owner._target.Is64Bit)
                throw new OverflowException("Immediate does not fit RV32 register.");

            // A short shift-and-add chain beats the pool, which costs the same instructions plus a load
            var plan = new List<WideImmediateStep>();
            if (TryPlanWideImmediate(value, plan, WideImmediateInstructionBudget))
            {
                foreach (var step in plan)
                {
                    switch (step.Kind)
                    {
                        case RVInstrKind.Lui:
                            Emit(RVInstruction.U(RVInstrKind.Lui, ToRegister(destination), step.Operand));
                            break;
                        case RVInstrKind.Slli:
                            EmitShiftImmediate(RVInstrKind.Slli, destination, destination, step.Operand);
                            break;
                        default:
                            EmitImm(step.Kind, destination, step.FromZero ? MachineRegister.X0 : destination, step.Operand);
                            break;
                    }
                }

                SetIntegerRepresentation(destination, representation);
                return;
            }

            var label = _owner.CreateLocalLabel("i64");
            var offset = _owner._rodata.Align(8);
            _owner._rodata.DefineSymbol(label, offset, 8, RVObjectSymbolBinding.Local, _owner._symbols);
            _owner._rodata.EmitInteger(value, 8, _owner._target.Endianness);
            MaterializeSymbolAddress(label, destination);
            LoadFromMemory(destination, destination, 0, 8, signed: false);
            SetIntegerRepresentation(destination, representation);
        }

        private readonly struct WideImmediateStep
        {
            public RVInstrKind Kind { get; }
            public int Operand { get; }
            public bool FromZero { get; }

            public WideImmediateStep(RVInstrKind kind, int operand, bool fromZero = false)
            {
                Kind = kind;
                Operand = operand;
                FromZero = fromZero;
            }
        }

        // Splits a constant into lui/addi/slli steps the way the ISA manual builds one, low bits last
        private static bool TryPlanWideImmediate(long value, List<WideImmediateStep> plan, int budget)
        {
            if (plan.Count >= budget)
                return false;

            if (FitsSignedImmediate(value, 12))
            {
                plan.Add(new WideImmediateStep(RVInstrKind.Addi, (int)value, fromZero: true));
                return true;
            }

            if (value >= int.MinValue && value <= int.MaxValue)
            {
                var upper = (int)((value + 0x800L) >> 12);
                var lower = (int)(value - ((long)upper << 12));
                plan.Add(new WideImmediateStep(RVInstrKind.Lui, upper));
                if (lower != 0)
                    plan.Add(new WideImmediateStep(RVInstrKind.Addiw, lower));
                return plan.Count <= budget;
            }

            var low12 = (value << 52) >> 52;
            var upper12 = value - low12;
            var shift = BitOperations.TrailingZeroCount((ulong)(upper12 >> 12)) + 12;
            if (!TryPlanWideImmediate(upper12 >> shift, plan, budget))
                return false;

            plan.Add(new WideImmediateStep(RVInstrKind.Slli, shift));
            if (low12 != 0)
                plan.Add(new WideImmediateStep(RVInstrKind.Addi, (int)low12));
            return plan.Count <= budget;
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


        private int VectorGroupOf(QualifiedType type)
            => type.Type is RVVectorType vector ? vector.RegisterCount : 1;

        private void MoveVectorRegister(MachineRegister destination, MachineRegister source, int groupRegisters = 1)
        {
            if (destination == source)
                return;

            // A segment tuple is a run of registers rather than a group, so it moves one at a time
            if (groupRegisters is not (1 or 2 or 4 or 8) ||
                (((int)destination | (int)source) - (int)MachineRegister.V0) % groupRegisters != 0)
            {
                var ascending = (int)destination < (int)source;
                for (var i = 0; i < groupRegisters; i++)
                {
                    var offset = ascending ? i : groupRegisters - 1 - i;
                    Emit(RVInstruction.Vv(RVInstrKind.Vmv1rV,
                        ToVectorRegister((MachineRegister)((int)destination + offset)),
                        ToVectorRegister((MachineRegister)((int)source + offset)), RVRegister.V0));
                }
                return;
            }

            var shape = ByteVectorShape(groupRegisters, destination, source);
            EmitVectorMaxConfiguration(shape);
            Emit(RVInstruction.Vx(RVInstrKind.VaddVx, ToVectorRegister(destination), ToVectorRegister(source), RVRegister.X0));
        }

        private void ZeroVectorRegister(MachineRegister destination, int groupRegisters = 1)
        {
            var shape = ByteVectorShape(groupRegisters, destination, destination);
            EmitVectorMaxConfiguration(shape);
            Emit(RVInstruction.Vv(RVInstrKind.VxorVv, ToVectorRegister(destination), ToVectorRegister(destination), ToVectorRegister(destination)));
        }

        /// <summary>Addresses a whole register group as bytes, which every group-wide copy does</summary>
        private static VectorShape ByteVectorShape(int groupRegisters, MachineRegister first, MachineRegister second)
        {
            if (groupRegisters is not (1 or 2 or 4 or 8))
                throw new NotSupportedException("A vector register group must hold one, two, four or eight registers.");
            if ((((int)first | (int)second) - (int)MachineRegister.V0) % groupRegisters != 0)
                throw new NotSupportedException("A vector register group must start on an aligned register.");
            return new VectorShape(8, System.Numerics.BitOperations.Log2((uint)groupRegisters), isFloating: false);
        }

        private void MoveRegister(MachineRegister destination, MachineRegister source, int vectorGroupRegisters = 1)
        {
            if (destination == source)
                return;
            if (IsVectorRegister(destination) || IsVectorRegister(source))
            {
                if (!IsVectorRegister(destination) || !IsVectorRegister(source))
                    throw new NotSupportedException("Cross-class register move requires an explicit conversion or bitcast.");
                MoveVectorRegister(destination, source, vectorGroupRegisters);
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

        // A value nothing reads above its own width is already what its every use wants
        private void NormalizeResultRegister(MachineRegister destination, LirVirtualRegister result)
        {
            if (_narrowStoreOnlyValues.Contains(result))
                return;
            NormalizeScalarRegister(destination, result.Type);
        }

        private void NormalizeIntegerRegister(MachineRegister register, QualifiedType type)
        {
            if (!IsIntegerLike(type) && !IsPointerLike(type))
                return;
            if (IsPointerLike(type))
                return;

            var required = CanonicalIntegerRepresentation(type);
            if (!required.IsKnown || IntegerRepresentationSatisfies(GetIntegerRepresentation(register), required))
                return;

            ExtendIntegerRegister(register, required);
        }

        private void ExtendIntegerRegister(MachineRegister register, IntegerRepresentationFact required)
        {
            var unsigned = required.Kind == IntegerRepresentationKind.ZeroExtended;
            if (_owner._target.Is64Bit)
            {
                if (required.Bits == 32)
                {
                    if (unsigned)
                    {
                        EmitShiftImmediate(RVInstrKind.Slli, register, register, 32);
                        EmitShiftImmediate(RVInstrKind.Srli, register, register, 32);
                    }
                    else
                    {
                        EmitImm(RVInstrKind.Addiw, register, register, 0);
                    }
                    SetIntegerRepresentation(register, required);
                    return;
                }

                var shift = 64 - required.Bits;
                EmitShiftImmediate(RVInstrKind.Slli, register, register, shift);
                EmitShiftImmediate(unsigned ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift);
                SetIntegerRepresentation(register, required);
                return;
            }

            var shift32 = 32 - required.Bits;
            EmitShiftImmediate(RVInstrKind.Slli, register, register, shift32);
            EmitShiftImmediate(unsigned ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift32);
            SetIntegerRepresentation(register, required);
        }

        /// <summary>Reports what a value of this type has to look like to stand in for one that wide</summary>
        private IntegerRepresentationFact WideningRepresentation(QualifiedType source, int destinationBits)
        {
            if (!IsIntegerLike(source) && !IsPointerLike(source))
                return IntegerRepresentationFact.Unknown;

            var bits = Math.Min(SizeOf(source) * 8, destinationBits);
            if (bits >= destinationBits)
                return IntegerRepresentationFact.Unknown;
            return IsUnsignedIntegerType(source) || IsPointerLike(source)
                ? IntegerRepresentationFact.ZeroExtended(bits)
                : IntegerRepresentationFact.SignExtended(bits);
        }

        // A register with no tracked fact still holds whatever the canonical form of its type guarantees
        private IntegerRepresentationFact KnownIntegerRepresentation(MachineRegister register, QualifiedType type)
        {
            var fact = GetIntegerRepresentation(register);
            return fact.IsKnown ? fact : CanonicalIntegerRepresentation(type);
        }

        /// <summary>Widens a value that is about to be read at more bits than its own type carries</summary>
        private MachineRegister ExtendOperandToWidth(MachineRegister register, QualifiedType type, int destinationBits, MachineRegister scratch)
        {
            var required = WideningRepresentation(type, destinationBits);
            if (!required.IsKnown || IntegerRepresentationSatisfies(KnownIntegerRepresentation(register, type), required))
                return register;

            if (register != scratch)
            {
                MoveRegister(scratch, register);
                register = scratch;
            }

            ExtendIntegerRegister(register, required);
            return register;
        }

        /// <summary>
        /// Reports what a register carrying a scalar across the ABI is already known to hold, which
        /// covers an incoming argument and a returned value alike. The psABI widens a narrower scalar
        /// according to its own sign and then sign-extends to XLEN, which agrees with the canonical
        /// form everywhere except a 32-bit unsigned value on a 64-bit target.
        /// </summary>
        private IntegerRepresentationFact AbiScalarRepresentation(QualifiedType type)
        {
            if (_owner._target.Is64Bit && SizeOf(type) == 4 && IsUnsignedIntegerType(type))
                return IntegerRepresentationFact.Unknown;

            return CanonicalIntegerRepresentation(type);
        }

        private IntegerRepresentationFact CanonicalIntegerRepresentation(QualifiedType type)
        {
            if (!IsIntegerLike(type) || IsPointerLike(type))
                return IntegerRepresentationFact.Unknown;

            var bits = checked(SizeOf(type) * 8);
            var registerBits = checked(_owner._target.RegisterSize * 8);
            if (bits >= registerBits)
                return IntegerRepresentationFact.Unknown;
            return IsUnsignedIntegerType(type)
                ? IntegerRepresentationFact.ZeroExtended(bits)
                : IntegerRepresentationFact.SignExtended(bits);
        }

        private bool IntegerRepresentationSatisfies(IntegerRepresentationFact actual, QualifiedType type)
        {
            if (IsPointerLike(type))
                return true;
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
            => GetIntegerRepresentation(ToRegister(register));

        private IntegerRepresentationFact GetIntegerRepresentation(RVRegister register)
        {
            if (register == RVRegister.X0)
                return IntegerRepresentationFact.ZeroExtended(1);
            var index = (int)register;
            return index > 0 && index < _integerRepresentationFacts.Length
                ? _integerRepresentationFacts[index]
                : IntegerRepresentationFact.Unknown;
        }

        private void SetIntegerRepresentation(MachineRegister register, IntegerRepresentationFact fact)
            => SetIntegerRepresentation(ToRegister(register), fact);

        private void SetIntegerRepresentation(RVRegister register, IntegerRepresentationFact fact)
        {
            var index = (int)register;
            if (index > 0 && index < _integerRepresentationFacts.Length)
                _integerRepresentationFacts[index] = fact;
        }

        private void ClearIntegerRepresentationFacts()
        {
            Array.Clear(_integerRepresentationFacts, 0, _integerRepresentationFacts.Length);
            ClearVectorConfiguration();
        }

        private void ClearVectorConfiguration()
        {
            _vectorConfigType = -1;
            _vectorConfigLength = RVRegister.Invalid;
            _vectorConfigResult = RVRegister.Invalid;
        }

        private void TrackVectorConfiguration(RVInstruction instruction)
        {
            if (_vectorConfigType < 0)
                return;

            if (instruction.Opcode is RVInstrKind.Jal or RVInstrKind.Jalr or RVInstrKind.Ecall or RVInstrKind.Ebreak
                or RVInstrKind.Vsetvl or RVInstrKind.Vsetivli or RVInstrKind.Vsetvli)
            {
                ClearVectorConfiguration();
                return;
            }

            if (RVRegisters.IsInteger(instruction.Rd) && instruction.Rd != RVRegister.X0 &&
                (instruction.Rd == _vectorConfigLength || instruction.Rd == _vectorConfigResult))
            {
                ClearVectorConfiguration();
            }
        }

        private void ClearCallerClobberedIntegerRepresentationFacts()
        {
            _integerRepresentationFacts[1] = IntegerRepresentationFact.Unknown;
            for (var i = 5; i <= 7; i++)
                _integerRepresentationFacts[i] = IntegerRepresentationFact.Unknown;
            for (var i = 10; i <= 17; i++)
                _integerRepresentationFacts[i] = IntegerRepresentationFact.Unknown;
            for (var i = 28; i <= 31; i++)
                _integerRepresentationFacts[i] = IntegerRepresentationFact.Unknown;
        }

        private void TrackIntegerRepresentation(RVInstruction instruction)
        {
            if (instruction.Opcode is RVInstrKind.Jal or RVInstrKind.Jalr or RVInstrKind.Ecall)
            {
                if (instruction.Opcode == RVInstrKind.Ecall || instruction.Rd != RVRegister.X0)
                    ClearCallerClobberedIntegerRepresentationFacts();
                return;
            }

            var rd = (int)instruction.Rd;
            if (rd <= 0 || rd >= _integerRepresentationFacts.Length)
                return;

            var source = GetIntegerRepresentation(instruction.Rs1);
            var fact = IntegerRepresentationFact.Unknown;
            switch (instruction.Opcode)
            {
                // A register move carries the source representation, so a following no-op
                // conversion does not have to re-extend the value
                case RVInstrKind.Addi when instruction.Immediate == 0:
                    fact = source;
                    break;
                case RVInstrKind.Lb:
                    fact = IntegerRepresentationFact.SignExtended(8);
                    break;
                case RVInstrKind.Lh:
                    fact = IntegerRepresentationFact.SignExtended(16);
                    break;
                case RVInstrKind.Lw:
                case RVInstrKind.LrW:
                case RVInstrKind.AmoSwapW:
                case RVInstrKind.AmoAddW:
                case RVInstrKind.AmoXorW:
                case RVInstrKind.AmoAndW:
                case RVInstrKind.AmoOrW:
                case RVInstrKind.AmoMinW:
                case RVInstrKind.AmoMaxW:
                case RVInstrKind.AmoMinuW:
                case RVInstrKind.AmoMaxuW:
                    fact = IntegerRepresentationFact.SignExtended(32);
                    break;
                case RVInstrKind.Lbu:
                    fact = IntegerRepresentationFact.ZeroExtended(8);
                    break;
                case RVInstrKind.Lhu:
                    fact = IntegerRepresentationFact.ZeroExtended(16);
                    break;
                case RVInstrKind.Lwu:
                    fact = IntegerRepresentationFact.ZeroExtended(32);
                    break;
                // A logical 32-bit right shift clears bit 31, so the sign extension to 64 bits is
                // a no-op and the result is known to be zero-extended
                case RVInstrKind.Srliw when instruction.Immediate > 0:
                    fact = IntegerRepresentationFact.ZeroExtended(32 - instruction.Immediate);
                    break;
                case RVInstrKind.Addiw:
                case RVInstrKind.Slliw:
                case RVInstrKind.Srliw:
                case RVInstrKind.Sraiw:
                case RVInstrKind.Addw:
                case RVInstrKind.Subw:
                case RVInstrKind.Sllw:
                case RVInstrKind.Srlw:
                case RVInstrKind.Sraw:
                case RVInstrKind.Mulw:
                case RVInstrKind.Divw:
                case RVInstrKind.Divuw:
                case RVInstrKind.Remw:
                case RVInstrKind.Remuw:
                case RVInstrKind.FcvtWS:
                case RVInstrKind.FcvtWuS:
                case RVInstrKind.FcvtWD:
                case RVInstrKind.FcvtWuD:
                case RVInstrKind.FmvXW:
                    fact = IntegerRepresentationFact.SignExtended(32);
                    break;
                case RVInstrKind.Slti:
                case RVInstrKind.Sltiu:
                case RVInstrKind.Slt:
                case RVInstrKind.Sltu:
                case RVInstrKind.FeqS:
                case RVInstrKind.FltS:
                case RVInstrKind.FleS:
                case RVInstrKind.FeqD:
                case RVInstrKind.FltD:
                case RVInstrKind.FleD:
                    fact = IntegerRepresentationFact.ZeroExtended(1);
                    break;
                case RVInstrKind.Addi when instruction.Rs1 == RVRegister.X0:
                    fact = RepresentationForConstant(instruction.Immediate);
                    break;
                case RVInstrKind.Addi when instruction.Immediate == 0:
                    fact = source;
                    break;
                case RVInstrKind.Xori when instruction.Immediate == 1 && source.Kind == IntegerRepresentationKind.ZeroExtended && source.Bits <= 1:
                    fact = IntegerRepresentationFact.ZeroExtended(1);
                    break;
                case RVInstrKind.Andi when instruction.Immediate >= 0:
                    fact = RepresentationForNonNegativeMask((uint)instruction.Immediate);
                    break;
                case RVInstrKind.And:
                    {
                        var right = GetIntegerRepresentation(instruction.Rs2);
                        if (source.Kind == IntegerRepresentationKind.ZeroExtended && right.Kind == IntegerRepresentationKind.ZeroExtended)
                            fact = IntegerRepresentationFact.ZeroExtended(Math.Min(source.Bits, right.Bits));
                        else if (source.Kind == IntegerRepresentationKind.ZeroExtended)
                            fact = source;
                        else if (right.Kind == IntegerRepresentationKind.ZeroExtended)
                            fact = right;
                        break;
                    }
                case RVInstrKind.Or:
                case RVInstrKind.Xor:
                    {
                        var right = GetIntegerRepresentation(instruction.Rs2);
                        if (source.Kind == IntegerRepresentationKind.ZeroExtended && right.Kind == IntegerRepresentationKind.ZeroExtended)
                            fact = IntegerRepresentationFact.ZeroExtended(Math.Max(source.Bits, right.Bits));
                        break;
                    }
                case RVInstrKind.Srli when instruction.Immediate > 0:
                    fact = IntegerRepresentationFact.ZeroExtended(Math.Max(1, _owner._target.RegisterSize * 8 - instruction.Immediate));
                    break;
                case RVInstrKind.Srai when instruction.Immediate > 0:
                    fact = IntegerRepresentationFact.SignExtended(Math.Max(1, _owner._target.RegisterSize * 8 - instruction.Immediate));
                    break;
                case RVInstrKind.Lui when _owner._target.Is64Bit:
                    fact = IntegerRepresentationFact.SignExtended(32);
                    break;
            }

            _integerRepresentationFacts[rd] = fact;
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

        private static IntegerRepresentationFact RepresentationForNonNegativeMask(uint mask)
        {
            if (mask <= 1)
                return IntegerRepresentationFact.ZeroExtended(1);
            if (mask <= byte.MaxValue)
                return IntegerRepresentationFact.ZeroExtended(8);
            if (mask <= ushort.MaxValue)
                return IntegerRepresentationFact.ZeroExtended(16);
            return IntegerRepresentationFact.ZeroExtended(32);
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
        {
            _owner._text.Emit(instruction);
            TrackIntegerRepresentation(instruction);
            TrackVectorConfiguration(instruction);
        }

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

        private int BlockAlignment(QualifiedType type)
            => Math.Max(1, _owner._target.AlignOf(type));

        private int SizeOfRegisterType(QualifiedType type)
        {
            if (type.Type is RVVectorType vector)
                return TargetRegisterInfo.VectorRegisterSize(_owner._target) * vector.RegisterCount;
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

        private bool TypesNeedIntegerConversion(QualifiedType source, QualifiedType destination)
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

        public void RelaxBranches(List<RVObjectSymbol> symbols)
        {
            while (true)
            {
                var relaxations = new BranchRelaxationKind[_instructions.Count];
                var extraInstructions = new int[_instructions.Count];
                var relaxationCount = 0;
                for (var i = 0; i < _instructions.Count; i++)
                {
                    var instruction = _instructions[i];
                    if (instruction.Symbol is null || !_labels.TryGetValue(instruction.Symbol, out var targetOffset))
                        continue;

                    var displacement = checked(targetOffset - i * 4);
                    BranchRelaxationKind relaxation;
                    if (IsConditionalBranch(instruction.Opcode))
                        relaxation = FitsBranchImmediate(displacement) ? BranchRelaxationKind.None : BranchRelaxationKind.ConditionalViaJal;
                    else if (instruction.Opcode == RVInstrKind.Jal)
                        relaxation = FitsJalImmediate(displacement) ? BranchRelaxationKind.None : BranchRelaxationKind.LongJal;
                    else
                        relaxation = BranchRelaxationKind.None;

                    if (relaxation == BranchRelaxationKind.None)
                        continue;
                    relaxations[i] = relaxation;
                    extraInstructions[i] = 1;
                    relaxationCount++;
                }

                if (relaxationCount == 0)
                    return;

                var prefix = new int[_instructions.Count + 1];
                for (var i = 0; i < _instructions.Count; i++)
                    prefix[i + 1] = checked(prefix[i] + extraInstructions[i]);

                int RemapOffset(int offset)
                {
                    var instructionIndex = offset / 4;
                    if ((uint)instructionIndex > (uint)_instructions.Count)
                        throw new InvalidOperationException("RISC-V text offset is outside the instruction stream.");
                    return checked(offset + prefix[instructionIndex] * 4);
                }

                var rewritten = new List<RVInstruction>(checked(_instructions.Count + prefix[_instructions.Count]));
                var generatedLabels = new List<KeyValuePair<string, int>>();
                var generatedRelocations = new List<RVObjectRelocation>();
                for (var i = 0; i < _instructions.Count; i++)
                {
                    var instruction = _instructions[i];
                    var relaxation = relaxations[i];
                    if (relaxation == BranchRelaxationKind.None)
                    {
                        rewritten.Add(instruction);
                        continue;
                    }

                    var rewrittenOffset = checked(rewritten.Count * 4);
                    switch (relaxation)
                    {
                        case BranchRelaxationKind.ConditionalViaJal:
                            {
                                var skipLabel = CreateRelaxationLabel();
                                rewritten.Add(RVInstruction.B(InvertBranch(instruction.Opcode), instruction.Rs1, instruction.Rs2, skipLabel));
                                rewritten.Add(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, instruction.Symbol!));
                                generatedLabels.Add(new KeyValuePair<string, int>(skipLabel, checked(rewrittenOffset + 8)));
                                generatedRelocations.Add(new RVObjectRelocation(
                                    Name,
                                    checked(rewrittenOffset + 4),
                                    instruction.Symbol!,
                                    0,
                                    RVObjectRelocationKind.Jal20));
                                break;
                            }
                        case BranchRelaxationKind.LongJal:
                            {
                                var baseRegister = instruction.Rd == RVRegister.X0 ? RVRegister.X5 : instruction.Rd;
                                rewritten.Add(new RVInstruction(
                                    RVInstrKind.Auipc,
                                    baseRegister,
                                    symbol: instruction.Symbol,
                                    relocationKind: RVRelocationKind.AbsoluteUpper20));
                                rewritten.Add(new RVInstruction(
                                    RVInstrKind.Jalr,
                                    instruction.Rd,
                                    baseRegister,
                                    immediate: 0,
                                    symbol: instruction.Symbol,
                                    relocationKind: RVRelocationKind.AbsoluteLow12));
                                generatedRelocations.Add(new RVObjectRelocation(
                                    Name,
                                    rewrittenOffset,
                                    instruction.Symbol!,
                                    0,
                                    RVObjectRelocationKind.PcrelHi20));
                                generatedRelocations.Add(new RVObjectRelocation(
                                    Name,
                                    checked(rewrittenOffset + 4),
                                    instruction.Symbol!,
                                    0,
                                    RVObjectRelocationKind.PcrelLo12I));
                                break;
                            }
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
                    var oldInstructionIndex = relocation.Offset / 4;
                    if ((uint)oldInstructionIndex < (uint)relaxations.Length &&
                        relaxations[oldInstructionIndex] != BranchRelaxationKind.None &&
                        relocation.Kind is RVObjectRelocationKind.Branch12 or RVObjectRelocationKind.Jal20)
                    {
                        continue;
                    }

                    _relocations.Add(new RVObjectRelocation(
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
                    symbols[i] = new RVObjectSymbol(
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

        private string CreateRelaxationLabel()
        {
            while (true)
            {
                var label = $"__riscv_relax_skip_{_nextRelaxationLabelId++}";
                if (!_labels.ContainsKey(label))
                    return label;
            }
        }

        private static bool FitsJalImmediate(int displacement)
            => (displacement & 1) == 0 && displacement >= -1048576 && displacement <= 1048574;

        private static bool FitsBranchImmediate(int displacement)
            => (displacement & 1) == 0 && displacement >= -4096 && displacement <= 4094;

        private static bool IsConditionalBranch(RVInstrKind opcode)
            => opcode is RVInstrKind.Beq or RVInstrKind.Bne or RVInstrKind.Blt or RVInstrKind.Bge or RVInstrKind.Bltu or RVInstrKind.Bgeu;

        private static RVInstrKind InvertBranch(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.Beq => RVInstrKind.Bne,
                RVInstrKind.Bne => RVInstrKind.Beq,
                RVInstrKind.Blt => RVInstrKind.Bge,
                RVInstrKind.Bge => RVInstrKind.Blt,
                RVInstrKind.Bltu => RVInstrKind.Bgeu,
                RVInstrKind.Bgeu => RVInstrKind.Bltu,
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };

        private enum BranchRelaxationKind : byte
        {
            None,
            ConditionalViaJal,
            LongJal,
        }

        private int _nextRelaxationLabelId;

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
