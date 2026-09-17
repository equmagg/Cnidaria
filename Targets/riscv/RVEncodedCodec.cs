using System;
using System.Collections.Generic;

namespace Cnidaria.RiscV;

/// <summary>Encodes and decodes the instructions whose whole encoding lives in their table entry</summary>
internal static class RVEncodedCodec
{
    private static readonly List<RVEncodedInstructions.Entry>[] WideBuckets = CreateBuckets(false);
    private static readonly List<RVEncodedInstructions.Entry>[] CompressedBuckets = CreateBuckets(true);

    private static List<RVEncodedInstructions.Entry>[] CreateBuckets(bool compressed)
    {
        var width = compressed ? 4 : 128;
        var buckets = new List<RVEncodedInstructions.Entry>[width];
        for (var i = 0; i < buckets.Length; i++)
            buckets[i] = new List<RVEncodedInstructions.Entry>();
        foreach (var entry in RVEncodedInstructions.All)
        {
            if (entry.Form.IsCompressed != compressed)
                continue;
            buckets[entry.Form.Match & (uint)(width - 1)].Add(entry);
        }

        // The most constrained encoding wins: a segment load fixes bits its plain form leaves free
        foreach (var bucket in buckets)
            bucket.Sort(static (left, right) => System.Numerics.BitOperations.PopCount(right.Form.Mask).CompareTo(System.Numerics.BitOperations.PopCount(left.Form.Mask)));
        return buckets;
    }

    public static uint Encode(RVInstruction instruction, RVEncodedForm form)
    {
        uint word = form.Match;
        foreach (var slot in form.Slots)
            word |= Field(instruction, slot, form);
        return word;
    }

    public static bool TryDecode(uint word, bool compressed, RVTarget target, out RVInstruction instruction)
    {
        var buckets = compressed ? CompressedBuckets : WideBuckets;
        foreach (var entry in buckets[word & (uint)(buckets.Length - 1)])
        {
            if ((word & entry.Form.Mask) != entry.Form.Match)
                continue;
            // c.ld and c.flw share an encoding: only the register width tells them apart
            if (entry.Requires64Bit && !target.Is64Bit)
                continue;
            if (entry.Requires32Bit && target.Is64Bit)
                continue;
            if (!target.Has(entry.RequiredIsa))
                continue;
            instruction = Rebuild(word, entry);
            return true;
        }

        instruction = default;
        return false;
    }

