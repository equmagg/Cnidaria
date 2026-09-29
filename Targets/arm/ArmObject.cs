using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Arm;

internal static class ArmObjectLinker
{
    public static ArmLinkedImage LinkFlat(
        ArmProgram obj,
        ulong imageBase = 0,
        IReadOnlyDictionary<string, ulong>? externalSymbols = null)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));

        var sections = LayoutSections(obj, imageBase);
        var symbols = BuildSymbolAddressMap(obj, sections, externalSymbols);
        var size = sections.Values.Count == 0 ? 0 : sections.Values.Max(static section => section.Offset + section.Size);
        var image = new byte[size];

        EncodeText(obj, sections, symbols, image);
        CopyDataSections(obj, sections, image);
        ApplyDataRelocations(obj, sections, symbols, image);

        var entryAddress = string.IsNullOrEmpty(obj.EntrySymbol) ? imageBase : ResolveSymbol(symbols, obj.EntrySymbol);
        var entryOffset = checked((int)(entryAddress - imageBase));
        return new ArmLinkedImage(obj, imageBase, entryAddress, entryOffset, sections, symbols, image);
    }

    private static Dictionary<string, ArmLinkedSection> LayoutSections(ArmProgram obj, ulong imageBase)
    {
        var result = new Dictionary<string, ArmLinkedSection>(StringComparer.Ordinal);
        var offset = 0;
        AddSection(result, obj.Text.SizeInBytes, ".text", ArmObjectSectionKind.Text, obj.Target.Is64Bit ? 16 : 4, imageBase, ref offset);
        foreach (var section in obj.DataSections)
        {
            var size = section.Kind == ArmObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
            AddSection(result, size, section.Name, section.Kind, section.Alignment, imageBase, ref offset);
        }
        return result;
    }

    private static void AddSection(
        Dictionary<string, ArmLinkedSection> sections,
        int size,
        string name,
        ArmObjectSectionKind kind,
        int alignment,
        ulong imageBase,
        ref int offset)
    {
        alignment = Math.Max(1, alignment);
        offset = AlignUp(offset, alignment);
        sections[name] = new ArmLinkedSection(name, kind, offset, Math.Max(0, size), alignment, checked(imageBase + (ulong)offset));
        offset = checked(offset + Math.Max(0, size));
    }

    private static Dictionary<string, ulong> BuildSymbolAddressMap(
        ArmProgram obj,
        IReadOnlyDictionary<string, ArmLinkedSection> sections,
        IReadOnlyDictionary<string, ulong>? externalSymbols)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var text = sections[".text"];
        foreach (var label in obj.Text.Labels)
            result[label.Key] = checked(text.Address + (ulong)label.Value);

        foreach (var symbol in obj.Symbols)
        {
            if (symbol.Binding == ArmObjectSymbolBinding.External || string.IsNullOrEmpty(symbol.SectionName))
                continue;
            if (!sections.TryGetValue(symbol.SectionName, out var section))
                throw new InvalidOperationException($"Symbol section does not exist: {symbol.SectionName}");
            result[symbol.Name] = checked(section.Address + (ulong)symbol.Offset);
        }

        if (externalSymbols is not null)
        {
            foreach (var pair in externalSymbols)
                result[pair.Key] = pair.Value;
        }
        return result;
    }

    private static void EncodeText(
        ArmProgram obj,
        IReadOnlyDictionary<string, ArmLinkedSection> sections,
        IReadOnlyDictionary<string, ulong> symbols,
        byte[] image)
    {
        var text = sections[".text"];
        var relocations = obj.Text.Relocations.ToDictionary(static relocation => relocation.Offset);
        for (var index = 0; index < obj.Text.Instructions.Length; index++)
        {
            var instructionOffset = checked(index * 4);
            var instruction = obj.Text.Instructions[index];
            if (relocations.TryGetValue(instructionOffset, out var relocation))
                instruction = ApplyTextRelocation(instruction, relocation, checked(text.Address + (ulong)instructionOffset), symbols);
            var word = ArmCodeEncoder.EncodeWord(instruction, obj.Target, checked(text.Address + (ulong)instructionOffset), symbols);
            ArmCodeEncoder.WriteWord(image, checked(text.Offset + instructionOffset), word, obj.Target.Endianness);
        }
    }

    private static ArmInstruction ApplyTextRelocation(
        ArmInstruction instruction,
        ArmObjectRelocation relocation,
        ulong pc,
        IReadOnlyDictionary<string, ulong> symbols)
    {
        var address = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
        switch (relocation.Kind)
        {
            case ArmObjectRelocationKind.ArmBranch24:
            case ArmObjectRelocationKind.ArmCall24:
                return instruction.WithOperand0(ArmOperand.ImmediateOperand(checked(address - (long)pc)));
            case ArmObjectRelocationKind.ArmMovw16:
                return instruction.WithOperand1(ArmOperand.ImmediateOperand(address & 0xFFFF));
            case ArmObjectRelocationKind.ArmMovt16:
                return instruction.WithOperand1(ArmOperand.ImmediateOperand((address >> 16) & 0xFFFF));
            case ArmObjectRelocationKind.AArch64Branch26:
            case ArmObjectRelocationKind.AArch64Call26:
            case ArmObjectRelocationKind.AArch64ConditionalBranch19:
                return instruction.WithOperand0(ArmOperand.ImmediateOperand(checked(address - (long)pc)));
            case ArmObjectRelocationKind.AArch64CompareBranch19:
                return instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(address - (long)pc)));
            case ArmObjectRelocationKind.AArch64TestBranch14:
                return instruction.WithOperand2(ArmOperand.ImmediateOperand(checked(address - (long)pc)));
            case ArmObjectRelocationKind.AArch64Adr21:
                return instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(address - (long)pc)));
            case ArmObjectRelocationKind.AArch64Adrp21:
                return instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(((address & ~0xFFFL) - ((long)pc & ~0xFFFL)))));
            case ArmObjectRelocationKind.AArch64AddLow12:
                return instruction.WithOperand2(ArmOperand.ImmediateOperand(address & 0xFFF));
            case ArmObjectRelocationKind.AArch64LoadLiteral19:
                return instruction.WithOperand1(ArmOperand.Literal(checked(address - (long)pc), instruction.Operand0.Size));
            default:
                throw new NotSupportedException($"Unsupported text relocation: {relocation.Kind}");
        }
    }

    private static void CopyDataSections(
        ArmProgram obj,
        IReadOnlyDictionary<string, ArmLinkedSection> sections,
        byte[] image)
    {
        foreach (var section in obj.DataSections)
        {
            if (section.Kind == ArmObjectSectionKind.Bss)
                continue;
            var layout = sections[section.Name];
            for (var i = 0; i < section.Data.Length; i++)
                image[layout.Offset + i] = section.Data[i];
        }
    }

    private static void ApplyDataRelocations(
        ArmProgram obj,
        IReadOnlyDictionary<string, ArmLinkedSection> sections,
        IReadOnlyDictionary<string, ulong> symbols,
        byte[] image)
    {
        foreach (var section in obj.DataSections)
        {
            if (section.Relocations.IsDefaultOrEmpty)
                continue;
            if (section.Kind == ArmObjectSectionKind.Bss)
                throw new InvalidOperationException("BSS relocations cannot be represented in a flat loaded image.");

            var layout = sections[section.Name];
            foreach (var relocation in section.Relocations)
            {
                var signed = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
                if (signed < 0)
                    throw new OverflowException("ARM absolute relocation resolved to a negative address.");
                var value = (ulong)signed;
                var offset = checked(layout.Offset + relocation.Offset);
                switch (relocation.Kind)
                {
                    case ArmObjectRelocationKind.AbsolutePointer:
                        WriteAbsolute(image, offset, value, obj.Target.Is32Bit ? 4 : 8, obj.Target.Endianness);
                        break;
                    case ArmObjectRelocationKind.Absolute32:
                        WriteAbsolute(image, offset, value, 4, obj.Target.Endianness);
                        break;
                    case ArmObjectRelocationKind.Absolute64:
                        WriteAbsolute(image, offset, value, 8, obj.Target.Endianness);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported ARM data relocation: {relocation.Kind}");
                }
            }
        }
    }

    private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
    {
        if (symbols.TryGetValue(symbol, out var address))
            return address;
        throw new KeyNotFoundException($"Undefined ARM symbol: {symbol}");
    }

    private static void WriteAbsolute(byte[] image, int offset, ulong value, int size, TargetEndianness endianness)
    {
        if (size == 4 && value > uint.MaxValue)
            throw new OverflowException("ARM 32-bit absolute relocation overflow.");
        if (endianness == TargetEndianness.Little)
        {
            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> (i * 8));
        }
        else
        {
            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> ((size - 1 - i) * 8));
        }
    }

    private static int AlignUp(int value, int alignment)
    {
        if (alignment <= 1)
            return value;
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }
}

