using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Cnidaria.RiscV;

/// <summary>Names the role one operand slot of a table-driven instruction plays</summary>
internal enum RVOperandKind : byte
{
    IntegerRd,
    IntegerRs1,
    IntegerRs2,
    FloatRd,
    FloatRs1,
    FloatRs2,
    FloatRs3,
    VectorVd,
    VectorVs1,
    VectorVs2,
    VectorVs3,
    CompressedRd,
    CompressedRs1,
    CompressedRs2,
    CompressedRdRs1,
    CompressedFloatRd,
    CompressedFloatRs2,
    WideRs2,
    WideFloatRs2,
    IntegerRs1Wide,
    Zimm5,
    AtomicOrdering,
    Immediate,
    RoundingMode,
    FloatConstant,
    MaskBit,
}

/// <summary>Moves one run of immediate bits into the instruction word</summary>
internal readonly struct RVImmediatePiece
{
    public RVImmediatePiece(byte instructionLow, byte valueLow, byte width)
    {
        InstructionLow = instructionLow;
        ValueLow = valueLow;
        Width = width;
    }

    public byte InstructionLow { get; }
    public byte ValueLow { get; }
    public byte Width { get; }
}

/// <summary>Describes how one immediate scatters across an instruction word</summary>
internal sealed class RVImmediateShape
{
    public RVImmediateShape(string name, bool signed, int bits, params RVImmediatePiece[] pieces)
    {
        Name = name;
        Signed = signed;
        Bits = bits;
        Pieces = pieces.ToImmutableArray();
    }

    public string Name { get; }
    public bool Signed { get; }
    public int Bits { get; }

    /// <summary>lui and auipc take their field either as a signed value or as raw upper bits</summary>
    public bool AllowsUnsignedAlias { get; init; }
    public ImmutableArray<RVImmediatePiece> Pieces { get; }

    public uint Encode(int value)
    {
        var aliased = false;
        if (Signed)
        {
            var limit = 1 << (Bits - 1);
            if (value < -limit || value >= limit)
            {
                if (!AllowsUnsignedAlias || value < 0 || value >= 1 << Bits)
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"Immediate does not fit the {Name} field");
                aliased = true;
            }
        }
        else if (value < 0 || (Bits < 32 && value >= 1 << Bits))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Immediate does not fit the {Name} field");
        }

        var bits = unchecked((uint)value);
        uint word = 0;
        foreach (var piece in Pieces)
        {
            var mask = piece.Width == 32 ? uint.MaxValue : (1U << piece.Width) - 1;
            word |= ((bits >> piece.ValueLow) & mask) << piece.InstructionLow;
        }

        if (!aliased && Decode(word) != value)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Immediate is not representable in the {Name} field");
        return word;
    }

    public int Decode(uint word)
    {
        uint bits = 0;
        foreach (var piece in Pieces)
        {
            var mask = piece.Width == 32 ? uint.MaxValue : (1U << piece.Width) - 1;
            bits |= ((word >> piece.InstructionLow) & mask) << piece.ValueLow;
        }

        if (!Signed || Bits >= 32)
            return unchecked((int)bits);
        var sign = 1U << (Bits - 1);
        return unchecked((int)((bits ^ sign) - sign));
    }

    public uint Mask()
    {
        uint mask = 0;
        foreach (var piece in Pieces)
            mask |= ((piece.Width == 32 ? uint.MaxValue : (1U << piece.Width) - 1)) << piece.InstructionLow;
        return mask;
    }
}

internal readonly struct RVOperandSlot
{
    public RVOperandSlot(RVOperandKind kind, RVImmediateShape? immediate = null)
    {
        Kind = kind;
        Immediate = immediate;
    }

    public RVOperandKind Kind { get; }
    public RVImmediateShape? Immediate { get; }
}

/// <summary>One table-driven instruction: its fixed bits and the operands written into the rest</summary>
internal sealed class RVEncodedForm
{
    private RVEncodedForm(string pattern, uint match, uint mask, ImmutableArray<RVOperandSlot> slots, ImmutableArray<string> syntax, bool compressed, bool customSyntax)
    {
        HasCustomSyntax = customSyntax;
        Pattern = pattern;
        Match = match;
        Mask = mask;
        Slots = slots;
        Syntax = syntax;
        IsCompressed = compressed;
    }

