using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.RiscV
{
    internal static class RiscVElfExecutableWriter
    {
        private const int PageAlignment = 0x1000;
        private const ushort EtExec = 2;
        private const uint EvCurrent = 1;
        private const uint PtLoad = 1;
        private const uint PfX = 1;
        private const uint PfW = 2;
        private const uint PfR = 4;
        private const ushort EmRiscV = 243;
        private const uint EfRiscVRvc = 0x0001;
        private const uint EfRiscVFloatAbiSingle = 0x0002;
        private const uint EfRiscVFloatAbiDouble = 0x0004;

        public static ulong DefaultImageBase(RVTarget target)
            => target.Is64Bit ? 0x10000UL : 0x10000UL;

        public static byte[] WriteExecutable(RiscVProgram obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            if (obj.Target.OperatingSystem != OperatingSystemKind.Linux)
                throw new ArgumentException("ELF executable writer requires a Linux RISC-V target.", nameof(obj));
            if (imageBase == 0)
                imageBase = DefaultImageBase(obj.Target);
            if (imageBase % PageAlignment != 0)
                throw new ArgumentException("ELF image base must be page-aligned.", nameof(imageBase));

            var sections = CreateSectionLayouts(obj);
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
            CopyDataSections(sections, image);
            ApplyDataRelocations(obj, sectionMap, symbols, image);

            return image;
        }

        private static List<ElfSectionLayout> CreateSectionLayouts(RiscVProgram obj)
        {
            var referencedDataSections = new HashSet<string>(
                obj.Symbols
                    .Where(static s => s.Binding != RVObjectSymbolBinding.External && s.Kind != RVObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
                    .Select(static s => s.SectionName),
                StringComparer.Ordinal);

            var sections = new List<ElfSectionLayout>
            {
                new ElfSectionLayout(".text", RVObjectSectionKind.Text, 4, new byte[obj.Text.SizeInBytes], obj.Text.SizeInBytes)
            };

            foreach (var section in obj.DataSections)
            {
                var memorySize = section.Kind == RVObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
                if (memorySize == 0 && section.Relocations.Length == 0 && !referencedDataSections.Contains(section.Name))
                    continue;
                var raw = section.Kind == RVObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray();
                sections.Add(new ElfSectionLayout(section.Name, section.Kind, section.Alignment, raw, memorySize));
            }

            return sections;
        }

        private static List<ElfSegmentLayout> LayoutSections(IReadOnlyList<ElfSectionLayout> sections, RVTarget target, ulong imageBase)
        {
            var segments = new List<ElfSegmentLayout>();
            var headerSize = HeaderSize(target, SegmentCount(sections));
            var cursor = AlignUp(headerSize, sections[0].Alignment);

            var text = sections[0];
            cursor = LayoutOne(text, cursor, imageBase);
            segments.Add(new ElfSegmentLayout(0, imageBase, cursor, cursor, PfR | PfX, PageAlignment));

            var rodata = sections.Where(static s => s.Kind == RVObjectSectionKind.Rodata).ToArray();
            if (rodata.Length != 0)
            {
                var start = AlignUp(cursor, PageAlignment);
                cursor = start;
                foreach (var section in rodata)
                    cursor = LayoutOne(section, cursor, imageBase);
                segments.Add(new ElfSegmentLayout(start, checked(imageBase + (ulong)start), cursor - start, cursor - start, PfR, PageAlignment));
            }

            var writable = sections.Where(static s => s.Kind is RVObjectSectionKind.Data or RVObjectSectionKind.Bss).ToArray();
            if (writable.Length != 0)
            {
                var start = AlignUp(cursor, PageAlignment);
                cursor = start;
                var fileEnd = start;
                foreach (var section in writable.Where(static s => s.Kind != RVObjectSectionKind.Bss))
                {
                    cursor = LayoutOne(section, cursor, imageBase);
                    fileEnd = Math.Max(fileEnd, cursor);
                }
                foreach (var section in writable.Where(static s => s.Kind == RVObjectSectionKind.Bss))
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
            if (sections.Any(static s => s.Kind == RVObjectSectionKind.Rodata))
                count++;
            if (sections.Any(static s => s.Kind is RVObjectSectionKind.Data or RVObjectSectionKind.Bss))
                count++;
            return count;
        }

        private static int HeaderSize(RVTarget target, int segmentCount)
        {
            var ehSize = target.Is64Bit ? 64 : 52;
            var phSize = target.Is64Bit ? 56 : 32;
            return checked(ehSize + phSize * segmentCount);
        }

        private static Dictionary<string, ulong> BuildSymbolAddressMap(
            RiscVProgram obj,
            IReadOnlyDictionary<string, ElfSectionLayout> sections,
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
                {
                    if (symbol.Kind == RVObjectSymbolKind.Section && symbol.Size == 0)
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

        private static void EncodeText(RiscVProgram obj, ElfSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
        {
            var relocations = obj.Text.Relocations.ToDictionary(static r => r.Offset, static r => r);
            var sectionOffset = 0;
            for (var i = 0; i < obj.Text.Instructions.Length; i++)
            {
                var instruction = obj.Text.Instructions[i];
                if (relocations.TryGetValue(sectionOffset, out var relocation))
                    instruction = ResolveRelocatedInstruction(instruction, relocation, sectionOffset, relocations, text.Address, symbols);
                else if (instruction.HasSymbol)
                    instruction = ResolveSymbolicInstruction(instruction, checked(text.Address + (ulong)sectionOffset), symbols);

                var encoded = RiscVCodeEncoder.Encode(instruction, obj.Target);
                var size = RVInstructionTable.GetEncodedSize(instruction.Opcode);
                RiscVCodeEncoder.WriteInstruction(image, checked(text.FileOffset + sectionOffset), encoded, size, obj.Target.Endianness);
                sectionOffset = checked(sectionOffset + size);
            }
        }

        private static RVInstruction ResolveRelocatedInstruction(
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

        private static RVInstruction ResolveSymbolicInstruction(RVInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong> symbols)
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

        private static void CopyDataSections(IEnumerable<ElfSectionLayout> sections, byte[] image)
        {
            foreach (var section in sections)
            {
                if (section.Kind == RVObjectSectionKind.Text || section.Kind == RVObjectSectionKind.Bss || section.FileSize == 0)
                    continue;
                Array.Copy(section.RawData, 0, image, section.FileOffset, section.RawData.Length);
            }
        }

        private static void ApplyDataRelocations(
            RiscVProgram obj,
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
                if (section.Kind == RVObjectSectionKind.Bss)
                    throw new InvalidOperationException("BSS relocations cannot be represented in an ELF executable without runtime relocations.");

                foreach (var relocation in section.Relocations)
                {
                    var signedValue = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
                    if (signedValue < 0)
                        throw new OverflowException("RISC-V absolute relocation resolved to a negative address.");
                    var value = (ulong)signedValue;
                    var offset = checked(layout.FileOffset + relocation.Offset);
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

        private static void WriteHeaders(byte[] image, RVTarget target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            image[0] = 0x7f;
            image[1] = (byte)'E';
            image[2] = (byte)'L';
            image[3] = (byte)'F';
            image[4] = target.Is64Bit ? (byte)2 : (byte)1;
            image[5] = target.Endianness == TargetEndianness.Little ? (byte)1 : (byte)2;
            image[6] = 1;

            if (target.Is64Bit)
                WriteElf64Header(image, target, entryAddress, segments);
            else
                WriteElf32Header(image, target, entryAddress, segments);
        }

        private static void WriteElf64Header(byte[] image, RVTarget target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            WriteUInt16(image, 16, EtExec, target.Endianness);
            WriteUInt16(image, 18, EmRiscV, target.Endianness);
            WriteUInt32(image, 20, EvCurrent, target.Endianness);
            WriteUInt64(image, 24, entryAddress, target.Endianness);
            WriteUInt64(image, 32, 64, target.Endianness);
            WriteUInt64(image, 40, 0, target.Endianness);
            WriteUInt32(image, 48, ElfFlags(target), target.Endianness);
            WriteUInt16(image, 52, 64, target.Endianness);
            WriteUInt16(image, 54, 56, target.Endianness);
            WriteUInt16(image, 56, checked((ushort)segments.Count), target.Endianness);
            WriteUInt16(image, 58, 0, target.Endianness);
            WriteUInt16(image, 60, 0, target.Endianness);
            WriteUInt16(image, 62, 0, target.Endianness);

            var offset = 64;
            foreach (var segment in segments)
            {
                WriteUInt32(image, offset + 0, PtLoad, target.Endianness);
                WriteUInt32(image, offset + 4, segment.Flags, target.Endianness);
                WriteUInt64(image, offset + 8, checked((ulong)segment.FileOffset), target.Endianness);
                WriteUInt64(image, offset + 16, segment.Address, target.Endianness);
                WriteUInt64(image, offset + 24, segment.Address, target.Endianness);
                WriteUInt64(image, offset + 32, checked((ulong)segment.FileSize), target.Endianness);
                WriteUInt64(image, offset + 40, checked((ulong)segment.MemorySize), target.Endianness);
                WriteUInt64(image, offset + 48, checked((ulong)segment.Alignment), target.Endianness);
                offset += 56;
            }
        }

        private static void WriteElf32Header(byte[] image, RVTarget target, ulong entryAddress, IReadOnlyList<ElfSegmentLayout> segments)
        {
            WriteUInt16(image, 16, EtExec, target.Endianness);
            WriteUInt16(image, 18, EmRiscV, target.Endianness);
            WriteUInt32(image, 20, EvCurrent, target.Endianness);
            WriteUInt32(image, 24, checked((uint)entryAddress), target.Endianness);
            WriteUInt32(image, 28, 52, target.Endianness);
            WriteUInt32(image, 32, 0, target.Endianness);
            WriteUInt32(image, 36, ElfFlags(target), target.Endianness);
            WriteUInt16(image, 40, 52, target.Endianness);
            WriteUInt16(image, 42, 32, target.Endianness);
            WriteUInt16(image, 44, checked((ushort)segments.Count), target.Endianness);
            WriteUInt16(image, 46, 0, target.Endianness);
            WriteUInt16(image, 48, 0, target.Endianness);
            WriteUInt16(image, 50, 0, target.Endianness);

            var offset = 52;
            foreach (var segment in segments)
            {
                WriteUInt32(image, offset + 0, PtLoad, target.Endianness);
                WriteUInt32(image, offset + 4, checked((uint)segment.FileOffset), target.Endianness);
                WriteUInt32(image, offset + 8, checked((uint)segment.Address), target.Endianness);
                WriteUInt32(image, offset + 12, checked((uint)segment.Address), target.Endianness);
                WriteUInt32(image, offset + 16, checked((uint)segment.FileSize), target.Endianness);
                WriteUInt32(image, offset + 20, checked((uint)segment.MemorySize), target.Endianness);
                WriteUInt32(image, offset + 24, segment.Flags, target.Endianness);
                WriteUInt32(image, offset + 28, checked((uint)segment.Alignment), target.Endianness);
                offset += 32;
            }
        }

        private static uint ElfFlags(RVTarget target)
        {
            var flags = target.HasC ? EfRiscVRvc : 0u;
            flags |= target.Abi switch
            {
                RVAbiKind.Ilp32F or RVAbiKind.Lp64F => EfRiscVFloatAbiSingle,
                RVAbiKind.Ilp32D or RVAbiKind.Lp64D => EfRiscVFloatAbiDouble,
                _ => 0u,
            };
            return flags;
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

        private static int AlignUp(int value, int alignment)
        {
            alignment = Math.Max(1, alignment);
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private static void WriteAbsolute(byte[] image, int offset, ulong value, int size, TargetEndianness endianness)
        {
            if (size != 4 && size != 8)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (size == 4 && value > uint.MaxValue)
                throw new OverflowException("absolute 32-bit relocation overflow.");
            WriteUnsigned(image, offset, value, size, endianness);
        }

        private static void WriteUInt16(byte[] image, int offset, ushort value, TargetEndianness endianness)
            => WriteUnsigned(image, offset, value, 2, endianness);

        private static void WriteUInt32(byte[] image, int offset, uint value, TargetEndianness endianness)
            => WriteUnsigned(image, offset, value, 4, endianness);

        private static void WriteUInt64(byte[] image, int offset, ulong value, TargetEndianness endianness)
            => WriteUnsigned(image, offset, value, 8, endianness);

        private static void WriteUnsigned(byte[] image, int offset, ulong value, int size, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
            {
                for (var i = 0; i < size; i++)
                    image[offset + i] = (byte)(value >> (i * 8));
                return;
            }

            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> ((size - 1 - i) * 8));
        }

        private sealed class ElfSectionLayout
        {
            public string Name { get; }
            public RVObjectSectionKind Kind { get; }
            public int Alignment { get; }
            public byte[] RawData { get; }
            public int MemorySize { get; }
            public int FileOffset { get; set; }
            public int FileSize => RawData.Length;
            public ulong Address { get; set; }

            public ElfSectionLayout(string name, RVObjectSectionKind kind, int alignment, byte[] rawData, int memorySize)
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
