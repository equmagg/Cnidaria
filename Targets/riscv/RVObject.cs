using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.RiscV
{

    internal static class RVObjectLinker
    {
        public static RVLinkedImage LinkFlat(
            RiscVProgram obj,
            ulong imageBase = 0,
            IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            var sections = LayoutSections(obj, imageBase);
            var symbols = BuildSymbolAddressMap(obj, sections, externalSymbols);
            var size = sections.Values.Count == 0 ? 0 : sections.Values.Max(static s => s.Offset + s.Size);
            var image = new byte[size];

            EncodeText(obj, sections, symbols, image);
            CopyDataSections(obj, sections, image);
            ApplyDataRelocations(obj, sections, symbols, image);

            var entryAddress = string.IsNullOrEmpty(obj.EntrySymbol) ? imageBase : ResolveSymbol(symbols, obj.EntrySymbol);
            var entryOffset = checked((int)(entryAddress - imageBase));
            return new RVLinkedImage(obj, imageBase, entryAddress, entryOffset, sections, symbols, image);
        }

        private static Dictionary<string, RVLinkedSection> LayoutSections(RiscVProgram obj, ulong imageBase)
        {
            var result = new Dictionary<string, RVLinkedSection>(StringComparer.Ordinal);
            var offset = 0;
            AddSection(result, obj.Text.SizeInBytes, ".text", RVObjectSectionKind.Text, 4, imageBase, ref offset);
            foreach (var section in obj.DataSections)
            {
                var size = section.Kind == RVObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
                AddSection(result, size, section.Name, section.Kind, section.Alignment, imageBase, ref offset);
            }
            return result;
        }

        private static void AddSection(
            Dictionary<string, RVLinkedSection> sections,
            int size,
            string name,
            RVObjectSectionKind kind,
            int alignment,
            ulong imageBase,
            ref int offset)
        {
            alignment = Math.Max(1, alignment);
            offset = AlignUp(offset, alignment);
            sections[name] = new RVLinkedSection(name, kind, offset, Math.Max(0, size), alignment, checked(imageBase + (ulong)offset));
            offset = checked(offset + Math.Max(0, size));
        }

        private static Dictionary<string, ulong> BuildSymbolAddressMap(
            RiscVProgram obj,
            IReadOnlyDictionary<string, RVLinkedSection> sections,
            IReadOnlyDictionary<string, ulong>? externalSymbols)
        {
            var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var text = sections[".text"];
            foreach (var label in obj.Text.Labels)
                result[label.Key] = checked(text.Address + (ulong)label.Value);

            foreach (var symbol in obj.Symbols)
            {
                if (symbol.Binding == RVObjectSymbolBinding.External)
                    continue;
                if (string.IsNullOrEmpty(symbol.SectionName))
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
            RiscVProgram obj,
            IReadOnlyDictionary<string, RVLinkedSection> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            var text = sections[".text"];
            var relocations = obj.Text.Relocations.ToDictionary(r => r.Offset, r => r);
            var sectionOffset = 0;
            for (var i = 0; i < obj.Text.Instructions.Length; i++)
            {
                var instruction = obj.Text.Instructions[i];
                if (relocations.TryGetValue(sectionOffset, out var relocation))
                    instruction = ResolveRelocatedInstruction(obj, instruction, relocation, sectionOffset, relocations, text.Address, symbols);
                else if (instruction.HasSymbol)
                    instruction = ResolveSymbolicInstruction(instruction, checked(text.Address + (ulong)sectionOffset), symbols);

                var encoded = RiscVCodeEncoder.Encode(instruction, obj.Target);
                var size = RVInstructionTable.GetEncodedSize(instruction.Opcode);
                RiscVCodeEncoder.WriteInstruction(image, checked(text.Offset + sectionOffset), encoded, size, obj.Target.Endianness);
                sectionOffset = checked(sectionOffset + size);
            }
        }

        private static RVInstruction ResolveRelocatedInstruction(
            RiscVProgram obj,
            RVInstruction instruction,
            RVObjectRelocation relocation,
            int instructionOffset,
            IReadOnlyDictionary<int, RVObjectRelocation> textRelocations,
            ulong textAddress,
            IReadOnlyDictionary<string, ulong> symbols)
        {
            var symbolAddress = ResolveSymbol(symbols, relocation.SymbolName);
            var pc = checked(textAddress + (ulong)instructionOffset);
            var value = checked((long)symbolAddress + relocation.Addend);
            switch (relocation.Kind)
            {
                case RVObjectRelocationKind.Branch12:
                    return instruction.WithImmediate(checked((int)(value - (long)pc)));
                case RVObjectRelocationKind.Jal20:
                    return instruction.WithImmediate(checked((int)(value - (long)pc)));
                case RVObjectRelocationKind.PcrelHi20:
                    return instruction.WithImmediate(PcrelHi20(value, (long)pc));
                case RVObjectRelocationKind.PcrelLo12I:
                case RVObjectRelocationKind.PcrelLo12S:
                    return instruction.WithImmediate(PcrelLo12(value, FindPcrelHiPc(relocation, instructionOffset, textRelocations, textAddress)));
                default:
                    throw new NotSupportedException($"Unsupported text relocation: {relocation.Kind}");
            }
        }

        private static RVInstruction ResolveSymbolicInstruction(
            RVInstruction instruction,
            ulong pc,
            IReadOnlyDictionary<string, ulong> symbols)
        {
            if (!instruction.HasSymbol)
                return instruction;
            var value = ResolveSymbol(symbols, instruction.Symbol!);
            switch (instruction.RelocationKind)
            {
                case RVRelocationKind.RelativeBranch:
                case RVRelocationKind.RelativeJal:
                    return instruction.WithImmediate(checked((int)((long)value - (long)pc)));
                case RVRelocationKind.AbsoluteUpper20:
                    return instruction.WithImmediate(PcrelHi20((long)value, (long)pc));
                case RVRelocationKind.AbsoluteLow12:
                    return instruction.WithImmediate(PcrelLo12((long)value, checked((long)pc - 4)));
                default:
                    throw new NotSupportedException($"Unsupported symbolic instruction relocation: {instruction.RelocationKind}");
            }
        }

        private static long FindPcrelHiPc(
            RVObjectRelocation relocation,
            int instructionOffset,
            IReadOnlyDictionary<int, RVObjectRelocation> textRelocations,
            ulong textAddress)
        {
            var previousOffset = instructionOffset - 4;
            if (textRelocations.TryGetValue(previousOffset, out var previous) &&
                previous.Kind == RVObjectRelocationKind.PcrelHi20 &&
                previous.SymbolName == relocation.SymbolName)
                return checked((long)textAddress + previousOffset);
            return checked((long)textAddress + instructionOffset);
        }

        private static void CopyDataSections(
            RiscVProgram obj,
            IReadOnlyDictionary<string, RVLinkedSection> sections,
            byte[] image)
        {
            foreach (var section in obj.DataSections)
            {
                if (section.Kind == RVObjectSectionKind.Bss)
                    continue;
                var layout = sections[section.Name];
                for (var i = 0; i < section.Data.Length; i++)
                    image[layout.Offset + i] = section.Data[i];
            }
        }

        private static void ApplyDataRelocations(
            RiscVProgram obj,
            IReadOnlyDictionary<string, RVLinkedSection> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            foreach (var section in obj.DataSections)
            {
                if (section.Relocations.IsDefaultOrEmpty)
                    continue;
                if (section.Kind == RVObjectSectionKind.Bss)
                    throw new InvalidOperationException("BSS relocations cannot be represented in a flat loaded image.");

                var layout = sections[section.Name];
                foreach (var relocation in section.Relocations)
                {
                    var signedValue = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
                    if (signedValue < 0)
                        throw new OverflowException("RISC-V absolute relocation resolved to a negative address.");
                    var value = (ulong)signedValue;
                    var offset = checked(layout.Offset + relocation.Offset);
                    switch (relocation.Kind)
                    {
                        case RVObjectRelocationKind.AbsolutePointer:
                            WriteAbsolute(image, offset, value, obj.Target.Is32Bit ? 4 : 8, obj.Target.Endianness);
                            break;
                        case RVObjectRelocationKind.Absolute32:
                            WriteAbsolute(image, offset, value, 4, obj.Target.Endianness);
                            break;
                        case RVObjectRelocationKind.Absolute64:
                            WriteAbsolute(image, offset, value, 8, obj.Target.Endianness);
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported data relocation: {relocation.Kind}");
                    }
                }
            }
        }

        private static int PcrelHi20(long value, long pc)
        {
            var delta = checked(value - pc);
            return checked((int)((delta + 0x800L) >> 12));
        }

        private static int PcrelLo12(long value, long pc)
        {
            var hi = PcrelHi20(value, pc);
            return checked((int)(value - pc - ((long)hi << 12)));
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
        {
            if (symbols.TryGetValue(symbol, out var address))
                return address;
            throw new InvalidOperationException($"Unresolved symbol: {symbol}");
        }

        private static void WriteAbsolute(byte[] image, int offset, ulong value, int size, TargetEndianness endianness)
        {
            if (size != 4 && size != 8)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (size == 4 && value > uint.MaxValue)
                throw new OverflowException("RISC-V absolute 32-bit relocation overflow.");

            if (endianness == TargetEndianness.Little)
            {
                for (var i = 0; i < size; i++)
                    image[offset + i] = (byte)(value >> (i * 8));
                return;
            }

            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> ((size - 1 - i) * 8));
        }

        private static void WriteUInt32(byte[] image, int offset, uint value, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
            {
                image[offset] = (byte)value;
                image[offset + 1] = (byte)(value >> 8);
                image[offset + 2] = (byte)(value >> 16);
                image[offset + 3] = (byte)(value >> 24);
                return;
            }

            image[offset] = (byte)(value >> 24);
            image[offset + 1] = (byte)(value >> 16);
            image[offset + 2] = (byte)(value >> 8);
            image[offset + 3] = (byte)value;
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }

    public static class RiscVObjectComposer
    {
        public static RiscVProgram Compose(RiscVProgram primary, params RiscVProgram[] libraries)
        {
            if (primary is null)
                throw new ArgumentNullException(nameof(primary));

            libraries ??= Array.Empty<RiscVProgram>();
            var inputs = new RiscVProgram[libraries.Length + 1];
            inputs[0] = primary;
            for (int i = 0; i < libraries.Length; i++)
            {
                inputs[i + 1] = libraries[i] ?? throw new ArgumentNullException(nameof(libraries));
                ValidateTargetCompatibility(primary.Target, inputs[i + 1].Target);
            }

            var globalDefinitions = ResolveGlobalDefinitions(inputs);
            var renames = BuildLocalRenameMaps(inputs);
            var textBases = new int[inputs.Length];
            var instructions = ImmutableArray.CreateBuilder<RVInstruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var textRelocations = ImmutableArray.CreateBuilder<RVObjectRelocation>();

            int textOffset = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                textBases[i] = textOffset;
                for (int instructionIndex = 0; instructionIndex < input.Text.Instructions.Length; instructionIndex++)
                {
                    var instruction = input.Text.Instructions[instructionIndex];
                    if (instruction.HasSymbol)
                    {
                        string symbol = Rename(renames[i], instruction.Symbol!);
                        if (!StringComparer.Ordinal.Equals(symbol, instruction.Symbol))
                            instruction = instruction.WithSymbol(symbol, instruction.RelocationKind);
                    }
                    instructions.Add(instruction);
                }

                foreach (var pair in input.Text.Labels)
                {
                    string name = Rename(renames[i], pair.Key);
                    if (!labels.TryAdd(name, checked(textOffset + pair.Value)))
                        throw new InvalidOperationException($"Duplicate composed text label: {name}");
                }

                for (int r = 0; r < input.Text.Relocations.Length; r++)
                {
                    var relocation = input.Text.Relocations[r];
                    textRelocations.Add(new RVObjectRelocation(
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
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                for (int s = 0; s < input.DataSections.Length; s++)
                {
                    var section = input.DataSections[s];
                    if (!sectionBuilders.TryGetValue(section.Name, out var builder))
                    {
                        builder = new ComposedSectionBuilder(section.Name, section.Kind);
                        sectionBuilders.Add(section.Name, builder);
                        sectionOrder.Add(section.Name);
                    }
                    else if (builder.Kind != section.Kind)
                    {
                        throw new InvalidOperationException($"RISC-V sections with the same name have different kinds: {section.Name}");
                    }

                    int sectionBase = builder.Append(section, renames[i]);
                    sectionBases.Add((i, section.Name), sectionBase);
                }
            }

            var symbols = ImmutableArray.CreateBuilder<RVObjectSymbol>();
            var emittedExternalSymbols = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                for (int s = 0; s < input.Symbols.Length; s++)
                {
                    var symbol = input.Symbols[s];
                    if (symbol.Kind == RVObjectSymbolKind.Section)
                        continue;

                    string name = Rename(renames[i], symbol.Name);
                    if (symbol.Binding == RVObjectSymbolBinding.External)
                    {
                        if (!globalDefinitions.Contains(name) && emittedExternalSymbols.Add(name))
                        {
                            symbols.Add(new RVObjectSymbol(
                                name,
                                string.Empty,
                                0,
                                0,
                                RVObjectSymbolBinding.External,
                                symbol.Kind));
                        }
                        continue;
                    }

                    if (symbol.Binding == RVObjectSymbolBinding.Global &&
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
                        if (!sectionBases.TryGetValue((i, symbol.SectionName), out int sectionBase))
                            throw new InvalidOperationException($"Symbol section is missing from composed RISC-V object: {symbol.SectionName}");
                        offset = checked(sectionBase + symbol.Offset);
                    }

                    symbols.Add(new RVObjectSymbol(
                        name,
                        symbol.SectionName,
                        offset,
                        symbol.Size,
                        symbol.Binding,
                        symbol.Kind,
                        symbol.IsTentative));
                }
            }

            symbols.Add(new RVObjectSymbol(
                ".text",
                ".text",
                0,
                textOffset,
                RVObjectSymbolBinding.Local,
                RVObjectSymbolKind.Section));

            var dataSections = ImmutableArray.CreateBuilder<RVDataSection>(sectionOrder.Count);
            for (int i = 0; i < sectionOrder.Count; i++)
            {
                var builder = sectionBuilders[sectionOrder[i]];
                var section = builder.ToSection();
                dataSections.Add(section);
                symbols.Add(new RVObjectSymbol(
                    section.Name,
                    section.Name,
                    0,
                    section.Kind == RVObjectSectionKind.Bss ? section.BssSize : section.Data.Length,
                    RVObjectSymbolBinding.Local,
                    RVObjectSymbolKind.Section));
            }

            return new RiscVProgram(
                primary.Target,
                new RVTextSection(instructions.ToImmutable(), labels, textRelocations.ToImmutable()),
                dataSections.ToImmutable(),
                symbols.ToImmutable(),
                Rename(renames[0], primary.EntrySymbol));
        }

        private static GlobalDefinitions ResolveGlobalDefinitions(IReadOnlyList<RiscVProgram> inputs)
        {
            var candidates = new Dictionary<string, List<GlobalDefinitionCandidate>>(StringComparer.Ordinal);
            var externalKinds = new Dictionary<string, RVObjectSymbolKind>(StringComparer.Ordinal);
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var symbols = inputs[inputIndex].Symbols;
                for (var symbolIndex = 0; symbolIndex < symbols.Length; symbolIndex++)
                {
                    var symbol = symbols[symbolIndex];
                    if (symbol.Kind == RVObjectSymbolKind.Section || string.IsNullOrEmpty(symbol.Name))
                        continue;

                    if (symbol.Binding == RVObjectSymbolBinding.External)
                    {
                        if (externalKinds.TryGetValue(symbol.Name, out var externalKind) && externalKind != symbol.Kind)
                            throw new InvalidOperationException($"Conflicting external symbol kinds for '{symbol.Name}'.");
                        externalKinds[symbol.Name] = symbol.Kind;
                        continue;
                    }

                    if (symbol.Binding != RVObjectSymbolBinding.Global)
                        continue;

                    if (symbol.IsTentative && symbol.Kind != RVObjectSymbolKind.Object)
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
                        throw new InvalidOperationException($"Conflicting RISC-V symbol kinds for '{pair.Key}'.");
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
            public RVObjectSymbol Symbol { get; }

            public GlobalDefinitionCandidate(int inputIndex, int symbolIndex, RVObjectSymbol symbol)
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

        private static Dictionary<string, string>[] BuildLocalRenameMaps(IReadOnlyList<RiscVProgram> inputs)
        {
            var result = new Dictionary<string, string>[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var globalNames = new HashSet<string>(
                    input.Symbols
                        .Where(static symbol => symbol.Binding == RVObjectSymbolBinding.Global && symbol.Kind != RVObjectSymbolKind.Section)
                        .Select(static symbol => symbol.Name),
                    StringComparer.Ordinal);
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                string prefix = $".Lobj{i}_";

                void AddLocal(string name)
                {
                    if (map.ContainsKey(name))
                        return;

                    string baseName = prefix + SanitizeLocalName(name);
                    string candidate = baseName;
                    int suffix = 0;
                    while (!usedNames.Add(candidate))
                        candidate = $"{baseName}_{(++suffix)}";
                    map.Add(name, candidate);
                }

                foreach (var pair in input.Text.Labels)
                {
                    if (!globalNames.Contains(pair.Key))
                        AddLocal(pair.Key);
                }

                for (int s = 0; s < input.Symbols.Length; s++)
                {
                    var symbol = input.Symbols[s];
                    if (symbol.Binding == RVObjectSymbolBinding.Local && symbol.Kind != RVObjectSymbolKind.Section)
                        AddLocal(symbol.Name);
                }

                result[i] = map;
            }
            return result;
        }

        private static string Rename(IReadOnlyDictionary<string, string> renames, string name)
            => renames.TryGetValue(name, out var renamed) ? renamed : name;

        private static string SanitizeLocalName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "symbol";

            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c is not '_' and not '$')
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static void ValidateTargetCompatibility(RVTarget primary, RVTarget other)
        {
            if (primary.XLen != other.XLen ||
                primary.Abi != other.Abi ||
                primary.Isa != other.Isa ||
                primary.Endianness != other.Endianness ||
                primary.OperatingSystem != other.OperatingSystem)
            {
                throw new InvalidOperationException("Cannot compose ABI-incompatible RISC-V objects.");
            }
        }

        private sealed class ComposedSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly ImmutableArray<RVObjectRelocation>.Builder _relocations = ImmutableArray.CreateBuilder<RVObjectRelocation>();
            private int _bssSize;

            public string Name { get; }
            public RVObjectSectionKind Kind { get; }
            public int Alignment { get; private set; } = 1;

            public ComposedSectionBuilder(string name, RVObjectSectionKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public int Append(RVDataSection section, IReadOnlyDictionary<string, string> renames)
            {
                Alignment = Math.Max(Alignment, section.Alignment);
                int offset;
                if (Kind == RVObjectSectionKind.Bss)
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

                for (int i = 0; i < section.Relocations.Length; i++)
                {
                    var relocation = section.Relocations[i];
                    _relocations.Add(new RVObjectRelocation(
                        Name,
                        checked(offset + relocation.Offset),
                        Rename(renames, relocation.SymbolName),
                        relocation.Addend,
                        relocation.Kind));
                }

                return offset;
            }
            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }
            public RVDataSection ToSection()
                => new RVDataSection(
                    Name,
                    Kind,
                    Alignment,
                    _data.ToImmutableArray(),
                    _bssSize,
                    _relocations.ToImmutable());
        }
    }

}
