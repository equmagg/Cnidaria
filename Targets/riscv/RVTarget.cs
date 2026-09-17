using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.RiscV;


public sealed class RiscVProgram
{
    public RVTarget Target { get; }
    public RVTextSection Text { get; }
    public ImmutableArray<RVDataSection> DataSections { get; }
    public ImmutableArray<RVObjectSymbol> Symbols { get; }
    public string EntrySymbol { get; }

    public RiscVProgram(
        RVTarget machineTarget,
        RVTextSection text,
        ImmutableArray<RVDataSection> dataSections,
        ImmutableArray<RVObjectSymbol> symbols,
        string entrySymbol)
    {
        Target = machineTarget ?? throw new ArgumentNullException(nameof(machineTarget));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        DataSections = dataSections.IsDefault ? ImmutableArray<RVDataSection>.Empty : dataSections;
        Symbols = symbols.IsDefault ? ImmutableArray<RVObjectSymbol>.Empty : symbols;
        EntrySymbol = entrySymbol ?? string.Empty;
    }

    public RiscVProgram(
        RVTarget machineTarget,
        IEnumerable<RVInstruction> instructions,
        IReadOnlyDictionary<string, int>? textLabels = null,
        string entrySymbol = "")
        : this(
            machineTarget,
            new RVTextSection(instructions, textLabels, ImmutableArray<RVObjectRelocation>.Empty),
            ImmutableArray<RVDataSection>.Empty,
            ImmutableArray<RVObjectSymbol>.Empty,
            entrySymbol)
    {
    }

    public string FormatText(RiscVAssemblyWriterOptions? options = null)
        => RiscVDisassembler.Disassemble(this, options);

    public RiscVProgram Link(params RiscVProgram[] objects)
        => RiscVObjectComposer.Compose(this, objects);

    public RVLinkedImage LinkFlat(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => RVObjectLinker.LinkFlat(this, imageBase, externalSymbols);

    public byte[] ToExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Target.OperatingSystem switch
        {
            OperatingSystemKind.Linux => RiscVElfWriter.WriteExecutable(this, imageBase == 0 ? RiscVElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols),
            _ => LinkFlat(imageBase, externalSymbols).Bytes.ToArray(),
        };

    public byte[] ToLinuxDynamicExecutableBytes(
        IEnumerable<string>? needed = null,
        string interpreter = RiscVElfWriter.DefaultInterpreterPath,
        ulong imageBase = 0x10000)
        => RiscVElfWriter.WriteDynamicExecutable(this, imageBase, interpreter, needed);

    public byte[] ToSharedObjectBytes(string soName, IEnumerable<string>? needed = null, string? initSymbol = null)
        => RiscVElfWriter.WriteSharedObject(this, soName, needed, initSymbol);

    public byte[] ToLinuxExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => RiscVElfWriter.WriteExecutable(this, imageBase == 0 ? RiscVElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols);
}

public sealed class RVTextSection
{
    public ImmutableArray<RVInstruction> Instructions { get; }
    public ImmutableDictionary<string, int> Labels { get; }
    public ImmutableArray<RVObjectRelocation> Relocations { get; }

    public RVTextSection(
        IEnumerable<RVInstruction> instructions,
        IReadOnlyDictionary<string, int>? labels = null,
        ImmutableArray<RVObjectRelocation> relocations = default)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));

        Instructions = instructions.ToImmutableArray();
        Labels = labels is null
            ? ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)
            : labels.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Relocations = relocations.IsDefault ? ImmutableArray<RVObjectRelocation>.Empty : relocations;
    }

    public int SizeInBytes => RVInstructionTable.GetEncodedSize(Instructions);

    public int GetInstructionOffset(int index)
    {
        if ((uint)index > (uint)Instructions.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var offset = 0;
        for (var i = 0; i < index; i++)
            offset = checked(offset + RVInstructionTable.GetEncodedSize(Instructions[i].Opcode));
        return offset;
    }

    public byte[] Encode(RVTarget target)
        => RiscVCodeEncoder.Encode(Instructions, target, Labels);

    public string Format(RiscVAssemblyWriterOptions? options = null)
        => RiscVDisassembler.Disassemble(this, options);
}

public sealed class RVDataSection
{
    public string Name { get; }
    public RVObjectSectionKind Kind { get; }
    public int Alignment { get; }
    public ImmutableArray<byte> Data { get; }
    public int BssSize { get; }
    public ImmutableArray<RVObjectRelocation> Relocations { get; }

    public RVDataSection(
        string name,
        RVObjectSectionKind kind,
        int alignment,
        ImmutableArray<byte> data,
        int bssSize,
        ImmutableArray<RVObjectRelocation> relocations)
    {
        Name = string.IsNullOrWhiteSpace(name) ? ".data" : name;
        Kind = kind;
        Alignment = Math.Max(1, alignment);
        Data = data.IsDefault ? ImmutableArray<byte>.Empty : data;
        BssSize = Math.Max(0, bssSize);
        Relocations = relocations.IsDefault ? ImmutableArray<RVObjectRelocation>.Empty : relocations;
    }
}

public sealed class RVObjectSymbol
{
    public string Name { get; }
    public string SectionName { get; }
    public int Offset { get; }
    public int Size { get; }
    public RVObjectSymbolBinding Binding { get; }
    public RVObjectSymbolKind Kind { get; }
    public bool IsTentative { get; }

    public RVObjectSymbol(
        string name,
        string sectionName,
        int offset,
        int size,
        RVObjectSymbolBinding binding,
        RVObjectSymbolKind kind,
        bool isTentative = false)
    {
        Name = name ?? string.Empty;
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        Size = Math.Max(0, size);
        Binding = binding;
        Kind = kind;
        IsTentative = isTentative;
    }
}

public sealed class RVObjectRelocation
{
    public string SectionName { get; }
    public int Offset { get; }
    public string SymbolName { get; }
    public int Addend { get; }
    public RVObjectRelocationKind Kind { get; }

    public RVObjectRelocation(string sectionName, int offset, string symbolName, int addend, RVObjectRelocationKind kind)
    {
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        SymbolName = symbolName ?? string.Empty;
        Addend = addend;
        Kind = kind;
    }
}

public sealed class RVLinkedImage
{
    public RiscVProgram Source { get; }
    public ulong ImageBase { get; }
    public ulong EntryAddress { get; }
    public int EntryOffset { get; }
    public ImmutableDictionary<string, RVLinkedSection> Sections { get; }
    public ImmutableDictionary<string, ulong> SymbolAddresses { get; }
    public ImmutableArray<byte> Bytes { get; }

    public RVLinkedImage(
        RiscVProgram source,
        ulong imageBase,
        ulong entryAddress,
        int entryOffset,
        IReadOnlyDictionary<string, RVLinkedSection> sections,
        IReadOnlyDictionary<string, ulong> symbolAddresses,
        byte[] bytes)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ImageBase = imageBase;
        EntryAddress = entryAddress;
        EntryOffset = entryOffset;
        Sections = sections.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        SymbolAddresses = symbolAddresses.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Bytes = bytes is null ? ImmutableArray<byte>.Empty : bytes.ToImmutableArray();
    }

    public byte[] ToArray()
        => Bytes.ToArray();
}

public sealed class RVLinkedSection
{
    public string Name { get; }
    public RVObjectSectionKind Kind { get; }
    public int Offset { get; }
    public int Size { get; }
    public int Alignment { get; }
    public ulong Address { get; }

    public RVLinkedSection(string name, RVObjectSectionKind kind, int offset, int size, int alignment, ulong address)
    {
        Name = name ?? string.Empty;
        Kind = kind;
        Offset = Math.Max(0, offset);
        Size = Math.Max(0, size);
        Alignment = Math.Max(1, alignment);
        Address = address;
    }
}

public enum RVObjectSectionKind : byte
{
    Text,
    Rodata,
    Data,
    Bss,
}

public enum RVObjectSymbolBinding : byte
{
    Local,
    Global,
    External,
}

public enum RVObjectSymbolKind : byte
{
    None,
    Function,
    Object,
    Section,
}

public enum RVObjectRelocationKind : byte
{
    None,
    Branch12,
    Jal20,
    PcrelHi20,
    PcrelLo12I,
    PcrelLo12S,
    AbsolutePointer,
    Absolute32,
    Absolute64,
}

[Flags]
public enum RVIsaFlags : ulong
{
    None = 0,
    I = 1UL << 0,
    M = 1UL << 1,
    A = 1UL << 2,
    F = 1UL << 3,
    D = 1UL << 4,
    C = 1UL << 5,
    V = 1UL << 6,
    B = 1UL << 7,
    H = 1UL << 8,
    Zicsr = 1UL << 16,
    Zifencei = 1UL << 17,
    Zacas = 1UL << 18,
    Zaamo = 1UL << 19,
    Zalrsc = 1UL << 20,
    Zicond = 1UL << 21,
    Zicbom = 1UL << 22,
    Zicbop = 1UL << 23,
    Zicboz = 1UL << 24,
    Zawrs = 1UL << 25,
    Zfa = 1UL << 26,
    Zfhmin = 1UL << 27,
    Zihintpause = 1UL << 28,
    Zihintntl = 1UL << 29,
    Zimop = 1UL << 30,
    Zcmop = 1UL << 31,
    Zcb = 1UL << 33,
    Zcd = 1UL << 34,
    Zvbb = 1UL << 35,
    Svinval = 1UL << 36,
    Zicntr = 1UL << 37,
    Zihpm = 1UL << 38,
    Privileged = 1UL << 32,

    /// <summary>Every extension RVA23U64 and RVA23S64 make mandatory that adds instructions</summary>
    RVA23 = I | M | A | F | D | C | V | B | H |
            Zicsr | Zifencei | Zicntr | Zihpm |
            Zicbom | Zicbop | Zicboz | Zicond | Zawrs |
            Zfa | Zfhmin | Zihintpause | Zihintntl |
            Zimop | Zcmop | Zcb | Zcd | Zvbb | Svinval,
}

public enum RVAbiKind : byte
{
    Ilp32,
    Ilp32F,
    Ilp32D,
    Lp64,
    Lp64F,
    Lp64D,
}

public enum RVInstructionFormat : byte
{
    Raw,
    Raw16,
    Encoded,
}

