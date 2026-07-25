using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.X86
{
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

        public X86LinkedImage LinkFlat(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
            => X86ObjectLinker.LinkFlat(this, imageBase, externalSymbols);

        public byte[] ToExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
            => Target.OperatingSystem switch
            {
                OperatingSystemKind.Windows => X86PortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86PortableExecutableWriter.DefaultImageBase(Target) : imageBase),
                OperatingSystemKind.Linux => X86ElfExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86ElfExecutableWriter.DefaultImageBase(Target) : imageBase, externalSymbols),
                _ => LinkFlat(imageBase, externalSymbols).Bytes.ToArray(),
            };

        public byte[] ToWindowsExecutableBytes(ulong imageBase = 0)
            => X86PortableExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86PortableExecutableWriter.DefaultImageBase(Target) : imageBase);

        public byte[] ToLinuxExecutableBytes(ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
            => X86ElfExecutableWriter.WriteExecutable(this, imageBase == 0 ? X86ElfExecutableWriter.DefaultImageBase(Target) : imageBase, externalSymbols);
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

        public X86ObjectSymbol(
            string name,
            string sectionName,
            int offset,
            int size,
            X86ObjectSymbolBinding binding,
            X86ObjectSymbolKind kind)
        {
            Name = name ?? string.Empty;
            SectionName = sectionName ?? string.Empty;
            Offset = Math.Max(0, offset);
            Size = Math.Max(0, size);
            Binding = binding;
            Kind = kind;
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
        Vzeroupper,
        Vzeroall,
        Vmovaps,
        Vmovups,
        Vmovapd,
        Vmovupd,
        Vmovdqa,
        Vmovdqu,
        Vaddss,
        Vaddsd,
        Vsubss,
        Vsubsd,
        Vmulss,
        Vmulsd,
        Vdivss,
        Vdivsd,
        Vsqrtss,
        Vsqrtsd,
        Vucomiss,
        Vucomisd,
        Vcvtsi2ss,
        Vcvtsi2sd,
        Vcvttss2si,
        Vcvttsd2si,
        Vaddps,
        Vaddpd,
        Vsubps,
        Vsubpd,
        Vmulps,
        Vmulpd,
        Vdivps,
        Vdivpd,
        Vsqrtps,
        Vsqrtpd,
        Vandps,
        Vandpd,
        Vorps,
        Vorpd,
        Vxorps,
        Vxorpd,
        Vpaddb,
        Vpaddw,
        Vpaddd,
        Vpaddq,
        Vpsubb,
        Vpsubw,
        Vpsubd,
        Vpsubq,
        Vpmulld,
        Vpand,
        Vpor,
        Vpxor,
        Vpcmpeqb,
        Vpcmpeqw,
        Vpcmpeqd,
        Vpcmpeqq,
        Vpcmpgtb,
        Vpcmpgtw,
        Vpcmpgtd,
        Vpcmpgtq,
        Vpslld,
        Vpsllq,
        Vpsrld,
        Vpsrlq,
        Vpsrad,
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
            bool isRipRelative)
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
            => new X86Operand(Kind, size, Register, BaseRegister, IndexRegister, Scale, Displacement, Immediate, Symbol, Addend, RelocationKind, IsRipRelative);

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

    public readonly struct X86Instruction
    {
        public X86InstrKind Opcode { get; }
        public X86Operand Operand0 { get; }
        public X86Operand Operand1 { get; }
        public X86Operand Operand2 { get; }
        public X86Condition Condition { get; }
        public ImmutableArray<byte> RawBytes { get; }

        public X86Instruction(
            X86InstrKind opcode,
            X86Operand operand0 = default,
            X86Operand operand1 = default,
            X86Operand operand2 = default,
            X86Condition condition = X86Condition.E,
            ImmutableArray<byte> rawBytes = default)
        {
            Opcode = opcode;
            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
            Condition = condition;
            RawBytes = rawBytes.IsDefault ? ImmutableArray<byte>.Empty : rawBytes;
        }

        public X86Instruction WithOperand0(X86Operand operand)
            => new X86Instruction(Opcode, operand, Operand1, Operand2, Condition, RawBytes);

        public X86Instruction WithOperand1(X86Operand operand)
            => new X86Instruction(Opcode, Operand0, operand, Operand2, Condition, RawBytes);

        public X86Instruction WithOperand2(X86Operand operand)
            => new X86Instruction(Opcode, Operand0, Operand1, operand, Condition, RawBytes);

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
                throw new ArgumentException("Duplicate x86 label: " + label, nameof(label));
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
            if ((features & TargetArchitectureFeatures.X86Avx) != 0)
                isa |= X86IsaFlags.Avx;
            if ((features & TargetArchitectureFeatures.X86Avx2) != 0)
                isa |= X86IsaFlags.Avx | X86IsaFlags.Avx2;

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
            => register >= X86Register.Xmm0 && register <= X86Register.Xmm15;

        public static bool IsYmm(X86Register register)
            => register >= X86Register.Ymm0 && register <= X86Register.Ymm15;

        public static bool IsVector(X86Register register)
            => IsXmm(register) || IsYmm(register);

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
                return "xmm" + Index(register).ToString(CultureInfo.InvariantCulture);
            if (IsYmm(register))
                return "ymm" + Index(register).ToString(CultureInfo.InvariantCulture);
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
            throw new FormatException("Invalid x86 register: " + text);
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
            for (var i = 0; i < 16; i++)
            {
                Add(map, "xmm" + i.ToString(CultureInfo.InvariantCulture), (X86Register)((int)X86Register.Xmm0 + i), 16);
                Add(map, "ymm" + i.ToString(CultureInfo.InvariantCulture), (X86Register)((int)X86Register.Ymm0 + i), 32);
            }
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
        private static readonly Dictionary<X86InstrKind, X86InstructionMetadata> ByOpcode = CreateOpcodeMap();
        private static readonly Dictionary<string, X86InstrKind> ByMnemonic = CreateMnemonicMap();

        public static X86InstructionMetadata Get(X86InstrKind opcode)
        {
            if (ByOpcode.TryGetValue(opcode, out var metadata))
                return metadata;
            throw new ArgumentOutOfRangeException(nameof(opcode));
        }

        public static X86InstrKind GetOpcode(string mnemonic)
        {
            if (mnemonic is null)
                throw new ArgumentNullException(nameof(mnemonic));
            if (ByMnemonic.TryGetValue(mnemonic.Trim().ToLowerInvariant(), out var opcode))
                return opcode;
            throw new FormatException("Unknown x86 mnemonic: " + mnemonic);
        }

        public static bool TryGetOpcode(string mnemonic, out X86InstrKind opcode)
        {
            opcode = X86InstrKind.Invalid;
            return mnemonic is not null && ByMnemonic.TryGetValue(mnemonic.Trim().ToLowerInvariant(), out opcode);
        }

        public static string GetMnemonic(X86InstrKind opcode)
        {
            return opcode switch
            {
                X86InstrKind.Raw => ".byte",
                X86InstrKind.Nop => "nop",
                X86InstrKind.Ret => "ret",
                X86InstrKind.Push => "push",
                X86InstrKind.Pop => "pop",
                X86InstrKind.Mov => "mov",
                X86InstrKind.Lea => "lea",
                X86InstrKind.Movsx => "movsx",
                X86InstrKind.Movsxd => "movsxd",
                X86InstrKind.Movzx => "movzx",
                X86InstrKind.Add => "add",
                X86InstrKind.Or => "or",
                X86InstrKind.Adc => "adc",
                X86InstrKind.Sbb => "sbb",
                X86InstrKind.And => "and",
                X86InstrKind.Sub => "sub",
                X86InstrKind.Xor => "xor",
                X86InstrKind.Cmp => "cmp",
                X86InstrKind.Test => "test",
                X86InstrKind.Inc => "inc",
                X86InstrKind.Dec => "dec",
                X86InstrKind.Neg => "neg",
                X86InstrKind.Not => "not",
                X86InstrKind.Imul => "imul",
                X86InstrKind.Mul => "mul",
                X86InstrKind.Idiv => "idiv",
                X86InstrKind.Div => "div",
                X86InstrKind.Shl => "shl",
                X86InstrKind.Shr => "shr",
                X86InstrKind.Sar => "sar",
                X86InstrKind.Rol => "rol",
                X86InstrKind.Ror => "ror",
                X86InstrKind.Call => "call",
                X86InstrKind.Jmp => "jmp",
                X86InstrKind.Jcc => "j" + X86Conditions.Format(X86Condition.E),
                X86InstrKind.Setcc => "set" + X86Conditions.Format(X86Condition.E),
                X86InstrKind.Cmovcc => "cmov" + X86Conditions.Format(X86Condition.E),
                X86InstrKind.Cdq => "cdq",
                X86InstrKind.Cqo => "cqo",
                X86InstrKind.Cbw => "cbw",
                X86InstrKind.Cwde => "cwde",
                X86InstrKind.Cdqe => "cdqe",
                X86InstrKind.Leave => "leave",
                X86InstrKind.Int3 => "int3",
                X86InstrKind.Ud2 => "ud2",
                X86InstrKind.Syscall => "syscall",
                X86InstrKind.Movss => "movss",
                X86InstrKind.Movsd => "movsd",
                X86InstrKind.Addss => "addss",
                X86InstrKind.Addsd => "addsd",
                X86InstrKind.Subss => "subss",
                X86InstrKind.Subsd => "subsd",
                X86InstrKind.Mulss => "mulss",
                X86InstrKind.Mulsd => "mulsd",
                X86InstrKind.Divss => "divss",
                X86InstrKind.Divsd => "divsd",
                X86InstrKind.Ucomiss => "ucomiss",
                X86InstrKind.Ucomisd => "ucomisd",
                X86InstrKind.Cvtsi2ss => "cvtsi2ss",
                X86InstrKind.Cvtsi2sd => "cvtsi2sd",
                X86InstrKind.Cvtss2sd => "cvtss2sd",
                X86InstrKind.Cvtsd2ss => "cvtsd2ss",
                X86InstrKind.Cvttss2si => "cvttss2si",
                X86InstrKind.Cvttsd2si => "cvttsd2si",
                X86InstrKind.Sqrtss => "sqrtss",
                X86InstrKind.Sqrtsd => "sqrtsd",
                X86InstrKind.Movaps => "movaps",
                X86InstrKind.Movups => "movups",
                X86InstrKind.Movapd => "movapd",
                X86InstrKind.Movupd => "movupd",
                X86InstrKind.Movdqa => "movdqa",
                X86InstrKind.Movdqu => "movdqu",
                X86InstrKind.Addps => "addps",
                X86InstrKind.Addpd => "addpd",
                X86InstrKind.Subps => "subps",
                X86InstrKind.Subpd => "subpd",
                X86InstrKind.Mulps => "mulps",
                X86InstrKind.Mulpd => "mulpd",
                X86InstrKind.Divps => "divps",
                X86InstrKind.Divpd => "divpd",
                X86InstrKind.Sqrtps => "sqrtps",
                X86InstrKind.Sqrtpd => "sqrtpd",
                X86InstrKind.Andps => "andps",
                X86InstrKind.Andpd => "andpd",
                X86InstrKind.Orps => "orps",
                X86InstrKind.Orpd => "orpd",
                X86InstrKind.Xorps => "xorps",
                X86InstrKind.Xorpd => "xorpd",
                X86InstrKind.Pxor => "pxor",
                X86InstrKind.Vzeroupper => "vzeroupper",
                X86InstrKind.Vzeroall => "vzeroall",
                X86InstrKind.Vmovaps => "vmovaps",
                X86InstrKind.Vmovups => "vmovups",
                X86InstrKind.Vmovapd => "vmovapd",
                X86InstrKind.Vmovupd => "vmovupd",
                X86InstrKind.Vmovdqa => "vmovdqa",
                X86InstrKind.Vmovdqu => "vmovdqu",
                X86InstrKind.Vaddss => "vaddss",
                X86InstrKind.Vaddsd => "vaddsd",
                X86InstrKind.Vsubss => "vsubss",
                X86InstrKind.Vsubsd => "vsubsd",
                X86InstrKind.Vmulss => "vmulss",
                X86InstrKind.Vmulsd => "vmulsd",
                X86InstrKind.Vdivss => "vdivss",
                X86InstrKind.Vdivsd => "vdivsd",
                X86InstrKind.Vsqrtss => "vsqrtss",
                X86InstrKind.Vsqrtsd => "vsqrtsd",
                X86InstrKind.Vucomiss => "vucomiss",
                X86InstrKind.Vucomisd => "vucomisd",
                X86InstrKind.Vcvtsi2ss => "vcvtsi2ss",
                X86InstrKind.Vcvtsi2sd => "vcvtsi2sd",
                X86InstrKind.Vcvttss2si => "vcvttss2si",
                X86InstrKind.Vcvttsd2si => "vcvttsd2si",
                X86InstrKind.Vaddps => "vaddps",
                X86InstrKind.Vaddpd => "vaddpd",
                X86InstrKind.Vsubps => "vsubps",
                X86InstrKind.Vsubpd => "vsubpd",
                X86InstrKind.Vmulps => "vmulps",
                X86InstrKind.Vmulpd => "vmulpd",
                X86InstrKind.Vdivps => "vdivps",
                X86InstrKind.Vdivpd => "vdivpd",
                X86InstrKind.Vsqrtps => "vsqrtps",
                X86InstrKind.Vsqrtpd => "vsqrtpd",
                X86InstrKind.Vandps => "vandps",
                X86InstrKind.Vandpd => "vandpd",
                X86InstrKind.Vorps => "vorps",
                X86InstrKind.Vorpd => "vorpd",
                X86InstrKind.Vxorps => "vxorps",
                X86InstrKind.Vxorpd => "vxorpd",
                X86InstrKind.Vpaddb => "vpaddb",
                X86InstrKind.Vpaddw => "vpaddw",
                X86InstrKind.Vpaddd => "vpaddd",
                X86InstrKind.Vpaddq => "vpaddq",
                X86InstrKind.Vpsubb => "vpsubb",
                X86InstrKind.Vpsubw => "vpsubw",
                X86InstrKind.Vpsubd => "vpsubd",
                X86InstrKind.Vpsubq => "vpsubq",
                X86InstrKind.Vpmulld => "vpmulld",
                X86InstrKind.Vpand => "vpand",
                X86InstrKind.Vpor => "vpor",
                X86InstrKind.Vpxor => "vpxor",
                X86InstrKind.Vpcmpeqb => "vpcmpeqb",
                X86InstrKind.Vpcmpeqw => "vpcmpeqw",
                X86InstrKind.Vpcmpeqd => "vpcmpeqd",
                X86InstrKind.Vpcmpeqq => "vpcmpeqq",
                X86InstrKind.Vpcmpgtb => "vpcmpgtb",
                X86InstrKind.Vpcmpgtw => "vpcmpgtw",
                X86InstrKind.Vpcmpgtd => "vpcmpgtd",
                X86InstrKind.Vpcmpgtq => "vpcmpgtq",
                X86InstrKind.Vpslld => "vpslld",
                X86InstrKind.Vpsllq => "vpsllq",
                X86InstrKind.Vpsrld => "vpsrld",
                X86InstrKind.Vpsrlq => "vpsrlq",
                X86InstrKind.Vpsrad => "vpsrad",
                _ => "invalid",
            };
        }

        private static Dictionary<X86InstrKind, X86InstructionMetadata> CreateOpcodeMap()
        {
            var map = new Dictionary<X86InstrKind, X86InstructionMetadata>();
            Add(map, X86InstrKind.Raw, X86InstructionFormat.Raw);
            Add(map, X86InstrKind.Nop, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Ret, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Push, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Pop, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Mov, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Lea, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Movsx, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Movsxd, X86InstructionFormat.Binary, requires64Bit: true);
            Add(map, X86InstrKind.Movzx, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Add, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Or, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Adc, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Sbb, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.And, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Sub, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Xor, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Cmp, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Test, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Inc, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Dec, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Neg, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Not, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Imul, X86InstructionFormat.Ternary);
            Add(map, X86InstrKind.Mul, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Idiv, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Div, X86InstructionFormat.Unary);
            Add(map, X86InstrKind.Shl, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Shr, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Sar, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Rol, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Ror, X86InstructionFormat.Binary);
            Add(map, X86InstrKind.Call, X86InstructionFormat.Branch);
            Add(map, X86InstrKind.Jmp, X86InstructionFormat.Branch);
            Add(map, X86InstrKind.Jcc, X86InstructionFormat.Branch);
            Add(map, X86InstrKind.Setcc, X86InstructionFormat.ConditionUnary);
            Add(map, X86InstrKind.Cmovcc, X86InstructionFormat.ConditionBinary);
            Add(map, X86InstrKind.Cdq, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Cqo, X86InstructionFormat.NoOperands, requires64Bit: true);
            Add(map, X86InstrKind.Cbw, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Cwde, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Cdqe, X86InstructionFormat.NoOperands, requires64Bit: true);
            Add(map, X86InstrKind.Leave, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Int3, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Ud2, X86InstructionFormat.NoOperands);
            Add(map, X86InstrKind.Syscall, X86InstructionFormat.NoOperands, requires64Bit: true);
            Add(map, X86InstrKind.Movss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Movsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Addss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Addsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Subss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Subsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Mulss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Mulsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Divss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Divsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Ucomiss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Ucomisd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Cvtsi2ss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Cvtsi2sd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Cvtss2sd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Cvtsd2ss, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Cvttss2si, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Cvttsd2si, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Sqrtss, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Sqrtsd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Movaps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Movups, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Movapd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Movupd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Movdqa, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Movdqu, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Addps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Addpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Subps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Subpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Mulps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Mulpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Divps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Divpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Sqrtps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Sqrtpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Andps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Andpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Orps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Orpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Xorps, X86InstructionFormat.Binary, X86IsaFlags.Sse);
            Add(map, X86InstrKind.Xorpd, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Pxor, X86InstructionFormat.Binary, X86IsaFlags.Sse2);
            Add(map, X86InstrKind.Vzeroupper, X86InstructionFormat.NoOperands, X86IsaFlags.Avx);
            Add(map, X86InstrKind.Vzeroall, X86InstructionFormat.NoOperands, X86IsaFlags.Avx);
            foreach (var op in AvxOpcodes())
                Add(map, op, AvxBinaryOpcodes().Contains(op) ? X86InstructionFormat.Binary : X86InstructionFormat.Ternary, X86IsaFlags.Avx);
            foreach (var op in Avx2Opcodes())
                Add(map, op, X86InstructionFormat.Ternary, X86IsaFlags.Avx | X86IsaFlags.Avx2);
            return map;
        }

        private static IEnumerable<X86InstrKind> AvxBinaryOpcodes()
        {
            yield return X86InstrKind.Vmovaps;
            yield return X86InstrKind.Vmovups;
            yield return X86InstrKind.Vmovapd;
            yield return X86InstrKind.Vmovupd;
            yield return X86InstrKind.Vmovdqa;
            yield return X86InstrKind.Vmovdqu;
            yield return X86InstrKind.Vucomiss;
            yield return X86InstrKind.Vucomisd;
            yield return X86InstrKind.Vcvttss2si;
            yield return X86InstrKind.Vcvttsd2si;
            yield return X86InstrKind.Vsqrtps;
            yield return X86InstrKind.Vsqrtpd;
        }

        private static IEnumerable<X86InstrKind> AvxOpcodes()
        {
            yield return X86InstrKind.Vmovaps;
            yield return X86InstrKind.Vmovups;
            yield return X86InstrKind.Vmovapd;
            yield return X86InstrKind.Vmovupd;
            yield return X86InstrKind.Vmovdqa;
            yield return X86InstrKind.Vmovdqu;
            yield return X86InstrKind.Vaddss;
            yield return X86InstrKind.Vaddsd;
            yield return X86InstrKind.Vsubss;
            yield return X86InstrKind.Vsubsd;
            yield return X86InstrKind.Vmulss;
            yield return X86InstrKind.Vmulsd;
            yield return X86InstrKind.Vdivss;
            yield return X86InstrKind.Vdivsd;
            yield return X86InstrKind.Vsqrtss;
            yield return X86InstrKind.Vsqrtsd;
            yield return X86InstrKind.Vucomiss;
            yield return X86InstrKind.Vucomisd;
            yield return X86InstrKind.Vcvtsi2ss;
            yield return X86InstrKind.Vcvtsi2sd;
            yield return X86InstrKind.Vcvttss2si;
            yield return X86InstrKind.Vcvttsd2si;
            yield return X86InstrKind.Vaddps;
            yield return X86InstrKind.Vaddpd;
            yield return X86InstrKind.Vsubps;
            yield return X86InstrKind.Vsubpd;
            yield return X86InstrKind.Vmulps;
            yield return X86InstrKind.Vmulpd;
            yield return X86InstrKind.Vdivps;
            yield return X86InstrKind.Vdivpd;
            yield return X86InstrKind.Vsqrtps;
            yield return X86InstrKind.Vsqrtpd;
            yield return X86InstrKind.Vandps;
            yield return X86InstrKind.Vandpd;
            yield return X86InstrKind.Vorps;
            yield return X86InstrKind.Vorpd;
            yield return X86InstrKind.Vxorps;
            yield return X86InstrKind.Vxorpd;
        }

        private static IEnumerable<X86InstrKind> Avx2Opcodes()
        {
            yield return X86InstrKind.Vpaddb;
            yield return X86InstrKind.Vpaddw;
            yield return X86InstrKind.Vpaddd;
            yield return X86InstrKind.Vpaddq;
            yield return X86InstrKind.Vpsubb;
            yield return X86InstrKind.Vpsubw;
            yield return X86InstrKind.Vpsubd;
            yield return X86InstrKind.Vpsubq;
            yield return X86InstrKind.Vpmulld;
            yield return X86InstrKind.Vpand;
            yield return X86InstrKind.Vpor;
            yield return X86InstrKind.Vpxor;
            yield return X86InstrKind.Vpcmpeqb;
            yield return X86InstrKind.Vpcmpeqw;
            yield return X86InstrKind.Vpcmpeqd;
            yield return X86InstrKind.Vpcmpeqq;
            yield return X86InstrKind.Vpcmpgtb;
            yield return X86InstrKind.Vpcmpgtw;
            yield return X86InstrKind.Vpcmpgtd;
            yield return X86InstrKind.Vpcmpgtq;
            yield return X86InstrKind.Vpslld;
            yield return X86InstrKind.Vpsllq;
            yield return X86InstrKind.Vpsrld;
            yield return X86InstrKind.Vpsrlq;
            yield return X86InstrKind.Vpsrad;
        }

        private static Dictionary<string, X86InstrKind> CreateMnemonicMap()
        {
            var map = new Dictionary<string, X86InstrKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ByOpcode)
            {
                var mnemonic = GetMnemonic(pair.Key);
                if (mnemonic != "invalid" && !mnemonic.StartsWith("j", StringComparison.Ordinal) && !mnemonic.StartsWith("set", StringComparison.Ordinal) && !mnemonic.StartsWith("cmov", StringComparison.Ordinal))
                    map[mnemonic] = pair.Key;
            }
            map["sal"] = X86InstrKind.Shl;
            return map;
        }

        private static void Add(Dictionary<X86InstrKind, X86InstructionMetadata> map, X86InstrKind opcode, X86InstructionFormat format, X86IsaFlags requiredIsa = X86IsaFlags.None, bool requires64Bit = false)
            => map.Add(opcode, new X86InstructionMetadata(format, requiredIsa, requires64Bit));
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
            throw new FormatException("Invalid x86 condition: " + text);
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
}
