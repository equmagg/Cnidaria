using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Arm;

internal static class ArmCodeEncoder
{
    public static byte[] Encode(ArmProgram obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));
        return obj.LinkFlat(imageBase, externalSymbols).Bytes.ToArray();
    }

    public static byte[] Encode(IEnumerable<ArmInstruction> instructions, ArmTarget target, IReadOnlyDictionary<string, int>? labels = null)
    {
        if (instructions is null)
            throw new ArgumentNullException(nameof(instructions));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        var array = instructions.ToImmutableArray();
        var symbols = new Dictionary<string, ulong>(StringComparer.Ordinal);
        if (labels is not null)
        {
            foreach (var pair in labels)
                symbols[pair.Key] = checked((ulong)pair.Value);
        }

        var result = new byte[checked(array.Length * 4)];
        for (var i = 0; i < array.Length; i++)
        {
            var pc = checked((ulong)(i * 4));
            var word = EncodeWord(array[i], target, pc, symbols);
            WriteWord(result, i * 4, word, target.Endianness);
        }
        return result;
    }

    public static byte[] Encode(ArmInstruction instruction, ArmTarget target)
        => Encode(instruction, target, 0, null);

    public static byte[] Encode(ArmInstruction instruction, ArmTarget target, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        var result = new byte[4];
        WriteWord(result, 0, EncodeWord(instruction, target, pc, symbols), target.Endianness);
        return result;
    }

    public static uint EncodeWord(ArmInstruction instruction, ArmTarget target, ulong pc = 0, IReadOnlyDictionary<string, ulong>? symbols = null)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (instruction.Opcode == ArmInstrKind.Raw)
            return instruction.RawWord;
        foreach (ArmInstructionForm form in ArmInstructionForms.ForOpcode(instruction.Opcode))
        {
            if (form.XLen != target.XLen || !TryEncodeForm(form, instruction, pc, symbols, out uint word))
                continue;
            if (!target.Has(form.Isa))
                throw new NotSupportedException($"{instruction.Opcode} requires {form.Isa}");
            return word;
        }
        throw new ArgumentException($"No {target.XLen}-bit ARM encoding matches {instruction.Opcode} and its operands", nameof(instruction));
    }

    public static void WriteWord(byte[] destination, int offset, uint word, TargetEndianness endianness)
    {
        if (destination is null)
            throw new ArgumentNullException(nameof(destination));
        if ((uint)offset > (uint)(destination.Length - 4))
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (endianness == TargetEndianness.Little)
        {
            destination[offset] = (byte)word;
            destination[offset + 1] = (byte)(word >> 8);
            destination[offset + 2] = (byte)(word >> 16);
            destination[offset + 3] = (byte)(word >> 24);
        }
        else
        {
            destination[offset] = (byte)(word >> 24);
            destination[offset + 1] = (byte)(word >> 16);
            destination[offset + 2] = (byte)(word >> 8);
            destination[offset + 3] = (byte)word;
        }
    }

    private static bool TryEncodeForm(ArmInstructionForm form, ArmInstruction instruction, ulong pc,
        IReadOnlyDictionary<string, ulong>? symbols, out uint word)
    {
        word = form.Word;
        if (form.ConditionShift < 0 && instruction.Condition != ArmCondition.Al ||
            form.ConditionalOnly && instruction.Condition == ArmCondition.Al)
            return false;
        if ((uint)instruction.Condition > 15)
            return false;
        if (form.ConditionShift >= 0)
            word |= (uint)instruction.Condition << form.ConditionShift;
        if (instruction.SetFlags)
            word |= form.FlagMask;
        for (int i = 0; i < 5; i++)
        {
            ArmOperand operand = instruction.GetOperand(i);
            if (i >= form.Operands.Length)
            {
                if (operand.Kind != ArmOperandKind.None)
                    return false;
                continue;
            }
            ArmOperandSpec spec = form.Operands[i];
            if (operand.Kind == ArmOperandKind.None && spec.Optional)
                operand = spec.Default;
            if (spec.Tie >= 0 && operand.Register != instruction.GetOperand(spec.Tie).Register)
                return false;
            if (!TryEncodeOperand(spec, operand, form.XLen, pc, symbols, out uint bits))
                return false;
            word |= bits;
        }
        return true;
    }

    private static bool TryEncodeOperand(ArmOperandSpec spec, ArmOperand operand, int xlen, ulong pc,
        IReadOnlyDictionary<string, ulong>? symbols, out uint bits)
    {
        bits = 0;
        long value;
        uint encoded;
        switch (spec.Encoding)
        {
            case ArmOperandEncoding.Register:
            case ArmOperandEncoding.VectorList:
            case ArmOperandEncoding.VectorElement:
                if (spec.Encoding == ArmOperandEncoding.VectorList)
                {
                    if (operand.Kind != ArmOperandKind.VectorList && !(spec.Count == 1 && operand.Kind == ArmOperandKind.Register) ||
                        operand.Kind == ArmOperandKind.VectorList && operand.RegisterCount != spec.Count ||
                        (spec.IndexMask != 0) != (operand.Kind == ArmOperandKind.VectorList && operand.ElementIndex >= 0))
                        return false;
                }
                else if (operand.Kind != (spec.Encoding == ArmOperandEncoding.VectorElement ? ArmOperandKind.VectorElement : ArmOperandKind.Register))
                    return false;
                if (!TryRegister(spec, operand, xlen, out encoded))
                    return false;
                bits = Deposit(encoded, spec.Mask);
                if (spec.Encoding == ArmOperandEncoding.VectorElement)
                {
                    if (!TryImmediate(spec, operand.ElementIndex, out uint index))
                        return false;
                    // AdvSIMD by-element indices store their high bit H below L and M
                    int indexBits = System.Numerics.BitOperations.PopCount(spec.ExtraMask);
                    if (spec.IndexRotate && indexBits > 1)
                        index = ((index << 1) | (index >> (indexBits - 1))) & ((1u << indexBits) - 1);
                    bits |= Deposit(index, spec.ExtraMask);
                }
                else
                    bits |= Deposit(encoded, spec.ExtraMask) | Deposit(encoded, spec.BaseMask);
                if (spec.Encoding == ArmOperandEncoding.VectorList && spec.IndexMask != 0)
                {
                    if (!TryImmediate(spec, operand.ElementIndex, out uint lane))
                        return false;
                    bits |= Deposit(lane, spec.IndexMask);
                }
                return true;

            case ArmOperandEncoding.Immediate:
            case ArmOperandEncoding.Pattern:
            case ArmOperandEncoding.Multiplier:
            case ArmOperandEncoding.PrefetchOperation:
                ArmOperandKind kind = spec.Encoding switch
                {
                    ArmOperandEncoding.Pattern => ArmOperandKind.Pattern,
                    ArmOperandEncoding.Multiplier => ArmOperandKind.Multiplier,
                    ArmOperandEncoding.PrefetchOperation => ArmOperandKind.PrefetchOperation,
                    _ => ArmOperandKind.Immediate,
                };
                if (operand.Kind != kind && !(kind is ArmOperandKind.Pattern or ArmOperandKind.PrefetchOperation && operand.Kind == ArmOperandKind.Immediate))
                    return false;
                if (operand.ShiftAmount != spec.Shift || operand.Shift is not ArmShiftKind.None and not ArmShiftKind.Lsl ||
                    !TryImmediate(spec, operand.Immediate, out encoded))
                    return false;
                bits = Deposit(encoded, spec.Mask) | Deposit(encoded, spec.ExtraMask);
                return true;

            case ArmOperandEncoding.FloatingConstant:
            case ArmOperandEncoding.FloatingImmediate:
                if (operand.Kind != ArmOperandKind.FloatingImmediate && operand.Kind != ArmOperandKind.Immediate)
                    return false;
                double floating = operand.Kind == ArmOperandKind.FloatingImmediate ? operand.FloatingImmediate : operand.Immediate;
                if (spec.Encoding == ArmOperandEncoding.FloatingConstant)
                    return BitConverter.DoubleToInt64Bits(floating) == spec.Minimum;
                if (!TryFloatingImmediate(floating, out encoded))
                    return false;
                bits = Deposit(encoded, spec.Mask);
                return true;

            case ArmOperandEncoding.LogicalImmediate:
                if (operand.Kind != ArmOperandKind.Immediate || operand.ShiftAmount != 0)
                    return false;
                if (!FitsElement(operand.Immediate, spec.Size * 8))
                    return false;
                ulong logical = unchecked((ulong)operand.Immediate);
                if (spec.Invert)
                    logical = ~logical;
                if (!TryEncodeLogicalImmediate(logical, spec.Size * 8, out uint n, out uint immr, out uint imms))
                    return false;
                bits = Deposit((n << 12) | (immr << 6) | imms, spec.Mask);
                return true;

            case ArmOperandEncoding.MoveWideImmediate:
                if (operand.Kind != ArmOperandKind.Immediate || operand.ShiftAmount != 0 ||
                    !TryEncodeMoveWide(unchecked((ulong)operand.Immediate), spec.Size * 8, spec.Invert, out ushort wide, out int shift))
                    return false;
                bits = ((uint)wide << 5) | ((uint)(shift / 16) << 21);
                return true;

            case ArmOperandEncoding.AddImmediate:
                if (operand.Kind != ArmOperandKind.Immediate)
                    return false;
                value = operand.Immediate;
                if (spec.Size is > 0 and < 8)
                {
                    int significantBits = spec.Size * 8 - operand.ShiftAmount;
                    if (significantBits <= 0 || significantBits > 64)
                        return false;
                    if (!FitsElement(value, significantBits))
                        return false;
                    value = (value << (64 - significantBits)) >> (64 - significantBits);
                }
                if (operand.ShiftAmount != 0)
                {
                    if (operand.Shift != ArmShiftKind.Lsl || operand.ShiftAmount != spec.Shift || value < spec.Minimum || value > spec.Maximum)
                        return false;
                    bits = spec.ExtraMask;
                }
                else if (value < spec.Minimum || value > spec.Maximum)
                {
                    long unit = 1L << spec.Shift;
                    if (spec.Shift == 0 || value % unit != 0 || value / unit < spec.Minimum || value / unit > spec.Maximum)
                        return false;
                    value /= unit;
                    bits = spec.ExtraMask;
                }
                bits |= Deposit(unchecked((uint)value), spec.Mask);
                return true;

            case ArmOperandEncoding.ShiftLeftImmediate:
            case ArmOperandEncoding.ShiftRightImmediate:
                if (operand.Kind != ArmOperandKind.Immediate || operand.ShiftAmount != 0 || !TryImmediate(spec, operand.Immediate, out _))
                    return false;
                int amount = (int)operand.Immediate;
                int width = spec.Size * 8;
                if (spec.Encoding == ArmOperandEncoding.ShiftLeftImmediate)
                    bits = Deposit((uint)((width - amount) & (width - 1)), spec.Mask) | Deposit((uint)(width - 1 - amount), spec.ExtraMask);
                else
                    bits = Deposit((uint)amount, spec.Mask);
                return true;

            case ArmOperandEncoding.ShiftedRegister:
                if (operand.Kind is not ArmOperandKind.Register and not ArmOperandKind.ShiftedRegister ||
                    !TryRegister(spec, operand, xlen, out encoded) || operand.ShiftAmount < 0 || operand.ShiftAmount >= spec.Size * 8 ||
                    operand.Shift is ArmShiftKind.Rrx || operand.Shift == ArmShiftKind.Ror && spec.Maximum == 2)
                    return false;
                bits = Deposit(encoded, spec.Mask) | ((uint)operand.ShiftAmount << 10) | (EncodeA64ShiftKind(operand.Shift) << 22);
                return true;

            case ArmOperandEncoding.ExtendedRegister:
                if (operand.Kind != ArmOperandKind.ExtendedRegister || !ArmRegisters.IsAArch64General(operand.Register) && operand.Register != ArmRegister.Xzr ||
                    operand.ShiftAmount < 0 || operand.ShiftAmount > 4 || operand.Extend == ArmExtendKind.None)
                    return false;
                int sourceSize = operand.Extend is ArmExtendKind.Uxtx or ArmExtendKind.Sxtx ? 8 : 4;
                if (operand.Size != 0 && operand.Size != sourceSize || spec.Size == 4 && sourceSize == 8)
                    return false;
                bits = ((uint)ArmRegisters.Index(operand.Register) << 16) | (EncodeA64Extend(operand.Extend) << 13) | ((uint)operand.ShiftAmount << 10);
                return true;

            case ArmOperandEncoding.A32Shifter:
                if (operand.Kind == ArmOperandKind.Immediate)
                {
                    if (operand.Immediate < int.MinValue || operand.Immediate > uint.MaxValue || !TryEncodeA32Immediate(unchecked((uint)operand.Immediate), out encoded))
                        return false;
                    bits = encoded | (1u << 25);
                    return true;
                }
                if (operand.Kind is not ArmOperandKind.Register and not ArmOperandKind.ShiftedRegister || !TryRegister(spec, operand, xlen, out encoded))
                    return false;
                if (!TryA32Shift(operand.Shift, operand.ShiftAmount, out uint shifted))
                    return false;
                bits = encoded | shifted;
                return true;

            case ArmOperandEncoding.Memory:
            case ArmOperandEncoding.RegisterMemory:
            case ArmOperandEncoding.A32Memory:
            case ArmOperandEncoding.A32HalfMemory:
            case ArmOperandEncoding.A32VfpMemory:
                return TryEncodeMemory(spec, operand, xlen, out bits);

            case ArmOperandEncoding.Relative:
            case ArmOperandEncoding.Address:
            case ArmOperandEncoding.PageAddress:
                if (operand.Kind is not ArmOperandKind.Immediate and not ArmOperandKind.Symbol &&
                    !(operand.Kind == ArmOperandKind.Memory && operand.AddressingMode == ArmAddressingMode.Literal))
                    return false;
                value = ResolveDisplacement(operand, pc, symbols);
                if (spec.Encoding == ArmOperandEncoding.PageAddress && operand.HasSymbol)
                    value = checked((long)(ResolveAddress(operand, symbols) & ~4095UL) - (long)(pc & ~4095UL));
                if (!TryImmediate(spec, value, out encoded))
                    return false;
                bits = spec.Encoding == ArmOperandEncoding.Relative ? Deposit(encoded, spec.Mask)
                    : ((encoded & 3) << 29) | ((encoded & 0x1FFFFC) << 3);
                return true;

            case ArmOperandEncoding.RegisterList:
                if (operand.Kind != ArmOperandKind.RegisterList || operand.RegisterMask == 0 || (operand.RegisterMask & 0xFFFF0000u) != 0)
                    return false;
                bits = operand.RegisterMask;
                return true;

            case ArmOperandEncoding.SystemRegister:
                if (operand.Kind != ArmOperandKind.SystemRegister)
                    return false;
                if (xlen == 32)
                {
                    if (operand.SystemRegister is not ArmSystemRegister.Cpsr and not ArmSystemRegister.Spsr)
                        return false;
                    bits = operand.SystemRegister == ArmSystemRegister.Spsr ? 1u << 22 : 0;
                }
                else
                    bits = EncodeSystemRegister(operand.SystemRegister);
                return true;

            default:
                throw new InvalidOperationException("Unknown ARM operand encoding");
        }
    }

    private static bool TryRegister(ArmOperandSpec spec, ArmOperand operand, int xlen, out uint value)
    {
        value = 0;
        bool valid = spec.Class switch
        {
            ArmRegisterClass.General32 => ArmRegisters.IsAArch32General(operand.Register) || operand.Register == ArmRegister.Sp,
            ArmRegisterClass.General64 => ArmRegisters.IsAArch64General(operand.Register) || (spec.AllowSp ? operand.Register == ArmRegister.Sp : operand.Register == ArmRegister.Xzr),
            ArmRegisterClass.Vector => ArmRegisters.IsVector(operand.Register),
            ArmRegisterClass.ScalableVector => ArmRegisters.IsScalableVector(operand.Register),
            ArmRegisterClass.Predicate => ArmRegisters.IsPredicate(operand.Register),
            _ => false,
        };
        if (!valid || operand.Predication != spec.Predication || operand.VectorSize != spec.VectorSize)
            return false;
        int size = operand.Size;
        if (size == 0 && spec.Class is ArmRegisterClass.General32 or ArmRegisterClass.General64 or ArmRegisterClass.Vector)
            size = xlen / 8;
        if (size != spec.Size)
            return false;
        value = operand.Register == ArmRegister.Sp && xlen == 32 ? 13u : (uint)ArmRegisters.Index(operand.Register);
        if (value >= spec.RegisterLimit)
            return false;
        if (xlen == 32 && spec.Class == ArmRegisterClass.Vector && spec.Size == 4)
            value = (value >> 1) | ((value & 1) << 4);
        if (spec.RegisterRotation != 0)
            value = ((value << spec.RegisterRotation) | (value >> (5 - spec.RegisterRotation))) & 31;
        return true;
    }

    private static bool TryEncodeMemory(ArmOperandSpec spec, ArmOperand operand, int xlen, out uint bits)
    {
        bits = 0;
        if (operand.Kind != ArmOperandKind.Memory || operand.AddressingMode != spec.Mode)
            return false;
        bool a32 = xlen == 32;
        if (spec.BaseVectorSize != 0
            ? !ArmRegisters.IsScalableVector(operand.BaseRegister) || operand.Size != spec.BaseVectorSize
            : a32 ? !ArmRegisters.IsAArch32General(operand.BaseRegister) && operand.BaseRegister != ArmRegister.Sp
            : !ArmRegisters.IsAArch64General(operand.BaseRegister) && operand.BaseRegister != ArmRegister.Sp)
            return false;
        uint rn = operand.BaseRegister == ArmRegister.Sp && a32 ? 13u : (uint)ArmRegisters.Index(operand.BaseRegister);
        bits = Deposit(rn, spec.BaseMask);
        if (spec.Encoding == ArmOperandEncoding.RegisterMemory)
        {
            bool validIndex = spec.Class == ArmRegisterClass.ScalableVector
                ? ArmRegisters.IsScalableVector(operand.IndexRegister)
                : ArmRegisters.IsAArch64General(operand.IndexRegister) || operand.IndexRegister == ArmRegister.Xzr;
            if (operand.Immediate != 0 || !validIndex || ArmRegisters.Index(operand.IndexRegister) >= spec.RegisterLimit ||
                operand.Shift is not ArmShiftKind.None and not ArmShiftKind.Lsl ||
                operand.Extend != spec.Extend || operand.ShiftAmount != spec.Shift ||
                operand.IndexSize != 0 && operand.IndexSize != spec.IndexSize)
                return false;
            bits |= Deposit((uint)ArmRegisters.Index(operand.IndexRegister), spec.IndexMask);
            return true;
        }
        if (spec.Encoding == ArmOperandEncoding.Memory)
        {
            if (operand.IndexRegister != ArmRegister.Invalid || operand.ShiftAmount != 0 || operand.Extend != ArmExtendKind.None ||
                !TryImmediate(spec, operand.Immediate, out uint offset))
                return false;
            bits |= Deposit(offset, spec.Mask);
            return true;
        }
        if (operand.Extend != ArmExtendKind.None || operand.Immediate == long.MinValue)
            return false;
        if (operand.Immediate >= 0)
            bits |= 1u << 23;
        if (spec.Encoding == ArmOperandEncoding.A32VfpMemory)
        {
            if (operand.IndexRegister != ArmRegister.Invalid || !TryImmediate(spec, Math.Abs(operand.Immediate), out uint offset))
                return false;
            bits |= offset;
            return true;
        }
        if (operand.IndexRegister != ArmRegister.Invalid)
        {
            if (!ArmRegisters.IsAArch32General(operand.IndexRegister) || operand.Immediate != 0)
                return false;
            bits |= (uint)ArmRegisters.Index(operand.IndexRegister);
            if (spec.Encoding == ArmOperandEncoding.A32HalfMemory)
                return operand.ShiftAmount == 0 && operand.Shift == ArmShiftKind.None;
            if (!TryA32Shift(operand.Shift, operand.ShiftAmount, out uint shifted))
                return false;
            bits |= shifted | (1u << 25);
        }
        else
        {
            if (!TryImmediate(spec, Math.Abs(operand.Immediate), out uint offset))
                return false;
            bits |= Deposit(offset, spec.Mask);
            if (spec.Encoding == ArmOperandEncoding.A32HalfMemory)
                bits |= 1u << 22;
        }
        return true;
    }

    private static bool TryImmediate(ArmOperandSpec spec, long value, out uint encoded)
    {
        encoded = 0;
        if (value < spec.Minimum || value > spec.Maximum || value % spec.Scale != 0)
            return false;
        encoded = unchecked((uint)(value / spec.Scale + spec.Bias));
        return true;
    }

    private static bool FitsElement(long value, int width)
    {
        if (width == 64)
            return true;
        long mask = (1L << width) - 1;
        return value >= -mask && value <= mask;
    }

    private static uint Deposit(uint value, uint mask)
    {
        if (mask == 0)
            return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint field = mask >> shift;
        if ((field & (field + 1)) == 0)
            return (value << shift) & mask;
        uint result = 0;
        while (mask != 0)
        {
            uint bit = mask & unchecked(0u - mask);
            if ((value & 1) != 0)
                result |= bit;
            value >>= 1;
            mask &= mask - 1;
        }
        return result;
    }

    private static bool TryA32Shift(ArmShiftKind shift, int amount, out uint bits)
    {
        bits = 0;
        if (shift == ArmShiftKind.None)
            return amount == 0;
        if (shift == ArmShiftKind.Rrx)
        {
            bits = 3u << 5;
            return amount is 0 or 1;
        }
        if (amount < 0 || amount > 32 || shift is ArmShiftKind.Lsl or ArmShiftKind.Ror && amount == 32)
            return false;
        bits = ((uint)(amount & 31) << 7) | (EncodeA32ShiftKind(shift) << 5);
        return true;
    }

    private static bool TryFloatingImmediate(double value, out uint encoded)
    {
        for (uint i = 0; i < 256; i++)
        {
            ulong bits = ((ulong)(i & 0x80) << 56) | ((ulong)((i & 0x40) == 0 ? 0x400 : 0x3FC) << 52) | ((ulong)(i & 0x3F) << 48);
            if (BitConverter.Int64BitsToDouble(unchecked((long)bits)) == value)
            {
                encoded = i;
                return true;
            }
        }
        encoded = 0;
        return false;
    }

    private static uint EncodeA64ShiftKind(ArmShiftKind shift)
        => shift switch
        {
            ArmShiftKind.None or ArmShiftKind.Lsl => 0u,
            ArmShiftKind.Lsr => 1u,
            ArmShiftKind.Asr => 2u,
            ArmShiftKind.Ror => 3u,
            _ => throw new NotSupportedException("Unsupported AArch64 shift kind."),
        };

    private static uint EncodeA32ShiftKind(ArmShiftKind shift)
        => shift switch
        {
            ArmShiftKind.None or ArmShiftKind.Lsl => 0u,
            ArmShiftKind.Lsr => 1u,
            ArmShiftKind.Asr => 2u,
            ArmShiftKind.Ror or ArmShiftKind.Rrx => 3u,
            _ => throw new NotSupportedException("Unsupported ARM shift kind."),
        };

    private static uint EncodeA64Extend(ArmExtendKind extend)
        => extend switch
        {
            ArmExtendKind.Uxtb => 0u,
            ArmExtendKind.Uxth => 1u,
            ArmExtendKind.Uxtw => 2u,
            ArmExtendKind.Uxtx => 3u,
            ArmExtendKind.Sxtb => 4u,
            ArmExtendKind.Sxth => 5u,
            ArmExtendKind.Sxtw => 6u,
            ArmExtendKind.Sxtx => 7u,
            _ => throw new NotSupportedException("Unsupported AArch64 extension."),
        };

    private static uint EncodeSystemRegister(ArmSystemRegister register)
        => register switch
        {
            ArmSystemRegister.Nzcv => 0x000B4200u,
            ArmSystemRegister.Fpcr => 0x000B4400u,
            ArmSystemRegister.Fpsr => 0x000B4420u,
            ArmSystemRegister.TpidrEl0 => 0x000BD040u,
            ArmSystemRegister.TpidrroEl0 => 0x000BD060u,
            ArmSystemRegister.TpidrEl1 => 0x000CD080u,
            ArmSystemRegister.SpEl0 => 0x000C4100u,
            ArmSystemRegister.ElrEl1 => 0x000C4020u,
            ArmSystemRegister.SpsrEl1 => 0x000C4000u,
            ArmSystemRegister.CurrentEl => 0x00084240u,
            ArmSystemRegister.Daif => 0x000B4220u,
            _ => throw new NotSupportedException($"Unsupported AArch64 system register: {register}"),
        };

    private static long ResolveDisplacement(ArmOperand operand, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        if (!operand.HasSymbol)
            return operand.Immediate;
        return checked((long)ResolveAddress(operand, symbols) - (long)pc);
    }

    private static ulong ResolveAddress(ArmOperand operand, IReadOnlyDictionary<string, ulong>? symbols)
    {
        if (!operand.HasSymbol)
        {
            if (operand.Immediate < 0)
                throw new OverflowException("A negative immediate cannot be used as an absolute address.");
            return (ulong)operand.Immediate;
        }
        if (symbols is null || !symbols.TryGetValue(operand.Symbol!, out var address))
            throw new InvalidOperationException("Unresolved symbol: " + operand.Symbol);
        return checked((ulong)checked((long)address + operand.Addend));
    }

    private static bool TryEncodeA32Immediate(uint value, out uint encoded)
    {
        for (var rotate = 0; rotate < 16; rotate++)
        {
            var candidate = RotateLeft(value, rotate * 2);
            if ((candidate & ~0xFFu) == 0)
            {
                encoded = ((uint)rotate << 8) | candidate;
                return true;
            }
        }
        encoded = 0;
        return false;
    }

    private static bool TryEncodeMoveWide(ulong value, int width, bool inverted, out ushort immediate, out int shift)
    {
        var mask = width == 64 ? ulong.MaxValue : uint.MaxValue;
        var candidate = inverted ? (~value & mask) : value;
        for (var currentShift = 0; currentShift < width; currentShift += 16)
        {
            var outside = candidate & ~(0xFFFFUL << currentShift);
            if (outside == 0)
            {
                immediate = (ushort)(candidate >> currentShift);
                shift = currentShift;
                return true;
            }
        }
        immediate = 0;
        shift = 0;
        return false;
    }

    // Lets instruction selection ask whether a mask reaches the immediate form before it commits to one
    internal static bool IsEncodableLogicalImmediate(ulong value, int width)
        => TryEncodeLogicalImmediate(value, width, out _, out _, out _);

    private static bool TryEncodeLogicalImmediate(ulong value, int width, out uint n, out uint immr, out uint imms)
    {
        var widthMask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        value &= widthMask;
        if (value == 0 || value == widthMask)
        {
            n = immr = imms = 0;
            return false;
        }

        for (var elementSize = 2; elementSize <= width; elementSize <<= 1)
        {
            var elementMask = elementSize == 64 ? ulong.MaxValue : (1UL << elementSize) - 1;
            var element = value & elementMask;
            var replicated = Replicate(element, elementSize, width);
            if (replicated != value)
                continue;
            for (var ones = 1; ones < elementSize; ones++)
            {
                var oneMask = (1UL << ones) - 1;
                for (var rotation = 0; rotation < elementSize; rotation++)
                {
                    if (RotateRight(oneMask, rotation, elementSize) != element)
                        continue;
                    n = elementSize == 64 ? 1u : 0u;
                    immr = (uint)rotation;
                    imms = (uint)((-(elementSize * 2) | (ones - 1)) & 0x3F);
                    return true;
                }
            }
        }
        n = immr = imms = 0;
        return false;
    }

    private static ulong Replicate(ulong element, int elementSize, int width)
    {
        var result = 0UL;
        for (var shift = 0; shift < width; shift += elementSize)
            result |= element << shift;
        return result;
    }

    private static ulong RotateRight(ulong value, int amount, int width)
    {
        amount &= width - 1;
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        value &= mask;
        if (amount == 0)
            return value;
        return ((value >> amount) | (value << (width - amount))) & mask;
    }

    private static uint RotateLeft(uint value, int amount)
    {
        amount &= 31;
        return amount == 0 ? value : value << amount | value >> (32 - amount);
    }

    private static int Log2(int value)
    {
        var result = 0;
        while ((1 << result) < value)
            result++;
        return result;
    }
}

