using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.X86;

public sealed class X86Program
{
    public X86Target Target { get; }
    public X86TextSection Text { get; }
    public ImmutableArray<X86DataSection> DataSections { get; }
    public ImmutableArray<X86ObjectSymbol> Symbols { get; }
    public string EntrySymbol { get; }

    public X86Program(
        X86Target target,
        X86TextSection text,
        ImmutableArray<X86DataSection> dataSections,
        ImmutableArray<X86ObjectSymbol> symbols,
        string entrySymbol)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        DataSections = dataSections.IsDefault ? ImmutableArray<X86DataSection>.Empty : dataSections;
        Symbols = symbols.IsDefault ? ImmutableArray<X86ObjectSymbol>.Empty : symbols;
        EntrySymbol = entrySymbol ?? string.Empty;
    }

    public X86Program(
        X86Target target,
        IEnumerable<X86Instruction> instructions,
        IReadOnlyDictionary<string, int>? textLabels = null,
        string entrySymbol = "")
        : this(
            target,
            new X86TextSection(instructions, textLabels, ImmutableArray<X86ObjectRelocation>.Empty),
            ImmutableArray<X86DataSection>.Empty,
            ImmutableArray<X86ObjectSymbol>.Empty,
            entrySymbol)
    {
    }

    public string FormatText(X86AssemblyWriterOptions? options = null)
        => X86Disassembler.Disassemble(this, options);

    public X86Program Link(params X86Program[] objects)
        => X86ObjectComposer.Compose(this, objects);

    public X86LinkedImage LinkFlat(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => X86ObjectLinker.LinkFlat(this, imageBase, externalSymbols);

    public byte[] ToExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Target.OperatingSystem switch
        {
            OperatingSystemKind.Windows => X86PortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86PortableExecutableWriter.DefaultImageBase(Target) : imageBase),
            OperatingSystemKind.Linux => X86ElfWriter.WriteExecutable(this, imageBase == 0 ? X86ElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols),
            _ => LinkFlat(imageBase, externalSymbols).Bytes.ToArray(),
        };

    public byte[] ToWindowsExecutableBytes(ulong imageBase = 0)
        => X86PortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86PortableExecutableWriter.DefaultImageBase(Target) : imageBase);

    public byte[] ToLinuxDynamicExecutableBytes(
        IEnumerable<string>? needed = null,
        string? interpreter = null,
        ulong imageBase = 0)
        => X86ElfWriter.WriteDynamicExecutable(this, imageBase, interpreter, needed);

    public byte[] ToSharedObjectBytes(string soName, IEnumerable<string>? needed = null, string? initSymbol = null)
        => X86ElfWriter.WriteSharedObject(this, soName, needed, initSymbol);

    public byte[] ToLinuxExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => X86ElfWriter.WriteExecutable(this, imageBase == 0 ? X86ElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols);
}

public sealed class X86TextSection
{
    public ImmutableArray<X86Instruction> Instructions { get; }
    public ImmutableDictionary<string, int> Labels { get; }
    public ImmutableArray<X86ObjectRelocation> Relocations { get; }

    public X86TextSection(
        IEnumerable<X86Instruction> instructions,
        IReadOnlyDictionary<string, int>? labels = null,
        ImmutableArray<X86ObjectRelocation> relocations = default)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));

        Instructions = instructions.ToImmutableArray();
        Labels = labels is null
            ? ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)
            : labels.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Relocations = relocations.IsDefault ? ImmutableArray<X86ObjectRelocation>.Empty : relocations;
    }

    public byte[] Encode(X86Target target)
        => X86CodeEncoder.Encode(Instructions, target, Labels);

    public string Format(X86AssemblyWriterOptions? options = null)
        => X86Disassembler.Disassemble(this, options);
}

public sealed class X86DataSection
{
    public string Name { get; }
    public X86ObjectSectionKind Kind { get; }
    public int Alignment { get; }
    public ImmutableArray<byte> Data { get; }
    public int BssSize { get; }
    public ImmutableArray<X86ObjectRelocation> Relocations { get; }

    public X86DataSection(
        string name,
        X86ObjectSectionKind kind,
        int alignment,
        ImmutableArray<byte> data,
        int bssSize,
        ImmutableArray<X86ObjectRelocation> relocations)
    {
        Name = string.IsNullOrWhiteSpace(name) ? ".data" : name;
        Kind = kind;
        Alignment = Math.Max(1, alignment);
        Data = data.IsDefault ? ImmutableArray<byte>.Empty : data;
        BssSize = Math.Max(0, bssSize);
        Relocations = relocations.IsDefault ? ImmutableArray<X86ObjectRelocation>.Empty : relocations;
    }
}

public sealed class X86ObjectSymbol
{
    public string Name { get; }
    public string SectionName { get; }
    public int Offset { get; }
    public int Size { get; }
    public X86ObjectSymbolBinding Binding { get; }
    public X86ObjectSymbolKind Kind { get; }
    public bool IsTentative { get; }

