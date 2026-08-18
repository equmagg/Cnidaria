using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.X86
{
    internal static class X86CodeDecoder
    {
        public static X86Program Decode(byte[] bytes, X86Target target, ulong imageBase = 0)
        {
            var instructions = DecodeInstructions(bytes, target, imageBase);
            return new X86Program(target, instructions, null);
        }

        public static ImmutableArray<X86Instruction> DecodeInstructions(byte[] bytes, X86Target target, ulong imageBase = 0)
        {
            if (bytes is null)
                throw new ArgumentNullException(nameof(bytes));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var reader = new X86InstructionReader(bytes);
            var builder = ImmutableArray.CreateBuilder<X86Instruction>();
            while (!reader.End)
                builder.Add(DecodeOne(reader, target, imageBase));
            return builder.MoveToImmutable();
        }

        private static X86Instruction DecodeOne(X86InstructionReader reader, X86Target target, ulong imageBase)
        {
            var start = reader.Position;
            var prefix66 = false;
            byte scalarPrefix = 0;
            byte rex = 0;

            while (!reader.End)
            {
                var b = reader.PeekByte();
                if (b == 0x66)
                {
                    prefix66 = true;
                    reader.ReadByte();
                    continue;
                }
                if (b is 0xF2 or 0xF3)
                {
                    scalarPrefix = reader.ReadByte();
                    continue;
                }
                if (target.Is64Bit && b >= 0x40 && b <= 0x4F)
                {
                    rex = reader.ReadByte();
                    continue;
                }
                break;
            }

            if (reader.End)
                return RawFrom(reader.Bytes, start, reader.Position - start);

            if (reader.PeekByte() is 0xC4 or 0xC5)
                return DecodeVex(reader, target, start);

            var opcode = reader.ReadByte();
            var rexW = (rex & 0x08) != 0;
            var rexR = (rex & 0x04) != 0;
            var rexX = (rex & 0x02) != 0;
            var rexB = (rex & 0x01) != 0;
            var opSize = rexW ? 8 : prefix66 ? 2 : 4;

            try
            {
                if (opcode == 0x90)
                    return X86Instruction.Nop();
                if (opcode == 0xC3)
                    return X86Instruction.Ret();
                if (opcode == 0xC9)
                    return new X86Instruction(X86InstrKind.Leave);
                if (opcode == 0xCC)
                    return new X86Instruction(X86InstrKind.Int3);
                if (opcode == 0x98)
                    return new X86Instruction(rexW ? X86InstrKind.Cdqe : prefix66 ? X86InstrKind.Cbw : X86InstrKind.Cwde);
                if (opcode == 0x99)
                    return new X86Instruction(rexW ? X86InstrKind.Cqo : X86InstrKind.Cdq);
                if (opcode >= 0x50 && opcode <= 0x57)
                    return X86Instruction.Unary(X86InstrKind.Push, X86Operand.RegisterOperand(Gpr((opcode - 0x50) | (rexB ? 8 : 0)), target.XLen / 8));
                if (opcode >= 0x58 && opcode <= 0x5F)
                    return X86Instruction.Unary(X86InstrKind.Pop, X86Operand.RegisterOperand(Gpr((opcode - 0x58) | (rexB ? 8 : 0)), target.XLen / 8));
                if (opcode >= 0xB0 && opcode <= 0xB7)
                    return X86Instruction.Binary(X86InstrKind.Mov, X86Operand.RegisterOperand(Gpr((opcode - 0xB0) | (rexB ? 8 : 0)), 1), X86Operand.ImmediateOperand(reader.ReadInt8(), 1));
                if (opcode >= 0xB8 && opcode <= 0xBF)
                {
                    var size = opSize;
                    var value = size == 8 ? reader.ReadInt64() : size == 2 ? reader.ReadInt16() : reader.ReadInt32();
                    return X86Instruction.Binary(X86InstrKind.Mov, X86Operand.RegisterOperand(Gpr((opcode - 0xB8) | (rexB ? 8 : 0)), size), X86Operand.ImmediateOperand(value, size));
                }
                if (opcode is 0x88 or 0x89 or 0x8A or 0x8B)
                {
                    var size = (opcode & 1) == 0 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    var reg = X86Operand.RegisterOperand(Gpr(decoded.Reg), size);
                    if (opcode is 0x8A or 0x8B)
                        return X86Instruction.Binary(X86InstrKind.Mov, reg, decoded.Rm);
                    return X86Instruction.Binary(X86InstrKind.Mov, decoded.Rm, reg);
                }
                if (opcode is 0xC6 or 0xC7)
                {
                    var size = opcode == 0xC6 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    var value = size == 1 ? reader.ReadInt8() : size == 2 ? reader.ReadInt16() : reader.ReadInt32();
                    if (decoded.Reg == 0)
                        return X86Instruction.Binary(X86InstrKind.Mov, decoded.Rm, X86Operand.ImmediateOperand(value, size));
                }
                if (opcode == 0x8D)
                {
                    var decoded = DecodeModRm(reader, target, 0, rexR, rexX, rexB, vector: false);
                    return X86Instruction.Binary(X86InstrKind.Lea, X86Operand.RegisterOperand(Gpr(decoded.Reg), opSize), decoded.Rm.WithSize(0));
                }
                if (opcode == 0x63)
                {
                    var decoded = DecodeModRm(reader, target, 4, rexR, rexX, rexB, vector: false);
                    return X86Instruction.Binary(X86InstrKind.Movsxd, X86Operand.RegisterOperand(Gpr(decoded.Reg), rexW ? 8 : 4), decoded.Rm);
                }
                if (opcode is 0x00 or 0x01 or 0x08 or 0x09 or 0x10 or 0x11 or 0x18 or 0x19 or 0x20 or 0x21 or 0x28 or 0x29 or 0x30 or 0x31 or 0x38 or 0x39)
                {
                    var kind = BinaryFromOpcode(opcode, rmReg: true);
                    var size = (opcode & 1) == 0 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    return X86Instruction.Binary(kind, decoded.Rm, X86Operand.RegisterOperand(Gpr(decoded.Reg), size));
                }
                if (opcode is 0x02 or 0x03 or 0x0A or 0x0B or 0x12 or 0x13 or 0x1A or 0x1B or 0x22 or 0x23 or 0x2A or 0x2B or 0x32 or 0x33 or 0x3A or 0x3B)
                {
                    var kind = BinaryFromOpcode(opcode, rmReg: false);
                    var size = (opcode & 1) == 0 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    return X86Instruction.Binary(kind, X86Operand.RegisterOperand(Gpr(decoded.Reg), size), decoded.Rm);
                }
                if (opcode is 0x80 or 0x81 or 0x83)
                {
                    var size = opcode == 0x80 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    var immSize = opcode == 0x83 ? 1 : size == 8 ? 4 : size;
                    var value = immSize == 1 ? reader.ReadInt8() : immSize == 2 ? reader.ReadInt16() : reader.ReadInt32();
                    return X86Instruction.Binary(BinaryFromGroup(decoded.Reg), decoded.Rm, X86Operand.ImmediateOperand(value, immSize));
                }
                if (opcode is 0x84 or 0x85)
                {
                    var size = opcode == 0x84 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    return X86Instruction.Binary(X86InstrKind.Test, decoded.Rm, X86Operand.RegisterOperand(Gpr(decoded.Reg), size));
                }
                if (opcode is 0xF6 or 0xF7)
                {
                    var size = opcode == 0xF6 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    if (decoded.Reg == 0)
                    {
                        var value = size == 1 ? reader.ReadInt8() : size == 2 ? reader.ReadInt16() : reader.ReadInt32();
                        return X86Instruction.Binary(X86InstrKind.Test, decoded.Rm, X86Operand.ImmediateOperand(value, size));
                    }
                    return X86Instruction.Unary(UnaryFromGroup(decoded.Reg), decoded.Rm);
                }
                if (opcode is 0xFE or 0xFF)
                {
                    var size = opcode == 0xFE ? 1 : target.XLen / 8;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    return decoded.Reg switch
                    {
                        0 => X86Instruction.Unary(X86InstrKind.Inc, decoded.Rm),
                        1 => X86Instruction.Unary(X86InstrKind.Dec, decoded.Rm),
                        2 => X86Instruction.Branch(X86InstrKind.Call, decoded.Rm),
                        4 => X86Instruction.Branch(X86InstrKind.Jmp, decoded.Rm),
                        6 => X86Instruction.Unary(X86InstrKind.Push, decoded.Rm),
                        _ => RawFrom(reader.Bytes, start, reader.Position - start),
                    };
                }
                if (opcode is 0xD0 or 0xD1 or 0xD2 or 0xD3 or 0xC0 or 0xC1)
                {
                    var size = (opcode & 1) == 0 ? 1 : opSize;
                    var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                    var kind = ShiftFromGroup(decoded.Reg);
                    X86Operand count;
                    if (opcode is 0xD0 or 0xD1)
                        count = X86Operand.ImmediateOperand(1, 1);
                    else if (opcode is 0xD2 or 0xD3)
                        count = X86Operand.RegisterOperand(X86Register.Rcx, 1);
                    else
                        count = X86Operand.ImmediateOperand(reader.ReadInt8(), 1);
                    return X86Instruction.Binary(kind, decoded.Rm, count);
                }
                if (opcode == 0x69 || opcode == 0x6B)
                {
                    var decoded = DecodeModRm(reader, target, opSize, rexR, rexX, rexB, vector: false);
                    var imm = opcode == 0x6B ? reader.ReadInt8() : opSize == 2 ? reader.ReadInt16() : reader.ReadInt32();
                    return X86Instruction.Ternary(X86InstrKind.Imul, X86Operand.RegisterOperand(Gpr(decoded.Reg), opSize), decoded.Rm, X86Operand.ImmediateOperand(imm, opcode == 0x6B ? 1 : opSize));
                }
                if (opcode == 0xE8)
                    return X86Instruction.Branch(X86InstrKind.Call, X86Operand.ImmediateOperand(reader.ReadInt32(), 4));
                if (opcode == 0xE9)
                    return X86Instruction.Branch(X86InstrKind.Jmp, X86Operand.ImmediateOperand(reader.ReadInt32(), 4));
                if (opcode == 0xEB)
                    return X86Instruction.Branch(X86InstrKind.Jmp, X86Operand.ImmediateOperand(reader.ReadInt8(), 1));
                if (opcode >= 0x70 && opcode <= 0x7F)
                    return X86Instruction.ConditionalBranch((X86Condition)(opcode & 0xF), X86Operand.ImmediateOperand(reader.ReadInt8(), 1));

                if (opcode == 0x0F)
                    return DecodeTwoByte(reader, target, start, prefix66, scalarPrefix, rexW, rexR, rexX, rexB, opSize);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or InvalidOperationException or NotSupportedException or OverflowException)
            {
                return RawFrom(reader.Bytes, start, Math.Max(1, reader.Position - start));
            }

            return RawFrom(reader.Bytes, start, Math.Max(1, reader.Position - start));
        }

        private static X86Instruction DecodeTwoByte(X86InstructionReader reader, X86Target target, int start, bool prefix66, byte scalarPrefix, bool rexW, bool rexR, bool rexX, bool rexB, int opSize)
        {
            if (reader.End)
                return RawFrom(reader.Bytes, start, reader.Position - start);

            if (reader.PeekByte() is 0xC4 or 0xC5)
                return DecodeVex(reader, target, start);

            var opcode = reader.ReadByte();
            if (opcode == 0x0B)
                return new X86Instruction(X86InstrKind.Ud2);
            if (opcode == 0x05)
                return new X86Instruction(X86InstrKind.Syscall);
            if (opcode >= 0x80 && opcode <= 0x8F)
                return X86Instruction.ConditionalBranch((X86Condition)(opcode & 0xF), X86Operand.ImmediateOperand(reader.ReadInt32(), 4));
            if (opcode >= 0x90 && opcode <= 0x9F)
            {
                var decoded = DecodeModRm(reader, target, 1, rexR, rexX, rexB, vector: false);
                return X86Instruction.Setcc((X86Condition)(opcode & 0xF), decoded.Rm.WithSize(1));
            }
            if (opcode >= 0x40 && opcode <= 0x4F)
            {
                var decoded = DecodeModRm(reader, target, opSize, rexR, rexX, rexB, vector: false);
                return X86Instruction.Cmovcc((X86Condition)(opcode & 0xF), X86Operand.RegisterOperand(Gpr(decoded.Reg), opSize), decoded.Rm);
            }
            if (opcode == 0xAF)
            {
                var decoded = DecodeModRm(reader, target, opSize, rexR, rexX, rexB, vector: false);
                return X86Instruction.Binary(X86InstrKind.Imul, X86Operand.RegisterOperand(Gpr(decoded.Reg), opSize), decoded.Rm);
            }
            if (opcode is 0xB0 or 0xB1)
            {
                int size = opcode == 0xB0 ? 1 : opSize;
                var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                return X86Instruction.Binary(
                    X86InstrKind.Cmpxchg,
                    decoded.Rm,
                    X86Operand.RegisterOperand(Gpr(decoded.Reg), size));
            }
            if (opcode is 0xC0 or 0xC1)
            {
                int size = opcode == 0xC0 ? 1 : opSize;
                var decoded = DecodeModRm(reader, target, size, rexR, rexX, rexB, vector: false);
                return X86Instruction.Binary(
                    X86InstrKind.Xadd,
                    decoded.Rm,
                    X86Operand.RegisterOperand(Gpr(decoded.Reg), size));
            }
            if (opcode is 0xBE or 0xBF or 0xB6 or 0xB7)
            {
                var srcSize = opcode is 0xBE or 0xB6 ? 1 : 2;
                var decoded = DecodeModRm(reader, target, srcSize, rexR, rexX, rexB, vector: false);
                var kind = opcode is 0xBE or 0xBF ? X86InstrKind.Movsx : X86InstrKind.Movzx;
                return X86Instruction.Binary(kind, X86Operand.RegisterOperand(Gpr(decoded.Reg), opSize), decoded.Rm);
            }
            if (opcode == 0x10 || opcode == 0x11 || opcode is 0x58 or 0x5C or 0x59 or 0x5E or 0x2E or 0x51 or 0x5A or 0x2A or 0x2C)
            {
                var effectiveSimdPrefix = scalarPrefix != 0 ? scalarPrefix : prefix66 ? (byte)0x66 : (byte)0;
                var scalarKind = ScalarKind(opcode, effectiveSimdPrefix);
                if (scalarKind != X86InstrKind.Invalid)
                {
                    if (opcode == 0x11)
                    {
                        var decoded = DecodeModRm(reader, target, effectiveSimdPrefix == 0xF2 || effectiveSimdPrefix == 0x66 && opcode == 0x2E ? 8 : 4, rexR, rexX, rexB, vector: false);
                        return X86Instruction.Binary(scalarKind, decoded.Rm, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16));
                    }
                    if (opcode == 0x2A)
                    {
                        var decoded = DecodeModRm(reader, target, rexW ? 8 : 4, rexR, rexX, rexB, vector: false);
                        return X86Instruction.Binary(scalarKind, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16), decoded.Rm);
                    }
                    if (opcode == 0x2C)
                    {
                        var decoded = DecodeModRm(reader, target, effectiveSimdPrefix == 0xF2 || effectiveSimdPrefix == 0x66 && opcode == 0x2E ? 8 : 4, rexR, rexX, rexB, vector: true);
                        return X86Instruction.Binary(scalarKind, X86Operand.RegisterOperand(Gpr(decoded.Reg), rexW ? 8 : 4), decoded.Rm);
                    }
                    else
                    {
                        var decoded = DecodeModRm(reader, target, effectiveSimdPrefix == 0xF2 || effectiveSimdPrefix == 0x66 && opcode == 0x2E ? 8 : 4, rexR, rexX, rexB, vector: true);
                        return X86Instruction.Binary(scalarKind, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16), decoded.Rm);
                    }
                }
                var packedKind = PackedSseKind(opcode, effectiveSimdPrefix);
                if (packedKind != X86InstrKind.Invalid)
                {
                    var decoded = DecodeModRm(reader, target, 16, rexR, rexX, rexB, vector: true);
                    if (opcode is 0x11 or 0x29 or 0x7F)
                        return X86Instruction.Binary(packedKind, decoded.Rm, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16));
                    return X86Instruction.Binary(packedKind, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16), decoded.Rm);
                }
            }
            else
            {
                var packedKind = PackedSseKind(opcode, prefix66 ? (byte)0x66 : scalarPrefix);
                if (packedKind != X86InstrKind.Invalid)
                {
                    var decoded = DecodeModRm(reader, target, 16, rexR, rexX, rexB, vector: true);
                    if (opcode is 0x11 or 0x29 or 0x7F)
                        return X86Instruction.Binary(packedKind, decoded.Rm, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16));
                    return X86Instruction.Binary(packedKind, X86Operand.RegisterOperand(Xmm(decoded.Reg), 16), decoded.Rm);
                }
            }
            return RawFrom(reader.Bytes, start, reader.Position - start);
        }

        private static DecodedModRm DecodeModRm(X86InstructionReader reader, X86Target target, int size, bool rexR, bool rexX, bool rexB, bool vector, int vectorSize = 16)
        {
            var modRm = reader.ReadByte();
            var mod = (modRm >> 6) & 3;
            var reg = ((modRm >> 3) & 7) | (rexR ? 8 : 0);
            var rm = (modRm & 7) | (rexB ? 8 : 0);

            if (mod == 3)
            {
                var register = vector ? X86Operand.RegisterOperand(VectorRegister(rm, vectorSize), vectorSize) : X86Operand.RegisterOperand(Gpr(rm), size);
                return new DecodedModRm(reg, register);
            }

            X86Register baseRegister = X86Register.Invalid;
            X86Register indexRegister = X86Register.Invalid;
            var scale = 1;
            long displacement = 0;
            var ripRelative = false;

            if ((modRm & 7) == 4)
            {
                var sib = reader.ReadByte();
                scale = 1 << ((sib >> 6) & 3);
                var index = ((sib >> 3) & 7) | (rexX ? 8 : 0);
                var b = (sib & 7) | (rexB ? 8 : 0);
                if ((index & 7) != 4)
                    indexRegister = Gpr(index);
                if (mod == 0 && (b & 7) == 5)
                {
                    displacement = reader.ReadInt32();
                }
                else
                {
                    baseRegister = Gpr(b);
                }
            }
            else if (mod == 0 && (modRm & 7) == 5)
            {
                displacement = reader.ReadInt32();
                if (target.Is64Bit)
                {
                    baseRegister = X86Register.Rip;
                    ripRelative = true;
                }
            }
            else
            {
                baseRegister = Gpr(rm);
            }

            if (mod == 1)
                displacement = reader.ReadInt8();
            else if (mod == 2)
                displacement = reader.ReadInt32();

            return new DecodedModRm(reg, X86Operand.Memory(baseRegister, displacement, size, indexRegister, scale, null, X86ObjectRelocationKind.None, 0, ripRelative));
        }

        private static X86InstrKind BinaryFromOpcode(byte opcode, bool rmReg)
        {
            var group = (opcode >> 3) & 7;
            return BinaryFromGroup(group);
        }

        private static X86InstrKind BinaryFromGroup(int group)
        {
            return group switch
            {
                0 => X86InstrKind.Add,
                1 => X86InstrKind.Or,
                2 => X86InstrKind.Adc,
                3 => X86InstrKind.Sbb,
                4 => X86InstrKind.And,
                5 => X86InstrKind.Sub,
                6 => X86InstrKind.Xor,
                7 => X86InstrKind.Cmp,
                _ => X86InstrKind.Invalid,
            };
        }

        private static X86InstrKind UnaryFromGroup(int group)
        {
            return group switch
            {
                2 => X86InstrKind.Not,
                3 => X86InstrKind.Neg,
                4 => X86InstrKind.Mul,
                5 => X86InstrKind.Imul,
                6 => X86InstrKind.Div,
                7 => X86InstrKind.Idiv,
                _ => X86InstrKind.Invalid,
            };
        }

        private static X86InstrKind ShiftFromGroup(int group)
        {
            return group switch
            {
                0 => X86InstrKind.Rol,
                1 => X86InstrKind.Ror,
                4 => X86InstrKind.Shl,
                5 => X86InstrKind.Shr,
                7 => X86InstrKind.Sar,
                _ => X86InstrKind.Invalid,
            };
        }


        private static X86InstrKind PackedSseKind(byte opcode, byte prefix)
        {
            return (prefix, opcode) switch
            {
                (0, 0x10) or (0, 0x11) => X86InstrKind.Movups,
                (0, 0x28) or (0, 0x29) => X86InstrKind.Movaps,
                (0x66, 0x10) or (0x66, 0x11) => X86InstrKind.Movupd,
                (0x66, 0x28) or (0x66, 0x29) => X86InstrKind.Movapd,
                (0x66, 0x6F) or (0x66, 0x7F) => X86InstrKind.Movdqa,
                (0xF3, 0x6F) or (0xF3, 0x7F) => X86InstrKind.Movdqu,
                (0, 0x58) => X86InstrKind.Addps,
                (0x66, 0x58) => X86InstrKind.Addpd,
                (0, 0x5C) => X86InstrKind.Subps,
                (0x66, 0x5C) => X86InstrKind.Subpd,
                (0, 0x59) => X86InstrKind.Mulps,
                (0x66, 0x59) => X86InstrKind.Mulpd,
                (0, 0x5E) => X86InstrKind.Divps,
                (0x66, 0x5E) => X86InstrKind.Divpd,
                (0, 0x51) => X86InstrKind.Sqrtps,
                (0x66, 0x51) => X86InstrKind.Sqrtpd,
                (0, 0x54) => X86InstrKind.Andps,
                (0x66, 0x54) => X86InstrKind.Andpd,
                (0, 0x56) => X86InstrKind.Orps,
                (0x66, 0x56) => X86InstrKind.Orpd,
                (0, 0x57) => X86InstrKind.Xorps,
                (0x66, 0x57) => X86InstrKind.Xorpd,
                (0x66, 0xEF) => X86InstrKind.Pxor,
                _ => X86InstrKind.Invalid,
            };
        }

        private static X86InstrKind ScalarKind(byte opcode, byte prefix)
        {
            if (opcode == 0x2E)
                return prefix == 0x66 ? X86InstrKind.Ucomisd : prefix == 0 ? X86InstrKind.Ucomiss : X86InstrKind.Invalid;
            var isDouble = prefix == 0xF2;
            var isSingle = prefix == 0xF3;
            if (!isDouble && !isSingle)
                return X86InstrKind.Invalid;
            return opcode switch
            {
                0x10 or 0x11 => isDouble ? X86InstrKind.Movsd : X86InstrKind.Movss,
                0x58 => isDouble ? X86InstrKind.Addsd : X86InstrKind.Addss,
                0x5C => isDouble ? X86InstrKind.Subsd : X86InstrKind.Subss,
                0x59 => isDouble ? X86InstrKind.Mulsd : X86InstrKind.Mulss,
                0x5E => isDouble ? X86InstrKind.Divsd : X86InstrKind.Divss,
                0x2A => isDouble ? X86InstrKind.Cvtsi2sd : X86InstrKind.Cvtsi2ss,
                0x5A => isDouble ? X86InstrKind.Cvtsd2ss : X86InstrKind.Cvtss2sd,
                0x2C => isDouble ? X86InstrKind.Cvttsd2si : X86InstrKind.Cvttss2si,
                0x51 => isDouble ? X86InstrKind.Sqrtsd : X86InstrKind.Sqrtss,
                _ => X86InstrKind.Invalid,
            };
        }


        private static X86Instruction DecodeVex(X86InstructionReader reader, X86Target target, int start)
        {
            var prefix = reader.ReadByte();
            bool rexR;
            bool rexX;
            bool rexB;
            bool vexW;
            int map;
            int vvvv;
            int vectorLength;
            int pp;
            if (prefix == 0xC5)
            {
                var b = reader.ReadByte();
                rexR = (b & 0x80) == 0;
                rexX = false;
                rexB = false;
                vexW = false;
                map = 1;
                vvvv = (~(b >> 3)) & 0xF;
                vectorLength = (b >> 2) & 1;
                pp = b & 3;
            }
            else
            {
                var b1 = reader.ReadByte();
                var b2 = reader.ReadByte();
                rexR = (b1 & 0x80) == 0;
                rexX = (b1 & 0x40) == 0;
                rexB = (b1 & 0x20) == 0;
                map = b1 & 0x1F;
                vexW = (b2 & 0x80) != 0;
                vvvv = (~(b2 >> 3)) & 0xF;
                vectorLength = (b2 >> 2) & 1;
                pp = b2 & 3;
            }

            var opcode = reader.ReadByte();
            if (opcode == 0x77 && map == 1 && pp == 0)
                return new X86Instruction(vectorLength == 0 ? X86InstrKind.Vzeroupper : X86InstrKind.Vzeroall);

            var kind = VexKind(map, pp, opcode);
            if (kind == X86InstrKind.Invalid)
                return RawFrom(reader.Bytes, start, reader.Position - start);

            var vectorSize = vectorLength == 0 ? 16 : 32;
            var scalar = IsVexScalar(kind);
            if (IsVexMove(kind) && IsVexStoreOpcode(kind, opcode))
            {
                var decoded = DecodeModRm(reader, target, vectorSize, rexR, rexX, rexB, vector: false);
                return X86Instruction.Binary(kind, decoded.Rm, X86Operand.RegisterOperand(VectorRegister(decoded.Reg, vectorSize), vectorSize));
            }
            if (IsVexMove(kind))
            {
                var decoded = DecodeModRm(reader, target, vectorSize, rexR, rexX, rexB, vector: true, vectorSize: vectorSize);
                return X86Instruction.Binary(kind, X86Operand.RegisterOperand(VectorRegister(decoded.Reg, vectorSize), vectorSize), decoded.Rm);
            }
            if (IsVexBinary(kind))
            {
                var rmVector = !IsVexIntegerDestination(kind);
                var decoded = DecodeModRm(reader, target, scalar ? (kind == X86InstrKind.Vucomisd || kind == X86InstrKind.Vcvttsd2si ? 8 : 4) : vectorSize, rexR, rexX, rexB, vector: rmVector, vectorSize: vectorSize);
                var destination = IsVexIntegerDestination(kind)
                    ? X86Operand.RegisterOperand(Gpr(decoded.Reg), vexW ? 8 : 4)
                    : X86Operand.RegisterOperand(VectorRegister(decoded.Reg, scalar ? 16 : vectorSize), scalar ? 16 : vectorSize);
                return X86Instruction.Binary(kind, destination, decoded.Rm);
            }
            else
            {
                var decoded = DecodeModRm(reader, target, scalar && (kind == X86InstrKind.Vcvtsi2sd) ? 8 : scalar ? 4 : vectorSize, rexR, rexX, rexB, vector: !IsVexIntegerSource(kind), vectorSize: vectorSize);
                var destination = X86Operand.RegisterOperand(VectorRegister(decoded.Reg, scalar ? 16 : vectorSize), scalar ? 16 : vectorSize);
                var source0 = X86Operand.RegisterOperand(VectorRegister(vvvv, scalar ? 16 : vectorSize), scalar ? 16 : vectorSize);
                return X86Instruction.Ternary(kind, destination, source0, decoded.Rm);
            }
        }

        private static X86InstrKind VexKind(int map, int prefix, byte opcode)
        {
            if (map == 1)
            {
                if (prefix == 0)
                {
                    return opcode switch
                    {
                        0x10 or 0x11 => X86InstrKind.Vmovups,
                        0x28 or 0x29 => X86InstrKind.Vmovaps,
                        0x58 => X86InstrKind.Vaddps,
                        0x5C => X86InstrKind.Vsubps,
                        0x59 => X86InstrKind.Vmulps,
                        0x5E => X86InstrKind.Vdivps,
                        0x51 => X86InstrKind.Vsqrtps,
                        0x54 => X86InstrKind.Vandps,
                        0x56 => X86InstrKind.Vorps,
                        0x57 => X86InstrKind.Vxorps,
                        0x2E => X86InstrKind.Vucomiss,
                        _ => X86InstrKind.Invalid,
                    };
                }
                if (prefix == 1)
                {
                    return opcode switch
                    {
                        0x10 or 0x11 => X86InstrKind.Vmovupd,
                        0x28 or 0x29 => X86InstrKind.Vmovapd,
                        0x6F or 0x7F => X86InstrKind.Vmovdqa,
                        0x58 => X86InstrKind.Vaddpd,
                        0x5C => X86InstrKind.Vsubpd,
                        0x59 => X86InstrKind.Vmulpd,
                        0x5E => X86InstrKind.Vdivpd,
                        0x51 => X86InstrKind.Vsqrtpd,
                        0x54 => X86InstrKind.Vandpd,
                        0x56 => X86InstrKind.Vorpd,
                        0x57 => X86InstrKind.Vxorpd,
                        0x2E => X86InstrKind.Vucomisd,
                        0xDB => X86InstrKind.Vpand,
                        0xEB => X86InstrKind.Vpor,
                        0xEF => X86InstrKind.Vpxor,
                        0xFC => X86InstrKind.Vpaddb,
                        0xFD => X86InstrKind.Vpaddw,
                        0xFE => X86InstrKind.Vpaddd,
                        0xD4 => X86InstrKind.Vpaddq,
                        0xF8 => X86InstrKind.Vpsubb,
                        0xF9 => X86InstrKind.Vpsubw,
                        0xFA => X86InstrKind.Vpsubd,
                        0xFB => X86InstrKind.Vpsubq,
                        0x74 => X86InstrKind.Vpcmpeqb,
                        0x75 => X86InstrKind.Vpcmpeqw,
                        0x76 => X86InstrKind.Vpcmpeqd,
                        0x64 => X86InstrKind.Vpcmpgtb,
                        0x65 => X86InstrKind.Vpcmpgtw,
                        0x66 => X86InstrKind.Vpcmpgtd,
                        0xF2 => X86InstrKind.Vpslld,
                        0xF3 => X86InstrKind.Vpsllq,
                        0xD2 => X86InstrKind.Vpsrld,
                        0xD3 => X86InstrKind.Vpsrlq,
                        0xE2 => X86InstrKind.Vpsrad,
                        _ => X86InstrKind.Invalid,
                    };
                }
                if (prefix == 2)
                {
                    return opcode switch
                    {
                        0x6F or 0x7F => X86InstrKind.Vmovdqu,
                        0x58 => X86InstrKind.Vaddss,
                        0x5C => X86InstrKind.Vsubss,
                        0x59 => X86InstrKind.Vmulss,
                        0x5E => X86InstrKind.Vdivss,
                        0x51 => X86InstrKind.Vsqrtss,
                        0x2A => X86InstrKind.Vcvtsi2ss,
                        0x2C => X86InstrKind.Vcvttss2si,
                        _ => X86InstrKind.Invalid,
                    };
                }
                if (prefix == 3)
                {
                    return opcode switch
                    {
                        0x58 => X86InstrKind.Vaddsd,
                        0x5C => X86InstrKind.Vsubsd,
                        0x59 => X86InstrKind.Vmulsd,
                        0x5E => X86InstrKind.Vdivsd,
                        0x51 => X86InstrKind.Vsqrtsd,
                        0x2A => X86InstrKind.Vcvtsi2sd,
                        0x2C => X86InstrKind.Vcvttsd2si,
                        _ => X86InstrKind.Invalid,
                    };
                }
            }
            if (map == 2 && prefix == 1)
            {
                return opcode switch
                {
                    0x40 => X86InstrKind.Vpmulld,
                    0x29 => X86InstrKind.Vpcmpeqq,
                    0x37 => X86InstrKind.Vpcmpgtq,
                    _ => X86InstrKind.Invalid,
                };
            }
            return X86InstrKind.Invalid;
        }

        private static bool IsVexMove(X86InstrKind kind)
            => kind is X86InstrKind.Vmovaps or X86InstrKind.Vmovups or X86InstrKind.Vmovapd or X86InstrKind.Vmovupd or X86InstrKind.Vmovdqa or X86InstrKind.Vmovdqu;

        private static bool IsVexStoreOpcode(X86InstrKind kind, byte opcode)
            => IsVexMove(kind) && opcode is 0x11 or 0x29 or 0x7F;

        private static bool IsVexBinary(X86InstrKind kind)
            => kind is X86InstrKind.Vucomiss or X86InstrKind.Vucomisd or X86InstrKind.Vcvttss2si or X86InstrKind.Vcvttsd2si or X86InstrKind.Vsqrtps or X86InstrKind.Vsqrtpd;

        private static bool IsVexIntegerDestination(X86InstrKind kind)
            => kind is X86InstrKind.Vcvttss2si or X86InstrKind.Vcvttsd2si;

        private static bool IsVexIntegerSource(X86InstrKind kind)
            => kind is X86InstrKind.Vcvtsi2ss or X86InstrKind.Vcvtsi2sd;

        private static bool IsVexScalar(X86InstrKind kind)
            => kind is X86InstrKind.Vaddss or X86InstrKind.Vaddsd or X86InstrKind.Vsubss or X86InstrKind.Vsubsd or X86InstrKind.Vmulss or X86InstrKind.Vmulsd or X86InstrKind.Vdivss or X86InstrKind.Vdivsd or X86InstrKind.Vsqrtss or X86InstrKind.Vsqrtsd or X86InstrKind.Vucomiss or X86InstrKind.Vucomisd or X86InstrKind.Vcvtsi2ss or X86InstrKind.Vcvtsi2sd or X86InstrKind.Vcvttss2si or X86InstrKind.Vcvttsd2si;

        private static X86Register VectorRegister(int index, int size)
            => size == 32 ? Ymm(index) : Xmm(index);

        private static X86Register Gpr(int index)
            => (X86Register)(index & 15);

        private static X86Register Xmm(int index)
            => (X86Register)((int)X86Register.Xmm0 + (index & 15));

        private static X86Register Ymm(int index)
            => (X86Register)((int)X86Register.Ymm0 + (index & 15));

        private static X86Instruction RawFrom(byte[] bytes, int start, int length)
        {
            var raw = new byte[Math.Max(0, length)];
            Array.Copy(bytes, start, raw, 0, raw.Length);
            return X86Instruction.Raw(raw);
        }

        private readonly struct DecodedModRm
        {
            public int Reg { get; }
            public X86Operand Rm { get; }

            public DecodedModRm(int reg, X86Operand rm)
            {
                Reg = reg;
                Rm = rm;
            }
        }

        private sealed class X86InstructionReader
        {
            public byte[] Bytes { get; }
            public int Position { get; private set; }
            public bool End => Position >= Bytes.Length;

            public X86InstructionReader(byte[] bytes)
            {
                Bytes = bytes;
            }

            public byte PeekByte()
                => Bytes[Position];

            public byte ReadByte()
                => Bytes[Position++];

            public sbyte ReadInt8()
                => unchecked((sbyte)ReadByte());

            public short ReadInt16()
            {
                var value = (ushort)(ReadByte() | (ReadByte() << 8));
                return unchecked((short)value);
            }

            public int ReadInt32()
            {
                var value = (uint)(ReadByte() | (ReadByte() << 8) | (ReadByte() << 16) | (ReadByte() << 24));
                return unchecked((int)value);
            }

            public long ReadInt64()
            {
                ulong value = 0;
                for (var i = 0; i < 8; i++)
                    value |= (ulong)ReadByte() << (i * 8);
                return unchecked((long)value);
            }
        }
    }
}