public enum RVRelocationKind : byte
{
    None,
    RelativeBranch,
    RelativeJal,
    JalrLow12,
    AbsoluteLow12,
    AbsoluteUpper20
}
[Flags]
public enum RVInstructionFlags : byte
{
    None = 0,
    VectorUnmasked = 1 << 0,
    AtomicAcquire = 1 << 1,
    AtomicRelease = 1 << 2,
}
public enum RVInstrKind : ushort
{
    Invalid = 0,
    Raw32,
    Raw16,
    Lui,
    Auipc,
    Jal,
    Jalr,
    Beq,
    Bne,
    Blt,
    Bge,
    Bltu,
    Bgeu,
    Lb,
    Lh,
    Lw,
    Lbu,
    Lhu,
    Lwu,
    Ld,
    Sb,
    Sh,
    Sw,
    Sd,
    Flw,
    Fld,
    Fsw,
    Fsd,
    FaddS,
    FsubS,
    FmulS,
    FdivS,
    FaddD,
    FsubD,
    FmulD,
    FdivD,
    FsgnjS,
    FsgnjnS,
    FsgnjxS,
    FsgnjD,
    FsgnjnD,
    FsgnjxD,
    FeqS,
    FltS,
    FleS,
    FeqD,
    FltD,
    FleD,
    FcvtSW,
    FcvtSWu,
    FcvtSL,
    FcvtSLu,
    FcvtDW,
    FcvtDWu,
    FcvtDL,
    FcvtDLu,
    FcvtWS,
    FcvtWuS,
    FcvtLS,
    FcvtLuS,
    FcvtWD,
    FcvtWuD,
    FcvtLD,
    FcvtLuD,
    FcvtSD,
    FcvtDS,
    FmvXW,
    FmvWX,
    FmvXD,
    FmvDX,
    Addi,
    Slti,
    Sltiu,
    Xori,
    Ori,
    Andi,
    Slli,
    Srli,
    Srai,
    Add,
    Sub,
    Sll,
    Slt,
    Sltu,
    Xor,
    Srl,
    Sra,
    Or,
    And,
    Addiw,
    Slliw,
    Srliw,
    Sraiw,
    Addw,
    Subw,
    Sllw,
    Srlw,
    Sraw,
    Mul,
    Mulh,
    Mulhsu,
    Mulhu,
    Div,
    Divu,
    Rem,
    Remu,
    Mulw,
    Divw,
    Divuw,
    Remw,
    Remuw,
    LrW,
    ScW,
    AmoSwapW,
    AmoAddW,
    AmoXorW,
    AmoAndW,
    AmoOrW,
    AmoMinW,
    AmoMaxW,
    AmoMinuW,
    AmoMaxuW,
    LrD,
    ScD,
    AmoSwapD,
    AmoAddD,
    AmoXorD,
    AmoAndD,
    AmoOrD,
    AmoMinD,
    AmoMaxD,
    AmoMinuD,
    AmoMaxuD,
    AmocasW,
    AmocasD,
    AmocasQ,
    Fence,
    FenceI,
    Ecall,
    Ebreak,
    Uret,
    Sret,
    Mret,
    Wfi,
    SfenceVma,
    SinvalVma,
    SfenceWInval,
    SfenceInvalIr,
    HfenceVvma,
    HfenceGvma,
    Csrrw,
    Csrrs,
    Csrrc,
    Csrrwi,
    Csrrsi,
    Csrrci,
    Vsetvli,
    Vsetivli,
    Vsetvl,
    Vle8V,
    Vle16V,
    Vle32V,
    Vle64V,
    Vse8V,
    Vse16V,
    Vse32V,
    Vse64V,
    VaddVv,
    VaddVx,
    VaddVi,
    VsubVv,
    VsubVx,
    VrsubVx,
    VrsubVi,
    VandVv,
    VandVx,
    VandVi,
    VorVv,
    VorVx,
    VorVi,
    VxorVv,
    VxorVx,
    VxorVi,
    VmvVv,
    VmvXs,
    VmvSx,
    VmvVx,
    VmvVi,
    VmergeVvm,
    VmergeVxm,
    VmergeVim,
    VredsumVs,
    VredandVs,
    VredorVs,
    VredxorVs,
    VredminuVs,
    VredminVs,
    VredmaxuVs,
    VredmaxVs,
    VminuVv,
    VminuVx,
    VminVv,
    VminVx,
    VmaxuVv,
    VmaxuVx,
    VmaxVv,
    VmaxVx,
    VmseqVv,
    VmseqVx,
    VmseqVi,
    VmsneVv,
    VmsneVx,
    VmsneVi,
    VmsltuVv,
    VmsltuVx,
    VmsltVv,
    VmsltVx,
    VmsleuVv,
    VmsleuVx,
    VmsleuVi,
    VmsleVv,
    VmsleVx,
    VmsleVi,
    VmsgtuVx,
    VmsgtuVi,
    VmsgtVx,
    VmsgtVi,
    VsllVv,
    VsllVx,
    VsllVi,
    VsrlVv,
    VsrlVx,
    VsrlVi,
    VsraVv,
    VsraVx,
    VsraVi,
    VdivuVv,
    VdivuVx,
    VdivVv,
    VdivVx,
    VremuVv,
    VremuVx,
    VremVv,
    VremVx,
    VmulhuVv,
    VmulhuVx,
    VmulVv,
    VmulVx,
    VmulhsuVv,
    VmulhsuVx,
    VmulhVv,
    VmulhVx,
    VmaddVv,
    VmaddVx,
    VnmsubVv,
    VnmsubVx,
    VmaccVv,
    VmaccVx,
    VnmsacVv,
    VnmsacVx,
    VnsrlWv,
    VnsrlWx,
    VnsrlWi,
    VnsraWv,
    VnsraWx,
    VnsraWi,
    VrgatherVv,
    VrgatherVx,
    VrgatherVi,
    VfaddVv,
    VfaddVf,
    VfsubVv,
    VfsubVf,
    VfrsubVf,
    VfminVv,
    VfminVf,
    VfmaxVv,
    VfmaxVf,
    VfsgnjVv,
    VfsgnjVf,
    VfsgnjnVv,
    VfsgnjnVf,
    VfsgnjxVv,
    VfsgnjxVf,
    VmfeqVv,
    VmfeqVf,
    VmfleVv,
    VmfleVf,
    VmfltVv,
    VmfltVf,
    VmfneVv,
    VmfneVf,
    VmfgtVf,
    VmfgeVf,
    VfdivVv,
    VfdivVf,
    VfrdivVf,
    VfmulVv,
    VfmulVf,
    Andn,
    Orn,
    Xnor,
    Clz,
    Ctz,
    Cpop,
    Clzw,
    Ctzw,
    Cpopw,
    Max,
    Maxu,
    Min,
    Minu,
    SextB,
    SextH,
    ZextH,
    Rol,
    Ror,
    Rori,
    Rolw,
    Rorw,
    Roriw,
    OrcB,
    Rev8,
    Sh1Add,
    Sh2Add,
    Sh3Add,
    AddUw,
    Sh1AddUw,
    Sh2AddUw,
    Sh3AddUw,
    CzeroEqz,
    CzeroNez,
    SlliUw,
    Bclr,
    Bext,
    Binv,
    Bset,
    Bclri,
    Bexti,
    Binvi,
    Bseti,
    Clmul,
    Clmulr,
    Clmulh,
    CAddi4Spn,
    CFld,
    CLw,
    CFlw,
    CFsd,
    CSw,
    CFsw,
    CLd,
    CSd,
    CNop,
    CAddi,
    CJal,
    CAddiw,
    CLi,
    CAddi16Sp,
    CLui,
    CSrli,
    CSrai,
    CAndi,
    CSub,
    CXor,
    COr,
    CAnd,
    CSubw,
    CAddw,
    CJ,
    CBeqz,
    CBnez,
    CSlli,
    CLwSp,
    CLdSp,
    CJr,
    CMv,
    CEbreak,
    CJalr,
    CAdd,
    CSwSp,
    CSdSp,
    HlvB,
    HlvBu,
    HlvH,
    HlvHu,
    HlvxHu,
    HlvW,
    HlvWu,
    HlvxWu,
    HlvD,
    HsvB,
    HsvH,
    HsvW,
    HsvD,
    // <generated: RVA23 table-driven instructions>
    FmaddS,
    FmsubS,
    FnmsubS,
    FnmaddS,
    FsqrtS,
    FminS,
    FmaxS,
    FclassS,
    FmaddD,
    FmsubD,
    FnmsubD,
    FnmaddD,
    FsqrtD,
    FminD,
    FmaxD,
    FclassD,
    CFldsp,
    CFsdsp,
    CboClean,
    CboFlush,
    CboInval,
    CboZero,
    PrefetchI,
    PrefetchR,
    PrefetchW,
    WrsNto,
    WrsSto,
    FliS,
    FminmS,
    FmaxmS,
    FroundS,
    FroundnxS,
    FleqS,
    FltqS,
    FliD,
    FminmD,
    FmaxmD,
    FroundD,
    FroundnxD,
    FcvtmodWD,
    FleqD,
    FltqD,
    Flh,
    Fsh,
    FcvtSH,
    FcvtHS,
    FmvXH,
    FmvHX,
    FcvtDH,
    FcvtHD,
    NtlP1,
    NtlPall,
    NtlS1,
    NtlAll,
    CNtlP1,
    CNtlPall,
    CNtlS1,
    CNtlAll,
    MopR0,
    MopR1,
    MopR2,
    MopR3,
    MopR4,
    MopR5,
    MopR6,
    MopR7,
    MopR8,
    MopR9,
    MopR10,
    MopR11,
    MopR12,
    MopR13,
    MopR14,
    MopR15,
    MopR16,
    MopR17,
    MopR18,
    MopR19,
    MopR20,
    MopR21,
    MopR22,
    MopR23,
    MopR24,
    MopR25,
    MopR26,
    MopR27,
    MopR28,
    MopR29,
    MopR30,
    MopR31,
    MopRr0,
    MopRr1,
    MopRr2,
    MopRr3,
    MopRr4,
    MopRr5,
    MopRr6,
    MopRr7,
    CMop1,
    CMop3,
    CMop5,
    CMop7,
    CMop9,
    CMop11,
    CMop13,
    CMop15,
    CLbu,
    CLhu,
    CLh,
    CSb,
    CSh,
    CZextB,
    CSextB,
    CZextH,
    CSextH,
    CNot,
    CMul,
    CZextW,
    VlmV,
    VsmV,
    Vluxei8V,
    Vluxei16V,
    Vluxei32V,
    Vluxei64V,
    Vsuxei8V,
    Vsuxei16V,
    Vsuxei32V,
    Vsuxei64V,
    Vlse8V,
    Vlse16V,
    Vlse32V,
    Vlse64V,
    Vsse8V,
    Vsse16V,
    Vsse32V,
    Vsse64V,
    Vloxei8V,
    Vloxei16V,
    Vloxei32V,
    Vloxei64V,
    Vsoxei8V,
    Vsoxei16V,
    Vsoxei32V,
    Vsoxei64V,
    Vle8ffV,
    Vle16ffV,
    Vle32ffV,
    Vle64ffV,
    Vl1re8V,
    Vl1re16V,
    Vl1re32V,
    Vl1re64V,
    Vl2re8V,
    Vl2re16V,
    Vl2re32V,
    Vl2re64V,
    Vl4re8V,
    Vl4re16V,
    Vl4re32V,
    Vl4re64V,
    Vl8re8V,
    Vl8re16V,
    Vl8re32V,
    Vl8re64V,
    Vs1rV,
    Vs2rV,
    Vs4rV,
    Vs8rV,
    Vfslide1upVf,
    Vfslide1downVf,
    VfmvSF,
    VfmergeVfm,
    VfmvVF,
    VfmaddVf,
    VfnmaddVf,
    VfmsubVf,
    VfnmsubVf,
    VfmaccVf,
    VfnmaccVf,
    VfmsacVf,
    VfnmsacVf,
    VfwaddVf,
    VfwsubVf,
    VfwaddWf,
    VfwsubWf,
    VfwmulVf,
    VfwmaccVf,
    VfwnmaccVf,
    VfwmsacVf,
    VfwnmsacVf,
    VfredusumVs,
    VfredosumVs,
    VfredminVs,
    VfredmaxVs,
    VfmvFS,
    VfmaddVv,
    VfnmaddVv,
    VfmsubVv,
    VfnmsubVv,
    VfmaccVv,
    VfnmaccVv,
    VfmsacVv,
    VfnmsacVv,
    VfcvtXuFV,
    VfcvtXFV,
    VfcvtFXuV,
    VfcvtFXV,
    VfcvtRtzXuFV,
    VfcvtRtzXFV,
    VfwcvtXuFV,
    VfwcvtXFV,
    VfwcvtFXuV,
    VfwcvtFXV,
    VfwcvtFFV,
    VfwcvtRtzXuFV,
    VfwcvtRtzXFV,
    VfncvtXuFW,
    VfncvtXFW,
    VfncvtFXuW,
    VfncvtFXW,
    VfncvtFFW,
    VfncvtRodFFW,
    VfncvtRtzXuFW,
    VfncvtRtzXFW,
    VfsqrtV,
    Vfrsqrt7V,
    Vfrec7V,
    VfclassV,
    VfwaddVv,
    VfwredusumVs,
    VfwsubVv,
    VfwredosumVs,
    VfwaddWv,
    VfwsubWv,
    VfwmulVv,
    VfwmaccVv,
    VfwnmaccVv,
    VfwmsacVv,
    VfwnmsacVv,
    VslideupVx,
    VslidedownVx,
    VadcVxm,
    VmadcVxm,
    VmadcVx,
    VsbcVxm,
    VmsbcVxm,
    VmsbcVx,
    VsadduVx,
    VsaddVx,
    VssubuVx,
    VssubVx,
    VsmulVx,
    VssrlVx,
    VssraVx,
    VnclipuWx,
    VnclipWx,
    Vrgatherei16Vv,
    VadcVvm,
    VmadcVvm,
    VmadcVv,
    VsbcVvm,
    VmsbcVvm,
    VmsbcVv,
    VsadduVv,
    VsaddVv,
    VssubuVv,
    VssubVv,
    VsmulVv,
    VssrlVv,
    VssraVv,
    VnclipuWv,
    VnclipWv,
    VwredsumuVs,
    VwredsumVs,
    VslideupVi,
    VslidedownVi,
    VadcVim,
    VmadcVim,
    VmadcVi,
    VsadduVi,
    VsaddVi,
    Vmv1rV,
    Vmv2rV,
    Vmv4rV,
    Vmv8rV,
    VssrlVi,
    VssraVi,
    VnclipuWi,
    VnclipWi,
    VaadduVv,
    VaaddVv,
    VasubuVv,
    VasubVv,
    VzextVf8,
    VsextVf8,
    VzextVf4,
    VsextVf4,
    VzextVf2,
    VsextVf2,
    VcompressVm,
    VmandnMm,
    VmandMm,
    VmorMm,
    VmxorMm,
    VmornMm,
    VmnandMm,
    VmnorMm,
    VmxnorMm,
    VmsbfM,
    VmsofM,
    VmsifM,
    ViotaM,
    VidV,
    VcpopM,
    VfirstM,
    VwadduVv,
    VwaddVv,
    VwsubuVv,
    VwsubVv,
    VwadduWv,
    VwaddWv,
    VwsubuWv,
    VwsubWv,
    VwmuluVv,
    VwmulsuVv,
    VwmulVv,
    VwmaccuVv,
    VwmaccVv,
    VwmaccsuVv,
    VaadduVx,
    VaaddVx,
    VasubuVx,
    VasubVx,
    Vslide1upVx,
    Vslide1downVx,
    VwadduVx,
    VwaddVx,
    VwsubuVx,
    VwsubVx,
    VwadduWx,
    VwaddWx,
    VwsubuWx,
    VwsubWx,
    VwmuluVx,
    VwmulsuVx,
    VwmulVx,
    VwmaccuVx,
    VwmaccVx,
    VwmaccusVx,
    VwmaccsuVx,
    VandnVv,
    VandnVx,
    VbrevV,
    Vbrev8V,
    Vrev8V,
    VclzV,
    VctzV,
    VcpopV,
    VrolVv,
    VrolVx,
    VrorVv,
    VrorVx,
    VrorVi,
    VwsllVv,
    VwsllVx,
    VwsllVi,
    HinvalVvma,
    HinvalGvma,
    Pause,
    Vlseg2e8V,
    Vlseg2e16V,
    Vlseg2e32V,
    Vlseg2e64V,
    Vlseg3e8V,
    Vlseg3e16V,
    Vlseg3e32V,
    Vlseg3e64V,
    Vlseg4e8V,
    Vlseg4e16V,
    Vlseg4e32V,
    Vlseg4e64V,
    Vlseg5e8V,
    Vlseg5e16V,
    Vlseg5e32V,
    Vlseg5e64V,
    Vlseg6e8V,
    Vlseg6e16V,
    Vlseg6e32V,
    Vlseg6e64V,
    Vlseg7e8V,
    Vlseg7e16V,
    Vlseg7e32V,
    Vlseg7e64V,
    Vlseg8e8V,
    Vlseg8e16V,
    Vlseg8e32V,
    Vlseg8e64V,
    Vsseg2e8V,
    Vsseg2e16V,
    Vsseg2e32V,
    Vsseg2e64V,
    Vsseg3e8V,
    Vsseg3e16V,
    Vsseg3e32V,
    Vsseg3e64V,
    Vsseg4e8V,
    Vsseg4e16V,
    Vsseg4e32V,
    Vsseg4e64V,
    Vsseg5e8V,
    Vsseg5e16V,
    Vsseg5e32V,
    Vsseg5e64V,
    Vsseg6e8V,
    Vsseg6e16V,
    Vsseg6e32V,
    Vsseg6e64V,
    Vsseg7e8V,
    Vsseg7e16V,
    Vsseg7e32V,
    Vsseg7e64V,
    Vsseg8e8V,
    Vsseg8e16V,
    Vsseg8e32V,
    Vsseg8e64V,
    Vlseg2e8ffV,
    Vlseg2e16ffV,
    Vlseg2e32ffV,
    Vlseg2e64ffV,
    Vlseg3e8ffV,
    Vlseg3e16ffV,
    Vlseg3e32ffV,
    Vlseg3e64ffV,
    Vlseg4e8ffV,
    Vlseg4e16ffV,
    Vlseg4e32ffV,
    Vlseg4e64ffV,
    Vlseg5e8ffV,
    Vlseg5e16ffV,
    Vlseg5e32ffV,
    Vlseg5e64ffV,
    Vlseg6e8ffV,
    Vlseg6e16ffV,
    Vlseg6e32ffV,
    Vlseg6e64ffV,
    Vlseg7e8ffV,
    Vlseg7e16ffV,
    Vlseg7e32ffV,
    Vlseg7e64ffV,
    Vlseg8e8ffV,
    Vlseg8e16ffV,
    Vlseg8e32ffV,
    Vlseg8e64ffV,
    Vlsseg2e8V,
    Vlsseg2e16V,
    Vlsseg2e32V,
    Vlsseg2e64V,
    Vlsseg3e8V,
    Vlsseg3e16V,
    Vlsseg3e32V,
    Vlsseg3e64V,
    Vlsseg4e8V,
    Vlsseg4e16V,
    Vlsseg4e32V,
    Vlsseg4e64V,
    Vlsseg5e8V,
    Vlsseg5e16V,
    Vlsseg5e32V,
    Vlsseg5e64V,
    Vlsseg6e8V,
    Vlsseg6e16V,
    Vlsseg6e32V,
    Vlsseg6e64V,
    Vlsseg7e8V,
    Vlsseg7e16V,
    Vlsseg7e32V,
    Vlsseg7e64V,
    Vlsseg8e8V,
    Vlsseg8e16V,
    Vlsseg8e32V,
    Vlsseg8e64V,
    Vssseg2e8V,
    Vssseg2e16V,
    Vssseg2e32V,
    Vssseg2e64V,
    Vssseg3e8V,
    Vssseg3e16V,
    Vssseg3e32V,
    Vssseg3e64V,
    Vssseg4e8V,
    Vssseg4e16V,
    Vssseg4e32V,
    Vssseg4e64V,
    Vssseg5e8V,
    Vssseg5e16V,
    Vssseg5e32V,
    Vssseg5e64V,
    Vssseg6e8V,
    Vssseg6e16V,
    Vssseg6e32V,
    Vssseg6e64V,
    Vssseg7e8V,
    Vssseg7e16V,
    Vssseg7e32V,
    Vssseg7e64V,
    Vssseg8e8V,
    Vssseg8e16V,
    Vssseg8e32V,
    Vssseg8e64V,
    Vluxseg2ei8V,
    Vluxseg2ei16V,
    Vluxseg2ei32V,
    Vluxseg2ei64V,
    Vluxseg3ei8V,
    Vluxseg3ei16V,
    Vluxseg3ei32V,
    Vluxseg3ei64V,
    Vluxseg4ei8V,
    Vluxseg4ei16V,
    Vluxseg4ei32V,
    Vluxseg4ei64V,
    Vluxseg5ei8V,
    Vluxseg5ei16V,
    Vluxseg5ei32V,
    Vluxseg5ei64V,
    Vluxseg6ei8V,
    Vluxseg6ei16V,
    Vluxseg6ei32V,
    Vluxseg6ei64V,
    Vluxseg7ei8V,
    Vluxseg7ei16V,
    Vluxseg7ei32V,
    Vluxseg7ei64V,
    Vluxseg8ei8V,
    Vluxseg8ei16V,
    Vluxseg8ei32V,
    Vluxseg8ei64V,
    Vloxseg2ei8V,
    Vloxseg2ei16V,
    Vloxseg2ei32V,
    Vloxseg2ei64V,
    Vloxseg3ei8V,
    Vloxseg3ei16V,
    Vloxseg3ei32V,
    Vloxseg3ei64V,
    Vloxseg4ei8V,
    Vloxseg4ei16V,
    Vloxseg4ei32V,
    Vloxseg4ei64V,
    Vloxseg5ei8V,
    Vloxseg5ei16V,
    Vloxseg5ei32V,
    Vloxseg5ei64V,
    Vloxseg6ei8V,
    Vloxseg6ei16V,
    Vloxseg6ei32V,
    Vloxseg6ei64V,
    Vloxseg7ei8V,
    Vloxseg7ei16V,
    Vloxseg7ei32V,
    Vloxseg7ei64V,
    Vloxseg8ei8V,
    Vloxseg8ei16V,
    Vloxseg8ei32V,
    Vloxseg8ei64V,
    Vsuxseg2ei8V,
    Vsuxseg2ei16V,
    Vsuxseg2ei32V,
    Vsuxseg2ei64V,
    Vsuxseg3ei8V,
    Vsuxseg3ei16V,
    Vsuxseg3ei32V,
    Vsuxseg3ei64V,
    Vsuxseg4ei8V,
    Vsuxseg4ei16V,
    Vsuxseg4ei32V,
    Vsuxseg4ei64V,
    Vsuxseg5ei8V,
    Vsuxseg5ei16V,
    Vsuxseg5ei32V,
    Vsuxseg5ei64V,
    Vsuxseg6ei8V,
    Vsuxseg6ei16V,
    Vsuxseg6ei32V,
    Vsuxseg6ei64V,
    Vsuxseg7ei8V,
    Vsuxseg7ei16V,
    Vsuxseg7ei32V,
    Vsuxseg7ei64V,
    Vsuxseg8ei8V,
    Vsuxseg8ei16V,
    Vsuxseg8ei32V,
    Vsuxseg8ei64V,
    Vsoxseg2ei8V,
    Vsoxseg2ei16V,
    Vsoxseg2ei32V,
    Vsoxseg2ei64V,
    Vsoxseg3ei8V,
    Vsoxseg3ei16V,
    Vsoxseg3ei32V,
    Vsoxseg3ei64V,
    Vsoxseg4ei8V,
    Vsoxseg4ei16V,
    Vsoxseg4ei32V,
    Vsoxseg4ei64V,
    Vsoxseg5ei8V,
    Vsoxseg5ei16V,
    Vsoxseg5ei32V,
    Vsoxseg5ei64V,
    Vsoxseg6ei8V,
    Vsoxseg6ei16V,
    Vsoxseg6ei32V,
    Vsoxseg6ei64V,
    Vsoxseg7ei8V,
    Vsoxseg7ei16V,
    Vsoxseg7ei32V,
    Vsoxseg7ei64V,
    Vsoxseg8ei8V,
    Vsoxseg8ei16V,
    Vsoxseg8ei32V,
    Vsoxseg8ei64V,
    // </generated>
}

