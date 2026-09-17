using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria;

public enum ElfImageKind : byte
{
    Executable,
    DynamicExecutable,
    SharedObject,
}

public sealed class ElfImageOptions
{
    public ElfImageKind Kind { get; set; }
    public ulong ImageBase { get; set; }
    public string SoName { get; set; } = string.Empty;
    public string Interpreter { get; set; } = string.Empty;
    public ImmutableArray<string> Needed { get; set; } = ImmutableArray<string>.Empty;
    public string InitSymbol { get; set; } = string.Empty;
    public string FiniSymbol { get; set; } = string.Empty;

    /// <summary>Addresses the caller pins down itself, such as a routine the image is linked next to.</summary>
    public IReadOnlyDictionary<string, ulong>? ExternalSymbols { get; set; }

    public bool Shared => Kind == ElfImageKind.SharedObject;
    public bool Dynamic => Kind != ElfImageKind.Executable;
}

internal enum ElfSectionKind : byte
{
    Text,
    Rodata,
    Data,
    Bss,
}

internal sealed class ElfSectionInput
{
    public string Name { get; init; } = string.Empty;
    public ElfSectionKind Kind { get; init; }
    public int Alignment { get; init; } = 1;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public int MemorySize { get; init; }
}

internal sealed class ElfSymbolInput
{
    public string Name { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Size { get; init; }
    public bool IsFunction { get; init; }
    public bool IsGlobal { get; init; }
}

/// <summary>References that no section defines, split by whether they name code or data.</summary>
internal readonly struct ElfImportSet
{
    public ImmutableArray<string> Functions { get; }
    public ImmutableArray<string> Data { get; }

    private ElfImportSet(ImmutableArray<string> functions, ImmutableArray<string> data)
    {
        Functions = functions;
        Data = data;
    }

    public static ElfImportSet Empty => new ElfImportSet(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

    public static ElfImportSet Collect(
        ICollection<string> defined,
        IEnumerable<string> references,
        ICollection<string> dataSymbols)
    {
        var functions = ImmutableArray.CreateBuilder<string>();
        var data = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in references)
        {
            if (string.IsNullOrEmpty(name) || defined.Contains(name) || !seen.Add(name))
                continue;
            (dataSymbols.Contains(name) ? data : functions).Add(name);
        }
        return new ElfImportSet(functions.ToImmutable(), data.ToImmutable());
    }
}

internal sealed class ElfRelocationInput
{
    public string SectionName { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Size { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public long Addend { get; init; }
}

/// <summary>Everything a target contributes to a dynamic image beyond its plain section and symbol tables.</summary>
internal sealed class ElfTarget
{
    public ushort Machine { get; init; }
    public bool Is64Bit { get; init; }
    public TargetEndianness Endianness { get; init; }
    public uint Flags { get; init; }
    public int PltEntrySize { get; init; }
    public int ReservedGlobalOffsetTableEntries { get; init; }
    public uint AbsoluteRelocation { get; init; }
    public uint RelativeRelocation { get; init; }
    public uint JumpSlotRelocation { get; init; }
    public uint GlobalDataRelocation { get; init; }

    /// <summary>Writes one procedure linkage table entry that jumps through its global offset table slot.</summary>
    public Action<byte[], int, ulong, ulong> EmitPltEntry { get; init; } = static (_, _, _, _) => { };

    /// <summary>Encodes the text section once every symbol, including the imported ones, has an address.</summary>
    public Action<IReadOnlyDictionary<string, ulong>, byte[], int, ulong> EncodeText { get; init; } = static (_, _, _, _) => { };
}

internal sealed class ElfImageBuilder
{
    private const int PageAlignment = 0x1000;
    private const ushort EtExec = 2;
    private const ushort EtDyn = 3;
    private const uint EvCurrent = 1;

    private const uint PtLoad = 1;
    private const uint PtDynamic = 2;
    private const uint PtInterp = 3;
    private const uint PtPhdr = 6;
    private const uint PfX = 1;
    private const uint PfW = 2;
    private const uint PfR = 4;

    private const uint ShtProgbits = 1;
    private const uint ShtStrtab = 3;
    private const uint ShtRela = 4;
    private const uint ShtHash = 5;
    private const uint ShtDynamic = 6;
    private const uint ShtNobits = 8;
    private const uint ShtDynsym = 11;
    private const ulong ShfWrite = 1;
    private const ulong ShfAlloc = 2;
    private const ulong ShfExecInstr = 4;
    private const ulong ShfStrings = 0x20;
    private const ulong ShfInfoLink = 0x40;

    private const int DtNull = 0;
    private const int DtNeeded = 1;
    private const int DtPltRelSz = 2;
    private const int DtPltGot = 3;
    private const int DtHash = 4;
    private const int DtStrTab = 5;
    private const int DtSymTab = 6;
    private const int DtRela = 7;
    private const int DtRelaSz = 8;
    private const int DtRelaEnt = 9;
    private const int DtStrSz = 10;
    private const int DtSymEnt = 11;
    private const int DtInit = 12;
    private const int DtFini = 13;
    private const int DtSoName = 14;
    private const int DtPltRel = 20;
    private const int DtJmpRel = 23;
    private const int DtBindNow = 24;
    private const int DtFlags = 30;
    private const uint DfBindNow = 8;

    private readonly ElfTarget _target;
    private readonly ElfImageOptions _options;
    private readonly bool _is64;
    private readonly int _pointerSize;
    private readonly TargetEndianness _endianness;

    private readonly List<Section> _sections = new();
    private readonly List<Section> _programSections = new();
    private readonly Dictionary<string, Section> _sectionsByName = new(StringComparer.Ordinal);
    private readonly List<string> _functionImports = new();
    private readonly List<string> _dataImports = new();
    private readonly HashSet<string> _importNames = new(StringComparer.Ordinal);
    private readonly List<DynamicSymbol> _dynamicSymbols = new();
    private readonly Dictionary<string, int> _dynamicSymbolIndices = new(StringComparer.Ordinal);
    private readonly List<DataRelocation> _dataRelocations = new();
    private readonly List<DataRelocation> _dynamicDataRelocations = new();
    private readonly List<DynamicEntry> _dynamicEntries = new();
    private readonly Dictionary<string, ulong> _symbolAddresses = new(StringComparer.Ordinal);
    private readonly StringTable _dynstrBuilder = new();
    private readonly StringTable _shstrBuilder = new();

    private readonly ElfSymbolInput[] _exports;
    private readonly ElfRelocationInput[] _inputRelocations;
    private readonly string _entrySymbol;

    private Section _text = null!;
    private Section? _plt;
    private Section? _pltGot;
    private Section? _got;
    private Section? _dynsym;
    private Section? _dynstr;
    private Section? _hash;
    private Section? _relaDyn;
    private Section? _relaPlt;
    private Section? _dynamic;
    private Section? _interp;
    private Section _shstrtab = null!;

    public ElfImageBuilder(
        ElfTarget target,
        ElfImageOptions options,
        IEnumerable<ElfSectionInput> sections,
        IEnumerable<ElfSymbolInput> exports,
        IEnumerable<ElfRelocationInput> relocations,
        IEnumerable<string> functionImports,
        IEnumerable<string> dataImports,
        string entrySymbol)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _is64 = target.Is64Bit;
        _pointerSize = _is64 ? 8 : 4;
        _endianness = target.Endianness;
        _exports = exports.ToArray();
        _inputRelocations = relocations.ToArray();
        _functionImports.AddRange(functionImports);
        _dataImports.AddRange(dataImports);
        _importNames.UnionWith(_functionImports);
        _importNames.UnionWith(_dataImports);
        _entrySymbol = entrySymbol ?? string.Empty;

        foreach (var input in sections)
        {
            var section = new Section
            {
                Name = input.Name,
                Type = input.Kind == ElfSectionKind.Bss ? ShtNobits : ShtProgbits,
                Flags = input.Kind switch
                {
                    ElfSectionKind.Text => ShfAlloc | ShfExecInstr,
                    ElfSectionKind.Rodata => ShfAlloc,
                    _ => ShfAlloc | ShfWrite,
                },
                Alignment = Math.Max(1, input.Alignment),
                Data = input.Data,
                MemorySize = Math.Max(input.MemorySize, input.Data.Length),
                NoBits = input.Kind == ElfSectionKind.Bss,
            };
            if (input.Kind == ElfSectionKind.Text)
                _text = section;
            else
                _programSections.Add(section);
            _sectionsByName[section.Name] = section;
        }

        if (_text is null)
            throw new ArgumentException("A dynamic image needs a text section.", nameof(sections));
    }

    public static void Validate(ElfImageOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.Kind == ElfImageKind.DynamicExecutable && string.IsNullOrEmpty(options.Interpreter))
            throw new ArgumentException("A dynamically linked executable needs an interpreter path.", nameof(options));
        if (options.Shared && options.ImageBase != 0)
            throw new ArgumentException("A shared object is linked at address zero.", nameof(options));
        if (!options.Shared && options.ImageBase == 0)
            throw new ArgumentException("An executable needs a non-zero image base.", nameof(options));
        if (options.ImageBase % PageAlignment != 0)
            throw new ArgumentException("The image base must be page aligned.", nameof(options));
    }