    public X86ObjectSymbol(
        string name,
        string sectionName,
        int offset,
        int size,
        X86ObjectSymbolBinding binding,
        X86ObjectSymbolKind kind,
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

public sealed class X86ObjectRelocation
{
    public string SectionName { get; }
    public int Offset { get; }
    public string SymbolName { get; }
    public long Addend { get; }
    public X86ObjectRelocationKind Kind { get; }

    public X86ObjectRelocation(string sectionName, int offset, string symbolName, long addend, X86ObjectRelocationKind kind)
    {
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        SymbolName = symbolName ?? string.Empty;
        Addend = addend;
        Kind = kind;
    }
}

public sealed class X86LinkedImage
{
    public X86Program Source { get; }
    public ulong ImageBase { get; }
    public ulong EntryAddress { get; }
    public int EntryOffset { get; }
    public ImmutableDictionary<string, X86LinkedSection> Sections { get; }
    public ImmutableDictionary<string, ulong> SymbolAddresses { get; }
    public ImmutableArray<byte> Bytes { get; }

    public X86LinkedImage(
        X86Program source,
        ulong imageBase,
        ulong entryAddress,
        int entryOffset,
        IReadOnlyDictionary<string, X86LinkedSection> sections,
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

public sealed class X86LinkedSection
{
    public string Name { get; }
    public X86ObjectSectionKind Kind { get; }
    public int Offset { get; }
    public int Size { get; }
    public int Alignment { get; }
    public ulong Address { get; }

    public X86LinkedSection(string name, X86ObjectSectionKind kind, int offset, int size, int alignment, ulong address)
    {
        Name = name ?? string.Empty;
        Kind = kind;
        Offset = Math.Max(0, offset);
        Size = Math.Max(0, size);
        Alignment = Math.Max(1, alignment);
        Address = address;
    }
}

public enum X86ObjectSectionKind : byte
{
    Text,
    Rodata,
    Data,
    Bss,
}

public enum X86ObjectSymbolBinding : byte
{
    Local,
    Global,
    External,
}

public enum X86ObjectSymbolKind : byte
{
    None,
    Function,
    Object,
    Section,
}

public enum X86ObjectRelocationKind : byte
{
    None,
    Relative8,
    Relative32,
    AbsolutePointer,
    Absolute32,
    Absolute64,
    RipRelative32,
}

[Flags]
public enum X86IsaFlags : ulong
{
    None = 0,
    Sse = 1UL << 0,
    Sse2 = 1UL << 1,
    Avx = 1UL << 2,
    Avx2 = 1UL << 3,
    Sse3 = 1UL << 4,
    Ssse3 = 1UL << 5,
    Sse41 = 1UL << 6,
    Sse42 = 1UL << 7,
    Lzcnt = 1UL << 8,
    Avx512F = 1UL << 9,
    Avx512BW = 1UL << 10,
    Avx512DQ = 1UL << 11,
    Avx512CD = 1UL << 12,
    Avx512Vbmi = 1UL << 13,
    Avx512Vbmi2 = 1UL << 14,
    Avx10v2 = 1UL << 15,
    Popcnt = 1UL << 16,
    Bmi1 = 1UL << 17,
    Bmi2 = 1UL << 18,
    Fma = 1UL << 19,
    Aes = 1UL << 20,
    Pclmulqdq = 1UL << 21,
    Vpclmulqdq = 1UL << 22,
    Gfni = 1UL << 23,
    AvxVnni = 1UL << 24,
    AvxVnniInt8 = 1UL << 25,
    AvxVnniInt16 = 1UL << 26,
    Serialize = 1UL << 27,
}

public enum X86AbiKind : byte
{
    Cdecl,
    SysV64,
    WindowsX64,
}

internal enum X86InstructionFormat : byte
{
    None,
    Raw,
    NoOperands,
    Unary,
    Binary,
    Ternary,
    Branch,
    ConditionUnary,
    ConditionBinary,
}

public enum X86OperandKind : byte
{
    None,
    Register,
    Memory,
    Immediate,
    Symbol,
}

internal enum X86RegisterClass : byte
{
    Invalid,
    General,
    Vector,
}

public enum X86Condition : byte
{
    O = 0,
    No = 1,
    B = 2,
    C = 2,
    Nae = 2,
    Ae = 3,
    Nb = 3,
    Nc = 3,
    E = 4,
    Z = 4,
    Ne = 5,
    Nz = 5,
    Be = 6,
    Na = 6,
    A = 7,
    Nbe = 7,
    S = 8,
    Ns = 9,
    P = 10,
    Pe = 10,
    Np = 11,
    Po = 11,
    L = 12,
    Nge = 12,
    Ge = 13,
    Nl = 13,
    Le = 14,
    Ng = 14,
    G = 15,
    Nle = 15,
}

public enum X86InstrKind : ushort
{
    Invalid = 0,
    Raw,
    Nop,
    Ret,
    Push,
    Pop,
    Mov,
    Lea,
    Movsx,
    Movsxd,
    Movzx,
    Add,
    Or,
    Adc,
    Sbb,
    And,
    Sub,
    Xor,
    Cmp,
    Test,
    Inc,
    Dec,
    Neg,
    Not,
    Imul,
    Mul,
    Idiv,
    Div,
    Shl,
    Shr,
    Sar,
    Rol,
    Ror,
    Call,
    Jmp,
    Jcc,
    Setcc,
    Cmovcc,
    Cdq,
    Cqo,
    Cbw,
    Cwde,
    Cdqe,
    Leave,
    Int3,
    Ud2,
    Syscall,
    Movss,
    Movsd,
    Addss,
    Addsd,
    Subss,
    Subsd,
    Mulss,
    Mulsd,
    Divss,
    Divsd,
    Ucomiss,
    Ucomisd,
    Cvtsi2ss,
    Cvtsi2sd,
    Cvtss2sd,
    Cvtsd2ss,
    Cvttss2si,
    Cvttsd2si,
    Sqrtss,
    Sqrtsd,
    Movaps,
    Movups,
    Movapd,
    Movupd,
    Movdqa,
    Movdqu,
    Addps,
    Addpd,
    Subps,
    Subpd,
    Mulps,
    Mulpd,
    Divps,
    Divpd,
    Sqrtps,
    Sqrtpd,
    Andps,
    Andpd,
    Orps,
    Orpd,
    Xorps,
    Xorpd,
    Pxor,
    Movhlps,
    Movlhps,
    Unpcklps,
    Unpckhps,
    Unpcklpd,
    Unpckhpd,
    Shufps,
    Shufpd,
    Andnps,
    Andnpd,
    Minps,
    Minpd,
    Maxps,
    Maxpd,
    Cmpps,
    Cmppd,
    Movmskps,
    Movmskpd,
    Cvtdq2ps,
    Cvttps2dq,
    Cvtps2pd,
    Cvtpd2ps,
    Cvtdq2pd,
    Cvttpd2dq,
    Movd,
    Movq,
    Paddb,
    Paddw,
    Paddd,
    Paddq,
    Psubb,
    Psubw,
    Psubd,
    Psubq,
    Paddsb,
    Paddsw,
    Paddusb,
    Paddusw,
    Psubsb,
    Psubsw,
    Psubusb,
    Psubusw,
    Pmullw,
    Pmulhw,
    Pmulhuw,
    Pmuludq,
    Pand,
    Pandn,
    Por,
    Pcmpeqb,
    Pcmpeqw,
    Pcmpeqd,
    Pcmpgtb,
    Pcmpgtw,
    Pcmpgtd,
    Pminub,
    Pmaxub,
    Pminsw,
    Pmaxsw,
    Psadbw,
    Pmovmskb,
    Packsswb,
    Packssdw,
    Packuswb,
    Punpcklbw,
    Punpcklwd,
    Punpckldq,
    Punpcklqdq,
    Punpckhbw,
    Punpckhwd,
    Punpckhdq,
    Punpckhqdq,
    Pshufd,
    Pshuflw,
    Pshufhw,
    Pinsrw,
    Pextrw,
    Psllw,
    Pslld,
    Psllq,
    Psrlw,
    Psrld,
    Psrlq,
    Psraw,
    Psrad,
    Pslldq,
    Psrldq,
    Haddps,
    Haddpd,
    Movddup,
    Movshdup,
    Movsldup,
    Pshufb,
    Phaddw,
    Phaddd,
    Pabsb,
    Pabsw,
    Pabsd,
    Palignr,
    Pmulld,
    Pmuldq,
    Pminsb,
    Pminsd,
    Pminuw,
    Pminud,
    Pmaxsb,
    Pmaxsd,
    Pmaxuw,
    Pmaxud,
    Pcmpeqq,
    Packusdw,
    Ptest,
    Pmovsxbw,
    Pmovsxwd,
    Pmovsxdq,
    Pmovzxbw,
    Pmovzxwd,
    Pmovzxdq,
    Roundps,
    Roundpd,
    Blendps,
    Blendpd,
    Pblendw,
    Pextrb,
    Pextrd,
    Pextrq,
    Extractps,
    Pinsrb,
    Insertps,
    Pinsrd,
    Pinsrq,
    Pcmpgtq,
    Rcpps,
    Rcpss,
    Rsqrtps,
    Rsqrtss,
    Minss,
    Minsd,
    Maxss,
    Maxsd,
    Cmpss,
    Cmpsd,
    Comiss,
    Comisd,
    Cvtss2si,
    Cvtsd2si,
    Cvtps2dq,
    Cvtpd2dq,
    Movlps,
    Movhps,
    Movlpd,
    Movhpd,
    Movntps,
    Movntpd,
    Movntdq,
    Movntdqa,
    Movnti,
    Maskmovdqu,
    Lddqu,
    Pavgb,
    Pavgw,
    Pmaddwd,
    Addsubps,
    Addsubpd,
    Hsubps,
    Hsubpd,
    Phaddsw,
    Phsubw,
    Phsubd,
    Phsubsw,
    Pmaddubsw,
    Pmulhrsw,
    Psignb,
    Psignw,
    Psignd,
    Blendvps,
    Blendvpd,
    Pblendvb,
    Dpps,
    Dppd,
    Mpsadbw,
    Phminposuw,
    Roundss,
    Roundsd,
    Pmovsxbd,
    Pmovsxbq,
    Pmovsxwq,
    Pmovzxbd,
    Pmovzxbq,
    Pmovzxwq,
    Crc32,
    Lzcnt,
    Bsf,
    Bsr,
    Prefetchnta,
    Prefetcht0,
    Prefetcht1,
    Prefetcht2,
    Sfence,
    Lfence,
    Pause,
    Vzeroupper,
    Vzeroall,
    Vbroadcastss,
    Vbroadcastsd,
    Vpbroadcastb,
    Vpbroadcastw,
    Vpbroadcastd,
    Vpbroadcastq,
    Vbroadcasti128,
    Vinsertf128,
    Vinserti128,
    Vextractf128,
    Vextracti128,
    Vperm2i128,
    Vpermq,
    Vpermd,
    Vpblendd,
    Vpsllvd,
    Vpsllvq,
    Vpsrlvd,
    Vpsrlvq,
    Vpsravd,
    Vpsravq,
    Vpsllvw,
    Vpsrlvw,
    Vpsravw,
    Vpternlogd,
    Vpternlogq,
    Vpcmpb,
    Vpcmpub,
    Vpcmpw,
    Vpcmpuw,
    Vpcmpd,
    Vpcmpud,
    Vpcmpq,
    Vpcmpuq,
    Vpmovm2b,
    Vpmovm2w,
    Vpmovm2d,
    Vpmovm2q,
    Vpmovb2m,
    Vpmovw2m,
    Vpmovd2m,
    Vpmovq2m,
    Kmovb,
    Kmovw,
    Kmovd,
    Kmovq,
    Kortestb,
    Kortestw,
    Kortestd,
    Kortestq,
    Vpabsq,
    Vpmaxsq,
    Vpminsq,
    Vpmaxuq,
    Vpminuq,
    Vpsraq,
    Vpmullq,
    Vrndscaleps,
    Vrndscalepd,
    Vinserti32x4,
    Vextracti32x4,
    Vinserti64x4,
    Vextracti64x4,
    Vshufi64x2,
    Serialize,
    Popcnt,
    Tzcnt,
    Andn,
    Bextr,
    Blsr,
    Blsmsk,
    Blsi,
    Bzhi,
    Pdep,
    Pext,
    Mulx,
    Aesenc,
    Aesenclast,
    Aesdec,
    Aesdeclast,
    Aesimc,
    Aeskeygenassist,
    Pclmulqdq,
    Gf2p8mulb,
    Gf2p8affineqb,
    Gf2p8affineinvqb,
    Vbroadcastf128,
    Vbroadcastf32x2,
    Vbroadcasti32x2,
    Vbroadcastf32x4,
    Vbroadcasti32x4,
    Vbroadcastf64x2,
    Vbroadcasti64x2,
    Vbroadcastf32x8,
    Vbroadcasti32x8,
    Vbroadcastf64x4,
    Vbroadcasti64x4,
    Vinsertf32x4,
    Vextractf32x4,
    Vinsertf64x2,
    Vextractf64x2,
    Vinserti64x2,
    Vextracti64x2,
    Vinsertf32x8,
    Vextractf32x8,
    Vinserti32x8,
    Vextracti32x8,
    Vinsertf64x4,
    Vextractf64x4,
    Vperm2f128,
    Vpermilps,
    Vpermilpd,
    Vpermpd,
    Vpermps,
    Vpermb,
    Vpermw,
    Vpermt2b,
    Vpermi2b,
    Vpermt2w,
    Vpermi2w,
    Vpermt2d,
    Vpermi2d,
    Vpermt2q,
    Vpermi2q,
    Vpermt2ps,
    Vpermi2ps,
    Vpermt2pd,
    Vpermi2pd,
    Vshuff32x4,
    Vshuff64x2,
    Vshufi32x4,
    Valignd,
    Valignq,
    Vdbpsadbw,
    Vpmultishiftqb,
    Vpblendmb,
    Vpblendmw,
    Vpblendmd,
    Vpblendmq,
    Vblendmps,
    Vblendmpd,
    Vmaskmovps,
    Vmaskmovpd,
    Vpmaskmovd,
    Vpmaskmovq,
    Vmovdqa32,
    Vmovdqa64,
    Vmovdqu8,
    Vmovdqu16,
    Vmovdqu32,
    Vmovdqu64,
    Vcompressps,
    Vexpandps,
    Vcompresspd,
    Vexpandpd,
    Vpcompressd,
    Vpexpandd,
    Vpcompressq,
    Vpexpandq,
    Vpcompressb,
    Vpexpandb,
    Vpcompressw,
    Vpexpandw,
    Vpgatherdd,
    Vpgatherdq,
    Vpgatherqd,
    Vpgatherqq,
    Vgatherdps,
    Vgatherdpd,
    Vgatherqps,
    Vgatherqpd,
    Vprold,
    Vprolq,
    Vprord,
    Vprorq,
    Vprolvd,
    Vprolvq,
    Vprorvd,
    Vprorvq,
    Vptestmb,
    Vptestnmb,
    Vptestmw,
    Vptestnmw,
    Vptestmd,
    Vptestnmd,
    Vptestmq,
    Vptestnmq,
    Vpmovwb,
    Vpmovswb,
    Vpmovuswb,
    Vpmovdb,
    Vpmovsdb,
    Vpmovusdb,
    Vpmovqb,
    Vpmovsqb,
    Vpmovusqb,
    Vpmovdw,
    Vpmovsdw,
    Vpmovusdw,
    Vpmovqw,
    Vpmovsqw,
    Vpmovusqw,
    Vpmovqd,
    Vpmovsqd,
    Vpmovusqd,
    Vpandq,
    Vpandnq,
    Vporq,
    Vpxorq,
    Vpconflictd,
    Vpconflictq,
    Vplzcntd,
    Vplzcntq,
    Vgetexpps,
    Vgetexppd,
    Vgetexpss,
    Vgetexpsd,
    Vrcp14ps,
    Vrcp14pd,
    Vrcp14ss,
    Vrcp14sd,
    Vrsqrt14ps,
    Vrsqrt14pd,
    Vrsqrt14ss,
    Vrsqrt14sd,
    Vscalefps,
    Vscalefpd,
    Vscalefss,
    Vscalefsd,
    Vgetmantps,
    Vgetmantpd,
    Vgetmantss,
    Vgetmantsd,
    Vreduceps,
    Vreducepd,
    Vreducess,
    Vreducesd,
    Vrangeps,
    Vrangepd,
    Vrangess,
    Vrangesd,
    Vfixupimmps,
    Vfixupimmpd,
    Vfixupimmss,
    Vfixupimmsd,
    Vrndscaless,
    Vrndscalesd,
    Vfpclassps,
    Vfpclasspd,
    Vfpclassss,
    Vfpclasssd,
    Vcvtps2udq,
    Vcvttps2udq,
    Vcvtudq2ps,
    Vcvtpd2udq,
    Vcvttpd2udq,
    Vcvtudq2pd,
    Vcvtpd2qq,
    Vcvttpd2qq,
    Vcvtpd2uqq,
    Vcvttpd2uqq,
    Vcvtps2qq,
    Vcvttps2qq,
    Vcvtps2uqq,
    Vcvttps2uqq,
    Vcvtqq2pd,
    Vcvtuqq2pd,
    Vcvtqq2ps,
    Vcvtuqq2ps,
    Vcvtss2usi,
    Vcvtsd2usi,
    Vcvttss2usi,
    Vcvttsd2usi,
    Vcvtusi2ss,
    Vcvtusi2sd,
    Vtestps,
    Vtestpd,
    Vpdpbusd,
    Vpdpbusds,
    Vpdpwssd,
    Vpdpwssds,
    Vpdpbssd,
    Vpdpbssds,
    Vpdpbsud,
    Vpdpbsuds,
    Vpdpbuud,
    Vpdpbuuds,
    Vpdpwsud,
    Vpdpwsuds,
    Vpdpwusd,
    Vpdpwusds,
    Vpdpwuud,
    Vpdpwuuds,
    Vminmaxps,
    Vminmaxpd,
    Vminmaxss,
    Vminmaxsd,
    Vcvttps2dqs,
    Vcvttps2udqs,
    Vcvttpd2dqs,
    Vcvttpd2udqs,
    Vcvttps2qqs,
    Vcvttps2uqqs,
    Vcvttpd2qqs,
    Vcvttpd2uqqs,
    Vcvtps2ibs,
    Vcvtps2iubs,
    Vcvttps2ibs,
    Vcvttps2iubs,
    Vmovw,
    Vfmadd132ps,
    Vfmadd132pd,
    Vfmadd132ss,
    Vfmadd132sd,
    Vfmadd213ps,
    Vfmadd213pd,
    Vfmadd213ss,
    Vfmadd213sd,
    Vfmadd231ps,
    Vfmadd231pd,
    Vfmadd231ss,
    Vfmadd231sd,
    Vfmsub132ps,
    Vfmsub132pd,
    Vfmsub132ss,
    Vfmsub132sd,
    Vfmsub213ps,
    Vfmsub213pd,
    Vfmsub213ss,
    Vfmsub213sd,
    Vfmsub231ps,
    Vfmsub231pd,
    Vfmsub231ss,
    Vfmsub231sd,
    Vfnmadd132ps,
    Vfnmadd132pd,
    Vfnmadd132ss,
    Vfnmadd132sd,
    Vfnmadd213ps,
    Vfnmadd213pd,
    Vfnmadd213ss,
    Vfnmadd213sd,
    Vfnmadd231ps,
    Vfnmadd231pd,
    Vfnmadd231ss,
    Vfnmadd231sd,
    Vfnmsub132ps,
    Vfnmsub132pd,
    Vfnmsub132ss,
    Vfnmsub132sd,
    Vfnmsub213ps,
    Vfnmsub213pd,
    Vfnmsub213ss,
    Vfnmsub213sd,
    Vfnmsub231ps,
    Vfnmsub231pd,
    Vfnmsub231ss,
    Vfnmsub231sd,
    Vfmaddsub132ps,
    Vfmaddsub132pd,
    Vfmaddsub213ps,
    Vfmaddsub213pd,
    Vfmaddsub231ps,
    Vfmaddsub231pd,
    Vfmsubadd132ps,
    Vfmsubadd132pd,
    Vfmsubadd213ps,
    Vfmsubadd213pd,
    Vfmsubadd231ps,
    Vfmsubadd231pd,
    Cmpxchg,
    Xadd,
    Xchg,
    Mfence,
}

public enum X86Register : byte
{
    Rax = 0,
    Rcx = 1,
    Rdx = 2,
    Rbx = 3,
    Rsp = 4,
    Rbp = 5,
    Rsi = 6,
    Rdi = 7,
    R8 = 8,
    R9 = 9,
    R10 = 10,
    R11 = 11,
    R12 = 12,
    R13 = 13,
    R14 = 14,
    R15 = 15,
    Rip = 16,
    Xmm0 = 32,
    Xmm1 = 33,
    Xmm2 = 34,
    Xmm3 = 35,
    Xmm4 = 36,
    Xmm5 = 37,
    Xmm6 = 38,
    Xmm7 = 39,
    Xmm8 = 40,
    Xmm9 = 41,
    Xmm10 = 42,
    Xmm11 = 43,
    Xmm12 = 44,
    Xmm13 = 45,
    Xmm14 = 46,
    Xmm15 = 47,
    Xmm16 = 48,
    Xmm17 = 49,
    Xmm18 = 50,
    Xmm19 = 51,
    Xmm20 = 52,
    Xmm21 = 53,
    Xmm22 = 54,
    Xmm23 = 55,
    Xmm24 = 56,
    Xmm25 = 57,
    Xmm26 = 58,
    Xmm27 = 59,
    Xmm28 = 60,
    Xmm29 = 61,
    Xmm30 = 62,
    Xmm31 = 63,
    Ymm0 = 64,
    Ymm1 = 65,
    Ymm2 = 66,
    Ymm3 = 67,
    Ymm4 = 68,
    Ymm5 = 69,
    Ymm6 = 70,
    Ymm7 = 71,
    Ymm8 = 72,
    Ymm9 = 73,
    Ymm10 = 74,
    Ymm11 = 75,
    Ymm12 = 76,
    Ymm13 = 77,
    Ymm14 = 78,
    Ymm15 = 79,
    Ymm16 = 80,
    Ymm17 = 81,
    Ymm18 = 82,
    Ymm19 = 83,
    Ymm20 = 84,
    Ymm21 = 85,
    Ymm22 = 86,
    Ymm23 = 87,
    Ymm24 = 88,
    Ymm25 = 89,
    Ymm26 = 90,
    Ymm27 = 91,
    Ymm28 = 92,
    Ymm29 = 93,
    Ymm30 = 94,
    Ymm31 = 95,
    Zmm0 = 96,
    Zmm1 = 97,
    Zmm2 = 98,
    Zmm3 = 99,
    Zmm4 = 100,
    Zmm5 = 101,
    Zmm6 = 102,
    Zmm7 = 103,
    Zmm8 = 104,
    Zmm9 = 105,
    Zmm10 = 106,
    Zmm11 = 107,
    Zmm12 = 108,
    Zmm13 = 109,
    Zmm14 = 110,
    Zmm15 = 111,
    Zmm16 = 112,
    Zmm17 = 113,
    Zmm18 = 114,
    Zmm19 = 115,
    Zmm20 = 116,
    Zmm21 = 117,
    Zmm22 = 118,
    Zmm23 = 119,
    Zmm24 = 120,
    Zmm25 = 121,
    Zmm26 = 122,
    Zmm27 = 123,
    Zmm28 = 124,
    Zmm29 = 125,
    Zmm30 = 126,
    Zmm31 = 127,
    K0 = 128,
    K1 = 129,
    K2 = 130,
    K3 = 131,
    K4 = 132,
    K5 = 133,
    K6 = 134,
    K7 = 135,
    Invalid = 255,
}

public readonly struct X86Operand
{
    public X86OperandKind Kind { get; }
    public int Size { get; }
    public X86Register Register { get; }
    public X86Register BaseRegister { get; }
    public X86Register IndexRegister { get; }
    public int Scale { get; }
    public long Displacement { get; }
    public long Immediate { get; }
    public string? Symbol { get; }
    public long Addend { get; }
    public X86ObjectRelocationKind RelocationKind { get; }
    public bool IsRipRelative { get; }
    // A broadcast memory operand has the size of the whole operand it replaces
    public bool IsBroadcast { get; }
    public bool HasSymbol => !string.IsNullOrEmpty(Symbol);

    private X86Operand(
        X86OperandKind kind,
        int size,
        X86Register register,
        X86Register baseRegister,
        X86Register indexRegister,
        int scale,
        long displacement,
        long immediate,
        string? symbol,
        long addend,
        X86ObjectRelocationKind relocationKind,
        bool isRipRelative,
        bool isBroadcast = false)
    {
        Kind = kind;
        Size = Math.Max(0, size);
        Register = register;
        BaseRegister = baseRegister;
        IndexRegister = indexRegister;
        Scale = scale <= 0 ? 1 : scale;
        Displacement = displacement;
        Immediate = immediate;
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;
        Addend = addend;
        RelocationKind = relocationKind;
        IsRipRelative = isRipRelative;
        IsBroadcast = isBroadcast;
    }

    public static X86Operand None => default;

    public static X86Operand RegisterOperand(X86Register register, int size)
        => new X86Operand(X86OperandKind.Register, size, register, X86Register.Invalid, X86Register.Invalid, 1, 0, 0, null, 0, X86ObjectRelocationKind.None, false);

    public static X86Operand ImmediateOperand(long value, int size = 0)
        => new X86Operand(X86OperandKind.Immediate, size, X86Register.Invalid, X86Register.Invalid, X86Register.Invalid, 1, 0, value, null, 0, X86ObjectRelocationKind.None, false);

    public static X86Operand SymbolOperand(string symbol, int size, X86ObjectRelocationKind relocationKind = X86ObjectRelocationKind.Relative32, long addend = 0)
        => new X86Operand(X86OperandKind.Symbol, size, X86Register.Invalid, X86Register.Invalid, X86Register.Invalid, 1, 0, 0, symbol, addend, relocationKind, false);

    public static X86Operand Memory(
        X86Register baseRegister,
        long displacement = 0,
        int size = 0,
        X86Register indexRegister = X86Register.Invalid,
        int scale = 1,
        string? symbol = null,
        X86ObjectRelocationKind relocationKind = X86ObjectRelocationKind.None,
        long addend = 0,
        bool ripRelative = false)
        => new X86Operand(X86OperandKind.Memory, size, X86Register.Invalid, baseRegister, indexRegister, scale, displacement, 0, symbol, addend, relocationKind, ripRelative);

    public static X86Operand RipRelative(string symbol, long addend = 0, int size = 0)
        => Memory(X86Register.Rip, 0, size, X86Register.Invalid, 1, symbol, X86ObjectRelocationKind.RipRelative32, addend, true);

    public X86Operand WithSize(int size)
        => new X86Operand(Kind, size, Register, BaseRegister, IndexRegister, Scale, Displacement, Immediate, Symbol, Addend, RelocationKind, IsRipRelative, IsBroadcast);

    public X86Operand WithBroadcast(bool broadcast)
        => new X86Operand(Kind, Size, Register, BaseRegister, IndexRegister, Scale, Displacement, Immediate, Symbol, Addend, RelocationKind, IsRipRelative, broadcast);

    public X86Operand WithImmediate(long immediate)
        => new X86Operand(Kind, Size, Register, BaseRegister, IndexRegister, Scale, Displacement, immediate, Symbol, Addend, RelocationKind, IsRipRelative);

    public X86Operand WithDisplacement(long displacement)
        => new X86Operand(Kind, Size, Register, BaseRegister, IndexRegister, Scale, displacement, Immediate, Symbol, Addend, RelocationKind, IsRipRelative);

    public X86Operand WithSymbol(string symbol, X86ObjectRelocationKind relocationKind, long addend = 0)
        => new X86Operand(Kind, Size, Register, BaseRegister, IndexRegister, Scale, Displacement, Immediate, symbol, addend, relocationKind, IsRipRelative);
}

internal readonly struct X86InstructionMetadata
{
    public X86InstructionFormat Format { get; }
    public X86IsaFlags RequiredIsa { get; }
    public bool Requires64Bit { get; }

    public X86InstructionMetadata(X86InstructionFormat format, X86IsaFlags requiredIsa = X86IsaFlags.None, bool requires64Bit = false)
    {
        Format = format;
        RequiredIsa = requiredIsa;
        Requires64Bit = requires64Bit;
    }
}

public enum X86RoundingMode : byte
{
    None,
    Nearest,
    Down,
    Up,
    Zero,
}

public readonly struct X86Instruction
{
    public X86InstrKind Opcode { get; }
    public X86Operand Operand0 { get; }
    public X86Operand Operand1 { get; }
    public X86Operand Operand2 { get; }
    public X86Operand Operand3 { get; }
    public X86Condition Condition { get; }
    public ImmutableArray<byte> RawBytes { get; }
    public int OpMask { get; }
    public bool Zeroing { get; }
    public X86RoundingMode Rounding { get; }

    public X86Instruction(
        X86InstrKind opcode,
        X86Operand operand0 = default,
        X86Operand operand1 = default,
        X86Operand operand2 = default,
        X86Condition condition = X86Condition.E,
        ImmutableArray<byte> rawBytes = default,
        X86Operand operand3 = default,
        int opMask = 0,
        bool zeroing = false,
        X86RoundingMode rounding = X86RoundingMode.None)
    {
        Opcode = opcode;
        Operand0 = operand0;
        Operand1 = operand1;
        Operand2 = operand2;
        Operand3 = operand3;
        Condition = condition;
        RawBytes = rawBytes.IsDefault ? ImmutableArray<byte>.Empty : rawBytes;
        OpMask = opMask;
        Zeroing = zeroing;
        Rounding = rounding;
    }

    public int OperandCount
        => Operand0.Kind == X86OperandKind.None ? 0 : Operand1.Kind == X86OperandKind.None ? 1 : Operand2.Kind == X86OperandKind.None ? 2 : Operand3.Kind == X86OperandKind.None ? 3 : 4;

    public X86Operand GetOperand(int index)
        => index switch { 0 => Operand0, 1 => Operand1, 2 => Operand2, 3 => Operand3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    public X86Instruction WithOperand0(X86Operand operand)
        => new X86Instruction(Opcode, operand, Operand1, Operand2, Condition, RawBytes, Operand3, OpMask, Zeroing, Rounding);

    public X86Instruction WithOperand1(X86Operand operand)
        => new X86Instruction(Opcode, Operand0, operand, Operand2, Condition, RawBytes, Operand3, OpMask, Zeroing, Rounding);

    public X86Instruction WithOperand2(X86Operand operand)
        => new X86Instruction(Opcode, Operand0, Operand1, operand, Condition, RawBytes, Operand3, OpMask, Zeroing, Rounding);

    public X86Instruction WithMask(int opMask, bool zeroing)
        => new X86Instruction(Opcode, Operand0, Operand1, Operand2, Condition, RawBytes, Operand3, opMask, zeroing, Rounding);

    public X86Instruction WithRounding(X86RoundingMode rounding)
        => new X86Instruction(Opcode, Operand0, Operand1, Operand2, Condition, RawBytes, Operand3, OpMask, Zeroing, rounding);

    public static X86Instruction Quaternary(X86InstrKind opcode, X86Operand destination, X86Operand source1, X86Operand source2, X86Operand source3)
        => new X86Instruction(opcode, destination, source1, source2, operand3: source3);

    public static X86Instruction Raw(IEnumerable<byte> bytes)
        => new X86Instruction(X86InstrKind.Raw, rawBytes: bytes is null ? ImmutableArray<byte>.Empty : bytes.ToImmutableArray());

    public static X86Instruction Nop()
        => new X86Instruction(X86InstrKind.Nop);

    public static X86Instruction Ret()
        => new X86Instruction(X86InstrKind.Ret);

    public static X86Instruction Unary(X86InstrKind opcode, X86Operand operand)
        => new X86Instruction(opcode, operand);

    public static X86Instruction Binary(X86InstrKind opcode, X86Operand destination, X86Operand source)
        => new X86Instruction(opcode, destination, source);

    public static X86Instruction Ternary(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Operand immediate)
        => new X86Instruction(opcode, destination, source, immediate);

    public static X86Instruction Branch(X86InstrKind opcode, X86Operand target)
        => new X86Instruction(opcode, target);

    public static X86Instruction ConditionalBranch(X86Condition condition, X86Operand target)
        => new X86Instruction(X86InstrKind.Jcc, target, condition: condition);

    public static X86Instruction Setcc(X86Condition condition, X86Operand target)
        => new X86Instruction(X86InstrKind.Setcc, target, condition: condition);

    public static X86Instruction Cmovcc(X86Condition condition, X86Operand destination, X86Operand source)
        => new X86Instruction(X86InstrKind.Cmovcc, destination, source, condition: condition);

    public static X86Instruction AvxTernary(X86InstrKind opcode, X86Operand destination, X86Operand source1, X86Operand source2)
        => new X86Instruction(opcode, destination, source1, source2);
}

internal sealed class X86InstructionBuilder
{
    private readonly X86Target _target;
    private readonly List<X86Instruction> _instructions = new List<X86Instruction>();
    private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
    private int _position;

    public X86InstructionBuilder(X86Target target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public int Count => _instructions.Count;
    public int Position => _position;

    public void Emit(X86Instruction instruction)
    {
        _instructions.Add(instruction);
        _position = checked(_position + X86CodeEncoder.GetEncodedLength(instruction, _target));
    }

    public bool HasLabel(string label)
        => !string.IsNullOrWhiteSpace(label) && _labels.ContainsKey(label);

    public void DefineLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("x86 label must not be empty", nameof(label));
        if (_labels.ContainsKey(label))
            throw new ArgumentException($"Duplicate label: {label}", nameof(label));
        _labels.Add(label, Position);
    }

    public X86TextSection ToTextSection()
        => new X86TextSection(_instructions, _labels);

    public X86Program ToObject(string entrySymbol = "")
        => new X86Program(_target, ToTextSection(), ImmutableArray<X86DataSection>.Empty, ImmutableArray<X86ObjectSymbol>.Empty, entrySymbol);

    public void Clear()
    {
        _instructions.Clear();
        _labels.Clear();
        _position = 0;
    }
}

public sealed class X86Target
{
    public static X86Target I386 { get; } = new X86Target(32, X86AbiKind.Cdecl, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little);
    public static X86Target I386Windows { get; } = new X86Target(32, X86AbiKind.Cdecl, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little, OperatingSystemKind.Windows);
    public static X86Target I386Linux { get; } = new X86Target(32, X86AbiKind.Cdecl, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little, OperatingSystemKind.Linux);
    public static X86Target X64SysV { get; } = new X86Target(64, X86AbiKind.SysV64, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little);
    public static X86Target X64Linux { get; } = new X86Target(64, X86AbiKind.SysV64, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little, OperatingSystemKind.Linux);
    public static X86Target X64Windows { get; } = new X86Target(64, X86AbiKind.WindowsX64, X86IsaFlags.Sse | X86IsaFlags.Sse2, TargetEndianness.Little, OperatingSystemKind.Windows);
    public static X86Target FromTargetInfo(Cnidaria.C.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.I386 and not TargetArchitectureKind.X86_64)
            throw new ArgumentException("Target architecture is not x86", nameof(target));

        return CreateFromDescriptor(target.Architecture, target.OperatingSystem, target.ArchitectureFeatures, target.Endianness);
    }

    public static X86Target FromTargetInfo(Cnidaria.Cs.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.I386 and not TargetArchitectureKind.X86_64)
            throw new ArgumentException("Target architecture is not x86", nameof(target));

        return CreateFromDescriptor(target.Architecture, target.OperatingSystem, target.ArchitectureFeatures, target.Endianness);
    }

    private static X86Target CreateFromDescriptor(TargetArchitectureKind architecture, OperatingSystemKind operatingSystem, TargetArchitectureFeatures features, TargetEndianness endianness)
    {
        var isa = X86IsaFlags.None;
        if ((features & TargetArchitectureFeatures.X86Sse2) != 0)
            isa |= X86IsaFlags.Sse | X86IsaFlags.Sse2;
        if ((features & TargetArchitectureFeatures.X86Sse3) != 0)
            isa |= X86IsaFlags.Sse3;
        if ((features & TargetArchitectureFeatures.X86Ssse3) != 0)
            isa |= X86IsaFlags.Ssse3;
        if ((features & TargetArchitectureFeatures.X86Sse41) != 0)
            isa |= X86IsaFlags.Sse41;
        if ((features & TargetArchitectureFeatures.X86Sse42) != 0)
            isa |= X86IsaFlags.Sse42 | X86IsaFlags.Popcnt;
        if ((features & TargetArchitectureFeatures.X86Lzcnt) != 0)
            isa |= X86IsaFlags.Lzcnt;
        if ((features & TargetArchitectureFeatures.X86Avx) != 0)
            isa |= X86IsaFlags.Avx;
        if ((features & TargetArchitectureFeatures.X86Avx2) != 0)
            isa |= X86IsaFlags.Avx | X86IsaFlags.Avx2 | X86IsaFlags.Bmi1 | X86IsaFlags.Bmi2 | X86IsaFlags.Fma;
        if ((features & TargetArchitectureFeatures.X86Avx512) != 0)
            isa |= X86IsaFlags.Avx512F | X86IsaFlags.Avx512BW | X86IsaFlags.Avx512DQ | X86IsaFlags.Avx512CD;
        if ((features & TargetArchitectureFeatures.X86Avx512Vbmi) != 0)
            isa |= X86IsaFlags.Avx512Vbmi;
        if ((features & TargetArchitectureFeatures.X86Avx512Vbmi2) != 0)
            isa |= X86IsaFlags.Avx512Vbmi2;
        if ((features & TargetArchitectureFeatures.X86Avx10v1) != 0)
            isa |= X86IsaFlags.Avx512F | X86IsaFlags.Avx512BW | X86IsaFlags.Avx512DQ | X86IsaFlags.Avx512CD | X86IsaFlags.Avx512Vbmi | X86IsaFlags.Avx512Vbmi2;
        if ((features & TargetArchitectureFeatures.X86Avx10v2) != 0)
            isa |= X86IsaFlags.Avx10v2;
        if ((features & TargetArchitectureFeatures.X86AvxVnni) != 0)
            isa |= X86IsaFlags.AvxVnni;
        if ((features & TargetArchitectureFeatures.X86AvxVnniInt) != 0)
            isa |= X86IsaFlags.AvxVnniInt8 | X86IsaFlags.AvxVnniInt16;
        if ((features & TargetArchitectureFeatures.X86Aes) != 0)
            isa |= X86IsaFlags.Aes | X86IsaFlags.Pclmulqdq;
        if ((features & TargetArchitectureFeatures.X86Vpclmulqdq) != 0)
            isa |= X86IsaFlags.Vpclmulqdq;
        if ((features & TargetArchitectureFeatures.X86Gfni) != 0)
            isa |= X86IsaFlags.Gfni;
        if ((features & TargetArchitectureFeatures.X86Serialize) != 0)
            isa |= X86IsaFlags.Serialize;

        if (architecture == TargetArchitectureKind.I386)
            return new X86Target(32, X86AbiKind.Cdecl, isa, endianness, operatingSystem);

        return new X86Target(64, operatingSystem == OperatingSystemKind.Windows ? X86AbiKind.WindowsX64 : X86AbiKind.SysV64, isa | X86IsaFlags.Sse | X86IsaFlags.Sse2, endianness, operatingSystem);
    }

    public int XLen { get; }
    public X86AbiKind Abi { get; }
    public X86IsaFlags Isa { get; }
    public TargetEndianness Endianness { get; }
    public OperatingSystemKind OperatingSystem { get; }
    public bool Is32Bit => XLen == 32;
    public bool Is64Bit => XLen == 64;
    public bool HasSse => Has(X86IsaFlags.Sse);
    public bool HasSse2 => Has(X86IsaFlags.Sse2);
    public bool HasAvx => Has(X86IsaFlags.Avx);
    public bool HasAvx2 => Has(X86IsaFlags.Avx2);

    public X86Target(int xlen, X86AbiKind abi, X86IsaFlags isa, TargetEndianness endianness = TargetEndianness.Little, OperatingSystemKind operatingSystem = OperatingSystemKind.None)
    {
        if (xlen is not 32 and not 64)
            throw new ArgumentOutOfRangeException(nameof(xlen));
        if (xlen == 32 && abi is X86AbiKind.SysV64 or X86AbiKind.WindowsX64)
            throw new ArgumentException("64-bit x86 ABI requires x86-64", nameof(abi));
        if (endianness != TargetEndianness.Little)
            throw new ArgumentException("x86 targets are little-endian", nameof(endianness));

        XLen = xlen;
        Abi = abi;
        Isa = isa;
        Endianness = endianness;
        OperatingSystem = operatingSystem;
    }

    public bool Has(X86IsaFlags flags)
        => (Isa & flags) == flags;

    public override string ToString()
    {
        if (Is32Bit)
            return OperatingSystem switch
            {
                OperatingSystemKind.Windows => "i386-windows" + FormatIsaSuffix(),
                OperatingSystemKind.Linux => "i386-linux" + FormatIsaSuffix(),
                _ => "i386" + FormatIsaSuffix(),
            };
        return OperatingSystem switch
        {
            OperatingSystemKind.Windows => "x86_64-windows" + FormatIsaSuffix(),
            OperatingSystemKind.Linux => "x86_64-linux" + FormatIsaSuffix(),
            _ => "x86_64" + FormatIsaSuffix(),
        };
    }

    private string FormatIsaSuffix()
    {
        var suffix = new StringBuilder();
        if (HasSse2)
            suffix.Append("+sse2");
        if (HasAvx)
            suffix.Append("+avx");
        if (HasAvx2)
            suffix.Append("+avx2");
        return suffix.ToString();
    }
}

internal static class X86Registers
{
    public const X86Register ReturnValue0 = X86Register.Rax;
    public const X86Register ReturnValue1SysV = X86Register.Rdx;
    public const X86Register ReturnValue1Windows = X86Register.Rdx;
    public const X86Register StackPointer = X86Register.Rsp;
    public const X86Register FramePointer = X86Register.Rbp;
    public const X86Register InstructionPointer = X86Register.Rip;
    public const X86Register Scratch0 = X86Register.R10;
    public const X86Register Scratch1 = X86Register.R11;

    public static ImmutableArray<X86Register> I386ArgumentRegisters { get; } = ImmutableArray<X86Register>.Empty;
    public static ImmutableArray<X86Register> I386CallerSaved { get; } = ImmutableArray.Create(X86Register.Rax, X86Register.Rcx, X86Register.Rdx);
    public static ImmutableArray<X86Register> I386CalleeSaved { get; } = ImmutableArray.Create(X86Register.Rbx, X86Register.Rsi, X86Register.Rdi, X86Register.Rbp);
    public static ImmutableArray<X86Register> I386AllocatableGprs { get; } = ImmutableArray.Create(X86Register.Rax, X86Register.Rcx, X86Register.Rdx, X86Register.Rbx, X86Register.Rsi, X86Register.Rdi);
    public static ImmutableArray<X86Register> I386XmmRegisters { get; } = CreateRange(X86Register.Xmm0, 8);
    public static ImmutableArray<X86Register> I386YmmRegisters { get; } = CreateRange(X86Register.Ymm0, 8);

    public static ImmutableArray<X86Register> SysV64IntegerArguments { get; } = ImmutableArray.Create(X86Register.Rdi, X86Register.Rsi, X86Register.Rdx, X86Register.Rcx, X86Register.R8, X86Register.R9);
    public static ImmutableArray<X86Register> Windows64IntegerArguments { get; } = ImmutableArray.Create(X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9);
    public static ImmutableArray<X86Register> SysV64CallerSavedGprs { get; } = ImmutableArray.Create(X86Register.Rax, X86Register.Rcx, X86Register.Rdx, X86Register.Rsi, X86Register.Rdi, X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11);
    public static ImmutableArray<X86Register> Windows64CallerSavedGprs { get; } = ImmutableArray.Create(X86Register.Rax, X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11);
    public static ImmutableArray<X86Register> SysV64CalleeSavedGprs { get; } = ImmutableArray.Create(X86Register.Rbx, X86Register.Rbp, X86Register.R12, X86Register.R13, X86Register.R14, X86Register.R15);
    public static ImmutableArray<X86Register> Windows64CalleeSavedGprs { get; } = ImmutableArray.Create(X86Register.Rbx, X86Register.Rbp, X86Register.Rsi, X86Register.Rdi, X86Register.R12, X86Register.R13, X86Register.R14, X86Register.R15);
    public static ImmutableArray<X86Register> X64AllocatableGprs { get; } = ImmutableArray.Create(X86Register.Rax, X86Register.Rcx, X86Register.Rdx, X86Register.Rbx, X86Register.Rsi, X86Register.Rdi, X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11, X86Register.R12, X86Register.R13, X86Register.R14, X86Register.R15);
    public static ImmutableArray<X86Register> X64XmmRegisters { get; } = CreateRange(X86Register.Xmm0, 16);
    public static ImmutableArray<X86Register> X64YmmRegisters { get; } = CreateRange(X86Register.Ymm0, 16);

    private static readonly string[] Gpr8 = { "al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil", "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b" };
    private static readonly string[] Gpr16 = { "ax", "cx", "dx", "bx", "sp", "bp", "si", "di", "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w" };
    private static readonly string[] Gpr32 = { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi", "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d" };
    private static readonly string[] Gpr64 = { "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15" };
    private static readonly Dictionary<string, X86RegisterName> Names = CreateNameMap();

    public static bool IsGeneral(X86Register register)
        => register >= X86Register.Rax && register <= X86Register.R15;

    public static bool IsXmm(X86Register register)
        => register >= X86Register.Xmm0 && register <= X86Register.Xmm31;

    public static bool IsYmm(X86Register register)
        => register >= X86Register.Ymm0 && register <= X86Register.Ymm31;

    public static bool IsZmm(X86Register register)
        => register >= X86Register.Zmm0 && register <= X86Register.Zmm31;

    public static bool IsMask(X86Register register)
        => register >= X86Register.K0 && register <= X86Register.K7;

    public static bool IsVector(X86Register register)
        => IsXmm(register) || IsYmm(register) || IsZmm(register);

    public static X86Register Vector(int index, int size)
        => (X86Register)(index + size switch { 16 => (int)X86Register.Xmm0, 32 => (int)X86Register.Ymm0, _ => (int)X86Register.Zmm0 });

    public static bool IsInstructionPointer(X86Register register)
        => register == X86Register.Rip;

    public static int Index(X86Register register)
    {
        if (IsGeneral(register))
            return (int)register;
        if (IsXmm(register))
            return (int)register - (int)X86Register.Xmm0;
        if (IsYmm(register))
            return (int)register - (int)X86Register.Ymm0;
        if (IsZmm(register))
            return (int)register - (int)X86Register.Zmm0;
        if (IsMask(register))
            return (int)register - (int)X86Register.K0;
        throw new ArgumentOutOfRangeException(nameof(register));
    }

    public static X86RegisterClass GetClass(X86Register register)
    {
        if (IsGeneral(register))
            return X86RegisterClass.General;
        if (IsVector(register))
            return X86RegisterClass.Vector;
        return X86RegisterClass.Invalid;
    }

    public static string Format(X86Register register, int size = 0)
    {
        if (register == X86Register.Invalid)
            return "invalid";
        if (register == X86Register.Rip)
            return "rip";
        if (IsXmm(register))
            return $"xmm{Index(register)}";
        if (IsYmm(register))
            return $"ymm{Index(register)}";
        if (IsZmm(register))
            return $"zmm{Index(register)}";
        if (IsMask(register))
            return $"k{Index(register)}";
        if (!IsGeneral(register))
            throw new ArgumentOutOfRangeException(nameof(register));

        var index = Index(register);
        return size switch
        {
            1 => Gpr8[index],
            2 => Gpr16[index],
            4 => Gpr32[index],
            8 => Gpr64[index],
            _ => Gpr64[index],
        };
    }

    public static bool TryParse(string text, out X86Register register, out int size)
    {
        if (text is null)
        {
            register = X86Register.Invalid;
            size = 0;
            return false;
        }

        if (Names.TryGetValue(text.Trim().ToLowerInvariant(), out var name))
        {
            register = name.Register;
            size = name.Size;
            return true;
        }

        register = X86Register.Invalid;
        size = 0;
        return false;
    }

    public static X86Register Parse(string text, out int size)
    {
        if (TryParse(text, out var register, out size))
            return register;
        throw new FormatException($"Invalid register: {text}");
    }

    private static Dictionary<string, X86RegisterName> CreateNameMap()
    {
        var map = new Dictionary<string, X86RegisterName>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 16; i++)
        {
            Add(map, Gpr8[i], (X86Register)i, 1);
            Add(map, Gpr16[i], (X86Register)i, 2);
            Add(map, Gpr32[i], (X86Register)i, 4);
            Add(map, Gpr64[i], (X86Register)i, 8);
        }
        for (var i = 0; i < 32; i++)
        {
            Add(map, $"xmm{i}", (X86Register)((int)X86Register.Xmm0 + i), 16);
            Add(map, $"ymm{i}", (X86Register)((int)X86Register.Ymm0 + i), 32);
            Add(map, $"zmm{i}", (X86Register)((int)X86Register.Zmm0 + i), 64);
        }
        for (var i = 0; i < 8; i++)
            Add(map, $"k{i}", (X86Register)((int)X86Register.K0 + i), 8);
        Add(map, "rip", X86Register.Rip, 8);
        return map;
    }

    private static void Add(Dictionary<string, X86RegisterName> map, string name, X86Register register, int size)
        => map[name] = new X86RegisterName(register, size);

    private static ImmutableArray<X86Register> CreateRange(X86Register first, int count)
    {
        var builder = ImmutableArray.CreateBuilder<X86Register>(count);
        var start = (int)first;
        for (var i = 0; i < count; i++)
            builder.Add((X86Register)(start + i));
        return builder.MoveToImmutable();
    }

    private readonly struct X86RegisterName
    {
        public X86Register Register { get; }
        public int Size { get; }

        public X86RegisterName(X86Register register, int size)
        {
            Register = register;
            Size = size;
        }
    }
}

internal static class X86InstructionTable
{
    private static readonly string[] Mnemonics = CreateMnemonics();
    private static readonly Dictionary<string, X86InstrKind> ByMnemonic = CreateMnemonicMap();

    public static X86InstructionMetadata Get(X86InstrKind opcode)
    {
        if (opcode == X86InstrKind.Raw)
            return new X86InstructionMetadata(X86InstructionFormat.Raw);
        var forms = X86Forms.ForKind(opcode);
        if (forms.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(opcode));
        var form = forms[0];
        var format = opcode switch
        {
            X86InstrKind.Call or X86InstrKind.Jmp or X86InstrKind.Jcc => X86InstructionFormat.Branch,
            X86InstrKind.Setcc => X86InstructionFormat.ConditionUnary,
            X86InstrKind.Cmovcc => X86InstructionFormat.ConditionBinary,
            _ => form.ExplicitOperandCount switch
            {
                0 => X86InstructionFormat.NoOperands,
                1 => X86InstructionFormat.Unary,
                2 => X86InstructionFormat.Binary,
                _ => X86InstructionFormat.Ternary,
            },
        };
        return new X86InstructionMetadata(format, form.Isa, form.Has(X86FormFlags.Mode64));
    }

    public static X86InstrKind GetOpcode(string mnemonic)
    {
        if (mnemonic is null)
            throw new ArgumentNullException(nameof(mnemonic));
        if (ByMnemonic.TryGetValue(mnemonic.Trim().ToLowerInvariant(), out var opcode))
            return opcode;
        throw new FormatException($"Unknown mnemonic: {mnemonic}");
    }

    public static bool TryGetOpcode(string mnemonic, out X86InstrKind opcode)
    {
        opcode = X86InstrKind.Invalid;
        return mnemonic is not null && ByMnemonic.TryGetValue(mnemonic.Trim().ToLowerInvariant(), out opcode);
    }

    public static string GetMnemonic(X86InstrKind opcode)
        => (uint)opcode < (uint)Mnemonics.Length ? Mnemonics[(int)opcode] : "invalid";

    private static string[] CreateMnemonics()
    {
        var kinds = Enum.GetValues<X86InstrKind>();
        var mnemonics = new string[kinds.Max(k => (int)k) + 1];
        foreach (var kind in kinds)
        {
            mnemonics[(int)kind] = kind switch
            {
                X86InstrKind.Raw => ".byte",
                X86InstrKind.Jcc => "j",
                X86InstrKind.Setcc => "set",
                X86InstrKind.Cmovcc => "cmov",
                _ => kind.ToString().ToLowerInvariant(),
            };
        }
        return mnemonics;
    }

    private static Dictionary<string, X86InstrKind> CreateMnemonicMap()
    {
        var map = new Dictionary<string, X86InstrKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in Enum.GetValues<X86InstrKind>())
        {
            if (kind is X86InstrKind.Invalid || X86Forms.ForKind(kind).Count == 0 && kind != X86InstrKind.Raw)
                continue;
            var mnemonic = GetMnemonic(kind);
            if (mnemonic != "invalid" && !mnemonic.StartsWith("j", StringComparison.Ordinal) && !mnemonic.StartsWith("set", StringComparison.Ordinal) && !mnemonic.StartsWith("cmov", StringComparison.Ordinal))
                map[mnemonic] = kind;
        }
        map["sal"] = X86InstrKind.Shl;
        foreach (var form in X86Forms.Every)
        {
            if (form.Mnemonic is not null)
                map[form.Mnemonic] = form.Kind;
        }
        return map;
    }

}

internal static class X86Conditions
{
    private static readonly Dictionary<string, X86Condition> Names = CreateNameMap();

    public static string Format(X86Condition condition)
    {
        return condition switch
        {
            X86Condition.O => "o",
            X86Condition.No => "no",
            X86Condition.B => "b",
            X86Condition.Ae => "ae",
            X86Condition.E => "e",
            X86Condition.Ne => "ne",
            X86Condition.Be => "be",
            X86Condition.A => "a",
            X86Condition.S => "s",
            X86Condition.Ns => "ns",
            X86Condition.P => "p",
            X86Condition.Np => "np",
            X86Condition.L => "l",
            X86Condition.Ge => "ge",
            X86Condition.Le => "le",
            X86Condition.G => "g",
            _ => "e",
        };
    }

    public static bool TryParse(string text, out X86Condition condition)
    {
        condition = X86Condition.E;
        return text is not null && Names.TryGetValue(text.Trim().ToLowerInvariant(), out condition);
    }

    public static X86Condition Parse(string text)
    {
        if (TryParse(text, out var condition))
            return condition;
        throw new FormatException($"Invalid condition: {text}");
    }

    public static bool TryParseConditionalMnemonic(string mnemonic, string prefix, out X86Condition condition)
    {
        condition = X86Condition.E;
        if (mnemonic is null || !mnemonic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var suffix = mnemonic.Substring(prefix.Length);
        return suffix.Length != 0 && TryParse(suffix, out condition);
    }

    private static Dictionary<string, X86Condition> CreateNameMap()
    {
        var map = new Dictionary<string, X86Condition>(StringComparer.OrdinalIgnoreCase);
        Add(map, X86Condition.O, "o");
        Add(map, X86Condition.No, "no");
        Add(map, X86Condition.B, "b", "c", "nae");
        Add(map, X86Condition.Ae, "ae", "nb", "nc");
        Add(map, X86Condition.E, "e", "z");
        Add(map, X86Condition.Ne, "ne", "nz");
        Add(map, X86Condition.Be, "be", "na");
        Add(map, X86Condition.A, "a", "nbe");
        Add(map, X86Condition.S, "s");
        Add(map, X86Condition.Ns, "ns");
        Add(map, X86Condition.P, "p", "pe");
        Add(map, X86Condition.Np, "np", "po");
        Add(map, X86Condition.L, "l", "nge");
        Add(map, X86Condition.Ge, "ge", "nl");
        Add(map, X86Condition.Le, "le", "ng");
        Add(map, X86Condition.G, "g", "nle");
        return map;
    }

    private static void Add(Dictionary<string, X86Condition> map, X86Condition condition, params string[] names)
    {
        foreach (var name in names)
            map[name] = condition;
    }
}
