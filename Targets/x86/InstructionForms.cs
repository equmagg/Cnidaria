using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Cnidaria.X86;

internal enum X86EncodingSpace : byte
{
    Legacy,
    Vex,
    Evex,
}

internal enum X86OperandRole : byte
{
    Reg,
    Rm,
    Vvvv,
    OpcodeRegister,
    Is4,
    Immediate,
    Relative,
    Implicit,
}

internal enum X86OperandClass : byte
{
    Gpr,
    Xmm,
    Ymm,
    Zmm,
    Mask,
    Memory,
    Immediate,
    Relative,
}

internal enum X86WMode : byte
{
    Ignored,
    Zero,
    One,
    OperandSize,
}

[Flags]
internal enum X86FormFlags : ushort
{
    None = 0,
    OpcodeRegister = 1 << 0,
    OpcodeCondition = 1 << 1,
    Default64 = 1 << 2,
    Mode64 = 1 << 3,
    Masking = 1 << 4,
    LegacyXmm0 = 1 << 5,
    VectorLengthIgnored = 1 << 6,
    Rounding = 1 << 7,
}

internal enum X86Tuple : byte
{
    None,
    Full,
    FullMemory,
    Half,
    HalfMemory,
    QuarterMemory,
    EighthMemory,
    Scalar,
    Fixed,
    Tuple2,
    Tuple4,
    Tuple8,
    Mem128,
    MoveDup,
}

internal readonly struct X86OperandSpec
{
    public const byte SizeV = 0xFE;
    public const byte SizeY = 0xFD;
    public const byte SizeZ = 0xFC;

    public X86OperandRole Role { get; init; }
    public X86OperandClass Class { get; init; }
    public bool AllowsMemory { get; init; }
    public bool AllowsRegister { get; init; }
    public byte Size { get; init; }
    public byte MemorySize { get; init; }
    public X86Register Fixed { get; init; }
    public bool SignedByte { get; init; }
    public bool One { get; init; }
    public byte Broadcast { get; init; }
    // The index register class of a VSIB operand
    public X86OperandClass IndexClass { get; init; }

    public bool IsVector => Class is X86OperandClass.Xmm or X86OperandClass.Ymm or X86OperandClass.Zmm;
}

// One encoding of an instruction kind; rows are tried in table order, shortest encodings first
internal sealed class X86Form
{
    public X86InstrKind Kind { get; init; }
    public X86EncodingSpace Space { get; init; }
    public byte Prefixes { get; init; }
    public byte Map { get; init; }
    public byte Opcode { get; init; }
    public sbyte Extension { get; init; } = -1;
    public short FixedModRm { get; init; } = -1;
    public X86WMode W { get; init; }
    public byte VectorLength { get; init; }
    public X86FormFlags Flags { get; init; }
    public X86IsaFlags Isa { get; init; }
    public X86Tuple Tuple { get; init; }
    public byte ElementSize { get; init; }
    public string? Mnemonic { get; init; }
    private readonly X86OperandSpec[] _operands = Array.Empty<X86OperandSpec>();

    public X86OperandSpec[] Operands
    {
        get => _operands;
        init
        {
            _operands = value;
            foreach (var operand in value)
            {
                if (operand.Role != X86OperandRole.Implicit || operand.One || operand.Fixed == X86Register.Rcx)
                    ExplicitOperandCount++;
                HasRegOrRm |= operand.Role is X86OperandRole.Reg or X86OperandRole.Rm;
                UsesOperandSize |= operand.Size is X86OperandSpec.SizeV or X86OperandSpec.SizeZ;
            }
        }
    }

    public int ExplicitOperandCount { get; private init; }

    public bool UsesOperandSize { get; private init; }

    private bool HasRegOrRm { get; init; }

    public bool Has(X86FormFlags flag) => (Flags & flag) != 0;

    public bool HasModRm => FixedModRm >= 0 || Extension >= 0 || HasRegOrRm;

    public override string ToString() => $"{Kind} {Space} map{Map} {Opcode:X2}";
}

internal static partial class X86Forms
{
    public const byte Prefix66 = 1;
    public const byte PrefixF3 = 2;
    public const byte PrefixF2 = 4;

    private static readonly List<X86Form> All = new List<X86Form>();
    private static readonly Dictionary<X86InstrKind, X86Form[]> ByKind;
    private static readonly Dictionary<(X86EncodingSpace, byte, byte), List<X86Form>> ByOpcode = new Dictionary<(X86EncodingSpace, byte, byte), List<X86Form>>();