    private static RVInstruction Rebuild(uint word, RVEncodedInstructions.Entry entry)
    {
        var rd = RVRegister.Invalid;
        var rs1 = RVRegister.Invalid;
        var rs2 = RVRegister.Invalid;
        var rs3 = RVRegister.Invalid;
        var immediate = 0;
        var flags = RVInstructionFlags.None;

        foreach (var slot in entry.Form.Slots)
        {
            switch (slot.Kind)
            {
                case RVOperandKind.IntegerRd:
                    rd = (RVRegister)Extract(word, 7, 5);
                    break;
                case RVOperandKind.IntegerRs1:
                    rs1 = (RVRegister)Extract(word, 15, 5);
                    break;
                case RVOperandKind.IntegerRs2:
                    rs2 = (RVRegister)Extract(word, 20, 5);
                    break;
                case RVOperandKind.FloatRd:
                    rd = RVRegisters.Float(Extract(word, 7, 5));
                    break;
                case RVOperandKind.FloatRs1:
                    rs1 = RVRegisters.Float(Extract(word, 15, 5));
                    break;
                case RVOperandKind.FloatRs2:
                    rs2 = RVRegisters.Float(Extract(word, 20, 5));
                    break;
                case RVOperandKind.FloatRs3:
                    rs3 = RVRegisters.Float(Extract(word, 27, 5));
                    break;
                case RVOperandKind.VectorVd:
                    rd = RVRegisters.Vector(Extract(word, 7, 5));
                    break;
                case RVOperandKind.VectorVs1:
                    rs1 = RVRegisters.Vector(Extract(word, 15, 5));
                    break;
                case RVOperandKind.VectorVs2:
                    rs2 = RVRegisters.Vector(Extract(word, 20, 5));
                    break;
                case RVOperandKind.VectorVs3:
                    rd = RVRegisters.Vector(Extract(word, 7, 5));
                    break;
                case RVOperandKind.CompressedRd:
                    rd = (RVRegister)(8 + Extract(word, 2, 3));
                    break;
                case RVOperandKind.CompressedRs1:
                    rs1 = (RVRegister)(8 + Extract(word, 7, 3));
                    break;
                case RVOperandKind.CompressedRs2:
                    rs2 = (RVRegister)(8 + Extract(word, 2, 3));
                    break;
                case RVOperandKind.CompressedRdRs1:
                    rd = (RVRegister)(8 + Extract(word, 7, 3));
                    rs1 = rd;
                    break;
                case RVOperandKind.CompressedFloatRd:
                    rd = RVRegisters.Float(8 + Extract(word, 2, 3));
                    break;
                case RVOperandKind.CompressedFloatRs2:
                    rs2 = RVRegisters.Float(8 + Extract(word, 2, 3));
                    break;
                case RVOperandKind.WideRs2:
                    rs2 = (RVRegister)Extract(word, 2, 5);
                    break;
                case RVOperandKind.WideFloatRs2:
                    rs2 = RVRegisters.Float(Extract(word, 2, 5));
                    break;
                case RVOperandKind.IntegerRs1Wide:
                    rs1 = (RVRegister)Extract(word, 7, 5);
                    break;
                case RVOperandKind.Zimm5:
                    rs1 = (RVRegister)Extract(word, 15, 5);
                    break;
                case RVOperandKind.FloatConstant:
                    immediate = (int)Extract(word, 15, 5);
                    break;
                case RVOperandKind.AtomicOrdering:
                    if ((word & (1U << 26)) != 0)
                        flags |= RVInstructionFlags.AtomicAcquire;
                    if ((word & (1U << 25)) != 0)
                        flags |= RVInstructionFlags.AtomicRelease;
                    break;
                case RVOperandKind.RoundingMode:
                    immediate = (int)Extract(word, 12, 3);
                    break;
                case RVOperandKind.Immediate:
                    immediate = slot.Immediate!.Decode(word);
                    break;
                case RVOperandKind.MaskBit:
                    if ((word & (1U << 25)) != 0)
                        flags |= RVInstructionFlags.VectorUnmasked;
                    break;
            }
        }

        return new RVInstruction(entry.Opcode, rd, rs1, rs2, immediate, null, RVRelocationKind.None, flags, rs3);
    }