    public byte[] Build()
    {
        if (!_options.Dynamic && (_functionImports.Count != 0 || _dataImports.Count != 0))
            throw new InvalidOperationException($"An executable cannot import {_functionImports.Concat(_dataImports).First()} without a dynamic section.");

        CollectDynamicSymbols();
        CollectDataRelocations();
        if (_options.Dynamic)
            CollectDynamicEntries();
        CreateSections();

        var segments = Layout();
        ResolveSymbolAddresses();
        ResolveDataRelocationValues();
        EmitPltCode();
        EmitGlobalOffsetTable();
        if (_options.Dynamic)
        {
            EmitDynamicSymbolTable();
            EmitRelocationTables();
            EmitDynamicSection();
        }

        var contentEnd = _sections.Where(static s => !s.NoBits).Max(static s => s.FileOffset + s.Data.Length);
        var sectionHeaderOffset = AlignUp(contentEnd, 8);
        var image = new byte[checked(sectionHeaderOffset + SectionHeaderSize * (_sections.Count + 1))];

        foreach (var section in _sections)
        {
            if (!section.NoBits && section.Data.Length != 0)
                Array.Copy(section.Data, 0, image, section.FileOffset, section.Data.Length);
        }

        _target.EncodeText(_symbolAddresses, image, _text.FileOffset, _text.Address);
        ApplyDataRelocations(image);
        WriteElfHeader(image, segments, sectionHeaderOffset);
        WriteProgramHeaders(image, segments);
        WriteSectionHeaders(image, sectionHeaderOffset);
        return image;
    }

