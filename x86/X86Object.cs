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
                    throw new NotSupportedException("Unsupported x86 relocation: " + relocation.Kind);
            }
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
        {
            if (symbols.TryGetValue(symbol, out var value))
                return value;
            throw new KeyNotFoundException("Undefined x86 symbol: " + symbol);
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
}