public static class RVVector
{
    /// <summary>The widest vector register the backend spills and the emulator provides.</summary>
    public const int LengthBits = 512;
    public const int RegisterBytes = LengthBits / 8;
}

public enum RVRegister : byte
{
    X0 = 0,
    X1 = 1,
    X2 = 2,
    X3 = 3,
    X4 = 4,
    X5 = 5,
    X6 = 6,
    X7 = 7,
    X8 = 8,
    X9 = 9,
    X10 = 10,
    X11 = 11,
    X12 = 12,
    X13 = 13,
    X14 = 14,
    X15 = 15,
    X16 = 16,
    X17 = 17,
    X18 = 18,
    X19 = 19,
    X20 = 20,
    X21 = 21,
    X22 = 22,
    X23 = 23,
    X24 = 24,
    X25 = 25,
    X26 = 26,
    X27 = 27,
    X28 = 28,
    X29 = 29,
    X30 = 30,
    X31 = 31,
    F0 = 32,
    F1 = 33,
    F2 = 34,
    F3 = 35,
    F4 = 36,
    F5 = 37,
    F6 = 38,
    F7 = 39,
    F8 = 40,
    F9 = 41,
    F10 = 42,
    F11 = 43,
    F12 = 44,
    F13 = 45,
    F14 = 46,
    F15 = 47,
    F16 = 48,
    F17 = 49,
    F18 = 50,
    F19 = 51,
    F20 = 52,
    F21 = 53,
    F22 = 54,
    F23 = 55,
    F24 = 56,
    F25 = 57,
    F26 = 58,
    F27 = 59,
    F28 = 60,
    F29 = 61,
    F30 = 62,
    F31 = 63,
    V0 = 64,
    V1 = 65,
    V2 = 66,
    V3 = 67,
    V4 = 68,
    V5 = 69,
    V6 = 70,
    V7 = 71,
    V8 = 72,
    V9 = 73,
    V10 = 74,
    V11 = 75,
    V12 = 76,
    V13 = 77,
    V14 = 78,
    V15 = 79,
    V16 = 80,
    V17 = 81,
    V18 = 82,
    V19 = 83,
    V20 = 84,
    V21 = 85,
    V22 = 86,
    V23 = 87,
    V24 = 88,
    V25 = 89,
    V26 = 90,
    V27 = 91,
    V28 = 92,
    V29 = 93,
    V30 = 94,
    V31 = 95,
    Invalid = 255,
}