    private int SymbolEntrySize => _is64 ? 24 : 16;
    private int RelaEntrySize => _is64 ? 24 : 12;
    private int DynamicEntrySize => _is64 ? 16 : 8;
    private int SectionHeaderSize => _is64 ? 64 : 40;
    private int ProgramHeaderSize => _is64 ? 56 : 32;
    private int ElfHeaderSize => _is64 ? 64 : 52;
    private int DynamicRelocationCount => _dynamicDataRelocations.Count + _dataImports.Count;

    private void CollectDynamicSymbols()
    {
        if (!_options.Dynamic)
            return;
        _dynamicSymbols.Add(new DynamicSymbol());

        foreach (var name in _functionImports)
        {
            _dynamicSymbolIndices[name] = _dynamicSymbols.Count;
            _dynamicSymbols.Add(new DynamicSymbol { Name = name, Info = 0x12, SectionIndex = 0 });
        }

        foreach (var name in _dataImports)
        {
            _dynamicSymbolIndices[name] = _dynamicSymbols.Count;
            _dynamicSymbols.Add(new DynamicSymbol { Name = name, Info = 0x11, SectionIndex = 0 });
        }

        if (!_options.Shared)
            return;

        foreach (var symbol in _exports)
        {
            if (!symbol.IsGlobal || string.IsNullOrEmpty(symbol.Name) || _dynamicSymbolIndices.ContainsKey(symbol.Name))
                continue;
            _dynamicSymbolIndices[symbol.Name] = _dynamicSymbols.Count;
            _dynamicSymbols.Add(new DynamicSymbol
            {
                Name = symbol.Name,
                Info = (byte)(0x10 | (symbol.IsFunction ? 2 : 1)),
                SectionIndex = -1,
                Size = symbol.Size,
            });
        }
    }

    private void CollectDataRelocations()
    {
        foreach (var relocation in _inputRelocations)
        {
            if (!_sectionsByName.TryGetValue(relocation.SectionName, out var layout))
                continue;
            if (layout.NoBits)
                throw new InvalidOperationException("A BSS section cannot carry relocations.");

            var entry = new DataRelocation
            {
                Section = layout,
                Offset = relocation.Offset,
                Size = relocation.Size,
                SymbolName = relocation.SymbolName,
                Addend = relocation.Addend,
            };
            _dataRelocations.Add(entry);

            if (_importNames.Contains(relocation.SymbolName))
            {
                entry.SymbolIndex = _dynamicSymbolIndices[relocation.SymbolName];
                _dynamicDataRelocations.Add(entry);
                continue;
            }

            if (!_options.Shared)
                continue;
            if (relocation.Size != _pointerSize)
                throw new NotSupportedException("A shared object cannot relocate a narrow absolute reference.");
            _dynamicDataRelocations.Add(entry);
        }
    }

    private void CollectDynamicEntries()
    {
        foreach (var name in _options.Needed)
            _dynamicEntries.Add(Entry(DtNeeded, DynamicValueKind.StringOffset, (ulong)_dynstrBuilder.Add(name)));
        if (_options.Shared && !string.IsNullOrEmpty(_options.SoName))
            _dynamicEntries.Add(Entry(DtSoName, DynamicValueKind.StringOffset, (ulong)_dynstrBuilder.Add(_options.SoName)));

        _dynamicEntries.Add(Entry(DtHash, DynamicValueKind.HashAddress, 0));
        _dynamicEntries.Add(Entry(DtStrTab, DynamicValueKind.StringTableAddress, 0));
        _dynamicEntries.Add(Entry(DtSymTab, DynamicValueKind.SymbolTableAddress, 0));
        _dynamicEntries.Add(Entry(DtSymEnt, DynamicValueKind.Immediate, (ulong)SymbolEntrySize));
        _dynamicEntries.Add(Entry(DtStrSz, DynamicValueKind.Immediate, 0));

        if (DynamicRelocationCount != 0)
        {
            _dynamicEntries.Add(Entry(DtRela, DynamicValueKind.RelaAddress, 0));
            _dynamicEntries.Add(Entry(DtRelaSz, DynamicValueKind.Immediate, 0));
            _dynamicEntries.Add(Entry(DtRelaEnt, DynamicValueKind.Immediate, (ulong)RelaEntrySize));
        }

        if (_functionImports.Count != 0)
        {
            _dynamicEntries.Add(Entry(DtPltGot, DynamicValueKind.PltGotAddress, 0));
            _dynamicEntries.Add(Entry(DtPltRel, DynamicValueKind.Immediate, DtRela));
            _dynamicEntries.Add(Entry(DtPltRelSz, DynamicValueKind.Immediate, 0));
            _dynamicEntries.Add(Entry(DtJmpRel, DynamicValueKind.JumpRelAddress, 0));
        }

        if (!string.IsNullOrEmpty(_options.InitSymbol))
            _dynamicEntries.Add(Entry(DtInit, DynamicValueKind.InitAddress, 0));
        if (!string.IsNullOrEmpty(_options.FiniSymbol))
            _dynamicEntries.Add(Entry(DtFini, DynamicValueKind.FiniAddress, 0));

        _dynamicEntries.Add(Entry(DtBindNow, DynamicValueKind.Immediate, 0));
        _dynamicEntries.Add(Entry(DtFlags, DynamicValueKind.Immediate, DfBindNow));
        _dynamicEntries.Add(Entry(DtNull, DynamicValueKind.Immediate, 0));
    }

