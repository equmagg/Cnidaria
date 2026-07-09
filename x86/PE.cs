using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.X86
{
    internal static class X86PortableExecutableWriter
    {
        private const int DosHeaderOffset = 0x80;
        private const int SectionAlignment = 0x1000;
        private const int FileAlignment = 0x200;
        private const ushort MachineI386 = 0x014c;
        private const ushort MachineAmd64 = 0x8664;
        private const ushort Pe32Magic = 0x10b;
        private const ushort Pe32PlusMagic = 0x20b;
        private const ushort SubsystemConsole = 3;
        private const uint CharacteristicsRelocationsStripped = 0x0001;
        private const uint CharacteristicsExecutableImage = 0x0002;
        private const uint CharacteristicsLargeAddressAware = 0x0020;
        private const uint CharacteristicsMachine32Bit = 0x0100;
        private const uint SectionCode = 0x00000020;
        private const uint SectionInitializedData = 0x00000040;
        private const uint SectionUninitializedData = 0x00000080;
        private const uint SectionRead = 0x40000000;
        private const uint SectionWrite = 0x80000000;
        private const uint SectionExecute = 0x20000000;
        private const int ImportDirectoryIndex = 1;
        private static readonly ImmutableArray<WindowsImportDll> Kernel32Imports = ImmutableArray.Create(
            new WindowsImportDll("KERNEL32.dll", ImmutableArray.Create("GetStdHandle", "WriteFile", "ExitProcess")));

        public static ulong DefaultImageBase(X86Target target)
            => target is not null && target.Is64Bit ? 0x0000000140000000UL : 0x00400000UL;

        public static byte[] WriteExecutable(X86Program obj, ulong imageBase)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            if (obj.Target is null || !obj.Target.Is32Bit && !obj.Target.Is64Bit)
                throw new NotSupportedException("PE emission requires an x86 or x64 target.");

            var importSize = WindowsImportTable.ComputeSize(obj.Target.XLen / 8, Kernel32Imports);
            var sections = CreateSectionLayouts(obj, importSize);
            var optionalHeaderSize = obj.Target.Is64Bit ? 240 : 224;
            var headersSize = AlignUp(DosHeaderOffset + 4 + 20 + optionalHeaderSize + sections.Count * 40, FileAlignment);
            LayoutSections(sections, headersSize);

            var idata = sections.First(static s => s.Name == ".idata");
            var importTable = WindowsImportTable.Build(obj.Target.XLen / 8, Kernel32Imports, idata.Rva);
            idata.RawData = importTable.Bytes;
            idata.VirtualSize = importTable.Bytes.Length;
            idata.RawSize = AlignUp(idata.RawData.Length, FileAlignment);

            var linkedSections = sections.ToDictionary(
                static s => s.Name,
                s => new X86LinkedSection(s.Name, s.Kind, s.RawPointer, s.VirtualSize, s.Alignment, checked(imageBase + (ulong)s.Rva)),
                StringComparer.Ordinal);
            var symbols = BuildSymbolAddressMap(obj, linkedSections, imageBase, idata.Rva, importTable.IatOffsets);

            EncodeText(obj, sections.First(static s => s.Name == ".text"), symbols, imageBase);
            ApplyTextRelocations(obj, sections.First(static s => s.Name == ".text"), symbols, imageBase);
            CopyAndRelocateData(obj, sections, symbols, imageBase);

            if (string.IsNullOrEmpty(obj.EntrySymbol) || !symbols.TryGetValue(obj.EntrySymbol, out var entryAddress))
                throw new InvalidOperationException($"PE entry symbol is not defined: {obj.EntrySymbol}");

            var sizeOfImage = AlignUp(sections.Max(static s => s.Rva + Math.Max(1, s.VirtualSize)), SectionAlignment);
            var sizeOfCode = sections.Where(static s => s.Kind == X86ObjectSectionKind.Text).Sum(static s => s.RawSize);
            var sizeOfInitializedData = sections.Where(static s => s.Kind != X86ObjectSectionKind.Text && s.Kind != X86ObjectSectionKind.Bss).Sum(static s => s.RawSize);
            var sizeOfUninitializedData = sections.Where(static s => s.Kind == X86ObjectSectionKind.Bss).Sum(static s => AlignUp(s.VirtualSize, SectionAlignment));
            var image = new byte[sections.Max(static s => s.RawPointer + s.RawSize)];
            WriteHeaders(image, obj.Target, imageBase, sections, headersSize, sizeOfImage, checked((uint)(entryAddress - imageBase)), 
                sizeOfCode, sizeOfInitializedData, sizeOfUninitializedData, idata.Rva, importTable.Bytes.Length);

            foreach (var section in sections)
            {
                if (section.RawSize == 0)
                    continue;
                Array.Copy(section.RawData, 0, image, section.RawPointer, section.RawData.Length);
            }

            return image;
        }

        private static List<PeSectionLayout> CreateSectionLayouts(X86Program obj, int importSize)
        {
            var sections = new List<PeSectionLayout>();
            var referencedDataSections = new HashSet<string>(
                obj.Symbols
                    .Where(static s => s.Binding != X86ObjectSymbolBinding.External && s.Kind != X86ObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
                    .Select(static s => s.SectionName),
                StringComparer.Ordinal);
            sections.Add(new PeSectionLayout(".text", X86ObjectSectionKind.Text, Math.Max(1, obj.Target.Is64Bit ? 16 : 4), 
                new byte[ComputeTextSize(obj.Text, obj.Target)], 0, SectionCode | SectionRead | SectionExecute));

            foreach (var section in obj.DataSections)
            {
                var size = section.Kind == X86ObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
                if (size == 0 && section.Relocations.Length == 0 && !referencedDataSections.Contains(section.Name))
                    continue;
                var raw = section.Kind == X86ObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray();
                var characteristics = section.Kind switch
                {
                    X86ObjectSectionKind.Rodata => SectionInitializedData | SectionRead,
                    X86ObjectSectionKind.Data => SectionInitializedData | SectionRead | SectionWrite,
                    X86ObjectSectionKind.Bss => SectionUninitializedData | SectionRead | SectionWrite,
                    _ => SectionInitializedData | SectionRead,
                };
                sections.Add(new PeSectionLayout(section.Name, section.Kind, section.Alignment, raw, size, characteristics));
            }

            sections.Add(new PeSectionLayout(".idata", X86ObjectSectionKind.Rodata, 4, new byte[importSize], importSize, SectionInitializedData | SectionRead | SectionWrite));
            return sections;
        }

        private static void LayoutSections(IReadOnlyList<PeSectionLayout> sections, int headersSize)
        {
            var rva = AlignUp(headersSize, SectionAlignment);
            var rawPointer = headersSize;
            foreach (var section in sections)
            {
                section.Rva = rva;
                section.RawPointer = section.Kind == X86ObjectSectionKind.Bss ? 0 : rawPointer;
                section.RawSize = section.Kind == X86ObjectSectionKind.Bss ? 0 : AlignUp(section.RawData.Length, FileAlignment);
                section.VirtualSize = Math.Max(1, section.VirtualSize);
                rva = checked(rva + AlignUp(section.VirtualSize, SectionAlignment));
                rawPointer = checked(rawPointer + section.RawSize);
            }
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
            IReadOnlyDictionary<string, X86LinkedSection> sections,
            ulong imageBase,
            int importRva,
            IReadOnlyDictionary<string, int> importOffsets)
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

            foreach (var pair in importOffsets)
                result[pair.Key] = checked(imageBase + (ulong)importRva + (ulong)pair.Value);

            return result;
        }

        private static void EncodeText(X86Program obj, PeSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, ulong imageBase)
        {
            var offset = 0;
            foreach (var instruction in obj.Text.Instructions)
            {
                var encoded = X86CodeEncoder.Encode(instruction, obj.Target, checked(imageBase + (ulong)text.Rva + (ulong)offset), symbols);
                Array.Copy(encoded, 0, text.RawData, offset, encoded.Length);
                offset = checked(offset + encoded.Length);
            }
        }

        private static void ApplyTextRelocations(X86Program obj, PeSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, ulong imageBase)
        {
            foreach (var relocation in obj.Text.Relocations)
                ApplyRelocation(text.RawData, relocation.Offset, checked(imageBase + (ulong)text.Rva), relocation.Offset, relocation, symbols, obj.Target);
        }

        private static void CopyAndRelocateData(X86Program obj, IReadOnlyList<PeSectionLayout> sections, IReadOnlyDictionary<string, ulong> symbols, ulong imageBase)
        {
            var layouts = sections.ToDictionary(static s => s.Name, StringComparer.Ordinal);
            foreach (var section in obj.DataSections)
            {
                if (!layouts.TryGetValue(section.Name, out var layout))
                    continue;
                if (section.Kind != X86ObjectSectionKind.Bss)
                    Array.Copy(section.Data.ToArray(), 0, layout.RawData, 0, section.Data.Length);
                if (section.Relocations.Length == 0)
                    continue;
                if (section.Kind == X86ObjectSectionKind.Bss)
                    throw new InvalidOperationException("BSS relocations cannot be represented in a PE image without loader relocations.");
                foreach (var relocation in section.Relocations)
                    ApplyRelocation(layout.RawData, relocation.Offset, checked(imageBase + (ulong)layout.Rva), relocation.Offset, relocation, symbols, obj.Target);
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
            if (!symbols.TryGetValue(relocation.SymbolName, out var symbolAddress))
                throw new KeyNotFoundException($"Undefined x86 symbol: {relocation.SymbolName}");
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
                    throw new NotSupportedException($"Unsupported x86 relocation: {relocation.Kind}");
            }
        }

        private static void WriteHeaders(
            byte[] image,
            X86Target target,
            ulong imageBase,
            IReadOnlyList<PeSectionLayout> sections,
            int headersSize,
            int sizeOfImage,
            uint entryPointRva,
            int sizeOfCode,
            int sizeOfInitializedData,
            int sizeOfUninitializedData,
            int importRva,
            int importSize)
        {
            image[0] = 0x4d;
            image[1] = 0x5a;
            WriteUInt32(image, 0x3c, DosHeaderOffset);
            var pe = DosHeaderOffset;
            image[pe] = 0x50;
            image[pe + 1] = 0x45;
            var coff = pe + 4;
            WriteUInt16(image, coff + 0, target.Is64Bit ? MachineAmd64 : MachineI386);
            WriteUInt16(image, coff + 2, (ushort)sections.Count);
            WriteUInt16(image, coff + 16, (ushort)(target.Is64Bit ? 240 : 224));
            WriteUInt16(image, coff + 18, (ushort)(CharacteristicsRelocationsStripped 
                | CharacteristicsExecutableImage | CharacteristicsLargeAddressAware | (target.Is32Bit ? CharacteristicsMachine32Bit : 0)));

            var opt = coff + 20;
            WriteUInt16(image, opt + 0, target.Is64Bit ? Pe32PlusMagic : Pe32Magic);
            image[opt + 2] = 14;
            WriteUInt32(image, opt + 4, (uint)sizeOfCode);
            WriteUInt32(image, opt + 8, (uint)sizeOfInitializedData);
            WriteUInt32(image, opt + 12, (uint)sizeOfUninitializedData);
            WriteUInt32(image, opt + 16, entryPointRva);
            WriteUInt32(image, opt + 20, (uint)sections.First(static s => s.Kind == X86ObjectSectionKind.Text).Rva);

            if (target.Is64Bit)
            {
                WriteUInt64(image, opt + 24, imageBase);
                WriteCommonOptionalHeaderTail(image, opt + 32, headersSize, sizeOfImage, importRva, importSize, target.Is64Bit);
            }
            else
            {
                var dataSection = sections.FirstOrDefault(static s => s.Kind == X86ObjectSectionKind.Data || s.Kind == X86ObjectSectionKind.Bss);
                WriteUInt32(image, opt + 24, (uint)(dataSection?.Rva ?? 0));
                WriteUInt32(image, opt + 28, checked((uint)imageBase));
                WriteCommonOptionalHeaderTail(image, opt + 32, headersSize, sizeOfImage, importRva, importSize, target.Is64Bit);
            }

            var sectionHeader = opt + (target.Is64Bit ? 240 : 224);
            for (var i = 0; i < sections.Count; i++)
                WriteSectionHeader(image, sectionHeader + i * 40, sections[i]);
        }

        private static void WriteCommonOptionalHeaderTail(byte[] image, int offset, int headersSize, int sizeOfImage, int importRva, int importSize, bool is64Bit)
        {
            WriteUInt32(image, offset + 0, SectionAlignment);
            WriteUInt32(image, offset + 4, FileAlignment);
            WriteUInt16(image, offset + 8, 6);
            WriteUInt16(image, offset + 10, 0);
            WriteUInt16(image, offset + 16, 6);
            WriteUInt32(image, offset + 24, (uint)sizeOfImage);
            WriteUInt32(image, offset + 28, (uint)headersSize);
            WriteUInt16(image, offset + 36, SubsystemConsole);
            WriteUInt16(image, offset + 38, 0);

            if (is64Bit)
            {
                WriteUInt64(image, offset + 40, 0x100000);
                WriteUInt64(image, offset + 48, 0x1000);
                WriteUInt64(image, offset + 56, 0x100000);
                WriteUInt64(image, offset + 64, 0x1000);
                WriteUInt32(image, offset + 72, 0);
                WriteUInt32(image, offset + 76, 16);
                WriteUInt32(image, offset + 80 + ImportDirectoryIndex * 8, (uint)importRva);
                WriteUInt32(image, offset + 84 + ImportDirectoryIndex * 8, (uint)importSize);
            }
            else
            {
                WriteUInt32(image, offset + 40, 0x100000);
                WriteUInt32(image, offset + 44, 0x1000);
                WriteUInt32(image, offset + 48, 0x100000);
                WriteUInt32(image, offset + 52, 0x1000);
                WriteUInt32(image, offset + 56, 0);
                WriteUInt32(image, offset + 60, 16);
                WriteUInt32(image, offset + 64 + ImportDirectoryIndex * 8, (uint)importRva);
                WriteUInt32(image, offset + 68 + ImportDirectoryIndex * 8, (uint)importSize);
            }
        }

        private static void WriteSectionHeader(byte[] image, int offset, PeSectionLayout section)
        {
            var name = Encoding.ASCII.GetBytes(section.Name);
            Array.Copy(name, 0, image, offset, Math.Min(8, name.Length));
            WriteUInt32(image, offset + 8, (uint)section.VirtualSize);
            WriteUInt32(image, offset + 12, (uint)section.Rva);
            WriteUInt32(image, offset + 16, (uint)section.RawSize);
            WriteUInt32(image, offset + 20, (uint)section.RawPointer);
            WriteUInt32(image, offset + 36, section.Characteristics);
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
                    WriteUInt32(image, offset, unchecked((uint)(int)value));
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

        private static void WriteUInt16(byte[] image, int offset, ushort value)
        {
            image[offset] = (byte)value;
            image[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] image, int offset, uint value)
        {
            image[offset] = (byte)value;
            image[offset + 1] = (byte)(value >> 8);
            image[offset + 2] = (byte)(value >> 16);
            image[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] image, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
                image[offset + i] = (byte)(value >> (i * 8));
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private sealed class PeSectionLayout
        {
            public string Name { get; }
            public X86ObjectSectionKind Kind { get; }
            public int Alignment { get; }
            public uint Characteristics { get; }
            public byte[] RawData { get; set; }
            public int VirtualSize { get; set; }
            public int Rva { get; set; }
            public int RawPointer { get; set; }
            public int RawSize { get; set; }

            public PeSectionLayout(string name, X86ObjectSectionKind kind, int alignment, byte[] rawData, int virtualSize, uint characteristics)
            {
                Name = name;
                Kind = kind;
                Alignment = Math.Max(1, alignment);
                RawData = rawData ?? Array.Empty<byte>();
                VirtualSize = Math.Max(virtualSize, RawData.Length);
                Characteristics = characteristics;
            }
        }

        private readonly struct WindowsImportDll
        {
            public string Name { get; }
            public ImmutableArray<string> Functions { get; }

            public WindowsImportDll(string name, ImmutableArray<string> functions)
            {
                Name = name;
                Functions = functions.IsDefault ? ImmutableArray<string>.Empty : functions;
            }
        }

        private sealed class WindowsImportTable
        {
            public byte[] Bytes { get; }
            public IReadOnlyDictionary<string, int> IatOffsets { get; }

            private WindowsImportTable(byte[] bytes, IReadOnlyDictionary<string, int> iatOffsets)
            {
                Bytes = bytes;
                IatOffsets = iatOffsets;
            }

            public static int ComputeSize(int pointerSize, ImmutableArray<WindowsImportDll> dlls)
                => Build(pointerSize, dlls, 0).Bytes.Length;

            public static WindowsImportTable Build(int pointerSize, ImmutableArray<WindowsImportDll> dlls, int sectionRva)
            {
                var layouts = new List<DllLayout>();
                var offset = checked((dlls.Length + 1) * 20);
                offset = AlignUp(offset, pointerSize);

                foreach (var dll in dlls)
                {
                    var layout = new DllLayout(dll.Name, dll.Functions, layouts.Count * 20);
                    layout.IltOffset = offset;
                    offset = checked(offset + (dll.Functions.Length + 1) * pointerSize);
                    layout.IatOffset = offset;
                    offset = checked(offset + (dll.Functions.Length + 1) * pointerSize);
                    layouts.Add(layout);
                }

                foreach (var layout in layouts)
                {
                    offset = AlignUp(offset, 2);
                    layout.NameOffset = offset;
                    offset = checked(offset + Encoding.ASCII.GetByteCount(layout.Name) + 1);
                    foreach (var function in layout.Functions)
                    {
                        offset = AlignUp(offset, 2);
                        layout.HintNameOffsets[function] = offset;
                        offset = checked(offset + 2 + Encoding.ASCII.GetByteCount(function) + 1);
                    }
                }

                var bytes = new byte[offset];
                var iatOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var layout in layouts)
                {
                    WriteUInt32(bytes, layout.DescriptorOffset + 0, checked((uint)(sectionRva + layout.IltOffset)));
                    WriteUInt32(bytes, layout.DescriptorOffset + 12, checked((uint)(sectionRva + layout.NameOffset)));
                    WriteUInt32(bytes, layout.DescriptorOffset + 16, checked((uint)(sectionRva + layout.IatOffset)));
                    WriteAsciiNull(bytes, layout.NameOffset, layout.Name);

                    for (var i = 0; i < layout.Functions.Length; i++)
                    {
                        var function = layout.Functions[i];
                        var hintNameRva = checked((ulong)(sectionRva + layout.HintNameOffsets[function]));
                        WriteThunk(bytes, layout.IltOffset + i * pointerSize, hintNameRva, pointerSize);
                        WriteThunk(bytes, layout.IatOffset + i * pointerSize, hintNameRva, pointerSize);
                        WriteAsciiNull(bytes, layout.HintNameOffsets[function] + 2, function);
                        iatOffsets["__imp_" + function] = layout.IatOffset + i * pointerSize;
                    }
                }

                return new WindowsImportTable(bytes, iatOffsets);
            }

            private static void WriteThunk(byte[] bytes, int offset, ulong value, int pointerSize)
            {
                if (pointerSize == 8)
                    WriteUInt64(bytes, offset, value);
                else
                    WriteUInt32(bytes, offset, checked((uint)value));
            }

            private static void WriteAsciiNull(byte[] bytes, int offset, string value)
            {
                var raw = Encoding.ASCII.GetBytes(value);
                Array.Copy(raw, 0, bytes, offset, raw.Length);
                bytes[offset + raw.Length] = 0;
            }

            private sealed class DllLayout
            {
                public string Name { get; }
                public ImmutableArray<string> Functions { get; }
                public int DescriptorOffset { get; }
                public int IltOffset { get; set; }
                public int IatOffset { get; set; }
                public int NameOffset { get; set; }
                public Dictionary<string, int> HintNameOffsets { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

                public DllLayout(string name, ImmutableArray<string> functions, int descriptorOffset)
                {
                    Name = name;
                    Functions = functions;
                    DescriptorOffset = descriptorOffset;
                }
            }
        }
    }
}