    public string Pattern { get; }
    public uint Match { get; }
    public uint Mask { get; }
    public ImmutableArray<RVOperandSlot> Slots { get; }

    /// <summary>The textual operand list, one entry per comma-separated operand</summary>
    public ImmutableArray<string> Syntax { get; }
    public bool IsCompressed { get; }

    /// <summary>Set where the text form is not a plain operand list, so the assembler spells it itself</summary>
    public bool HasCustomSyntax { get; }

    public static RVEncodedForm Create(uint match, uint mask, string pattern, bool compressed)
    {
        var customSyntax = pattern.StartsWith("!", StringComparison.Ordinal);
        if (customSyntax)
            pattern = pattern.Substring(1);
        var slots = ImmutableArray.CreateBuilder<RVOperandSlot>();
        var syntax = ImmutableArray.CreateBuilder<string>();
        foreach (var operand in SplitOperands(pattern))
        {
            syntax.Add(operand);
            foreach (var token in Tokens(operand))
            {
                // A literal operand such as the implicit v0 or sp contributes no encoded field
                if (token.StartsWith("#", StringComparison.Ordinal))
                    continue;
                slots.Add(CreateSlot(token));
            }
        }

        return new RVEncodedForm(pattern, match, mask, slots.ToImmutable(), syntax.ToImmutable(), compressed, customSyntax);
    }

    /// <summary>Splits a pattern into its comma-separated operands, keeping the memory form together</summary>
    private static IEnumerable<string> SplitOperands(string pattern)
    {
        if (pattern.Length == 0)
            yield break;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '(')
                depth++;
            else if (pattern[i] == ')')
                depth--;
            else if (pattern[i] == ',' && depth == 0)
            {
                yield return pattern.Substring(start, i - start);
                start = i + 1;
            }
        }

        yield return pattern.Substring(start);
    }

    private static IEnumerable<string> Tokens(string operand)
    {
        var start = 0;
        for (var i = 0; i < operand.Length; i++)
        {
            if (operand[i] is '(' or ')')
            {
                if (i > start)
                    yield return operand.Substring(start, i - start);
                start = i + 1;
            }
        }

        if (start < operand.Length)
            yield return operand.Substring(start);
    }

    private static RVOperandSlot CreateSlot(string token)
    {
        if (token.StartsWith("i:", StringComparison.Ordinal))
            return new RVOperandSlot(RVOperandKind.Immediate, RVImmediateShapes.Get(token.Substring(2)));

        return token switch
        {
            "rd" => new RVOperandSlot(RVOperandKind.IntegerRd),
            "rs1" => new RVOperandSlot(RVOperandKind.IntegerRs1),
            "rs2" => new RVOperandSlot(RVOperandKind.IntegerRs2),
            "fd" => new RVOperandSlot(RVOperandKind.FloatRd),
            "f1" => new RVOperandSlot(RVOperandKind.FloatRs1),
            "f2" => new RVOperandSlot(RVOperandKind.FloatRs2),
            "f3" => new RVOperandSlot(RVOperandKind.FloatRs3),
            "vd" => new RVOperandSlot(RVOperandKind.VectorVd),
            "v1" => new RVOperandSlot(RVOperandKind.VectorVs1),
            "v2" => new RVOperandSlot(RVOperandKind.VectorVs2),
            "v3" => new RVOperandSlot(RVOperandKind.VectorVs3),
            "cd" => new RVOperandSlot(RVOperandKind.CompressedRd),
            "c1" => new RVOperandSlot(RVOperandKind.CompressedRs1),
            "c2" => new RVOperandSlot(RVOperandKind.CompressedRs2),
            "cds" => new RVOperandSlot(RVOperandKind.CompressedRdRs1),
            "cfd" => new RVOperandSlot(RVOperandKind.CompressedFloatRd),
            "cf2" => new RVOperandSlot(RVOperandKind.CompressedFloatRs2),
            "w2" => new RVOperandSlot(RVOperandKind.WideRs2),
            "cw2" => new RVOperandSlot(RVOperandKind.WideFloatRs2),
            "s1w" => new RVOperandSlot(RVOperandKind.IntegerRs1Wide),
            "z5" => new RVOperandSlot(RVOperandKind.Zimm5),
            "aqrl" => new RVOperandSlot(RVOperandKind.AtomicOrdering),
            "rm" => new RVOperandSlot(RVOperandKind.RoundingMode),
            "fli" => new RVOperandSlot(RVOperandKind.FloatConstant, RVImmediateShapes.Get("zimm5")),
            "vm" => new RVOperandSlot(RVOperandKind.MaskBit),
            _ => throw new ArgumentException("Unknown RISC-V operand token: " + token),
        };
    }
}

