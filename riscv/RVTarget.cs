using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.RiscV
{

    internal sealed class RiscVProgram
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

        public RVLinkedImage LinkFlat(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
            => RVObjectLinker.LinkFlat(this, imageBase, externalSymbols);

        public byte[] ToExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
            => LinkFlat(imageBase, externalSymbols).Bytes.ToArray();
    }

    internal sealed class RVTextSection
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

        public byte[] Encode(RVTarget target)
            => RiscVCodeEncoder.Encode(Instructions, target, Labels);

        public string Format(RiscVAssemblyWriterOptions? options = null)
            => RiscVDisassembler.Disassemble(this, options);
    }

    internal sealed class RVDataSection
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

    internal sealed class RVObjectSymbol
    {
        public string Name { get; }
        public string SectionName { get; }
        public int Offset { get; }
        public int Size { get; }
        public RVObjectSymbolBinding Binding { get; }
        public RVObjectSymbolKind Kind { get; }

        public RVObjectSymbol(
            string name,
            string sectionName,
            int offset,
            int size,
            RVObjectSymbolBinding binding,
            RVObjectSymbolKind kind)
        {
            Name = name ?? string.Empty;
            SectionName = sectionName ?? string.Empty;
            Offset = Math.Max(0, offset);
            Size = Math.Max(0, size);
            Binding = binding;
            Kind = kind;
        }
    }

    internal sealed class RVObjectRelocation
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

    internal sealed class RVLinkedImage
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

    internal sealed class RVLinkedSection
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

    internal enum RVObjectSectionKind : byte
    {
        Text,
        Rodata,
        Data,
        Bss,
    }

    internal enum RVObjectSymbolBinding : byte
    {
        Local,
        Global,
        External,
    }

    internal enum RVObjectSymbolKind : byte
    {
        None,
        Function,
        Object,
        Section,
    }

    internal enum RVObjectRelocationKind : byte
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
    internal enum RVIsaFlags : ulong
    {
        None = 0,
        I = 1UL << 0,
        M = 1UL << 1,
        A = 1UL << 2,
        F = 1UL << 3,
        D = 1UL << 4,
        C = 1UL << 5,
        V = 1UL << 6,
        Zicsr = 1UL << 16,
        Zifencei = 1UL << 17,
        Privileged = 1UL << 32,
    }

    internal enum RVAbiKind : byte
    {
        Ilp32,
        Ilp32F,
        Ilp32D,
        Lp64,
        Lp64F,
        Lp64D,
    }

    internal enum RVInstructionFormat : byte
    {
        None,
        Raw,
        R,
        I,
        ShiftI,
        S,
        FloatLoad,
        FloatStore,
        FloatRRR,
        FloatCompare,
        FloatConvertFromInteger,
        FloatConvertToInteger,
        FloatConvert,
        FloatMoveToInteger,
        FloatMoveFromInteger,
        B,
        U,
        J,
        Fence,
        System,
        Csr,
        CsrImmediate,
        Amo,
        VectorConfig,
        VectorOp,
        VectorLoad,
        VectorStore,
        PrivilegedFence,
    }

    internal enum RVRelocationKind : byte
    {
        None,
        RelativeBranch,
        RelativeJal,
        JalrLow12,
        AbsoluteLow12,
        AbsoluteUpper20
    }
    [Flags]
    internal enum RVInstructionFlags : byte
    {
        None = 0,
        VectorUnmasked = 1 << 0,
        AtomicAcquire = 1 << 1,
        AtomicRelease = 1 << 2,
    }
    internal enum RVInstrKind : ushort
    {
        Invalid = 0,
        Raw32,
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
    }

    internal enum RVRegister : byte
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
        SAtp = 0x180,
        SContext = 0x5A8,
        HStatus = 0x600,
        HEDeleg = 0x602,
        HIDeleg = 0x603,
        HIe = 0x604,
        HCounterEn = 0x606,
        HGEIe = 0x607,
        HTVal = 0x643,
        HIp = 0x644,
        HVIp = 0x645,
        HGEIp = 0xE12,
        HEnvCfg = 0x60A,
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
        Dcsr = 0x7B0,
        Dpc = 0x7B1,
        DScratch0 = 0x7B2,
        DScratch1 = 0x7B3,
        VLenB = 0xC22,
    }

    internal sealed class RVTarget
    {
        public static RVTarget Rv32I { get; } = new RVTarget(32, RVAbiKind.Ilp32, RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei, TargetEndianness.Little);
        public static RVTarget Rv64I { get; } = new RVTarget(64, RVAbiKind.Lp64, RVIsaFlags.I | RVIsaFlags.Zicsr | RVIsaFlags.Zifencei, TargetEndianness.Little);
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
            if ((features & TargetArchitectureFeatures.RiscVF) != 0)
                flags |= RVIsaFlags.F;
            if ((features & TargetArchitectureFeatures.RiscVD) != 0)
                flags |= RVIsaFlags.D;
            if ((features & TargetArchitectureFeatures.RiscVC) != 0)
                flags |= RVIsaFlags.C;
            if ((features & TargetArchitectureFeatures.RiscVV) != 0)
                flags |= RVIsaFlags.V;
            if ((features & TargetArchitectureFeatures.RiscVPrivileged) != 0)
                flags |= RVIsaFlags.Privileged;

            var abi = target.Architecture == TargetArchitectureKind.RiscV64
                ? ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Lp64D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Lp64F : RVAbiKind.Lp64)
                : ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Ilp32D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Ilp32F : RVAbiKind.Ilp32);

            return new RVTarget(target.PointerSize * 8, abi, flags, target.Endianness);
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
            if ((features & TargetArchitectureFeatures.RiscVF) != 0)
                flags |= RVIsaFlags.F;
            if ((features & TargetArchitectureFeatures.RiscVD) != 0)
                flags |= RVIsaFlags.D;
            if ((features & TargetArchitectureFeatures.RiscVC) != 0)
                flags |= RVIsaFlags.C;
            if ((features & TargetArchitectureFeatures.RiscVV) != 0)
                flags |= RVIsaFlags.V;
            if ((features & TargetArchitectureFeatures.RiscVPrivileged) != 0)
                flags |= RVIsaFlags.Privileged;

            var abi = target.Architecture == TargetArchitectureKind.RiscV64
                ? ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Lp64D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Lp64F : RVAbiKind.Lp64)
                : ((flags & RVIsaFlags.D) != 0 ? RVAbiKind.Ilp32D : (flags & RVIsaFlags.F) != 0 ? RVAbiKind.Ilp32F : RVAbiKind.Ilp32);

            return new RVTarget(target.PointerSize * 8, abi, flags, target.Endianness);
        }

        public int XLen { get; }
        public RVAbiKind Abi { get; }
        public RVIsaFlags Isa { get; }
        public TargetEndianness Endianness { get; }
        public bool Is32Bit => XLen == 32;
        public bool Is64Bit => XLen == 64;
        public bool HasM => Has(RVIsaFlags.M);
        public bool HasA => Has(RVIsaFlags.A);
        public bool HasF => Has(RVIsaFlags.F);
        public bool HasD => Has(RVIsaFlags.D);
        public bool HasC => Has(RVIsaFlags.C);
        public bool HasV => Has(RVIsaFlags.V);
        public bool HasZicsr => Has(RVIsaFlags.Zicsr);
        public bool HasZifencei => Has(RVIsaFlags.Zifencei);
        public bool HasPrivileged => Has(RVIsaFlags.Privileged);

        public RVTarget(int xlen, RVAbiKind abi, RVIsaFlags isa, TargetEndianness endianness = TargetEndianness.Little)
        {
            if (xlen is not 32 and not 64)
                throw new ArgumentOutOfRangeException(nameof(xlen));
            if ((isa & RVIsaFlags.I) == 0)
                throw new ArgumentException("RISC-V target requires base I extension", nameof(isa));
            if (xlen == 32 && abi is RVAbiKind.Lp64 or RVAbiKind.Lp64F or RVAbiKind.Lp64D)
                throw new ArgumentException("LP64 ABI requires RV64", nameof(abi));
            if (xlen == 64 && abi is RVAbiKind.Ilp32 or RVAbiKind.Ilp32F or RVAbiKind.Ilp32D)
                throw new ArgumentException("ILP32 ABI requires RV32", nameof(abi));

            XLen = xlen;
            Abi = abi;
            Isa = isa;
            Endianness = endianness;
        }

        public bool Has(RVIsaFlags flags)
            => (Isa & flags) == flags;

        public override string ToString()
            => XLen == 64 ? "rv64" + FormatIsaSuffix() : "rv32" + FormatIsaSuffix();

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

        public static string Format(RVRegister register, bool abiName = true)
        {
            if (register == RVRegister.Invalid)
                return "invalid";
            if (IsInteger(register))
            {
                int index = IntegerIndex(register);
                return abiName ? IntegerAbiNames[index] : "x" + index.ToString(CultureInfo.InvariantCulture);
            }
            if (IsFloat(register))
            {
                int index = FloatIndex(register);
                return abiName ? FloatAbiNames[index] : "f" + index.ToString(CultureInfo.InvariantCulture);
            }
            if (IsVector(register))
                return "v" + VectorIndex(register).ToString(CultureInfo.InvariantCulture);
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
            throw new FormatException("Invalid RISC-V register: " + text);
        }

        private static Dictionary<string, RVRegister> CreateNameMap()
        {
            var map = new Dictionary<string, RVRegister>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 32; i++)
            {
                map["x" + i.ToString(CultureInfo.InvariantCulture)] = (RVRegister)i;
                map[IntegerAbiNames[i]] = (RVRegister)i;
                map["f" + i.ToString(CultureInfo.InvariantCulture)] = (RVRegister)(i + 32);
                map[FloatAbiNames[i]] = (RVRegister)(i + 32);
                map["v" + i.ToString(CultureInfo.InvariantCulture)] = (RVRegister)(i + 64);
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
            throw new FormatException("Invalid RISC-V CSR: " + text);
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
            return "0x" + csr.ToString("X", CultureInfo.InvariantCulture).ToLowerInvariant();
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
                ["hcounteren"] = 0x606,
                ["hgeie"] = 0x607,
                ["henvcfg"] = 0x60A,
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

        public RVInstructionMetadata(RVInstructionFormat format, RVIsaFlags requiredIsa, bool requires64Bit, byte opcode, byte funct3, byte funct7)
        {
            Format = format;
            RequiredIsa = requiredIsa;
            Requires64Bit = requires64Bit;
            Opcode = opcode;
            Funct3 = funct3;
            Funct7 = funct7;
        }
    }

    internal readonly struct RVInstruction
    {
        public RVInstrKind Opcode { get; }
        public RVRegister Rd { get; }
        public RVRegister Rs1 { get; }
        public RVRegister Rs2 { get; }
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
            RVInstructionFlags flags = RVInstructionFlags.None)
        {
            Opcode = opcode;
            Rd = rd;
            Rs1 = rs1;
            Rs2 = rs2;
            Immediate = immediate;
            Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;
            RelocationKind = relocationKind;
            Flags = flags;
        }

        public RVInstruction WithImmediate(int immediate)
            => new RVInstruction(Opcode, Rd, Rs1, Rs2, immediate, null, RVRelocationKind.None, Flags);

        public RVInstruction WithSymbol(string symbol, RVRelocationKind relocationKind)
            => new RVInstruction(Opcode, Rd, Rs1, Rs2, Immediate, symbol, relocationKind, Flags);

        public RVInstruction WithFlags(RVInstructionFlags flags)
            => new RVInstruction(Opcode, Rd, Rs1, Rs2, Immediate, Symbol, RelocationKind, flags);

        public static RVInstruction Raw(uint word)
            => new RVInstruction(RVInstrKind.Raw32, immediate: unchecked((int)word));

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
            => new RVInstruction(opcode, RVRegister.Invalid, rs1, vs3, flags: unmasked ? RVInstructionFlags.VectorUnmasked : RVInstructionFlags.None);
    }

    internal sealed class RVInstructionBuilder
    {
        private readonly List<RVInstruction> _instructions = new List<RVInstruction>();
        private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count => _instructions.Count;
        public int Position => _instructions.Count * 4;

        public void Emit(RVInstruction instruction)
            => _instructions.Add(instruction);

        public void DefineLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("RISC-V label must not be empty", nameof(label));
            if (_labels.ContainsKey(label))
                throw new ArgumentException("Duplicate RISC-V label: " + label, nameof(label));
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

        public static string GetMnemonic(RVInstrKind opcode)
        {
            return opcode switch
            {
                RVInstrKind.Raw32 => ".word",
                RVInstrKind.Lui => "lui",
                RVInstrKind.Auipc => "auipc",
                RVInstrKind.Jal => "jal",
                RVInstrKind.Jalr => "jalr",
                RVInstrKind.Beq => "beq",
                RVInstrKind.Bne => "bne",
                RVInstrKind.Blt => "blt",
                RVInstrKind.Bge => "bge",
                RVInstrKind.Bltu => "bltu",
                RVInstrKind.Bgeu => "bgeu",
                RVInstrKind.Lb => "lb",
                RVInstrKind.Lh => "lh",
                RVInstrKind.Lw => "lw",
                RVInstrKind.Lbu => "lbu",
                RVInstrKind.Lhu => "lhu",
                RVInstrKind.Lwu => "lwu",
                RVInstrKind.Ld => "ld",
                RVInstrKind.Sb => "sb",
                RVInstrKind.Sh => "sh",
                RVInstrKind.Sw => "sw",
                RVInstrKind.Sd => "sd",
                RVInstrKind.Flw => "flw",
                RVInstrKind.Fld => "fld",
                RVInstrKind.Fsw => "fsw",
                RVInstrKind.Fsd => "fsd",
                RVInstrKind.FaddS => "fadd.s",
                RVInstrKind.FsubS => "fsub.s",
                RVInstrKind.FmulS => "fmul.s",
                RVInstrKind.FdivS => "fdiv.s",
                RVInstrKind.FaddD => "fadd.d",
                RVInstrKind.FsubD => "fsub.d",
                RVInstrKind.FmulD => "fmul.d",
                RVInstrKind.FdivD => "fdiv.d",
                RVInstrKind.FsgnjS => "fsgnj.s",
                RVInstrKind.FsgnjnS => "fsgnjn.s",
                RVInstrKind.FsgnjxS => "fsgnjx.s",
                RVInstrKind.FsgnjD => "fsgnj.d",
                RVInstrKind.FsgnjnD => "fsgnjn.d",
                RVInstrKind.FsgnjxD => "fsgnjx.d",
                RVInstrKind.FeqS => "feq.s",
                RVInstrKind.FltS => "flt.s",
                RVInstrKind.FleS => "fle.s",
                RVInstrKind.FeqD => "feq.d",
                RVInstrKind.FltD => "flt.d",
                RVInstrKind.FleD => "fle.d",
                RVInstrKind.FcvtSW => "fcvt.s.w",
                RVInstrKind.FcvtSWu => "fcvt.s.wu",
                RVInstrKind.FcvtSL => "fcvt.s.l",
                RVInstrKind.FcvtSLu => "fcvt.s.lu",
                RVInstrKind.FcvtDW => "fcvt.d.w",
                RVInstrKind.FcvtDWu => "fcvt.d.wu",
                RVInstrKind.FcvtDL => "fcvt.d.l",
                RVInstrKind.FcvtDLu => "fcvt.d.lu",
                RVInstrKind.FcvtWS => "fcvt.w.s",
                RVInstrKind.FcvtWuS => "fcvt.wu.s",
                RVInstrKind.FcvtLS => "fcvt.l.s",
                RVInstrKind.FcvtLuS => "fcvt.lu.s",
                RVInstrKind.FcvtWD => "fcvt.w.d",
                RVInstrKind.FcvtWuD => "fcvt.wu.d",
                RVInstrKind.FcvtLD => "fcvt.l.d",
                RVInstrKind.FcvtLuD => "fcvt.lu.d",
                RVInstrKind.FcvtSD => "fcvt.s.d",
                RVInstrKind.FcvtDS => "fcvt.d.s",
                RVInstrKind.FmvXW => "fmv.x.w",
                RVInstrKind.FmvWX => "fmv.w.x",
                RVInstrKind.FmvXD => "fmv.x.d",
                RVInstrKind.FmvDX => "fmv.d.x",
                RVInstrKind.Addi => "addi",
                RVInstrKind.Slti => "slti",
                RVInstrKind.Sltiu => "sltiu",
                RVInstrKind.Xori => "xori",
                RVInstrKind.Ori => "ori",
                RVInstrKind.Andi => "andi",
                RVInstrKind.Slli => "slli",
                RVInstrKind.Srli => "srli",
                RVInstrKind.Srai => "srai",
                RVInstrKind.Add => "add",
                RVInstrKind.Sub => "sub",
                RVInstrKind.Sll => "sll",
                RVInstrKind.Slt => "slt",
                RVInstrKind.Sltu => "sltu",
                RVInstrKind.Xor => "xor",
                RVInstrKind.Srl => "srl",
                RVInstrKind.Sra => "sra",
                RVInstrKind.Or => "or",
                RVInstrKind.And => "and",
                RVInstrKind.Addiw => "addiw",
                RVInstrKind.Slliw => "slliw",
                RVInstrKind.Srliw => "srliw",
                RVInstrKind.Sraiw => "sraiw",
                RVInstrKind.Addw => "addw",
                RVInstrKind.Subw => "subw",
                RVInstrKind.Sllw => "sllw",
                RVInstrKind.Srlw => "srlw",
                RVInstrKind.Sraw => "sraw",
                RVInstrKind.Mul => "mul",
                RVInstrKind.Mulh => "mulh",
                RVInstrKind.Mulhsu => "mulhsu",
                RVInstrKind.Mulhu => "mulhu",
                RVInstrKind.Div => "div",
                RVInstrKind.Divu => "divu",
                RVInstrKind.Rem => "rem",
                RVInstrKind.Remu => "remu",
                RVInstrKind.Mulw => "mulw",
                RVInstrKind.Divw => "divw",
                RVInstrKind.Divuw => "divuw",
                RVInstrKind.Remw => "remw",
                RVInstrKind.Remuw => "remuw",
                RVInstrKind.LrW => "lr.w",
                RVInstrKind.ScW => "sc.w",
                RVInstrKind.AmoSwapW => "amoswap.w",
                RVInstrKind.AmoAddW => "amoadd.w",
                RVInstrKind.AmoXorW => "amoxor.w",
                RVInstrKind.AmoAndW => "amoand.w",
                RVInstrKind.AmoOrW => "amoor.w",
                RVInstrKind.AmoMinW => "amomin.w",
                RVInstrKind.AmoMaxW => "amomax.w",
                RVInstrKind.AmoMinuW => "amominu.w",
                RVInstrKind.AmoMaxuW => "amomaxu.w",
                RVInstrKind.LrD => "lr.d",
                RVInstrKind.ScD => "sc.d",
                RVInstrKind.AmoSwapD => "amoswap.d",
                RVInstrKind.AmoAddD => "amoadd.d",
                RVInstrKind.AmoXorD => "amoxor.d",
                RVInstrKind.AmoAndD => "amoand.d",
                RVInstrKind.AmoOrD => "amoor.d",
                RVInstrKind.AmoMinD => "amomin.d",
                RVInstrKind.AmoMaxD => "amomax.d",
                RVInstrKind.AmoMinuD => "amominu.d",
                RVInstrKind.AmoMaxuD => "amomaxu.d",
                RVInstrKind.Fence => "fence",
                RVInstrKind.FenceI => "fence.i",
                RVInstrKind.Ecall => "ecall",
                RVInstrKind.Ebreak => "ebreak",
                RVInstrKind.Uret => "uret",
                RVInstrKind.Sret => "sret",
                RVInstrKind.Mret => "mret",
                RVInstrKind.Wfi => "wfi",
                RVInstrKind.SfenceVma => "sfence.vma",
                RVInstrKind.SinvalVma => "sinval.vma",
                RVInstrKind.SfenceWInval => "sfence.w.inval",
                RVInstrKind.SfenceInvalIr => "sfence.inval.ir",
                RVInstrKind.HfenceVvma => "hfence.vvma",
                RVInstrKind.HfenceGvma => "hfence.gvma",
                RVInstrKind.Csrrw => "csrrw",
                RVInstrKind.Csrrs => "csrrs",
                RVInstrKind.Csrrc => "csrrc",
                RVInstrKind.Csrrwi => "csrrwi",
                RVInstrKind.Csrrsi => "csrrsi",
                RVInstrKind.Csrrci => "csrrci",
                RVInstrKind.Vsetvli => "vsetvli",
                RVInstrKind.Vsetivli => "vsetivli",
                RVInstrKind.Vsetvl => "vsetvl",
                RVInstrKind.Vle8V => "vle8.v",
                RVInstrKind.Vle16V => "vle16.v",
                RVInstrKind.Vle32V => "vle32.v",
                RVInstrKind.Vle64V => "vle64.v",
                RVInstrKind.Vse8V => "vse8.v",
                RVInstrKind.Vse16V => "vse16.v",
                RVInstrKind.Vse32V => "vse32.v",
                RVInstrKind.Vse64V => "vse64.v",
                RVInstrKind.VaddVv => "vadd.vv",
                RVInstrKind.VaddVx => "vadd.vx",
                RVInstrKind.VaddVi => "vadd.vi",
                RVInstrKind.VsubVv => "vsub.vv",
                RVInstrKind.VsubVx => "vsub.vx",
                RVInstrKind.VrsubVx => "vrsub.vx",
                RVInstrKind.VrsubVi => "vrsub.vi",
                RVInstrKind.VandVv => "vand.vv",
                RVInstrKind.VandVx => "vand.vx",
                RVInstrKind.VandVi => "vand.vi",
                RVInstrKind.VorVv => "vor.vv",
                RVInstrKind.VorVx => "vor.vx",
                RVInstrKind.VorVi => "vor.vi",
                RVInstrKind.VxorVv => "vxor.vv",
                RVInstrKind.VxorVx => "vxor.vx",
                RVInstrKind.VxorVi => "vxor.vi",
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

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
            throw new FormatException("Unknown RISC-V mnemonic: " + mnemonic);
        }

        public static bool IsBranch(RVInstrKind opcode)
            => opcode is RVInstrKind.Beq or RVInstrKind.Bne or RVInstrKind.Blt or RVInstrKind.Bge or RVInstrKind.Bltu or RVInstrKind.Bgeu;

        public static bool IsLoad(RVInstrKind opcode)
            => opcode is RVInstrKind.Lb or RVInstrKind.Lh or RVInstrKind.Lw or RVInstrKind.Lbu or RVInstrKind.Lhu or RVInstrKind.Lwu or RVInstrKind.Ld or RVInstrKind.Flw or RVInstrKind.Fld;

        public static bool IsStore(RVInstrKind opcode)
            => opcode is RVInstrKind.Sb or RVInstrKind.Sh or RVInstrKind.Sw or RVInstrKind.Sd or RVInstrKind.Fsw or RVInstrKind.Fsd;

        public static bool IsAmo(RVInstrKind opcode)
            => Get(opcode).Format == RVInstructionFormat.Amo;

        public static bool IsVector(RVInstrKind opcode)
        {
            var format = Get(opcode).Format;
            return format is RVInstructionFormat.VectorConfig or RVInstructionFormat.VectorOp or RVInstructionFormat.VectorLoad or RVInstructionFormat.VectorStore;
        }

        public static bool Is64BitOpcode(RVInstrKind opcode)
            => Get(opcode).Requires64Bit;

        public static bool IsMExtensionOpcode(RVInstrKind opcode)
            => (Get(opcode).RequiredIsa & RVIsaFlags.M) != 0;

        private static Dictionary<string, RVInstrKind> CreateMnemonicMap()
        {
            var map = new Dictionary<string, RVInstrKind>(StringComparer.OrdinalIgnoreCase);
            foreach (RVInstrKind opcode in Enum.GetValues(typeof(RVInstrKind)))
            {
                if (opcode is RVInstrKind.Invalid)
                    continue;
                if (!ByOpcode.ContainsKey(opcode))
                    continue;
                map[GetMnemonic(opcode)] = opcode;
            }
            return map;
        }

        private static Dictionary<RVInstrKind, RVInstructionMetadata> CreateOpcodeMap()
        {
            var map = new Dictionary<RVInstrKind, RVInstructionMetadata>();
            Add(map, RVInstrKind.Raw32, RVInstructionFormat.Raw, RVIsaFlags.I, false, 0, 0, 0);
            Add(map, RVInstrKind.Lui, RVInstructionFormat.U, RVIsaFlags.I, false, 0x37, 0, 0);
            Add(map, RVInstrKind.Auipc, RVInstructionFormat.U, RVIsaFlags.I, false, 0x17, 0, 0);
            Add(map, RVInstrKind.Jal, RVInstructionFormat.J, RVIsaFlags.I, false, 0x6F, 0, 0);
            Add(map, RVInstrKind.Jalr, RVInstructionFormat.I, RVIsaFlags.I, false, 0x67, 0, 0);
            Add(map, RVInstrKind.Beq, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 0, 0);
            Add(map, RVInstrKind.Bne, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 1, 0);
            Add(map, RVInstrKind.Blt, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 4, 0);
            Add(map, RVInstrKind.Bge, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 5, 0);
            Add(map, RVInstrKind.Bltu, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 6, 0);
            Add(map, RVInstrKind.Bgeu, RVInstructionFormat.B, RVIsaFlags.I, false, 0x63, 7, 0);
            Add(map, RVInstrKind.Lb, RVInstructionFormat.I, RVIsaFlags.I, false, 0x03, 0, 0);
            Add(map, RVInstrKind.Lh, RVInstructionFormat.I, RVIsaFlags.I, false, 0x03, 1, 0);
            Add(map, RVInstrKind.Lw, RVInstructionFormat.I, RVIsaFlags.I, false, 0x03, 2, 0);
            Add(map, RVInstrKind.Lbu, RVInstructionFormat.I, RVIsaFlags.I, false, 0x03, 4, 0);
            Add(map, RVInstrKind.Lhu, RVInstructionFormat.I, RVIsaFlags.I, false, 0x03, 5, 0);
            Add(map, RVInstrKind.Lwu, RVInstructionFormat.I, RVIsaFlags.I, true, 0x03, 6, 0);
            Add(map, RVInstrKind.Ld, RVInstructionFormat.I, RVIsaFlags.I, true, 0x03, 3, 0);
            Add(map, RVInstrKind.Sb, RVInstructionFormat.S, RVIsaFlags.I, false, 0x23, 0, 0);
            Add(map, RVInstrKind.Sh, RVInstructionFormat.S, RVIsaFlags.I, false, 0x23, 1, 0);
            Add(map, RVInstrKind.Sw, RVInstructionFormat.S, RVIsaFlags.I, false, 0x23, 2, 0);
            Add(map, RVInstrKind.Sd, RVInstructionFormat.S, RVIsaFlags.I, true, 0x23, 3, 0);
            Add(map, RVInstrKind.Flw, RVInstructionFormat.FloatLoad, RVIsaFlags.F, false, 0x07, 2, 0);
            Add(map, RVInstrKind.Fld, RVInstructionFormat.FloatLoad, RVIsaFlags.D, false, 0x07, 3, 0);
            Add(map, RVInstrKind.Fsw, RVInstructionFormat.FloatStore, RVIsaFlags.F, false, 0x27, 2, 0);
            Add(map, RVInstrKind.Fsd, RVInstructionFormat.FloatStore, RVIsaFlags.D, false, 0x27, 3, 0);
            AddFloatR(map, RVInstrKind.FaddS, RVIsaFlags.F, 0x00);
            AddFloatR(map, RVInstrKind.FsubS, RVIsaFlags.F, 0x04);
            AddFloatR(map, RVInstrKind.FmulS, RVIsaFlags.F, 0x08);
            AddFloatR(map, RVInstrKind.FdivS, RVIsaFlags.F, 0x0C);
            AddFloatR(map, RVInstrKind.FaddD, RVIsaFlags.D, 0x01);
            AddFloatR(map, RVInstrKind.FsubD, RVIsaFlags.D, 0x05);
            AddFloatR(map, RVInstrKind.FmulD, RVIsaFlags.D, 0x09);
            AddFloatR(map, RVInstrKind.FdivD, RVIsaFlags.D, 0x0D);
            Add(map, RVInstrKind.FsgnjS, RVInstructionFormat.FloatRRR, RVIsaFlags.F, false, 0x53, 0, 0x10);
            Add(map, RVInstrKind.FsgnjnS, RVInstructionFormat.FloatRRR, RVIsaFlags.F, false, 0x53, 1, 0x10);
            Add(map, RVInstrKind.FsgnjxS, RVInstructionFormat.FloatRRR, RVIsaFlags.F, false, 0x53, 2, 0x10);
            Add(map, RVInstrKind.FsgnjD, RVInstructionFormat.FloatRRR, RVIsaFlags.D, false, 0x53, 0, 0x11);
            Add(map, RVInstrKind.FsgnjnD, RVInstructionFormat.FloatRRR, RVIsaFlags.D, false, 0x53, 1, 0x11);
            Add(map, RVInstrKind.FsgnjxD, RVInstructionFormat.FloatRRR, RVIsaFlags.D, false, 0x53, 2, 0x11);
            Add(map, RVInstrKind.FeqS, RVInstructionFormat.FloatCompare, RVIsaFlags.F, false, 0x53, 2, 0x50);
            Add(map, RVInstrKind.FltS, RVInstructionFormat.FloatCompare, RVIsaFlags.F, false, 0x53, 1, 0x50);
            Add(map, RVInstrKind.FleS, RVInstructionFormat.FloatCompare, RVIsaFlags.F, false, 0x53, 0, 0x50);
            Add(map, RVInstrKind.FeqD, RVInstructionFormat.FloatCompare, RVIsaFlags.D, false, 0x53, 2, 0x51);
            Add(map, RVInstrKind.FltD, RVInstructionFormat.FloatCompare, RVIsaFlags.D, false, 0x53, 1, 0x51);
            Add(map, RVInstrKind.FleD, RVInstructionFormat.FloatCompare, RVIsaFlags.D, false, 0x53, 0, 0x51);
            Add(map, RVInstrKind.FcvtSW, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.F, false, 0x53, 0, 0x68);
            Add(map, RVInstrKind.FcvtSWu, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.F, false, 0x53, 0, 0x68);
            Add(map, RVInstrKind.FcvtSL, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.F, true, 0x53, 0, 0x68);
            Add(map, RVInstrKind.FcvtSLu, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.F, true, 0x53, 0, 0x68);
            Add(map, RVInstrKind.FcvtDW, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.D, false, 0x53, 0, 0x69);
            Add(map, RVInstrKind.FcvtDWu, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.D, false, 0x53, 0, 0x69);
            Add(map, RVInstrKind.FcvtDL, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.D, true, 0x53, 0, 0x69);
            Add(map, RVInstrKind.FcvtDLu, RVInstructionFormat.FloatConvertFromInteger, RVIsaFlags.D, true, 0x53, 0, 0x69);
            Add(map, RVInstrKind.FcvtWS, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.F, false, 0x53, 0, 0x60);
            Add(map, RVInstrKind.FcvtWuS, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.F, false, 0x53, 0, 0x60);
            Add(map, RVInstrKind.FcvtLS, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.F, true, 0x53, 0, 0x60);
            Add(map, RVInstrKind.FcvtLuS, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.F, true, 0x53, 0, 0x60);
            Add(map, RVInstrKind.FcvtWD, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.D, false, 0x53, 0, 0x61);
            Add(map, RVInstrKind.FcvtWuD, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.D, false, 0x53, 0, 0x61);
            Add(map, RVInstrKind.FcvtLD, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.D, true, 0x53, 0, 0x61);
            Add(map, RVInstrKind.FcvtLuD, RVInstructionFormat.FloatConvertToInteger, RVIsaFlags.D, true, 0x53, 0, 0x61);
            Add(map, RVInstrKind.FcvtSD, RVInstructionFormat.FloatConvert, RVIsaFlags.D, false, 0x53, 0, 0x20);
            Add(map, RVInstrKind.FcvtDS, RVInstructionFormat.FloatConvert, RVIsaFlags.D, false, 0x53, 0, 0x21);
            Add(map, RVInstrKind.FmvXW, RVInstructionFormat.FloatMoveToInteger, RVIsaFlags.F, false, 0x53, 0, 0x70);
            Add(map, RVInstrKind.FmvWX, RVInstructionFormat.FloatMoveFromInteger, RVIsaFlags.F, false, 0x53, 0, 0x78);
            Add(map, RVInstrKind.FmvXD, RVInstructionFormat.FloatMoveToInteger, RVIsaFlags.D, true, 0x53, 0, 0x71);
            Add(map, RVInstrKind.FmvDX, RVInstructionFormat.FloatMoveFromInteger, RVIsaFlags.D, true, 0x53, 0, 0x79);
            Add(map, RVInstrKind.Addi, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 0, 0);
            Add(map, RVInstrKind.Slti, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 2, 0);
            Add(map, RVInstrKind.Sltiu, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 3, 0);
            Add(map, RVInstrKind.Xori, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 4, 0);
            Add(map, RVInstrKind.Ori, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 6, 0);
            Add(map, RVInstrKind.Andi, RVInstructionFormat.I, RVIsaFlags.I, false, 0x13, 7, 0);
            Add(map, RVInstrKind.Slli, RVInstructionFormat.ShiftI, RVIsaFlags.I, false, 0x13, 1, 0x00);
            Add(map, RVInstrKind.Srli, RVInstructionFormat.ShiftI, RVIsaFlags.I, false, 0x13, 5, 0x00);
            Add(map, RVInstrKind.Srai, RVInstructionFormat.ShiftI, RVIsaFlags.I, false, 0x13, 5, 0x20);
            Add(map, RVInstrKind.Add, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 0, 0x00);
            Add(map, RVInstrKind.Sub, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 0, 0x20);
            Add(map, RVInstrKind.Sll, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 1, 0x00);
            Add(map, RVInstrKind.Slt, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 2, 0x00);
            Add(map, RVInstrKind.Sltu, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 3, 0x00);
            Add(map, RVInstrKind.Xor, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 4, 0x00);
            Add(map, RVInstrKind.Srl, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 5, 0x00);
            Add(map, RVInstrKind.Sra, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 5, 0x20);
            Add(map, RVInstrKind.Or, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 6, 0x00);
            Add(map, RVInstrKind.And, RVInstructionFormat.R, RVIsaFlags.I, false, 0x33, 7, 0x00);
            Add(map, RVInstrKind.Addiw, RVInstructionFormat.I, RVIsaFlags.I, true, 0x1B, 0, 0);
            Add(map, RVInstrKind.Slliw, RVInstructionFormat.ShiftI, RVIsaFlags.I, true, 0x1B, 1, 0x00);
            Add(map, RVInstrKind.Srliw, RVInstructionFormat.ShiftI, RVIsaFlags.I, true, 0x1B, 5, 0x00);
            Add(map, RVInstrKind.Sraiw, RVInstructionFormat.ShiftI, RVIsaFlags.I, true, 0x1B, 5, 0x20);
            Add(map, RVInstrKind.Addw, RVInstructionFormat.R, RVIsaFlags.I, true, 0x3B, 0, 0x00);
            Add(map, RVInstrKind.Subw, RVInstructionFormat.R, RVIsaFlags.I, true, 0x3B, 0, 0x20);
            Add(map, RVInstrKind.Sllw, RVInstructionFormat.R, RVIsaFlags.I, true, 0x3B, 1, 0x00);
            Add(map, RVInstrKind.Srlw, RVInstructionFormat.R, RVIsaFlags.I, true, 0x3B, 5, 0x00);
            Add(map, RVInstrKind.Sraw, RVInstructionFormat.R, RVIsaFlags.I, true, 0x3B, 5, 0x20);
            Add(map, RVInstrKind.Mul, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 0, 0x01);
            Add(map, RVInstrKind.Mulh, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 1, 0x01);
            Add(map, RVInstrKind.Mulhsu, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 2, 0x01);
            Add(map, RVInstrKind.Mulhu, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 3, 0x01);
            Add(map, RVInstrKind.Div, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 4, 0x01);
            Add(map, RVInstrKind.Divu, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 5, 0x01);
            Add(map, RVInstrKind.Rem, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 6, 0x01);
            Add(map, RVInstrKind.Remu, RVInstructionFormat.R, RVIsaFlags.M, false, 0x33, 7, 0x01);
            Add(map, RVInstrKind.Mulw, RVInstructionFormat.R, RVIsaFlags.M, true, 0x3B, 0, 0x01);
            Add(map, RVInstrKind.Divw, RVInstructionFormat.R, RVIsaFlags.M, true, 0x3B, 4, 0x01);
            Add(map, RVInstrKind.Divuw, RVInstructionFormat.R, RVIsaFlags.M, true, 0x3B, 5, 0x01);
            Add(map, RVInstrKind.Remw, RVInstructionFormat.R, RVIsaFlags.M, true, 0x3B, 6, 0x01);
            Add(map, RVInstrKind.Remuw, RVInstructionFormat.R, RVIsaFlags.M, true, 0x3B, 7, 0x01);
            AddAmo(map, RVInstrKind.LrW, false, 2, 0x02);
            AddAmo(map, RVInstrKind.ScW, false, 2, 0x03);
            AddAmo(map, RVInstrKind.AmoSwapW, false, 2, 0x01);
            AddAmo(map, RVInstrKind.AmoAddW, false, 2, 0x00);
            AddAmo(map, RVInstrKind.AmoXorW, false, 2, 0x04);
            AddAmo(map, RVInstrKind.AmoAndW, false, 2, 0x0C);
            AddAmo(map, RVInstrKind.AmoOrW, false, 2, 0x08);
            AddAmo(map, RVInstrKind.AmoMinW, false, 2, 0x10);
            AddAmo(map, RVInstrKind.AmoMaxW, false, 2, 0x14);
            AddAmo(map, RVInstrKind.AmoMinuW, false, 2, 0x18);
            AddAmo(map, RVInstrKind.AmoMaxuW, false, 2, 0x1C);
            AddAmo(map, RVInstrKind.LrD, true, 3, 0x02);
            AddAmo(map, RVInstrKind.ScD, true, 3, 0x03);
            AddAmo(map, RVInstrKind.AmoSwapD, true, 3, 0x01);
            AddAmo(map, RVInstrKind.AmoAddD, true, 3, 0x00);
            AddAmo(map, RVInstrKind.AmoXorD, true, 3, 0x04);
            AddAmo(map, RVInstrKind.AmoAndD, true, 3, 0x0C);
            AddAmo(map, RVInstrKind.AmoOrD, true, 3, 0x08);
            AddAmo(map, RVInstrKind.AmoMinD, true, 3, 0x10);
            AddAmo(map, RVInstrKind.AmoMaxD, true, 3, 0x14);
            AddAmo(map, RVInstrKind.AmoMinuD, true, 3, 0x18);
            AddAmo(map, RVInstrKind.AmoMaxuD, true, 3, 0x1C);
            Add(map, RVInstrKind.Fence, RVInstructionFormat.Fence, RVIsaFlags.I, false, 0x0F, 0, 0);
            Add(map, RVInstrKind.FenceI, RVInstructionFormat.System, RVIsaFlags.Zifencei, false, 0x0F, 1, 0);
            Add(map, RVInstrKind.Ecall, RVInstructionFormat.System, RVIsaFlags.I, false, 0x73, 0, 0);
            Add(map, RVInstrKind.Ebreak, RVInstructionFormat.System, RVIsaFlags.I, false, 0x73, 0, 0);
            Add(map, RVInstrKind.Uret, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.Sret, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.Mret, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.Wfi, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.SfenceVma, RVInstructionFormat.PrivilegedFence, RVIsaFlags.Privileged, false, 0x73, 0, 0x09);
            Add(map, RVInstrKind.SinvalVma, RVInstructionFormat.PrivilegedFence, RVIsaFlags.Privileged, false, 0x73, 0, 0x0B);
            Add(map, RVInstrKind.SfenceWInval, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.SfenceInvalIr, RVInstructionFormat.System, RVIsaFlags.Privileged, false, 0x73, 0, 0);
            Add(map, RVInstrKind.HfenceVvma, RVInstructionFormat.PrivilegedFence, RVIsaFlags.Privileged, false, 0x73, 0, 0x11);
            Add(map, RVInstrKind.HfenceGvma, RVInstructionFormat.PrivilegedFence, RVIsaFlags.Privileged, false, 0x73, 0, 0x31);
            Add(map, RVInstrKind.Csrrw, RVInstructionFormat.Csr, RVIsaFlags.Zicsr, false, 0x73, 1, 0);
            Add(map, RVInstrKind.Csrrs, RVInstructionFormat.Csr, RVIsaFlags.Zicsr, false, 0x73, 2, 0);
            Add(map, RVInstrKind.Csrrc, RVInstructionFormat.Csr, RVIsaFlags.Zicsr, false, 0x73, 3, 0);
            Add(map, RVInstrKind.Csrrwi, RVInstructionFormat.CsrImmediate, RVIsaFlags.Zicsr, false, 0x73, 5, 0);
            Add(map, RVInstrKind.Csrrsi, RVInstructionFormat.CsrImmediate, RVIsaFlags.Zicsr, false, 0x73, 6, 0);
            Add(map, RVInstrKind.Csrrci, RVInstructionFormat.CsrImmediate, RVIsaFlags.Zicsr, false, 0x73, 7, 0);
            Add(map, RVInstrKind.Vsetvli, RVInstructionFormat.VectorConfig, RVIsaFlags.V, false, 0x57, 7, 0);
            Add(map, RVInstrKind.Vsetivli, RVInstructionFormat.VectorConfig, RVIsaFlags.V, false, 0x57, 7, 0);
            Add(map, RVInstrKind.Vsetvl, RVInstructionFormat.VectorConfig, RVIsaFlags.V, false, 0x57, 7, 0x40);
            AddVectorLoad(map, RVInstrKind.Vle8V, 0);
            AddVectorLoad(map, RVInstrKind.Vle16V, 5);
            AddVectorLoad(map, RVInstrKind.Vle32V, 6);
            AddVectorLoad(map, RVInstrKind.Vle64V, 7);
            AddVectorStore(map, RVInstrKind.Vse8V, 0);
            AddVectorStore(map, RVInstrKind.Vse16V, 5);
            AddVectorStore(map, RVInstrKind.Vse32V, 6);
            AddVectorStore(map, RVInstrKind.Vse64V, 7);
            AddVectorOp(map, RVInstrKind.VaddVv, 0, 0);
            AddVectorOp(map, RVInstrKind.VaddVx, 4, 0);
            AddVectorOp(map, RVInstrKind.VaddVi, 3, 0);
            AddVectorOp(map, RVInstrKind.VsubVv, 0, 2);
            AddVectorOp(map, RVInstrKind.VsubVx, 4, 2);
            AddVectorOp(map, RVInstrKind.VrsubVx, 4, 3);
            AddVectorOp(map, RVInstrKind.VrsubVi, 3, 3);
            AddVectorOp(map, RVInstrKind.VandVv, 0, 9);
            AddVectorOp(map, RVInstrKind.VandVx, 4, 9);
            AddVectorOp(map, RVInstrKind.VandVi, 3, 9);
            AddVectorOp(map, RVInstrKind.VorVv, 0, 10);
            AddVectorOp(map, RVInstrKind.VorVx, 4, 10);
            AddVectorOp(map, RVInstrKind.VorVi, 3, 10);
            AddVectorOp(map, RVInstrKind.VxorVv, 0, 11);
            AddVectorOp(map, RVInstrKind.VxorVx, 4, 11);
            AddVectorOp(map, RVInstrKind.VxorVi, 3, 11);
            return map;
        }

        private static void Add(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, RVInstructionFormat format, RVIsaFlags requiredIsa, bool requires64Bit, byte op, byte funct3, byte funct7)
            => map.Add(opcode, new RVInstructionMetadata(format, requiredIsa, requires64Bit, op, funct3, funct7));

        private static void AddFloatR(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, RVIsaFlags requiredIsa, byte funct7)
            => Add(map, opcode, RVInstructionFormat.FloatRRR, requiredIsa, false, 0x53, 0, funct7);

        private static void AddAmo(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, bool requires64Bit, byte funct3, byte funct5)
            => Add(map, opcode, RVInstructionFormat.Amo, RVIsaFlags.A, requires64Bit, 0x2F, funct3, funct5);

        private static void AddVectorLoad(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, byte width)
            => Add(map, opcode, RVInstructionFormat.VectorLoad, RVIsaFlags.V, false, 0x07, width, 0);

        private static void AddVectorStore(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, byte width)
            => Add(map, opcode, RVInstructionFormat.VectorStore, RVIsaFlags.V, false, 0x27, width, 0);

        private static void AddVectorOp(Dictionary<RVInstrKind, RVInstructionMetadata> map, RVInstrKind opcode, byte funct3, byte funct6)
            => Add(map, opcode, RVInstructionFormat.VectorOp, RVIsaFlags.V, false, 0x57, funct3, funct6);
    }
}