    private static DynamicEntry Entry(int tag, DynamicValueKind kind, ulong immediate)
        => new() { Tag = tag, Kind = kind, Immediate = immediate };

    private void CreateSections()
    {
        foreach (var symbol in _dynamicSymbols)
            _dynstrBuilder.Add(symbol.Name);

        if (_options.Dynamic)
            CreateDynamicSections();

        _shstrtab = new Section
        {
            Name = ".shstrtab",
            Type = ShtStrtab,
            Flags = ShfStrings,
            Alignment = 1,
        };

        AddSection(_interp);
        AddSection(_hash);
        AddSection(_dynsym);
        AddSection(_dynstr);
        AddSection(_relaDyn);
        AddSection(_relaPlt);
        AddSection(_text);
        AddSection(_plt);
        foreach (var section in _programSections.Where(static s => (s.Flags & ShfWrite) == 0))
            AddSection(section);
        foreach (var section in _programSections.Where(static s => (s.Flags & ShfWrite) != 0 && !s.NoBits))
            AddSection(section);
        AddSection(_got);
        AddSection(_pltGot);
        AddSection(_dynamic);
        foreach (var section in _programSections.Where(static s => s.NoBits))
            AddSection(section);
        AddSection(_shstrtab);

        if (_options.Dynamic)
        {
            _dynsym!.Link = _dynstr!.Index;
            _hash!.Link = _dynsym.Index;
            _dynamic!.Link = _dynstr.Index;
            if (_relaDyn is not null)
                _relaDyn.Link = _dynsym.Index;
            if (_relaPlt is not null)
            {
                _relaPlt.Link = _dynsym.Index;
                _relaPlt.Info = _pltGot!.Index;
            }
        }

        foreach (var section in _sections)
            _shstrBuilder.Add(section.Name);
        _shstrtab.Data = _shstrBuilder.ToArray();
    }

    private void CreateDynamicSections()
    {
        if (_options.Kind == ElfImageKind.DynamicExecutable)
        {
            _interp = new Section
            {
                Name = ".interp",
                Type = ShtProgbits,
                Flags = ShfAlloc,
                Alignment = 1,
                Data = Encoding.UTF8.GetBytes(_options.Interpreter + "\0"),
            };
        }

        _hash = new Section
        {
            Name = ".hash",
            Type = ShtHash,
            Flags = ShfAlloc,
            Alignment = 4,
            Data = BuildHashTable(),
            EntrySize = 4,
        };

        _dynsym = new Section
        {
            Name = ".dynsym",
            Type = ShtDynsym,
            Flags = ShfAlloc,
            Alignment = _pointerSize,
            Data = new byte[_dynamicSymbols.Count * SymbolEntrySize],
            Info = 1,
            EntrySize = SymbolEntrySize,
        };

        _dynstr = new Section
        {
            Name = ".dynstr",
            Type = ShtStrtab,
            Flags = ShfAlloc | ShfStrings,
            Alignment = 1,
            Data = _dynstrBuilder.ToArray(),
        };

        if (DynamicRelocationCount != 0)
        {
            _relaDyn = new Section
            {
                Name = ".rela.dyn",
                Type = ShtRela,
                Flags = ShfAlloc,
                Alignment = _pointerSize,
                Data = new byte[DynamicRelocationCount * RelaEntrySize],
                EntrySize = RelaEntrySize,
            };
        }

        if (_functionImports.Count != 0)
        {
            _relaPlt = new Section
            {
                Name = ".rela.plt",
                Type = ShtRela,
                Flags = ShfAlloc | ShfInfoLink,
                Alignment = _pointerSize,
                Data = new byte[_functionImports.Count * RelaEntrySize],
                EntrySize = RelaEntrySize,
            };
            _plt = new Section
            {
                Name = ".plt",
                Type = ShtProgbits,
                Flags = ShfAlloc | ShfExecInstr,
                Alignment = Math.Max(4, _target.PltEntrySize),
                Data = new byte[_functionImports.Count * _target.PltEntrySize],
                EntrySize = _target.PltEntrySize,
            };
            _pltGot = new Section
            {
                Name = ".got.plt",
                Type = ShtProgbits,
                Flags = ShfAlloc | ShfWrite,
                Alignment = _pointerSize,
                Data = new byte[(_target.ReservedGlobalOffsetTableEntries + _functionImports.Count) * _pointerSize],
                EntrySize = _pointerSize,
            };
        }

        if (_dataImports.Count != 0)
        {
            _got = new Section
            {
                Name = ".got",
                Type = ShtProgbits,
                Flags = ShfAlloc | ShfWrite,
                Alignment = _pointerSize,
                Data = new byte[_dataImports.Count * _pointerSize],
                EntrySize = _pointerSize,
            };
        }

        _dynamic = new Section
        {
            Name = ".dynamic",
            Type = ShtDynamic,
            Flags = ShfAlloc | ShfWrite,
            Alignment = _pointerSize,
            Data = new byte[_dynamicEntries.Count * DynamicEntrySize],
            EntrySize = DynamicEntrySize,
        };

    }

    private void AddSection(Section? section)
    {
        if (section is null)
            return;
        section.Index = _sections.Count + 1;
        _sections.Add(section);
        if (section.MemorySize < section.Data.Length)
            section.MemorySize = section.Data.Length;
    }

