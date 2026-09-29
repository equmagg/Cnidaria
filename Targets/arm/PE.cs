using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.Arm;

internal static class ArmPortableExecutableWriter
{
    private const int DosHeaderOffset = 0x80;
    private const int SectionAlignment = 0x1000;
    private const int FileAlignment = 0x200;
    private const ushort MachineArm = 0x01C0;
    private const ushort MachineArm64 = 0xAA64;
    private const ushort Pe32Magic = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;
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
        new WindowsImportDll("KERNEL32.dll", ImmutableArray.Create(
            "GetStdHandle", "WriteFile", "ExitProcess", "GetProcessHeap", "HeapAlloc", "HeapReAlloc", "HeapFree", "VirtualAlloc", "VirtualFree")));

    public static ulong DefaultImageBase(ArmTarget target)
        => target is not null && target.Is64Bit ? 0x0000000140000000UL : 0x00400000UL;

    public static byte[] WriteExecutable(ArmProgram obj, ulong imageBase)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));
        if (obj.Target.OperatingSystem != OperatingSystemKind.Windows)
            throw new ArgumentException("PE executable writer requires a Windows ARM target.", nameof(obj));
        if (obj.Target.Endianness != TargetEndianness.Little)
            throw new NotSupportedException("PE emission requires a little-endian ARM target.");
        if (obj.Target.Is32Bit && imageBase > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(imageBase));

        var importSize = WindowsImportTable.ComputeSize(obj.Target.XLen / 8, Kernel32Imports);
        var sections = CreateSectionLayouts(obj, importSize);
        var optionalHeaderSize = obj.Target.Is64Bit ? 240 : 224;
        var headersSize = AlignUp(DosHeaderOffset + 4 + 20 + optionalHeaderSize + sections.Count * 40, FileAlignment);
        LayoutSections(sections, headersSize);

        var idata = sections.First(static section => section.Name == ".idata");
        var importTable = WindowsImportTable.Build(obj.Target.XLen / 8, Kernel32Imports, idata.Rva);
        idata.RawData = importTable.Bytes;
        idata.VirtualSize = importTable.Bytes.Length;
        idata.RawSize = AlignUp(idata.RawData.Length, FileAlignment);

        var symbols = BuildSymbolAddressMap(obj, sections, imageBase, idata.Rva, importTable.IatOffsets);
        EncodeText(obj, sections.First(static section => section.Name == ".text"), symbols, imageBase);
        CopyAndRelocateData(obj, sections, symbols, imageBase);

        if (string.IsNullOrEmpty(obj.EntrySymbol) || !symbols.TryGetValue(obj.EntrySymbol, out var entryAddress))
            throw new InvalidOperationException("PE entry symbol is not defined: " + obj.EntrySymbol);

        var sizeOfImage = AlignUp(sections.Max(static section => section.Rva + Math.Max(1, section.VirtualSize)), SectionAlignment);
        var sizeOfCode = sections.Where(static section => section.Kind == ArmObjectSectionKind.Text).Sum(static section => section.RawSize);
        var sizeOfInitializedData = sections.Where(static section => section.Kind != ArmObjectSectionKind.Text && section.Kind != ArmObjectSectionKind.Bss).Sum(static section => section.RawSize);
        var sizeOfUninitializedData = sections.Where(static section => section.Kind == ArmObjectSectionKind.Bss).Sum(static section => AlignUp(section.VirtualSize, SectionAlignment));
        var fileSize = sections.Max(static section => section.RawPointer + section.RawSize);
        var image = new byte[Math.Max(headersSize, fileSize)];

        WriteHeaders(
            image,
            obj.Target,
            imageBase,
            sections,
            headersSize,
            sizeOfImage,
            checked((uint)(entryAddress - imageBase)),
            sizeOfCode,
            sizeOfInitializedData,
            sizeOfUninitializedData,
            idata.Rva,
            importTable.Bytes.Length);

        foreach (var section in sections)
        {
            if (section.RawSize == 0)
                continue;
            Array.Copy(section.RawData, 0, image, section.RawPointer, section.RawData.Length);
        }

        return image;
    }

    private static List<PeSectionLayout> CreateSectionLayouts(ArmProgram obj, int importSize)
    {
        var referenced = new HashSet<string>(
            obj.Symbols
                .Where(static symbol => symbol.Binding != ArmObjectSymbolBinding.External && symbol.Kind != ArmObjectSymbolKind.Section && !string.IsNullOrEmpty(symbol.SectionName))
                .Select(static symbol => symbol.SectionName),
            StringComparer.Ordinal);
        var sections = new List<PeSectionLayout>
        {
            new PeSectionLayout(
                ".text",
                ArmObjectSectionKind.Text,
                obj.Target.Is64Bit ? 16 : 4,
                new byte[obj.Text.SizeInBytes],
                obj.Text.SizeInBytes,
                SectionCode | SectionRead | SectionExecute)
        };

        foreach (var section in obj.DataSections)
        {
            var size = section.Kind == ArmObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
            if (size == 0 && section.Relocations.Length == 0 && !referenced.Contains(section.Name))
                continue;
            var rawData = section.Kind == ArmObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray();
            var characteristics = section.Kind switch
            {
                ArmObjectSectionKind.Rodata => SectionInitializedData | SectionRead,
                ArmObjectSectionKind.Data => SectionInitializedData | SectionRead | SectionWrite,
                ArmObjectSectionKind.Bss => SectionUninitializedData | SectionRead | SectionWrite,
                _ => SectionInitializedData | SectionRead,
            };
            sections.Add(new PeSectionLayout(section.Name, section.Kind, section.Alignment, rawData, size, characteristics));
        }

        sections.Add(new PeSectionLayout(
            ".idata",
            ArmObjectSectionKind.Rodata,
            4,
            new byte[importSize],
            importSize,
            SectionInitializedData | SectionRead | SectionWrite));
        return sections;
    }

    private static void LayoutSections(IReadOnlyList<PeSectionLayout> sections, int headersSize)
    {
        var rva = AlignUp(headersSize, SectionAlignment);
        var rawPointer = headersSize;
        foreach (var section in sections)
        {
            section.Rva = rva;
            section.RawPointer = section.Kind == ArmObjectSectionKind.Bss ? 0 : rawPointer;
            section.RawSize = section.Kind == ArmObjectSectionKind.Bss ? 0 : AlignUp(section.RawData.Length, FileAlignment);
            section.VirtualSize = Math.Max(1, section.VirtualSize);
            rva = checked(rva + AlignUp(section.VirtualSize, SectionAlignment));
            rawPointer = checked(rawPointer + section.RawSize);
        }
    }

    private static Dictionary<string, ulong> BuildSymbolAddressMap(
        ArmProgram obj,
        IReadOnlyList<PeSectionLayout> sections,
        ulong imageBase,
        int importRva,
        IReadOnlyDictionary<string, int> importOffsets)
    {
        var sectionMap = sections.ToDictionary(static section => section.Name, StringComparer.Ordinal);
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var text = sectionMap[".text"];
        foreach (var label in obj.Text.Labels)
            result[label.Key] = checked(imageBase + (ulong)text.Rva + (ulong)label.Value);

        foreach (var symbol in obj.Symbols)
        {
            if (symbol.Binding == ArmObjectSymbolBinding.External || string.IsNullOrEmpty(symbol.SectionName))
                continue;
            if (!sectionMap.TryGetValue(symbol.SectionName, out var section))
            {
                if (symbol.Kind == ArmObjectSymbolKind.Section && symbol.Size == 0)
                    continue;
                throw new InvalidOperationException("Symbol section does not exist: " + symbol.SectionName);
            }
            result[symbol.Name] = checked(imageBase + (ulong)section.Rva + (ulong)symbol.Offset);
        }

        foreach (var pair in importOffsets)
            result[pair.Key] = checked(imageBase + (ulong)importRva + (ulong)pair.Value);
        return result;
    }

    private static void EncodeText(ArmProgram obj, PeSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, ulong imageBase)
    {
        var relocations = obj.Text.Relocations.ToDictionary(static relocation => relocation.Offset);
        for (var index = 0; index < obj.Text.Instructions.Length; index++)
        {
            var offset = checked(index * 4);
            var pc = checked(imageBase + (ulong)text.Rva + (ulong)offset);
            var instruction = obj.Text.Instructions[index];
            if (relocations.TryGetValue(offset, out var relocation))
                instruction = ApplyTextRelocation(instruction, relocation, pc, symbols);
            var word = ArmCodeEncoder.EncodeWord(instruction, obj.Target, pc, symbols);
            ArmCodeEncoder.WriteWord(text.RawData, offset, word, TargetEndianness.Little);
        }
    }

    private static ArmInstruction ApplyTextRelocation(
        ArmInstruction instruction,
        ArmObjectRelocation relocation,
        ulong pc,
        IReadOnlyDictionary<string, ulong> symbols)
    {
        var value = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
        return relocation.Kind switch
        {
            ArmObjectRelocationKind.ArmMovw16 => instruction.WithOperand1(ArmOperand.ImmediateOperand(value & 0xFFFF)),
            ArmObjectRelocationKind.ArmMovt16 => instruction.WithOperand1(ArmOperand.ImmediateOperand((value >> 16) & 0xFFFF)),
            ArmObjectRelocationKind.ArmBranch24 or
            ArmObjectRelocationKind.ArmCall24 or
            ArmObjectRelocationKind.AArch64Branch26 or
            ArmObjectRelocationKind.AArch64Call26 or
            ArmObjectRelocationKind.AArch64ConditionalBranch19 => instruction.WithOperand0(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64CompareBranch19 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64TestBranch14 => instruction.WithOperand2(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64Adr21 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64Adrp21 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked((value & ~0xFFFL) - ((long)pc & ~0xFFFL)))),
            ArmObjectRelocationKind.AArch64AddLow12 => instruction.WithOperand2(ArmOperand.ImmediateOperand(value & 0xFFF)),
            ArmObjectRelocationKind.AArch64LoadLiteral19 => instruction.WithOperand1(ArmOperand.Literal(checked(value - (long)pc), instruction.Operand0.Size)),
            _ => throw new NotSupportedException("Unsupported ARM PE text relocation: " + relocation.Kind),
        };
    }

    private static void CopyAndRelocateData(
        ArmProgram obj,
        IReadOnlyList<PeSectionLayout> sections,
        IReadOnlyDictionary<string, ulong> symbols,
        ulong imageBase)
    {
        var sectionMap = sections.ToDictionary(static section => section.Name, StringComparer.Ordinal);
        foreach (var section in obj.DataSections)
        {
            if (!sectionMap.TryGetValue(section.Name, out var layout))
                continue;
            if (section.Kind != ArmObjectSectionKind.Bss)
                Array.Copy(section.Data.ToArray(), 0, layout.RawData, 0, section.Data.Length);
            if (section.Relocations.Length == 0)
                continue;
            if (section.Kind == ArmObjectSectionKind.Bss)
                throw new InvalidOperationException("BSS relocations cannot be represented in a PE image without loader relocations.");

            foreach (var relocation in section.Relocations)
            {
                var signed = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
                if (signed < 0)
                    throw new OverflowException("ARM absolute relocation resolved to a negative address.");
                var value = (ulong)signed;
                switch (relocation.Kind)
                {
                    case ArmObjectRelocationKind.AbsolutePointer:
                        WriteAbsolute(layout.RawData, relocation.Offset, value, obj.Target.Is32Bit ? 4 : 8);
                        break;
                    case ArmObjectRelocationKind.Absolute32:
                        WriteAbsolute(layout.RawData, relocation.Offset, value, 4);
                        break;
                    case ArmObjectRelocationKind.Absolute64:
                        WriteAbsolute(layout.RawData, relocation.Offset, value, 8);
                        break;
                    default:
                        throw new NotSupportedException("Unsupported ARM PE data relocation: " + relocation.Kind);
                }
            }
        }
    }

    private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
    {
        if (symbols.TryGetValue(symbol, out var address))
            return address;
        throw new KeyNotFoundException("Undefined ARM symbol: " + symbol);
    }

    private static void WriteHeaders(
        byte[] image,
        ArmTarget target,
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
        image[0] = 0x4D;
        image[1] = 0x5A;
        WriteUInt32(image, 0x3C, DosHeaderOffset);
        var pe = DosHeaderOffset;
        image[pe] = 0x50;
        image[pe + 1] = 0x45;
        var coff = pe + 4;
        WriteUInt16(image, coff, target.Is64Bit ? MachineArm64 : MachineArm);
        WriteUInt16(image, coff + 2, checked((ushort)sections.Count));
        WriteUInt16(image, coff + 16, checked((ushort)(target.Is64Bit ? 240 : 224)));
        WriteUInt16(image, coff + 18, checked((ushort)(
            CharacteristicsRelocationsStripped |
            CharacteristicsExecutableImage |
            CharacteristicsLargeAddressAware |
            (target.Is32Bit ? CharacteristicsMachine32Bit : 0))));

        var optional = coff + 20;
        WriteUInt16(image, optional, target.Is64Bit ? Pe32PlusMagic : Pe32Magic);
        image[optional + 2] = 14;
        WriteUInt32(image, optional + 4, checked((uint)sizeOfCode));
        WriteUInt32(image, optional + 8, checked((uint)sizeOfInitializedData));
        WriteUInt32(image, optional + 12, checked((uint)sizeOfUninitializedData));
        WriteUInt32(image, optional + 16, entryPointRva);
        WriteUInt32(image, optional + 20, checked((uint)sections.First(static section => section.Kind == ArmObjectSectionKind.Text).Rva));

        if (target.Is64Bit)
        {
            WriteUInt64(image, optional + 24, imageBase);
            WriteCommonOptionalHeaderTail(image, optional + 32, headersSize, sizeOfImage, importRva, importSize, true);
        }
        else
        {
            var dataSection = sections.FirstOrDefault(static section => section.Kind is ArmObjectSectionKind.Data or ArmObjectSectionKind.Bss);
            WriteUInt32(image, optional + 24, checked((uint)(dataSection?.Rva ?? 0)));
            WriteUInt32(image, optional + 28, checked((uint)imageBase));
            WriteCommonOptionalHeaderTail(image, optional + 32, headersSize, sizeOfImage, importRva, importSize, false);
        }

        var sectionHeader = optional + (target.Is64Bit ? 240 : 224);
        for (var index = 0; index < sections.Count; index++)
            WriteSectionHeader(image, sectionHeader + index * 40, sections[index]);
    }

    private static void WriteCommonOptionalHeaderTail(
        byte[] image,
        int offset,
        int headersSize,
        int sizeOfImage,
        int importRva,
        int importSize,
        bool is64Bit)
    {
        WriteUInt32(image, offset, SectionAlignment);
        WriteUInt32(image, offset + 4, FileAlignment);
        WriteUInt16(image, offset + 8, 6);
        WriteUInt16(image, offset + 10, 0);
        WriteUInt16(image, offset + 16, 6);
        WriteUInt32(image, offset + 24, checked((uint)sizeOfImage));
        WriteUInt32(image, offset + 28, checked((uint)headersSize));
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
            WriteUInt32(image, offset + 80 + ImportDirectoryIndex * 8, checked((uint)importRva));
            WriteUInt32(image, offset + 84 + ImportDirectoryIndex * 8, checked((uint)importSize));
        }
        else
        {
            WriteUInt32(image, offset + 40, 0x100000);
            WriteUInt32(image, offset + 44, 0x1000);
            WriteUInt32(image, offset + 48, 0x100000);
            WriteUInt32(image, offset + 52, 0x1000);
            WriteUInt32(image, offset + 56, 0);
            WriteUInt32(image, offset + 60, 16);
            WriteUInt32(image, offset + 64 + ImportDirectoryIndex * 8, checked((uint)importRva));
            WriteUInt32(image, offset + 68 + ImportDirectoryIndex * 8, checked((uint)importSize));
        }
    }

    private static void WriteSectionHeader(byte[] image, int offset, PeSectionLayout section)
    {
        var name = Encoding.ASCII.GetBytes(section.Name);
        Array.Copy(name, 0, image, offset, Math.Min(8, name.Length));
        WriteUInt32(image, offset + 8, checked((uint)section.VirtualSize));
        WriteUInt32(image, offset + 12, checked((uint)section.Rva));
        WriteUInt32(image, offset + 16, checked((uint)section.RawSize));
        WriteUInt32(image, offset + 20, checked((uint)section.RawPointer));
        WriteUInt32(image, offset + 36, section.Characteristics);
    }

    private static void WriteAbsolute(byte[] image, int offset, ulong value, int size)
    {
        if (size == 4 && value > uint.MaxValue)
            throw new OverflowException("ARM 32-bit absolute relocation overflow.");
        if (offset < 0 || size < 0 || offset > image.Length - size)
            throw new ArgumentOutOfRangeException(nameof(offset));
        for (var index = 0; index < size; index++)
            image[offset + index] = (byte)(value >> (index * 8));
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
        for (var index = 0; index < 8; index++)
            image[offset + index] = (byte)(value >> (index * 8));
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
        public ArmObjectSectionKind Kind { get; }
        public int Alignment { get; }
        public uint Characteristics { get; }
        public byte[] RawData { get; set; }
        public int VirtualSize { get; set; }
        public int Rva { get; set; }
        public int RawPointer { get; set; }
        public int RawSize { get; set; }

        public PeSectionLayout(string name, ArmObjectSectionKind kind, int alignment, byte[] rawData, int virtualSize, uint characteristics)
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
                WriteUInt32(bytes, layout.DescriptorOffset, checked((uint)(sectionRva + layout.IltOffset)));
                WriteUInt32(bytes, layout.DescriptorOffset + 12, checked((uint)(sectionRva + layout.NameOffset)));
                WriteUInt32(bytes, layout.DescriptorOffset + 16, checked((uint)(sectionRva + layout.IatOffset)));
                WriteAsciiNull(bytes, layout.NameOffset, layout.Name);

                for (var index = 0; index < layout.Functions.Length; index++)
                {
                    var function = layout.Functions[index];
                    var hintNameRva = checked((ulong)(sectionRva + layout.HintNameOffsets[function]));
                    WriteThunk(bytes, layout.IltOffset + index * pointerSize, hintNameRva, pointerSize);
                    WriteThunk(bytes, layout.IatOffset + index * pointerSize, hintNameRva, pointerSize);
                    WriteAsciiNull(bytes, layout.HintNameOffsets[function] + 2, function);
                    iatOffsets["__imp_" + function] = layout.IatOffset + index * pointerSize;
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