internal static class ArmCodeDecoder
{
    public static ArmProgram DecodeObject(ReadOnlySpan<byte> bytes, ArmTarget target, ulong imageBase = 0)
        => new ArmProgram(target, Decode(bytes, target, imageBase));

    public static ImmutableArray<ArmInstruction> Decode(ReadOnlySpan<byte> bytes, ArmTarget target, ulong imageBase = 0)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if ((bytes.Length & 3) != 0)
            throw new FormatException("ARM instruction stream length must be divisible by four.");
        var builder = ImmutableArray.CreateBuilder<ArmInstruction>(bytes.Length / 4);
        for (var offset = 0; offset < bytes.Length; offset += 4)
        {
            var word = ReadWord(bytes, offset, target.Endianness);
            var pc = checked(imageBase + (ulong)offset);
            builder.Add(target.Is64Bit ? DecodeAArch64(word, pc) : DecodeAArch32(word, pc));
        }
        return builder.MoveToImmutable();
    }

    public static uint ReadWord(ReadOnlySpan<byte> source, int offset, TargetEndianness endianness)
    {
        if ((uint)offset > (uint)(source.Length - 4))
            throw new ArgumentOutOfRangeException(nameof(offset));
        return endianness == TargetEndianness.Little
            ? (uint)(source[offset] | source[offset + 1] << 8 | source[offset + 2] << 16 | source[offset + 3] << 24)
            : (uint)(source[offset] << 24 | source[offset + 1] << 16 | source[offset + 2] << 8 | source[offset + 3]);
    }

    private static ArmInstruction DecodeAArch64(uint word, ulong pc)
    {
        if (word == 0xD503201F) return ArmInstruction.Nop();
        if (word == 0xD503203F) return new ArmInstruction(ArmInstrKind.Yield);
        if (word == 0xD503205F) return new ArmInstruction(ArmInstrKind.Wfe);
        if (word == 0xD503207F) return new ArmInstruction(ArmInstrKind.Wfi);
        if (word == 0xD503209F) return new ArmInstruction(ArmInstrKind.Sev);
        if (word == 0xD50320BF) return new ArmInstruction(ArmInstrKind.Sevl);

        if ((word & 0xFFFFFC1Fu) == 0xD65F0000u)
            return ArmInstruction.Unary(ArmInstrKind.Ret, A64Gpr((word >> 5) & 31, 8, false));
        if ((word & 0xFFFFFC1Fu) == 0xD61F0000u)
            return ArmInstruction.Unary(ArmInstrKind.Br, A64Gpr((word >> 5) & 31, 8, false));
        if ((word & 0xFFFFFC1Fu) == 0xD63F0000u)
            return ArmInstruction.Unary(ArmInstrKind.Blr, A64Gpr((word >> 5) & 31, 8, false));

        if ((word & 0x7C000000u) == 0x14000000u)
        {
            var displacement = SignExtend(word & 0x03FFFFFFu, 26) << 2;
            return ArmInstruction.Branch((word & 0x80000000u) != 0 ? ArmInstrKind.Bl : ArmInstrKind.B, displacement);
        }
        if ((word & 0xFF000010u) == 0x54000000u)
        {
            var displacement = SignExtend((word >> 5) & 0x7FFFFu, 19) << 2;
            return ArmInstruction.Branch(ArmInstrKind.B, displacement, (ArmCondition)(word & 15));
        }
        if ((word & 0x7E000000u) == 0x34000000u)
        {
            var size = (word & 0x80000000u) != 0 ? 8 : 4;
            var displacement = SignExtend((word >> 5) & 0x7FFFFu, 19) << 2;
            return new ArmInstruction((word & 0x01000000u) != 0 ? ArmInstrKind.Cbnz : ArmInstrKind.Cbz,
                A64Gpr(word & 31, size, true), ArmOperand.ImmediateOperand(displacement));
        }
        if ((word & 0x7E000000u) == 0x36000000u)
        {
            var bit = (int)(((word >> 19) & 31) | ((word >> 31) << 5));
            var size = bit >= 32 ? 8 : 4;
            var displacement = SignExtend((word >> 5) & 0x3FFFu, 14) << 2;
            return new ArmInstruction((word & 0x01000000u) != 0 ? ArmInstrKind.Tbnz : ArmInstrKind.Tbz,
                A64Gpr(word & 31, size, true), ArmOperand.ImmediateOperand(bit), ArmOperand.ImmediateOperand(displacement));
        }
        if ((word & 0x1F000000u) == 0x10000000u)
        {
            var page = (word & 0x80000000u) != 0;
            var immediate = SignExtend(((word >> 5) & 0x7FFFFu) << 2 | ((word >> 29) & 3), 21);
            if (page)
                immediate <<= 12;
            return ArmInstruction.Binary(page ? ArmInstrKind.Adrp : ArmInstrKind.Adr, A64Gpr(word & 31, 8, false), ArmOperand.ImmediateOperand(immediate));
        }
        if ((word & 0xFFE0001Fu) == 0xD4000001u)
            return ArmInstruction.Unary(ArmInstrKind.Svc, ArmOperand.ImmediateOperand((word >> 5) & 0xFFFF));
        if ((word & 0xFFE0001Fu) == 0xD4200000u)
            return ArmInstruction.Unary(ArmInstrKind.Brk, ArmOperand.ImmediateOperand((word >> 5) & 0xFFFF));
        if ((word & 0xFFFFF09Fu) == 0xD503309Fu)
            return ArmInstruction.Unary(ArmInstrKind.Dsb, ArmOperand.ImmediateOperand((word >> 8) & 15));
        if ((word & 0xFFFFF09Fu) == 0xD50330BFu)
            return ArmInstruction.Unary(ArmInstrKind.Dmb, ArmOperand.ImmediateOperand((word >> 8) & 15));
        if ((word & 0xFFFFF09Fu) == 0xD50330DFu)
            return ArmInstruction.Unary(ArmInstrKind.Isb, ArmOperand.ImmediateOperand((word >> 8) & 15));
        if ((word & 0xFFF00000u) == 0xD5300000u || (word & 0xFFF00000u) == 0xD5100000u)
        {
            var read = (word & 0x00200000u) != 0;
            var sys = DecodeSystemRegister(word & 0x001FFFE0u);
            var reg = A64Gpr(word & 31, 8, false);
            return read
                ? ArmInstruction.Binary(ArmInstrKind.Mrs, reg, ArmOperand.SystemRegisterOperand(sys))
                : ArmInstruction.Binary(ArmInstrKind.Msr, ArmOperand.SystemRegisterOperand(sys), reg);
        }

        if ((word & 0x1F000000u) == 0x11000000u)
            return DecodeA64AddSubImmediate(word);
        if ((word & 0x1F200000u) == 0x0B000000u)
            return DecodeA64AddSubRegister(word);
        if ((word & 0x1FE0FC00u) == 0x1A000000u)
            return DecodeA64AddSubCarry(word);
        if ((word & 0x1F800000u) == 0x12000000u)
            return DecodeA64LogicalImmediate(word);
        if ((word & 0x1F200000u) == 0x0A000000u)
            return DecodeA64LogicalRegister(word);
        if ((word & 0x1F800000u) == 0x12800000u)
            return DecodeA64MoveWide(word);
        if ((word & 0x1F800000u) is 0x13000000u or 0x53000000u)
            return DecodeA64Bitfield(word);
        if ((word & 0x7FE0FC00u) is 0x1AC00800u or 0x1AC00C00u)
            return DecodeA64Divide(word);
        if ((word & 0x7FE00C00u) == 0x1A800000u)
            return DecodeA64ConditionalSelect(word);
        if ((word & 0x7FE0FC00u) is 0x1AC02000u or 0x1AC02400u or 0x1AC02800u or 0x1AC02C00u)
            return DecodeA64VariableShift(word);
        if ((word & 0x7FE0E000u) == 0x5AC00000u)
            return DecodeA64OneSource(word);
        if ((word & 0x1F000000u) == 0x1B000000u)
            return DecodeA64Multiply(word);
        if ((word & 0x3B000000u) == 0x18000000u)
            return DecodeA64LiteralLoad(word);
        if ((word & 0x3A000000u) == 0x28000000u)
            return DecodeA64Pair(word);
        if ((word & 0x3B200C00u) == 0x38200800u)
            return DecodeA64RegisterOffsetLoadStore(word);
        if ((word & 0x3B000000u) == 0x39000000u)
            return DecodeA64UnsignedLoadStore(word);
        if ((word & 0x3B200000u) == 0x38000000u)
            return DecodeA64UnscaledLoadStore(word);
        if ((word & 0x5F200C00u) == 0x1E200800u || (word & 0x5F200C00u) == 0x1E201800u ||
            (word & 0x5F200C00u) == 0x1E202800u || (word & 0x5F200C00u) == 0x1E203800u)
            return DecodeA64FloatingBinary(word);
        if ((word & 0x5F3F0000u) is 0x1E260000u or 0x1E270000u)
            return DecodeA64IntegerFmov(word);
        if ((word & 0xFF3E7C00u) == 0x1E224000u)
            return DecodeA64FloatPrecision(word);
        if ((word & 0x5F207C00u) is 0x1E204000u or 0x1E20C000u or 0x1E214000u or 0x1E21C000u)
            return DecodeA64FloatingUnary(word);
        if ((word & 0x5F20FC1Fu) == 0x1E202000u)
            return DecodeA64Fcmp(word);
        if ((word & 0x5F3E0000u) is 0x1E220000u or 0x1E230000u or 0x1E380000u or 0x1E390000u)
            return DecodeA64FloatConvert(word);

        return ArmInstruction.Raw(word);
    }

    private static ArmInstruction DecodeA64AddSubImmediate(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var sub = (word & 0x40000000u) != 0;
        var flags = (word & 0x20000000u) != 0;
        var rd = A64Gpr(word & 31, size, !flags);
        var rn = A64Gpr((word >> 5) & 31, size, true);
        var immediate = (long)((word >> 10) & 0xFFF);
        if ((word & (1u << 22)) != 0)
            immediate <<= 12;
        if (flags && (word & 31) == 31)
            return ArmInstruction.Binary(sub ? ArmInstrKind.Cmp : ArmInstrKind.Cmn, rn, ArmOperand.ImmediateOperand(immediate));
        return new ArmInstruction(sub ? flags ? ArmInstrKind.Subs : ArmInstrKind.Sub : flags ? ArmInstrKind.Adds : ArmInstrKind.Add,
            rd, rn, ArmOperand.ImmediateOperand(immediate));
    }

    private static ArmInstruction DecodeA64AddSubRegister(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var sub = (word & 0x40000000u) != 0;
        var flags = (word & 0x20000000u) != 0;
        var rd = A64Gpr(word & 31, size, !flags);
        var rn = A64Gpr((word >> 5) & 31, size, true);
        ArmOperand operand;
        if ((word & (1u << 21)) != 0)
        {
            var extend = DecodeA64Extend((word >> 13) & 7);
            var narrowExtend = extend is ArmExtendKind.Uxtb or ArmExtendKind.Uxth or ArmExtendKind.Uxtw or ArmExtendKind.Sxtb or ArmExtendKind.Sxth or ArmExtendKind.Sxtw;
            var operandSize = size == 4 || narrowExtend ? 4 : 8;
            operand = ArmOperand.ExtendedRegister(A64GprRegister((word >> 16) & 31), extend, (int)((word >> 10) & 7), operandSize);
        }
        else
        {
            operand = ArmOperand.ShiftedRegister(A64GprRegister((word >> 16) & 31), DecodeA64Shift((word >> 22) & 3), (int)((word >> 10) & 63), size);
        }
        if (flags && (word & 31) == 31)
            return ArmInstruction.Binary(sub ? ArmInstrKind.Cmp : ArmInstrKind.Cmn, rn, operand);
        return new ArmInstruction(sub ? flags ? ArmInstrKind.Subs : ArmInstrKind.Sub : flags ? ArmInstrKind.Adds : ArmInstrKind.Add, rd, rn, operand);
    }

    private static ArmInstruction DecodeA64AddSubCarry(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var sub = (word & 0x40000000u) != 0;
        var flags = (word & 0x20000000u) != 0;
        return ArmInstruction.Ternary(sub ? flags ? ArmInstrKind.Sbcs : ArmInstrKind.Sbc : flags ? ArmInstrKind.Adcs : ArmInstrKind.Adc,
            A64Gpr(word & 31, size, false), A64Gpr((word >> 5) & 31, size, false), A64Gpr((word >> 16) & 31, size, false));
    }

    private static ArmInstruction DecodeA64LogicalImmediate(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var opc = (word >> 29) & 3;
        var value = DecodeLogicalImmediate((word >> 22) & 1, (word >> 16) & 63, (word >> 10) & 63, size * 8);
        var rdIndex = word & 31;
        var rnIndex = (word >> 5) & 31;
        if (opc == 3 && rdIndex == 31)
            return ArmInstruction.Binary(ArmInstrKind.Tst, A64Gpr(rnIndex, size, false), ArmOperand.ImmediateOperand(unchecked((long)value)));
        if (opc == 1 && rnIndex == 31)
            return ArmInstruction.Binary(ArmInstrKind.Mov, A64Gpr(rdIndex, size, false), ArmOperand.ImmediateOperand(unchecked((long)value)));
        var opcode = opc switch
        {
            0 => ArmInstrKind.And,
            1 => ArmInstrKind.Orr,
            2 => ArmInstrKind.Eor,
            3 => ArmInstrKind.Ands,
            _ => ArmInstrKind.Invalid,
        };
        return ArmInstruction.Ternary(opcode, A64Gpr(rdIndex, size, false), A64Gpr(rnIndex, size, false), ArmOperand.ImmediateOperand(unchecked((long)value)));
    }

    private static ArmInstruction DecodeA64LogicalRegister(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var opc = (word >> 29) & 3;
        var inverted = (word & (1u << 21)) != 0;
        var rdIndex = word & 31;
        var rnIndex = (word >> 5) & 31;
        var shift = DecodeA64Shift((word >> 22) & 3);
        var amount = (int)((word >> 10) & 63);
        var rm = ArmOperand.ShiftedRegister(A64GprRegister((word >> 16) & 31), shift, amount, size);
        if (opc == 1 && rnIndex == 31)
            return ArmInstruction.Binary(inverted ? ArmInstrKind.Mvn : ArmInstrKind.Mov, A64Gpr(rdIndex, size, false), rm);
        if (opc == 3 && rdIndex == 31)
            return ArmInstruction.Binary(ArmInstrKind.Tst, A64Gpr(rnIndex, size, false), rm);
        var opcode = (opc, inverted) switch
        {
            (0, false) => ArmInstrKind.And,
            (0, true) => ArmInstrKind.Bic,
            (1, false) => ArmInstrKind.Orr,
            (2, false) => ArmInstrKind.Eor,
            (3, false) => ArmInstrKind.Ands,
            (3, true) => ArmInstrKind.Bics,
            _ => ArmInstrKind.Raw,
        };
        if (opcode == ArmInstrKind.Raw)
            return ArmInstruction.Raw(word);
        return ArmInstruction.Ternary(opcode, A64Gpr(rdIndex, size, false), A64Gpr(rnIndex, size, false), rm);
    }

    private static ArmInstruction DecodeA64MoveWide(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var opc = (word >> 29) & 3;
        var opcode = opc switch
        {
            0 => ArmInstrKind.Movn,
            2 => ArmInstrKind.Movz,
            3 => ArmInstrKind.Movk,
            _ => ArmInstrKind.Raw,
        };
        if (opcode == ArmInstrKind.Raw)
            return ArmInstruction.Raw(word);
        return ArmInstruction.Ternary(opcode, A64Gpr(word & 31, size, false), ArmOperand.ImmediateOperand((word >> 5) & 0xFFFF), ArmOperand.ImmediateOperand(((word >> 21) & 3) * 16));
    }

    private static ArmInstruction DecodeA64Bitfield(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var signed = (word & 0x40000000u) == 0;
        var immr = (int)((word >> 16) & 63);
        var imms = (int)((word >> 10) & 63);
        var width = size * 8;
        var rd = A64Gpr(word & 31, size, false);
        var rn = A64Gpr((word >> 5) & 31, size, false);
        if (imms == width - 1)
            return ArmInstruction.Ternary(signed ? ArmInstrKind.Asr : ArmInstrKind.Lsr, rd, rn, ArmOperand.ImmediateOperand(immr));
        var shift = (width - immr) & (width - 1);
        if (!signed && imms == width - 1 - shift)
            return ArmInstruction.Ternary(ArmInstrKind.Lsl, rd, rn, ArmOperand.ImmediateOperand(shift));
        return ArmInstruction.Raw(word);
    }

    private static ArmInstruction DecodeA64Divide(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        return ArmInstruction.Ternary((word & 0x400u) != 0 ? ArmInstrKind.Sdiv : ArmInstrKind.Udiv,
            A64Gpr(word & 31, size, false), A64Gpr((word >> 5) & 31, size, false), A64Gpr((word >> 16) & 31, size, false));
    }

    private static ArmInstruction DecodeA64ConditionalSelect(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        return ArmInstruction.Ternary(ArmInstrKind.Csel, A64Gpr(word & 31, size, false), A64Gpr((word >> 5) & 31, size, false),
            A64Gpr((word >> 16) & 31, size, false), (ArmCondition)((word >> 12) & 15));
    }

    private static ArmInstruction DecodeA64VariableShift(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var op = (word >> 10) & 3;
        var opcode = op switch
        {
            0 => ArmInstrKind.Lsl,
            1 => ArmInstrKind.Lsr,
            2 => ArmInstrKind.Asr,
            3 => ArmInstrKind.Ror,
            _ => ArmInstrKind.Invalid,
        };
        return ArmInstruction.Ternary(opcode, A64Gpr(word & 31, size, false), A64Gpr((word >> 5) & 31, size, false), A64Gpr((word >> 16) & 31, size, false));
    }

    private static ArmInstruction DecodeA64OneSource(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var op = (word >> 10) & 7;
        var opcode = op switch
        {
            0 => ArmInstrKind.Rbit,
            1 => ArmInstrKind.Rev16,
            2 when size == 4 => ArmInstrKind.Rev,
            3 when size == 8 => ArmInstrKind.Rev,
            4 => ArmInstrKind.Clz,
            _ => ArmInstrKind.Raw,
        };
        if (opcode == ArmInstrKind.Raw)
            return ArmInstruction.Raw(word);
        return ArmInstruction.Binary(opcode, A64Gpr(word & 31, size, false), A64Gpr((word >> 5) & 31, size, false));
    }

    private static ArmInstruction DecodeA64Multiply(uint word)
    {
        var size = (word & 0x80000000u) != 0 ? 8 : 4;
        var subtract = (word & (1u << 15)) != 0;
        var addend = (word >> 10) & 31;
        var rd = A64Gpr(word & 31, size, false);
        var rn = A64Gpr((word >> 5) & 31, size, false);
        var rm = A64Gpr((word >> 16) & 31, size, false);
        if (!subtract && addend == 31)
            return ArmInstruction.Ternary(ArmInstrKind.Mul, rd, rn, rm);
        return ArmInstruction.Quaternary(subtract ? ArmInstrKind.Msub : ArmInstrKind.Madd, rd, rn, rm, A64Gpr(addend, size, false));
    }

    private static ArmInstruction DecodeA64LiteralLoad(uint word)
    {
        var vector = (word & (1u << 26)) != 0;
        var opc = (word >> 30) & 3;
        int size;
        ArmInstrKind opcode;
        if (vector)
        {
            size = opc switch { 0 => 4, 1 => 8, 2 => 16, _ => 4 };
            opcode = ArmInstrKind.Ldr;
        }
        else if (opc == 2)
        {
            size = 8;
            opcode = ArmInstrKind.Ldrsw;
        }
        else
        {
            size = opc == 1 ? 8 : 4;
            opcode = ArmInstrKind.Ldr;
        }
        var displacement = SignExtend((word >> 5) & 0x7FFFFu, 19) << 2;
        return ArmInstruction.Binary(opcode, vector ? A64Vector(word & 31, size) : A64Gpr(word & 31, size, false), ArmOperand.Literal(displacement, size));
    }

    private static ArmInstruction DecodeA64UnsignedLoadStore(uint word)
        => DecodeA64LoadStoreCommon(word, true, ArmAddressingMode.Offset);

    private static ArmInstruction DecodeA64UnscaledLoadStore(uint word)
    {
        var mode = (word >> 10) & 3;
        if (mode == 2)
            return ArmInstruction.Raw(word);
        var addressing = mode switch
        {
            1 => ArmAddressingMode.PostIndex,
            3 => ArmAddressingMode.PreIndex,
            _ => ArmAddressingMode.Offset,
        };
        return DecodeA64LoadStoreCommon(word, false, addressing);
    }

    private static ArmInstruction DecodeA64LoadStoreCommon(uint word, bool scaled, ArmAddressingMode mode)
    {
        var sizeCode = (word >> 30) & 3;
        var vector = (word & (1u << 26)) != 0;
        var opc = (word >> 22) & 3;
        var size = vector && (word & (1u << 23)) != 0 ? 16 : 1 << (int)sizeCode;
        var load = opc != 0;
        var opcode = GetDecodedLoadStoreOpcode(load, opc, size);
        var targetSize = opcode is ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh ? ((opc == 2) ? 8 : 4) : opcode == ArmInstrKind.Ldrsw ? 8 : size;
        var target = vector ? A64Vector(word & 31, size) : A64Gpr(word & 31, targetSize, false);
        var immediate = scaled ? (long)((word >> 10) & 0xFFF) * size : SignExtend((word >> 12) & 0x1FF, 9);
        return ArmInstruction.Binary(opcode, target, ArmOperand.Memory(A64BaseRegister((word >> 5) & 31), immediate, size, mode));
    }

    private static ArmInstruction DecodeA64RegisterOffsetLoadStore(uint word)
    {
        var sizeCode = (word >> 30) & 3;
        var vector = (word & (1u << 26)) != 0;
        var opc = (word >> 22) & 3;
        var size = vector && (word & (1u << 23)) != 0 ? 16 : 1 << (int)sizeCode;
        var opcode = GetDecodedLoadStoreOpcode(opc != 0, opc, size);
        var targetSize = opcode is ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh ? (opc == 2 ? 8 : 4) : opcode == ArmInstrKind.Ldrsw ? 8 : size;
        var option = (word >> 13) & 7;
        if (option is not 2 and not 3 and not 6 and not 7)
            return ArmInstruction.Raw(word);
        var extend = option == 3 ? ArmExtendKind.None : DecodeA64Extend(option);
        var shift = (word & (1u << 12)) != 0 ? Log2(size) : 0;
        var target = vector ? A64Vector(word & 31, size) : A64Gpr(word & 31, targetSize, false);
        return ArmInstruction.Binary(opcode, target, ArmOperand.Memory(A64BaseRegister((word >> 5) & 31), 0, size, ArmAddressingMode.Offset,
            A64GprRegister((word >> 16) & 31), ArmShiftKind.Lsl, shift, extend));
    }

    private static ArmInstruction DecodeA64Pair(uint word)
    {
        var vector = (word & (1u << 26)) != 0;
        var opc = (word >> 30) & 3;
        var size = vector ? opc switch { 0 => 4, 1 => 8, 2 => 16, _ => 4 } : opc == 2 ? 8 : 4;
        var load = (word & (1u << 22)) != 0;
        var mode = (word >> 23) & 3;
        if (mode == 0)
            return ArmInstruction.Raw(word);
        var addressing = mode switch
        {
            1 => ArmAddressingMode.PostIndex,
            3 => ArmAddressingMode.PreIndex,
            _ => ArmAddressingMode.Offset,
        };
        var immediate = SignExtend((word >> 15) & 0x7F, 7) * size;
        var first = vector ? A64Vector(word & 31, size) : A64Gpr(word & 31, size, false);
        var second = vector ? A64Vector((word >> 10) & 31, size) : A64Gpr((word >> 10) & 31, size, false);
        return ArmInstruction.Ternary(load ? ArmInstrKind.Ldp : ArmInstrKind.Stp, first, second,
            ArmOperand.Memory(A64BaseRegister((word >> 5) & 31), immediate, size, addressing));
    }

    private static ArmInstruction DecodeA64FloatingBinary(uint word)
    {
        var size = (word & (1u << 22)) != 0 ? 8 : 4;
        var masked = word & 0x5F200C00u;
        var opcode = masked switch
        {
            0x1E200800u => ArmInstrKind.Fmul,
            0x1E201800u => ArmInstrKind.Fdiv,
            0x1E202800u => ArmInstrKind.Fadd,
            0x1E203800u => ArmInstrKind.Fsub,
            _ => ArmInstrKind.Invalid,
        };
        return ArmInstruction.Ternary(opcode, A64Vector(word & 31, size), A64Vector((word >> 5) & 31, size), A64Vector((word >> 16) & 31, size));
    }

    private static ArmInstruction DecodeA64IntegerFmov(uint word)
    {
        var integerSize = (word & 0x80000000u) != 0 ? 8 : 4;
        var floatSize = (word & (1u << 22)) != 0 ? 8 : 4;
        var toFloat = (word & (1u << 16)) != 0;
        return toFloat
            ? ArmInstruction.Binary(ArmInstrKind.Fmov, A64Vector(word & 31, floatSize), A64Gpr((word >> 5) & 31, integerSize, false))
            : ArmInstruction.Binary(ArmInstrKind.Fmov, A64Gpr(word & 31, integerSize, false), A64Vector((word >> 5) & 31, floatSize));
    }

    private static ArmInstruction DecodeA64FloatingUnary(uint word)
    {
        var size = (word & (1u << 22)) != 0 ? 8 : 4;
        var masked = word & 0x5F207C00u;
        var opcode = masked switch
        {
            0x1E204000u => ArmInstrKind.Fmov,
            0x1E20C000u => ArmInstrKind.Fabs,
            0x1E214000u => ArmInstrKind.Fneg,
            0x1E21C000u => ArmInstrKind.Fsqrt,
            _ => ArmInstrKind.Invalid,
        };
        return ArmInstruction.Binary(opcode, A64Vector(word & 31, size), A64Vector((word >> 5) & 31, size));
    }

    private static ArmInstruction DecodeA64FloatPrecision(uint word)
    {
        var sourceSize = ((word >> 22) & 3) == 1 ? 8 : 4;
        var destinationSize = ((word >> 15) & 3) == 1 ? 8 : 4;
        return ArmInstruction.Binary(
            ArmInstrKind.Fcvt,
            A64Vector(word & 31, destinationSize),
            A64Vector((word >> 5) & 31, sourceSize));
    }

    private static ArmInstruction DecodeA64Fcmp(uint word)
    {
        var size = (word & (1u << 22)) != 0 ? 8 : 4;
        return ArmInstruction.Binary(ArmInstrKind.Fcmp, A64Vector((word >> 5) & 31, size), A64Vector((word >> 16) & 31, size));
    }

    private static ArmInstruction DecodeA64FloatConvert(uint word)
    {
        var sf = (word & 0x80000000u) != 0 ? 8 : 4;
        var floatSize = (word & (1u << 22)) != 0 ? 8 : 4;
        var operation = word & 0x5F3E0000u;
        return operation switch
        {
            0x1E220000u => ArmInstruction.Binary(ArmInstrKind.Scvtf, A64Vector(word & 31, floatSize), A64Gpr((word >> 5) & 31, sf, false)),
            0x1E230000u => ArmInstruction.Binary(ArmInstrKind.Ucvtf, A64Vector(word & 31, floatSize), A64Gpr((word >> 5) & 31, sf, false)),
            0x1E380000u => ArmInstruction.Binary(ArmInstrKind.Fcvtzs, A64Gpr(word & 31, sf, false), A64Vector((word >> 5) & 31, floatSize)),
            0x1E390000u => ArmInstruction.Binary(ArmInstrKind.Fcvtzu, A64Gpr(word & 31, sf, false), A64Vector((word >> 5) & 31, floatSize)),
            _ => ArmInstruction.Raw(word),
        };
    }

    private static ArmInstruction DecodeAArch32(uint word, ulong pc)
    {
        var condition = (ArmCondition)(word >> 28);
        if ((word & 0x0FFFFFF0u) == 0x0320F000u)
        {
            var hint = (word & 15u) switch
            {
                0 => ArmInstrKind.Nop,
                1 => ArmInstrKind.Yield,
                2 => ArmInstrKind.Wfe,
                3 => ArmInstrKind.Wfi,
                4 => ArmInstrKind.Sev,
                5 => ArmInstrKind.Sevl,
                _ => ArmInstrKind.Raw,
            };
            return hint == ArmInstrKind.Raw ? ArmInstruction.Raw(word) : new ArmInstruction(hint, condition: condition);
        }
        if ((word & 0xFFFFFFF0u) is 0xF57FF040u or 0xF57FF050u or 0xF57FF060u)
        {
            var opcode = (word & 0xF0u) switch
            {
                0x40 => ArmInstrKind.Dsb,
                0x50 => ArmInstrKind.Dmb,
                _ => ArmInstrKind.Isb,
            };
            return ArmInstruction.Unary(opcode, ArmOperand.ImmediateOperand(word & 15u));
        }
        if ((word & 0x0FBF0FFFu) is 0x010F0000u or 0x014F0000u)
        {
            var system = (word & 0x00400000u) != 0 ? ArmSystemRegister.Spsr : ArmSystemRegister.Cpsr;
            return ArmInstruction.Binary(ArmInstrKind.Mrs, A32Gpr((word >> 12) & 15), ArmOperand.SystemRegisterOperand(system), condition);
        }
        if ((word & 0x0FBFFFF0u) is 0x012FF000u or 0x016FF000u)
        {
            var system = (word & 0x00400000u) != 0 ? ArmSystemRegister.Spsr : ArmSystemRegister.Cpsr;
            return ArmInstruction.Binary(ArmInstrKind.Msr, ArmOperand.SystemRegisterOperand(system), A32Gpr(word & 15), condition);
        }
        if ((word & 0x0E000000u) == 0x0A000000u)
        {
            var displacement = (SignExtend(word & 0xFFFFFFu, 24) << 2) + 8;
            return ArmInstruction.Branch((word & 0x01000000u) != 0 ? ArmInstrKind.Bl : ArmInstrKind.B, displacement, condition);
        }
        if ((word & 0x0FFFFFF0u) == 0x012FFF10u)
            return ArmInstruction.Unary(ArmInstrKind.Bx, A32Gpr(word & 15), condition);
        if ((word & 0x0FFFFFF0u) == 0x012FFF30u)
            return ArmInstruction.Unary(ArmInstrKind.Blx, A32Gpr(word & 15), condition);
        if ((word & 0x0F000000u) == 0x0F000000u)
            return ArmInstruction.Unary(ArmInstrKind.Svc, ArmOperand.ImmediateOperand(word & 0xFFFFFFu), condition);
        if ((word & 0x0FF000F0u) == 0x01200070u)
        {
            var immediate = ((word >> 4) & 0xFFF0u) | (word & 15u);
            return ArmInstruction.Unary(ArmInstrKind.Bkpt, ArmOperand.ImmediateOperand(immediate), condition);
        }
        if ((word & 0x0FF00000u) is 0x03000000u or 0x03400000u)
        {
            var immediate = ((word >> 4) & 0xF000u) | (word & 0xFFFu);
            return ArmInstruction.Binary((word & 0x00400000u) != 0 ? ArmInstrKind.Movt : ArmInstrKind.Movw,
                A32Gpr((word >> 12) & 15), ArmOperand.ImmediateOperand(immediate), condition);
        }
        if ((word & 0x0FFF0FF0u) == 0x016F0F10u)
            return ArmInstruction.Binary(ArmInstrKind.Clz, A32Gpr((word >> 12) & 15), A32Gpr(word & 15), condition);
        if ((word & 0x0FFF0FF0u) is 0x06BF0F30u or 0x06BF0FB0u or 0x06FF0FB0u or 0x06FF0F30u)
            return DecodeA32Reverse(word, condition);
        if ((word & 0x0FF0F0F0u) is 0x0710F010u or 0x0730F010u)
            return DecodeA32Divide(word, condition);
        if ((word & 0x0FC000F0u) == 0x00000090u)
            return DecodeA32Multiply(word, condition);
        if ((word & 0x0F8000F0u) == 0x00800090u)
            return DecodeA32LongMultiply(word, condition);
        if ((word & 0x0FE000F0u) == 0x00600090u)
            return DecodeA32Mls(word, condition);
        if ((word & 0x0E000000u) == 0x08000000u)
            return DecodeA32Multiple(word, condition);
        if ((word & 0x0E000E00u) == 0x0C000A00u)
            return DecodeA32VfpLoadStore(word, condition);
        if ((word & 0x0E000090u) == 0x00000090u && (word & 0x00000060u) != 0)
            return DecodeA32HalfwordLoadStore(word, condition);
        if ((word & 0x0C000000u) == 0x04000000u)
            return DecodeA32LoadStore(word, condition);
        if ((word & 0x0FE00F7Fu) == 0x0E000A10u)
        {
            var single = DecodeA32Vn(word, 4);
            var core = A32Gpr((word >> 12) & 15);
            return (word & (1u << 20)) != 0
                ? ArmInstruction.Binary(ArmInstrKind.Fmov, core, single, condition)
                : ArmInstruction.Binary(ArmInstrKind.Fmov, single, core, condition);
        }
        if ((word & 0x0F000E10u) == 0x0E000A00u)
        {
            var floating = DecodeA32Floating(word, condition);
            if (floating.Opcode != ArmInstrKind.Raw)
                return floating;
        }
        if ((word & 0x0C000000u) == 0)
            return DecodeA32DataProcessing(word, condition);
        return ArmInstruction.Raw(word);
    }

    private static ArmInstruction DecodeA32DataProcessing(uint word, ArmCondition condition)
    {
        var immediate = (word & (1u << 25)) != 0;
        var opcodeValue = (word >> 21) & 15;
        var flags = (word & (1u << 20)) != 0;
        var opcode = opcodeValue switch
        {
            0 => flags ? ArmInstrKind.Ands : ArmInstrKind.And,
            1 => ArmInstrKind.Eor,
            2 => flags ? ArmInstrKind.Subs : ArmInstrKind.Sub,
            3 => ArmInstrKind.Rsb,
            4 => flags ? ArmInstrKind.Adds : ArmInstrKind.Add,
            5 => flags ? ArmInstrKind.Adcs : ArmInstrKind.Adc,
            6 => flags ? ArmInstrKind.Sbcs : ArmInstrKind.Sbc,
            8 => ArmInstrKind.Tst,
            9 => ArmInstrKind.Teq,
            10 => ArmInstrKind.Cmp,
            11 => ArmInstrKind.Cmn,
            12 => ArmInstrKind.Orr,
            13 => ArmInstrKind.Mov,
            14 => flags ? ArmInstrKind.Bics : ArmInstrKind.Bic,
            15 => ArmInstrKind.Mvn,
            _ => ArmInstrKind.Raw,
        };
        if (opcode == ArmInstrKind.Raw)
            return ArmInstruction.Raw(word);
        ArmOperand operand2;
        if (immediate)
        {
            var rotate = (int)((word >> 8) & 15) * 2;
            operand2 = ArmOperand.ImmediateOperand(RotateRight32(word & 0xFFu, rotate));
        }
        else
        {
            if ((word & (1u << 4)) != 0)
            {
                if (opcodeValue != 13 || ((word >> 16) & 15) != 0 || (word & (1u << 7)) != 0)
                    return ArmInstruction.Raw(word);
                var shiftOpcode = ((word >> 5) & 3) switch
                {
                    0 => ArmInstrKind.Lsl,
                    1 => ArmInstrKind.Lsr,
                    2 => ArmInstrKind.Asr,
                    _ => ArmInstrKind.Ror,
                };
                return ArmInstruction.Ternary(shiftOpcode, A32Gpr((word >> 12) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), condition).WithSetFlags(flags);
            }
            var shiftType = DecodeA32Shift((word >> 5) & 3, (int)((word >> 7) & 31));
            operand2 = shiftType.Kind == ArmShiftKind.None
                ? A32Gpr(word & 15)
                : ArmOperand.ShiftedRegister(A32GprRegister(word & 15), shiftType.Kind, shiftType.Amount, 4);
        }
        if (opcode is ArmInstrKind.Tst or ArmInstrKind.Teq or ArmInstrKind.Cmp or ArmInstrKind.Cmn)
            return ArmInstruction.Binary(opcode, A32Gpr((word >> 16) & 15), operand2, condition);
        if (opcode == ArmInstrKind.Mov && operand2.Kind == ArmOperandKind.ShiftedRegister && operand2.Shift is not ArmShiftKind.Rrx)
        {
            var shiftOpcode = operand2.Shift switch
            {
                ArmShiftKind.Lsl => ArmInstrKind.Lsl,
                ArmShiftKind.Lsr => ArmInstrKind.Lsr,
                ArmShiftKind.Asr => ArmInstrKind.Asr,
                _ => ArmInstrKind.Ror,
            };
            return ArmInstruction.Ternary(shiftOpcode, A32Gpr((word >> 12) & 15), A32Gpr(word & 15), ArmOperand.ImmediateOperand(operand2.ShiftAmount), condition).WithSetFlags(flags);
        }
        if (opcode is ArmInstrKind.Mov or ArmInstrKind.Mvn)
            return ArmInstruction.Binary(opcode, A32Gpr((word >> 12) & 15), operand2, condition).WithSetFlags(flags);
        return ArmInstruction.Ternary(opcode, A32Gpr((word >> 12) & 15), A32Gpr((word >> 16) & 15), operand2, condition).WithSetFlags(flags);
    }

    private static ArmInstruction DecodeA32Multiply(uint word, ArmCondition condition)
    {
        var accumulate = (word & (1u << 21)) != 0;
        var flags = (word & (1u << 20)) != 0;
        if (accumulate)
            return new ArmInstruction(ArmInstrKind.Mla, A32Gpr((word >> 16) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), A32Gpr((word >> 12) & 15), condition, flags);
        return new ArmInstruction(ArmInstrKind.Mul, A32Gpr((word >> 16) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), condition: condition, setFlags: flags);
    }

    private static ArmInstruction DecodeA32Mls(uint word, ArmCondition condition)
        => ArmInstruction.Quaternary(ArmInstrKind.Mls, A32Gpr((word >> 16) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), A32Gpr((word >> 12) & 15), condition);

    private static ArmInstruction DecodeA32LongMultiply(uint word, ArmCondition condition)
    {
        var signed = (word & (1u << 22)) != 0;
        return new ArmInstruction(signed ? ArmInstrKind.Smull : ArmInstrKind.Umull,
            A32Gpr((word >> 12) & 15), A32Gpr((word >> 16) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), condition, (word & (1u << 20)) != 0);
    }

    private static ArmInstruction DecodeA32Divide(uint word, ArmCondition condition)
        => ArmInstruction.Ternary((word & (1u << 21)) == 0 ? ArmInstrKind.Sdiv : ArmInstrKind.Udiv,
            A32Gpr((word >> 16) & 15), A32Gpr(word & 15), A32Gpr((word >> 8) & 15), condition);

    private static ArmInstruction DecodeA32Reverse(uint word, ArmCondition condition)
    {
        var masked = word & 0x0FFF0FF0u;
        var opcode = masked switch
        {
            0x06BF0F30u => ArmInstrKind.Rev,
            0x06BF0FB0u => ArmInstrKind.Rev16,
            0x06FF0FB0u => ArmInstrKind.Revsh,
            0x06FF0F30u => ArmInstrKind.Rbit,
            _ => ArmInstrKind.Raw,
        };
        return ArmInstruction.Binary(opcode, A32Gpr((word >> 12) & 15), A32Gpr(word & 15), condition);
    }

    private static ArmInstruction DecodeA32LoadStore(uint word, ArmCondition condition)
    {
        var load = (word & (1u << 20)) != 0;
        var byteAccess = (word & (1u << 22)) != 0;
        var registerOffset = (word & (1u << 25)) != 0;
        var pre = (word & (1u << 24)) != 0;
        var add = (word & (1u << 23)) != 0;
        var writeback = (word & (1u << 21)) != 0;
        var mode = !pre ? ArmAddressingMode.PostIndex : writeback ? ArmAddressingMode.PreIndex : ArmAddressingMode.Offset;
        ArmOperand memory;
        if (registerOffset)
        {
            var shift = DecodeA32Shift((word >> 5) & 3, (int)((word >> 7) & 31));
            memory = ArmOperand.Memory(A32GprRegister((word >> 16) & 15), 0, byteAccess ? 1 : 4, mode,
                A32GprRegister(word & 15), shift.Kind, shift.Amount);
        }
        else
        {
            var offset = (long)(word & 0xFFF);
            if (!add)
                offset = -offset;
            memory = ArmOperand.Memory(A32GprRegister((word >> 16) & 15), offset, byteAccess ? 1 : 4, mode);
        }
        return ArmInstruction.Binary(load ? byteAccess ? ArmInstrKind.Ldrb : ArmInstrKind.Ldr : byteAccess ? ArmInstrKind.Strb : ArmInstrKind.Str,
            A32Gpr((word >> 12) & 15), memory, condition);
    }

    private static ArmInstruction DecodeA32HalfwordLoadStore(uint word, ArmCondition condition)
    {
        var load = (word & (1u << 20)) != 0;
        var immediate = (word & (1u << 22)) != 0;
        var pre = (word & (1u << 24)) != 0;
        var add = (word & (1u << 23)) != 0;
        var writeback = (word & (1u << 21)) != 0;
        var operation = (word >> 5) & 3;
        var opcode = (operation, load) switch
        {
            (1, true) => ArmInstrKind.Ldrh,
            (1, false) => ArmInstrKind.Strh,
            (2, true) => ArmInstrKind.Ldrsb,
            (3, true) => ArmInstrKind.Ldrsh,
            _ => ArmInstrKind.Raw,
        };
        if (opcode == ArmInstrKind.Raw)
            return ArmInstruction.Raw(word);
        var mode = !pre ? ArmAddressingMode.PostIndex : writeback ? ArmAddressingMode.PreIndex : ArmAddressingMode.Offset;
        ArmOperand memory;
        if (immediate)
        {
            var offset = (long)(((word >> 4) & 0xF0) | (word & 15));
            if (!add)
                offset = -offset;
            memory = ArmOperand.Memory(A32GprRegister((word >> 16) & 15), offset, opcode == ArmInstrKind.Ldrsb ? 1 : 2, mode);
        }
        else
        {
            memory = ArmOperand.Memory(A32GprRegister((word >> 16) & 15), 0, opcode == ArmInstrKind.Ldrsb ? 1 : 2, mode, A32GprRegister(word & 15));
        }
        return ArmInstruction.Binary(opcode, A32Gpr((word >> 12) & 15), memory, condition);
    }

    private static ArmInstruction DecodeA32Multiple(uint word, ArmCondition condition)
    {
        var load = (word & (1u << 20)) != 0;
        var p = (word & (1u << 24)) != 0;
        var u = (word & (1u << 23)) != 0;
        var writeback = (word & (1u << 21)) != 0;
        var rn = (word >> 16) & 15;
        var mask = word & 0xFFFF;
        if (!load && p && !u && writeback && rn == 13)
            return ArmInstruction.Unary(ArmInstrKind.Push, ArmOperand.RegisterList(mask), condition);
        if (load && !p && u && writeback && rn == 13)
            return ArmInstruction.Unary(ArmInstrKind.Pop, ArmOperand.RegisterList(mask), condition);
        var mode = !p && u ? 1 : p && !u ? 2 : p && u ? 3 : 4;
        return new ArmInstruction(load ? ArmInstrKind.Ldm : ArmInstrKind.Stm, A32Gpr(rn), ArmOperand.RegisterList(mask), ArmOperand.ImmediateOperand(mode), condition: condition, setFlags: writeback);
    }

    private static ArmInstruction DecodeA32VfpLoadStore(uint word, ArmCondition condition)
    {
        var load = (word & (1u << 20)) != 0;
        var add = (word & (1u << 23)) != 0;
        var size = (word & (1u << 8)) != 0 ? 8 : 4;
        var immediate = (long)(word & 0xFFu) << 2;
        if (!add)
            immediate = -immediate;
        var memory = ArmOperand.Memory(A32GprRegister((word >> 16) & 15), immediate, size);
        return ArmInstruction.Binary(load ? ArmInstrKind.Ldr : ArmInstrKind.Str, DecodeA32Vd(word, size), memory, condition);
    }

    private static ArmInstruction DecodeA32Floating(uint word, ArmCondition condition)
    {
        var doublePrecision = (word & (1u << 8)) != 0;
        var size = doublePrecision ? 8 : 4;
        var op = word & 0x0FB00E50u;
        ArmInstrKind opcode;
        if ((word & 0x0FB00E50u) == 0x0E300A00u) opcode = ArmInstrKind.Fadd;
        else if ((word & 0x0FB00E50u) == 0x0E300A40u) opcode = ArmInstrKind.Fsub;
        else if ((word & 0x0FB00E50u) == 0x0E200A00u) opcode = ArmInstrKind.Fmul;
        else if ((word & 0x0FB00E50u) == 0x0E800A00u) opcode = ArmInstrKind.Fdiv;
        else if ((word & 0x0FBF0ED0u) == 0x0EB00A40u) opcode = ArmInstrKind.Fmov;
        else if ((word & 0x0FBF0ED0u) == 0x0EB00AC0u) opcode = ArmInstrKind.Fabs;
        else if ((word & 0x0FBF0ED0u) == 0x0EB10A40u) opcode = ArmInstrKind.Fneg;
        else if ((word & 0x0FBF0ED0u) == 0x0EB10AC0u) opcode = ArmInstrKind.Fsqrt;
        else if ((word & 0x0FBF0E50u) == 0x0EB40A40u) opcode = ArmInstrKind.Fcmp;
        else if ((word & 0x0FBF0ED0u) == 0x0EB80AC0u) opcode = ArmInstrKind.Scvtf;
        else if ((word & 0x0FBF0ED0u) == 0x0EB80A40u) opcode = ArmInstrKind.Ucvtf;
        else if ((word & 0x0FBF0ED0u) == 0x0EBD0AC0u) opcode = ArmInstrKind.Fcvtzs;
        else if ((word & 0x0FBF0ED0u) == 0x0EBC0AC0u) opcode = ArmInstrKind.Fcvtzu;
        else if ((word & 0x0FBF0ED0u) == 0x0EB70AC0u) opcode = ArmInstrKind.Fcvt;
        else return ArmInstruction.Raw(word);

        if (opcode == ArmInstrKind.Fcvt)
            return ArmInstruction.Binary(opcode, DecodeA32Vd(word, size == 8 ? 4 : 8), DecodeA32Vm(word, size), condition);

        if (opcode is ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf)
            return ArmInstruction.Binary(opcode, DecodeA32Vd(word, size), DecodeA32Vm(word, 4), condition);
        if (opcode is ArmInstrKind.Fcvtzs or ArmInstrKind.Fcvtzu)
            return ArmInstruction.Binary(opcode, DecodeA32Vd(word, 4), DecodeA32Vm(word, size), condition);

        var vd = DecodeA32Vd(word, size);
        var vm = DecodeA32Vm(word, size);
        if (opcode is ArmInstrKind.Fmov or ArmInstrKind.Fabs or ArmInstrKind.Fneg or ArmInstrKind.Fsqrt or ArmInstrKind.Fcmp)
            return ArmInstruction.Binary(opcode, vd, vm, condition);
        return ArmInstruction.Ternary(opcode, vd, DecodeA32Vn(word, size), vm, condition);
    }

    private static ArmOperand DecodeA32Vd(uint word, int size)
    {
        var index = size == 4 ? (int)(((word >> 12) & 15) << 1 | ((word >> 22) & 1)) : (int)(((word >> 22) & 1) << 4 | ((word >> 12) & 15));
        return A64Vector((uint)index, size);
    }

    private static ArmOperand DecodeA32Vn(uint word, int size)
    {
        var index = size == 4 ? (int)(((word >> 16) & 15) << 1 | ((word >> 7) & 1)) : (int)(((word >> 7) & 1) << 4 | ((word >> 16) & 15));
        return A64Vector((uint)index, size);
    }

    private static ArmOperand DecodeA32Vm(uint word, int size)
    {
        var index = size == 4 ? (int)((word & 15) << 1 | ((word >> 5) & 1)) : (int)(((word >> 5) & 1) << 4 | (word & 15));
        return A64Vector((uint)index, size);
    }

    private static ArmOperand A64Gpr(uint index, int size, bool allowSp)
        => ArmOperand.RegisterOperand(index == 31 ? allowSp ? ArmRegister.Sp : ArmRegister.Xzr : (ArmRegister)((int)ArmRegister.X0 + index), size);

    private static ArmRegister A64GprRegister(uint index)
        => index == 31 ? ArmRegister.Xzr : (ArmRegister)((int)ArmRegister.X0 + index);

    private static ArmRegister A64BaseRegister(uint index)
        => index == 31 ? ArmRegister.Sp : (ArmRegister)((int)ArmRegister.X0 + index);

    private static ArmOperand A64Vector(uint index, int size)
        => ArmOperand.RegisterOperand((ArmRegister)((int)ArmRegister.V0 + index), size);

    private static ArmOperand A32Gpr(uint index)
        => ArmOperand.RegisterOperand(A32GprRegister(index), 4);

    private static ArmRegister A32GprRegister(uint index)
        => (ArmRegister)index;

    private static ArmInstrKind GetDecodedLoadStoreOpcode(bool load, uint opc, int size)
    {
        if (!load)
            return size switch
            {
                1 => ArmInstrKind.Strb,
                2 => ArmInstrKind.Strh,
                _ => ArmInstrKind.Str,
            };
        if (opc == 1)
            return size switch
            {
                1 => ArmInstrKind.Ldrb,
                2 => ArmInstrKind.Ldrh,
                _ => ArmInstrKind.Ldr,
            };
        if (size == 4 && opc == 2)
            return ArmInstrKind.Ldrsw;
        if (size == 1)
            return ArmInstrKind.Ldrsb;
        if (size == 2)
            return ArmInstrKind.Ldrsh;
        return ArmInstrKind.Ldr;
    }

    private static ArmShiftKind DecodeA64Shift(uint value)
        => value switch
        {
            0 => ArmShiftKind.Lsl,
            1 => ArmShiftKind.Lsr,
            2 => ArmShiftKind.Asr,
            3 => ArmShiftKind.Ror,
            _ => ArmShiftKind.None,
        };

    private static ArmExtendKind DecodeA64Extend(uint value)
        => value switch
        {
            0 => ArmExtendKind.Uxtb,
            1 => ArmExtendKind.Uxth,
            2 => ArmExtendKind.Uxtw,
            3 => ArmExtendKind.Uxtx,
            4 => ArmExtendKind.Sxtb,
            5 => ArmExtendKind.Sxth,
            6 => ArmExtendKind.Sxtw,
            7 => ArmExtendKind.Sxtx,
            _ => ArmExtendKind.None,
        };

    private static (ArmShiftKind Kind, int Amount) DecodeA32Shift(uint type, int amount)
    {
        if (type == 0 && amount == 0)
            return (ArmShiftKind.None, 0);
        if (type == 3 && amount == 0)
            return (ArmShiftKind.Rrx, 1);
        if ((type == 1 || type == 2) && amount == 0)
            amount = 32;
        return (type switch { 0 => ArmShiftKind.Lsl, 1 => ArmShiftKind.Lsr, 2 => ArmShiftKind.Asr, _ => ArmShiftKind.Ror }, amount);
    }

    private static ArmSystemRegister DecodeSystemRegister(uint encoding)
        => encoding switch
        {
            0x000B4200u => ArmSystemRegister.Nzcv,
            0x000B4400u => ArmSystemRegister.Fpcr,
            0x000B4420u => ArmSystemRegister.Fpsr,
            0x000BD040u => ArmSystemRegister.TpidrEl0,
            0x000BD060u => ArmSystemRegister.TpidrroEl0,
            0x000CD080u => ArmSystemRegister.TpidrEl1,
            0x000C4100u => ArmSystemRegister.SpEl0,
            0x000C4020u => ArmSystemRegister.ElrEl1,
            0x000C4000u => ArmSystemRegister.SpsrEl1,
            0x00084240u => ArmSystemRegister.CurrentEl,
            0x000B4220u => ArmSystemRegister.Daif,
            _ => ArmSystemRegister.Invalid,
        };

    private static ulong DecodeLogicalImmediate(uint n, uint immr, uint imms, int width)
    {
        var combined = (n << 6) | ((~imms) & 0x3F);
        var length = HighestSetBit(combined);
        if (length < 1)
            return 0;
        var levels = (1u << length) - 1;
        var s = imms & levels;
        var r = immr & levels;
        if (s == levels)
            return 0;
        var elementSize = 1 << length;
        var element = (1UL << ((int)s + 1)) - 1;
        element = RotateRight(element, (int)r, elementSize);
        var result = 0UL;
        for (var shift = 0; shift < width; shift += elementSize)
            result |= element << shift;
        return width == 32 ? result & uint.MaxValue : result;
    }

    private static int HighestSetBit(uint value)
    {
        for (var bit = 31; bit >= 0; bit--)
        {
            if ((value & (1u << bit)) != 0)
                return bit;
        }
        return -1;
    }

    private static ulong RotateRight(ulong value, int amount, int width)
    {
        amount &= width - 1;
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        value &= mask;
        if (amount == 0)
            return value;
        return ((value >> amount) | (value << (width - amount))) & mask;
    }

    private static uint RotateRight32(uint value, int amount)
    {
        amount &= 31;
        return amount == 0 ? value : value >> amount | value << (32 - amount);
    }

    private static long SignExtend(uint value, int bits)
    {
        var shift = 64 - bits;
        return ((long)value << shift) >> shift;
    }

    private static int Log2(int value)
    {
        var result = 0;
        while ((1 << result) < value)
            result++;
        return result;
    }
}
