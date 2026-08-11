using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Cnidaria.X86
{
    internal static class X86CodeEncoder
    {
        public static byte[] Encode(X86Program obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            return X86ObjectLinker.LinkFlat(obj, imageBase, externalSymbols).ToArray();
        }

        public static byte[] Encode(IEnumerable<X86Instruction> instructions, X86Target target, IReadOnlyDictionary<string, int>? labels = null)
        {
            if (instructions is null)
                throw new ArgumentNullException(nameof(instructions));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var symbols = labels?.ToDictionary(kv => kv.Key, kv => (ulong)Math.Max(0, kv.Value), StringComparer.Ordinal);
            var output = new List<byte>();
            foreach (var instruction in instructions)
            {
                var pc = (ulong)output.Count;
                output.AddRange(Encode(instruction, target, pc, symbols));
            }
            return output.ToArray();
        }

        public static byte[] Encode(X86Instruction instruction, X86Target target)
            => Encode(instruction, target, 0, null);

        public static byte[] Encode(X86Instruction instruction, X86Target target, ulong pc, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var firstPass = EncodeCore(instruction, target, pc, pc, symbols);
            return EncodeCore(instruction, target, pc, checked(pc + (ulong)firstPass.Length), symbols);
        }

        public static int GetEncodedLength(X86Instruction instruction, X86Target target)
            => Encode(instruction, target).Length;

        private static byte[] EncodeCore(X86Instruction instruction, X86Target target, ulong pc, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            ValidateInstruction(instruction, target);

            var writer = new X86InstructionWriter();
            if (IsPackedSseInstruction(instruction.Opcode))
            {
                EncodePackedSse(instruction, target, writer, nextIp, symbols);
                return writer.ToArray();
            }
            if (IsVexInstruction(instruction.Opcode))
            {
                EncodeVexInstruction(instruction, target, writer, nextIp, symbols);
                return writer.ToArray();
            }
            switch (instruction.Opcode)
            {
                case X86InstrKind.Raw:
                    writer.Write(instruction.RawBytes);
                    break;
                case X86InstrKind.Nop:
                    writer.WriteByte(0x90);
                    break;
                case X86InstrKind.Ret:
                    writer.WriteByte(0xC3);
                    break;
                case X86InstrKind.Cdq:
                    writer.WriteByte(0x99);
                    break;
                case X86InstrKind.Cqo:
                    EmitRex(writer, target, w: true);
                    writer.WriteByte(0x99);
                    break;
                case X86InstrKind.Cbw:
                    writer.WriteByte(0x66);
                    writer.WriteByte(0x98);
                    break;
                case X86InstrKind.Cwde:
                    writer.WriteByte(0x98);
                    break;
                case X86InstrKind.Cdqe:
                    EmitRex(writer, target, w: true);
                    writer.WriteByte(0x98);
                    break;
                case X86InstrKind.Leave:
                    writer.WriteByte(0xC9);
                    break;
                case X86InstrKind.Int3:
                    writer.WriteByte(0xCC);
                    break;
                case X86InstrKind.Ud2:
                    writer.WriteByte(0x0F);
                    writer.WriteByte(0x0B);
                    break;
                case X86InstrKind.Syscall:
                    writer.WriteByte(0x0F);
                    writer.WriteByte(0x05);
                    break;
                case X86InstrKind.Push:
                    EncodePush(instruction.Operand0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Pop:
                    EncodePop(instruction.Operand0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Mov:
                    EncodeMov(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Lea:
                    EncodeLea(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Movsx:
                    EncodeMovExtend(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols, signed: true, movsxd: false);
                    break;
                case X86InstrKind.Movsxd:
                    EncodeMovExtend(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols, signed: true, movsxd: true);
                    break;
                case X86InstrKind.Movzx:
                    EncodeMovExtend(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols, signed: false, movsxd: false);
                    break;
                case X86InstrKind.Add:
                case X86InstrKind.Or:
                case X86InstrKind.Adc:
                case X86InstrKind.Sbb:
                case X86InstrKind.And:
                case X86InstrKind.Sub:
                case X86InstrKind.Xor:
                case X86InstrKind.Cmp:
                    EncodeBinaryInteger(instruction.Opcode, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Test:
                    EncodeTest(instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Inc:
                    EncodeGroupUnary(instruction.Operand0, 0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Dec:
                    EncodeGroupUnary(instruction.Operand0, 1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Not:
                    EncodeGroupUnary(instruction.Operand0, 2, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Neg:
                    EncodeGroupUnary(instruction.Operand0, 3, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Mul:
                    EncodeGroupUnary(instruction.Operand0, 4, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Div:
                    EncodeGroupUnary(instruction.Operand0, 6, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Idiv:
                    EncodeGroupUnary(instruction.Operand0, 7, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Imul:
                    EncodeImul(instruction, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Rol:
                    EncodeShift(instruction.Operand0, instruction.Operand1, 0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Ror:
                    EncodeShift(instruction.Operand0, instruction.Operand1, 1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Shl:
                    EncodeShift(instruction.Operand0, instruction.Operand1, 4, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Shr:
                    EncodeShift(instruction.Operand0, instruction.Operand1, 5, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Sar:
                    EncodeShift(instruction.Operand0, instruction.Operand1, 7, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Call:
                    EncodeCall(instruction.Operand0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Jmp:
                    EncodeJmp(instruction.Operand0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Jcc:
                    EncodeJcc(instruction.Condition, instruction.Operand0, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Setcc:
                    EncodeSetcc(instruction.Condition, instruction.Operand0, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Cmovcc:
                    EncodeCmovcc(instruction.Condition, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Movss:
                case X86InstrKind.Movsd:
                    EncodeScalarMove(instruction.Opcode, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Addss:
                case X86InstrKind.Addsd:
                case X86InstrKind.Subss:
                case X86InstrKind.Subsd:
                case X86InstrKind.Mulss:
                case X86InstrKind.Mulsd:
                case X86InstrKind.Divss:
                case X86InstrKind.Divsd:
                case X86InstrKind.Ucomiss:
                case X86InstrKind.Ucomisd:
                case X86InstrKind.Cvtss2sd:
                case X86InstrKind.Cvtsd2ss:
                case X86InstrKind.Sqrtss:
                case X86InstrKind.Sqrtsd:
                    EncodeScalarBinary(instruction.Opcode, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Cvtsi2ss:
                case X86InstrKind.Cvtsi2sd:
                    EncodeScalarConvertToVector(instruction.Opcode, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                case X86InstrKind.Cvttss2si:
                case X86InstrKind.Cvttsd2si:
                    EncodeScalarConvertToInteger(instruction.Opcode, instruction.Operand0, instruction.Operand1, target, writer, nextIp, symbols);
                    break;
                default:
                    throw new NotSupportedException("Unsupported x86 instruction: " + instruction.Opcode);
            }
            return writer.ToArray();
        }

        private static void ValidateInstruction(X86Instruction instruction, X86Target target)
        {
            var metadata = X86InstructionTable.Get(instruction.Opcode);
            if (metadata.Requires64Bit && !target.Is64Bit)
                throw new InvalidOperationException("Instruction requires x86-64 target: " + instruction.Opcode);
            if (metadata.RequiredIsa != X86IsaFlags.None && !target.Has(metadata.RequiredIsa))
                throw new InvalidOperationException("Instruction requires unsupported x86 ISA feature: " + metadata.RequiredIsa);
        }

        private static void EncodePush(X86Operand operand, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (operand.Kind == X86OperandKind.Register)
            {
                var reg = X86Registers.Index(operand.Register);
                if (operand.Size == 2)
                    writer.WriteByte(0x66);
                EmitRex(writer, target, b: reg >= 8);
                writer.WriteByte((byte)(0x50 + (reg & 7)));
                return;
            }
            if (operand.Kind == X86OperandKind.Immediate || operand.Kind == X86OperandKind.Symbol)
            {
                var value = ResolveImmediate(operand, symbols, nextIp);
                if (!operand.HasSymbol && FitsSignedByte(value))
                {
                    writer.WriteByte(0x6A);
                    writer.WriteInt8((sbyte)value);
                }
                else
                {
                    writer.WriteByte(0x68);
                    writer.WriteInt32(checked((int)value));
                }
                return;
            }
            EncodeModRmUnary(operand, 6, 0xFF, target, writer, nextIp, symbols, OperandSize(operand, target));
        }

        private static void EncodePop(X86Operand operand, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (operand.Kind == X86OperandKind.Register)
            {
                var reg = X86Registers.Index(operand.Register);
                if (operand.Size == 2)
                    writer.WriteByte(0x66);
                EmitRex(writer, target, b: reg >= 8);
                writer.WriteByte((byte)(0x58 + (reg & 7)));
                return;
            }
            EncodeModRmUnary(operand, 0, 0x8F, target, writer, nextIp, symbols, OperandSize(operand, target));
        }

        private static void EncodeMov(X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var size = CommonSize(destination, source, target);
            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol))
            {
                if (!X86Registers.IsGeneral(destination.Register))
                    throw new NotSupportedException("mov immediate destination must be a general register");
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(destination.Register);
                var rexW = size == 8;
                EmitRex(writer, target, w: rexW, b: reg >= 8, force: RequiresByteRex(destination));
                writer.WriteByte((byte)((size == 1 ? 0xB0 : 0xB8) + (reg & 7)));
                WriteImmediate(writer, ResolveImmediate(source, symbols, nextIp), size == 8 ? 8 : size);
                return;
            }

            if ((destination.Kind == X86OperandKind.Register || destination.Kind == X86OperandKind.Memory) && (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol))
            {
                EmitSizePrefix(writer, size);
                var rexW = size == 8;
                EmitRexForModRm(writer, target, rexW, 0, destination, RequiresByteRex(destination));
                writer.WriteByte(size == 1 ? (byte)0xC6 : (byte)0xC7);
                EmitModRm(writer, 0, destination, target, nextIp, symbols);
                WriteImmediate(writer, ResolveImmediate(source, symbols, nextIp), size == 8 ? 4 : size, signExtended: size == 8);
                return;
            }

            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory))
            {
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, size == 8, reg, source, RequiresByteRex(destination) || RequiresByteRex(source));
                writer.WriteByte(size == 1 ? (byte)0x8A : (byte)0x8B);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }

            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Register)
            {
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(source.Register);
                EmitRexForModRm(writer, target, size == 8, reg, destination, RequiresByteRex(source) || RequiresByteRex(destination));
                writer.WriteByte(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitModRm(writer, reg, destination, target, nextIp, symbols);
                return;
            }

            throw new NotSupportedException("Unsupported mov operands");
        }

        private static void EncodeLea(X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (destination.Kind != X86OperandKind.Register || source.Kind != X86OperandKind.Memory)
                throw new NotSupportedException("lea requires register, memory operands");
            var size = destination.Size == 0 ? target.XLen / 8 : destination.Size;
            EmitSizePrefix(writer, size);
            var reg = X86Registers.Index(destination.Register);
            EmitRexForModRm(writer, target, size == 8, reg, source, false);
            writer.WriteByte(0x8D);
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeMovExtend(X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, bool signed, bool movsxd)
        {
            if (destination.Kind != X86OperandKind.Register || (source.Kind != X86OperandKind.Register && source.Kind != X86OperandKind.Memory))
                throw new NotSupportedException("mov extension requires register, r/m operands");

            var destSize = destination.Size == 0 ? target.XLen / 8 : destination.Size;
            var srcSize = source.Size;
            if (movsxd)
                srcSize = 4;
            if (srcSize != 1 && srcSize != 2 && !(movsxd && srcSize == 4))
                throw new NotSupportedException("unsupported mov extension source size");

            EmitSizePrefix(writer, destSize);
            var reg = X86Registers.Index(destination.Register);
            EmitRexForModRm(writer, target, destSize == 8, reg, source, RequiresByteRex(source));
            if (movsxd)
            {
                writer.WriteByte(0x63);
            }
            else
            {
                writer.WriteByte(0x0F);
                writer.WriteByte((byte)(signed ? (srcSize == 1 ? 0xBE : 0xBF) : (srcSize == 1 ? 0xB6 : 0xB7)));
            }
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeBinaryInteger(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var info = BinaryInfo(opcode);
            var size = CommonSize(destination, source, target);
            if (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol)
            {
                EmitSizePrefix(writer, size);
                EmitRexForModRm(writer, target, size == 8, info.Group, destination, RequiresByteRex(destination));
                var value = ResolveImmediate(source, symbols, nextIp);
                if (size != 1 && !source.HasSymbol && FitsSignedByte(value))
                {
                    writer.WriteByte(0x83);
                    EmitModRm(writer, info.Group, destination, target, nextIp, symbols);
                    writer.WriteInt8((sbyte)value);
                }
                else
                {
                    writer.WriteByte(size == 1 ? (byte)0x80 : (byte)0x81);
                    EmitModRm(writer, info.Group, destination, target, nextIp, symbols);
                    WriteImmediate(writer, value, size == 8 ? 4 : size, signExtended: size == 8);
                }
                return;
            }

            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory))
            {
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, size == 8, reg, source, RequiresByteRex(destination) || RequiresByteRex(source));
                writer.WriteByte(size == 1 ? (byte)(info.RegRmOpcode - 1) : info.RegRmOpcode);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }

            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Register)
            {
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(source.Register);
                EmitRexForModRm(writer, target, size == 8, reg, destination, RequiresByteRex(source) || RequiresByteRex(destination));
                writer.WriteByte(size == 1 ? (byte)(info.RmRegOpcode - 1) : info.RmRegOpcode);
                EmitModRm(writer, reg, destination, target, nextIp, symbols);
                return;
            }

            throw new NotSupportedException("Unsupported integer binary operands");
        }

        private static void EncodeTest(X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var size = CommonSize(destination, source, target);
            if (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol)
            {
                EmitSizePrefix(writer, size);
                EmitRexForModRm(writer, target, size == 8, 0, destination, RequiresByteRex(destination));
                writer.WriteByte(size == 1 ? (byte)0xF6 : (byte)0xF7);
                EmitModRm(writer, 0, destination, target, nextIp, symbols);
                WriteImmediate(writer, ResolveImmediate(source, symbols, nextIp), size == 8 ? 4 : size, signExtended: size == 8);
                return;
            }

            EmitSizePrefix(writer, size);
            var reg = X86Registers.Index(source.Register);
            EmitRexForModRm(writer, target, size == 8, reg, destination, RequiresByteRex(source) || RequiresByteRex(destination));
            writer.WriteByte(size == 1 ? (byte)0x84 : (byte)0x85);
            EmitModRm(writer, reg, destination, target, nextIp, symbols);
        }

        private static void EncodeGroupUnary(X86Operand operand, int group, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var size = OperandSize(operand, target);
            var opcode = size == 1 ? 0xF6 : 0xF7;
            if (group is 0 or 1)
                opcode = size == 1 ? 0xFE : 0xFF;
            EncodeModRmUnary(operand, group, (byte)opcode, target, writer, nextIp, symbols, size);
        }

        private static void EncodeModRmUnary(X86Operand operand, int group, byte opcode, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, int size)
        {
            EmitSizePrefix(writer, size);
            EmitRexForModRm(writer, target, size == 8, group, operand, RequiresByteRex(operand));
            writer.WriteByte(opcode);
            EmitModRm(writer, group, operand, target, nextIp, symbols);
        }

        private static void EncodeImul(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var destination = instruction.Operand0;
            var source = instruction.Operand1;
            var immediate = instruction.Operand2;
            if (destination.Kind == X86OperandKind.Register && source.Kind != X86OperandKind.None && immediate.Kind == X86OperandKind.None)
            {
                var size = CommonSize(destination, source, target);
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, size == 8, reg, source, RequiresByteRex(source));
                writer.WriteByte(0x0F);
                writer.WriteByte(0xAF);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }

            if (destination.Kind == X86OperandKind.Register && source.Kind != X86OperandKind.None && immediate.Kind != X86OperandKind.None)
            {
                var size = CommonSize(destination, source, target);
                var value = ResolveImmediate(immediate, symbols, nextIp);
                EmitSizePrefix(writer, size);
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, size == 8, reg, source, RequiresByteRex(source));
                writer.WriteByte(!immediate.HasSymbol && FitsSignedByte(value) ? (byte)0x6B : (byte)0x69);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                if (!immediate.HasSymbol && FitsSignedByte(value))
                    writer.WriteInt8((sbyte)value);
                else
                    WriteImmediate(writer, value, size == 8 ? 4 : size, signExtended: size == 8);
                return;
            }

            EncodeModRmUnary(destination, 5, destination.Size == 1 ? (byte)0xF6 : (byte)0xF7, target, writer, nextIp, symbols, OperandSize(destination, target));
        }

        private static void EncodeShift(X86Operand destination, X86Operand count, int group, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var size = OperandSize(destination, target);
            EmitSizePrefix(writer, size);
            EmitRexForModRm(writer, target, size == 8, group, destination, RequiresByteRex(destination));
            if (count.Kind == X86OperandKind.Immediate && count.Immediate == 1)
            {
                writer.WriteByte(size == 1 ? (byte)0xD0 : (byte)0xD1);
                EmitModRm(writer, group, destination, target, nextIp, symbols);
                return;
            }
            if (count.Kind == X86OperandKind.Register && count.Register == X86Register.Rcx && count.Size == 1)
            {
                writer.WriteByte(size == 1 ? (byte)0xD2 : (byte)0xD3);
                EmitModRm(writer, group, destination, target, nextIp, symbols);
                return;
            }
            if (count.Kind == X86OperandKind.Immediate)
            {
                writer.WriteByte(size == 1 ? (byte)0xC0 : (byte)0xC1);
                EmitModRm(writer, group, destination, target, nextIp, symbols);
                writer.WriteByte((byte)count.Immediate);
                return;
            }
            throw new NotSupportedException("Unsupported shift count operand");
        }

        private static void EncodeCall(X86Operand operand, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (operand.Kind == X86OperandKind.Symbol || operand.Kind == X86OperandKind.Immediate)
            {
                writer.WriteByte(0xE8);
                writer.WriteInt32(checked((int)ResolveRelative(operand, symbols, nextIp)));
                return;
            }
            EncodeModRmUnary(operand, 2, 0xFF, target, writer, nextIp, symbols, target.XLen / 8);
        }

        private static void EncodeJmp(X86Operand operand, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (operand.Kind == X86OperandKind.Symbol || operand.Kind == X86OperandKind.Immediate)
            {
                writer.WriteByte(0xE9);
                writer.WriteInt32(checked((int)ResolveRelative(operand, symbols, nextIp)));
                return;
            }
            EncodeModRmUnary(operand, 4, 0xFF, target, writer, nextIp, symbols, target.XLen / 8);
        }

        private static void EncodeJcc(X86Condition condition, X86Operand operand, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            writer.WriteByte(0x0F);
            writer.WriteByte((byte)(0x80 + ((int)condition & 0xF)));
            writer.WriteInt32(checked((int)ResolveRelative(operand, symbols, nextIp)));
        }

        private static void EncodeSetcc(X86Condition condition, X86Operand operand, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            EmitRexForModRm(writer, target, false, 0, operand, RequiresByteRex(operand));
            writer.WriteByte(0x0F);
            writer.WriteByte((byte)(0x90 + ((int)condition & 0xF)));
            EmitModRm(writer, 0, operand.WithSize(1), target, nextIp, symbols);
        }

        private static void EncodeCmovcc(X86Condition condition, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var size = CommonSize(destination, source, target);
            EmitSizePrefix(writer, size);
            var reg = X86Registers.Index(destination.Register);
            EmitRexForModRm(writer, target, size == 8, reg, source, false);
            writer.WriteByte(0x0F);
            writer.WriteByte((byte)(0x40 + ((int)condition & 0xF)));
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeScalarMove(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            EmitScalarPrefix(writer, opcode);
            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Register)
            {
                var reg = X86Registers.Index(source.Register);
                EmitRexForModRm(writer, target, false, reg, destination, false);
                writer.WriteByte(0x0F);
                writer.WriteByte(0x11);
                EmitModRm(writer, reg, destination, target, nextIp, symbols);
                return;
            }
            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory))
            {
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, false, reg, source, false);
                writer.WriteByte(0x0F);
                writer.WriteByte(0x10);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }
            throw new NotSupportedException("Unsupported scalar mov operands");
        }

        private static void EncodeScalarBinary(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (destination.Kind != X86OperandKind.Register || (source.Kind != X86OperandKind.Register && source.Kind != X86OperandKind.Memory))
                throw new NotSupportedException("Scalar binary instruction requires xmm, xmm/mem operands");
            EmitScalarPrefix(writer, opcode);
            var reg = X86Registers.Index(destination.Register);
            EmitRexForModRm(writer, target, false, reg, source, false);
            writer.WriteByte(0x0F);
            writer.WriteByte(ScalarOpcode(opcode));
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeScalarConvertToVector(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (destination.Kind != X86OperandKind.Register || !X86Registers.IsVector(destination.Register))
                throw new NotSupportedException("Scalar conversion destination must be an xmm register");
            EmitScalarPrefix(writer, opcode);
            var reg = X86Registers.Index(destination.Register);
            var sourceSize = source.Size == 0 ? target.XLen / 8 : source.Size;
            EmitRexForModRm(writer, target, sourceSize == 8, reg, source, false);
            writer.WriteByte(0x0F);
            writer.WriteByte(0x2A);
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeScalarConvertToInteger(X86InstrKind opcode, X86Operand destination, X86Operand source, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (destination.Kind != X86OperandKind.Register || !X86Registers.IsGeneral(destination.Register))
                throw new NotSupportedException("Scalar conversion destination must be a general register");
            EmitScalarPrefix(writer, opcode);
            var reg = X86Registers.Index(destination.Register);
            var destSize = destination.Size == 0 ? target.XLen / 8 : destination.Size;
            EmitRexForModRm(writer, target, destSize == 8, reg, source, false);
            writer.WriteByte(0x0F);
            writer.WriteByte(0x2C);
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }


        private static bool IsPackedSseInstruction(X86InstrKind opcode)
        {
            return opcode is X86InstrKind.Movaps or X86InstrKind.Movups or X86InstrKind.Movapd or X86InstrKind.Movupd or X86InstrKind.Movdqa or X86InstrKind.Movdqu or
                X86InstrKind.Addps or X86InstrKind.Addpd or X86InstrKind.Subps or X86InstrKind.Subpd or X86InstrKind.Mulps or X86InstrKind.Mulpd or
                X86InstrKind.Divps or X86InstrKind.Divpd or X86InstrKind.Sqrtps or X86InstrKind.Sqrtpd or X86InstrKind.Andps or X86InstrKind.Andpd or
                X86InstrKind.Orps or X86InstrKind.Orpd or X86InstrKind.Xorps or X86InstrKind.Xorpd or X86InstrKind.Pxor;
        }

        private static bool IsVexInstruction(X86InstrKind opcode)
        {
            return opcode is X86InstrKind.Vzeroupper or X86InstrKind.Vzeroall or
                X86InstrKind.Vmovaps or X86InstrKind.Vmovups or X86InstrKind.Vmovapd or X86InstrKind.Vmovupd or X86InstrKind.Vmovdqa or X86InstrKind.Vmovdqu or
                X86InstrKind.Vaddss or X86InstrKind.Vaddsd or X86InstrKind.Vsubss or X86InstrKind.Vsubsd or X86InstrKind.Vmulss or X86InstrKind.Vmulsd or
                X86InstrKind.Vdivss or X86InstrKind.Vdivsd or X86InstrKind.Vsqrtss or X86InstrKind.Vsqrtsd or X86InstrKind.Vucomiss or X86InstrKind.Vucomisd or
                X86InstrKind.Vcvtsi2ss or X86InstrKind.Vcvtsi2sd or X86InstrKind.Vcvttss2si or X86InstrKind.Vcvttsd2si or
                X86InstrKind.Vaddps or X86InstrKind.Vaddpd or X86InstrKind.Vsubps or X86InstrKind.Vsubpd or X86InstrKind.Vmulps or X86InstrKind.Vmulpd or
                X86InstrKind.Vdivps or X86InstrKind.Vdivpd or X86InstrKind.Vsqrtps or X86InstrKind.Vsqrtpd or X86InstrKind.Vandps or X86InstrKind.Vandpd or
                X86InstrKind.Vorps or X86InstrKind.Vorpd or X86InstrKind.Vxorps or X86InstrKind.Vxorpd or
                X86InstrKind.Vpaddb or X86InstrKind.Vpaddw or X86InstrKind.Vpaddd or X86InstrKind.Vpaddq or X86InstrKind.Vpsubb or X86InstrKind.Vpsubw or
                X86InstrKind.Vpsubd or X86InstrKind.Vpsubq or X86InstrKind.Vpmulld or X86InstrKind.Vpand or X86InstrKind.Vpor or X86InstrKind.Vpxor or
                X86InstrKind.Vpcmpeqb or X86InstrKind.Vpcmpeqw or X86InstrKind.Vpcmpeqd or X86InstrKind.Vpcmpeqq or X86InstrKind.Vpcmpgtb or X86InstrKind.Vpcmpgtw or
                X86InstrKind.Vpcmpgtd or X86InstrKind.Vpcmpgtq or X86InstrKind.Vpslld or X86InstrKind.Vpsllq or X86InstrKind.Vpsrld or X86InstrKind.Vpsrlq or X86InstrKind.Vpsrad;
        }

        private static void EncodePackedSse(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            var info = PackedSseInfo(instruction.Opcode);
            var destination = instruction.Operand0;
            var source = instruction.Operand1;
            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Register && info.StoreOpcode >= 0)
            {
                EmitMandatoryPrefix(writer, info.Prefix);
                var reg = X86Registers.Index(source.Register);
                EmitRexForModRm(writer, target, false, reg, destination, false);
                writer.WriteByte(0x0F);
                writer.WriteByte((byte)info.StoreOpcode);
                EmitModRm(writer, reg, destination, target, nextIp, symbols);
                return;
            }
            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory))
            {
                EmitMandatoryPrefix(writer, info.Prefix);
                var reg = X86Registers.Index(destination.Register);
                EmitRexForModRm(writer, target, false, reg, source, false);
                writer.WriteByte(0x0F);
                writer.WriteByte(info.Opcode);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }
            throw new NotSupportedException("Unsupported packed SSE operands");
        }

        private static void EncodeVexInstruction(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (instruction.Opcode == X86InstrKind.Vzeroupper || instruction.Opcode == X86InstrKind.Vzeroall)
            {
                writer.WriteByte(0xC5);
                writer.WriteByte(instruction.Opcode == X86InstrKind.Vzeroall ? (byte)0xFC : (byte)0xF8);
                writer.WriteByte(0x77);
                return;
            }

            var info = VexInfo(instruction.Opcode);
            if (info.Move)
            {
                EncodeVexMove(instruction, target, writer, nextIp, symbols, info);
                return;
            }
            if (info.CompareOrConvertToInt)
            {
                EncodeVexBinary(instruction, target, writer, nextIp, symbols, info);
                return;
            }
            if (info.Binary)
            {
                EncodeVexBinary(instruction, target, writer, nextIp, symbols, info);
                return;
            }
            EncodeVexTernary(instruction, target, writer, nextIp, symbols, info);
        }

        private static void EncodeVexMove(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, X86VexInfo info)
        {
            var destination = instruction.Operand0;
            var source = instruction.Operand1;
            if (destination.Kind == X86OperandKind.Memory && source.Kind == X86OperandKind.Register && info.StoreOpcode >= 0)
            {
                var reg = X86Registers.Index(source.Register);
                EmitVex(writer, target, info.Map, info.Prefix, info.W, reg, 15, VectorLength(source, destination, false), destination);
                writer.WriteByte((byte)info.StoreOpcode);
                EmitModRm(writer, reg, destination, target, nextIp, symbols);
                return;
            }
            if (destination.Kind == X86OperandKind.Register && (source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory))
            {
                var reg = X86Registers.Index(destination.Register);
                EmitVex(writer, target, info.Map, info.Prefix, info.W, reg, 15, VectorLength(destination, source, false), source);
                writer.WriteByte(info.Opcode);
                EmitModRm(writer, reg, source, target, nextIp, symbols);
                return;
            }
            throw new NotSupportedException("Unsupported AVX move operands");
        }

        private static void EncodeVexBinary(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, X86VexInfo info)
        {
            var destination = instruction.Operand0;
            var source = instruction.Operand1;
            if (destination.Kind != X86OperandKind.Register || (source.Kind != X86OperandKind.Register && source.Kind != X86OperandKind.Memory))
                throw new NotSupportedException("Unsupported AVX binary operands");
            var reg = X86Registers.Index(destination.Register);
            var w = info.W || IsVexIntegerConvertTo64(instruction.Opcode, destination);
            EmitVex(writer, target, info.Map, info.Prefix, w, reg, 15, VectorLength(destination, source, info.Scalar), source);
            writer.WriteByte(info.Opcode);
            EmitModRm(writer, reg, source, target, nextIp, symbols);
        }

        private static void EncodeVexTernary(X86Instruction instruction, X86Target target, X86InstructionWriter writer, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols, X86VexInfo info)
        {
            var destination = instruction.Operand0;
            var source1 = instruction.Operand1;
            var source2 = instruction.Operand2;
            if (destination.Kind != X86OperandKind.Register || source1.Kind != X86OperandKind.Register || (source2.Kind != X86OperandKind.Register && source2.Kind != X86OperandKind.Memory))
                throw new NotSupportedException("Unsupported AVX ternary operands");
            var reg = X86Registers.Index(destination.Register);
            var vvvv = X86Registers.Index(source1.Register);
            var w = info.W || IsVexIntegerConvertFrom64(instruction.Opcode, source2);
            EmitVex(writer, target, info.Map, info.Prefix, w, reg, vvvv, VectorLength(destination, source2, info.Scalar), source2);
            writer.WriteByte(info.Opcode);
            EmitModRm(writer, reg, source2, target, nextIp, symbols);
        }

        private static int VectorLength(X86Operand first, X86Operand second, bool scalar)
        {
            if (scalar)
                return 0;
            if (first.Kind == X86OperandKind.Register && X86Registers.IsYmm(first.Register))
                return 1;
            if (second.Kind == X86OperandKind.Register && X86Registers.IsYmm(second.Register))
                return 1;
            if (first.Size == 32 || second.Size == 32)
                return 1;
            return 0;
        }

        private static bool IsVexIntegerConvertTo64(X86InstrKind opcode, X86Operand destination)
            => (opcode == X86InstrKind.Vcvttss2si || opcode == X86InstrKind.Vcvttsd2si) && destination.Size == 8;

        private static bool IsVexIntegerConvertFrom64(X86InstrKind opcode, X86Operand source)
            => (opcode == X86InstrKind.Vcvtsi2ss || opcode == X86InstrKind.Vcvtsi2sd) && source.Size == 8;

        private static void EmitMandatoryPrefix(X86InstructionWriter writer, int prefix)
        {
            if (prefix == 1)
                writer.WriteByte(0x66);
            else if (prefix == 2)
                writer.WriteByte(0xF3);
            else if (prefix == 3)
                writer.WriteByte(0xF2);
        }

        private static void EmitVex(X86InstructionWriter writer, X86Target target, int map, int prefix, bool w, int regField, int vvvv, int vectorLength, X86Operand rmOperand)
        {
            var r = regField >= 8;
            var x = false;
            var b = false;
            if (rmOperand.Kind == X86OperandKind.Register)
            {
                b = X86Registers.IsVector(rmOperand.Register) || X86Registers.IsGeneral(rmOperand.Register) ? X86Registers.Index(rmOperand.Register) >= 8 : false;
            }
            else if (rmOperand.Kind == X86OperandKind.Memory)
            {
                b = X86Registers.IsGeneral(rmOperand.BaseRegister) && X86Registers.Index(rmOperand.BaseRegister) >= 8;
                x = X86Registers.IsGeneral(rmOperand.IndexRegister) && X86Registers.Index(rmOperand.IndexRegister) >= 8;
            }
            if (!target.Is64Bit && (r || x || b || vvvv >= 8))
                throw new NotSupportedException("Extended AVX registers require an x86-64 target");
            writer.WriteByte(0xC4);
            writer.WriteByte((byte)((r ? 0 : 0x80) | (x ? 0 : 0x40) | (b ? 0 : 0x20) | (map & 0x1F)));
            writer.WriteByte((byte)((w ? 0x80 : 0) | (((~vvvv) & 0xF) << 3) | ((vectorLength & 1) << 2) | (prefix & 3)));
        }

        private static X86PackedSseInfo PackedSseInfo(X86InstrKind opcode)
        {
            return opcode switch
            {
                X86InstrKind.Movaps => new X86PackedSseInfo(0, 0x28, 0x29),
                X86InstrKind.Movups => new X86PackedSseInfo(0, 0x10, 0x11),
                X86InstrKind.Movapd => new X86PackedSseInfo(1, 0x28, 0x29),
                X86InstrKind.Movupd => new X86PackedSseInfo(1, 0x10, 0x11),
                X86InstrKind.Movdqa => new X86PackedSseInfo(1, 0x6F, 0x7F),
                X86InstrKind.Movdqu => new X86PackedSseInfo(2, 0x6F, 0x7F),
                X86InstrKind.Addps => new X86PackedSseInfo(0, 0x58, -1),
                X86InstrKind.Addpd => new X86PackedSseInfo(1, 0x58, -1),
                X86InstrKind.Subps => new X86PackedSseInfo(0, 0x5C, -1),
                X86InstrKind.Subpd => new X86PackedSseInfo(1, 0x5C, -1),
                X86InstrKind.Mulps => new X86PackedSseInfo(0, 0x59, -1),
                X86InstrKind.Mulpd => new X86PackedSseInfo(1, 0x59, -1),
                X86InstrKind.Divps => new X86PackedSseInfo(0, 0x5E, -1),
                X86InstrKind.Divpd => new X86PackedSseInfo(1, 0x5E, -1),
                X86InstrKind.Sqrtps => new X86PackedSseInfo(0, 0x51, -1),
                X86InstrKind.Sqrtpd => new X86PackedSseInfo(1, 0x51, -1),
                X86InstrKind.Andps => new X86PackedSseInfo(0, 0x54, -1),
                X86InstrKind.Andpd => new X86PackedSseInfo(1, 0x54, -1),
                X86InstrKind.Orps => new X86PackedSseInfo(0, 0x56, -1),
                X86InstrKind.Orpd => new X86PackedSseInfo(1, 0x56, -1),
                X86InstrKind.Xorps => new X86PackedSseInfo(0, 0x57, -1),
                X86InstrKind.Xorpd => new X86PackedSseInfo(1, 0x57, -1),
                X86InstrKind.Pxor => new X86PackedSseInfo(1, 0xEF, -1),
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

        private static X86VexInfo VexInfo(X86InstrKind opcode)
        {
            return opcode switch
            {
                X86InstrKind.Vmovaps => new X86VexInfo(1, 0, 0x28, 0x29, move: true, binary: true),
                X86InstrKind.Vmovups => new X86VexInfo(1, 0, 0x10, 0x11, move: true, binary: true),
                X86InstrKind.Vmovapd => new X86VexInfo(1, 1, 0x28, 0x29, move: true, binary: true),
                X86InstrKind.Vmovupd => new X86VexInfo(1, 1, 0x10, 0x11, move: true, binary: true),
                X86InstrKind.Vmovdqa => new X86VexInfo(1, 1, 0x6F, 0x7F, move: true, binary: true),
                X86InstrKind.Vmovdqu => new X86VexInfo(1, 2, 0x6F, 0x7F, move: true, binary: true),
                X86InstrKind.Vucomiss => new X86VexInfo(1, 0, 0x2E, -1, binary: true, compareOrConvertToInt: true, scalar: true),
                X86InstrKind.Vucomisd => new X86VexInfo(1, 1, 0x2E, -1, binary: true, compareOrConvertToInt: true, scalar: true),
                X86InstrKind.Vcvttss2si => new X86VexInfo(1, 2, 0x2C, -1, binary: true, compareOrConvertToInt: true, scalar: true),
                X86InstrKind.Vcvttsd2si => new X86VexInfo(1, 3, 0x2C, -1, binary: true, compareOrConvertToInt: true, scalar: true),
                X86InstrKind.Vcvtsi2ss => new X86VexInfo(1, 2, 0x2A, -1, scalar: true),
                X86InstrKind.Vcvtsi2sd => new X86VexInfo(1, 3, 0x2A, -1, scalar: true),
                X86InstrKind.Vaddss => new X86VexInfo(1, 2, 0x58, -1, scalar: true),
                X86InstrKind.Vaddsd => new X86VexInfo(1, 3, 0x58, -1, scalar: true),
                X86InstrKind.Vsubss => new X86VexInfo(1, 2, 0x5C, -1, scalar: true),
                X86InstrKind.Vsubsd => new X86VexInfo(1, 3, 0x5C, -1, scalar: true),
                X86InstrKind.Vmulss => new X86VexInfo(1, 2, 0x59, -1, scalar: true),
                X86InstrKind.Vmulsd => new X86VexInfo(1, 3, 0x59, -1, scalar: true),
                X86InstrKind.Vdivss => new X86VexInfo(1, 2, 0x5E, -1, scalar: true),
                X86InstrKind.Vdivsd => new X86VexInfo(1, 3, 0x5E, -1, scalar: true),
                X86InstrKind.Vsqrtss => new X86VexInfo(1, 2, 0x51, -1, scalar: true),
                X86InstrKind.Vsqrtsd => new X86VexInfo(1, 3, 0x51, -1, scalar: true),
                X86InstrKind.Vaddps => new X86VexInfo(1, 0, 0x58, -1),
                X86InstrKind.Vaddpd => new X86VexInfo(1, 1, 0x58, -1),
                X86InstrKind.Vsubps => new X86VexInfo(1, 0, 0x5C, -1),
                X86InstrKind.Vsubpd => new X86VexInfo(1, 1, 0x5C, -1),
                X86InstrKind.Vmulps => new X86VexInfo(1, 0, 0x59, -1),
                X86InstrKind.Vmulpd => new X86VexInfo(1, 1, 0x59, -1),
                X86InstrKind.Vdivps => new X86VexInfo(1, 0, 0x5E, -1),
                X86InstrKind.Vdivpd => new X86VexInfo(1, 1, 0x5E, -1),
                X86InstrKind.Vsqrtps => new X86VexInfo(1, 0, 0x51, -1, binary: true),
                X86InstrKind.Vsqrtpd => new X86VexInfo(1, 1, 0x51, -1, binary: true),
                X86InstrKind.Vandps => new X86VexInfo(1, 0, 0x54, -1),
                X86InstrKind.Vandpd => new X86VexInfo(1, 1, 0x54, -1),
                X86InstrKind.Vorps => new X86VexInfo(1, 0, 0x56, -1),
                X86InstrKind.Vorpd => new X86VexInfo(1, 1, 0x56, -1),
                X86InstrKind.Vxorps => new X86VexInfo(1, 0, 0x57, -1),
                X86InstrKind.Vxorpd => new X86VexInfo(1, 1, 0x57, -1),
                X86InstrKind.Vpaddb => new X86VexInfo(1, 1, 0xFC, -1),
                X86InstrKind.Vpaddw => new X86VexInfo(1, 1, 0xFD, -1),
                X86InstrKind.Vpaddd => new X86VexInfo(1, 1, 0xFE, -1),
                X86InstrKind.Vpaddq => new X86VexInfo(1, 1, 0xD4, -1),
                X86InstrKind.Vpsubb => new X86VexInfo(1, 1, 0xF8, -1),
                X86InstrKind.Vpsubw => new X86VexInfo(1, 1, 0xF9, -1),
                X86InstrKind.Vpsubd => new X86VexInfo(1, 1, 0xFA, -1),
                X86InstrKind.Vpsubq => new X86VexInfo(1, 1, 0xFB, -1),
                X86InstrKind.Vpand => new X86VexInfo(1, 1, 0xDB, -1),
                X86InstrKind.Vpor => new X86VexInfo(1, 1, 0xEB, -1),
                X86InstrKind.Vpxor => new X86VexInfo(1, 1, 0xEF, -1),
                X86InstrKind.Vpcmpeqb => new X86VexInfo(1, 1, 0x74, -1),
                X86InstrKind.Vpcmpeqw => new X86VexInfo(1, 1, 0x75, -1),
                X86InstrKind.Vpcmpeqd => new X86VexInfo(1, 1, 0x76, -1),
                X86InstrKind.Vpcmpgtb => new X86VexInfo(1, 1, 0x64, -1),
                X86InstrKind.Vpcmpgtw => new X86VexInfo(1, 1, 0x65, -1),
                X86InstrKind.Vpcmpgtd => new X86VexInfo(1, 1, 0x66, -1),
                X86InstrKind.Vpmulld => new X86VexInfo(2, 1, 0x40, -1),
                X86InstrKind.Vpcmpeqq => new X86VexInfo(2, 1, 0x29, -1),
                X86InstrKind.Vpcmpgtq => new X86VexInfo(2, 1, 0x37, -1),
                X86InstrKind.Vpslld => new X86VexInfo(1, 1, 0xF2, -1),
                X86InstrKind.Vpsllq => new X86VexInfo(1, 1, 0xF3, -1),
                X86InstrKind.Vpsrld => new X86VexInfo(1, 1, 0xD2, -1),
                X86InstrKind.Vpsrlq => new X86VexInfo(1, 1, 0xD3, -1),
                X86InstrKind.Vpsrad => new X86VexInfo(1, 1, 0xE2, -1),
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

        private readonly struct X86PackedSseInfo
        {
            public int Prefix { get; }
            public byte Opcode { get; }
            public int StoreOpcode { get; }

            public X86PackedSseInfo(int prefix, int opcode, int storeOpcode)
            {
                Prefix = prefix;
                Opcode = (byte)opcode;
                StoreOpcode = storeOpcode;
            }
        }

        private readonly struct X86VexInfo
        {
            public int Map { get; }
            public int Prefix { get; }
            public byte Opcode { get; }
            public int StoreOpcode { get; }
            public bool W { get; }
            public bool Move { get; }
            public bool Binary { get; }
            public bool CompareOrConvertToInt { get; }
            public bool Scalar { get; }

            public X86VexInfo(int map, int prefix, int opcode, int storeOpcode, bool w = false, bool move = false, bool binary = false, bool compareOrConvertToInt = false, bool scalar = false)
            {
                Map = map;
                Prefix = prefix;
                Opcode = (byte)opcode;
                StoreOpcode = storeOpcode;
                W = w;
                Move = move;
                Binary = binary;
                CompareOrConvertToInt = compareOrConvertToInt;
                Scalar = scalar;
            }
        }

        private static void EmitScalarPrefix(X86InstructionWriter writer, X86InstrKind opcode)
        {
            var prefix = ScalarPrefix(opcode);
            if (prefix != 0)
                writer.WriteByte(prefix);
        }

        private static byte ScalarPrefix(X86InstrKind opcode)
        {
            if (opcode == X86InstrKind.Ucomiss)
                return 0;
            if (opcode == X86InstrKind.Ucomisd)
                return 0x66;
            if (opcode == X86InstrKind.Cvtss2sd)
                return 0xF3;
            if (opcode == X86InstrKind.Cvtsd2ss)
                return 0xF2;
            return IsDoubleScalar(opcode) ? (byte)0xF2 : (byte)0xF3;
        }

        private static byte ScalarOpcode(X86InstrKind opcode)
        {
            return opcode switch
            {
                X86InstrKind.Addss or X86InstrKind.Addsd => 0x58,
                X86InstrKind.Subss or X86InstrKind.Subsd => 0x5C,
                X86InstrKind.Mulss or X86InstrKind.Mulsd => 0x59,
                X86InstrKind.Divss or X86InstrKind.Divsd => 0x5E,
                X86InstrKind.Ucomiss or X86InstrKind.Ucomisd => 0x2E,
                X86InstrKind.Cvtss2sd or X86InstrKind.Cvtsd2ss => 0x5A,
                X86InstrKind.Sqrtss or X86InstrKind.Sqrtsd => 0x51,
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

        private static bool IsDoubleScalar(X86InstrKind opcode)
            => opcode is X86InstrKind.Movsd or X86InstrKind.Addsd or X86InstrKind.Subsd or X86InstrKind.Mulsd or X86InstrKind.Divsd or X86InstrKind.Ucomisd or X86InstrKind.Cvtsi2sd or X86InstrKind.Cvtss2sd or X86InstrKind.Cvttsd2si or X86InstrKind.Sqrtsd;

        private static void EmitModRm(X86InstructionWriter writer, int regField, X86Operand rmOperand, X86Target target, ulong nextIp, IReadOnlyDictionary<string, ulong>? symbols)
        {
            if (rmOperand.Kind == X86OperandKind.Register)
            {
                var rm = X86Registers.Index(rmOperand.Register) & 7;
                writer.WriteByte((byte)(0xC0 | ((regField & 7) << 3) | rm));
                return;
            }
            if (rmOperand.Kind != X86OperandKind.Memory)
                throw new NotSupportedException("ModRM operand must be register or memory");

            var displacement = ResolveMemoryDisplacement(rmOperand, symbols, nextIp);
            var baseRegister = rmOperand.BaseRegister;
            var hasBase = X86Registers.IsGeneral(baseRegister);
            var hasIndex = X86Registers.IsGeneral(rmOperand.IndexRegister);
            var ripRelative = rmOperand.IsRipRelative || baseRegister == X86Register.Rip;

            if (ripRelative)
            {
                writer.WriteByte((byte)(((regField & 7) << 3) | 0x05));
                writer.WriteInt32(checked((int)displacement));
                return;
            }

            if (!hasBase && !hasIndex)
            {
                if (target.Is64Bit)
                {
                    writer.WriteByte((byte)(((regField & 7) << 3) | 0x04));
                    writer.WriteByte(0x25);
                }
                else
                {
                    writer.WriteByte((byte)(((regField & 7) << 3) | 0x05));
                }
                writer.WriteInt32(checked((int)displacement));
                return;
            }

            if (hasIndex && rmOperand.IndexRegister == X86Register.Rsp)
                throw new NotSupportedException("rsp cannot be encoded as an x86 index register");

            var baseIndex = hasBase ? X86Registers.Index(baseRegister) : 5;
            var baseLow = baseIndex & 7;
            var needsSib = hasIndex || baseLow == 4 || !hasBase;
            int mod;
            var forceDisp8Zero = hasBase && displacement == 0 && baseLow == 5;
            if (!hasBase)
                mod = 0;
            else if (displacement == 0 && baseLow != 5)
                mod = 0;
            else if (FitsSignedByte(displacement) || forceDisp8Zero)
                mod = 1;
            else
                mod = 2;

            writer.WriteByte((byte)((mod << 6) | ((regField & 7) << 3) | (needsSib ? 4 : baseLow)));
            if (needsSib)
            {
                var scale = ScaleBits(rmOperand.Scale);
                var index = hasIndex ? X86Registers.Index(rmOperand.IndexRegister) & 7 : 4;
                var sibBase = hasBase ? baseLow : 5;
                writer.WriteByte((byte)((scale << 6) | (index << 3) | sibBase));
            }

            if (mod == 1)
                writer.WriteInt8((sbyte)displacement);
            else if (mod == 2 || !hasBase)
                writer.WriteInt32(checked((int)displacement));
        }

        private static void EmitRexForModRm(X86InstructionWriter writer, X86Target target, bool w, int regField, X86Operand rmOperand, bool force = false)
        {
            var r = regField >= 8;
            var x = false;
            var b = false;
            if (rmOperand.Kind == X86OperandKind.Register)
            {
                b = X86Registers.IsGeneral(rmOperand.Register) || X86Registers.IsVector(rmOperand.Register) ? X86Registers.Index(rmOperand.Register) >= 8 : false;
            }
            else if (rmOperand.Kind == X86OperandKind.Memory)
            {
                b = X86Registers.IsGeneral(rmOperand.BaseRegister) && X86Registers.Index(rmOperand.BaseRegister) >= 8;
                x = X86Registers.IsGeneral(rmOperand.IndexRegister) && X86Registers.Index(rmOperand.IndexRegister) >= 8;
            }
            EmitRex(writer, target, w, r, x, b, force);
        }

        private static void EmitRex(X86InstructionWriter writer, X86Target target, bool w = false, bool r = false, bool x = false, bool b = false, bool force = false)
        {
            if (!target.Is64Bit)
                return;
            if (!w && !r && !x && !b && !force)
                return;
            writer.WriteByte((byte)(0x40 | (w ? 0x08 : 0) | (r ? 0x04 : 0) | (x ? 0x02 : 0) | (b ? 0x01 : 0)));
        }

        private static void EmitSizePrefix(X86InstructionWriter writer, int size)
        {
            if (size == 2)
                writer.WriteByte(0x66);
        }

        private static int OperandSize(X86Operand operand, X86Target target)
        {
            if (operand.Size != 0)
                return operand.Size;
            return target.XLen / 8;
        }

        private static int CommonSize(X86Operand left, X86Operand right, X86Target target)
        {
            if (left.Size != 0)
                return left.Size;
            if (right.Size != 0)
                return right.Size;
            return target.XLen / 8;
        }

        private static bool RequiresByteRex(X86Operand operand)
        {
            if (operand.Kind == X86OperandKind.Register && operand.Size == 1 && X86Registers.IsGeneral(operand.Register))
            {
                var index = X86Registers.Index(operand.Register);
                return index >= 4;
            }
            if (operand.Kind == X86OperandKind.Memory)
            {
                return (X86Registers.IsGeneral(operand.BaseRegister) && X86Registers.Index(operand.BaseRegister) >= 8) ||
                    (X86Registers.IsGeneral(operand.IndexRegister) && X86Registers.Index(operand.IndexRegister) >= 8);
            }
            return false;
        }

        private static long ResolveImmediate(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
        {
            if (operand.Kind == X86OperandKind.Immediate)
                return operand.Immediate;
            if (operand.Kind != X86OperandKind.Symbol)
                throw new InvalidOperationException("Operand is not immediate or symbolic");
            var value = checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend);
            if (operand.RelocationKind is X86ObjectRelocationKind.Relative8 or X86ObjectRelocationKind.Relative32)
                return checked(value - (long)nextIp);
            return value;
        }

        private static long ResolveRelative(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
        {
            if (operand.Kind == X86OperandKind.Immediate)
                return operand.Immediate;
            if (operand.Kind == X86OperandKind.Symbol)
                return checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend - (long)nextIp);
            throw new InvalidOperationException("Operand is not a relative branch target");
        }

        private static long ResolveMemoryDisplacement(X86Operand operand, IReadOnlyDictionary<string, ulong>? symbols, ulong nextIp)
        {
            var value = operand.Displacement;
            if (!operand.HasSymbol)
                return value;

            var symbolValue = checked((long)ResolveSymbol(symbols, operand.Symbol!) + operand.Addend);
            if (operand.RelocationKind == X86ObjectRelocationKind.RipRelative32 || operand.IsRipRelative)
                return checked(symbolValue + value - (long)nextIp);
            return checked(symbolValue + value);
        }

        private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong>? symbols, string symbol)
        {
            if (symbols is null)
                return 0;
            if (symbols.TryGetValue(symbol, out var value))
                return value;
            throw new KeyNotFoundException($"Undefined x86 symbol: {symbol}");
        }

        private static void WriteImmediate(X86InstructionWriter writer, long value, int size, bool signExtended = false)
        {
            switch (size)
            {
                case 1:
                    if (value < sbyte.MinValue ||
                        (!signExtended && value > byte.MaxValue) ||
                        (signExtended && value > sbyte.MaxValue))
                    {
                        throw new OverflowException("Immediate does not fit 8 bits.");
                    }
                    writer.WriteInt8(unchecked((sbyte)value));
                    break;
                case 2:
                    if (value < short.MinValue ||
                        (!signExtended && value > ushort.MaxValue) ||
                        (signExtended && value > short.MaxValue))
                    {
                        throw new OverflowException("Immediate does not fit 16 bits.");
                    }
                    writer.WriteInt16(unchecked((short)value));
                    break;
                case 4:
                    if (value < int.MinValue ||
                        (!signExtended && value > uint.MaxValue) ||
                        (signExtended && value > int.MaxValue))
                    {
                        throw new OverflowException("Immediate does not fit 32 bits.");
                    }
                    writer.WriteInt32(unchecked((int)value));
                    break;
                case 8:
                    writer.WriteInt64(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        private static X86BinaryInfo BinaryInfo(X86InstrKind opcode)
        {
            return opcode switch
            {
                X86InstrKind.Add => new X86BinaryInfo(0, 0x01, 0x03),
                X86InstrKind.Or => new X86BinaryInfo(1, 0x09, 0x0B),
                X86InstrKind.Adc => new X86BinaryInfo(2, 0x11, 0x13),
                X86InstrKind.Sbb => new X86BinaryInfo(3, 0x19, 0x1B),
                X86InstrKind.And => new X86BinaryInfo(4, 0x21, 0x23),
                X86InstrKind.Sub => new X86BinaryInfo(5, 0x29, 0x2B),
                X86InstrKind.Xor => new X86BinaryInfo(6, 0x31, 0x33),
                X86InstrKind.Cmp => new X86BinaryInfo(7, 0x39, 0x3B),
                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

        private static int ScaleBits(int scale)
        {
            return scale switch
            {
                1 => 0,
                2 => 1,
                4 => 2,
                8 => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(scale)),
            };
        }

        private static bool FitsSignedByte(long value)
            => value >= sbyte.MinValue && value <= sbyte.MaxValue;

        private readonly struct X86BinaryInfo
        {
            public int Group { get; }
            public byte RmRegOpcode { get; }
            public byte RegRmOpcode { get; }

            public X86BinaryInfo(int group, int rmRegOpcode, int regRmOpcode)
            {
                Group = group;
                RmRegOpcode = (byte)rmRegOpcode;
                RegRmOpcode = (byte)regRmOpcode;
            }
        }

        private sealed class X86InstructionWriter
        {
            private readonly List<byte> _bytes = new List<byte>();

            public int Count => _bytes.Count;

            public void WriteByte(byte value)
                => _bytes.Add(value);

            public void Write(IEnumerable<byte> bytes)
            {
                if (bytes is null)
                    return;
                _bytes.AddRange(bytes);
            }

            public void WriteInt8(sbyte value)
                => _bytes.Add(unchecked((byte)value));

            public void WriteInt16(short value)
            {
                _bytes.Add((byte)value);
                _bytes.Add((byte)(value >> 8));
            }

            public void WriteInt32(int value)
            {
                _bytes.Add((byte)value);
                _bytes.Add((byte)(value >> 8));
                _bytes.Add((byte)(value >> 16));
                _bytes.Add((byte)(value >> 24));
            }

            public void WriteInt64(long value)
            {
                for (var i = 0; i < 8; i++)
                    _bytes.Add((byte)(value >> (i * 8)));
            }

            public byte[] ToArray()
                => _bytes.ToArray();
        }
    }
}