internal enum RVCsr : ushort
{
    FFlags = 0x001,
    FRm = 0x002,
    FCsr = 0x003,
    VStart = 0x008,
    VxSat = 0x009,
    VxRm = 0x00A,
    VCsr = 0x00F,
    Seed = 0x015,
    Jvt = 0x017,
    SStatus = 0x100,
    SIe = 0x104,
    STVec = 0x105,
    SCounterEn = 0x106,
    SEnvCfg = 0x10A,
    SScratch = 0x140,
    SEpc = 0x141,
    SCause = 0x142,
    STVal = 0x143,
    SIp = 0x144,
    STimeCmp = 0x14D,
    SCountOvf = 0xDA0,
    SAtp = 0x180,
    SContext = 0x5A8,
    HStatus = 0x600,
    HEDeleg = 0x602,
    HIDeleg = 0x603,
    HIe = 0x604,
    HTimeDelta = 0x605,
    HCounterEn = 0x606,
    HGEIe = 0x607,
    HTVal = 0x643,
    HIp = 0x644,
    HVIp = 0x645,
    HGEIp = 0xE12,
    HEnvCfg = 0x60A,
    HTimeDeltaH = 0x615,
    HEnvCfgH = 0x61A,
    HStateEn0 = 0x60C,
    HStateEn1 = 0x60D,
    HStateEn2 = 0x60E,
    HStateEn3 = 0x60F,
    HStateEn0H = 0x61C,
    HStateEn1H = 0x61D,
    HStateEn2H = 0x61E,
    HStateEn3H = 0x61F,
    HTInst = 0x64A,
    HGEAtp = 0x680,
    VStartVs = 0x208,
    VsStatus = 0x200,
    VsIe = 0x204,
    VsTVec = 0x205,
    VsScratch = 0x240,
    VsEpc = 0x241,
    VsCause = 0x242,
    VsTVal = 0x243,
    VsIp = 0x244,
    VsAtp = 0x280,
    MVendorId = 0xF11,
    MArchId = 0xF12,
    MImpId = 0xF13,
    MHartId = 0xF14,
    MConfigPtr = 0xF15,
    MStatus = 0x300,
    MIsa = 0x301,
    MEDeleg = 0x302,
    MIDeleg = 0x303,
    MIe = 0x304,
    MTVec = 0x305,
    MCounterEn = 0x306,
    MStatusH = 0x310,
    MScratch = 0x340,
    MEpc = 0x341,
    MCause = 0x342,
    MTVal = 0x343,
    MIp = 0x344,
    MTInst = 0x34A,
    MTVal2 = 0x34B,
    MEnvCfg = 0x30A,
    MEnvCfgH = 0x31A,
    MSecCfg = 0x747,
    MSecCfgH = 0x757,
    MCycle = 0xB00,
    MInstRet = 0xB02,
    Cycle = 0xC00,
    Time = 0xC01,
    InstRet = 0xC02,
    CycleH = 0xC80,
    TimeH = 0xC81,
    InstRetH = 0xC82,
    VL = 0xC20,
    VType = 0xC21,
    VLenB = 0xC22,
    Dcsr = 0x7B0,
    Dpc = 0x7B1,
    DScratch0 = 0x7B2,
    DScratch1 = 0x7B3,
}

public sealed class RVTarget
{
    public static RVTarget Rv32I { get; } = new RVTarget(32, RVAbiKind.Ilp32, RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei, TargetEndianness.Little);
    public static RVTarget Rv64I { get; } = new RVTarget(64, RVAbiKind.Lp64, RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei, TargetEndianness.Little);
    public static RVTarget Rv64G { get; } = new RVTarget(64, RVAbiKind.Lp64D, RVIsaFlags.I | RVIsaFlags.M | RVIsaFlags.A | RVIsaFlags.F | RVIsaFlags.D
        | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei, TargetEndianness.Little);
    public static RVTarget Rv64GPrivileged { get; } = new RVTarget(64, RVAbiKind.Lp64D, RVIsaFlags.I | RVIsaFlags.M | RVIsaFlags.A | RVIsaFlags.F | RVIsaFlags.D
        | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei | RVIsaFlags.Privileged, TargetEndianness.Little);
    public static RVTarget Rv64GVPrivileged { get; } = new RVTarget(64, RVAbiKind.Lp64D, RVIsaFlags.I | RVIsaFlags.M | RVIsaFlags.A | RVIsaFlags.F | RVIsaFlags.D
        | RVIsaFlags.V | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei | RVIsaFlags.Privileged, TargetEndianness.Little);
    public static RVTarget Rv64GHPrivileged { get; } = new RVTarget(64, RVAbiKind.Lp64D, RVIsaFlags.I | RVIsaFlags.M | RVIsaFlags.A | RVIsaFlags.F | RVIsaFlags.D
        | RVIsaFlags.H | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei | RVIsaFlags.Privileged, TargetEndianness.Little);
    /// <summary>Maps the fine-grained profile extensions onto the ISA flags the encoder checks</summary>
    private static RVIsaFlags ProfileExtensions(TargetArchitectureFeatures features)
    {
        var flags = RVIsaFlags.None;
        foreach (var (feature, isa) in ProfileExtensionMap)
        {
            if ((features & feature) != 0)
                flags |= isa;
        }

        return flags;
    }

