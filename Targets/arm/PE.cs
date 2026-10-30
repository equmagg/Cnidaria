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
    public static ulong DefaultImageBase(ArmTarget target)
        => target is not null && target.Is64Bit ? 0x0000000140000000UL : 0x00400000UL;

    public static byte[] WriteExecutable(ArmProgram obj, ulong imageBase, IEnumerable<PeImport>? imports = null)
        => Write(obj, imageBase, imports, null, obj.EntrySymbol);

    public static byte[] WriteDynamicLibrary(ArmProgram obj, string libraryName, IEnumerable<PeImport>? imports = null, string? entrySymbol = null, ulong imageBase = 0)
    {
        if (!obj.Target.Is64Bit)
            throw new NotSupportedException("PE dynamic libraries require a 64-bit target");
        return Write(obj, imageBase == 0 ? 0x180000000UL : imageBase, imports, libraryName, entrySymbol ?? string.Empty);
    }

    private static byte[] Write(ArmProgram obj, ulong imageBase, IEnumerable<PeImport>? additionalImports, string? libraryName, string entrySymbol)
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
                throw new NotSupportedException("A PE data initializer cannot contain the address of imported data: " + relocation.SymbolName);
        }
        obj = ArmObjectLinker.RedirectDataImports(obj, dataImports.ToImmutableArray());
        var defined = obj.Text.Labels.Keys.Concat(obj.Symbols.Where(static symbol => symbol.Binding != ArmObjectSymbolBinding.External).Select(static symbol => symbol.Name)).ToHashSet(StringComparer.Ordinal);
        var references = obj.Symbols.Where(static symbol => symbol.Binding == ArmObjectSymbolBinding.External).Select(static symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
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

        var importTable = PeImportTable.Build(obj.Target.XLen / 8, imports, 0);
        var sections = CreateSectionLayouts(obj, importTable.Bytes.Length);
        PeSectionLayout? thunkSection = null;
        if (thunks.Length != 0)
        {
            if (!obj.Target.Is64Bit)
                throw new NotSupportedException("ARM import thunks require ARM64");
            thunkSection = new PeSectionLayout(".thunks", ArmObjectSectionKind.Text, 16, new byte[thunks.Length * 12], thunks.Length * 12, SectionCode | SectionRead | SectionExecute);
            sections.Add(thunkSection);
        }
        PeSectionLayout? exportsSection = null;
        PeSectionLayout? relocationsSection = null;
        var exports = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (libraryName is not null)
        {
            foreach (var symbol in obj.Symbols)
            {
                if (symbol.Binding == ArmObjectSymbolBinding.Global && symbol.Kind != ArmObjectSymbolKind.Section && !string.IsNullOrEmpty(symbol.SectionName))
                    exports.Add(symbol.Name, 0);
            }
            byte[] data = PeTables.Exports(libraryName, exports, 0);
            exportsSection = new PeSectionLayout(".edata", ArmObjectSectionKind.Rodata, 4, data, data.Length, SectionInitializedData | SectionRead);
            relocationsSection = new PeSectionLayout(".reloc", ArmObjectSectionKind.Rodata, 4, Array.Empty<byte>(), 0, SectionInitializedData | SectionRead | 0x02000000);
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
        var symbols = BuildSymbolAddressMap(obj, sections, imageBase, idata.Rva, importTable.AddressOffsets);
        var baseRelocations = libraryName is null ? null : new List<(int Rva, int Size)>();
        if (thunkSection is not null)
        {
            for (int i = 0; i < thunks.Length; i++)
            {
                int offset = i * 12;
                ulong address = checked(imageBase + (ulong)(thunkSection.Rva + offset));
                ulong slot = symbols[thunks[i].AddressSymbol];
                symbols.Add(thunks[i].SymbolName, address);
                long pages = checked(((long)slot >> 12) - ((long)address >> 12));
                if (pages < -(1 << 20) || pages >= 1 << 20)
                    throw new OverflowException("ARM64 import table is outside ADRP range");
                uint immediate = (uint)(pages & 0x1fffff);
                WriteUInt32(thunkSection.RawData, offset, 0x90000010u | ((immediate & 3) << 29) | ((immediate >> 2) << 5));
                WriteUInt32(thunkSection.RawData, offset + 4, 0xf9400210u | ((uint)(slot & 4095) >> 3) << 10);
                WriteUInt32(thunkSection.RawData, offset + 8, 0xd61f0200u);
            }
        }
        var text = sections.First(static section => section.Name == ".text");
        EncodeText(obj, text, symbols, imageBase);
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
        int sizeOfCode = sections.Where(static section => section.Kind == ArmObjectSectionKind.Text).Sum(static section => section.RawSize);
        int sizeOfInitializedData = sections.Where(static section => section.Kind is not (ArmObjectSectionKind.Text or ArmObjectSectionKind.Bss)).Sum(static section => section.RawSize);
        int sizeOfUninitializedData = sections.Where(static section => section.Kind == ArmObjectSectionKind.Bss).Sum(static section => AlignUp(section.VirtualSize, SectionAlignment));
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

    private static void RecordBaseRelocation(List<(int Rva, int Size)> relocations, int sectionRva, ArmObjectRelocation relocation, int pointerSize)
    {
        int size = relocation.Kind switch
        {
            ArmObjectRelocationKind.AbsolutePointer => pointerSize,
            ArmObjectRelocationKind.Absolute32 => 4,
            ArmObjectRelocationKind.Absolute64 => 8,
            _ => 0,
        };
        if (size != 0)
            relocations.Add((checked(sectionRva + relocation.Offset), size));
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
            ArmObjectRelocationKind.AArch64LoadLow12 => instruction.WithOperand1(ArmOperand.Memory(instruction.Operand1.BaseRegister, value & 0xfff, instruction.Operand1.Size)),
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

}