    private byte[] BuildHashTable()
    {
        var count = _dynamicSymbols.Count;
        var buckets = Math.Max(1, count / 2);
        var bucket = new uint[buckets];
        var chain = new uint[count];
        for (var i = count - 1; i >= 1; i--)
        {
            var slot = ElfHash(_dynamicSymbols[i].Name) % (uint)buckets;
            chain[i] = bucket[slot];
            bucket[slot] = (uint)i;
        }

        var data = new byte[(2 + buckets + count) * 4];
        Write(data, 0, (uint)buckets, 4);
        Write(data, 4, (uint)count, 4);
        for (var i = 0; i < buckets; i++)
            Write(data, 8 + i * 4, bucket[i], 4);
        for (var i = 0; i < count; i++)
            Write(data, 8 + (buckets + i) * 4, chain[i], 4);
        return data;
    }

    private static uint ElfHash(string name)
    {
        uint hash = 0;
        foreach (var character in Encoding.UTF8.GetBytes(name ?? string.Empty))
        {
            hash = (hash << 4) + character;
            var high = hash & 0xf0000000u;
            if (high != 0)
                hash ^= high >> 24;
            hash &= ~high;
        }
        return hash;
    }

    private List<Segment> Layout()
    {
        var readOnly = _programSections.Where(static s => (s.Flags & ShfWrite) == 0).ToArray();
        var writable = _programSections.Any(static s => (s.Flags & ShfWrite) != 0) ||
            _got is not null || _pltGot is not null || _dynamic is not null;
        var loadCount = 1 + (readOnly.Length == 0 ? 0 : 1) + (writable ? 1 : 0);
        var headerCount = 1 + (_interp is null ? 0 : 1) + loadCount + (_options.Dynamic ? 1 : 0);
        var segments = new List<Segment>();
        var cursor = checked(ElfHeaderSize + ProgramHeaderSize * headerCount);

        segments.Add(new Segment(PtPhdr, ElfHeaderSize, Address(ElfHeaderSize),
            ProgramHeaderSize * headerCount, ProgramHeaderSize * headerCount, PfR, _pointerSize));
        if (_interp is not null)
            segments.Add(new Segment(PtInterp, 0, 0, 0, 0, PfR, 1));

        foreach (var section in _sections)
        {
            if (section == _shstrtab || _programSections.Contains(section) ||
                section == _got || section == _pltGot || section == _dynamic)
            {
                continue;
            }
            cursor = Place(section, cursor);
        }
        segments.Add(new Segment(PtLoad, 0, _options.ImageBase, cursor, cursor, PfR | PfX, PageAlignment));

        if (readOnly.Length != 0)
        {
            cursor = AlignUp(cursor, PageAlignment);
            var start = cursor;
            foreach (var section in readOnly)
                cursor = Place(section, cursor);
            segments.Add(new Segment(PtLoad, start, Address(start), cursor - start, cursor - start, PfR, PageAlignment));
        }

        if (!writable)
        {
            _shstrtab.FileOffset = cursor;
            if (segments.Count != headerCount)
                throw new InvalidOperationException("The ELF program header count does not match the layout.");
            return segments;
        }

        cursor = AlignUp(cursor, PageAlignment);
        var writableStart = cursor;
        foreach (var section in _programSections.Where(static s => (s.Flags & ShfWrite) != 0 && !s.NoBits))
            cursor = Place(section, cursor);
        if (_got is not null)
            cursor = Place(_got, cursor);
        if (_pltGot is not null)
            cursor = Place(_pltGot, cursor);
        if (_dynamic is not null)
            cursor = Place(_dynamic, cursor);
        var fileEnd = cursor;
        foreach (var section in _programSections.Where(static s => s.NoBits))
        {
            cursor = AlignUp(cursor, section.Alignment);
            section.FileOffset = fileEnd;
            section.Address = Address(cursor);
            cursor = checked(cursor + section.MemorySize);
        }
        segments.Add(new Segment(PtLoad, writableStart, Address(writableStart),
            fileEnd - writableStart, cursor - writableStart, PfR | PfW, PageAlignment));
        if (_dynamic is not null)
        {
            segments.Add(new Segment(PtDynamic, _dynamic.FileOffset, _dynamic.Address,
                _dynamic.Data.Length, _dynamic.Data.Length, PfR | PfW, _pointerSize));
        }

        _shstrtab.FileOffset = fileEnd;

        if (_interp is not null)
            segments[1] = new Segment(PtInterp, _interp.FileOffset, _interp.Address, _interp.Data.Length, _interp.Data.Length, PfR, 1);
        if (segments.Count != headerCount)
            throw new InvalidOperationException("The ELF program header count does not match the layout.");
        return segments;
    }

    private int Place(Section section, int cursor)
    {
        cursor = AlignUp(cursor, section.Alignment);
        section.FileOffset = cursor;
        section.Address = Address(cursor);
        return checked(cursor + section.MemorySize);
    }

    private ulong Address(int fileOffset)
        => checked(_options.ImageBase + (ulong)fileOffset);