    static X86Forms()
    {
        Build();
        var byKind = new Dictionary<X86InstrKind, List<X86Form>>();
        foreach (var form in All)
        {
            if (!byKind.TryGetValue(form.Kind, out var list))
                byKind.Add(form.Kind, list = new List<X86Form>());
            list.Add(form);

            int span = form.Has(X86FormFlags.OpcodeRegister) ? 8 : form.Has(X86FormFlags.OpcodeCondition) ? 16 : 1;
            for (int i = 0; i < span; i++)
            {
                var key = (form.Space, form.Map, (byte)(form.Opcode + i));
                if (!ByOpcode.TryGetValue(key, out var candidates))
                    ByOpcode.Add(key, candidates = new List<X86Form>());
                candidates.Add(form);
            }
        }
        ByKind = new Dictionary<X86InstrKind, X86Form[]>();
        foreach (var pair in byKind)
            ByKind.Add(pair.Key, pair.Value.ToArray());
    }

    public static IReadOnlyList<X86Form> ForKind(X86InstrKind kind)
        => ByKind.TryGetValue(kind, out var forms) ? forms : Array.Empty<X86Form>();

    public static IReadOnlyList<X86Form> ForOpcode(X86EncodingSpace space, byte map, byte opcode)
        => ByOpcode.TryGetValue((space, map, opcode), out var forms) ? forms : Array.Empty<X86Form>();

    public static IEnumerable<X86Form> Every => All;

    private static void Def(X86InstrKind kind, string opcode, string operands, X86IsaFlags isa = X86IsaFlags.None, X86FormFlags flags = X86FormFlags.None,
        X86Tuple tuple = X86Tuple.None, int element = 0, string? mnemonic = null)
        => All.Add(Parse(kind, opcode, operands, isa, flags, tuple, element, mnemonic));

    // The SDM opcode column: [NP|66|F2|F3]* [REX.W] [0F [38|3A]] op[+r|+cc] [/r|/digit] [!modrm], or VEX/EVEX.L.pp.map.W op [/r|/digit]
    private static X86Form Parse(X86InstrKind kind, string opcode, string operands, X86IsaFlags isa, X86FormFlags flags, X86Tuple tuple, int element, string? mnemonic)
    {
        var tokens = opcode.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var space = X86EncodingSpace.Legacy;
        byte prefixes = 0;
        byte map = 0;
        byte length = 0;
        var w = X86WMode.Ignored;
        int index = 0;

        if (tokens[0].StartsWith("VEX.", StringComparison.Ordinal) || tokens[0].StartsWith("EVEX.", StringComparison.Ordinal))
        {
            var parts = tokens[0].Split('.');
            space = parts[0] == "VEX" ? X86EncodingSpace.Vex : X86EncodingSpace.Evex;
            foreach (var part in parts[1..])
            {
                switch (part)
                {
                    case "128": case "L0": case "LZ": length = 0; break;
                    case "256": case "L1": length = 1; break;
                    case "512": length = 2; break;
                    case "LIG": case "LLIG": flags |= X86FormFlags.VectorLengthIgnored; break;
                    case "NP": break;
                    case "66": prefixes = Prefix66; break;
                    case "F3": prefixes = PrefixF3; break;
                    case "F2": prefixes = PrefixF2; break;
                    case "0F": map = 1; break;
                    case "0F38": map = 2; break;
                    case "0F3A": map = 3; break;
                    case "MAP5": map = 5; break;
                    case "MAP6": map = 6; break;
                    case "W0": w = X86WMode.Zero; break;
                    case "W1": w = X86WMode.One; break;
                    case "WIG": w = X86WMode.Ignored; break;
                    default: throw new InvalidOperationException($"Bad encoding field '{part}' in '{opcode}'");
                }
            }
            index = 1;
        }
        else
        {
            int opcodeIndex = tokens.Length - 1;
            while (tokens[opcodeIndex].StartsWith('/') || tokens[opcodeIndex].StartsWith('!'))
                opcodeIndex--;
            for (; index < opcodeIndex; index++)
            {
                switch (tokens[index])
                {
                    case "NP": break;
                    case "66": prefixes |= Prefix66; break;
                    case "F3": prefixes |= PrefixF3; break;
                    case "F2": prefixes |= PrefixF2; break;
                    case "REX.W": w = X86WMode.One; break;
                    case "0F": map = 1; break;
                    case "0F38": map = 2; break;
                    case "0F3A": map = 3; break;
                    case "38" when map == 1: map = 2; break;
                    case "3A" when map == 1: map = 3; break;
                    default: throw new InvalidOperationException($"Bad prefix '{tokens[index]}' in '{opcode}'");
                }
            }
        }

        string op = tokens[index++];
        if (op.EndsWith("+r", StringComparison.Ordinal))
        {
            flags |= X86FormFlags.OpcodeRegister;
            op = op[..^2];
        }
        else if (op.EndsWith("+cc", StringComparison.Ordinal))
        {
            flags |= X86FormFlags.OpcodeCondition;
            op = op[..^3];
        }
        byte opcodeByte = byte.Parse(op, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        sbyte extension = -1;
        short fixedModRm = -1;
        for (; index < tokens.Length; index++)
        {
            string token = tokens[index];
            if (token == "/r")
                continue;
            if (token.Length == 2 && token[0] == '/' && char.IsDigit(token[1]))
            {
                extension = (sbyte)(token[1] - '0');
                continue;
            }
            if (token.StartsWith('!'))
            {
                fixedModRm = byte.Parse(token[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }
            throw new InvalidOperationException($"Bad opcode token '{token}' in '{opcode}'");
        }

        var specs = ParseOperands(operands, flags);
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].IndexClass != X86OperandClass.Gpr)
                specs[i] = specs[i] with { MemorySize = (byte)(w == X86WMode.One ? 8 : 4) };
        }
        if (space == X86EncodingSpace.Legacy && w == X86WMode.Ignored && Array.Exists(specs, s => s.Size is X86OperandSpec.SizeV or X86OperandSpec.SizeY or X86OperandSpec.SizeZ))
            w = X86WMode.OperandSize;
        if (space != X86EncodingSpace.Legacy && w == X86WMode.Ignored && Array.Exists(specs, s => s.Size == X86OperandSpec.SizeY))
            w = X86WMode.OperandSize;

        return new X86Form
        {
            Kind = kind,
            Space = space,
            Prefixes = prefixes,
            Map = map,
            Opcode = opcodeByte,
            Extension = extension,
            FixedModRm = fixedModRm,
            W = w,
            VectorLength = length,
            Flags = flags,
            Isa = isa,
            Tuple = tuple,
            ElementSize = (byte)element,
            Mnemonic = mnemonic,
            Operands = specs,
        };
    }

