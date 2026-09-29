using Cnidaria.RiscV;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.Arm;

public sealed class ArmProgram
{
    public ArmTarget Target { get; }
    public ArmTextSection Text { get; }
    public ImmutableArray<ArmDataSection> DataSections { get; }
    public ImmutableArray<ArmObjectSymbol> Symbols { get; }
    public string EntrySymbol { get; }

    public ArmProgram(
        ArmTarget target,
        ArmTextSection text,
        ImmutableArray<ArmDataSection> dataSections,
        ImmutableArray<ArmObjectSymbol> symbols,
        string entrySymbol)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        DataSections = dataSections.IsDefault ? ImmutableArray<ArmDataSection>.Empty : dataSections;
        Symbols = symbols.IsDefault ? ImmutableArray<ArmObjectSymbol>.Empty : symbols;
        EntrySymbol = entrySymbol ?? string.Empty;
    }

    public ArmProgram(
        ArmTarget target,
        IEnumerable<ArmInstruction> instructions,
        IReadOnlyDictionary<string, int>? textLabels = null,
        string entrySymbol = "")
        : this(
            target,
            new ArmTextSection(instructions, textLabels, ImmutableArray<ArmObjectRelocation>.Empty),
            ImmutableArray<ArmDataSection>.Empty,
            ImmutableArray<ArmObjectSymbol>.Empty,
            entrySymbol)
    { }

    public ArmProgram Link(params ArmProgram[] objects)
        => ArmObjectComposer.Compose(this, objects);

    public ArmLinkedImage LinkFlat(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => ArmObjectLinker.LinkFlat(this, imageBase, externalSymbols);

    public byte[] ToExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Target.OperatingSystem switch
        {
            OperatingSystemKind.Windows => ArmPortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? ArmPortableExecutableWriter.DefaultImageBase(Target) : imageBase),
            OperatingSystemKind.Linux => ArmElfWriter.WriteExecutable(this, imageBase == 0 ? ArmElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols),
            _ => LinkFlat(imageBase, externalSymbols).Bytes.ToArray(),
        };

    public byte[] ToWindowsExecutableBytes(ulong imageBase = 0)
        => ArmPortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? ArmPortableExecutableWriter.DefaultImageBase(Target) : imageBase);

    public byte[] ToLinuxDynamicExecutableBytes(
        IEnumerable<string>? needed = null,
        string? interpreter = null,
        ulong imageBase = 0)
        => ArmElfWriter.WriteDynamicExecutable(this, imageBase, interpreter, needed);

    public byte[] ToSharedObjectBytes(string soName, IEnumerable<string>? needed = null, string? initSymbol = null)
        => ArmElfWriter.WriteSharedObject(this, soName, needed, initSymbol);

    public byte[] ToLinuxExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => ArmElfWriter.WriteExecutable(this, imageBase == 0 ? ArmElfWriter.DefaultImageBase(Target) : imageBase, externalSymbols);
}

public sealed class ArmTextSection
{
    public ImmutableArray<ArmInstruction> Instructions { get; }
    public ImmutableDictionary<string, int> Labels { get; }
    public ImmutableArray<ArmObjectRelocation> Relocations { get; }

    public ArmTextSection(
        IEnumerable<ArmInstruction> instructions,
        IReadOnlyDictionary<string, int>? labels = null,
        ImmutableArray<ArmObjectRelocation> relocations = default)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));

        Instructions = instructions.ToImmutableArray();
        Labels = labels is null
            ? ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)
            : labels.ToImmutableDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Relocations = relocations.IsDefault ? ImmutableArray<ArmObjectRelocation>.Empty : relocations;
    }

    public int SizeInBytes => checked(Instructions.Length * 4);

    public int GetInstructionOffset(int index)
    {
        if ((uint)index > (uint)Instructions.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return checked(index * 4);
    }

    public byte[] Encode(ArmTarget target)
        => ArmCodeEncoder.Encode(Instructions, target, Labels);

    public string Format(ArmAssemblyWriterOptions? options = null)
        => ArmDisassembler.Disassemble(this, options);
}

public sealed class ArmDataSection
{
    public string Name { get; }
    public ArmObjectSectionKind Kind { get; }
    public int Alignment { get; }
    public ImmutableArray<byte> Data { get; }
    public int BssSize { get; }
    public ImmutableArray<ArmObjectRelocation> Relocations { get; }

    public ArmDataSection(
        string name,
        ArmObjectSectionKind kind,
        int alignment,
        ImmutableArray<byte> data,
        int bssSize,
        ImmutableArray<ArmObjectRelocation> relocations)
    {
        Name = string.IsNullOrWhiteSpace(name) ? ".data" : name;
        Kind = kind;
        Alignment = Math.Max(1, alignment);
        Data = data.IsDefault ? ImmutableArray<byte>.Empty : data;
        BssSize = Math.Max(0, bssSize);
        Relocations = relocations.IsDefault ? ImmutableArray<ArmObjectRelocation>.Empty : relocations;
    }
}