    private void ResolveSymbolAddresses()
    {
        foreach (var symbol in _exports)
        {
            if (string.IsNullOrEmpty(symbol.SectionName))
                continue;
            if (!_sectionsByName.TryGetValue(symbol.SectionName, out var section))
                throw new InvalidOperationException($"Symbol section does not exist: {symbol.SectionName}");
            _symbolAddresses[symbol.Name] = checked(section.Address + (ulong)symbol.Offset);
        }

        for (var i = 0; i < _functionImports.Count; i++)
            _symbolAddresses[_functionImports[i]] = PltEntryAddress(i);
        for (var i = 0; i < _dataImports.Count; i++)
            _symbolAddresses[_dataImports[i]] = DataSlotAddress(i);

        if (_options.ExternalSymbols is null)
            return;
        foreach (var pair in _options.ExternalSymbols)
            _symbolAddresses[pair.Key] = pair.Value;
    }

    private ulong DataSlotAddress(int index)
        => checked(_got!.Address + (ulong)(index * _pointerSize));

    private ulong PltEntryAddress(int index)
        => checked(_plt!.Address + (ulong)(index * _target.PltEntrySize));

    private ulong PltGotEntryAddress(int index)
        => checked(_pltGot!.Address + (ulong)((_target.ReservedGlobalOffsetTableEntries + index) * _pointerSize));

    private void ResolveDataRelocationValues()
    {
        foreach (var relocation in _dataRelocations)
        {
            if (relocation.SymbolIndex >= 0)
                continue;
            if (!_symbolAddresses.TryGetValue(relocation.SymbolName, out var address))
                throw new InvalidOperationException($"Unresolved symbol: {relocation.SymbolName}");
            var resolved = checked((long)address + relocation.Addend);
            if (resolved < 0)
                throw new OverflowException("An absolute relocation resolved to a negative address.");
            relocation.Value = (ulong)resolved;
        }
    }

    private void EmitPltCode()
    {
        if (_plt is null)
            return;
        for (var i = 0; i < _functionImports.Count; i++)
        {
            _target.EmitPltEntry(_plt.Data, i * _target.PltEntrySize, PltEntryAddress(i), PltGotEntryAddress(i));
        }
    }

    private void EmitGlobalOffsetTable()
    {
        if (_pltGot is null || _target.ReservedGlobalOffsetTableEntries == 0)
            return;
        Write(_pltGot.Data, 0, _dynamic!.Address, _pointerSize);
    }

    private void EmitDynamicSymbolTable()
    {
        for (var i = 0; i < _dynamicSymbols.Count; i++)
        {
            var symbol = _dynamicSymbols[i];
            if (symbol.SectionIndex < 0)
            {
                if (!_symbolAddresses.TryGetValue(symbol.Name, out var address))
                    throw new InvalidOperationException($"Unresolved symbol: {symbol.Name}");
                symbol.Value = address;
                symbol.SectionIndex = SectionIndexOf(address);
            }

            var offset = i * SymbolEntrySize;
            var name = (ulong)_dynstrBuilder.Add(symbol.Name);
            if (_is64)
            {
                Write(_dynsym!.Data, offset + 0, name, 4);
                _dynsym!.Data[offset + 4] = symbol.Info;
                Write(_dynsym!.Data, offset + 6, (ulong)symbol.SectionIndex, 2);
                Write(_dynsym!.Data, offset + 8, symbol.Value, 8);
                Write(_dynsym!.Data, offset + 16, (ulong)symbol.Size, 8);
                continue;
            }

            Write(_dynsym!.Data, offset + 0, name, 4);
            Write(_dynsym!.Data, offset + 4, symbol.Value, 4);
            Write(_dynsym!.Data, offset + 8, (ulong)symbol.Size, 4);
            _dynsym!.Data[offset + 12] = symbol.Info;
            Write(_dynsym!.Data, offset + 14, (ulong)symbol.SectionIndex, 2);
        }
    }

    private int SectionIndexOf(ulong address)
    {
        foreach (var section in _sections)
        {
            if ((section.Flags & ShfAlloc) == 0 || section.MemorySize == 0)
                continue;
            if (address >= section.Address && address < checked(section.Address + (ulong)section.MemorySize))
                return section.Index;
        }
        return _text.Index;
    }

    private void EmitRelocationTables()
    {
        if (_relaPlt is not null)
        {
            for (var i = 0; i < _functionImports.Count; i++)
            {
                WriteRela(
                    _relaPlt.Data,
                    i * RelaEntrySize,
                    PltGotEntryAddress(i),
                    (ulong)_dynamicSymbolIndices[_functionImports[i]],
                    _target.JumpSlotRelocation,
                    0);
            }
        }

        if (_relaDyn is null)
            return;

        for (var i = 0; i < _dataImports.Count; i++)
        {
            WriteRela(
                _relaDyn.Data,
                i * RelaEntrySize,
                DataSlotAddress(i),
                (ulong)_dynamicSymbolIndices[_dataImports[i]],
                _target.GlobalDataRelocation,
                0);
        }

        for (var i = 0; i < _dynamicDataRelocations.Count; i++)
        {
            var slot = _dataImports.Count + i;
            var relocation = _dynamicDataRelocations[i];
            var address = checked(relocation.Section.Address + (ulong)relocation.Offset);
            if (relocation.SymbolIndex >= 0)
            {
                WriteRela(_relaDyn.Data, slot * RelaEntrySize, address,
                    (ulong)relocation.SymbolIndex, _target.AbsoluteRelocation, relocation.Addend);
                continue;
            }
            WriteRela(_relaDyn.Data, slot * RelaEntrySize, address, 0, _target.RelativeRelocation, (long)relocation.Value);
        }
    }