    private static readonly (TargetArchitectureFeatures Feature, RVIsaFlags Isa)[] ProfileExtensionMap =
    {
        (TargetArchitectureFeatures.RiscVZicbom, RVIsaFlags.Zicbom),
        (TargetArchitectureFeatures.RiscVZicbop, RVIsaFlags.Zicbop),
        (TargetArchitectureFeatures.RiscVZicboz, RVIsaFlags.Zicboz),
        (TargetArchitectureFeatures.RiscVZawrs, RVIsaFlags.Zawrs),
        (TargetArchitectureFeatures.RiscVZfa, RVIsaFlags.Zfa),
        (TargetArchitectureFeatures.RiscVZfhmin, RVIsaFlags.Zfhmin),
        (TargetArchitectureFeatures.RiscVZihintpause, RVIsaFlags.Zihintpause),
        (TargetArchitectureFeatures.RiscVZihintntl, RVIsaFlags.Zihintntl),
        (TargetArchitectureFeatures.RiscVZimop, RVIsaFlags.Zimop),
        (TargetArchitectureFeatures.RiscVZcmop, RVIsaFlags.Zcmop),
        (TargetArchitectureFeatures.RiscVZcb, RVIsaFlags.Zcb),
        (TargetArchitectureFeatures.RiscVZcd, RVIsaFlags.Zcd),
        (TargetArchitectureFeatures.RiscVZvbb, RVIsaFlags.Zvbb),
        (TargetArchitectureFeatures.RiscVSvinval, RVIsaFlags.Svinval),
        (TargetArchitectureFeatures.RVA23, RVIsaFlags.Zicntr | RVIsaFlags.Zihpm),
    };

    public static RVTarget FromTargetInfo(Cnidaria.C.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.RiscV32 and not TargetArchitectureKind.RiscV64)
            throw new ArgumentException("Target architecture is not RISC-V", nameof(target));

        var flags = RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei;
        var features = target.ArchitectureFeatures;
        if ((features & TargetArchitectureFeatures.RiscVM) != 0)
            flags |= RVIsaFlags.M;
        if ((features & TargetArchitectureFeatures.RiscVA) != 0)
            flags |= RVIsaFlags.A;
        if ((features & TargetArchitectureFeatures.RiscVZaamo) != 0)
            flags |= RVIsaFlags.Zaamo;
        if ((features & TargetArchitectureFeatures.RiscVZalrsc) != 0)
            flags |= RVIsaFlags.Zalrsc;
        if ((features & TargetArchitectureFeatures.RiscVF) != 0)
            flags |= RVIsaFlags.F;
        if ((features & TargetArchitectureFeatures.RiscVD) != 0)
            flags |= RVIsaFlags.D;
        if ((features & TargetArchitectureFeatures.RiscVC) != 0)
            flags |= RVIsaFlags.C;
        if ((features & TargetArchitectureFeatures.RiscVV) != 0)
            flags |= RVIsaFlags.V;
        if ((features & TargetArchitectureFeatures.RiscVB) != 0)
            flags |= RVIsaFlags.B;
        if ((features & TargetArchitectureFeatures.RiscVH) != 0)
            flags |= RVIsaFlags.H;
        if ((features & TargetArchitectureFeatures.RiscVZacas) != 0)
            flags |= RVIsaFlags.Zacas;
        if ((features & TargetArchitectureFeatures.RiscVZicond) != 0)
            flags |= RVIsaFlags.Zicond;
        if ((features & TargetArchitectureFeatures.RiscVPrivileged) != 0)
            flags |= RVIsaFlags.Privileged;
        flags |= ProfileExtensions(features);

        var abi = target.Architecture == TargetArchitectureKind.RiscV64
            ? ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Lp64D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Lp64F : RVAbiKind.Lp64)
            : ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Ilp32D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Ilp32F : RVAbiKind.Ilp32);

        return new RVTarget(target.PointerSize * 8, abi, flags, target.Endianness, target.OperatingSystem);
    }
    public static RVTarget FromTargetInfo(Cnidaria.Cs.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.RiscV32 and not TargetArchitectureKind.RiscV64)
            throw new ArgumentException("Target architecture is not RISC-V", nameof(target));

        var flags = RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei;
        var features = target.ArchitectureFeatures;
        if ((features & TargetArchitectureFeatures.RiscVM) != 0)
            flags |= RVIsaFlags.M;
        if ((features & TargetArchitectureFeatures.RiscVA) != 0)
            flags |= RVIsaFlags.A;
        if ((features & TargetArchitectureFeatures.RiscVZaamo) != 0)
            flags |= RVIsaFlags.Zaamo;
        if ((features & TargetArchitectureFeatures.RiscVZalrsc) != 0)
            flags |= RVIsaFlags.Zalrsc;
        if ((features & TargetArchitectureFeatures.RiscVF) != 0)
            flags |= RVIsaFlags.F;
        if ((features & TargetArchitectureFeatures.RiscVD) != 0)
            flags |= RVIsaFlags.D;
        if ((features & TargetArchitectureFeatures.RiscVC) != 0)
            flags |= RVIsaFlags.C;
        if ((features & TargetArchitectureFeatures.RiscVV) != 0)
            flags |= RVIsaFlags.V;
        if ((features & TargetArchitectureFeatures.RiscVB) != 0)
            flags |= RVIsaFlags.B;
        if ((features & TargetArchitectureFeatures.RiscVH) != 0)
            flags |= RVIsaFlags.H;
        if ((features & TargetArchitectureFeatures.RiscVZacas) != 0)
            flags |= RVIsaFlags.Zacas;
        if ((features & TargetArchitectureFeatures.RiscVZicond) != 0)
            flags |= RVIsaFlags.Zicond;
        if ((features & TargetArchitectureFeatures.RiscVPrivileged) != 0)
            flags |= RVIsaFlags.Privileged;
        flags |= ProfileExtensions(features);

        var abi = target.Architecture == TargetArchitectureKind.RiscV64
            ? ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Lp64D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Lp64F : RVAbiKind.Lp64)
            : ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Ilp32D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Ilp32F : RVAbiKind.Ilp32);

        return new RVTarget(target.PointerSize * 8, abi, flags, target.Endianness, target.OperatingSystem);
    }

    public int XLen { get; }
    public RVAbiKind Abi { get; }
    public RVIsaFlags Isa { get; }
    public TargetEndianness Endianness { get; }
    public OperatingSystemKind OperatingSystem { get; }
    public bool Is32Bit => XLen == 32;
    public bool Is64Bit => XLen == 64;
    public bool HasM => Has(RVIsaFlags.M);
    public bool HasA => Has(RVIsaFlags.A);
    public bool HasF => Has(RVIsaFlags.F);
    public bool HasD => Has(RVIsaFlags.D);
    public bool HasC => Has(RVIsaFlags.C);
    public bool HasV => Has(RVIsaFlags.V);
    public bool HasB => Has(RVIsaFlags.B);
    public bool HasH => Has(RVIsaFlags.H);
    public bool HasZicsr => Has(RVIsaFlags.Zicsr);
    public bool HasZifencei => Has(RVIsaFlags.Zifencei);
    public bool HasZacas => Has(RVIsaFlags.Zacas);
    public bool HasZaamo => Has(RVIsaFlags.Zaamo);
    public bool HasZalrsc => Has(RVIsaFlags.Zalrsc);
    public bool HasZicond => Has(RVIsaFlags.Zicond);
    public bool HasPrivileged => Has(RVIsaFlags.Privileged);

    public RVTarget(int xlen, RVAbiKind abi, RVIsaFlags isa, TargetEndianness endianness = TargetEndianness.Little, OperatingSystemKind operatingSystem = OperatingSystemKind.None)
    {
        if (xlen is not 32 and not 64)
            throw new ArgumentOutOfRangeException(nameof(xlen));
        if ((isa & RVIsaFlags.I) == 0)
            throw new ArgumentException("RISC-V target requires base I extension", nameof(isa));
        if (xlen == 32 && abi is RVAbiKind.Lp64 or RVAbiKind.Lp64F or RVAbiKind.Lp64D)
            throw new ArgumentException("LP64 ABI requires RV64", nameof(abi));
        if (xlen == 64 && abi is RVAbiKind.Ilp32 or RVAbiKind.Ilp32F or RVAbiKind.Ilp32D)
            throw new ArgumentException("ILP32 ABI requires RV32", nameof(abi));

        if ((isa & RVIsaFlags.A) != 0)
            isa |= RVIsaFlags.Zaamo | RVIsaFlags.Zalrsc;
        if ((isa & RVIsaFlags.Zacas) != 0)
            isa |= RVIsaFlags.Zaamo;

        XLen = xlen;
        Abi = abi;
        Isa = isa;
        Endianness = endianness;
        OperatingSystem = operatingSystem;
    }

    public bool Has(RVIsaFlags flags)
        => (Isa & flags) == flags;

    public override string ToString()
        => XLen == 64 ? $"rv64{FormatIsaSuffix()}" : $"rv32{FormatIsaSuffix()}";

    private string FormatIsaSuffix()
    {
        var suffix = "i";
        if (HasM)
            suffix += "m";
        if (HasA)
            suffix += "a";
        if (HasF)
            suffix += "f";
        if (HasD)
            suffix += "d";
        if (HasC)
            suffix += "c";
        if (HasV)
            suffix += "v";
        if (HasB)
            suffix += "b";
        if (HasH)
            suffix += "h";
        if (HasZaamo && !HasA && !HasZacas)
            suffix += "_zaamo";
        if (HasZalrsc && !HasA)
            suffix += "_zalrsc";
        if (HasZacas)
            suffix += "_zacas";
        if (HasZicond)
            suffix += "_zicond";
        return suffix;
    }
}
internal static class RVRegisters
{
    public const RVRegister Zero = RVRegister.X0;
    public const RVRegister ReturnAddress = RVRegister.X1;
    public const RVRegister StackPointer = RVRegister.X2;
    public const RVRegister GlobalPointer = RVRegister.X3;
    public const RVRegister ThreadPointer = RVRegister.X4;
    public const RVRegister FramePointer = RVRegister.X8;
    public const RVRegister ReturnValue0 = RVRegister.X10;
    public const RVRegister ReturnValue1 = RVRegister.X11;
    public const RVRegister IntegerArgument0 = RVRegister.X10;
    public const RVRegister IntegerArgument7 = RVRegister.X17;
    public const RVRegister FloatReturnValue0 = RVRegister.F10;
    public const RVRegister FloatReturnValue1 = RVRegister.F11;
    public const RVRegister FloatArgument0 = RVRegister.F10;
    public const RVRegister FloatArgument7 = RVRegister.F17;
    public const RVRegister VectorMask = RVRegister.V0;
    public const RVRegister Scratch0 = RVRegister.X5;
    public const RVRegister Scratch1 = RVRegister.X6;
    public const RVRegister Scratch2 = RVRegister.X7;
    public const RVRegister Scratch3 = RVRegister.X28;
    public const RVRegister Scratch4 = RVRegister.X29;
    public const RVRegister Scratch5 = RVRegister.X30;
    public const RVRegister Scratch6 = RVRegister.X31;

