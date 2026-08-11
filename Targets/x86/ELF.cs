using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.X86
{
    internal static class X86ElfExecutableWriter
    {
        private const int PageAlignment = 0x1000;
        private const ushort EtExec = 2;
        private const uint EvCurrent = 1;
        private const uint PtLoad = 1;
        private const uint PfX = 1;
        private const uint PfW = 2;
        private const uint PfR = 4;
        private const ushort Em386 = 3;
        private const ushort EmX86_64 = 62;

        public static ulong DefaultImageBase(X86Target target)
            => target.Is64Bit ? 0x400000UL : 0x08048000UL;

        public static byte[] WriteExecutable(X86Program obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            if (obj.Target.OperatingSystem != OperatingSystemKind.Linux)
                throw new ArgumentException("ELF executable writer requires a Linux x86 target.", nameof(obj));
            if (imageBase == 0)
                imageBase = DefaultImageBase(obj.Target);
            if (imageBase % PageAlignment != 0)
                throw new ArgumentException("ELF image base must be page-aligned.", nameof(imageBase));

            var textSize = ComputeTextSize(obj.Text, obj.Target);
            var sections = CreateSectionLayouts(obj, textSize);
            var segments = LayoutSections(sections, obj.Target, imageBase);
            var sectionMap = sections.ToDictionary(static s => s.Name, StringComparer.Ordinal);
            var symbols = BuildSymbolAddressMap(obj, sectionMap, externalSymbols);
            var entryAddress = string.IsNullOrEmpty(obj.EntrySymbol)
                ? sectionMap[".text"].Address
                : ResolveSymbol(symbols, obj.EntrySymbol);
            var fileSize = sections.Count == 0 ? HeaderSize(obj.Target, 1) : sections.Max(static s => s.FileOffset + s.FileSize);
            if (fileSize < HeaderSize(obj.Target, segments.Count))
                fileSize = HeaderSize(obj.Target, segments.Count);
            var image = new byte[fileSize];

            WriteHeaders(image, obj.Target, entryAddress, segments);
            EncodeText(obj, sectionMap[".text"], symbols, image);
            ApplyTextRelocations(obj, sectionMap[".text"], symbols, image);
            CopyDataSections(sections, image);
            ApplyDataRelocations(obj, sectionMap, symbols, image);

            return image;
        }

        private static List<ElfSectionLayout> CreateSectionLayouts(X86Program obj, int textSize)
        {
            var referencedDataSections = new HashSet<string>(
                obj.Symbols
                    .Where(static s => s.Binding != X86ObjectSymbolBinding.External && s.Kind != X86ObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
                    .Select(static s => s.SectionName),
                StringComparer.Ordinal);

            var sections = new List<ElfSectionLayout>
            {
                new ElfSectionLayout(".text", X86ObjectSectionKind.Text, Math.Max(1, obj.Target.Is64Bit ? 16 : 4), new byte[textSize], textSize)
            };

            foreach (var section in obj.DataSections)
            {
                var memorySize = section.Kind == X86ObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
                if (memorySize == 0 && section.Relocations.Length == 0 && !referencedDataSections.Contains(section.Name))
                    continue;
                var raw = section.Kind == X86ObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray();
                sections.Add(new ElfSectionLayout(section.Name, section.Kind, section.Alignment, raw, memorySize));
            }

            return sections;
        }

        private static List<ElfSegmentLayout> LayoutSections(IReadOnlyList<ElfSectionLayout> sections, X86Target target, ulong imageBase)
        {
            var segments = new List<ElfSegmentLayout>();
            var headerSize = HeaderSize(target, SegmentCount(sections));
            var cursor = AlignUp(headerSize, sections[0].Alignment);

            var text = sections[0];
            cursor = LayoutOne(text, cursor, imageBase);
            segments.Add(new ElfSegmentLayout(0, imageBase, cursor, cursor, PfR | PfX, PageAlignment));

            var rodata = sections.Where(static s => s.Kind == X86ObjectSectionKind.Rodata).ToArray();
            if (rodata.Length != 0)
            {
                var start = AlignUp(cursor, PageAlignment);
                cursor = start;
                foreach (var section in rodata)
                    cursor = LayoutOne(section, cursor, imageBase);
                segments.Add(new ElfSegmentLayout(start, checked(imageBase + (ulong)start), cursor - start, cursor - start, PfR, PageAlignment));
            }

            var writable = sections.Where(static s => s.Kind is X86ObjectSectionKind.Data or X86ObjectSectionKind.Bss).ToArray();
            if (writable.Length != 0)
            {
                var start = AlignUp(cursor, PageAlignment);
                cursor = start;
                var fileEnd = start;
                foreach (var section in writable.Where(static s => s.Kind != X86ObjectSectionKind.Bss))
                {
                    cursor = LayoutOne(section, cursor, imageBase);
                    fileEnd = Math.Max(fileEnd, cursor);
                }
                foreach (var section in writable.Where(static s => s.Kind == X86ObjectSectionKind.Bss))
                    cursor = LayoutOne(section, cursor, imageBase);
                segments.Add(new ElfSegmentLayout(start, checked(imageBase + (ulong)start), fileEnd - start, cursor - start, PfR | PfW, PageAlignment));
            }

            return segments;
        }

        private static int LayoutOne(ElfSectionLayout section, int cursor, ulong imageBase)
        {
            cursor = AlignUp(cursor, section.Alignment);
            section.FileOffset = cursor;
            section.Address = checked(imageBase + (ulong)cursor);
            return checked(cursor + section.MemorySize);
        }

        private static int SegmentCount(IReadOnlyList<ElfSectionLayout> sections)
        {
            var count = 1;
            if (sections.Any(static s => s.Kind == X86ObjectSectionKind.Rodata))
                count++;
            if (sections.Any(static s => s.Kind is X86ObjectSectionKind.Data or X86ObjectSectionKind.Bss))
                count++;
            return count;
        }

        private static int HeaderSize(X86Target target, int segmentCount)
        {
            var ehSize = target.Is64Bit ? 64 : 52;
            var phSize = target.Is64Bit ? 56 : 32;
            return checked(ehSize + phSize * segmentCount);
        }

        private static int ComputeTextSize(X86TextSection text, X86Target target)
        {
            var size = 0;
            foreach (var instruction in text.Instructions)
                size = checked(size + X86CodeEncoder.GetEncodedLength(instruction, target));
            return size;
        }

        private static Dictionary<string, ulong> BuildSymbolAddressMap(
            X86Program obj,
            IReadOnlyDictionary<string, ElfSectionLayout> sections,
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
                {
                    if (symbol.Kind == X86ObjectSymbolKind.Section && symbol.Size == 0)
                        continue;
                    throw new InvalidOperationException($"Symbol section does not exist: {symbol.SectionName}");
                }
                result[symbol.Name] = checked(section.Address + (ulong)symbol.Offset);
            }

            if (externalSymbols is not null)
            {
                foreach (var pair in externalSymbols)
                    result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static void EncodeText(X86Program obj, ElfSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
        {
            var offset = 0;
            foreach (var instruction in obj.Text.Instructions)
            {
                var encoded = X86CodeEncoder.Encode(instruction, obj.Target, checked(text.Address + (ulong)offset), symbols);
                Array.Copy(encoded, 0, image, text.FileOffset + offset, encoded.Length);
                offset = checked(offset + encoded.Length);
            }
        }

        private static void ApplyTextRelocations(X86Program obj, ElfSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
        {
            foreach (var relocation in obj.Text.Relocations)
                ApplyRelocation(image, text.FileOffset + relocation.Offset, text.Address, relocation.Offset, relocation, symbols, obj.Target);
        }

        private static void CopyDataSections(IEnumerable<ElfSectionLayout> sections, byte[] image)
        {
            foreach (var section in sections)
            {
                if (section.Kind == X86ObjectSectionKind.Text || section.Kind == X86ObjectSectionKind.Bss || section.FileSize == 0)
                    continue;
                Array.Copy(section.RawData, 0, image, section.FileOffset, section.RawData.Length);
            }
        }

        private static void ApplyDataRelocations(
            X86Program obj,
            IReadOnlyDictionary<string, ElfSectionLayout> sections,
            IReadOnlyDictionary<string, ulong> symbols,
            byte[] image)
        {
            foreach (var section in obj.DataSections)
            {
                if (section.Relocations.Length == 0)
                    continue;
                if (!sections.TryGetValue(section.Name, out var layout))
                    continue;
                if (section.Kind == X86ObjectSectionKind.Bss)
                    throw new InvalidOperationException("BSS relocations cannot be represented in an ELF executable without runtime relocations.");
                foreach (var relocation in section.Relocations)
                    ApplyRelocation(image, layout.FileOffset + relocation.Offset, layout.Address, relocation.Offset, relocation, symbols, obj.Target);
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

        private static void WriteHeaders(byte[] image, X86Target target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            image[0] = 0x7f;
            image[1] = (byte)'E';
            image[2] = (byte)'L';
            image[3] = (byte)'F';
            image[4] = target.Is64Bit ? (byte)2 : (byte)1;
            image[5] = 1;
            image[6] = 1;

            if (target.Is64Bit)
                WriteElf64Header(image, target, entryAddress, segments);
            else
                WriteElf32Header(image, target, entryAddress, segments);
        }

        private static void WriteElf64Header(byte[] image, X86Target target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            WriteUInt16(image, 16, EtExec);
            WriteUInt16(image, 18, target.Is64Bit ? EmX86_64 : Em386);
            WriteUInt32(image, 20, EvCurrent);
            WriteUInt64(image, 24, entryAddress);
            WriteUInt64(image, 32, 64);
            WriteUInt64(image, 40, 0);
            WriteUInt32(image, 48, 0);
            WriteUInt16(image, 52, 64);
            WriteUInt16(image, 54, 56);
            WriteUInt16(image, 56, checked((ushort)segments.Count));
            WriteUInt16(image, 58, 0);
            WriteUInt16(image, 60, 0);
            WriteUInt16(image, 62, 0);

            var offset = 64;
            foreach (var segment in segments)
            {
                WriteUInt32(image, offset + 0, PtLoad);
                WriteUInt32(image, offset + 4, segment.Flags);
                WriteUInt64(image, offset + 8, checked((ulong)segment.FileOffset));
                WriteUInt64(image, offset + 16, segment.Address);
                WriteUInt64(image, offset + 24, segment.Address);
                WriteUInt64(image, offset + 32, checked((ulong)segment.FileSize));
                WriteUInt64(image, offset + 40, checked((ulong)segment.MemorySize));
                WriteUInt64(image, offset + 48, checked((ulong)segment.Alignment));
                offset += 56;
            }
        }

        private static void WriteElf32Header(byte[] image, X86Target target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            WriteUInt16(image, 16, EtExec);
            WriteUInt16(image, 18, target.Is64Bit ? EmX86_64 : Em386);
            WriteUInt32(image, 20, EvCurrent);
            WriteUInt32(image, 24, checked((uint)entryAddress));
            WriteUInt32(image, 28, 52);
            WriteUInt32(image, 32, 0);
            WriteUInt32(image, 36, 0);
            WriteUInt16(image, 40, 52);
            WriteUInt16(image, 42, 32);
            WriteUInt16(image, 44, checked((ushort)segments.Count));
            WriteUInt16(image, 46, 0);
            WriteUInt16(image, 48, 0);
            WriteUInt16(image, 50, 0);

            var offset = 52;
            foreach (var segment in segments)
            {
                WriteUInt32(image, offset + 0, PtLoad);
                WriteUInt32(image, offset + 4, checked((uint)segment.FileOffset));
                WriteUInt32(image, offset + 8, checked((uint)segment.Address));
                WriteUInt32(image, offset + 12, checked((uint)segment.Address));
                WriteUInt32(image, offset + 16, checked((uint)segment.FileSize));
                WriteUInt32(image, offset + 20, checked((uint)segment.MemorySize));
                WriteUInt32(image, offset + 24, segment.Flags);
                WriteUInt32(image, offset + 28, checked((uint)segment.Alignment));
                offset += 32;
            }
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
        {
            if (symbols.TryGetValue(symbol, out var value))
                return value;
            throw new KeyNotFoundException($"Undefined x86 symbol: {symbol}");
        }

        private static int AlignUp(int value, int alignment)
        {
            alignment = Math.Max(1, alignment);
            var mask = alignment - 1;
            return checked((value + mask) & ~mask);
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

        private static void WriteUInt16(byte[] image, int offset, ushort value)
            => WriteUnsigned(image, offset, value, 2);

        private static void WriteUInt32(byte[] image, int offset, uint value)
            => WriteUnsigned(image, offset, value, 4);

        private static void WriteUInt64(byte[] image, int offset, ulong value)
            => WriteUnsigned(image, offset, value, 8);

        private static void WriteUnsigned(byte[] image, int offset, ulong value, int size)
        {
            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> (i * 8));
        }

        private sealed class ElfSectionLayout
        {
            public string Name { get; }
            public X86ObjectSectionKind Kind { get; }
            public int Alignment { get; }
            public byte[] RawData { get; }
            public int MemorySize { get; }
            public int FileOffset { get; set; }
            public int FileSize => RawData.Length;
            public ulong Address { get; set; }

            public ElfSectionLayout(string name, X86ObjectSectionKind kind, int alignment, byte[] rawData, int memorySize)
            {
                Name = name ?? string.Empty;
                Kind = kind;
                Alignment = Math.Max(1, alignment);
                RawData = rawData ?? Array.Empty<byte>();
                MemorySize = Math.Max(memorySize, RawData.Length);
            }
        }

        private sealed class ElfSegmentLayout
        {
            public int FileOffset { get; }
            public ulong Address { get; }
            public int FileSize { get; }
            public int MemorySize { get; }
            public uint Flags { get; }
            public int Alignment { get; }

            public ElfSegmentLayout(int fileOffset, ulong address, int fileSize, int memorySize, uint flags, int alignment)
            {
                FileOffset = Math.Max(0, fileOffset);
                Address = address;
                FileSize = Math.Max(0, fileSize);
                MemorySize = Math.Max(FileSize, memorySize);
                Flags = flags;
                Alignment = Math.Max(1, alignment);
            }
        }
    }
}