/// <summary>The thirty-two constants fli loads, in the order Zfa numbers them</summary>
internal static class RVFloatConstants
{
    private static readonly string[] Names =
    {
        "-1.0", "min", "1.52587890625e-05", "3.0517578125e-05", "0.00390625", "0.0078125",
        "0.0625", "0.125", "0.25", "0.3125", "0.375", "0.4375", "0.5", "0.625", "0.75",
        "0.875", "1.0", "1.25", "1.5", "1.75", "2.0", "2.5", "3.0", "4.0", "8.0", "16.0",
        "128.0", "256.0", "32768.0", "65536.0", "inf", "nan",
    };

    public static string Format(int index)
        => index >= 0 && index < Names.Length ? Names[index] : throw new ArgumentOutOfRangeException(nameof(index), index, "fli constant index must be 0 through 31");

    public static bool TryParse(string text, out int index)
    {
        text = text.Trim();
        for (var i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i], text, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        // A value spelled some other way still names the same constant
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            for (var i = 0; i < Names.Length; i++)
            {
                if (i == 1 || i == 30 || i == 31)
                    continue;
                if (double.TryParse(Names[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var candidate) && candidate == value)
                {
                    index = i;
                    return true;
                }
            }
        }

        index = 0;
        return false;
    }
}

internal static class RVImmediateShapes
{
    private static readonly Dictionary<string, RVImmediateShape> Shapes = Create();

    public static RVImmediateShape Get(string name)
        => Shapes.TryGetValue(name, out var shape) ? shape : throw new ArgumentException("Unknown RISC-V immediate shape: " + name);

