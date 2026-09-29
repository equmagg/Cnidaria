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
        return target.Is64Bit
            ? EncodeAArch64(instruction, pc, symbols)
            : EncodeAArch32(instruction, pc, symbols);
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

    private static uint EncodeAArch64(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        switch (instruction.Opcode)
        {
            case ArmInstrKind.Nop: return 0xD503201Fu;
            case ArmInstrKind.Yield: return 0xD503203Fu;
            case ArmInstrKind.Wfe: return 0xD503205Fu;
            case ArmInstrKind.Wfi: return 0xD503207Fu;
            case ArmInstrKind.Sev: return 0xD503209Fu;
            case ArmInstrKind.Sevl: return 0xD50320BFu;
            case ArmInstrKind.Ret: return 0xD65F0000u | ((uint)A64Register(instruction.Operand0.Kind == ArmOperandKind.None ? ArmRegister.X30 : instruction.Operand0.Register) << 5);
            case ArmInstrKind.Br: return 0xD61F0000u | ((uint)A64Register(instruction.Operand0.Register) << 5);
            case ArmInstrKind.Blr: return 0xD63F0000u | ((uint)A64Register(instruction.Operand0.Register) << 5);
            case ArmInstrKind.B:
            case ArmInstrKind.Bl:
                return EncodeA64Branch(instruction, pc, symbols);
            case ArmInstrKind.Cbz:
            case ArmInstrKind.Cbnz:
                return EncodeA64CompareBranch(instruction, pc, symbols);
            case ArmInstrKind.Tbz:
            case ArmInstrKind.Tbnz:
                return EncodeA64TestBranch(instruction, pc, symbols);
            case ArmInstrKind.Adr:
            case ArmInstrKind.Adrp:
                return EncodeA64Adr(instruction, pc, symbols);
            case ArmInstrKind.Add:
            case ArmInstrKind.Adds:
            case ArmInstrKind.Sub:
            case ArmInstrKind.Subs:
            case ArmInstrKind.Cmp:
            case ArmInstrKind.Cmn:
                return EncodeA64AddSub(instruction);
            case ArmInstrKind.Adc:
            case ArmInstrKind.Adcs:
            case ArmInstrKind.Sbc:
            case ArmInstrKind.Sbcs:
                return EncodeA64AddSubCarry(instruction);
            case ArmInstrKind.And:
            case ArmInstrKind.Ands:
            case ArmInstrKind.Orr:
            case ArmInstrKind.Eor:
            case ArmInstrKind.Bic:
            case ArmInstrKind.Bics:
            case ArmInstrKind.Tst:
                return EncodeA64Logical(instruction);
            case ArmInstrKind.Mov:
                return EncodeA64Mov(instruction);
            case ArmInstrKind.Mvn:
                return EncodeA64Mvn(instruction);
            case ArmInstrKind.Movz:
            case ArmInstrKind.Movn:
            case ArmInstrKind.Movk:
                return EncodeA64MoveWide(instruction);
            case ArmInstrKind.Lsl:
            case ArmInstrKind.Lsr:
            case ArmInstrKind.Asr:
            case ArmInstrKind.Ror:
                return EncodeA64Shift(instruction);
            case ArmInstrKind.Mul:
            case ArmInstrKind.Madd:
            case ArmInstrKind.Msub:
                return EncodeA64Multiply(instruction);
            case ArmInstrKind.Umulh:
            case ArmInstrKind.Smulh:
                return EncodeA64MultiplyHigh(instruction);
            case ArmInstrKind.Udiv:
            case ArmInstrKind.Sdiv:
                return EncodeA64Divide(instruction);
            case ArmInstrKind.Clz:
            case ArmInstrKind.Rbit:
            case ArmInstrKind.Rev:
            case ArmInstrKind.Rev16:
                return EncodeA64OneSource(instruction);
            case ArmInstrKind.Sxtb:
            case ArmInstrKind.Sxth:
            case ArmInstrKind.Sxtw:
            case ArmInstrKind.Uxtb:
            case ArmInstrKind.Uxth:
                return EncodeA64Extend(instruction);
            case ArmInstrKind.Ldr:
            case ArmInstrKind.Str:
            case ArmInstrKind.Ldrb:
            case ArmInstrKind.Strb:
            case ArmInstrKind.Ldrh:
            case ArmInstrKind.Strh:
            case ArmInstrKind.Ldrsb:
            case ArmInstrKind.Ldrsh:
            case ArmInstrKind.Ldrsw:
            case ArmInstrKind.Ldur:
            case ArmInstrKind.Stur:
                return EncodeA64LoadStore(instruction, pc, symbols);
            case ArmInstrKind.Ldp:
            case ArmInstrKind.Stp:
                return EncodeA64LoadStorePair(instruction);
            case ArmInstrKind.Ldaxr:
            case ArmInstrKind.Ldaxrb:
            case ArmInstrKind.Ldaxrh:
            case ArmInstrKind.Stlxr:
            case ArmInstrKind.Stlxrb:
            case ArmInstrKind.Stlxrh:
                return EncodeA64Exclusive(instruction);
            case ArmInstrKind.Svc:
                return 0xD4000001u | ((uint)RequireUnsigned(instruction.Operand0.Immediate, 16, "AArch64 SVC immediate") << 5);
            case ArmInstrKind.Brk:
            case ArmInstrKind.Bkpt:
                return 0xD4200000u | ((uint)RequireUnsigned(instruction.Operand0.Immediate, 16, "AArch64 BRK immediate") << 5);
            case ArmInstrKind.Mrs:
            case ArmInstrKind.Msr:
                return EncodeA64SystemRegister(instruction);
            case ArmInstrKind.Dmb:
            case ArmInstrKind.Dsb:
            case ArmInstrKind.Isb:
                return EncodeA64Barrier(instruction);
            case ArmInstrKind.Fmov:
            case ArmInstrKind.Fadd:
            case ArmInstrKind.Fsub:
            case ArmInstrKind.Fmul:
            case ArmInstrKind.Fdiv:
            case ArmInstrKind.Fsqrt:
            case ArmInstrKind.Fabs:
            case ArmInstrKind.Fneg:
            case ArmInstrKind.Fcmp:
            case ArmInstrKind.Scvtf:
            case ArmInstrKind.Ucvtf:
            case ArmInstrKind.Fcvtzs:
            case ArmInstrKind.Fcvtzu:
            case ArmInstrKind.Fcvt:
                return EncodeA64Floating(instruction);
            default:
                throw new NotSupportedException($"Unsupported AArch64 instruction: {instruction.Opcode}");
        }
    }

    private static uint EncodeA64Branch(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var displacement = ResolveDisplacement(instruction.Operand0, pc, symbols);
        RequireAligned(displacement, 4, "AArch64 branch displacement");
        var immediate = RequireSigned(displacement >> 2, 26, "AArch64 branch displacement");
        if (instruction.Condition != ArmCondition.Al && instruction.Opcode == ArmInstrKind.B)
        {
            var condImmediate = RequireSigned(displacement >> 2, 19, "AArch64 conditional branch displacement");
            return 0x54000000u | ((uint)condImmediate & 0x7FFFFu) << 5 | (uint)instruction.Condition;
        }
        return (instruction.Opcode == ArmInstrKind.Bl ? 0x94000000u : 0x14000000u) | ((uint)immediate & 0x03FFFFFFu);
    }

    private static uint EncodeA64CompareBranch(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var register = instruction.Operand0;
        var displacement = ResolveDisplacement(instruction.Operand1, pc, symbols);
        RequireAligned(displacement, 4, "AArch64 compare branch displacement");
        var immediate = RequireSigned(displacement >> 2, 19, "AArch64 compare branch displacement");
        var sf = RegisterSize(register, 8) == 8 ? 1u : 0u;
        return 0x34000000u | (sf << 31) | (instruction.Opcode == ArmInstrKind.Cbnz ? 1u << 24 : 0) | (((uint)immediate & 0x7FFFFu) << 5) | (uint)A64Register(register.Register);
    }

    private static uint EncodeA64TestBranch(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var register = instruction.Operand0;
        var bit = RequireUnsigned(instruction.Operand1.Immediate, 6, "AArch64 test bit");
        var displacement = ResolveDisplacement(instruction.Operand2, pc, symbols);
        RequireAligned(displacement, 4, "AArch64 test branch displacement");
        var immediate = RequireSigned(displacement >> 2, 14, "AArch64 test branch displacement");
        var size = RegisterSize(register, 8);
        if (size == 4 && bit >= 32)
            throw new ArgumentOutOfRangeException(nameof(instruction), "A W-register test bit must be below 32.");
        return 0x36000000u |
            (instruction.Opcode == ArmInstrKind.Tbnz ? 1u << 24 : 0) |
            ((uint)bit >> 5 << 31) |
            (((uint)bit & 31u) << 19) |
            (((uint)immediate & 0x3FFFu) << 5) |
            (uint)A64Register(register.Register);
    }

    private static uint EncodeA64Adr(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var destination = instruction.Operand0;
        var operand = instruction.Operand1;
        long displacement;
        if (instruction.Opcode == ArmInstrKind.Adrp)
        {
            if (operand.HasSymbol)
            {
                var target = ResolveAddress(operand, symbols);
                var targetPage = (long)(target & ~0xFFFUL);
                var pcPage = (long)(pc & ~0xFFFUL);
                displacement = (targetPage - pcPage) >> 12;
            }
            else
            {
                RequireAligned(operand.Immediate, 4096, "AArch64 ADRP displacement");
                displacement = operand.Immediate >> 12;
            }
        }
        else
        {
            displacement = ResolveDisplacement(operand, pc, symbols);
        }
        var immediate = RequireSigned(displacement, 21, "AArch64 ADR displacement");
        var immlo = (uint)immediate & 3u;
        var immhi = ((uint)immediate >> 2) & 0x7FFFFu;
        return (instruction.Opcode == ArmInstrKind.Adrp ? 0x90000000u : 0x10000000u) | (immlo << 29) | (immhi << 5) | (uint)A64Register(destination.Register);
    }

    private static uint EncodeA64AddSub(ArmInstruction instruction)
    {
        var opcode = instruction.Opcode;
        var compare = opcode is ArmInstrKind.Cmp or ArmInstrKind.Cmn;
        var destination = compare
            ? ArmOperand.RegisterOperand(ArmRegister.Xzr, RegisterSize(instruction.Operand0, 8))
            : instruction.Operand0;
        var source = compare ? instruction.Operand0 : instruction.Operand1;
        var operand = compare ? instruction.Operand1 : instruction.Operand2;
        var size = RegisterSize(destination, RegisterSize(source, 8));
        var sf = size == 8 ? 1u : 0u;
        var isSub = opcode is ArmInstrKind.Sub or ArmInstrKind.Subs or ArmInstrKind.Cmp;
        var setFlags = opcode is ArmInstrKind.Adds or ArmInstrKind.Subs or ArmInstrKind.Cmp or ArmInstrKind.Cmn || instruction.SetFlags;
        var rd = A64Register(destination.Register);
        var rn = A64Register(source.Register);

        if (operand.Kind == ArmOperandKind.Immediate)
        {
            var immediate = operand.Immediate;
            uint shift;
            if (immediate >= 0 && immediate <= 4095)
                shift = 0;
            else if (immediate >= 0 && (immediate & 0xFFF) == 0 && (immediate >> 12) <= 4095)
            {
                shift = 1;
                immediate >>= 12;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 add/sub immediate is not encodable.");
            return 0x11000000u | (sf << 31) | (isSub ? 1u << 30 : 0) | (setFlags ? 1u << 29 : 0) | (shift << 22) | ((uint)immediate << 10) | ((uint)rn << 5) | (uint)rd;
        }

        if (operand.Kind == ArmOperandKind.ExtendedRegister)
        {
            var option = EncodeA64Extend(operand.Extend);
            if (operand.ShiftAmount > 4)
                throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 extended register shift must be in 0..4.");
            return 0x0B200000u | (sf << 31) | (isSub ? 1u << 30 : 0) | (setFlags ? 1u << 29 : 0) |
                ((uint)A64Register(operand.Register) << 16) | (option << 13) | ((uint)operand.ShiftAmount << 10) | ((uint)rn << 5) | (uint)rd;
        }

        var shifted = AsShiftedRegister(operand);
        if (shifted.Shift == ArmShiftKind.Rrx)
            throw new NotSupportedException("AArch64 add/sub does not support RRX.");
        var shiftKind = EncodeA64ShiftKind(shifted.Shift);
        var maximum = size * 8 - 1;
        if (shifted.ShiftAmount > maximum)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 register shift is out of range.");
        return 0x0B000000u | (sf << 31) | (isSub ? 1u << 30 : 0) | (setFlags ? 1u << 29 : 0) |
            (shiftKind << 22) | ((uint)A64Register(shifted.Register) << 16) | ((uint)shifted.ShiftAmount << 10) | ((uint)rn << 5) | (uint)rd;
    }

    private static uint EncodeA64AddSubCarry(ArmInstruction instruction)
    {
        var size = RegisterSize(instruction.Operand0, 8);
        var sf = size == 8 ? 1u : 0u;
        var sub = instruction.Opcode is ArmInstrKind.Sbc or ArmInstrKind.Sbcs;
        var flags = instruction.Opcode is ArmInstrKind.Adcs or ArmInstrKind.Sbcs || instruction.SetFlags;
        return 0x1A000000u | (sf << 31) | (sub ? 1u << 30 : 0) | (flags ? 1u << 29 : 0) |
            ((uint)A64Register(instruction.Operand2.Register) << 16) |
            ((uint)A64Register(instruction.Operand1.Register) << 5) |
            (uint)A64Register(instruction.Operand0.Register);
    }

    private static uint EncodeA64Logical(ArmInstruction instruction)
    {
        var test = instruction.Opcode == ArmInstrKind.Tst;
        var destination = test
            ? ArmOperand.RegisterOperand(ArmRegister.Xzr, RegisterSize(instruction.Operand0, 8))
            : instruction.Operand0;
        var source = test ? instruction.Operand0 : instruction.Operand1;
        var operand = test ? instruction.Operand1 : instruction.Operand2;
        var size = RegisterSize(destination, RegisterSize(source, 8));
        var sf = size == 8 ? 1u : 0u;
        var opc = instruction.Opcode switch
        {
            ArmInstrKind.And => 0u,
            ArmInstrKind.Bic => 0u,
            ArmInstrKind.Orr => 1u,
            ArmInstrKind.Eor => 2u,
            ArmInstrKind.Ands => 3u,
            ArmInstrKind.Bics => 3u,
            ArmInstrKind.Tst => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var inverted = instruction.Opcode is ArmInstrKind.Bic or ArmInstrKind.Bics;
        var rd = A64Register(destination.Register);
        var rn = A64Register(source.Register);

        if (operand.Kind == ArmOperandKind.Immediate)
        {
            if (inverted)
                throw new NotSupportedException("AArch64 BIC immediate has no direct encoding.");
            if (!TryEncodeLogicalImmediate(unchecked((ulong)operand.Immediate), size * 8, out var n, out var immr, out var imms))
                throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 logical immediate is not encodable.");
            return 0x12000000u | (sf << 31) | (opc << 29) | (n << 22) | (immr << 16) | (imms << 10) | ((uint)rn << 5) | (uint)rd;
        }

        var shifted = AsShiftedRegister(operand);
        var shiftKind = EncodeA64ShiftKind(shifted.Shift);
        if (shifted.ShiftAmount >= size * 8)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 logical shift is out of range.");
        return 0x0A000000u | (sf << 31) | (opc << 29) | (shiftKind << 22) | (inverted ? 1u << 21 : 0) |
            ((uint)A64Register(shifted.Register) << 16) | ((uint)shifted.ShiftAmount << 10) | ((uint)rn << 5) | (uint)rd;
    }

    private static uint EncodeA64Mov(ArmInstruction instruction)
    {
        var destination = instruction.Operand0;
        var source = instruction.Operand1;
        var size = RegisterSize(destination, RegisterSize(source, 8));
        if (source.Kind == ArmOperandKind.Immediate)
        {
            var value = size == 4 ? unchecked((uint)source.Immediate) : unchecked((ulong)source.Immediate);
            if (TryEncodeMoveWide(value, size * 8, false, out var immediate, out var shift))
                return EncodeA64MoveWide(new ArmInstruction(ArmInstrKind.Movz, destination, ArmOperand.ImmediateOperand(immediate), ArmOperand.ImmediateOperand(shift)));
            if (TryEncodeMoveWide(value, size * 8, true, out immediate, out shift))
                return EncodeA64MoveWide(new ArmInstruction(ArmInstrKind.Movn, destination, ArmOperand.ImmediateOperand(immediate), ArmOperand.ImmediateOperand(shift)));
            if (TryEncodeLogicalImmediate(value, size * 8, out var n, out var immr, out var imms))
            {
                var sf = size == 8 ? 1u : 0u;
                return 0x12000000u | (sf << 31) | (1u << 29) | (n << 22) | (immr << 16) | (imms << 10) | (31u << 5) | (uint)A64Register(destination.Register);
            }
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 MOV immediate requires more than one instruction.");
        }

        var shifted = AsShiftedRegister(source);
        if (shifted.Shift != ArmShiftKind.None || shifted.ShiftAmount != 0)
            return EncodeA64Logical(new ArmInstruction(ArmInstrKind.Orr, destination, ArmOperand.RegisterOperand(ArmRegister.Xzr, size), shifted));
        var baseWord = size == 8 ? 0xAA0003E0u : 0x2A0003E0u;
        return baseWord | ((uint)A64Register(source.Register) << 16) | (uint)A64Register(destination.Register);
    }

    private static uint EncodeA64Mvn(ArmInstruction instruction)
    {
        var size = RegisterSize(instruction.Operand0, RegisterSize(instruction.Operand1, 8));
        var source = AsShiftedRegister(instruction.Operand1);
        var sf = size == 8 ? 1u : 0u;
        return 0x0A2003E0u | (sf << 31) | (1u << 29) | (EncodeA64ShiftKind(source.Shift) << 22) |
            ((uint)A64Register(source.Register) << 16) | ((uint)source.ShiftAmount << 10) | (uint)A64Register(instruction.Operand0.Register);
    }

    private static uint EncodeA64MoveWide(ArmInstruction instruction)
    {
        var destination = instruction.Operand0;
        var size = RegisterSize(destination, 8);
        var shift = instruction.Operand2.Kind == ArmOperandKind.None ? 0 : checked((int)instruction.Operand2.Immediate);
        if (shift < 0 || (shift & 15) != 0 || shift >= size * 8)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 move-wide shift is invalid.");
        var immediate = RequireUnsigned(instruction.Operand1.Immediate, 16, "AArch64 move-wide immediate");
        var opc = instruction.Opcode switch
        {
            ArmInstrKind.Movn => 0u,
            ArmInstrKind.Movz => 2u,
            ArmInstrKind.Movk => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var sf = size == 8 ? 1u : 0u;
        return 0x12800000u | (sf << 31) | (opc << 29) | ((uint)(shift / 16) << 21) | ((uint)immediate << 5) | (uint)A64Register(destination.Register);
    }

    private static uint EncodeA64Shift(ArmInstruction instruction)
    {
        var destination = instruction.Operand0;
        var source = instruction.Operand1;
        var amount = instruction.Operand2;
        var size = RegisterSize(destination, RegisterSize(source, 8));
        var sf = size == 8 ? 1u : 0u;
        var width = size * 8;
        if (amount.Kind == ArmOperandKind.Register)
        {
            var baseWord = instruction.Opcode switch
            {
                ArmInstrKind.Lsl => 0x1AC02000u,
                ArmInstrKind.Lsr => 0x1AC02400u,
                ArmInstrKind.Asr => 0x1AC02800u,
                ArmInstrKind.Ror => 0x1AC02C00u,
                _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
            };
            return baseWord | (sf << 31) | ((uint)A64Register(amount.Register) << 16) | ((uint)A64Register(source.Register) << 5) | (uint)A64Register(destination.Register);
        }

        var shift = checked((int)amount.Immediate);
        if (shift < 0 || shift >= width)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 shift amount is out of range.");
        if (instruction.Opcode == ArmInstrKind.Ror)
        {
            return 0x13800000u | (sf << 31) | (sf << 22) | ((uint)A64Register(source.Register) << 16) |
                ((uint)shift << 10) | ((uint)A64Register(source.Register) << 5) | (uint)A64Register(destination.Register);
        }
        var signed = instruction.Opcode == ArmInstrKind.Asr;
        var immr = instruction.Opcode == ArmInstrKind.Lsl ? (width - shift) & (width - 1) : shift;
        var imms = instruction.Opcode == ArmInstrKind.Lsl ? width - 1 - shift : width - 1;
        return (signed ? 0x13000000u : 0x53000000u) | (sf << 31) | (sf << 22) |
            ((uint)immr << 16) | ((uint)imms << 10) | ((uint)A64Register(source.Register) << 5) | (uint)A64Register(destination.Register);
    }

    private static uint EncodeA64Multiply(ArmInstruction instruction)
    {
        var size = RegisterSize(instruction.Operand0, 8);
        var sf = size == 8 ? 1u : 0u;
        var addend = instruction.Opcode == ArmInstrKind.Mul ? 31 : A64Register(instruction.Operand3.Register);
        var subtract = instruction.Opcode == ArmInstrKind.Msub;
        return 0x1B000000u | (sf << 31) | ((uint)A64Register(instruction.Operand2.Register) << 16) |
            (subtract ? 1u << 15 : 0) | ((uint)addend << 10) | ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(instruction.Operand0.Register);
    }

    // smulh/umulh take the top half of a 64 by 64 product, which is what a checked multiply compares
    private static uint EncodeA64MultiplyHigh(ArmInstruction instruction)
    {
        var baseWord = instruction.Opcode == ArmInstrKind.Smulh ? 0x9B407C00u : 0x9BC07C00u;
        return baseWord | ((uint)A64Register(instruction.Operand2.Register) << 16) |
            ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(instruction.Operand0.Register);
    }

    private static uint EncodeA64Divide(ArmInstruction instruction)
    {
        var size = RegisterSize(instruction.Operand0, 8);
        var sf = size == 8 ? 1u : 0u;
        var baseWord = instruction.Opcode == ArmInstrKind.Sdiv ? 0x1AC00C00u : 0x1AC00800u;
        return baseWord | (sf << 31) | ((uint)A64Register(instruction.Operand2.Register) << 16) |
            ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(instruction.Operand0.Register);
    }

    private static uint EncodeA64OneSource(ArmInstruction instruction)
    {
        var size = RegisterSize(instruction.Operand0, RegisterSize(instruction.Operand1, 8));
        var sf = size == 8 ? 1u : 0u;
        var op = instruction.Opcode switch
        {
            ArmInstrKind.Rbit => 0u,
            ArmInstrKind.Rev16 => 1u,
            ArmInstrKind.Rev => size == 8 ? 3u : 2u,
            ArmInstrKind.Clz => 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        return 0x5AC00000u | (sf << 31) | (op << 10) | ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(instruction.Operand0.Register);
    }

    // The extend mnemonics are bitfield moves that take the low bits of the source and fill the rest
    private static uint EncodeA64Extend(ArmInstruction instruction)
    {
        var signed = instruction.Opcode is ArmInstrKind.Sxtb or ArmInstrKind.Sxth or ArmInstrKind.Sxtw;
        var sourceBits = instruction.Opcode switch
        {
            ArmInstrKind.Sxtb or ArmInstrKind.Uxtb => 8,
            ArmInstrKind.Sxth or ArmInstrKind.Uxth => 16,
            ArmInstrKind.Sxtw => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var size = RegisterSize(instruction.Operand0, signed ? 8 : 4);
        if (sourceBits >= size * 8)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 extend does not widen the value.");

        var sf = size == 8 ? 1u : 0u;
        return (signed ? 0x13000000u : 0x53000000u) | (sf << 31) | (sf << 22) |
            ((uint)(sourceBits - 1) << 10) | ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(instruction.Operand0.Register);
    }

    private static uint EncodeA64LoadStore(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var target = instruction.Operand0;
        var address = instruction.Operand1;
        var load = instruction.Opcode is ArmInstrKind.Ldr or ArmInstrKind.Ldrb or ArmInstrKind.Ldrh or ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh or ArmInstrKind.Ldrsw or ArmInstrKind.Ldur;
        var explicitUnscaled = instruction.Opcode is ArmInstrKind.Ldur or ArmInstrKind.Stur;

        if (address.Kind == ArmOperandKind.Symbol || address.AddressingMode == ArmAddressingMode.Literal)
        {
            if (!load)
                throw new NotSupportedException("AArch64 literal stores are not supported.");
            var displacement = address.Kind == ArmOperandKind.Symbol
                ? ResolveDisplacement(address, pc, symbols)
                : address.Immediate;
            RequireAligned(displacement, 4, "AArch64 literal load displacement");
            var immediate = RequireSigned(displacement >> 2, 19, "AArch64 literal load displacement");
            uint baseWord;
            if (ArmRegisters.IsVector(target.Register))
                baseWord = target.Size == 8 ? 0x5C000000u : target.Size == 16 ? 0x9C000000u : 0x1C000000u;
            else if (instruction.Opcode == ArmInstrKind.Ldrsw)
                baseWord = 0x98000000u;
            else
                baseWord = RegisterSize(target, 8) == 8 ? 0x58000000u : 0x18000000u;
            return baseWord | (((uint)immediate & 0x7FFFFu) << 5) | (uint)A64Register(target.Register);
        }

        if (address.Kind != ArmOperandKind.Memory)
            throw new ArgumentException("AArch64 load/store requires a memory operand.", nameof(instruction));
        if (address.IndexRegister != ArmRegister.Invalid)
            return EncodeA64RegisterOffsetLoadStore(instruction, target, address, load);

        var dataSize = GetLoadStoreSize(instruction, target);
        var vector = ArmRegisters.IsVector(target.Register);
        var rt = A64Register(target.Register);
        var rn = A64Register(address.BaseRegister);
        var displacementValue = address.Immediate;
        var unscaled = explicitUnscaled || address.AddressingMode != ArmAddressingMode.Offset || displacementValue < 0 || displacementValue % dataSize != 0 || displacementValue / dataSize > 4095;
        var sizeCode = dataSize switch
        {
            1 => 0u,
            2 => 1u,
            4 => 2u,
            8 => 3u,
            16 => 0u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        uint opc;
        if (instruction.Opcode == ArmInstrKind.Ldrsw)
            opc = 2;
        else if (instruction.Opcode is ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh)
            opc = RegisterSize(target, 8) == 8 ? 2u : 3u;
        else
            opc = load ? 1u : 0u;

        if (!unscaled)
        {
            var imm12 = RequireUnsigned(displacementValue / dataSize, 12, "AArch64 load/store offset");
            var word = 0x39000000u | (sizeCode << 30) | (vector ? 1u << 26 : 0) | (opc << 22) |
                ((uint)imm12 << 10) | ((uint)rn << 5) | (uint)rt;
            if (dataSize == 16)
                word |= 1u << 23;
            return word;
        }

        var imm9 = RequireSigned(displacementValue, 9, "AArch64 unscaled load/store offset");
        var mode = address.AddressingMode switch
        {
            ArmAddressingMode.Offset => 0u,
            ArmAddressingMode.PostIndex => 1u,
            ArmAddressingMode.PreIndex => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var result = 0x38000000u | (sizeCode << 30) | (vector ? 1u << 26 : 0) | (opc << 22) |
            (((uint)imm9 & 0x1FFu) << 12) | (mode << 10) | ((uint)rn << 5) | (uint)rt;
        if (dataSize == 16)
            result |= 1u << 23;
        return result;
    }

    private static uint EncodeA64RegisterOffsetLoadStore(ArmInstruction instruction, ArmOperand target, ArmOperand address, bool load)
    {
        var dataSize = GetLoadStoreSize(instruction, target);
        var vector = ArmRegisters.IsVector(target.Register);
        var sizeCode = dataSize switch
        {
            1 => 0u,
            2 => 1u,
            4 => 2u,
            8 => 3u,
            16 => 0u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        uint opc;
        if (instruction.Opcode == ArmInstrKind.Ldrsw)
            opc = 2;
        else if (instruction.Opcode is ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh)
            opc = RegisterSize(target, 8) == 8 ? 2u : 3u;
        else
            opc = load ? 1u : 0u;
        var option = address.Extend switch
        {
            ArmExtendKind.Uxtw => 2u,
            ArmExtendKind.Sxtw => 6u,
            ArmExtendKind.Sxtx => 7u,
            ArmExtendKind.None or ArmExtendKind.Uxtx => 3u,
            _ => throw new NotSupportedException("Unsupported AArch64 load/store register extension."),
        };
        var scaled = address.ShiftAmount != 0;
        var expectedShift = Log2(dataSize);
        if (scaled && address.ShiftAmount != expectedShift)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 load/store register shift must match the access size.");
        var result = 0x38200800u | (sizeCode << 30) | (vector ? 1u << 26 : 0) | (opc << 22) |
            ((uint)A64Register(address.IndexRegister) << 16) | (option << 13) | (scaled ? 1u << 12 : 0) |
            ((uint)A64Register(address.BaseRegister) << 5) | (uint)A64Register(target.Register);
        if (dataSize == 16)
            result |= 1u << 23;
        return result;
    }

    // ldaxr Xt, [Xn] and stlxr Ws, Xt, [Xn]: the status register of a store is always 32 bit
    private static uint EncodeA64Exclusive(ArmInstruction instruction)
    {
        bool load = instruction.Opcode is ArmInstrKind.Ldaxr or ArmInstrKind.Ldaxrb or ArmInstrKind.Ldaxrh;
        ArmOperand data = load ? instruction.Operand0 : instruction.Operand1;
        ArmOperand address = load ? instruction.Operand1 : instruction.Operand2;
        if (address.Kind != ArmOperandKind.Memory || address.BaseRegister == ArmRegister.Invalid)
            throw new ArgumentException("AArch64 exclusive access requires a base-only memory operand.", nameof(instruction));
        if (address.Immediate != 0 || address.IndexRegister != ArmRegister.Invalid)
            throw new ArgumentException("AArch64 exclusive access does not take an offset.", nameof(instruction));

        uint size = instruction.Opcode is ArmInstrKind.Ldaxrb or ArmInstrKind.Stlxrb
            ? 0u
            : instruction.Opcode is ArmInstrKind.Ldaxrh or ArmInstrKind.Stlxrh
                ? 1u
                : RegisterSize(data, 8) == 8 ? 3u : 2u;
        uint status = load ? 31u : (uint)A64Register(instruction.Operand0.Register);
        return 0x08000000u | (size << 30) | (load ? 1u << 22 : 0) | (status << 16) | (1u << 15) |
            (31u << 10) | ((uint)A64Register(address.BaseRegister) << 5) | (uint)A64Register(data.Register);
    }

    private static uint EncodeA64LoadStorePair(ArmInstruction instruction)
    {
        var first = instruction.Operand0;
        var second = instruction.Operand1;
        var address = instruction.Operand2;
        if (address.Kind != ArmOperandKind.Memory)
            throw new ArgumentException("AArch64 LDP/STP requires a memory operand.", nameof(instruction));
        var size = RegisterSize(first, RegisterSize(second, 8));
        var vector = ArmRegisters.IsVector(first.Register);
        var scale = vector ? size : size;
        if (scale is not 4 and not 8 and not 16)
            throw new NotSupportedException("Unsupported AArch64 pair access size.");
        if (address.Immediate % scale != 0)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 pair offset is not aligned to the element size.");
        var immediate = RequireSigned(address.Immediate / scale, 7, "AArch64 pair offset");
        var load = instruction.Opcode == ArmInstrKind.Ldp;
        uint mode = address.AddressingMode switch
        {
            ArmAddressingMode.PostIndex => 1u,
            ArmAddressingMode.Offset => 2u,
            ArmAddressingMode.PreIndex => 3u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        uint opc;
        if (vector)
            opc = size == 4 ? 0u : size == 8 ? 1u : 2u;
        else
            opc = size == 8 ? 2u : 0u;
        return 0x28000000u | (opc << 30) | (vector ? 1u << 26 : 0) | (mode << 23) | (load ? 1u << 22 : 0) |
            (((uint)immediate & 0x7Fu) << 15) | ((uint)A64Register(second.Register) << 10) |
            ((uint)A64Register(address.BaseRegister) << 5) | (uint)A64Register(first.Register);
    }

    private static uint EncodeA64SystemRegister(ArmInstruction instruction)
    {
        var read = instruction.Opcode == ArmInstrKind.Mrs;
        var registerOperand = read ? instruction.Operand0 : instruction.Operand1;
        var systemOperand = read ? instruction.Operand1 : instruction.Operand0;
        var encoding = EncodeSystemRegister(systemOperand.SystemRegister);
        return (read ? 0xD5300000u : 0xD5100000u) | encoding | (uint)A64Register(registerOperand.Register);
    }

    private static uint EncodeA64Barrier(ArmInstruction instruction)
    {
        var option = instruction.Operand0.Kind == ArmOperandKind.None ? 15 : checked((int)instruction.Operand0.Immediate);
        if ((uint)option > 15)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch64 barrier option must be in 0..15.");
        return instruction.Opcode switch
        {
            ArmInstrKind.Dsb => 0xD503309Fu | ((uint)option << 8),
            ArmInstrKind.Dmb => 0xD50330BFu | ((uint)option << 8),
            ArmInstrKind.Isb => 0xD50330DFu | ((uint)option << 8),
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
    }

    private static uint EncodeA64Floating(ArmInstruction instruction)
    {
        var destination = instruction.Operand0;
        var size = RegisterSize(destination, instruction.Opcode == ArmInstrKind.Fcmp ? RegisterSize(instruction.Operand0, 8) : 8);
        if (instruction.Opcode is ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf)
        {
            var integer = instruction.Operand1;
            var sf = RegisterSize(integer, 8) == 8 ? 1u : 0u;
            var type = size == 8 ? 1u : 0u;
            var baseWord = instruction.Opcode == ArmInstrKind.Scvtf ? 0x1E220000u : 0x1E230000u;
            return baseWord | (sf << 31) | (type << 22) | ((uint)A64Register(integer.Register) << 5) | (uint)A64Register(destination.Register);
        }
        if (instruction.Opcode == ArmInstrKind.Fcvt)
        {
            var source = instruction.Operand1;
            var sourceType = RegisterSize(source, 8) == 8 ? 1u : 0u;
            var destinationType = RegisterSize(destination, 8) == 8 ? 1u : 0u;
            return 0x1E224000u | (sourceType << 22) | (destinationType << 15) |
                ((uint)A64Register(source.Register) << 5) | (uint)A64Register(destination.Register);
        }
        if (instruction.Opcode is ArmInstrKind.Fcvtzs or ArmInstrKind.Fcvtzu)
        {
            var integer = destination;
            var floating = instruction.Operand1;
            var sf = RegisterSize(integer, 8) == 8 ? 1u : 0u;
            var type = RegisterSize(floating, 8) == 8 ? 1u : 0u;
            var baseWord = instruction.Opcode == ArmInstrKind.Fcvtzs ? 0x1E380000u : 0x1E390000u;
            return baseWord | (sf << 31) | (type << 22) | ((uint)A64Register(floating.Register) << 5) | (uint)A64Register(integer.Register);
        }

        var typeBit = size == 8 ? 1u << 22 : 0;
        if (instruction.Opcode == ArmInstrKind.Fmov)
        {
            if (ArmRegisters.IsVector(destination.Register) && ArmRegisters.IsVector(instruction.Operand1.Register))
                return 0x1E204000u | typeBit | ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(destination.Register);
            var integer = ArmRegisters.IsVector(destination.Register) ? instruction.Operand1 : destination;
            var floating = ArmRegisters.IsVector(destination.Register) ? destination : instruction.Operand1;
            var sf = RegisterSize(integer, 8) == 8 ? 1u : 0u;
            var toFloat = ArmRegisters.IsVector(destination.Register);
            return 0x1E260000u | (sf << 31) | typeBit | (toFloat ? 1u << 16 : 0) |
                ((uint)A64Register(integer.Register) << (toFloat ? 5 : 0)) |
                ((uint)A64Register(floating.Register) << (toFloat ? 0 : 5));
        }
        if (instruction.Opcode is ArmInstrKind.Fsqrt or ArmInstrKind.Fabs or ArmInstrKind.Fneg)
        {
            var baseWord = instruction.Opcode switch
            {
                ArmInstrKind.Fsqrt => 0x1E21C000u,
                ArmInstrKind.Fabs => 0x1E20C000u,
                ArmInstrKind.Fneg => 0x1E214000u,
                _ => 0u,
            };
            return baseWord | typeBit | ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(destination.Register);
        }
        if (instruction.Opcode == ArmInstrKind.Fcmp)
        {
            return 0x1E202000u | typeBit | ((uint)A64Register(instruction.Operand1.Register) << 16) | ((uint)A64Register(destination.Register) << 5);
        }
        var binaryBase = instruction.Opcode switch
        {
            ArmInstrKind.Fmul => 0x1E200800u,
            ArmInstrKind.Fdiv => 0x1E201800u,
            ArmInstrKind.Fadd => 0x1E202800u,
            ArmInstrKind.Fsub => 0x1E203800u,
            _ => throw new NotSupportedException($"Unsupported AArch64 floating instruction: {instruction.Opcode}"),
        };
        return binaryBase | typeBit | ((uint)A64Register(instruction.Operand2.Register) << 16) |
            ((uint)A64Register(instruction.Operand1.Register) << 5) | (uint)A64Register(destination.Register);
    }

    private static uint EncodeAArch32(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var condition = (uint)instruction.Condition << 28;
        switch (instruction.Opcode)
        {
            case ArmInstrKind.Nop: return condition | 0x0320F000u;
            case ArmInstrKind.Yield: return condition | 0x0320F001u;
            case ArmInstrKind.Wfe: return condition | 0x0320F002u;
            case ArmInstrKind.Wfi: return condition | 0x0320F003u;
            case ArmInstrKind.Sev: return condition | 0x0320F004u;
            case ArmInstrKind.Sevl: return condition | 0x0320F005u;
            case ArmInstrKind.B:
            case ArmInstrKind.Bl:
                return EncodeA32Branch(instruction, pc, symbols);
            case ArmInstrKind.Ret:
                return condition | 0x012FFF10u | (uint)A32Register(instruction.Operand0.Kind == ArmOperandKind.None ? ArmRegister.Lr : instruction.Operand0.Register);
            case ArmInstrKind.Bx:
                return condition | 0x012FFF10u | (uint)A32Register(instruction.Operand0.Register);
            case ArmInstrKind.Blx:
                return condition | 0x012FFF30u | (uint)A32Register(instruction.Operand0.Register);
            case ArmInstrKind.Movw:
            case ArmInstrKind.Movt:
                return EncodeA32MoveWide(instruction);
            case ArmInstrKind.Mov:
            case ArmInstrKind.Mvn:
            case ArmInstrKind.Add:
            case ArmInstrKind.Adds:
            case ArmInstrKind.Adc:
            case ArmInstrKind.Adcs:
            case ArmInstrKind.Sub:
            case ArmInstrKind.Subs:
            case ArmInstrKind.Sbc:
            case ArmInstrKind.Sbcs:
            case ArmInstrKind.Rsb:
            case ArmInstrKind.And:
            case ArmInstrKind.Ands:
            case ArmInstrKind.Orr:
            case ArmInstrKind.Eor:
            case ArmInstrKind.Bic:
            case ArmInstrKind.Bics:
            case ArmInstrKind.Tst:
            case ArmInstrKind.Teq:
            case ArmInstrKind.Cmp:
            case ArmInstrKind.Cmn:
                return EncodeA32DataProcessing(instruction);
            case ArmInstrKind.Lsl:
            case ArmInstrKind.Lsr:
            case ArmInstrKind.Asr:
            case ArmInstrKind.Ror:
                return EncodeA32Shift(instruction);
            case ArmInstrKind.Mul:
            case ArmInstrKind.Mla:
            case ArmInstrKind.Mls:
            case ArmInstrKind.Umull:
            case ArmInstrKind.Smull:
                return EncodeA32Multiply(instruction);
            case ArmInstrKind.Udiv:
            case ArmInstrKind.Sdiv:
                return EncodeA32Divide(instruction);
            case ArmInstrKind.Clz:
            case ArmInstrKind.Rev:
            case ArmInstrKind.Rev16:
            case ArmInstrKind.Revsh:
            case ArmInstrKind.Rbit:
                return EncodeA32OneSource(instruction);
            case ArmInstrKind.Ldr:
            case ArmInstrKind.Str:
            case ArmInstrKind.Ldrb:
            case ArmInstrKind.Strb:
            case ArmInstrKind.Ldrh:
            case ArmInstrKind.Strh:
            case ArmInstrKind.Ldrsb:
            case ArmInstrKind.Ldrsh:
                return EncodeA32LoadStore(instruction);
            case ArmInstrKind.Ldm:
            case ArmInstrKind.Stm:
            case ArmInstrKind.Push:
            case ArmInstrKind.Pop:
                return EncodeA32Multiple(instruction);
            case ArmInstrKind.Svc:
                return condition | 0x0F000000u | (uint)RequireUnsigned(instruction.Operand0.Immediate, 24, "ARM SVC immediate");
            case ArmInstrKind.Bkpt:
            case ArmInstrKind.Brk:
                return EncodeA32Bkpt(instruction);
            case ArmInstrKind.Mrs:
            case ArmInstrKind.Msr:
                return EncodeA32SystemRegister(instruction);
            case ArmInstrKind.Dmb:
            case ArmInstrKind.Dsb:
            case ArmInstrKind.Isb:
                return EncodeA32Barrier(instruction);
            case ArmInstrKind.Fmov:
            case ArmInstrKind.Fadd:
            case ArmInstrKind.Fsub:
            case ArmInstrKind.Fmul:
            case ArmInstrKind.Fdiv:
            case ArmInstrKind.Fsqrt:
            case ArmInstrKind.Fabs:
            case ArmInstrKind.Fneg:
            case ArmInstrKind.Fcmp:
            case ArmInstrKind.Scvtf:
            case ArmInstrKind.Ucvtf:
            case ArmInstrKind.Fcvtzs:
            case ArmInstrKind.Fcvtzu:
            case ArmInstrKind.Fcvt:
                return EncodeA32Floating(instruction);
            default:
                throw new NotSupportedException($"Unsupported AArch32 instruction: {instruction.Opcode}");
        }
    }

    private static uint EncodeA32Branch(ArmInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
    {
        var displacement = ResolveDisplacement(instruction.Operand0, pc, symbols) - 8;
        RequireAligned(displacement, 4, "ARM branch displacement");
        var immediate = RequireSigned(displacement >> 2, 24, "ARM branch displacement");
        return ((uint)instruction.Condition << 28) | (instruction.Opcode == ArmInstrKind.Bl ? 0x0B000000u : 0x0A000000u) | ((uint)immediate & 0x00FFFFFFu);
    }

    private static uint EncodeA32MoveWide(ArmInstruction instruction)
    {
        var immediate = RequireUnsigned(instruction.Operand1.Immediate, 16, "ARM move-wide immediate");
        return ((uint)instruction.Condition << 28) | (instruction.Opcode == ArmInstrKind.Movt ? 0x03400000u : 0x03000000u) |
            (((uint)immediate & 0xF000u) << 4) | ((uint)A32Register(instruction.Operand0.Register) << 12) | ((uint)immediate & 0xFFFu);
    }

    private static uint EncodeA32DataProcessing(ArmInstruction instruction)
    {
        var opcode = instruction.Opcode;
        var test = opcode is ArmInstrKind.Tst or ArmInstrKind.Teq or ArmInstrKind.Cmp or ArmInstrKind.Cmn;
        var unary = opcode is ArmInstrKind.Mov or ArmInstrKind.Mvn;
        ArmOperand rd;
        ArmOperand rn;
        ArmOperand operand2;
        if (test)
        {
            rd = ArmOperand.RegisterOperand(ArmRegister.R0, 4);
            rn = instruction.Operand0;
            operand2 = instruction.Operand1;
        }
        else if (unary)
        {
            rd = instruction.Operand0;
            rn = ArmOperand.RegisterOperand(ArmRegister.R0, 4);
            operand2 = instruction.Operand1;
        }
        else
        {
            rd = instruction.Operand0;
            rn = instruction.Operand1;
            operand2 = instruction.Operand2;
        }
        var op = opcode switch
        {
            ArmInstrKind.And or ArmInstrKind.Ands => 0u,
            ArmInstrKind.Eor => 1u,
            ArmInstrKind.Sub or ArmInstrKind.Subs => 2u,
            ArmInstrKind.Rsb => 3u,
            ArmInstrKind.Add or ArmInstrKind.Adds => 4u,
            ArmInstrKind.Adc or ArmInstrKind.Adcs => 5u,
            ArmInstrKind.Sbc or ArmInstrKind.Sbcs => 6u,
            ArmInstrKind.Tst => 8u,
            ArmInstrKind.Teq => 9u,
            ArmInstrKind.Cmp => 10u,
            ArmInstrKind.Cmn => 11u,
            ArmInstrKind.Orr => 12u,
            ArmInstrKind.Mov => 13u,
            ArmInstrKind.Bic or ArmInstrKind.Bics => 14u,
            ArmInstrKind.Mvn => 15u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var setFlags = test || instruction.SetFlags || opcode is ArmInstrKind.Adds or ArmInstrKind.Adcs or ArmInstrKind.Subs or ArmInstrKind.Sbcs or ArmInstrKind.Ands or ArmInstrKind.Bics;
        var condition = (uint)instruction.Condition << 28;
        uint encodedOperand;
        uint immediateBit;
        if (operand2.Kind == ArmOperandKind.Immediate)
        {
            if (!TryEncodeA32Immediate(unchecked((uint)operand2.Immediate), out encodedOperand))
                throw new ArgumentOutOfRangeException(nameof(instruction), "ARM data-processing immediate is not encodable.");
            immediateBit = 1u << 25;
        }
        else
        {
            encodedOperand = EncodeA32ShiftedRegister(AsShiftedRegister(operand2));
            immediateBit = 0;
        }
        return condition | immediateBit | (op << 21) | (setFlags ? 1u << 20 : 0) |
            ((uint)A32Register(rn.Register) << 16) | ((uint)A32Register(rd.Register) << 12) | encodedOperand;
    }

    private static uint EncodeA32Shift(ArmInstruction instruction)
    {
        var shift = instruction.Opcode switch
        {
            ArmInstrKind.Lsl => ArmShiftKind.Lsl,
            ArmInstrKind.Lsr => ArmShiftKind.Lsr,
            ArmInstrKind.Asr => ArmShiftKind.Asr,
            ArmInstrKind.Ror => ArmShiftKind.Ror,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        var shifted = instruction.Operand2.Kind == ArmOperandKind.Register
            ? new ArmOperand()
            : ArmOperand.ShiftedRegister(instruction.Operand1.Register, shift, checked((int)instruction.Operand2.Immediate), 4);
        if (instruction.Operand2.Kind == ArmOperandKind.Register)
        {
            var type = EncodeA32ShiftKind(shift);
            var word = ((uint)instruction.Condition << 28) | 0x01A00010u |
                ((uint)A32Register(instruction.Operand0.Register) << 12) |
                ((uint)A32Register(instruction.Operand2.Register) << 8) |
                (type << 5) |
                (uint)A32Register(instruction.Operand1.Register);
            if (instruction.SetFlags)
                word |= 1u << 20;
            return word;
        }
        return EncodeA32DataProcessing(new ArmInstruction(ArmInstrKind.Mov, instruction.Operand0, shifted, condition: instruction.Condition, setFlags: instruction.SetFlags));
    }

    private static uint EncodeA32Multiply(ArmInstruction instruction)
    {
        var condition = (uint)instruction.Condition << 28;
        if (instruction.Opcode is ArmInstrKind.Umull or ArmInstrKind.Smull)
        {
            var signed = instruction.Opcode == ArmInstrKind.Smull;
            return condition | 0x00800090u | (signed ? 1u << 22 : 0) | (instruction.SetFlags ? 1u << 20 : 0) |
                ((uint)A32Register(instruction.Operand1.Register) << 16) |
                ((uint)A32Register(instruction.Operand0.Register) << 12) |
                ((uint)A32Register(instruction.Operand3.Register) << 8) |
                (uint)A32Register(instruction.Operand2.Register);
        }
        if (instruction.Opcode == ArmInstrKind.Mls)
        {
            return condition | 0x00600090u |
                ((uint)A32Register(instruction.Operand0.Register) << 16) |
                ((uint)A32Register(instruction.Operand3.Register) << 12) |
                ((uint)A32Register(instruction.Operand2.Register) << 8) |
                (uint)A32Register(instruction.Operand1.Register);
        }
        var accumulate = instruction.Opcode == ArmInstrKind.Mla;
        var addend = accumulate ? A32Register(instruction.Operand3.Register) : 0;
        return condition | (accumulate ? 1u << 21 : 0) | (instruction.SetFlags ? 1u << 20 : 0) |
            ((uint)A32Register(instruction.Operand0.Register) << 16) |
            ((uint)addend << 12) |
            ((uint)A32Register(instruction.Operand2.Register) << 8) |
            0x90u |
            (uint)A32Register(instruction.Operand1.Register);
    }

    private static uint EncodeA32Divide(ArmInstruction instruction)
    {
        var signed = instruction.Opcode == ArmInstrKind.Sdiv;
        return ((uint)instruction.Condition << 28) | (signed ? 0x0710F010u : 0x0730F010u) |
            ((uint)A32Register(instruction.Operand0.Register) << 16) |
            ((uint)A32Register(instruction.Operand2.Register) << 8) |
            (uint)A32Register(instruction.Operand1.Register);
    }

    private static uint EncodeA32OneSource(ArmInstruction instruction)
    {
        var condition = (uint)instruction.Condition << 28;
        var rd = (uint)A32Register(instruction.Operand0.Register) << 12;
        var rm = (uint)A32Register(instruction.Operand1.Register);
        return instruction.Opcode switch
        {
            ArmInstrKind.Clz => condition | 0x016F0F10u | rd | rm,
            ArmInstrKind.Rev => condition | 0x06BF0F30u | rd | rm,
            ArmInstrKind.Rev16 => condition | 0x06BF0FB0u | rd | rm,
            ArmInstrKind.Revsh => condition | 0x06FF0FB0u | rd | rm,
            ArmInstrKind.Rbit => condition | 0x06FF0F30u | rd | rm,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
    }

    private static uint EncodeA32LoadStore(ArmInstruction instruction)
    {
        var target = instruction.Operand0;
        var address = instruction.Operand1;
        if (address.Kind != ArmOperandKind.Memory)
            throw new ArgumentException("ARM load/store requires a memory operand.", nameof(instruction));
        var load = instruction.Opcode is ArmInstrKind.Ldr or ArmInstrKind.Ldrb or ArmInstrKind.Ldrh or ArmInstrKind.Ldrsb or ArmInstrKind.Ldrsh;
        if (ArmRegisters.IsVector(target.Register))
        {
            if (instruction.Opcode is not ArmInstrKind.Ldr and not ArmInstrKind.Str)
                throw new NotSupportedException("AArch32 VFP memory operands only support VLDR and VSTR.");
            if (address.IndexRegister != ArmRegister.Invalid || address.AddressingMode != ArmAddressingMode.Offset)
                throw new NotSupportedException("AArch32 VLDR and VSTR require immediate offset addressing.");
            if ((address.Immediate & 3) != 0 || address.Immediate < -1020 || address.Immediate > 1020)
                throw new ArgumentOutOfRangeException(nameof(instruction), "AArch32 VFP load/store offset is out of range or unaligned.");
            var size = RegisterSize(target, 4);
            if (size is not 4 and not 8)
                throw new ArgumentOutOfRangeException(nameof(instruction), "AArch32 VFP load/store register size must be 4 or 8 bytes.");
            var word = ((uint)instruction.Condition << 28) | (load ? 0x0D100A00u : 0x0D000A00u);
            if (address.Immediate >= 0)
                word |= 1u << 23;
            word |= (uint)A32Register(address.BaseRegister) << 16;
            word |= (uint)(Math.Abs(address.Immediate) >> 2);
            word = InsertA32Vd(word, target.Register, size);
            if (size == 8)
                word |= 1u << 8;
            return word;
        }
        var byteAccess = instruction.Opcode is ArmInstrKind.Ldrb or ArmInstrKind.Strb;
        var halfword = instruction.Opcode is ArmInstrKind.Ldrh or ArmInstrKind.Strh or ArmInstrKind.Ldrsh;
        var signedByte = instruction.Opcode == ArmInstrKind.Ldrsb;
        var signedHalf = instruction.Opcode == ArmInstrKind.Ldrsh;
        var pre = address.AddressingMode != ArmAddressingMode.PostIndex;
        var writeback = address.AddressingMode is ArmAddressingMode.PreIndex or ArmAddressingMode.PostIndex;
        var add = address.Immediate >= 0;
        var offset = Math.Abs(address.Immediate);
        var condition = (uint)instruction.Condition << 28;
        if (halfword || signedByte || signedHalf)
        {
            uint encodedOffset;
            uint immediateBit;
            if (address.IndexRegister != ArmRegister.Invalid)
            {
                encodedOffset = (uint)A32Register(address.IndexRegister);
                immediateBit = 0;
            }
            else
            {
                if (offset > 255)
                    throw new ArgumentOutOfRangeException(nameof(instruction), "ARM halfword/signed load offset is out of range.");
                encodedOffset = ((uint)offset & 0xF0u) << 4 | ((uint)offset & 0xFu);
                immediateBit = 1u << 22;
            }
            var operation = signedByte ? 2u : signedHalf ? 3u : 1u;
            return condition | (pre ? 1u << 24 : 0) | (add ? 1u << 23 : 0) | immediateBit |
                (writeback && pre ? 1u << 21 : 0) | (load ? 1u << 20 : 0) |
                ((uint)A32Register(address.BaseRegister) << 16) | ((uint)A32Register(target.Register) << 12) |
                0x90u | (operation << 5) | encodedOffset;
        }

        uint offsetEncoding;
        uint registerOffset;
        if (address.IndexRegister != ArmRegister.Invalid)
        {
            var shifted = ArmOperand.ShiftedRegister(address.IndexRegister, address.Shift, address.ShiftAmount, 4);
            offsetEncoding = EncodeA32ShiftedRegister(shifted);
            registerOffset = 1u << 25;
        }
        else
        {
            if (offset > 4095)
                throw new ArgumentOutOfRangeException(nameof(instruction), "ARM load/store offset is out of range.");
            offsetEncoding = (uint)offset;
            registerOffset = 0;
        }
        return condition | 0x04000000u | registerOffset | (pre ? 1u << 24 : 0) | (add ? 1u << 23 : 0) |
            (byteAccess ? 1u << 22 : 0) | (writeback && pre ? 1u << 21 : 0) | (load ? 1u << 20 : 0) |
            ((uint)A32Register(address.BaseRegister) << 16) | ((uint)A32Register(target.Register) << 12) | offsetEncoding;
    }

    private static uint EncodeA32Multiple(ArmInstruction instruction)
    {
        if (instruction.Opcode == ArmInstrKind.Push)
            return ((uint)instruction.Condition << 28) | 0x092D0000u | instruction.Operand0.RegisterMask;
        if (instruction.Opcode == ArmInstrKind.Pop)
            return ((uint)instruction.Condition << 28) | 0x08BD0000u | instruction.Operand0.RegisterMask;

        var baseRegister = instruction.Operand0;
        var registers = instruction.Operand1;
        var mode = instruction.Operand2.Kind == ArmOperandKind.Immediate ? checked((int)instruction.Operand2.Immediate) : 1;
        var p = mode is 2 or 3;
        var u = mode is 1 or 3;
        var load = instruction.Opcode == ArmInstrKind.Ldm;
        return ((uint)instruction.Condition << 28) | 0x08000000u | (p ? 1u << 24 : 0) | (u ? 1u << 23 : 0) |
            (instruction.SetFlags ? 1u << 21 : 0) | (load ? 1u << 20 : 0) |
            ((uint)A32Register(baseRegister.Register) << 16) | registers.RegisterMask;
    }

    private static uint EncodeA32Bkpt(ArmInstruction instruction)
    {
        var immediate = RequireUnsigned(instruction.Operand0.Immediate, 16, "ARM BKPT immediate");
        return ((uint)instruction.Condition << 28) | 0x01200070u | (((uint)immediate & 0xFFF0u) << 4) | ((uint)immediate & 0xFu);
    }

    private static uint EncodeA32SystemRegister(ArmInstruction instruction)
    {
        var condition = (uint)instruction.Condition << 28;
        if (instruction.Opcode == ArmInstrKind.Mrs)
        {
            var system = instruction.Operand1.SystemRegister;
            var baseWord = system switch
            {
                ArmSystemRegister.Cpsr => 0x010F0000u,
                ArmSystemRegister.Spsr => 0x014F0000u,
                _ => throw new NotSupportedException("AArch32 MRS only supports CPSR and SPSR."),
            };
            return condition | baseWord | ((uint)A32Register(instruction.Operand0.Register) << 12);
        }

        var target = instruction.Operand0.SystemRegister;
        var msrBase = target switch
        {
            ArmSystemRegister.Cpsr => 0x012FF000u,
            ArmSystemRegister.Spsr => 0x016FF000u,
            _ => throw new NotSupportedException("AArch32 MSR only supports CPSR and SPSR."),
        };
        return condition | msrBase | (uint)A32Register(instruction.Operand1.Register);
    }

    private static uint EncodeA32Barrier(ArmInstruction instruction)
    {
        var option = instruction.Operand0.Kind == ArmOperandKind.None ? 15 : checked((int)instruction.Operand0.Immediate);
        if ((uint)option > 15)
            throw new ArgumentOutOfRangeException(nameof(instruction), "AArch32 barrier option must be in 0..15.");
        var baseWord = instruction.Opcode switch
        {
            ArmInstrKind.Dsb => 0xF57FF040u,
            ArmInstrKind.Dmb => 0xF57FF050u,
            ArmInstrKind.Isb => 0xF57FF060u,
            _ => throw new ArgumentOutOfRangeException(nameof(instruction)),
        };
        return baseWord | (uint)option;
    }

    private static uint EncodeA32Floating(ArmInstruction instruction)
    {
        var condition = (uint)instruction.Condition << 28;
        var destination = instruction.Operand0;
        var size = RegisterSize(destination, 4);
        if (instruction.Opcode == ArmInstrKind.Fmov)
        {
            if (!ArmRegisters.IsVector(instruction.Operand1.Register))
                throw new NotSupportedException("AArch32 FMOV integer transfers are not implemented.");
            return EncodeA32VfpUnary(condition, 0x0EB00A40u, destination, instruction.Operand1, size);
        }
        if (instruction.Opcode is ArmInstrKind.Fsqrt or ArmInstrKind.Fabs or ArmInstrKind.Fneg)
        {
            var baseWord = instruction.Opcode switch
            {
                ArmInstrKind.Fsqrt => 0x0EB10AC0u,
                ArmInstrKind.Fabs => 0x0EB00AC0u,
                ArmInstrKind.Fneg => 0x0EB10A40u,
                _ => 0u,
            };
            return EncodeA32VfpUnary(condition, baseWord, destination, instruction.Operand1, size);
        }
        if (instruction.Opcode == ArmInstrKind.Fcmp)
        {
            var word = condition | 0x0EB40A40u;
            word = InsertA32Vd(word, destination.Register, size);
            word = InsertA32Vm(word, instruction.Operand1.Register, size);
            if (size == 8)
                word |= 1u << 8;
            return word;
        }
        if (instruction.Opcode is ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf or ArmInstrKind.Fcvtzs or ArmInstrKind.Fcvtzu)
            return EncodeA32VfpConvert(instruction, condition);
        if (instruction.Opcode == ArmInstrKind.Fcvt)
        {
            // The size bit names the source precision, and each register is numbered in its own
            var source = instruction.Operand1;
            var sourceSize = RegisterSize(source, 4);
            var word = condition | 0x0EB70AC0u;
            if (sourceSize == 8)
                word |= 1u << 8;
            word = InsertA32Vd(word, destination.Register, RegisterSize(destination, 4));
            word = InsertA32Vm(word, source.Register, sourceSize);
            return word;
        }

        var binaryBase = instruction.Opcode switch
        {
            ArmInstrKind.Fmul => 0x0E200A00u,
            ArmInstrKind.Fadd => 0x0E300A00u,
            ArmInstrKind.Fsub => 0x0E300A40u,
            ArmInstrKind.Fdiv => 0x0E800A00u,
            _ => throw new NotSupportedException($"Unsupported AArch32 floating instruction: {instruction.Opcode}"),
        };
        var result = condition | binaryBase;
        result = InsertA32Vd(result, destination.Register, size);
        result = InsertA32Vn(result, instruction.Operand1.Register, size);
        result = InsertA32Vm(result, instruction.Operand2.Register, size);
        if (size == 8)
            result |= 1u << 8;
        return result;
    }

    private static uint EncodeA32VfpUnary(uint condition, uint baseWord, ArmOperand destination, ArmOperand source, int size)
    {
        var result = condition | baseWord;
        result = InsertA32Vd(result, destination.Register, size);
        result = InsertA32Vm(result, source.Register, size);
        if (size == 8)
            result |= 1u << 8;
        return result;
    }

    private static uint EncodeA32VfpConvert(ArmInstruction instruction, uint condition)
    {
        var destination = instruction.Operand0;
        var source = instruction.Operand1;
        var toFloat = instruction.Opcode is ArmInstrKind.Scvtf or ArmInstrKind.Ucvtf;
        var unsigned = instruction.Opcode is ArmInstrKind.Ucvtf or ArmInstrKind.Fcvtzu;
        var floatSize = toFloat ? RegisterSize(destination, 4) : RegisterSize(source, 4);
        uint result;
        if (toFloat)
        {
            result = condition | (unsigned ? 0x0EB80A40u : 0x0EB80AC0u);
            result = InsertA32Vd(result, destination.Register, floatSize);
            result = InsertA32Vm(result, source.Register, 4);
        }
        else
        {
            result = condition | (unsigned ? 0x0EBC0AC0u : 0x0EBD0AC0u);
            result = InsertA32Vd(result, destination.Register, 4);
            result = InsertA32Vm(result, source.Register, floatSize);
        }
        if (floatSize == 8)
            result |= 1u << 8;
        return result;
    }

    private static uint InsertA32Vd(uint word, ArmRegister register, int size)
    {
        var index = ArmRegisters.Index(register);
        if (size == 4)
            return word | ((uint)(index >> 1) << 12) | ((uint)(index & 1) << 22);
        return word | ((uint)(index & 15) << 12) | ((uint)(index >> 4) << 22);
    }

    private static uint InsertA32Vn(uint word, ArmRegister register, int size)
    {
        var index = ArmRegisters.Index(register);
        if (size == 4)
            return word | ((uint)(index >> 1) << 16) | ((uint)(index & 1) << 7);
        return word | ((uint)(index & 15) << 16) | ((uint)(index >> 4) << 7);
    }

    private static uint InsertA32Vm(uint word, ArmRegister register, int size)
    {
        var index = ArmRegisters.Index(register);
        if (size == 4)
            return word | ((uint)(index >> 1)) | ((uint)(index & 1) << 5);
        return word | (uint)(index & 15) | ((uint)(index >> 4) << 5);
    }

    private static uint EncodeA32ShiftedRegister(ArmOperand operand)
    {
        var result = (uint)A32Register(operand.Register);
        if (operand.Shift == ArmShiftKind.None)
            return result;
        if (operand.Shift == ArmShiftKind.Rrx)
            return result | (3u << 5);
        if (operand.ShiftAmount < 0 || operand.ShiftAmount > 32)
            throw new ArgumentOutOfRangeException(nameof(operand), "ARM shift amount is out of range.");
        var amount = operand.ShiftAmount;
        if ((operand.Shift is ArmShiftKind.Lsr or ArmShiftKind.Asr) && amount == 32)
            amount = 0;
        if (operand.Shift == ArmShiftKind.Lsl && amount == 32)
            throw new ArgumentOutOfRangeException(nameof(operand), "ARM LSL immediate must be below 32.");
        return result | ((uint)amount << 7) | (EncodeA32ShiftKind(operand.Shift) << 5);
    }

    private static ArmOperand AsShiftedRegister(ArmOperand operand)
    {
        if (operand.Kind == ArmOperandKind.Register)
            return ArmOperand.ShiftedRegister(operand.Register, ArmShiftKind.None, 0, operand.Size);
        if (operand.Kind == ArmOperandKind.ShiftedRegister)
            return operand;
        throw new ArgumentException("Expected a register operand.", nameof(operand));
    }

    private static int GetLoadStoreSize(ArmInstruction instruction, ArmOperand target)
    {
        return instruction.Opcode switch
        {
            ArmInstrKind.Ldrb or ArmInstrKind.Strb or ArmInstrKind.Ldrsb => 1,
            ArmInstrKind.Ldrh or ArmInstrKind.Strh or ArmInstrKind.Ldrsh => 2,
            ArmInstrKind.Ldrsw => 4,
            _ => RegisterSize(target, 8),
        };
    }

    private static int RegisterSize(ArmOperand operand, int defaultSize)
        => operand.Size > 0 ? operand.Size : defaultSize;

    private static int A64Register(ArmRegister register)
    {
        if (register == ArmRegister.Sp || register == ArmRegister.Xzr)
            return 31;
        if (ArmRegisters.IsAArch64General(register) || ArmRegisters.IsVector(register))
            return ArmRegisters.Index(register);
        throw new ArgumentOutOfRangeException(nameof(register), "Expected an AArch64 register.");
    }

    private static int A32Register(ArmRegister register)
    {
        if (register == ArmRegister.Sp)
            return 13;
        if (ArmRegisters.IsAArch32General(register))
            return ArmRegisters.Index(register);
        throw new ArgumentOutOfRangeException(nameof(register), "Expected an AArch32 register.");
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

    private static long RequireSigned(long value, int bits, string name)
    {
        var minimum = -(1L << (bits - 1));
        var maximum = (1L << (bits - 1)) - 1;
        if (value < minimum || value > maximum)
            throw new OverflowException(name + " is out of range.");
        return value;
    }

    private static long RequireUnsigned(long value, int bits, string name)
    {
        var maximum = bits == 63 ? long.MaxValue : (1L << bits) - 1;
        if (value < 0 || value > maximum)
            throw new OverflowException(name + " is out of range.");
        return value;
    }

    private static void RequireAligned(long value, int alignment, string name)
    {
        if (value % alignment != 0)
            throw new ArgumentException(name + " is not aligned.");
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
        var widthMask = width == 64 ? ulong.MaxValue : uint.MaxValue;
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