    public static ImmutableArray<RVRegister> IntegerArguments { get; } = ImmutableArray.Create(
        RVRegister.X10, RVRegister.X11, RVRegister.X12, RVRegister.X13,
        RVRegister.X14, RVRegister.X15, RVRegister.X16, RVRegister.X17);

    public static ImmutableArray<RVRegister> IntegerReturnValues { get; } = ImmutableArray.Create(
        RVRegister.X10, RVRegister.X11);

    public static ImmutableArray<RVRegister> CallerSavedGprs { get; } = ImmutableArray.Create(
        RVRegister.X1, RVRegister.X5, RVRegister.X6, RVRegister.X7,
        RVRegister.X10, RVRegister.X11, RVRegister.X12, RVRegister.X13,
        RVRegister.X14, RVRegister.X15, RVRegister.X16, RVRegister.X17,
        RVRegister.X28, RVRegister.X29, RVRegister.X30, RVRegister.X31);

    public static ImmutableArray<RVRegister> CalleeSavedGprs { get; } = ImmutableArray.Create(
        RVRegister.X8, RVRegister.X9, RVRegister.X18, RVRegister.X19,
        RVRegister.X20, RVRegister.X21, RVRegister.X22, RVRegister.X23,
        RVRegister.X24, RVRegister.X25, RVRegister.X26, RVRegister.X27);

    public static ImmutableArray<RVRegister> AllocatableGprs { get; } = ImmutableArray.Create(
        RVRegister.X5, RVRegister.X6, RVRegister.X7, RVRegister.X9,
        RVRegister.X10, RVRegister.X11, RVRegister.X12, RVRegister.X13,
        RVRegister.X14, RVRegister.X15, RVRegister.X16, RVRegister.X17,
        RVRegister.X18, RVRegister.X19, RVRegister.X20, RVRegister.X21,
        RVRegister.X22, RVRegister.X23, RVRegister.X24, RVRegister.X25,
        RVRegister.X26, RVRegister.X27, RVRegister.X28, RVRegister.X29,
        RVRegister.X30, RVRegister.X31);

    public static ImmutableArray<RVRegister> FloatArguments { get; } = ImmutableArray.Create(
        RVRegister.F10, RVRegister.F11, RVRegister.F12, RVRegister.F13,
        RVRegister.F14, RVRegister.F15, RVRegister.F16, RVRegister.F17);

    public static ImmutableArray<RVRegister> FloatReturnValues { get; } = ImmutableArray.Create(
        RVRegister.F10, RVRegister.F11);

    public static ImmutableArray<RVRegister> CallerSavedFprs { get; } = ImmutableArray.Create(
        RVRegister.F0, RVRegister.F1, RVRegister.F2, RVRegister.F3,
        RVRegister.F4, RVRegister.F5, RVRegister.F6, RVRegister.F7,
        RVRegister.F10, RVRegister.F11, RVRegister.F12, RVRegister.F13,
        RVRegister.F14, RVRegister.F15, RVRegister.F16, RVRegister.F17,
        RVRegister.F28, RVRegister.F29, RVRegister.F30, RVRegister.F31);

    public static ImmutableArray<RVRegister> CalleeSavedFprs { get; } = ImmutableArray.Create(
        RVRegister.F8, RVRegister.F9, RVRegister.F18, RVRegister.F19,
        RVRegister.F20, RVRegister.F21, RVRegister.F22, RVRegister.F23,
        RVRegister.F24, RVRegister.F25, RVRegister.F26, RVRegister.F27);

    public static ImmutableArray<RVRegister> AllocatableFprs { get; } = CreateRange(RVRegister.F0, 32);
    public static ImmutableArray<RVRegister> VectorRegisters { get; } = CreateRange(RVRegister.V0, 32);
    public static ImmutableArray<RVRegister> AllocatableVectorRegisters { get; } = CreateRange(RVRegister.V1, 31);

    private static readonly string[] IntegerAbiNames =
    {
        "zero", "ra", "sp", "gp", "tp", "t0", "t1", "t2", "s0", "s1", "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11", "t3", "t4", "t5", "t6"
    };

    private static readonly string[] FloatAbiNames =
    {
        "ft0", "ft1", "ft2", "ft3", "ft4", "ft5", "ft6", "ft7", "fs0", "fs1", "fa0", "fa1", "fa2", "fa3", "fa4", "fa5", "fa6", "fa7", "fs2", "fs3", "fs4", "fs5", "fs6", "fs7", "fs8", "fs9", "fs10", "fs11", "ft8", "ft9", "ft10", "ft11"
    };

    private static readonly Dictionary<string, RVRegister> Names = CreateNameMap();

    public static bool IsInteger(RVRegister register)
        => register >= RVRegister.X0 && register <= RVRegister.X31;

    public static bool IsFloat(RVRegister register)
        => register >= RVRegister.F0 && register <= RVRegister.F31;

    public static bool IsVector(RVRegister register)
        => register >= RVRegister.V0 && register <= RVRegister.V31;

    public static int IntegerIndex(RVRegister register)
    {
        if (!IsInteger(register))
            throw new ArgumentOutOfRangeException(nameof(register));
        return (int)register;
    }

    public static int FloatIndex(RVRegister register)
    {
        if (!IsFloat(register))
            throw new ArgumentOutOfRangeException(nameof(register));
        return (int)register - 32;
    }

    public static int VectorIndex(RVRegister register)
    {
        if (!IsVector(register))
            throw new ArgumentOutOfRangeException(nameof(register));
        return (int)register - 64;
    }

    public static RVRegister Float(uint index) => (RVRegister)(32 + index);

    public static RVRegister Vector(uint index) => (RVRegister)(64 + index);

    public static string Format(RVRegister register, bool abiName = true)
    {
        if (register == RVRegister.Invalid)
            return "invalid";
        if (IsInteger(register))
        {
            int index = IntegerIndex(register);
            return abiName ? IntegerAbiNames[index] : $"x{index}";
        }
        if (IsFloat(register))
        {
            int index = FloatIndex(register);
            return abiName ? FloatAbiNames[index] : $"f{index}";
        }
        if (IsVector(register))
            return $"v{VectorIndex(register)}";
        throw new ArgumentOutOfRangeException(nameof(register));
    }

    public static bool TryParse(string text, out RVRegister register)
    {
        if (text is null)
        {
            register = RVRegister.Invalid;
            return false;
        }
        return Names.TryGetValue(text.Trim().ToLowerInvariant(), out register);
    }

    public static RVRegister Parse(string text)
    {
        if (TryParse(text, out var register))
            return register;
        throw new FormatException($"Invalid register: {text}");
    }

    private static Dictionary<string, RVRegister> CreateNameMap()
    {
        var map = new Dictionary<string, RVRegister>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 32; i++)
        {
            map[$"x{i}"] = (RVRegister)i;
            map[IntegerAbiNames[i]] = (RVRegister)i;
            map[$"f{i}"] = (RVRegister)(i + 32);
            map[FloatAbiNames[i]] = (RVRegister)(i + 32);
            map[$"v{i}"] = (RVRegister)(i + 64);
        }
        map["fp"] = RVRegister.X8;
        return map;
    }

    private static ImmutableArray<RVRegister> CreateRange(RVRegister first, int count)
    {
        var builder = ImmutableArray.CreateBuilder<RVRegister>(count);
        int start = (int)first;
        for (int i = 0; i < count; i++)
            builder.Add((RVRegister)(start + i));
        return builder.MoveToImmutable();
    }
}

internal static class RiscVCsrs
{
    private static readonly Dictionary<string, int> NameToValue = CreateNameMap();
    private static readonly Dictionary<int, string> ValueToName = CreateValueMap(NameToValue);

    public static bool TryParse(string text, out int csr)
    {
        if (text is null)
        {
            csr = 0;
            return false;
        }

        text = text.Trim().ToLowerInvariant();
        if (NameToValue.TryGetValue(text, out csr))
            return true;
        if (TryParseIndexedCsr(text, "hpmcounter", 0xC00, 3, 31, out csr))
            return true;
        if (TryParseIndexedCsr(text, "hpmcounter", 0xC80, 3, 31, out csr, "h"))
            return true;
        if (TryParseIndexedCsr(text, "mhpmcounter", 0xB00, 3, 31, out csr))
            return true;
        if (TryParseIndexedCsr(text, "mhpmcounter", 0xB80, 3, 31, out csr, "h"))
            return true;
        if (TryParseIndexedCsr(text, "mhpmevent", 0x320, 3, 31, out csr))
            return true;
        if (TryParseIndexedCsr(text, "mhpmevent", 0x720, 3, 31, out csr, "h"))
            return true;
        if (TryParseIndexedCsr(text, "pmpcfg", 0x3A0, 0, 15, out csr))
            return true;
        if (TryParseIndexedCsr(text, "pmpaddr", 0x3B0, 0, 63, out csr))
            return true;
        if (TryParseIndexedCsr(text, "sstateen", 0x10C, 0, 3, out csr))
            return true;
        if (TryParseIndexedCsr(text, "sstateen", 0x11C, 0, 3, out csr, "h"))
            return true;
        if (TryParseIndexedCsr(text, "mstateen", 0x30C, 0, 3, out csr))
            return true;
        if (TryParseIndexedCsr(text, "mstateen", 0x31C, 0, 3, out csr, "h"))
            return true;
        return TryParseNumber(text, out csr) && csr >= 0 && csr <= 0xFFF;
    }