    private static Dictionary<string, RVImmediateShape> Create()
    {
        var shapes = new List<RVImmediateShape>
        {
            new RVImmediateShape("imm12", true, 12, new RVImmediatePiece(20, 0, 12)),
            new RVImmediateShape("store12", true, 12, new RVImmediatePiece(7, 0, 5), new RVImmediatePiece(25, 5, 7)),
            // A prefetch offset carries only its upper bits: the low five are the hint selector
            new RVImmediateShape("prefetch12", true, 12, new RVImmediatePiece(25, 5, 7)),
            new RVImmediateShape("simm5", true, 5, new RVImmediatePiece(15, 0, 5)),
            new RVImmediateShape("zimm5", false, 5, new RVImmediatePiece(15, 0, 5)),
            new RVImmediateShape("zimm6", false, 6, new RVImmediatePiece(15, 0, 5), new RVImmediatePiece(26, 5, 1)),
            new RVImmediateShape("zimm10", false, 10, new RVImmediatePiece(20, 0, 10)),
            new RVImmediateShape("zimm11", false, 11, new RVImmediatePiece(20, 0, 11)),
            new RVImmediateShape("cuimm1", false, 2, new RVImmediatePiece(5, 1, 1)),
            new RVImmediateShape("cuimm2", false, 2, new RVImmediatePiece(6, 0, 1), new RVImmediatePiece(5, 1, 1)),
            new RVImmediateShape("cuimm9sp", false, 9, new RVImmediatePiece(2, 6, 3), new RVImmediatePiece(5, 3, 2), new RVImmediatePiece(12, 5, 1)),
            new RVImmediateShape("cuimm9sps", false, 9, new RVImmediatePiece(7, 6, 3), new RVImmediatePiece(10, 3, 3)),

            new RVImmediateShape("bimm12", true, 13, new RVImmediatePiece(8, 1, 4), new RVImmediatePiece(25, 5, 6), new RVImmediatePiece(7, 11, 1), new RVImmediatePiece(31, 12, 1)),
            new RVImmediateShape("jimm20", true, 21, new RVImmediatePiece(21, 1, 10), new RVImmediatePiece(20, 11, 1), new RVImmediatePiece(12, 12, 8), new RVImmediatePiece(31, 20, 1)),
            new RVImmediateShape("imm20", true, 20, new RVImmediatePiece(12, 0, 20)) { AllowsUnsignedAlias = true },
            new RVImmediateShape("shamtd", false, 6, new RVImmediatePiece(20, 0, 6)),
            new RVImmediateShape("shamtw", false, 5, new RVImmediatePiece(20, 0, 5)),
            new RVImmediateShape("csr", false, 12, new RVImmediatePiece(20, 0, 12)),
            new RVImmediateShape("zimm5rs1", false, 5, new RVImmediatePiece(15, 0, 5)),

            new RVImmediateShape("cuimm7", false, 7, new RVImmediatePiece(10, 3, 3), new RVImmediatePiece(6, 2, 1), new RVImmediatePiece(5, 6, 1)),
            new RVImmediateShape("cuimm8", false, 8, new RVImmediatePiece(10, 3, 3), new RVImmediatePiece(5, 6, 2)),
            new RVImmediateShape("cnzuimm10", false, 10, new RVImmediatePiece(11, 4, 2), new RVImmediatePiece(7, 6, 4), new RVImmediatePiece(6, 2, 1), new RVImmediatePiece(5, 3, 1)),
            new RVImmediateShape("cimm6", true, 6, new RVImmediatePiece(2, 0, 5), new RVImmediatePiece(12, 5, 1)),
            new RVImmediateShape("cuimm6", false, 6, new RVImmediatePiece(2, 0, 5), new RVImmediatePiece(12, 5, 1)),
            new RVImmediateShape("cnzimm10", true, 10, new RVImmediatePiece(6, 4, 1), new RVImmediatePiece(5, 6, 1), new RVImmediatePiece(3, 7, 2), new RVImmediatePiece(2, 5, 1), new RVImmediatePiece(12, 9, 1)),
            new RVImmediateShape("cnzimm18", true, 18, new RVImmediatePiece(2, 12, 5), new RVImmediatePiece(12, 17, 1)),
            new RVImmediateShape("cbimm9", true, 9, new RVImmediatePiece(3, 1, 2), new RVImmediatePiece(10, 3, 2), new RVImmediatePiece(2, 5, 1), new RVImmediatePiece(5, 6, 2), new RVImmediatePiece(12, 8, 1)),
            new RVImmediateShape("cimm12", true, 12, new RVImmediatePiece(3, 1, 3), new RVImmediatePiece(11, 4, 1), new RVImmediatePiece(2, 5, 1), new RVImmediatePiece(7, 6, 1), new RVImmediatePiece(6, 7, 1), new RVImmediatePiece(9, 8, 2), new RVImmediatePiece(8, 10, 1), new RVImmediatePiece(12, 11, 1)),
            new RVImmediateShape("cuimm8sp", false, 8, new RVImmediatePiece(4, 2, 3), new RVImmediatePiece(12, 5, 1), new RVImmediatePiece(2, 6, 2)),
            new RVImmediateShape("cuimm8sps", false, 8, new RVImmediatePiece(9, 2, 4), new RVImmediatePiece(7, 6, 2)),
        };

        var map = new Dictionary<string, RVImmediateShape>(StringComparer.Ordinal);
        foreach (var shape in shapes)
            map.Add(shape.Name, shape);
        return map;
    }

    public static string Format(int value)
        => value.ToString(CultureInfo.InvariantCulture);
}