    private void WriteRela(byte[] destination, int offset, ulong address, ulong symbol, uint type, long addend)
    {
        if (_is64)
        {
            Write(destination, offset + 0, address, 8);
            Write(destination, offset + 8, (symbol << 32) | type, 8);
            Write(destination, offset + 16, (ulong)addend, 8);
            return;
        }

        Write(destination, offset + 0, address, 4);
        Write(destination, offset + 4, (symbol << 8) | (type & 0xff), 4);
        Write(destination, offset + 8, (ulong)addend, 4);
    }

    private void EmitDynamicSection()
    {
        for (var i = 0; i < _dynamicEntries.Count; i++)
        {
            var entry = _dynamicEntries[i];
            var value = entry.Kind switch
            {
                DynamicValueKind.Immediate or DynamicValueKind.StringOffset => entry.Immediate,
                DynamicValueKind.HashAddress => _hash!.Address,
                DynamicValueKind.StringTableAddress => _dynstr!.Address,
                DynamicValueKind.SymbolTableAddress => _dynsym!.Address,
                DynamicValueKind.RelaAddress => _relaDyn!.Address,
                DynamicValueKind.PltGotAddress => _pltGot!.Address,
                DynamicValueKind.JumpRelAddress => _relaPlt!.Address,
                DynamicValueKind.InitAddress => SymbolAddress(_options.InitSymbol),
                DynamicValueKind.FiniAddress => SymbolAddress(_options.FiniSymbol),
                _ => 0UL,
            };

            value = entry.Tag switch
            {
                DtStrSz => (ulong)_dynstr!.Data.Length,
                DtRelaSz => (ulong)(_relaDyn?.Data.Length ?? 0),
                DtPltRelSz => (ulong)(_relaPlt?.Data.Length ?? 0),
                _ => value,
            };

            var offset = i * DynamicEntrySize;
            Write(_dynamic!.Data, offset, (ulong)(long)entry.Tag, _pointerSize);
            Write(_dynamic!.Data, offset + _pointerSize, value, _pointerSize);
        }
    }

    private ulong SymbolAddress(string name)
    {
        if (_symbolAddresses.TryGetValue(name, out var address))
            return address;
        throw new InvalidOperationException($"Unresolved symbol: {name}");
    }

    private void ApplyDataRelocations(byte[] image)
    {
        foreach (var relocation in _dataRelocations)
        {
            var offset = checked(relocation.Section.FileOffset + relocation.Offset);
            var value = relocation.SymbolIndex >= 0 ? 0UL : relocation.Value;
            if (relocation.Size == 4 && value > uint.MaxValue)
                throw new OverflowException("An absolute 32-bit relocation overflowed.");
            Write(image, offset, value, relocation.Size);
        }
    }

    private void WriteElfHeader(byte[] image, List<Segment> segments, int sectionHeaderOffset)
    {
        image[0] = 0x7f;
        image[1] = (byte)'E';
        image[2] = (byte)'L';
        image[3] = (byte)'F';
        image[4] = _is64 ? (byte)2 : (byte)1;
        image[5] = _endianness == TargetEndianness.Little ? (byte)1 : (byte)2;
        image[6] = 1;

        var entry = _options.Shared || string.IsNullOrEmpty(_entrySymbol) ? 0UL : SymbolAddress(_entrySymbol);
        var type = _options.Shared ? EtDyn : EtExec;

        if (_is64)
        {
            Write(image, 16, type, 2);
            Write(image, 18, _target.Machine, 2);
            Write(image, 20, EvCurrent, 4);
            Write(image, 24, entry, 8);
            Write(image, 32, (ulong)ElfHeaderSize, 8);
            Write(image, 40, (ulong)sectionHeaderOffset, 8);
            Write(image, 48, _target.Flags, 4);
            Write(image, 52, (ulong)ElfHeaderSize, 2);
            Write(image, 54, (ulong)ProgramHeaderSize, 2);
            Write(image, 56, (ulong)segments.Count, 2);
            Write(image, 58, (ulong)SectionHeaderSize, 2);
            Write(image, 60, (ulong)(_sections.Count + 1), 2);
            Write(image, 62, (ulong)_shstrtab.Index, 2);
            return;
        }

        Write(image, 16, type, 2);
        Write(image, 18, _target.Machine, 2);
        Write(image, 20, EvCurrent, 4);
        Write(image, 24, entry, 4);
        Write(image, 28, (ulong)ElfHeaderSize, 4);
        Write(image, 32, (ulong)sectionHeaderOffset, 4);
        Write(image, 36, _target.Flags, 4);
        Write(image, 40, (ulong)ElfHeaderSize, 2);
        Write(image, 42, (ulong)ProgramHeaderSize, 2);
        Write(image, 44, (ulong)segments.Count, 2);
        Write(image, 46, (ulong)SectionHeaderSize, 2);
        Write(image, 48, (ulong)(_sections.Count + 1), 2);
        Write(image, 50, (ulong)_shstrtab.Index, 2);
    }