public static class ArmObjectComposer
{
    public static ArmProgram Compose(ArmProgram primary, params ArmProgram[] libraries)
    {
        if (primary is null)
            throw new ArgumentNullException(nameof(primary));
        libraries ??= Array.Empty<ArmProgram>();

        var inputs = new ArmProgram[libraries.Length + 1];
        inputs[0] = primary;
        for (var i = 0; i < libraries.Length; i++)
        {
            inputs[i + 1] = libraries[i] ?? throw new ArgumentNullException(nameof(libraries));
            ValidateTargetCompatibility(primary.Target, inputs[i + 1].Target);
        }

        var globalDefinitions = ResolveGlobalDefinitions(inputs);
        var renames = BuildLocalRenameMaps(inputs);
        var textBases = new int[inputs.Length];
        var instructions = ImmutableArray.CreateBuilder<ArmInstruction>();
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var textRelocations = ImmutableArray.CreateBuilder<ArmObjectRelocation>();
        var textOffset = 0;

        for (var i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            textBases[i] = textOffset;
            foreach (var instruction in input.Text.Instructions)
                instructions.Add(RewriteInstruction(instruction, renames[i]));
            foreach (var pair in input.Text.Labels)
            {
                var name = Rename(renames[i], pair.Key);
                if (!labels.TryAdd(name, checked(textOffset + pair.Value)))
                    throw new InvalidOperationException($"Duplicate composed ARM text label: {name}");
            }
            foreach (var relocation in input.Text.Relocations)
            {
                textRelocations.Add(new ArmObjectRelocation(
                    ".text",
                    checked(textOffset + relocation.Offset),
                    Rename(renames[i], relocation.SymbolName),
                    relocation.Addend,
                    relocation.Kind));
            }
            textOffset = checked(textOffset + input.Text.SizeInBytes);
        }

        var sectionBuilders = new Dictionary<string, ComposedSectionBuilder>(StringComparer.Ordinal);
        var sectionOrder = new List<string>();
        var sectionBases = new Dictionary<(int InputIndex, string SectionName), int>();
        for (var i = 0; i < inputs.Length; i++)
        {
            foreach (var section in inputs[i].DataSections)
            {
                if (!sectionBuilders.TryGetValue(section.Name, out var builder))
                {
                    builder = new ComposedSectionBuilder(section.Name, section.Kind);
                    sectionBuilders.Add(section.Name, builder);
                    sectionOrder.Add(section.Name);
                }
                else if (builder.Kind != section.Kind)
                {
                    throw new InvalidOperationException($"Cannot compose ARM sections with different kinds: {section.Name}");
                }
                sectionBases[(i, section.Name)] = builder.Append(section, renames[i]);
            }
        }

        var symbols = ImmutableArray.CreateBuilder<ArmObjectSymbol>();
        var emittedExternalSymbols = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < inputs.Length; i++)
        {
            for (var s = 0; s < inputs[i].Symbols.Length; s++)
            {
                var symbol = inputs[i].Symbols[s];
                if (symbol.Kind == ArmObjectSymbolKind.Section)
                    continue;
                var name = Rename(renames[i], symbol.Name);
                if (symbol.Binding == ArmObjectSymbolBinding.External)
                {
                    if (!globalDefinitions.Contains(name) && emittedExternalSymbols.Add(name))
                        symbols.Add(new ArmObjectSymbol(name, string.Empty, 0, 0, ArmObjectSymbolBinding.External, symbol.Kind));
                    continue;
                }
                if (symbol.Binding == ArmObjectSymbolBinding.Global &&
                    !globalDefinitions.IsWinner(i, s, symbol.Name))
                {
                    continue;
                }
                int offset;
                if (StringComparer.Ordinal.Equals(symbol.SectionName, ".text"))
                {
                    offset = checked(textBases[i] + symbol.Offset);
                }
                else
                {
                    if (!sectionBases.TryGetValue((i, symbol.SectionName), out var sectionBase))
                        throw new InvalidOperationException($"Symbol section is missing from composed ARM object: {symbol.SectionName}");
                    offset = checked(sectionBase + symbol.Offset);
                }
                symbols.Add(new ArmObjectSymbol(name, symbol.SectionName, offset, symbol.Size, symbol.Binding, symbol.Kind, symbol.IsTentative));
            }
        }

