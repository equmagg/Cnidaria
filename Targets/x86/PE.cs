using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.X86;

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
    public static ulong DefaultImageBase(X86Target target)
        => target is not null && target.Is64Bit ? 0x0000000140000000UL : 0x00400000UL;

    public static byte[] WriteExecutable(X86Program obj, ulong imageBase, IEnumerable<PeImport>? imports = null)
        => Write(obj, imageBase, imports, null, obj.EntrySymbol);

    public static byte[] WriteDynamicLibrary(X86Program obj, string libraryName, IEnumerable<PeImport>? imports = null, string? entrySymbol = null, ulong imageBase = 0)
    {
        if (!obj.Target.Is64Bit)
            throw new NotSupportedException("PE dynamic libraries require a 64-bit target");
        return Write(obj, imageBase == 0 ? 0x180000000UL : imageBase, imports, libraryName, entrySymbol ?? string.Empty);
    }

    private static byte[] Write(X86Program obj, ulong imageBase, IEnumerable<PeImport>? additionalImports, string? libraryName, string entrySymbol)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Target.OperatingSystem != OperatingSystemKind.Windows)
            throw new ArgumentException("PE emission requires a Windows target", nameof(obj));
        if (obj.Target.Endianness != TargetEndianness.Little)
            throw new NotSupportedException("PE emission requires a little-endian target");
        if ((imageBase & 65535) != 0 || obj.Target.Is32Bit && imageBase > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(imageBase));

        var imports = PeImport.Merge(PeImportTable.Kernel32Imports.Concat(obj.Imports).Concat(additionalImports ?? Array.Empty<PeImport>()));
        var dataImports = imports.Where(static import => import.IsData).Select(static import => import.SymbolName).ToHashSet(StringComparer.Ordinal);
        foreach (var relocation in obj.DataSections.SelectMany(static section => section.Relocations))
        {
            if (dataImports.Contains(relocation.SymbolName))
                throw new NotSupportedException($"A PE data initializer cannot contain the address of imported data: {relocation.SymbolName}");
        }
        obj = X86ObjectLinker.RedirectDataImports(obj, dataImports.ToImmutableArray());
        var defined = obj.Text.Labels.Keys.Concat(obj.Symbols.Where(static symbol => symbol.Binding != X86ObjectSymbolBinding.External).Select(static symbol => symbol.Name)).ToHashSet(StringComparer.Ordinal);
        var references = obj.Symbols.Where(static symbol => symbol.Binding == X86ObjectSymbolBinding.External).Select(static symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var instruction in obj.Text.Instructions)
        {
            foreach (var operand in new[] { instruction.Operand0, instruction.Operand1, instruction.Operand2, instruction.Operand3 })
            {
                if (operand.Symbol is string name)
                    references.Add(name);
            }
        }
        foreach (var relocation in obj.Text.Relocations.Concat(obj.DataSections.SelectMany(static section => section.Relocations)))
            references.Add(relocation.SymbolName);
        var thunks = imports.Where(import => !import.IsData && references.Contains(import.SymbolName) && !defined.Contains(import.SymbolName)).ToArray();
        obj = X86ObjectLinker.RelaxBranches(obj, imports.SelectMany(static import => new[] { import.SymbolName, import.AddressSymbol }));
        var importTable = PeImportTable.Build(obj.Target.XLen / 8, imports, 0);
        var sections = CreateSectionLayouts(obj, importTable.Bytes.Length);
        PeSectionLayout? thunkSection = null;
        if (thunks.Length != 0)
        {
            thunkSection = new PeSectionLayout(".thunks", X86ObjectSectionKind.Text, 16, new byte[thunks.Length * 6], thunks.Length * 6, SectionCode | SectionRead | SectionExecute);
            sections.Add(thunkSection);
        }
        PeSectionLayout? exportsSection = null;
        PeSectionLayout? relocationsSection = null;
        var exports = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (libraryName is not null)
        {
            foreach (var symbol in obj.Symbols)
            {
                if (symbol.Binding == X86ObjectSymbolBinding.Global && symbol.Kind != X86ObjectSymbolKind.Section && !string.IsNullOrEmpty(symbol.SectionName))
                    exports.Add(symbol.Name, 0);
            }
            byte[] data = PeTables.Exports(libraryName, exports, 0);
            exportsSection = new PeSectionLayout(".edata", X86ObjectSectionKind.Rodata, 4, data, data.Length, SectionInitializedData | SectionRead);
            relocationsSection = new PeSectionLayout(".reloc", X86ObjectSectionKind.Rodata, 4, Array.Empty<byte>(), 0, SectionInitializedData | SectionRead | 0x02000000);
            sections.Add(exportsSection);
            sections.Add(relocationsSection);
        }
        if (sections.Select(static section => section.Name).Distinct(StringComparer.Ordinal).Count() != sections.Count)
            throw new InvalidOperationException("Duplicate PE section name");
        var optionalHeaderSize = obj.Target.Is64Bit ? 240 : 224;
        var headersSize = AlignUp(DosHeaderOffset + 4 + 20 + optionalHeaderSize + sections.Count * 40, FileAlignment);
        LayoutSections(sections, headersSize);
        var idata = sections.First(static section => section.Name == ".idata");
        importTable = PeImportTable.Build(obj.Target.XLen / 8, imports, idata.Rva);
        idata.RawData = importTable.Bytes;
        var linkedSections = sections.ToDictionary(static section => section.Name,
            section => new X86LinkedSection(section.Name, section.Kind, section.RawPointer, section.VirtualSize, section.Alignment, checked(imageBase + (ulong)section.Rva)), StringComparer.Ordinal);
        var symbols = BuildSymbolAddressMap(obj, linkedSections, imageBase, idata.Rva, importTable.AddressOffsets);
        var baseRelocations = libraryName is null ? null : new List<(int Rva, int Size)>();
        if (thunkSection is not null)
        {
            for (int i = 0; i < thunks.Length; i++)
            {
                int offset = i * 6;
                ulong address = checked(imageBase + (ulong)(thunkSection.Rva + offset));
                ulong slot = symbols[thunks[i].AddressSymbol];
                symbols.Add(thunks[i].SymbolName, address);
                thunkSection.RawData[offset] = 0xff;
                thunkSection.RawData[offset + 1] = 0x25;
                if (obj.Target.Is64Bit)
                    WriteSigned(thunkSection.RawData, offset + 2, checked((long)slot - (long)(address + 6)), 4);
                else
                    WriteUInt32(thunkSection.RawData, offset + 2, checked((uint)slot));
            }
        }
        var text = sections.First(static section => section.Name == ".text");
        EncodeText(obj, text, symbols, imageBase, baseRelocations);
        ApplyTextRelocations(obj, text, symbols, imageBase);
        CopyAndRelocateData(obj, sections, symbols, imageBase);
        if (libraryName is not null)
        {
            foreach (string name in exports.Keys.ToArray())
                exports[name] = checked((uint)(symbols[name] - imageBase));
            exportsSection!.RawData = PeTables.Exports(libraryName, exports, exportsSection.Rva);
            foreach (var section in obj.DataSections)
            {
                var layout = sections.FirstOrDefault(layout => layout.Name == section.Name);
                if (layout is null)
                    continue;
                foreach (var relocation in section.Relocations)
                    RecordBaseRelocation(baseRelocations!, layout.Rva, relocation, obj.Target.XLen / 8);
            }
            foreach (var relocation in obj.Text.Relocations)
                RecordBaseRelocation(baseRelocations!, text.Rva, relocation, obj.Target.XLen / 8);
            relocationsSection!.RawData = PeTables.BaseRelocations(baseRelocations!);
            relocationsSection.VirtualSize = relocationsSection.RawData.Length;
            relocationsSection.RawSize = AlignUp(relocationsSection.RawData.Length, FileAlignment);
        }
        uint entryRva = 0;
        if (!string.IsNullOrEmpty(entrySymbol))
            entryRva = checked((uint)(symbols.TryGetValue(entrySymbol, out ulong entryAddress) ? entryAddress - imageBase : throw new InvalidOperationException("PE entry symbol is not defined: " + entrySymbol)));
        else if (libraryName is null)
            throw new InvalidOperationException("PE executable entry symbol is empty");

        int sizeOfImage = AlignUp(sections.Max(static section => section.Rva + Math.Max(1, section.VirtualSize)), SectionAlignment);
        int sizeOfCode = sections.Where(static section => section.Kind == X86ObjectSectionKind.Text).Sum(static section => section.RawSize);
        int sizeOfInitializedData = sections.Where(static section => section.Kind is not (X86ObjectSectionKind.Text or X86ObjectSectionKind.Bss)).Sum(static section => section.RawSize);
        int sizeOfUninitializedData = sections.Where(static section => section.Kind == X86ObjectSectionKind.Bss).Sum(static section => AlignUp(section.VirtualSize, SectionAlignment));
        var image = new byte[Math.Max(headersSize, sections.Max(static section => section.RawPointer + section.RawSize))];
        WriteHeaders(image, obj.Target, imageBase, sections, headersSize, sizeOfImage, entryRva,
            sizeOfCode, sizeOfInitializedData, sizeOfUninitializedData, idata.Rva, importTable.DirectorySize);
        if (libraryName is not null)
        {
            int coff = DosHeaderOffset + 4;
            WriteUInt16(image, coff + 18, (ushort)(CharacteristicsExecutableImage | CharacteristicsLargeAddressAware | 0x2000));
            WriteUInt16(image, coff + 20 + 70, 0x0160);
            int directories = coff + 20 + 112;
            WriteUInt32(image, directories, checked((uint)exportsSection!.Rva));
            WriteUInt32(image, directories + 4, checked((uint)exportsSection.RawData.Length));
            WriteUInt32(image, directories + 5 * 8, checked((uint)relocationsSection!.Rva));
            WriteUInt32(image, directories + 5 * 8 + 4, checked((uint)relocationsSection.RawData.Length));
        }
        foreach (var section in sections)
        {
            if (section.RawSize != 0)
                Array.Copy(section.RawData, 0, image, section.RawPointer, section.RawData.Length);
        }
        return image;
    }

    private static void RecordBaseRelocation(List<(int Rva, int Size)> relocations, int sectionRva, X86ObjectRelocation relocation, int pointerSize)
    {
        int size = relocation.Kind switch
        {
            X86ObjectRelocationKind.AbsolutePointer => pointerSize,
            X86ObjectRelocationKind.Absolute32 => 4,
            X86ObjectRelocationKind.Absolute64 => 8,
            _ => 0,
        };
        if (size != 0)
            relocations.Add((checked(sectionRva + relocation.Offset), size));
    }

    private static List<PeSectionLayout> CreateSectionLayouts(X86Program obj, int importSize)
    {
        var sections = new List<PeSectionLayout>();
        var referencedDataSections = new HashSet<string>(
            obj.Symbols
                .Where(static s => s.Binding != X86ObjectSymbolBinding.External 
                && s.Kind != X86ObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
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

        sections.Add(new PeSectionLayout(
            ".idata", X86ObjectSectionKind.Rodata, 4, new byte[importSize], importSize, SectionInitializedData | SectionRead | SectionWrite));
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

    private static void EncodeText(X86Program obj, PeSectionLayout text, IReadOnlyDictionary<string, ulong> symbols, ulong imageBase, List<(int Rva, int Size)>? baseRelocations)
    {
        var offset = 0;
        var instructionRelocations = baseRelocations is null ? null : new List<(int Offset, int Size)>();
        foreach (var instruction in obj.Text.Instructions)
        {
            instructionRelocations?.Clear();
            var encoded = X86CodeEncoder.Encode(instruction, obj.Target, checked(imageBase + (ulong)text.Rva + (ulong)offset), symbols, instructionRelocations);
            if (instructionRelocations is not null)
            {
                foreach (var relocation in instructionRelocations)
                    baseRelocations!.Add((checked(text.Rva + offset + relocation.Offset), relocation.Size));
            }
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

}
