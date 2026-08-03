using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.X86
{
    internal static class X86ObjectLinker
    {
        public static X86LinkedImage LinkFlat(
            X86Program obj,
            ulong imageBase = 0,
            IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            var textSize = ComputeTextSize(obj.Text, obj.Target);
            var sections = LayoutSections(obj, textSize, imageBase);
            var symbols = BuildSymbolAddressMap(obj, sections, externalSymbols);
            var size = sections.Values.Count == 0 ? 0 : sections.Values.Max(static s => s.Offset + s.Size);
            var image = new byte[size];

            EncodeText(obj, sections, symbols, image);
            ApplyTextRelocations(obj, sections, symbols, image);
            CopyDataSections(obj, sections, image);
            ApplyDataRelocations(obj, sections, symbols, image);

            var entryAddress = string.IsNullOrEmpty(obj.EntrySymbol) ? imageBase : ResolveSymbol(symbols, obj.EntrySymbol);
            var entryOffset = checked((int)(entryAddress - imageBase));
            return new X86LinkedImage(obj, imageBase, entryAddress, entryOffset, sections, symbols, image);
        }

        private static int ComputeTextSize(X86TextSection text, X86Target target)
        {
            var size = 0;
            foreach (var instruction in text.Instructions)
                size = checked(size + X86CodeEncoder.GetEncodedLength(instruction, target));
            return size;
        }

        private static Dictionary<string, X86LinkedSection> LayoutSections(X86Program obj, int textSize, ulong imageBase)
        {
            var result = new Dictionary<string, X86LinkedSection>(StringComparer.Ordinal);
            var offset = 0;
            AddSection(result, textSize, ".text", X86ObjectSectionKind.Text, Math.Max(1, obj.Target.Is64Bit ? 16 : 4), imageBase, ref offset);
            foreach (var section in obj.DataSections)
            {
                var size = section.Kind == X86ObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
                AddSection(result, size, section.Name, section.Kind, section.Alignment, imageBase, ref offset);
            }
            return result;
        }

        private static void AddSection(
            Dictionary<string, X86LinkedSection> sections,
            int size,
            string name,
            X86ObjectSectionKind kind,
            int alignment,
            ulong imageBase,
            ref int offset)
        {
            alignment = Math.Max(1, alignment);
            offset = AlignUp(offset, alignment);
            sections[name] = new X86LinkedSection(name, kind, offset, Math.Max(0, size), alignment, checked(imageBase + (ulong)offset));
            offset = checked(offset + Math.Max(0, size));
        }

        private static Dictionary<string, ulong> BuildSymbolAddressMap(
            X86Program obj,
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            IReadOnlyDictionary<string, ulong>? externalSymbols)
        {
            var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var text = sections[".text"];
            foreach (var label in obj.Text.Labels)
                result[label.Key] = checked(text.Address + (ulong)label.Value);

            foreach (var symbol in obj.Symbols)
            {
                if (symbol.Binding == X86ObjectSymbolBinding.External)
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
            X86Program obj,
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            var text = sections[".text"];
            var offset = 0;
            foreach (var instruction in obj.Text.Instructions)
            {
                var encoded = X86CodeEncoder.Encode(instruction, obj.Target, checked(text.Address + (ulong)offset), symbols);
                for (var i = 0; i < encoded.Length; i++)
                    image[text.Offset + offset + i] = encoded[i];
                offset = checked(offset + encoded.Length);
            }
        }

        private static void ApplyTextRelocations(
            X86Program obj,
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            if (obj.Text.Relocations.IsDefaultOrEmpty)
                return;

            var text = sections[".text"];
            foreach (var relocation in obj.Text.Relocations)
                ApplyRelocation(image, checked(text.Offset + relocation.Offset), text.Address, relocation.Offset, relocation, symbols, obj.Target);
        }

        private static void CopyDataSections(
            X86Program obj,
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            byte[] image)
        {
            foreach (var section in obj.DataSections)
            {
                if (section.Kind == X86ObjectSectionKind.Bss)
                    continue;
                var layout = sections[section.Name];
                for (var i = 0; i < section.Data.Length; i++)
                    image[layout.Offset + i] = section.Data[i];
            }
        }

        private static void ApplyDataRelocations(
            X86Program obj,
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            foreach (var section in obj.DataSections)
            {
                if (section.Relocations.IsDefaultOrEmpty)
                    continue;
                if (section.Kind == X86ObjectSectionKind.Bss)
                    throw new InvalidOperationException("BSS relocations cannot be represented in a flat loaded image.");

                var layout = sections[section.Name];
                foreach (var relocation in section.Relocations)
                    ApplyRelocation(image, checked(layout.Offset + relocation.Offset), layout.Address, relocation.Offset, relocation, symbols, obj.Target);
            }
        }

        private static void ApplyRelocation(
            byte[] image,
            int imageOffset,
            ulong sectionAddress,
            int sectionOffset,
            X86ObjectRelocation relocation,
            IReadOnlyDictionary<string, ulong> symbols,
            X86Target target)
        {
            var symbolAddress = ResolveSymbol(symbols, relocation.SymbolName);
            var value = checked((long)symbolAddress + relocation.Addend);
            switch (relocation.Kind)
            {
                case X86ObjectRelocationKind.Relative8:
                    WriteSigned(image, imageOffset, checked(value - (long)(sectionAddress + (ulong)sectionOffset + 1)), 1);
                    break;
                case X86ObjectRelocationKind.Relative32:
                    WriteSigned(image, imageOffset, checked(value - (long)(sectionAddress + (ulong)sectionOffset + 4)), 4);
                    break;
                case X86ObjectRelocationKind.RipRelative32:
                    WriteSigned(image, imageOffset, checked(value - (long)(sectionAddress + (ulong)sectionOffset + 4)), 4);
                    break;
                case X86ObjectRelocationKind.AbsolutePointer:
                    WriteUnsigned(image, imageOffset, checked((ulong)value), target.Is32Bit ? 4 : 8);
                    break;
                case X86ObjectRelocationKind.Absolute32:
                    WriteUnsigned(image, imageOffset, checked((ulong)value), 4);
                    break;
                case X86ObjectRelocationKind.Absolute64:
                    WriteUnsigned(image, imageOffset, checked((ulong)value), 8);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported relocation: {relocation.Kind}");
            }
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
        {
            if (symbols.TryGetValue(symbol, out var value))
                return value;
            throw new KeyNotFoundException($"Undefined symbol: {symbol}");
        }

        private static int AlignUp(int value, int alignment)
        {
            var mask = alignment - 1;
            return (value + mask) & ~mask;
        }

        private static void WriteSigned(byte[] image, int offset, long value, int size)
        {
            switch (size)
            {
                case 1:
                    if (value < sbyte.MinValue || value > sbyte.MaxValue)
                        throw new OverflowException("x86 relocation does not fit in 8 bits.");
                    image[offset] = unchecked((byte)(sbyte)value);
                    break;
                case 4:
                    if (value < int.MinValue || value > int.MaxValue)
                        throw new OverflowException("x86 relocation does not fit in 32 bits.");
                    WriteUnsigned(image, offset, unchecked((uint)(int)value), 4);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        private static void WriteUnsigned(byte[] image, int offset, ulong value, int size)
        {
            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> (i * 8));
        }
    }

    public static class X86ObjectComposer
    {
        public static X86Program Compose(X86Program primary, params X86Program[] libraries)
        {
            if (primary is null)
                throw new ArgumentNullException(nameof(primary));

            libraries ??= Array.Empty<X86Program>();
            var inputs = new X86Program[libraries.Length + 1];
            inputs[0] = primary;
            for (int i = 0; i < libraries.Length; i++)
            {
                inputs[i + 1] = libraries[i] ?? throw new ArgumentNullException(nameof(libraries));
                ValidateTargetCompatibility(primary.Target, inputs[i + 1].Target);
            }

            var globalDefinitions = ResolveGlobalDefinitions(inputs);
            var renames = BuildLocalRenameMaps(inputs);
            var textBases = new int[inputs.Length];
            var instructions = ImmutableArray.CreateBuilder<X86Instruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var textRelocations = ImmutableArray.CreateBuilder<X86ObjectRelocation>();

            int textOffset = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                textBases[i] = textOffset;
                for (int instructionIndex = 0; instructionIndex < input.Text.Instructions.Length; instructionIndex++)
                {
                    var instruction = input.Text.Instructions[instructionIndex];
                    instruction = RewriteInstruction(instruction, renames[i]);
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
                    textRelocations.Add(new X86ObjectRelocation(
                        ".text",
                        checked(textOffset + relocation.Offset),
                        Rename(renames[i], relocation.SymbolName),
                        relocation.Addend,
                        relocation.Kind));
                }

                textOffset = checked(textOffset + ComputeTextSize(input));
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
                        throw new InvalidOperationException($"x86 sections with the same name have different kinds: {section.Name}");
                    }

                    int sectionBase = builder.Append(section, renames[i]);
                    sectionBases.Add((i, section.Name), sectionBase);
                }
            }

            var symbols = ImmutableArray.CreateBuilder<X86ObjectSymbol>();
            var emittedExternalSymbols = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                for (int s = 0; s < input.Symbols.Length; s++)
                {
                    var symbol = input.Symbols[s];
                    if (symbol.Kind == X86ObjectSymbolKind.Section)
                        continue;

                    string name = Rename(renames[i], symbol.Name);
                    if (symbol.Binding == X86ObjectSymbolBinding.External)
                    {
                        if (!globalDefinitions.Contains(name) && emittedExternalSymbols.Add(name))
                        {
                            symbols.Add(new X86ObjectSymbol(
                                name,
                                string.Empty,
                                0,
                                0,
                                X86ObjectSymbolBinding.External,
                                symbol.Kind));
                        }
                        continue;
                    }

                    if (symbol.Binding == X86ObjectSymbolBinding.Global &&
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
                            throw new InvalidOperationException($"Symbol section is missing from composed x86 object: {symbol.SectionName}");
                        offset = checked(sectionBase + symbol.Offset);
                    }

                    symbols.Add(new X86ObjectSymbol(
                        name,
                        symbol.SectionName,
                        offset,
                        symbol.Size,
                        symbol.Binding,
                        symbol.Kind,
                        symbol.IsTentative));
                }
            }

            symbols.Add(new X86ObjectSymbol(
                ".text",
                ".text",
                0,
                textOffset,
                X86ObjectSymbolBinding.Local,
                X86ObjectSymbolKind.Section));

            var dataSections = ImmutableArray.CreateBuilder<X86DataSection>(sectionOrder.Count);
            for (int i = 0; i < sectionOrder.Count; i++)
            {
                var builder = sectionBuilders[sectionOrder[i]];
                var section = builder.ToSection();
                dataSections.Add(section);
                symbols.Add(new X86ObjectSymbol(
                    section.Name,
                    section.Name,
                    0,
                    section.Kind == X86ObjectSectionKind.Bss ? section.BssSize : section.Data.Length,
                    X86ObjectSymbolBinding.Local,
                    X86ObjectSymbolKind.Section));
            }

            return new X86Program(
                primary.Target,
                new X86TextSection(instructions.ToImmutable(), labels, textRelocations.ToImmutable()),
                dataSections.ToImmutable(),
                symbols.ToImmutable(),
                Rename(renames[0], primary.EntrySymbol));
        }

        private static int ComputeTextSize(X86Program program)
        {
            int size = 0;
            for (int i = 0; i < program.Text.Instructions.Length; i++)
                size = checked(size + X86CodeEncoder.GetEncodedLength(program.Text.Instructions[i], program.Target));
            return size;
        }

        private static X86Instruction RewriteInstruction(X86Instruction instruction, IReadOnlyDictionary<string, string> renames)
            => instruction
                .WithOperand0(RewriteOperand(instruction.Operand0, renames))
                .WithOperand1(RewriteOperand(instruction.Operand1, renames))
                .WithOperand2(RewriteOperand(instruction.Operand2, renames));

        private static X86Operand RewriteOperand(X86Operand operand, IReadOnlyDictionary<string, string> renames)
            => operand.Symbol is not null
                ? operand.WithSymbol(Rename(renames, operand.Symbol), operand.RelocationKind, operand.Addend)
                : operand;

        private static GlobalDefinitions ResolveGlobalDefinitions(IReadOnlyList<X86Program> inputs)
        {
            var candidates = new Dictionary<string, List<GlobalDefinitionCandidate>>(StringComparer.Ordinal);
            var externalKinds = new Dictionary<string, X86ObjectSymbolKind>(StringComparer.Ordinal);
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var symbols = inputs[inputIndex].Symbols;
                for (var symbolIndex = 0; symbolIndex < symbols.Length; symbolIndex++)
                {
                    var symbol = symbols[symbolIndex];
                    if (symbol.Kind == X86ObjectSymbolKind.Section || string.IsNullOrEmpty(symbol.Name))
                        continue;

                    if (symbol.Binding == X86ObjectSymbolBinding.External)
                    {
                        if (externalKinds.TryGetValue(symbol.Name, out var externalKind) && externalKind != symbol.Kind)
                            throw new InvalidOperationException($"Conflicting external symbol kinds for '{symbol.Name}'.");
                        externalKinds[symbol.Name] = symbol.Kind;
                        continue;
                    }

                    if (symbol.Binding != X86ObjectSymbolBinding.Global)
                        continue;

                    if (symbol.IsTentative && symbol.Kind != X86ObjectSymbolKind.Object)
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
                        throw new InvalidOperationException($"Conflicting x86 symbol kinds for '{pair.Key}'.");
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
            public X86ObjectSymbol Symbol { get; }

            public GlobalDefinitionCandidate(int inputIndex, int symbolIndex, X86ObjectSymbol symbol)
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

        private static Dictionary<string, string>[] BuildLocalRenameMaps(IReadOnlyList<X86Program> inputs)
        {
            var result = new Dictionary<string, string>[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var globalNames = new HashSet<string>(
                    input.Symbols
                        .Where(static symbol => symbol.Binding == X86ObjectSymbolBinding.Global && symbol.Kind != X86ObjectSymbolKind.Section)
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
                    if (symbol.Binding == X86ObjectSymbolBinding.Local && symbol.Kind != X86ObjectSymbolKind.Section)
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

        private static void ValidateTargetCompatibility(X86Target primary, X86Target other)
        {
            if (primary.XLen != other.XLen ||
                primary.Abi != other.Abi ||
                primary.Isa != other.Isa ||
                primary.Endianness != other.Endianness ||
                primary.OperatingSystem != other.OperatingSystem)
            {
                throw new InvalidOperationException("Cannot compose ABI-incompatible x86 objects.");
            }
        }

        private sealed class ComposedSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly ImmutableArray<X86ObjectRelocation>.Builder _relocations = ImmutableArray.CreateBuilder<X86ObjectRelocation>();
            private int _bssSize;

            public string Name { get; }
            public X86ObjectSectionKind Kind { get; }
            public int Alignment { get; private set; } = 1;

            public ComposedSectionBuilder(string name, X86ObjectSectionKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public int Append(X86DataSection section, IReadOnlyDictionary<string, string> renames)
            {
                Alignment = Math.Max(Alignment, section.Alignment);
                int offset;
                if (Kind == X86ObjectSectionKind.Bss)
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
                    _relocations.Add(new X86ObjectRelocation(
                        Name,
                        checked(offset + relocation.Offset),
                        Rename(renames, relocation.SymbolName),
                        relocation.Addend,
                        relocation.Kind));
                }

                return offset;
            }

            public X86DataSection ToSection()
                => new X86DataSection(
                    Name,
                    Kind,
                    Alignment,
                    _data.ToImmutableArray(),
                    _bssSize,
                    _relocations.ToImmutable());
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }

}