    private static X86OperandSpec[] ParseOperands(string text, X86FormFlags flags)
    {
        if (text.Length == 0)
            return Array.Empty<X86OperandSpec>();
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        var result = new X86OperandSpec[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            result[i] = ParseOperand(parts[i], flags);
        return result;
    }

    private static X86OperandSpec ParseOperand(string token, X86FormFlags flags)
    {
        byte broadcast = 0;
        int slash = token.IndexOf("/b", StringComparison.Ordinal);
        if (slash >= 0)
        {
            broadcast = byte.Parse(token[(slash + 2)..], CultureInfo.InvariantCulture);
            token = token[..slash];
        }
        byte memoryOverride = 0;
        int memorySlash = token.IndexOf("/m", StringComparison.Ordinal);
        if (memorySlash >= 0)
        {
            memoryOverride = byte.Parse(token[(memorySlash + 2)..], CultureInfo.InvariantCulture);
            token = token[..memorySlash];
        }

        static byte GprSize(string suffix)
            => suffix switch
            {
                "8" => 1,
                "16" => 2,
                "32" => 4,
                "64" => 8,
                "v" => X86OperandSpec.SizeV,
                "y" => X86OperandSpec.SizeY,
                _ => throw new InvalidOperationException("Bad general register size " + suffix),
            };

        static X86OperandClass VectorClass(char c)
            => c switch
            {
                'x' => X86OperandClass.Xmm,
                'y' => X86OperandClass.Ymm,
                'z' => X86OperandClass.Zmm,
                _ => throw new InvalidOperationException("Bad vector class " + c),
            };

        static byte VectorSize(X86OperandClass c) => c switch { X86OperandClass.Xmm => 16, X86OperandClass.Ymm => 32, _ => 64 };

        switch (token)
        {
            case "ib": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 1 };
            case "ibs": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 1, SignedByte = true };
            case "iw": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 2 };
            case "id": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 4 };
            case "ids": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 4, SignedByte = true };
            case "iz": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = X86OperandSpec.SizeZ };
            case "iv": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = X86OperandSpec.SizeV };
            case "io": return new X86OperandSpec { Role = X86OperandRole.Immediate, Class = X86OperandClass.Immediate, Size = 8 };
            case "rel8": return new X86OperandSpec { Role = X86OperandRole.Relative, Class = X86OperandClass.Relative, Size = 1 };
            case "rel32": return new X86OperandSpec { Role = X86OperandRole.Relative, Class = X86OperandClass.Relative, Size = 4 };
            case "one": return new X86OperandSpec { Role = X86OperandRole.Implicit, Class = X86OperandClass.Immediate, Size = 1, One = true };
            case "cl": return new X86OperandSpec { Role = X86OperandRole.Implicit, Class = X86OperandClass.Gpr, Size = 1, Fixed = X86Register.Rcx };
            case "xmm0": return new X86OperandSpec { Role = X86OperandRole.Implicit, Class = X86OperandClass.Xmm, Size = 16, Fixed = X86Register.Xmm0 };
            case "m": return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Memory, AllowsMemory = true };
            case "k": return new X86OperandSpec { Role = X86OperandRole.Reg, Class = X86OperandClass.Mask, AllowsRegister = true, Size = 8 };
            case "kr": return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Mask, AllowsRegister = true, Size = 8 };
            case "vk": return new X86OperandSpec { Role = X86OperandRole.Vvvv, Class = X86OperandClass.Mask, AllowsRegister = true, Size = 8 };
        }

        if (token.StartsWith("vm", StringComparison.Ordinal))
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Memory, AllowsMemory = true, IndexClass = VectorClass(token[^1]) };
        if (token.StartsWith("km", StringComparison.Ordinal))
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Mask, AllowsRegister = true, AllowsMemory = true, Size = 8, MemorySize = byte.Parse(token[2..], CultureInfo.InvariantCulture) };
        if (token.StartsWith("rm", StringComparison.Ordinal))
        {
            byte size = GprSize(token[2..]);
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Gpr, AllowsRegister = true, AllowsMemory = true, Size = size, MemorySize = memoryOverride != 0 ? (byte)(memoryOverride / 8) : size };
        }
        if (token.StartsWith("rr", StringComparison.Ordinal))
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Gpr, AllowsRegister = true, Size = GprSize(token[2..]) };
        if (token.StartsWith("vr", StringComparison.Ordinal))
            return new X86OperandSpec { Role = X86OperandRole.Vvvv, Class = X86OperandClass.Gpr, AllowsRegister = true, Size = GprSize(token[2..]) };
        if (token[0] == 'o' && token.Length > 1)
            return new X86OperandSpec { Role = X86OperandRole.OpcodeRegister, Class = X86OperandClass.Gpr, AllowsRegister = true, Size = GprSize(token[1..]) };
        if (token[0] == 'r' && token.Length > 1)
            return new X86OperandSpec { Role = X86OperandRole.Reg, Class = X86OperandClass.Gpr, AllowsRegister = true, Size = GprSize(token[1..]) };
        if (token[0] == 'm' && token.Length > 1)
        {
            int bits = int.Parse(token[1..], CultureInfo.InvariantCulture);
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = X86OperandClass.Memory, AllowsMemory = true, MemorySize = (byte)(bits / 8) };
        }
        if (token.StartsWith("is4", StringComparison.Ordinal))
        {
            var c = VectorClass(token[3]);
            return new X86OperandSpec { Role = X86OperandRole.Is4, Class = c, AllowsRegister = true, Size = VectorSize(c) };
        }
        if (token[0] == 'v' && token.Length == 2)
        {
            var c = VectorClass(token[1]);
            return new X86OperandSpec { Role = X86OperandRole.Vvvv, Class = c, AllowsRegister = true, Size = VectorSize(c) };
        }
        if (token.Length >= 2 && token[0] is 'x' or 'y' or 'z' && token[1] == 'r')
        {
            var c = VectorClass(token[0]);
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = c, AllowsRegister = true, Size = VectorSize(c) };
        }
        if (token.Length >= 2 && token[0] is 'x' or 'y' or 'z' && token[1] == 'm')
        {
            var c = VectorClass(token[0]);
            byte memory = token.Length > 2 ? (byte)(int.Parse(token[2..], CultureInfo.InvariantCulture) / 8) : VectorSize(c);
            return new X86OperandSpec { Role = X86OperandRole.Rm, Class = c, AllowsRegister = true, AllowsMemory = true, Size = VectorSize(c), MemorySize = memory, Broadcast = broadcast };
        }
        if (token.Length == 1 && token[0] is 'x' or 'y' or 'z')
        {
            var c = VectorClass(token[0]);
            return new X86OperandSpec { Role = X86OperandRole.Reg, Class = c, AllowsRegister = true, Size = VectorSize(c) };
        }
        throw new InvalidOperationException($"Bad operand token '{token}'");
    }
}
