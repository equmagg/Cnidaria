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
            AddSection(result, obj.Text.Instructions.Length * 4, ".text", RVObjectSectionKind.Text, 4, imageBase, ref offset);
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
                    throw new InvalidOperationException("Symbol section does not exist: " + symbol.SectionName);
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
            for (var i = 0; i < obj.Text.Instructions.Length; i++)
            {
                var sectionOffset = checked(i * 4);
                var instruction = obj.Text.Instructions[i];
                if (relocations.TryGetValue(sectionOffset, out var relocation))
                    instruction = ResolveRelocatedInstruction(obj, instruction, relocation, sectionOffset, relocations, text.Address, symbols);
                else if (instruction.HasSymbol)
                    instruction = ResolveSymbolicInstruction(instruction, checked(text.Address + (ulong)sectionOffset), symbols);

                var encoded = RiscVCodeEncoder.Encode(instruction, obj.Target);
                WriteUInt32(image, checked(text.Offset + sectionOffset), encoded, obj.Target.Endianness);
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
                    throw new NotSupportedException("Unsupported text relocation: " + relocation.Kind);
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
                    return instruction.WithImmediate(PcrelLo12((long)value, (long)pc));
                default:
                    throw new NotSupportedException("Unsupported symbolic instruction relocation: " + instruction.RelocationKind);
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
                            throw new NotSupportedException("Unsupported data relocation: " + relocation.Kind);
                    }
                }
            }
        }

        private static int PcrelHi20(long value, long pc)
        {
            var delta = checked(value - pc);
            return checked((int)((delta + 0x800L) & ~0xFFFL));
        }

        private static int PcrelLo12(long value, long pc)
        {
            var hi = PcrelHi20(value, pc);
            return checked((int)(value - pc - hi));
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
        {
            if (symbols.TryGetValue(symbol, out var address))
                return address;
            throw new InvalidOperationException("Unresolved RISC-V symbol: " + symbol);
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
}
