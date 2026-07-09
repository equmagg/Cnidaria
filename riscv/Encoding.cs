using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Cnidaria.RiscV
{
    internal static class RiscVCodeEncoder
    {
        public static byte[] Encode(RiscVProgram obj, ulong imageBase = 0, IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            return RVObjectLinker.LinkFlat(obj, imageBase, externalSymbols).ToArray();
        }

        public static byte[] Encode(IEnumerable<RVInstruction> instructions, RVTarget target, IReadOnlyDictionary<string, int>? labels = null)
        {
            if (instructions is null)
                throw new ArgumentNullException(nameof(instructions));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var list = instructions.ToImmutableArray();
            var bytes = new byte[list.Length * 4];
            for (int i = 0; i < list.Length; i++)
            {
                int pc = i * 4;
                uint encoded = Encode(ResolveInstruction(list[i], pc, labels), target);
                WriteUInt32(bytes, pc, encoded, target.Endianness);
            }
            return bytes;
        }

        public static uint Encode(RVInstruction instruction, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (instruction.HasSymbol)
                throw new InvalidOperationException("Symbolic RISC-V instruction must be resolved before binary encoding");

            var metadata = RVInstructionTable.Get(instruction.Opcode);
            if (metadata.Format == RVInstructionFormat.Raw)
                return unchecked((uint)instruction.Immediate);

            ValidateTarget(instruction.Opcode, metadata, target);
            switch (metadata.Format)
            {
                case RVInstructionFormat.R:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
                    return EncodeR(metadata.Opcode, instruction.Rd, metadata.Funct3, instruction.Rs1, instruction.Rs2, metadata.Funct7);
                case RVInstructionFormat.I:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateSignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeI(metadata.Opcode, instruction.Rd, metadata.Funct3, instruction.Rs1, instruction.Immediate);
                case RVInstructionFormat.ShiftI:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateShiftAmount(instruction.Opcode, instruction.Immediate, target);
                    return EncodeShiftI(metadata.Opcode, instruction.Rd, metadata.Funct3, instruction.Rs1, instruction.Immediate, metadata.Funct7, target);
                case RVInstructionFormat.S:
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
                    ValidateSignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeS(metadata.Opcode, metadata.Funct3, instruction.Rs1, instruction.Rs2, instruction.Immediate);
                case RVInstructionFormat.FloatLoad:
                    ValidateFloatRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateSignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeIRaw(metadata.Opcode, FloatIndex(instruction.Rd), metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1), instruction.Immediate);
                case RVInstructionFormat.FloatStore:
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateFloatRegister(instruction.Rs2, nameof(instruction.Rs2));
                    ValidateSignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeSRaw(metadata.Opcode, metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1), FloatIndex(instruction.Rs2), instruction.Immediate);
                case RVInstructionFormat.FloatRRR:
                    ValidateFloatRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateFloatRegister(instruction.Rs2, nameof(instruction.Rs2));
                    return EncodeRRaw(metadata.Opcode, FloatIndex(instruction.Rd), metadata.Funct3, FloatIndex(instruction.Rs1), FloatIndex(instruction.Rs2), metadata.Funct7);
                case RVInstructionFormat.FloatCompare:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateFloatRegister(instruction.Rs2, nameof(instruction.Rs2));
                    return EncodeRRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), metadata.Funct3, FloatIndex(instruction.Rs1), FloatIndex(instruction.Rs2), metadata.Funct7);
                case RVInstructionFormat.FloatConvertFromInteger:
                    ValidateFloatRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    return EncodeRRaw(metadata.Opcode, FloatIndex(instruction.Rd), FloatingRoundingMode(instruction), RVRegisters.IntegerIndex(instruction.Rs1), FloatingConversionSource(instruction.Opcode), metadata.Funct7);
                case RVInstructionFormat.FloatConvertToInteger:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                    return EncodeRRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), FloatingRoundingMode(instruction), FloatIndex(instruction.Rs1), FloatingConversionTarget(instruction.Opcode), metadata.Funct7);
                case RVInstructionFormat.FloatConvert:
                    ValidateFloatRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                    return EncodeRRaw(metadata.Opcode, FloatIndex(instruction.Rd), FloatingRoundingMode(instruction), FloatIndex(instruction.Rs1), FloatingPrecisionSource(instruction.Opcode), metadata.Funct7);
                case RVInstructionFormat.FloatMoveToInteger:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                    return EncodeRRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), metadata.Funct3, FloatIndex(instruction.Rs1), 0, metadata.Funct7);
                case RVInstructionFormat.FloatMoveFromInteger:
                    ValidateFloatRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    return EncodeRRaw(metadata.Opcode, FloatIndex(instruction.Rd), metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1), 0, metadata.Funct7);
                case RVInstructionFormat.B:
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
                    ValidateBranchImmediate(instruction.Immediate);
                    return EncodeB(metadata.Opcode, metadata.Funct3, instruction.Rs1, instruction.Rs2, instruction.Immediate);
                case RVInstructionFormat.U:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateUpperImmediate(instruction.Immediate);
                    return EncodeU(metadata.Opcode, instruction.Rd, instruction.Immediate);
                case RVInstructionFormat.J:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateJalImmediate(instruction.Immediate);
                    return EncodeJ(metadata.Opcode, instruction.Rd, instruction.Immediate);
                case RVInstructionFormat.Fence:
                    ValidateUnsignedImmediate(instruction.Immediate, 8, nameof(instruction.Immediate));
                    return EncodeI(metadata.Opcode, RVRegister.X0, metadata.Funct3, RVRegister.X0, instruction.Immediate);
                case RVInstructionFormat.System:
                    return EncodeSystem(instruction.Opcode);
                case RVInstructionFormat.PrivilegedFence:
                    ValidateIntegerRegisterOrZero(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateIntegerRegisterOrZero(instruction.Rs2, nameof(instruction.Rs2));
                    return EncodePrivilegedFence(metadata.Funct7, instruction.Rs1, instruction.Rs2);
                case RVInstructionFormat.Csr:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateUnsignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeI(metadata.Opcode, instruction.Rd, metadata.Funct3, instruction.Rs1, instruction.Immediate);
                case RVInstructionFormat.CsrImmediate:
                    ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
                    int zimm = instruction.Rs1 == RVRegister.Invalid ? 0 : (int)instruction.Rs1;
                    ValidateUnsignedImmediate(zimm, 5, nameof(instruction.Rs1));
                    ValidateUnsignedImmediate(instruction.Immediate, 12, nameof(instruction.Immediate));
                    return EncodeI(metadata.Opcode, instruction.Rd, metadata.Funct3, (RVRegister)zimm, instruction.Immediate);
                case RVInstructionFormat.Amo:
                    return EncodeAmo(instruction, metadata);
                case RVInstructionFormat.VectorConfig:
                    return EncodeVectorConfig(instruction, metadata);
                case RVInstructionFormat.VectorLoad:
                    return EncodeVectorLoad(instruction, metadata);
                case RVInstructionFormat.VectorStore:
                    return EncodeVectorStore(instruction, metadata);
                case RVInstructionFormat.VectorOp:
                    return EncodeVectorOp(instruction, metadata);
                default:
                    throw new NotSupportedException("Unsupported RISC-V instruction format: " + metadata.Format);
            }
        }

        private static RVInstruction ResolveInstruction(RVInstruction instruction, int pc, IReadOnlyDictionary<string, int>? labels)
        {
            if (!instruction.HasSymbol)
                return instruction;
            if (labels is null || !labels.TryGetValue(instruction.Symbol!, out int targetPc))
                throw new InvalidOperationException("Unresolved RISC-V symbol: " + instruction.Symbol);

            int relative = checked(targetPc - pc);
            switch (instruction.RelocationKind)
            {
                case RVRelocationKind.RelativeBranch:
                case RVRelocationKind.RelativeJal:
                    return instruction.WithImmediate(relative);
                default:
                    throw new NotSupportedException("Unsupported RISC-V symbolic relocation: " + instruction.RelocationKind);
            }
        }


        private static byte FloatingRoundingMode(RVInstruction instruction)
        {
            if (instruction.Immediate == 0)
                return 0;
            ValidateUnsignedImmediate(instruction.Immediate, 3, nameof(instruction.Immediate));
            return (byte)instruction.Immediate;
        }

        private static int FloatingConversionSource(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.FcvtSW or RVInstrKind.FcvtDW => 0,
                RVInstrKind.FcvtSWu or RVInstrKind.FcvtDWu => 1,
                RVInstrKind.FcvtSL or RVInstrKind.FcvtDL => 2,
                RVInstrKind.FcvtSLu or RVInstrKind.FcvtDLu => 3,
                _ => throw new NotSupportedException("Unsupported floating-point conversion opcode: " + opcode),
            };

        private static int FloatingConversionTarget(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.FcvtWS or RVInstrKind.FcvtWD => 0,
                RVInstrKind.FcvtWuS or RVInstrKind.FcvtWuD => 1,
                RVInstrKind.FcvtLS or RVInstrKind.FcvtLD => 2,
                RVInstrKind.FcvtLuS or RVInstrKind.FcvtLuD => 3,
                _ => throw new NotSupportedException("Unsupported floating-point conversion opcode: " + opcode),
            };

        private static int FloatingPrecisionSource(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.FcvtDS => 0,
                RVInstrKind.FcvtSD => 1,
                _ => throw new NotSupportedException("Unsupported floating-point precision conversion opcode: " + opcode),
            };

        private static int FloatIndex(RVRegister register)
            => RVRegisters.FloatIndex(register);

        private static void ValidateTarget(RVInstrKind opcode, RVInstructionMetadata metadata, RVTarget target)
        {
            if (metadata.Requires64Bit && !target.Is64Bit)
                throw new InvalidOperationException(opcode + " requires RV64 target");
            if (!target.Has(metadata.RequiredIsa))
                throw new InvalidOperationException(opcode + " requires RISC-V extension " + metadata.RequiredIsa);
        }

        private static uint EncodeR(byte opcode, RVRegister rd, byte funct3, RVRegister rs1, RVRegister rs2, byte funct7)
            => EncodeRRaw(opcode, RVRegisters.IntegerIndex(rd), funct3, RVRegisters.IntegerIndex(rs1), RVRegisters.IntegerIndex(rs2), funct7);

        private static uint EncodeRRaw(byte opcode, int rd, byte funct3, int rs1, int rs2, byte funct7)
            => (uint)opcode
                | (((uint)rd & 0x1FU) << 7)
                | ((uint)funct3 << 12)
                | (((uint)rs1 & 0x1FU) << 15)
                | (((uint)rs2 & 0x1FU) << 20)
                | ((uint)funct7 << 25);

        private static uint EncodeI(byte opcode, RVRegister rd, byte funct3, RVRegister rs1, int immediate)
            => EncodeIRaw(opcode, RVRegisters.IntegerIndex(rd), funct3, RVRegisters.IntegerIndex(rs1), immediate);

        private static uint EncodeIRaw(byte opcode, int rd, byte funct3, int rs1, int immediate)
            => (uint)opcode
                | (((uint)rd & 0x1FU) << 7)
                | ((uint)funct3 << 12)
                | (((uint)rs1 & 0x1FU) << 15)
                | (((uint)immediate & 0xFFFU) << 20);

        private static uint EncodeShiftI(byte opcode, RVRegister rd, byte funct3, RVRegister rs1, int shamt, byte funct7, RVTarget target)
        {
            uint encodedShamt = (uint)shamt;
            uint upper = (uint)funct7 << 25;
            if (target.Is64Bit && opcode == 0x13)
                return (uint)opcode
                    | ((uint)RVRegisters.IntegerIndex(rd) << 7)
                    | ((uint)funct3 << 12)
                    | ((uint)RVRegisters.IntegerIndex(rs1) << 15)
                    | ((encodedShamt & 0x3FU) << 20)
                    | upper;
            return (uint)opcode
                | ((uint)RVRegisters.IntegerIndex(rd) << 7)
                | ((uint)funct3 << 12)
                | ((uint)RVRegisters.IntegerIndex(rs1) << 15)
                | ((encodedShamt & 0x1FU) << 20)
                | upper;
        }

        private static uint EncodeS(byte opcode, byte funct3, RVRegister rs1, RVRegister rs2, int immediate)
            => EncodeSRaw(opcode, funct3, RVRegisters.IntegerIndex(rs1), RVRegisters.IntegerIndex(rs2), immediate);

        private static uint EncodeSRaw(byte opcode, byte funct3, int rs1, int rs2, int immediate)
            => (uint)opcode
                | (((uint)immediate & 0x1FU) << 7)
                | ((uint)funct3 << 12)
                | (((uint)rs1 & 0x1FU) << 15)
                | (((uint)rs2 & 0x1FU) << 20)
                | ((((uint)immediate >> 5) & 0x7FU) << 25);

        private static uint EncodeB(byte opcode, byte funct3, RVRegister rs1, RVRegister rs2, int immediate)
        {
            uint imm = (uint)immediate;
            return (uint)opcode
                | (((imm >> 11) & 0x1U) << 7)
                | (((imm >> 1) & 0xFU) << 8)
                | ((uint)funct3 << 12)
                | ((uint)RVRegisters.IntegerIndex(rs1) << 15)
                | ((uint)RVRegisters.IntegerIndex(rs2) << 20)
                | (((imm >> 5) & 0x3FU) << 25)
                | (((imm >> 12) & 0x1U) << 31);
        }

        private static uint EncodeU(byte opcode, RVRegister rd, int immediate)
            => (uint)opcode
                | ((uint)RVRegisters.IntegerIndex(rd) << 7)
                | (((uint)immediate & 0xFFFFFU) << 12);

        private static uint EncodeJ(byte opcode, RVRegister rd, int immediate)
        {
            uint imm = (uint)immediate;
            return (uint)opcode
                | ((uint)RVRegisters.IntegerIndex(rd) << 7)
                | (((imm >> 12) & 0xFFU) << 12)
                | (((imm >> 11) & 0x1U) << 20)
                | (((imm >> 1) & 0x3FFU) << 21)
                | (((imm >> 20) & 0x1U) << 31);
        }

        private static uint EncodeSystem(RVInstrKind opcode)
        {
            return opcode switch
            {
                RVInstrKind.Ecall => 0x00000073U,
                RVInstrKind.Ebreak => 0x00100073U,
                RVInstrKind.Uret => 0x00200073U,
                RVInstrKind.Sret => 0x10200073U,
                RVInstrKind.Mret => 0x30200073U,
                RVInstrKind.Wfi => 0x10500073U,
                RVInstrKind.FenceI => 0x0000100FU,
                RVInstrKind.SfenceWInval => 0x18000073U,
                RVInstrKind.SfenceInvalIr => 0x18100073U,
                _ => throw new NotSupportedException("Unsupported RISC-V system opcode: " + opcode),
            };
        }

        private static uint EncodePrivilegedFence(byte funct7, RVRegister rs1, RVRegister rs2)
        {
            int rs1Index = rs1 == RVRegister.Invalid ? 0 : RVRegisters.IntegerIndex(rs1);
            int rs2Index = rs2 == RVRegister.Invalid ? 0 : RVRegisters.IntegerIndex(rs2);
            return 0x73U
                | ((uint)rs1Index << 15)
                | ((uint)rs2Index << 20)
                | ((uint)funct7 << 25);
        }

        private static uint EncodeAmo(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            if (instruction.Opcode is RVInstrKind.LrW or RVInstrKind.LrD)
            {
                if (instruction.Rs2 != RVRegister.Invalid && instruction.Rs2 != RVRegister.X0)
                    throw new InvalidOperationException(instruction.Opcode + " requires rs2=x0");
            }
            else
            {
                ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
            }

            uint aq = instruction.AtomicAcquire ? 1U : 0U;
            uint rl = instruction.AtomicRelease ? 1U : 0U;
            uint rs2 = instruction.Opcode is RVInstrKind.LrW or RVInstrKind.LrD ? 0U : (uint)RVRegisters.IntegerIndex(instruction.Rs2);
            return (uint)metadata.Opcode
                | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7)
                | ((uint)metadata.Funct3 << 12)
                | ((uint)RVRegisters.IntegerIndex(instruction.Rs1) << 15)
                | (rs2 << 20)
                | (rl << 25)
                | (aq << 26)
                | ((uint)metadata.Funct7 << 27);
        }

        private static uint EncodeVectorConfig(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            switch (instruction.Opcode)
            {
                case RVInstrKind.Vsetvli:
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateUnsignedImmediate(instruction.Immediate, 11, nameof(instruction.Immediate));
                    return (uint)metadata.Opcode
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7)
                        | ((uint)metadata.Funct3 << 12)
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rs1) << 15)
                        | (((uint)instruction.Immediate & 0x7FFU) << 20);
                case RVInstrKind.Vsetivli:
                    int avl = (int)instruction.Rs1;
                    ValidateUnsignedImmediate(avl, 5, nameof(instruction.Rs1));
                    ValidateUnsignedImmediate(instruction.Immediate, 10, nameof(instruction.Immediate));
                    return (uint)metadata.Opcode
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7)
                        | ((uint)metadata.Funct3 << 12)
                        | ((uint)avl << 15)
                        | (((uint)instruction.Immediate & 0x3FFU) << 20)
                        | 0xC0000000U;
                case RVInstrKind.Vsetvl:
                    ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                    ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
                    return (uint)metadata.Opcode
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7)
                        | ((uint)metadata.Funct3 << 12)
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rs1) << 15)
                        | ((uint)RVRegisters.IntegerIndex(instruction.Rs2) << 20)
                        | 0x80000000U;
                default:
                    throw new NotSupportedException("Unsupported RISC-V vector configuration opcode: " + instruction.Opcode);
            }
        }

        private static uint EncodeVectorLoad(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateVectorRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            uint vm = instruction.VectorUnmasked ? 1U : 0U;
            return (uint)metadata.Opcode
                | ((uint)RVRegisters.VectorIndex(instruction.Rd) << 7)
                | ((uint)metadata.Funct3 << 12)
                | ((uint)RVRegisters.IntegerIndex(instruction.Rs1) << 15)
                | (vm << 25);
        }

        private static uint EncodeVectorStore(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateVectorRegister(instruction.Rs2, nameof(instruction.Rs2));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            uint vm = instruction.VectorUnmasked ? 1U : 0U;
            return (uint)metadata.Opcode
                | ((uint)RVRegisters.VectorIndex(instruction.Rs2) << 7)
                | ((uint)metadata.Funct3 << 12)
                | ((uint)RVRegisters.IntegerIndex(instruction.Rs1) << 15)
                | (vm << 25);
        }

        private static uint EncodeVectorOp(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateVectorRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateVectorRegister(instruction.Rs2, nameof(instruction.Rs2));
            uint vm = instruction.VectorUnmasked ? 1U : 0U;
            uint source1;
            if (metadata.Funct3 == 0)
            {
                ValidateVectorRegister(instruction.Rs1, nameof(instruction.Rs1));
                source1 = (uint)RVRegisters.VectorIndex(instruction.Rs1);
            }
            else if (metadata.Funct3 == 4)
            {
                ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                source1 = (uint)RVRegisters.IntegerIndex(instruction.Rs1);
            }
            else if (metadata.Funct3 == 3)
            {
                ValidateSignedImmediate(instruction.Immediate, 5, nameof(instruction.Immediate));
                source1 = (uint)instruction.Immediate & 0x1FU;
            }
            else
            {
                throw new NotSupportedException("Unsupported RISC-V vector funct3: " + metadata.Funct3);
            }

            return (uint)metadata.Opcode
                | ((uint)RVRegisters.VectorIndex(instruction.Rd) << 7)
                | ((uint)metadata.Funct3 << 12)
                | (source1 << 15)
                | ((uint)RVRegisters.VectorIndex(instruction.Rs2) << 20)
                | (vm << 25)
                | ((uint)metadata.Funct7 << 26);
        }

        private static void ValidateIntegerRegister(RVRegister register, string name)
        {
            if (!RVRegisters.IsInteger(register))
                throw new ArgumentException("Expected RISC-V integer register", name);
        }

        private static void ValidateIntegerRegisterOrZero(RVRegister register, string name)
        {
            if (register != RVRegister.Invalid && !RVRegisters.IsInteger(register))
                throw new ArgumentException("Expected RISC-V integer register", name);
        }

        private static void ValidateFloatRegister(RVRegister register, string name)
        {
            if (!RVRegisters.IsFloat(register))
                throw new ArgumentException("Expected RISC-V floating-point register", name);
        }

        private static void ValidateVectorRegister(RVRegister register, string name)
        {
            if (!RVRegisters.IsVector(register))
                throw new ArgumentException("Expected RISC-V vector register", name);
        }

        private static void ValidateSignedImmediate(int value, int bits, string name)
        {
            int min = -(1 << (bits - 1));
            int max = (1 << (bits - 1)) - 1;
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(name, value, "Signed immediate does not fit " + bits.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bits");
        }

        private static void ValidateUnsignedImmediate(int value, int bits, string name)
        {
            if (value < 0 || value > ((1 << bits) - 1))
                throw new ArgumentOutOfRangeException(name, value, "Unsigned immediate does not fit " + bits.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bits");
        }

        private static void ValidateBranchImmediate(int immediate)
        {
            if ((immediate & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "Branch immediate must be 2-byte aligned");
            ValidateSignedImmediate(immediate, 13, nameof(immediate));
        }

        private static void ValidateJalImmediate(int immediate)
        {
            if ((immediate & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "JAL immediate must be 2-byte aligned");
            ValidateSignedImmediate(immediate, 21, nameof(immediate));
        }

        private static void ValidateUpperImmediate(int immediate)
        {
            if (immediate < -(1 << 19) || immediate > 0xFFFFF)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "Upper immediate must fit in 20 bits");
        }

        private static void ValidateShiftAmount(RVInstrKind opcode, int shamt, RVTarget target)
        {
            int max = opcode is RVInstrKind.Slliw or RVInstrKind.Srliw or RVInstrKind.Sraiw ? 31 : target.Is64Bit ? 63 : 31;
            if (shamt < 0 || shamt > max)
                throw new ArgumentOutOfRangeException(nameof(shamt), shamt, "Invalid shift amount");
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
            {
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                bytes[offset + 2] = (byte)(value >> 16);
                bytes[offset + 3] = (byte)(value >> 24);
                return;
            }
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }

    internal static class RiscVCodeDecoder
    {
        public static RiscVProgram DecodeObject(ReadOnlySpan<byte> bytes, RVTarget target)
            => new RiscVProgram(target, Decode(bytes, target));

        public static ImmutableArray<RVInstruction> Decode(ReadOnlySpan<byte> bytes, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if ((bytes.Length & 3) != 0)
                throw new InvalidDataException("RISC-V byte stream length must be divisible by 4");

            var builder = ImmutableArray.CreateBuilder<RVInstruction>(bytes.Length / 4);
            for (int i = 0; i < bytes.Length; i += 4)
                builder.Add(Decode(ReadUInt32(bytes, i, target.Endianness), target));
            return builder.MoveToImmutable();
        }

        public static RVInstruction Decode(uint word, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            byte opcode = (byte)(word & 0x7F);
            byte funct3 = (byte)((word >> 12) & 0x7);
            byte funct7 = (byte)((word >> 25) & 0x7F);
            var rd = (RVRegister)((word >> 7) & 0x1F);
            var rs1 = (RVRegister)((word >> 15) & 0x1F);
            var rs2 = (RVRegister)((word >> 20) & 0x1F);

            try
            {
                switch (opcode)
                {
                    case 0x37:
                        return RVInstruction.U(RVInstrKind.Lui, rd, SignExtend((int)(word >> 12), 20));
                    case 0x17:
                        return RVInstruction.U(RVInstrKind.Auipc, rd, SignExtend((int)(word >> 12), 20));
                    case 0x6F:
                        return RVInstruction.J(RVInstrKind.Jal, rd, DecodeJImmediate(word));
                    case 0x67 when funct3 == 0:
                        return RVInstruction.I(RVInstrKind.Jalr, rd, rs1, SignExtend((int)(word >> 20), 12));
                    case 0x63:
                        return DecodeBranch(word, funct3, rs1, rs2);
                    case 0x03:
                        return DecodeLoad(word, funct3, rd, rs1, target);
                    case 0x23:
                        return DecodeStore(word, funct3, rs1, rs2, target);
                    case 0x13:
                        return DecodeOpImmediate(word, funct3, funct7, rd, rs1, target);
                    case 0x33:
                        return DecodeOp(word, funct3, funct7, rd, rs1, rs2, target);
                    case 0x1B when target.Is64Bit:
                        return DecodeOpImmediate32(word, funct3, funct7, rd, rs1);
                    case 0x3B when target.Is64Bit:
                        return DecodeOp32(word, funct3, funct7, rd, rs1, rs2, target);
                    case 0x2F:
                        return DecodeAmo(word, funct3, rd, rs1, rs2, target);
                    case 0x0F:
                        if (funct3 == 0)
                            return new RVInstruction(RVInstrKind.Fence, immediate: (int)((word >> 20) & 0xFF));
                        if (funct3 == 1 && word == 0x0000100FU)
                            return new RVInstruction(RVInstrKind.FenceI);
                        break;
                    case 0x73:
                        return DecodeSystem(word, funct3, rd, rs1, rs2);
                    case 0x57 when target.HasV:
                        return DecodeVector(word, funct3);
                    case 0x07 when target.HasV:
                        return DecodeVectorLoad(word, funct3, rd, rs1);
                    case 0x27 when target.HasV:
                        return DecodeVectorStore(word, funct3, rd, rs1);
                }
            }
            catch (InvalidDataException)
            {
                return RVInstruction.Raw(word);
            }

            return RVInstruction.Raw(word);
        }

        private static RVInstruction DecodeBranch(uint word, byte funct3, RVRegister rs1, RVRegister rs2)
        {
            var opcode = funct3 switch
            {
                0 => RVInstrKind.Beq,
                1 => RVInstrKind.Bne,
                4 => RVInstrKind.Blt,
                5 => RVInstrKind.Bge,
                6 => RVInstrKind.Bltu,
                7 => RVInstrKind.Bgeu,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V branch funct3");
            return RVInstruction.B(opcode, rs1, rs2, DecodeBImmediate(word));
        }

        private static RVInstruction DecodeLoad(uint word, byte funct3, RVRegister rd, RVRegister rs1, RVTarget target)
        {
            var opcode = funct3 switch
            {
                0 => RVInstrKind.Lb,
                1 => RVInstrKind.Lh,
                2 => RVInstrKind.Lw,
                3 when target.Is64Bit => RVInstrKind.Ld,
                4 => RVInstrKind.Lbu,
                5 => RVInstrKind.Lhu,
                6 when target.Is64Bit => RVInstrKind.Lwu,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V load funct3");
            return RVInstruction.I(opcode, rd, rs1, SignExtend((int)(word >> 20), 12));
        }

        private static RVInstruction DecodeStore(uint word, byte funct3, RVRegister rs1, RVRegister rs2, RVTarget target)
        {
            var opcode = funct3 switch
            {
                0 => RVInstrKind.Sb,
                1 => RVInstrKind.Sh,
                2 => RVInstrKind.Sw,
                3 when target.Is64Bit => RVInstrKind.Sd,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V store funct3");
            int imm = (int)(((word >> 7) & 0x1FU) | (((word >> 25) & 0x7FU) << 5));
            return RVInstruction.S(opcode, rs2, rs1, SignExtend(imm, 12));
        }

        private static RVInstruction DecodeOpImmediate(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1, RVTarget target)
        {
            int imm = SignExtend((int)(word >> 20), 12);
            switch (funct3)
            {
                case 0:
                    return RVInstruction.I(RVInstrKind.Addi, rd, rs1, imm);
                case 2:
                    return RVInstruction.I(RVInstrKind.Slti, rd, rs1, imm);
                case 3:
                    return RVInstruction.I(RVInstrKind.Sltiu, rd, rs1, imm);
                case 4:
                    return RVInstruction.I(RVInstrKind.Xori, rd, rs1, imm);
                case 6:
                    return RVInstruction.I(RVInstrKind.Ori, rd, rs1, imm);
                case 7:
                    return RVInstruction.I(RVInstrKind.Andi, rd, rs1, imm);
                case 1:
                    if (funct7 == 0 || (target.Is64Bit && (funct7 & 0x7E) == 0))
                        return RVInstruction.I(RVInstrKind.Slli, rd, rs1, (int)((word >> 20) & (target.Is64Bit ? 0x3FU : 0x1FU)));
                    break;
                case 5:
                    if (funct7 == 0 || (target.Is64Bit && (funct7 & 0x7E) == 0))
                        return RVInstruction.I(RVInstrKind.Srli, rd, rs1, (int)((word >> 20) & (target.Is64Bit ? 0x3FU : 0x1FU)));
                    if (funct7 == 0x20 || (target.Is64Bit && (funct7 & 0x7E) == 0x20))
                        return RVInstruction.I(RVInstrKind.Srai, rd, rs1, (int)((word >> 20) & (target.Is64Bit ? 0x3FU : 0x1FU)));
                    break;
            }
            throw new InvalidDataException("Invalid RISC-V immediate arithmetic instruction");
        }

        private static RVInstruction DecodeOp(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1, RVRegister rs2, RVTarget target)
        {
            var opcode = (funct7, funct3) switch
            {
                (0x00, 0) => RVInstrKind.Add,
                (0x20, 0) => RVInstrKind.Sub,
                (0x00, 1) => RVInstrKind.Sll,
                (0x00, 2) => RVInstrKind.Slt,
                (0x00, 3) => RVInstrKind.Sltu,
                (0x00, 4) => RVInstrKind.Xor,
                (0x00, 5) => RVInstrKind.Srl,
                (0x20, 5) => RVInstrKind.Sra,
                (0x00, 6) => RVInstrKind.Or,
                (0x00, 7) => RVInstrKind.And,
                (0x01, 0) when target.HasM => RVInstrKind.Mul,
                (0x01, 1) when target.HasM => RVInstrKind.Mulh,
                (0x01, 2) when target.HasM => RVInstrKind.Mulhsu,
                (0x01, 3) when target.HasM => RVInstrKind.Mulhu,
                (0x01, 4) when target.HasM => RVInstrKind.Div,
                (0x01, 5) when target.HasM => RVInstrKind.Divu,
                (0x01, 6) when target.HasM => RVInstrKind.Rem,
                (0x01, 7) when target.HasM => RVInstrKind.Remu,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V register arithmetic instruction");
            return RVInstruction.R(opcode, rd, rs1, rs2);
        }

        private static RVInstruction DecodeOpImmediate32(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1)
        {
            switch (funct3)
            {
                case 0:
                    return RVInstruction.I(RVInstrKind.Addiw, rd, rs1, SignExtend((int)(word >> 20), 12));
                case 1 when funct7 == 0x00:
                    return RVInstruction.I(RVInstrKind.Slliw, rd, rs1, (int)((word >> 20) & 0x1FU));
                case 5 when funct7 == 0x00:
                    return RVInstruction.I(RVInstrKind.Srliw, rd, rs1, (int)((word >> 20) & 0x1FU));
                case 5 when funct7 == 0x20:
                    return RVInstruction.I(RVInstrKind.Sraiw, rd, rs1, (int)((word >> 20) & 0x1FU));
            }
            throw new InvalidDataException("Invalid RV64 immediate word instruction");
        }

        private static RVInstruction DecodeOp32(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1, RVRegister rs2, RVTarget target)
        {
            var opcode = (funct7, funct3) switch
            {
                (0x00, 0) => RVInstrKind.Addw,
                (0x20, 0) => RVInstrKind.Subw,
                (0x00, 1) => RVInstrKind.Sllw,
                (0x00, 5) => RVInstrKind.Srlw,
                (0x20, 5) => RVInstrKind.Sraw,
                (0x01, 0) when target.HasM => RVInstrKind.Mulw,
                (0x01, 4) when target.HasM => RVInstrKind.Divw,
                (0x01, 5) when target.HasM => RVInstrKind.Divuw,
                (0x01, 6) when target.HasM => RVInstrKind.Remw,
                (0x01, 7) when target.HasM => RVInstrKind.Remuw,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RV64 word instruction");
            return RVInstruction.R(opcode, rd, rs1, rs2);
        }

        private static RVInstruction DecodeAmo(uint word, byte funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, RVTarget target)
        {
            byte funct5 = (byte)((word >> 27) & 0x1F);
            bool acquire = ((word >> 26) & 1) != 0;
            bool release = ((word >> 25) & 1) != 0;
            bool is64 = funct3 == 3;
            var opcode = (funct5, funct3) switch
            {
                (0x02, 2) when rs2 == RVRegister.X0 => RVInstrKind.LrW,
                (0x03, 2) => RVInstrKind.ScW,
                (0x01, 2) => RVInstrKind.AmoSwapW,
                (0x00, 2) => RVInstrKind.AmoAddW,
                (0x04, 2) => RVInstrKind.AmoXorW,
                (0x0C, 2) => RVInstrKind.AmoAndW,
                (0x08, 2) => RVInstrKind.AmoOrW,
                (0x10, 2) => RVInstrKind.AmoMinW,
                (0x14, 2) => RVInstrKind.AmoMaxW,
                (0x18, 2) => RVInstrKind.AmoMinuW,
                (0x1C, 2) => RVInstrKind.AmoMaxuW,
                (0x02, 3) when target.Is64Bit && rs2 == RVRegister.X0 => RVInstrKind.LrD,
                (0x03, 3) when target.Is64Bit => RVInstrKind.ScD,
                (0x01, 3) when target.Is64Bit => RVInstrKind.AmoSwapD,
                (0x00, 3) when target.Is64Bit => RVInstrKind.AmoAddD,
                (0x04, 3) when target.Is64Bit => RVInstrKind.AmoXorD,
                (0x0C, 3) when target.Is64Bit => RVInstrKind.AmoAndD,
                (0x08, 3) when target.Is64Bit => RVInstrKind.AmoOrD,
                (0x10, 3) when target.Is64Bit => RVInstrKind.AmoMinD,
                (0x14, 3) when target.Is64Bit => RVInstrKind.AmoMaxD,
                (0x18, 3) when target.Is64Bit => RVInstrKind.AmoMinuD,
                (0x1C, 3) when target.Is64Bit => RVInstrKind.AmoMaxuD,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid || (is64 && !target.Is64Bit))
                throw new InvalidDataException("Invalid RISC-V atomic instruction");
            return RVInstruction.Amo(opcode, rd, rs1, rs2, acquire, release);
        }

        private static RVInstruction DecodeSystem(uint word, byte funct3, RVRegister rd, RVRegister rs1, RVRegister rs2)
        {
            if (funct3 == 0)
            {
                if (word == 0x00000073U)
                    return new RVInstruction(RVInstrKind.Ecall);
                if (word == 0x00100073U)
                    return new RVInstruction(RVInstrKind.Ebreak);
                if (word == 0x00200073U)
                    return new RVInstruction(RVInstrKind.Uret);
                if (word == 0x10200073U)
                    return new RVInstruction(RVInstrKind.Sret);
                if (word == 0x30200073U)
                    return new RVInstruction(RVInstrKind.Mret);
                if (word == 0x10500073U)
                    return new RVInstruction(RVInstrKind.Wfi);
                if (word == 0x18000073U)
                    return new RVInstruction(RVInstrKind.SfenceWInval);
                if (word == 0x18100073U)
                    return new RVInstruction(RVInstrKind.SfenceInvalIr);

                byte funct7 = (byte)((word >> 25) & 0x7F);
                var fenceOpcode = funct7 switch
                {
                    0x09 => RVInstrKind.SfenceVma,
                    0x0B => RVInstrKind.SinvalVma,
                    0x11 => RVInstrKind.HfenceVvma,
                    0x31 => RVInstrKind.HfenceGvma,
                    _ => RVInstrKind.Invalid,
                };
                if (fenceOpcode != RVInstrKind.Invalid)
                    return new RVInstruction(fenceOpcode, rs1: rs1, rs2: rs2);
            }

            var opcode = funct3 switch
            {
                1 => RVInstrKind.Csrrw,
                2 => RVInstrKind.Csrrs,
                3 => RVInstrKind.Csrrc,
                5 => RVInstrKind.Csrrwi,
                6 => RVInstrKind.Csrrsi,
                7 => RVInstrKind.Csrrci,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V system instruction");
            return new RVInstruction(opcode, rd, rs1, RVRegister.Invalid, (int)(word >> 20));
        }

        private static RVInstruction DecodeVector(uint word, byte funct3)
        {
            var rd = (RVRegister)(64 + (int)((word >> 7) & 0x1F));
            var xrd = (RVRegister)((word >> 7) & 0x1F);
            var rs1 = (RVRegister)((word >> 15) & 0x1F);
            var vs1 = (RVRegister)(64 + (int)((word >> 15) & 0x1F));
            var vs2 = (RVRegister)(64 + (int)((word >> 20) & 0x1F));
            bool vm = ((word >> 25) & 1) != 0;
            byte funct6 = (byte)((word >> 26) & 0x3F);

            if (funct3 == 7)
            {
                if ((word & 0xC0000000U) == 0xC0000000U)
                    return RVInstruction.Vsetivli(xrd, (int)((word >> 15) & 0x1F), (int)((word >> 20) & 0x3FF));
                if ((word & 0x80000000U) != 0)
                    return RVInstruction.Vsetvl(xrd, rs1, (RVRegister)((word >> 20) & 0x1F));
                return RVInstruction.Vsetvli(xrd, rs1, (int)((word >> 20) & 0x7FF));
            }

            var opcode = (funct6, funct3) switch
            {
                (0, 0) => RVInstrKind.VaddVv,
                (0, 4) => RVInstrKind.VaddVx,
                (0, 3) => RVInstrKind.VaddVi,
                (2, 0) => RVInstrKind.VsubVv,
                (2, 4) => RVInstrKind.VsubVx,
                (3, 4) => RVInstrKind.VrsubVx,
                (3, 3) => RVInstrKind.VrsubVi,
                (9, 0) => RVInstrKind.VandVv,
                (9, 4) => RVInstrKind.VandVx,
                (9, 3) => RVInstrKind.VandVi,
                (10, 0) => RVInstrKind.VorVv,
                (10, 4) => RVInstrKind.VorVx,
                (10, 3) => RVInstrKind.VorVi,
                (11, 0) => RVInstrKind.VxorVv,
                (11, 4) => RVInstrKind.VxorVx,
                (11, 3) => RVInstrKind.VxorVi,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V vector instruction");
            if (funct3 == 0)
                return RVInstruction.Vv(opcode, rd, vs2, vs1, vm);
            if (funct3 == 4)
                return RVInstruction.Vx(opcode, rd, vs2, rs1, vm);
            return RVInstruction.Vi(opcode, rd, vs2, SignExtend((int)((word >> 15) & 0x1F), 5), vm);
        }

        private static RVInstruction DecodeVectorLoad(uint word, byte width, RVRegister rd, RVRegister rs1)
        {
            if (((word >> 26) & 0x3F) != 0 || ((word >> 20) & 0x1F) != 0)
                throw new InvalidDataException("Invalid RISC-V vector load");
            var opcode = width switch
            {
                0 => RVInstrKind.Vle8V,
                5 => RVInstrKind.Vle16V,
                6 => RVInstrKind.Vle32V,
                7 => RVInstrKind.Vle64V,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V vector load width");
            return RVInstruction.Vl(opcode, (RVRegister)(64 + (int)rd), rs1, ((word >> 25) & 1) != 0);
        }

        private static RVInstruction DecodeVectorStore(uint word, byte width, RVRegister rd, RVRegister rs1)
        {
            if (((word >> 26) & 0x3F) != 0 || ((word >> 20) & 0x1F) != 0)
                throw new InvalidDataException("Invalid RISC-V vector store");
            var opcode = width switch
            {
                0 => RVInstrKind.Vse8V,
                5 => RVInstrKind.Vse16V,
                6 => RVInstrKind.Vse32V,
                7 => RVInstrKind.Vse64V,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V vector store width");
            return RVInstruction.Vs(opcode, (RVRegister)(64 + (int)rd), rs1, ((word >> 25) & 1) != 0);
        }

        private static int DecodeBImmediate(uint word)
        {
            int imm = (int)((((word >> 31) & 0x1U) << 12)
                | (((word >> 7) & 0x1U) << 11)
                | (((word >> 25) & 0x3FU) << 5)
                | (((word >> 8) & 0xFU) << 1));
            return SignExtend(imm, 13);
        }

        private static int DecodeJImmediate(uint word)
        {
            int imm = (int)((((word >> 31) & 0x1U) << 20)
                | (((word >> 12) & 0xFFU) << 12)
                | (((word >> 20) & 0x1U) << 11)
                | (((word >> 21) & 0x3FFU) << 1));
            return SignExtend(imm, 21);
        }

        private static int SignExtend(int value, int bits)
        {
            int shift = 32 - bits;
            return (value << shift) >> shift;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
                return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            return (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
        }
    }
}