    public static int Parse(string text)
    {
        if (TryParse(text, out int csr))
            return csr;
        throw new FormatException($"Invalid CSR: {text}");
    }

    public static string Format(int csr)
    {
        if (ValueToName.TryGetValue(csr, out var name))
            return name;
        if (TryFormatIndexedCsr(csr, 0xC00, 3, 31, "hpmcounter", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0xC80, 3, 31, "hpmcounter", "h", out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0xB00, 3, 31, "mhpmcounter", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0xB80, 3, 31, "mhpmcounter", "h", out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x320, 3, 31, "mhpmevent", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x720, 3, 31, "mhpmevent", "h", out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x3A0, 0, 15, "pmpcfg", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x3B0, 0, 63, "pmpaddr", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x10C, 0, 3, "sstateen", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x11C, 0, 3, "sstateen", "h", out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x30C, 0, 3, "mstateen", string.Empty, out name))
            return name;
        if (TryFormatIndexedCsr(csr, 0x31C, 0, 3, "mstateen", "h", out name))
            return name;
        return $"0x{csr:X8}";
    }

    private static Dictionary<string, int> CreateNameMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["fflags"] = 0x001,
            ["frm"] = 0x002,
            ["fcsr"] = 0x003,
            ["vstart"] = 0x008,
            ["vxsat"] = 0x009,
            ["vxrm"] = 0x00A,
            ["vcsr"] = 0x00F,
            ["seed"] = 0x015,
            ["jvt"] = 0x017,
            ["cycle"] = 0xC00,
            ["time"] = 0xC01,
            ["instret"] = 0xC02,
            ["cycleh"] = 0xC80,
            ["timeh"] = 0xC81,
            ["instreth"] = 0xC82,
            ["vl"] = 0xC20,
            ["vtype"] = 0xC21,
            ["vlenb"] = 0xC22,
            ["sstatus"] = 0x100,
            ["sie"] = 0x104,
            ["stvec"] = 0x105,
            ["scounteren"] = 0x106,
            ["senvcfg"] = 0x10A,
            ["sscratch"] = 0x140,
            ["sepc"] = 0x141,
            ["scause"] = 0x142,
            ["stval"] = 0x143,
            ["sip"] = 0x144,
            ["satp"] = 0x180,
            ["scontext"] = 0x5A8,
            ["hstatus"] = 0x600,
            ["hedeleg"] = 0x602,
            ["hideleg"] = 0x603,
            ["hie"] = 0x604,
            ["htimedelta"] = 0x605,
            ["hcounteren"] = 0x606,
            ["hgeie"] = 0x607,
            ["henvcfg"] = 0x60A,
            ["htimedeltah"] = 0x615,
            ["henvcfgh"] = 0x61A,
            ["htval"] = 0x643,
            ["hip"] = 0x644,
            ["hvip"] = 0x645,
            ["htinst"] = 0x64A,
            ["hgatp"] = 0x680,
            ["hgeip"] = 0xE12,
            ["vsstatus"] = 0x200,
            ["vsie"] = 0x204,
            ["vstvec"] = 0x205,
            ["vsscratch"] = 0x240,
            ["vsepc"] = 0x241,
            ["vscause"] = 0x242,
            ["vstval"] = 0x243,
            ["vsip"] = 0x244,
            ["vsatp"] = 0x280,
            ["mvendorid"] = 0xF11,
            ["marchid"] = 0xF12,
            ["mimpid"] = 0xF13,
            ["mhartid"] = 0xF14,
            ["mconfigptr"] = 0xF15,
            ["mstatus"] = 0x300,
            ["misa"] = 0x301,
            ["medeleg"] = 0x302,
            ["mideleg"] = 0x303,
            ["mie"] = 0x304,
            ["mtvec"] = 0x305,
            ["mcounteren"] = 0x306,
            ["mstatush"] = 0x310,
            ["menvcfg"] = 0x30A,
            ["menvcfgh"] = 0x31A,
            ["mscratch"] = 0x340,
            ["mepc"] = 0x341,
            ["mcause"] = 0x342,
            ["mtval"] = 0x343,
            ["mip"] = 0x344,
            ["mtinst"] = 0x34A,
            ["mtval2"] = 0x34B,
            ["mseccfg"] = 0x747,
            ["mseccfgh"] = 0x757,
            ["mcycle"] = 0xB00,
            ["minstret"] = 0xB02,
            ["mcycleh"] = 0xB80,
            ["minstreth"] = 0xB82,
            ["dcsr"] = 0x7B0,
            ["dpc"] = 0x7B1,
            ["dscratch0"] = 0x7B2,
            ["dscratch1"] = 0x7B3,
        };
        return map;
    }

    private static Dictionary<int, string> CreateValueMap(Dictionary<string, int> source)
    {
        var map = new Dictionary<int, string>();
        foreach (var kv in source)
        {
            if (!map.ContainsKey(kv.Value))
                map.Add(kv.Value, kv.Key);
        }
        return map;
    }

    private static bool TryParseIndexedCsr(string text, string prefix, int baseValue, int minIndex, int maxIndex, out int csr, string suffix = "")
    {
        csr = 0;
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;
        int numberStart = prefix.Length;
        int numberLength = text.Length - numberStart - suffix.Length;
        if (numberLength <= 0)
            return false;
        if (!int.TryParse(text.Substring(numberStart, numberLength), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return false;
        if (index < minIndex || index > maxIndex)
            return false;
        csr = baseValue + index;
        return true;
    }

    private static bool TryFormatIndexedCsr(int csr, int baseValue, int minIndex, int maxIndex, string prefix, string suffix, out string name)
    {
        int index = csr - baseValue;
        if (index >= minIndex && index <= maxIndex)
        {
            name = prefix + index.ToString(CultureInfo.InvariantCulture) + suffix;
            return true;
        }
        name = string.Empty;
        return false;
    }

    private static bool TryParseNumber(string text, out int value)
    {
        text = text.Replace("_", string.Empty);
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

internal readonly struct RVInstructionMetadata
{
    public RVInstructionFormat Format { get; }
    public RVIsaFlags RequiredIsa { get; }
    public bool Requires64Bit { get; }
    public byte Opcode { get; }
    public byte Funct3 { get; }
    public byte Funct7 { get; }

    /// <summary>Set for the table-driven instructions, which carry their whole encoding here</summary>
    public RVEncodedForm? Form { get; }
    public bool Requires32Bit { get; }

    public RVInstructionMetadata(RVInstructionFormat format, RVIsaFlags requiredIsa, bool requires64Bit, byte opcode, byte funct3, byte funct7, RVEncodedForm? form = null, bool requires32Bit = false)
    {
        Requires32Bit = requires32Bit;
        Format = format;
        RequiredIsa = requiredIsa;
        Requires64Bit = requires64Bit;
        Opcode = opcode;
        Funct3 = funct3;
        Funct7 = funct7;
        Form = form;
    }
}

public readonly struct RVInstruction
{
    public RVInstrKind Opcode { get; }
    public RVRegister Rd { get; }
    public RVRegister Rs1 { get; }
    public RVRegister Rs2 { get; }
    public RVRegister Rs3 { get; }
    public int Immediate { get; }
    public string? Symbol { get; }
    public RVRelocationKind RelocationKind { get; }
    public RVInstructionFlags Flags { get; }
    public bool HasSymbol => !string.IsNullOrEmpty(Symbol);
    public bool VectorUnmasked => (Flags & RVInstructionFlags.VectorUnmasked) != 0;
    public bool AtomicAcquire => (Flags & RVInstructionFlags.AtomicAcquire) != 0;
    public bool AtomicRelease => (Flags & RVInstructionFlags.AtomicRelease) != 0;

    public RVInstruction(
        RVInstrKind opcode,
        RVRegister rd = RVRegister.Invalid,
        RVRegister rs1 = RVRegister.Invalid,
        RVRegister rs2 = RVRegister.Invalid,
        int immediate = 0,
        string? symbol = null,
        RVRelocationKind relocationKind = RVRelocationKind.None,
        RVInstructionFlags flags = RVInstructionFlags.None,
        RVRegister rs3 = RVRegister.Invalid)
    {
        Opcode = opcode;
        Rd = rd;
        Rs1 = rs1;
        Rs2 = rs2;
        Rs3 = rs3;
        Immediate = immediate;
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;
        RelocationKind = relocationKind;
        Flags = flags;
    }

    public RVInstruction WithImmediate(int immediate)
        => new RVInstruction(Opcode, Rd, Rs1, Rs2, immediate, null, RVRelocationKind.None, Flags, Rs3);

    public RVInstruction WithSymbol(string symbol, RVRelocationKind relocationKind)
        => new RVInstruction(Opcode, Rd, Rs1, Rs2, Immediate, symbol, relocationKind, Flags, Rs3);

    public RVInstruction WithFlags(RVInstructionFlags flags)
        => new RVInstruction(Opcode, Rd, Rs1, Rs2, Immediate, Symbol, RelocationKind, flags, Rs3);

    public static RVInstruction Raw(uint word)
        => new RVInstruction(RVInstrKind.Raw32, immediate: unchecked((int)word));

    public static RVInstruction Raw16(ushort halfword)
        => new RVInstruction(RVInstrKind.Raw16, immediate: halfword);

    public static RVInstruction R(RVInstrKind opcode, RVRegister rd, RVRegister rs1, RVRegister rs2)
        => new RVInstruction(opcode, rd, rs1, rs2);

    public static RVInstruction I(RVInstrKind opcode, RVRegister rd, RVRegister rs1, int immediate)
        => new RVInstruction(opcode, rd, rs1, RVRegister.Invalid, immediate);

    public static RVInstruction S(RVInstrKind opcode, RVRegister rs2, RVRegister rs1, int immediate)
        => new RVInstruction(opcode, RVRegister.Invalid, rs1, rs2, immediate);

    public static RVInstruction B(RVInstrKind opcode, RVRegister rs1, RVRegister rs2, int immediate)
        => new RVInstruction(opcode, RVRegister.Invalid, rs1, rs2, immediate);

    public static RVInstruction B(RVInstrKind opcode, RVRegister rs1, RVRegister rs2, string symbol)
        => new RVInstruction(opcode, RVRegister.Invalid, rs1, rs2, 0, symbol, RVRelocationKind.RelativeBranch);

    public static RVInstruction U(RVInstrKind opcode, RVRegister rd, int immediate)
        => new RVInstruction(opcode, rd, RVRegister.Invalid, RVRegister.Invalid, immediate);

    public static RVInstruction J(RVInstrKind opcode, RVRegister rd, int immediate)
        => new RVInstruction(opcode, rd, RVRegister.Invalid, RVRegister.Invalid, immediate);

    public static RVInstruction J(RVInstrKind opcode, RVRegister rd, string symbol)
        => new RVInstruction(opcode, rd, RVRegister.Invalid, RVRegister.Invalid, 0, symbol, RVRelocationKind.RelativeJal);

    public static RVInstruction Amo(RVInstrKind opcode, RVRegister rd, RVRegister rs1, RVRegister rs2, bool acquire = false, bool release = false)
    {
        var flags = RVInstructionFlags.None;
        if (acquire)
            flags |= RVInstructionFlags.AtomicAcquire;
        if (release)
            flags |= RVInstructionFlags.AtomicRelease;
        return new RVInstruction(opcode, rd, rs1, rs2, flags: flags);
    }

    public static RVInstruction Vsetvli(RVRegister rd, RVRegister rs1, int vtype)
        => new RVInstruction(RVInstrKind.Vsetvli, rd, rs1, RVRegister.Invalid, vtype);

    public static RVInstruction Vsetivli(RVRegister rd, int avl, int vtype)
        => new RVInstruction(RVInstrKind.Vsetivli, rd, (RVRegister)avl, RVRegister.Invalid, vtype);

    public static RVInstruction Vsetvl(RVRegister rd, RVRegister rs1, RVRegister rs2)
        => new RVInstruction(RVInstrKind.Vsetvl, rd, rs1, rs2);

    public static RVInstruction Vv(RVInstrKind opcode, RVRegister vd, RVRegister vs2, RVRegister vs1, bool unmasked = true)
        => new RVInstruction(opcode, vd, vs1, vs2, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);

    public static RVInstruction Vx(RVInstrKind opcode, RVRegister vd, RVRegister vs2, RVRegister rs1, bool unmasked = true)
        => new RVInstruction(opcode, vd, rs1, vs2, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);

    public static RVInstruction Vi(RVInstrKind opcode, RVRegister vd, RVRegister vs2, int immediate, bool unmasked = true)
        => new RVInstruction(opcode, vd, RVRegister.Invalid, vs2, immediate, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);

    public static RVInstruction Vl(RVInstrKind opcode, RVRegister vd, RVRegister rs1, bool unmasked = true)
        => new RVInstruction(opcode, vd, rs1, RVRegister.Invalid, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);

    public static RVInstruction Vs(RVInstrKind opcode, RVRegister vs3, RVRegister rs1, bool unmasked = true)
        => new RVInstruction(opcode, vs3, rs1, RVRegister.Invalid, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);

    /// <summary>A strided access, whose second register carries the step between elements</summary>
    public static RVInstruction Vls(RVInstrKind opcode, RVRegister vd, RVRegister rs1, RVRegister rs2, bool unmasked = true)
        => new RVInstruction(opcode, vd, rs1, rs2, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);
}

internal sealed class RVInstructionBuilder
{
    private readonly List<RVInstruction> _instructions = new List<RVInstruction>();
    private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);

    public int Count => _instructions.Count;
    public int Position => RVInstructionTable.GetEncodedSize(_instructions);

    public void Emit(RVInstruction instruction)
        => _instructions.Add(instruction);

    public void DefineLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("RISC-V label must not be empty", nameof(label));
        if (_labels.ContainsKey(label))
            throw new ArgumentException($"Duplicate label: {label}", nameof(label));
        _labels.Add(label, Position);
    }

    public RVTextSection ToTextSection()
        => new RVTextSection(_instructions, _labels);

    public RiscVProgram ToObject(RVTarget target, string entrySymbol = "")
        => new RiscVProgram(target, ToTextSection(), ImmutableArray<RVDataSection>.Empty, ImmutableArray<RVObjectSymbol>.Empty, entrySymbol);

    public void Clear()
    {
        _instructions.Clear();
        _labels.Clear();
    }
}

internal static class RVInstructionTable
{
    private static readonly Dictionary<RVInstrKind, RVInstructionMetadata> ByOpcode = CreateOpcodeMap();
    private static readonly Dictionary<string, RVInstrKind> ByMnemonic = CreateMnemonicMap();

    public static RVInstructionMetadata Get(RVInstrKind opcode)
    {
        if (ByOpcode.TryGetValue(opcode, out var metadata))
            return metadata;
        throw new ArgumentOutOfRangeException(nameof(opcode));
    }

    public static int GetEncodedSize(RVInstrKind opcode)
    {
        var metadata = Get(opcode);
        if (metadata.Format == RVInstructionFormat.Encoded)
            return metadata.Form!.IsCompressed ? 2 : 4;
        return metadata.Format == RVInstructionFormat.Raw16 ? 2 : 4;
    }

    public static int GetEncodedSize(IEnumerable<RVInstruction> instructions)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));
        var size = 0;
        foreach (var instruction in instructions)
            size = checked(size + GetEncodedSize(instruction.Opcode));
        return size;
    }

    public static bool IsCompressed(RVInstrKind opcode)
        => GetEncodedSize(opcode) == 2;

    public static string GetMnemonic(RVInstrKind opcode)
        => RVEncodedInstructions.TryGetMnemonic(opcode, out var generated) ? generated : GetCoreMnemonic(opcode);

    private static string GetCoreMnemonic(RVInstrKind opcode) => opcode switch
    {
        RVInstrKind.Raw32 => ".word",
        RVInstrKind.Raw16 => ".hword",
        _ => throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "RISC-V opcode has no mnemonic"),
    };

    public static bool TryGetOpcode(string mnemonic, out RVInstrKind opcode)
    {
        if (mnemonic is null)
        {
            opcode = RVInstrKind.Invalid;
            return false;
        }
        return ByMnemonic.TryGetValue(mnemonic.Trim(), out opcode);
    }

    public static RVInstrKind GetOpcode(string mnemonic)
    {
        if (TryGetOpcode(mnemonic, out var opcode))
            return opcode;
        throw new FormatException($"Unknown mnemonic: {mnemonic}");
    }

    public static bool IsBranch(RVInstrKind opcode)
        => opcode is RVInstrKind.Beq or RVInstrKind.Bne or RVInstrKind.Blt or RVInstrKind.Bge or
                     RVInstrKind.Bltu or RVInstrKind.Bgeu or RVInstrKind.CBeqz or RVInstrKind.CBnez;

    public static bool IsLoad(RVInstrKind opcode)
        => opcode is RVInstrKind.Lb or RVInstrKind.Lh or RVInstrKind.Lw or RVInstrKind.Lbu or RVInstrKind.Lhu or RVInstrKind.Lwu or
                     RVInstrKind.Ld or RVInstrKind.Flw or RVInstrKind.Fld or RVInstrKind.CLw or RVInstrKind.CLd or RVInstrKind.CFlw or
                     RVInstrKind.CFld or RVInstrKind.CLwSp or RVInstrKind.CLdSp or RVInstrKind.HlvB or RVInstrKind.HlvBu or
                     RVInstrKind.HlvH or RVInstrKind.HlvHu or RVInstrKind.HlvxHu or RVInstrKind.HlvW or RVInstrKind.HlvWu or
                     RVInstrKind.HlvxWu or RVInstrKind.HlvD;

    public static bool IsStore(RVInstrKind opcode)
        => opcode is RVInstrKind.Sb or RVInstrKind.Sh or RVInstrKind.Sw or RVInstrKind.Sd or RVInstrKind.Fsw or RVInstrKind.Fsd or
                     RVInstrKind.CSw or RVInstrKind.CSd or RVInstrKind.CFsw or RVInstrKind.CFsd or RVInstrKind.CSwSp or RVInstrKind.CSdSp or
                     RVInstrKind.HsvB or RVInstrKind.HsvH or RVInstrKind.HsvW or RVInstrKind.HsvD;

    public static bool IsAmo(RVInstrKind opcode) => MajorOpcode(opcode) == 0x2F;

    public static bool IsVector(RVInstrKind opcode)
        => MajorOpcode(opcode) is 0x57 or 0x07 or 0x27;

    private static int MajorOpcode(RVInstrKind opcode)
    {
        var form = Get(opcode).Form;
        return form is null ? 0 : (int)(form.Match & 0x7F);
    }

    public static bool Is64BitOpcode(RVInstrKind opcode)
        => Get(opcode).Requires64Bit;

    public static bool IsMExtensionOpcode(RVInstrKind opcode)
        => (Get(opcode).RequiredIsa & RVIsaFlags.M) != 0;

    private static Dictionary<string, RVInstrKind> CreateMnemonicMap()
    {
        var map = new Dictionary<string, RVInstrKind>(StringComparer.OrdinalIgnoreCase);
        foreach (RVInstrKind opcode in Enum.GetValues<RVInstrKind>())
        {
            if (opcode is RVInstrKind.Invalid)
                continue;
            if (!ByOpcode.ContainsKey(opcode))
                continue;
            map[GetMnemonic(opcode)] = opcode;
        }
        return map;
    }

    public static IReadOnlyCollection<RVInstrKind> AllOpcodes => ByOpcode.Keys;

    private static Dictionary<RVInstrKind, RVInstructionMetadata> CreateOpcodeMap()
    {
        var map = new Dictionary<RVInstrKind, RVInstructionMetadata>();
        foreach (var entry in RVEncodedInstructions.All)
            map.Add(entry.Opcode, new RVInstructionMetadata(RVInstructionFormat.Encoded, entry.RequiredIsa, entry.Requires64Bit, 0, 0, 0, entry.Form));
        Add(map, RVInstrKind.Raw32, RVInstructionFormat.Raw, RVIsaFlags.I, false, 0, 0, 0);
        Add(map, RVInstrKind.Raw16, RVInstructionFormat.Raw16, RVIsaFlags.I, false, 0, 0, 0);
        foreach (var entry in RVEncodedInstructions.All)
            map[entry.Opcode] = new RVInstructionMetadata(RVInstructionFormat.Encoded, entry.RequiredIsa, entry.Requires64Bit, 0, 0, 0, entry.Form, entry.Requires32Bit);
        return map;
    }

    private static void Add(
        Dictionary<RVInstrKind, RVInstructionMetadata> map,
        RVInstrKind opcode,
        RVInstructionFormat format,
        RVIsaFlags requiredIsa,
        bool requires64Bit,
        byte op,
        byte funct3,
        byte funct7)
        => map.Add(opcode, new RVInstructionMetadata(format, requiredIsa, requires64Bit, op, funct3, funct7));

}