    private static uint Field(RVInstruction instruction, RVOperandSlot slot, RVEncodedForm form)
    {
        switch (slot.Kind)
        {
            case RVOperandKind.IntegerRd:
                return Place(RVRegisters.IntegerIndex(instruction.Rd), 7, 5, form);
            case RVOperandKind.IntegerRs1:
                return Place(RVRegisters.IntegerIndex(instruction.Rs1), 15, 5, form);
            case RVOperandKind.IntegerRs2:
                return Place(RVRegisters.IntegerIndex(instruction.Rs2), 20, 5, form);
            case RVOperandKind.FloatRd:
                return Place(RVRegisters.FloatIndex(instruction.Rd), 7, 5, form);
            case RVOperandKind.FloatRs1:
                return Place(RVRegisters.FloatIndex(instruction.Rs1), 15, 5, form);
            case RVOperandKind.FloatRs2:
                return Place(RVRegisters.FloatIndex(instruction.Rs2), 20, 5, form);
            case RVOperandKind.FloatRs3:
                return Place(RVRegisters.FloatIndex(instruction.Rs3), 27, 5, form);
            case RVOperandKind.VectorVd:
                return Place(RVRegisters.VectorIndex(instruction.Rd), 7, 5, form);
            case RVOperandKind.VectorVs1:
                return Place(RVRegisters.VectorIndex(instruction.Rs1), 15, 5, form);
            case RVOperandKind.VectorVs2:
                return Place(RVRegisters.VectorIndex(instruction.Rs2), 20, 5, form);
            case RVOperandKind.VectorVs3:
                return Place(RVRegisters.VectorIndex(instruction.Rd), 7, 5, form);
            case RVOperandKind.CompressedRd:
                return Place(CompressedIndex(instruction.Rd), 2, 3, form);
            case RVOperandKind.CompressedRs1:
                return Place(CompressedIndex(instruction.Rs1), 7, 3, form);
            case RVOperandKind.CompressedRs2:
                return Place(CompressedIndex(instruction.Rs2), 2, 3, form);
            case RVOperandKind.CompressedRdRs1:
                return Place(CompressedIndex(instruction.Rd), 7, 3, form);
            case RVOperandKind.CompressedFloatRd:
                return Place(CompressedFloatIndex(instruction.Rd), 2, 3, form);
            case RVOperandKind.CompressedFloatRs2:
                return Place(CompressedFloatIndex(instruction.Rs2), 2, 3, form);
            case RVOperandKind.WideRs2:
                return Place(RVRegisters.IntegerIndex(instruction.Rs2), 2, 5, form);
            case RVOperandKind.WideFloatRs2:
                return Place(RVRegisters.FloatIndex(instruction.Rs2), 2, 5, form);
            case RVOperandKind.IntegerRs1Wide:
                return Place(RVRegisters.IntegerIndex(instruction.Rs1), 7, 5, form);
            case RVOperandKind.Zimm5:
                return Place(instruction.Rs1 == RVRegister.Invalid ? 0 : (int)instruction.Rs1, 15, 5, form);
            case RVOperandKind.FloatConstant:
                return Place(instruction.Immediate, 15, 5, form);
            case RVOperandKind.AtomicOrdering:
                return (instruction.AtomicAcquire ? 1U << 26 : 0U) | (instruction.AtomicRelease ? 1U << 25 : 0U);
            case RVOperandKind.RoundingMode:
                if ((instruction.Immediate & ~7) != 0)
                    throw new ArgumentOutOfRangeException(nameof(instruction), instruction.Immediate, "Invalid rounding mode");
                return (uint)instruction.Immediate << 12;
            case RVOperandKind.Immediate:
                return CheckedField(slot.Immediate!.Encode(instruction.Immediate), form);
            case RVOperandKind.MaskBit:
                return instruction.VectorUnmasked ? 1U << 25 : 0U;
            default:
                throw new NotSupportedException("Unsupported RISC-V operand: " + slot.Kind);
        }
    }

    private static int CompressedFloatIndex(RVRegister register)
    {
        var index = RVRegisters.FloatIndex(register);
        if (index < 8 || index > 15)
            throw new ArgumentOutOfRangeException(nameof(register), register, "Compressed operand must name f8 through f15");
        return index - 8;
    }

    private static int CompressedIndex(RVRegister register)
    {
        var index = RVRegisters.IntegerIndex(register);
        if (index < 8 || index > 15)
            throw new ArgumentOutOfRangeException(nameof(register), register, "Compressed operand must name x8 through x15");
        return index - 8;
    }

    private static uint Place(int value, int low, int width, RVEncodedForm form)
    {
        if (value < 0 || value >= 1 << width)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Register index does not fit its field");
        return CheckedField((uint)value << low, form);
    }

    private static uint CheckedField(uint bits, RVEncodedForm form)
    {
        if ((bits & form.Mask) != 0)
            throw new InvalidOperationException("RISC-V operand overlaps a fixed field of its encoding");
        return bits;
    }

    private static uint Extract(uint word, int low, int width)
        => (word >> low) & ((1U << width) - 1);

    private static RVRegister Float(uint index) => RVRegisters.Float(index);
}