        symbols.Add(new ArmObjectSymbol(".text", ".text", 0, textOffset, ArmObjectSymbolBinding.Local, ArmObjectSymbolKind.Section));
        var dataSections = ImmutableArray.CreateBuilder<ArmDataSection>(sectionOrder.Count);
        foreach (var name in sectionOrder)
        {
            var section = sectionBuilders[name].ToSection();
            dataSections.Add(section);
            symbols.Add(new ArmObjectSymbol(
                section.Name,
                section.Name,
                0,
                section.Kind == ArmObjectSectionKind.Bss ? section.BssSize : section.Data.Length,
                ArmObjectSymbolBinding.Local,
                ArmObjectSymbolKind.Section));
        }

        return new ArmProgram(
            primary.Target,
            new ArmTextSection(instructions.ToImmutable(), labels, textRelocations.ToImmutable()),
            dataSections.ToImmutable(),
            symbols.ToImmutable(),
            Rename(renames[0], primary.EntrySymbol));
    }

    private static ArmInstruction RewriteInstruction(ArmInstruction instruction, IReadOnlyDictionary<string, string> renames)
        => instruction
            .WithOperand0(RewriteOperand(instruction.Operand0, renames))
            .WithOperand1(RewriteOperand(instruction.Operand1, renames))
            .WithOperand2(RewriteOperand(instruction.Operand2, renames))
            .WithOperand3(RewriteOperand(instruction.Operand3, renames));

    private static ArmOperand RewriteOperand(ArmOperand operand, IReadOnlyDictionary<string, string> renames)
        => operand.Symbol is null ? operand : operand.WithSymbol(Rename(renames, operand.Symbol), operand.RelocationKind, operand.Addend);

    private static GlobalDefinitions ResolveGlobalDefinitions(IReadOnlyList<ArmProgram> inputs)
    {
        var candidates = new Dictionary<string, List<GlobalDefinitionCandidate>>(StringComparer.Ordinal);
        var externalKinds = new Dictionary<string, ArmObjectSymbolKind>(StringComparer.Ordinal);
        for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            var symbols = inputs[inputIndex].Symbols;
            for (var symbolIndex = 0; symbolIndex < symbols.Length; symbolIndex++)
            {
                var symbol = symbols[symbolIndex];
                if (symbol.Kind == ArmObjectSymbolKind.Section || string.IsNullOrEmpty(symbol.Name))
                    continue;

                if (symbol.Binding == ArmObjectSymbolBinding.External)
                {
                    if (externalKinds.TryGetValue(symbol.Name, out var externalKind) && externalKind != symbol.Kind)
                        throw new InvalidOperationException($"Conflicting external symbol kinds for '{symbol.Name}'.");
                    externalKinds[symbol.Name] = symbol.Kind;
                    continue;
                }

                if (symbol.Binding != ArmObjectSymbolBinding.Global)
                    continue;

                if (symbol.IsTentative && symbol.Kind != ArmObjectSymbolKind.Object)
                    throw new InvalidOperationException($"Only object symbols can be tentative: {symbol.Name}");

                if (!candidates.TryGetValue(symbol.Name, out var definitions))
                {
                    definitions = new List<GlobalDefinitionCandidate>();
                    candidates.Add(symbol.Name, definitions);
                }
                definitions.Add(new GlobalDefinitionCandidate(inputIndex, symbolIndex, symbol));
            }
        }

        var winners = new Dictionary<string, GlobalDefinitionCandidate>(StringComparer.Ordinal);
        foreach (var pair in candidates)
        {
            var definitions = pair.Value;
            var expectedKind = definitions[0].Symbol.Kind;
            for (var i = 1; i < definitions.Count; i++)
            {
                if (definitions[i].Symbol.Kind != expectedKind)
                    throw new InvalidOperationException($"Conflicting symbol kinds for '{pair.Key}'.");
            }

            GlobalDefinitionCandidate? strong = null;
            GlobalDefinitionCandidate? tentative = null;
            foreach (var definition in definitions)
            {
                if (!definition.Symbol.IsTentative)
                {
                    if (strong.HasValue)
                        throw new InvalidOperationException($"Duplicate global symbol: {pair.Key}");
                    strong = definition;
                    continue;
                }

                if (!tentative.HasValue || definition.Symbol.Size > tentative.Value.Symbol.Size)
                    tentative = definition;
            }

            if (!strong.HasValue && !tentative.HasValue)
                throw new InvalidOperationException("Global symbol resolution produced no candidate.");

            winners.Add(pair.Key, strong ?? tentative!.Value);
        }

        foreach (var external in externalKinds)
        {
            if (winners.TryGetValue(external.Key, out var definition) && definition.Symbol.Kind != external.Value)
                throw new InvalidOperationException($"Conflicting symbol kinds for '{external.Key}'.");
        }

        return new GlobalDefinitions(winners);
    }

    private readonly struct GlobalDefinitionCandidate
    {
        public int InputIndex { get; }
        public int SymbolIndex { get; }
        public ArmObjectSymbol Symbol { get; }

        public GlobalDefinitionCandidate(int inputIndex, int symbolIndex, ArmObjectSymbol symbol)
        {
            InputIndex = inputIndex;
            SymbolIndex = symbolIndex;
            Symbol = symbol;
        }
    }

    private sealed class GlobalDefinitions
    {
        private readonly Dictionary<string, GlobalDefinitionCandidate> _winners;

        public GlobalDefinitions(Dictionary<string, GlobalDefinitionCandidate> winners)
        {
            _winners = winners;
        }

        public bool Contains(string name)
            => _winners.ContainsKey(name);

        public bool IsWinner(int inputIndex, int symbolIndex, string name)
            => _winners.TryGetValue(name, out var winner) &&
               winner.InputIndex == inputIndex &&
               winner.SymbolIndex == symbolIndex;
    }

    private static Dictionary<string, string>[] BuildLocalRenameMaps(IReadOnlyList<ArmProgram> inputs)
    {
        var result = new Dictionary<string, string>[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var globals = new HashSet<string>(
                input.Symbols
                    .Where(static symbol => symbol.Binding == ArmObjectSymbolBinding.Global && symbol.Kind != ArmObjectSymbolKind.Section)
                    .Select(static symbol => symbol.Name),
                StringComparer.Ordinal);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var prefix = $".Larm{i}_";

            void Add(string name)
            {
                if (map.ContainsKey(name))
                    return;
                var baseName = prefix + Sanitize(name);
                var candidate = baseName;
                var suffix = 0;
                while (!used.Add(candidate))
                    candidate = $"{baseName}_{(++suffix)}";
                map.Add(name, candidate);
            }

            foreach (var pair in input.Text.Labels)
            {
                if (!globals.Contains(pair.Key))
                    Add(pair.Key);
            }
            foreach (var symbol in input.Symbols)
            {
                if (symbol.Binding == ArmObjectSymbolBinding.Local && symbol.Kind != ArmObjectSymbolKind.Section)
                    Add(symbol.Name);
            }
            result[i] = map;
        }
        return result;
    }

    private static string Rename(IReadOnlyDictionary<string, string> renames, string name)
        => renames.TryGetValue(name, out var renamed) ? renamed : name;

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "symbol";
        var characters = name.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (!char.IsLetterOrDigit(characters[i]) && characters[i] is not '_' and not '$')
                characters[i] = '_';
        }
        return new string(characters);
    }

    private static void ValidateTargetCompatibility(ArmTarget primary, ArmTarget other)
    {
        if (primary.XLen != other.XLen ||
            primary.Abi != other.Abi ||
            primary.Isa != other.Isa ||
            primary.Endianness != other.Endianness ||
            primary.OperatingSystem != other.OperatingSystem)
        {
            throw new InvalidOperationException("Cannot compose ABI-incompatible ARM objects.");
        }
    }

    private sealed class ComposedSectionBuilder
    {
        private readonly List<byte> _data = new List<byte>();
        private readonly ImmutableArray<ArmObjectRelocation>.Builder _relocations = ImmutableArray.CreateBuilder<ArmObjectRelocation>();
        private int _bssSize;

        public string Name { get; }
        public ArmObjectSectionKind Kind { get; }
        public int Alignment { get; private set; } = 1;

        public ComposedSectionBuilder(string name, ArmObjectSectionKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public int Append(ArmDataSection section, IReadOnlyDictionary<string, string> renames)
        {
            Alignment = Math.Max(Alignment, section.Alignment);
            int offset;
            if (Kind == ArmObjectSectionKind.Bss)
            {
                offset = AlignUp(_bssSize, section.Alignment);
                _bssSize = checked(offset + section.BssSize);
            }
            else
            {
                offset = AlignUp(_data.Count, section.Alignment);
                while (_data.Count < offset)
                    _data.Add(0);
                _data.AddRange(section.Data);
            }
            foreach (var relocation in section.Relocations)
            {
                _relocations.Add(new ArmObjectRelocation(
                    Name,
                    checked(offset + relocation.Offset),
                    Rename(renames, relocation.SymbolName),
                    relocation.Addend,
                    relocation.Kind));
            }
            return offset;
        }

        public ArmDataSection ToSection()
            => new ArmDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), _bssSize, _relocations.ToImmutable());

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }
}