    private void WriteProgramHeaders(byte[] image, List<Segment> segments)
    {
        var offset = ElfHeaderSize;
        foreach (var segment in segments)
        {
            if (_is64)
            {
                Write(image, offset + 0, segment.Type, 4);
                Write(image, offset + 4, segment.Flags, 4);
                Write(image, offset + 8, (ulong)segment.FileOffset, 8);
                Write(image, offset + 16, segment.Address, 8);
                Write(image, offset + 24, segment.Address, 8);
                Write(image, offset + 32, (ulong)segment.FileSize, 8);
                Write(image, offset + 40, (ulong)segment.MemorySize, 8);
                Write(image, offset + 48, (ulong)segment.Alignment, 8);
            }
            else
            {
                Write(image, offset + 0, segment.Type, 4);
                Write(image, offset + 4, (ulong)segment.FileOffset, 4);
                Write(image, offset + 8, segment.Address, 4);
                Write(image, offset + 12, segment.Address, 4);
                Write(image, offset + 16, (ulong)segment.FileSize, 4);
                Write(image, offset + 20, (ulong)segment.MemorySize, 4);
                Write(image, offset + 24, segment.Flags, 4);
                Write(image, offset + 28, (ulong)segment.Alignment, 4);
            }
            offset += ProgramHeaderSize;
        }
    }

    private void WriteSectionHeaders(byte[] image, int sectionHeaderOffset)
    {
        var offset = sectionHeaderOffset + SectionHeaderSize;
        foreach (var section in _sections)
        {
            var name = (ulong)_shstrBuilder.Add(section.Name);
            var size = (ulong)(section.NoBits ? section.MemorySize : section.Data.Length);
            if (_is64)
            {
                Write(image, offset + 0, name, 4);
                Write(image, offset + 4, section.Type, 4);
                Write(image, offset + 8, section.Flags, 8);
                Write(image, offset + 16, section.Address, 8);
                Write(image, offset + 24, (ulong)section.FileOffset, 8);
                Write(image, offset + 32, size, 8);
                Write(image, offset + 40, (ulong)section.Link, 4);
                Write(image, offset + 44, (ulong)section.Info, 4);
                Write(image, offset + 48, (ulong)section.Alignment, 8);
                Write(image, offset + 56, (ulong)section.EntrySize, 8);
            }
            else
            {
                Write(image, offset + 0, name, 4);
                Write(image, offset + 4, section.Type, 4);
                Write(image, offset + 8, section.Flags, 4);
                Write(image, offset + 12, section.Address, 4);
                Write(image, offset + 16, (ulong)section.FileOffset, 4);
                Write(image, offset + 20, size, 4);
                Write(image, offset + 24, (ulong)section.Link, 4);
                Write(image, offset + 28, (ulong)section.Info, 4);
                Write(image, offset + 32, (ulong)section.Alignment, 4);
                Write(image, offset + 36, (ulong)section.EntrySize, 4);
            }
            offset += SectionHeaderSize;
        }
    }

    private void Write(byte[] image, int offset, ulong value, int size)
    {
        if (_endianness == TargetEndianness.Little)
        {
            for (var i = 0; i < size; i++)
                image[offset + i] = (byte)(value >> (i * 8));
            return;
        }

        for (var i = 0; i < size; i++)
            image[offset + i] = (byte)(value >> ((size - 1 - i) * 8));
    }

    private static int AlignUp(int value, int alignment)
    {
        alignment = Math.Max(1, alignment);
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private enum DynamicValueKind : byte
    {
        Immediate,
        StringOffset,
        HashAddress,
        StringTableAddress,
        SymbolTableAddress,
        RelaAddress,
        PltGotAddress,
        JumpRelAddress,
        InitAddress,
        FiniAddress,
    }

    private sealed class DynamicEntry
    {
        public int Tag;
        public DynamicValueKind Kind;
        public ulong Immediate;
    }

    private sealed class DynamicSymbol
    {
        public string Name = string.Empty;
        public byte Info;
        public int SectionIndex;
        public ulong Value;
        public int Size;
    }

    private sealed class DataRelocation
    {
        public Section Section = null!;
        public int Offset;
        public int Size;
        public string SymbolName = string.Empty;
        public long Addend;
        public ulong Value;
        public int SymbolIndex = -1;
    }

    private sealed class Section
    {
        public string Name = string.Empty;
        public uint Type;
        public ulong Flags;
        public int Alignment = 1;
        public byte[] Data = Array.Empty<byte>();
        public int MemorySize;
        public int FileOffset;
        public ulong Address;
        public int Link;
        public int Info;
        public int EntrySize;
        public bool NoBits;
        public int Index;
    }

    private sealed class StringTable
    {
        private readonly List<byte> _bytes = new() { 0 };
        private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);

        public int Add(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;
            if (_offsets.TryGetValue(value, out var offset))
                return offset;
            offset = _bytes.Count;
            _bytes.AddRange(Encoding.UTF8.GetBytes(value));
            _bytes.Add(0);
            _offsets.Add(value, offset);
            return offset;
        }

        public byte[] ToArray() => _bytes.ToArray();
    }

    private readonly struct Segment
    {
        public readonly uint Type;
        public readonly int FileOffset;
        public readonly ulong Address;
        public readonly int FileSize;
        public readonly int MemorySize;
        public readonly uint Flags;
        public readonly int Alignment;

        public Segment(uint type, int fileOffset, ulong address, int fileSize, int memorySize, uint flags, int alignment)
        {
            Type = type;
            FileOffset = fileOffset;
            Address = address;
            FileSize = fileSize;
            MemorySize = Math.Max(fileSize, memorySize);
            Flags = flags;
            Alignment = Math.Max(1, alignment);
        }
    }
}