public sealed class ArmObjectSymbol
{
    public string Name { get; }
    public string SectionName { get; }
    public int Offset { get; }
    public int Size { get; }
    public ArmObjectSymbolBinding Binding { get; }
    public ArmObjectSymbolKind Kind { get; }
    public bool IsTentative { get; }

    public ArmObjectSymbol(
        string name,
        string sectionName,
        int offset,
        int size,
        ArmObjectSymbolBinding binding,
        ArmObjectSymbolKind kind,
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

public sealed class ArmObjectRelocation
{
    public string SectionName { get; }
    public int Offset { get; }
    public string SymbolName { get; }
    public long Addend { get; }
    public ArmObjectRelocationKind Kind { get; }

    public ArmObjectRelocation(string sectionName, int offset, string symbolName, long addend, ArmObjectRelocationKind kind)
    {
        SectionName = sectionName ?? string.Empty;
        Offset = Math.Max(0, offset);
        SymbolName = symbolName ?? string.Empty;
        Addend = addend;
        Kind = kind;
    }
}

public sealed class ArmLinkedImage
{
    public ArmProgram Source { get; }
    public ulong ImageBase { get; }
    public ulong EntryAddress { get; }
    public int EntryOffset { get; }
    public ImmutableDictionary<string, ArmLinkedSection> Sections { get; }
    public ImmutableDictionary<string, ulong> SymbolAddresses { get; }
    public ImmutableArray<byte> Bytes { get; }

    public ArmLinkedImage(
        ArmProgram source,
        ulong imageBase,
        ulong entryAddress,
        int entryOffset,
        IReadOnlyDictionary<string, ArmLinkedSection> sections,
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

public sealed class ArmLinkedSection
{
    public string Name { get; }
    public ArmObjectSectionKind Kind { get; }
    public int Offset { get; }
    public int Size { get; }
    public int Alignment { get; }
    public ulong Address { get; }

    public ArmLinkedSection(string name, ArmObjectSectionKind kind, int offset, int size, int alignment, ulong address)
    {
        Name = name ?? string.Empty;
        Kind = kind;
        Offset = Math.Max(0, offset);
        Size = Math.Max(0, size);
        Alignment = Math.Max(1, alignment);
        Address = address;
    }
}

public enum ArmObjectSectionKind : byte
{
    Text,
    Rodata,
    Data,
    Bss,
}

public enum ArmObjectSymbolBinding : byte
{
    Local,
    Global,
    External,
}

public enum ArmObjectSymbolKind : byte
{
    None,
    Function,
    Object,
    Section,
}

public enum ArmObjectRelocationKind : byte
{
    None,
    ArmBranch24,
    ArmCall24,
    AArch64Branch26,
    AArch64Call26,
    AArch64ConditionalBranch19,
    AArch64CompareBranch19,
    AArch64TestBranch14,
    AArch64Adr21,
    AArch64Adrp21,
    AArch64AddLow12,
    AArch64LoadLow12,
    AArch64LoadLiteral19,
    AbsolutePointer,
    Absolute32,
    Absolute64,
    ArmMovw16,
    ArmMovt16,
}

public enum ArmRelocationKind : byte
{
    None,
    Branch,
    Call,
    ConditionalBranch,
    CompareBranch,
    TestBranch,
    Adr,
    Adrp,
    AddLow12,
    LoadLiteral,
}

[Flags]
public enum ArmIsaFlags : ulong
{
    None = 0,
    Vfp = 1UL << 0,
    VfpD32 = 1UL << 1,
    Neon = 1UL << 2,
    HardFloat = 1UL << 3,
}

public enum ArmAbiKind : byte
{
    Aapcs,
    AapcsVfp,
    Aapcs64,
    WindowsArm,
    WindowsArm64,
}

public enum ArmCondition : byte
{
    Eq = 0,
    Ne = 1,
    Cs = 2,
    Hs = 2,
    Cc = 3,
    Lo = 3,
    Mi = 4,
    Pl = 5,
    Vs = 6,
    Vc = 7,
    Hi = 8,
    Ls = 9,
    Ge = 10,
    Lt = 11,
    Gt = 12,
    Le = 13,
    Al = 14,
    Nv = 15,
}

public enum ArmShiftKind : byte
{
    None,
    Lsl,
    Lsr,
    Asr,
    Ror,
    Rrx,
}

public enum ArmExtendKind : byte
{
    None,
    Uxtb,
    Uxth,
    Uxtw,
    Uxtx,
    Sxtb,
    Sxth,
    Sxtw,
    Sxtx,
}

public enum ArmAddressingMode : byte
{
    Offset,
    PreIndex,
    PostIndex,
    Literal,
}

public enum ArmOperandKind : byte
{
    None,
    Register,
    Immediate,
    ShiftedRegister,
    ExtendedRegister,
    Memory,
    RegisterList,
    Symbol,
    SystemRegister,
}

public enum ArmInstrKind : ushort
{
    Invalid = 0,
    Raw,
    Nop,
    Mov,
    Movw,
    Movt,
    Movz,
    Movn,
    Movk,
    Mvn,
    Add,
    Adds,
    Adc,
    Adcs,
    Sub,
    Subs,
    Sbc,
    Sbcs,
    Rsb,
    And,
    Ands,
    Orr,
    Eor,
    Bic,
    Bics,
    Tst,
    Teq,
    Cmp,
    Cmn,
    Lsl,
    Lsr,
    Asr,
    Ror,
    Mul,
    Mla,
    Mls,
    Madd,
    Msub,
    Umull,
    Smull,
    Umulh,
    Smulh,
    Udiv,
    Sdiv,
    Clz,
    Rev,
    Rev16,
    Revsh,
    Rbit,
    Sxtb,
    Sxth,
    Sxtw,
    Uxtb,
    Uxth,
    B,
    Bl,
    Bx,
    Blx,
    Br,
    Blr,
    Ret,
    Cbz,
    Cbnz,
    Tbz,
    Tbnz,
    Adr,
    Adrp,
    Ldr,
    Str,
    Ldrb,
    Strb,
    Ldrh,
    Strh,
    Ldrsb,
    Ldrsh,
    Ldrsw,
    Ldur,
    Stur,
    Ldp,
    Stp,
    Ldaxr,
    Ldaxrb,
    Ldaxrh,
    Stlxr,
    Stlxrb,
    Stlxrh,
    Ldm,
    Stm,
    Push,
    Pop,
    Svc,
    Brk,
    Bkpt,
    Mrs,
    Msr,
    Dmb,
    Dsb,
    Isb,
    Yield,
    Wfe,
    Wfi,
    Sev,
    Sevl,
    Fmov,
    Fadd,
    Fsub,
    Fmul,
    Fdiv,
    Fsqrt,
    Fabs,
    Fneg,
    Fcmp,
    Scvtf,
    Ucvtf,
    Fcvtzs,
    Fcvtzu,
    Fcvt,
}

public enum ArmRegister : byte
{
    R0 = 0,
    R1 = 1,
    R2 = 2,
    R3 = 3,
    R4 = 4,
    R5 = 5,
    R6 = 6,
    R7 = 7,
    R8 = 8,
    R9 = 9,
    R10 = 10,
    R11 = 11,
    R12 = 12,
    R13 = 13,
    R14 = 14,
    R15 = 15,
    Sp32 = R13,
    Lr = R14,
    Pc = R15,
    Sp = 31,
    X0 = 32,
    X1 = 33,
    X2 = 34,
    X3 = 35,
    X4 = 36,
    X5 = 37,
    X6 = 38,
    X7 = 39,
    X8 = 40,
    X9 = 41,
    X10 = 42,
    X11 = 43,
    X12 = 44,
    X13 = 45,
    X14 = 46,
    X15 = 47,
    X16 = 48,
    X17 = 49,
    X18 = 50,
    X19 = 51,
    X20 = 52,
    X21 = 53,
    X22 = 54,
    X23 = 55,
    X24 = 56,
    X25 = 57,
    X26 = 58,
    X27 = 59,
    X28 = 60,
    X29 = 61,
    X30 = 62,
    Xzr = 63,
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

public enum ArmSystemRegister : ushort
{
    Invalid = 0,
    Cpsr,
    Spsr,
    Nzcv,
    Fpcr,
    Fpsr,
    TpidrEl0,
    TpidrroEl0,
    TpidrEl1,
    SpEl0,
    ElrEl1,
    SpsrEl1,
    CurrentEl,
    Daif,
}

public readonly struct ArmOperand
{
    public ArmOperandKind Kind { get; }
    public ArmRegister Register { get; }
    public ArmRegister BaseRegister { get; }
    public ArmRegister IndexRegister { get; }
    public int Size { get; }
    public long Immediate { get; }
    public ArmShiftKind Shift { get; }
    public int ShiftAmount { get; }
    public ArmExtendKind Extend { get; }
    public ArmAddressingMode AddressingMode { get; }
    public uint RegisterMask { get; }
    public string? Symbol { get; }
    public long Addend { get; }
    public ArmRelocationKind RelocationKind { get; }
    public ArmSystemRegister SystemRegister { get; }
    public bool HasSymbol => !string.IsNullOrEmpty(Symbol);

    private ArmOperand(
        ArmOperandKind kind,
        ArmRegister register,
        ArmRegister baseRegister,
        ArmRegister indexRegister,
        int size,
        long immediate,
        ArmShiftKind shift,
        int shiftAmount,
        ArmExtendKind extend,
        ArmAddressingMode addressingMode,
        uint registerMask,
        string? symbol,
        long addend,
        ArmRelocationKind relocationKind,
        ArmSystemRegister systemRegister)
    {
        Kind = kind;
        Register = register;
        BaseRegister = baseRegister;
        IndexRegister = indexRegister;
        Size = Math.Max(0, size);
        Immediate = immediate;
        Shift = shift;
        ShiftAmount = Math.Max(0, shiftAmount);
        Extend = extend;
        AddressingMode = addressingMode;
        RegisterMask = registerMask;
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;
        Addend = addend;
        RelocationKind = relocationKind;
        SystemRegister = systemRegister;
    }

    public static ArmOperand None => default;

    public static ArmOperand RegisterOperand(ArmRegister register, int size = 0)
        => new ArmOperand(ArmOperandKind.Register, register, ArmRegister.Invalid, ArmRegister.Invalid, size, 0,
            ArmShiftKind.None, 0, ArmExtendKind.None, ArmAddressingMode.Offset, 0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand ImmediateOperand(long value)
        => new ArmOperand(ArmOperandKind.Immediate, ArmRegister.Invalid, ArmRegister.Invalid, ArmRegister.Invalid, 0,
            value, ArmShiftKind.None, 0, ArmExtendKind.None, ArmAddressingMode.Offset, 0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand ShiftedRegister(ArmRegister register, ArmShiftKind shift, int amount, int size = 0)
        => new ArmOperand(ArmOperandKind.ShiftedRegister, register, ArmRegister.Invalid, ArmRegister.Invalid, size, 0,
            shift, amount, ArmExtendKind.None, ArmAddressingMode.Offset, 0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand ExtendedRegister(ArmRegister register, ArmExtendKind extend, int amount = 0, int size = 0)
        => new ArmOperand(ArmOperandKind.ExtendedRegister, register, ArmRegister.Invalid, ArmRegister.Invalid, size, 0,
            ArmShiftKind.None, amount, extend, ArmAddressingMode.Offset, 0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand Memory(
        ArmRegister baseRegister,
        long displacement = 0,
        int size = 0,
        ArmAddressingMode addressingMode = ArmAddressingMode.Offset,
        ArmRegister indexRegister = ArmRegister.Invalid,
        ArmShiftKind shift = ArmShiftKind.None,
        int shiftAmount = 0,
        ArmExtendKind extend = ArmExtendKind.None)
        => new ArmOperand(ArmOperandKind.Memory, ArmRegister.Invalid, baseRegister, indexRegister, size, displacement, shift, shiftAmount, extend, addressingMode,
            0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand Literal(long displacement, int size = 0)
        => new ArmOperand(ArmOperandKind.Memory, ArmRegister.Invalid, ArmRegister.Pc, ArmRegister.Invalid, size, displacement, ArmShiftKind.None,
            0, ArmExtendKind.None, ArmAddressingMode.Literal, 0, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand RegisterList(uint registerMask)
        => new ArmOperand(ArmOperandKind.RegisterList, ArmRegister.Invalid, ArmRegister.Invalid, ArmRegister.Invalid, 0, 0, ArmShiftKind.None,
            0, ArmExtendKind.None, ArmAddressingMode.Offset, registerMask, null, 0, ArmRelocationKind.None, ArmSystemRegister.Invalid);

    public static ArmOperand SymbolOperand(string symbol, ArmRelocationKind relocationKind, long addend = 0, int size = 0)
        => new ArmOperand(ArmOperandKind.Symbol, ArmRegister.Invalid, ArmRegister.Invalid, ArmRegister.Invalid, size, 0, ArmShiftKind.None,
            0, ArmExtendKind.None, ArmAddressingMode.Offset, 0, symbol, addend, relocationKind, ArmSystemRegister.Invalid);

    public static ArmOperand SystemRegisterOperand(ArmSystemRegister register)
        => new ArmOperand(ArmOperandKind.SystemRegister, ArmRegister.Invalid, ArmRegister.Invalid, ArmRegister.Invalid, 0, 0, ArmShiftKind.None,
            0, ArmExtendKind.None, ArmAddressingMode.Offset, 0, null, 0, ArmRelocationKind.None, register);

    public ArmOperand WithImmediate(long immediate)
        => new ArmOperand(Kind, Register, BaseRegister, IndexRegister, Size, immediate, Shift, ShiftAmount,
            Extend, AddressingMode, RegisterMask, Symbol, Addend, RelocationKind, SystemRegister);

    public ArmOperand WithSize(int size)
        => new ArmOperand(Kind, Register, BaseRegister, IndexRegister, size, Immediate, Shift, ShiftAmount,
            Extend, AddressingMode, RegisterMask, Symbol, Addend, RelocationKind, SystemRegister);

    public ArmOperand WithSymbol(string symbol, ArmRelocationKind relocationKind, long addend = 0)
        => new ArmOperand(Kind, Register, BaseRegister, IndexRegister, Size, Immediate, Shift, ShiftAmount,
            Extend, AddressingMode, RegisterMask, symbol, addend, relocationKind, SystemRegister);
}

public readonly struct ArmInstruction
{
    public ArmInstrKind Opcode { get; }
    public ArmOperand Operand0 { get; }
    public ArmOperand Operand1 { get; }
    public ArmOperand Operand2 { get; }
    public ArmOperand Operand3 { get; }
    public ArmCondition Condition { get; }
    public bool SetFlags { get; }
    public uint RawWord { get; }
    public bool HasSymbol => Operand0.HasSymbol || Operand1.HasSymbol || Operand2.HasSymbol || Operand3.HasSymbol;

    public ArmInstruction(
        ArmInstrKind opcode,
        ArmOperand operand0 = default,
        ArmOperand operand1 = default,
        ArmOperand operand2 = default,
        ArmOperand operand3 = default,
        ArmCondition condition = ArmCondition.Al,
        bool setFlags = false,
        uint rawWord = 0)
    {
        Opcode = opcode;
        Operand0 = operand0;
        Operand1 = operand1;
        Operand2 = operand2;
        Operand3 = operand3;
        Condition = condition;
        SetFlags = setFlags;
        RawWord = rawWord;
    }

    public ArmInstruction WithOperand0(ArmOperand operand)
        => new ArmInstruction(Opcode, operand, Operand1, Operand2, Operand3, Condition, SetFlags, RawWord);

    public ArmInstruction WithOperand1(ArmOperand operand)
        => new ArmInstruction(Opcode, Operand0, operand, Operand2, Operand3, Condition, SetFlags, RawWord);

    public ArmInstruction WithOperand2(ArmOperand operand)
        => new ArmInstruction(Opcode, Operand0, Operand1, operand, Operand3, Condition, SetFlags, RawWord);

    public ArmInstruction WithOperand3(ArmOperand operand)
        => new ArmInstruction(Opcode, Operand0, Operand1, Operand2, operand, Condition, SetFlags, RawWord);

    public ArmInstruction WithCondition(ArmCondition condition)
        => new ArmInstruction(Opcode, Operand0, Operand1, Operand2, Operand3, condition, SetFlags, RawWord);

    public ArmInstruction WithSetFlags(bool setFlags)
        => new ArmInstruction(Opcode, Operand0, Operand1, Operand2, Operand3, Condition, setFlags, RawWord);

    public static ArmInstruction Raw(uint word)
        => new ArmInstruction(ArmInstrKind.Raw, rawWord: word);

    public static ArmInstruction Nop()
        => new ArmInstruction(ArmInstrKind.Nop);

    public static ArmInstruction Unary(ArmInstrKind opcode, ArmOperand operand, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, operand, condition: condition);

    public static ArmInstruction Binary(ArmInstrKind opcode, ArmOperand destination, ArmOperand source, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, destination, source, condition: condition);

    public static ArmInstruction Ternary(ArmInstrKind opcode, ArmOperand destination, ArmOperand source1, ArmOperand source2, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, destination, source1, source2, condition: condition);

    public static ArmInstruction Quaternary(ArmInstrKind opcode, ArmOperand destination, ArmOperand source1,
        ArmOperand source2, ArmOperand source3, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, destination, source1, source2, source3, condition);

    public static ArmInstruction Branch(ArmInstrKind opcode, long displacement, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, ArmOperand.ImmediateOperand(displacement), condition: condition);

    public static ArmInstruction Branch(ArmInstrKind opcode, string symbol, ArmCondition condition = ArmCondition.Al)
        => new ArmInstruction(opcode, ArmOperand.SymbolOperand(symbol, opcode == ArmInstrKind.Bl
            ? ArmRelocationKind.Call
            : condition == ArmCondition.Al ? ArmRelocationKind.Branch : ArmRelocationKind.ConditionalBranch), condition: condition);
}

public sealed class ArmTarget
{
    public static ArmTarget ArmV7 { get; } = new ArmTarget(32, ArmAbiKind.Aapcs, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon);
    public static ArmTarget ArmV7Linux { get; } = new ArmTarget(32, ArmAbiKind.Aapcs, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon,
        TargetEndianness.Little, OperatingSystemKind.Linux);
    public static ArmTarget ArmV7HardFloatLinux { get; } = new ArmTarget(32, ArmAbiKind.AapcsVfp, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon
        | ArmIsaFlags.HardFloat, TargetEndianness.Little, OperatingSystemKind.Linux);
    public static ArmTarget ArmV7Windows { get; } = new ArmTarget(32, ArmAbiKind.WindowsArm, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon,
        TargetEndianness.Little, OperatingSystemKind.Windows);
    public static ArmTarget Arm64 { get; } = new ArmTarget(64, ArmAbiKind.Aapcs64, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon);
    public static ArmTarget Arm64Linux { get; } = new ArmTarget(64, ArmAbiKind.Aapcs64, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon,
        TargetEndianness.Little, OperatingSystemKind.Linux);
    public static ArmTarget Arm64Windows { get; } = new ArmTarget(64, ArmAbiKind.WindowsArm64, ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32 | ArmIsaFlags.Neon,
        TargetEndianness.Little, OperatingSystemKind.Windows);

    public static ArmTarget FromTargetInfo(Cnidaria.C.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.Arm32 and not TargetArchitectureKind.Arm64)
            throw new ArgumentException("Target architecture is not ARM", nameof(target));
        return CreateFromDescriptor(target.Architecture, target.OperatingSystem, target.ArchitectureFeatures, target.Endianness);
    }

    public static ArmTarget FromTargetInfo(Cnidaria.Cs.TargetInfo target)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (target.Architecture is not TargetArchitectureKind.Arm32 and not TargetArchitectureKind.Arm64)
            throw new ArgumentException("Target architecture is not ARM", nameof(target));
        return CreateFromDescriptor(target.Architecture, target.OperatingSystem, target.ArchitectureFeatures, target.Endianness);
    }

    private static ArmTarget CreateFromDescriptor(
        TargetArchitectureKind architecture, OperatingSystemKind operatingSystem, TargetArchitectureFeatures features, TargetEndianness endianness)
    {
        var isa = ArmIsaFlags.None;
        if ((features & TargetArchitectureFeatures.ArmVfp) != 0)
            isa |= ArmIsaFlags.Vfp;
        if ((features & TargetArchitectureFeatures.ArmVfpD32) != 0)
            isa |= ArmIsaFlags.Vfp | ArmIsaFlags.VfpD32;
        if ((features & TargetArchitectureFeatures.ArmNeon) != 0)
            isa |= ArmIsaFlags.Vfp | ArmIsaFlags.Neon;
        if ((features & TargetArchitectureFeatures.ArmHardFloat) != 0)
            isa |= ArmIsaFlags.Vfp | ArmIsaFlags.HardFloat;

        if (architecture == TargetArchitectureKind.Arm32)
        {
            var abi = operatingSystem == OperatingSystemKind.Windows
                ? ArmAbiKind.WindowsArm
                : (isa & ArmIsaFlags.HardFloat) != 0 ? ArmAbiKind.AapcsVfp : ArmAbiKind.Aapcs;
            return new ArmTarget(32, abi, isa, endianness, operatingSystem);
        }

        return new ArmTarget(64,
            operatingSystem == OperatingSystemKind.Windows
                ? ArmAbiKind.WindowsArm64
                : ArmAbiKind.Aapcs64,
            isa | ArmIsaFlags.Vfp | ArmIsaFlags.Neon, endianness, operatingSystem);
    }

    public int XLen { get; }
    public ArmAbiKind Abi { get; }
    public ArmIsaFlags Isa { get; }
    public TargetEndianness Endianness { get; }
    public OperatingSystemKind OperatingSystem { get; }
    public bool Is32Bit => XLen == 32;
    public bool Is64Bit => XLen == 64;
    public bool IsAArch32 => XLen == 32;
    public bool IsAArch64 => XLen == 64;
    public bool HasVfp => Has(ArmIsaFlags.Vfp);
    public bool HasVfpD32 => Has(ArmIsaFlags.VfpD32);
    public bool HasNeon => Has(ArmIsaFlags.Neon);
    public bool UsesHardFloat => Has(ArmIsaFlags.HardFloat) || Abi == ArmAbiKind.AapcsVfp;

    public ArmTarget(
        int xlen,
        ArmAbiKind abi,
        ArmIsaFlags isa,
        TargetEndianness endianness = TargetEndianness.Little,
        OperatingSystemKind operatingSystem = OperatingSystemKind.None)
    {
        if (xlen is not 32 and not 64)
            throw new ArgumentOutOfRangeException(nameof(xlen));
        if (xlen == 32 && abi is (ArmAbiKind.Aapcs64 or ArmAbiKind.WindowsArm64))
            throw new ArgumentException("AArch64 ABI requires a 64-bit target", nameof(abi));
        if (xlen == 64 && abi is (ArmAbiKind.Aapcs or ArmAbiKind.AapcsVfp or ArmAbiKind.WindowsArm))
            throw new ArgumentException("AArch32 ABI requires a 32-bit target", nameof(abi));

        XLen = xlen;
        Abi = abi;
        Isa = isa;
        Endianness = endianness;
        OperatingSystem = operatingSystem;
    }

    public bool Has(ArmIsaFlags flags)
        => (Isa & flags) == flags;

    public override string ToString()
    {
        var name = Is64Bit ? "aarch64" : "armv7";
        name += OperatingSystem switch
        {
            OperatingSystemKind.Windows => "-windows",
            OperatingSystemKind.Linux => "-linux",
            _ => string.Empty,
        };
        if (Endianness == TargetEndianness.Big)
            name += "-be";
        if (Is32Bit && UsesHardFloat)
            name += "+hard-float";
        if (HasNeon)
            name += "+neon";
        else if (HasVfp)
            name += "+vfp";
        return name;
    }
}

internal static class ArmRegisters
{
    public static ImmutableArray<ArmRegister> Aapcs32IntegerArguments { get; } = ImmutableArray.Create(
        ArmRegister.R0, ArmRegister.R1, ArmRegister.R2, ArmRegister.R3);
    public static ImmutableArray<ArmRegister> Aapcs64IntegerArguments { get; } = CreateRange(ArmRegister.X0, 8);
    public static ImmutableArray<ArmRegister> Aapcs32CallerSaved { get; } = ImmutableArray.Create(
        ArmRegister.R0, ArmRegister.R1, ArmRegister.R2, ArmRegister.R3, ArmRegister.R12, ArmRegister.Lr);
    public static ImmutableArray<ArmRegister> Aapcs32CalleeSaved { get; } = ImmutableArray.Create(
        ArmRegister.R4, ArmRegister.R5, ArmRegister.R6, ArmRegister.R7, ArmRegister.R8, ArmRegister.R9, ArmRegister.R10, ArmRegister.R11);
    public static ImmutableArray<ArmRegister> Aapcs64CallerSaved { get; } = CreateRange(
        ArmRegister.X0, 19);
    public static ImmutableArray<ArmRegister> Aapcs64CalleeSaved { get; } = CreateRange(
        ArmRegister.X19, 10);
    public static ImmutableArray<ArmRegister> Aapcs32AllocatableGprs { get; } = ImmutableArray.Create(
        ArmRegister.R0, ArmRegister.R1, ArmRegister.R2, ArmRegister.R3, ArmRegister.R4, ArmRegister.R5, ArmRegister.R6, ArmRegister.R7,
        ArmRegister.R8, ArmRegister.R9, ArmRegister.R10, ArmRegister.R11, ArmRegister.R12);
    public static ImmutableArray<ArmRegister> Aapcs64AllocatableGprs { get; } = CreateRange(ArmRegister.X0, 29);
    public static ImmutableArray<ArmRegister> VectorRegisters { get; } = CreateRange(ArmRegister.V0, 32);

    public static bool IsAArch32General(ArmRegister register)
        => register >= ArmRegister.R0 && register <= ArmRegister.R15;

    public static bool IsAArch64General(ArmRegister register)
        => register >= ArmRegister.X0 && register <= ArmRegister.X30;

    public static bool IsZeroRegister(ArmRegister register)
        => register == ArmRegister.Xzr;

    public static bool IsVector(ArmRegister register)
        => register >= ArmRegister.V0 && register <= ArmRegister.V31;

    public static int Index(ArmRegister register)
    {
        if (IsAArch32General(register))
            return (int)register;
        if (register == ArmRegister.Sp)
            return 31;
        if (IsAArch64General(register))
            return (int)register - (int)ArmRegister.X0;
        if (IsZeroRegister(register))
            return 31;
        if (IsVector(register))
            return (int)register - (int)ArmRegister.V0;
        throw new ArgumentOutOfRangeException(nameof(register));
    }

    public static string Format(ArmRegister register, int size, ArmTarget target)
    {
        if (register == ArmRegister.Invalid)
            return "invalid";
        if (target.Is32Bit)
        {
            return register switch
            {
                ArmRegister.R13 => "sp",
                ArmRegister.Sp => "sp",
                ArmRegister.Lr => "lr",
                ArmRegister.Pc => "pc",
                _ when IsAArch32General(register) => $"r{Index(register)}",
                _ when IsVector(register) => FormatVector(register, size),
                _ => throw new ArgumentOutOfRangeException(nameof(register)),
            };
        }

        if (register == ArmRegister.Sp)
            return size == 4 ? "wsp" : "sp";
        if (register == ArmRegister.Xzr)
            return size == 4 ? "wzr" : "xzr";
        if (IsAArch64General(register))
            return $"{(size == 4 ? "w" : "x")}{Index(register)}";
        if (IsVector(register))
            return FormatVector(register, size);
        throw new ArgumentOutOfRangeException(nameof(register));
    }

    public static bool TryParse(string text, ArmTarget target, out ArmRegister register, out int size)
    {
        register = ArmRegister.Invalid;
        size = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim().ToLowerInvariant();
        if (target.Is32Bit)
        {
            if (value == "sp")
            {
                register = ArmRegister.R13;
                size = 4;
                return true;
            }
            if (value == "lr")
            {
                register = ArmRegister.Lr;
                size = 4;
                return true;
            }
            if (value == "pc")
            {
                register = ArmRegister.Pc;
                size = 4;
                return true;
            }
            if (TryParseIndexed(value, 'r', 15, out var index))
            {
                register = (ArmRegister)index;
                size = 4;
                return true;
            }
        }
        else
        {
            if (value == "sp")
            {
                register = ArmRegister.Sp;
                size = 8;
                return true;
            }
            if (value == "wsp")
            {
                register = ArmRegister.Sp;
                size = 4;
                return true;
            }
            if (value == "xzr")
            {
                register = ArmRegister.Xzr;
                size = 8;
                return true;
            }
            if (value == "wzr")
            {
                register = ArmRegister.Xzr;
                size = 4;
                return true;
            }
            if (TryParseIndexed(value, 'x', 30, out var x))
            {
                register = (ArmRegister)((int)ArmRegister.X0 + x);
                size = 8;
                return true;
            }
            if (TryParseIndexed(value, 'w', 30, out var w))
            {
                register = (ArmRegister)((int)ArmRegister.X0 + w);
                size = 4;
                return true;
            }
        }

        if (TryParseIndexed(value, 's', 31, out var s))
        {
            register = (ArmRegister)((int)ArmRegister.V0 + s);
            size = 4;
            return true;
        }
        if (TryParseIndexed(value, 'd', 31, out var d))
        {
            register = (ArmRegister)((int)ArmRegister.V0 + d);
            size = 8;
            return true;
        }
        if (TryParseIndexed(value, 'q', 31, out var q) || TryParseIndexed(value, 'v', 31, out q))
        {
            register = (ArmRegister)((int)ArmRegister.V0 + q);
            size = 16;
            return true;
        }
        return false;
    }

    public static ArmRegister Parse(string text, ArmTarget target, out int size)
    {
        if (TryParse(text, target, out var register, out size))
            return register;
        throw new FormatException($"Invalid register: {text}");
    }

    private static string FormatVector(ArmRegister register, int size)
    {
        var prefix = size switch
        {
            4 => "s",
            8 => "d",
            16 => "q",
            _ => "v",
        };
        return prefix + Index(register).ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseIndexed(string text, char prefix, int maximum, out int index)
    {
        index = -1;
        if (text.Length < 2 || text[0] != prefix)
            return false;
        if (!int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out index))
            return false;
        return index >= 0 && index <= maximum;
    }

    private static ImmutableArray<ArmRegister> CreateRange(ArmRegister first, int count)
    {
        var builder = ImmutableArray.CreateBuilder<ArmRegister>(count);
        var start = (int)first;
        for (var i = 0; i < count; i++)
            builder.Add((ArmRegister)(start + i));
        return builder.MoveToImmutable();
    }
}

internal static class ArmConditions
{
    public static string Format(ArmCondition condition)
        => condition switch
        {
            ArmCondition.Eq => "eq",
            ArmCondition.Ne => "ne",
            ArmCondition.Cs => "cs",
            ArmCondition.Cc => "cc",
            ArmCondition.Mi => "mi",
            ArmCondition.Pl => "pl",
            ArmCondition.Vs => "vs",
            ArmCondition.Vc => "vc",
            ArmCondition.Hi => "hi",
            ArmCondition.Ls => "ls",
            ArmCondition.Ge => "ge",
            ArmCondition.Lt => "lt",
            ArmCondition.Gt => "gt",
            ArmCondition.Le => "le",
            ArmCondition.Al => "al",
            ArmCondition.Nv => "nv",
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };

    public static bool TryParse(string text, out ArmCondition condition)
    {
        condition = ArmCondition.Al;
        if (text is null)
            return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "eq": condition = ArmCondition.Eq; return true;
            case "ne": condition = ArmCondition.Ne; return true;
            case "cs":
            case "hs": condition = ArmCondition.Cs; return true;
            case "cc":
            case "lo": condition = ArmCondition.Cc; return true;
            case "mi": condition = ArmCondition.Mi; return true;
            case "pl": condition = ArmCondition.Pl; return true;
            case "vs": condition = ArmCondition.Vs; return true;
            case "vc": condition = ArmCondition.Vc; return true;
            case "hi": condition = ArmCondition.Hi; return true;
            case "ls": condition = ArmCondition.Ls; return true;
            case "ge": condition = ArmCondition.Ge; return true;
            case "lt": condition = ArmCondition.Lt; return true;
            case "gt": condition = ArmCondition.Gt; return true;
            case "le": condition = ArmCondition.Le; return true;
            case "al": condition = ArmCondition.Al; return true;
            case "nv": condition = ArmCondition.Nv; return true;
            default: return false;
        }
    }
}

internal static class ArmMnemonics
{
    private static readonly Dictionary<string, ArmInstrKind> ByName = CreateMap();

    public static bool TryParse(string mnemonic, out ArmInstrKind opcode)
        => ByName.TryGetValue(mnemonic, out opcode);

    public static string Format(ArmInstrKind opcode)
        => opcode switch
        {
            ArmInstrKind.Raw => ".word",
            _ => opcode.ToString().ToLowerInvariant(),
        };

    private static Dictionary<string, ArmInstrKind> CreateMap()
    {
        var result = new Dictionary<string, ArmInstrKind>(StringComparer.OrdinalIgnoreCase);
        foreach (ArmInstrKind kind in Enum.GetValues<ArmInstrKind>())
        {
            if (kind is ArmInstrKind.Invalid or ArmInstrKind.Raw)
                continue;
            result[kind.ToString().ToLowerInvariant()] = kind;
        }
        return result;
    }
}
