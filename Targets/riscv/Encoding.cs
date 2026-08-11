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
            var bytes = new byte[RVInstructionTable.GetEncodedSize(list)];
            var offset = 0;
            for (int i = 0; i < list.Length; i++)
            {
                var instruction = ResolveInstruction(list[i], offset, labels);
                var encoded = Encode(instruction, target);
                var size = RVInstructionTable.GetEncodedSize(instruction.Opcode);
                WriteInstruction(bytes, offset, encoded, size, target.Endianness);
                offset = checked(offset + size);
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
            if (metadata.Format == RVInstructionFormat.Raw || metadata.Format == RVInstructionFormat.Raw16)
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
                case RVInstructionFormat.BitmanipUnary:
                    return EncodeBitmanipUnary(instruction, metadata, target);
                case RVInstructionFormat.BitmanipShiftI:
                    return EncodeBitmanipShiftI(instruction, metadata, target);
                case RVInstructionFormat.Compressed:
                    return EncodeCompressed(instruction, target);
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
                case RVInstructionFormat.HypervisorLoad:
                    return EncodeHypervisorLoad(instruction, metadata);
                case RVInstructionFormat.HypervisorStore:
                    return EncodeHypervisorStore(instruction, metadata);
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
                throw new InvalidOperationException($"Unresolved symbol: {instruction.Symbol}");

            int relative = checked(targetPc - pc);
            switch (instruction.RelocationKind)
            {
                case RVRelocationKind.RelativeBranch:
                case RVRelocationKind.RelativeJal:
                    return instruction.WithImmediate(relative);
                default:
                    throw new NotSupportedException($"Unsupported symbolic relocation: {instruction.RelocationKind}");
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

        private static uint EncodeHypervisorLoad(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            return EncodeRRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), metadata.Funct3,
                RVRegisters.IntegerIndex(instruction.Rs1), HypervisorLoadSelector(instruction.Opcode), metadata.Funct7);
        }

        private static uint EncodeHypervisorStore(RVInstruction instruction, RVInstructionMetadata metadata)
        {
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
            return EncodeRRaw(metadata.Opcode, 0, metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1),
                RVRegisters.IntegerIndex(instruction.Rs2), metadata.Funct7);
        }

        private static int HypervisorLoadSelector(RVInstrKind opcode)
            => opcode switch
            {
                RVInstrKind.HlvBu or RVInstrKind.HlvHu or RVInstrKind.HlvWu => 1,
                RVInstrKind.HlvxHu or RVInstrKind.HlvxWu => 3,
                RVInstrKind.HlvB or RVInstrKind.HlvH or RVInstrKind.HlvW or RVInstrKind.HlvD => 0,
                _ => throw new NotSupportedException("Unsupported RISC-V hypervisor load opcode: " + opcode),
            };

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
            if (metadata.Funct3 is 0 or 1 or 2)
            {
                ValidateVectorRegister(instruction.Rs1, nameof(instruction.Rs1));
                source1 = (uint)RVRegisters.VectorIndex(instruction.Rs1);
            }
            else if (metadata.Funct3 is 4 or 6)
            {
                ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
                source1 = (uint)RVRegisters.IntegerIndex(instruction.Rs1);
            }
            else if (metadata.Funct3 == 5)
            {
                ValidateFloatRegister(instruction.Rs1, nameof(instruction.Rs1));
                source1 = (uint)RVRegisters.FloatIndex(instruction.Rs1);
            }
            else if (metadata.Funct3 == 3)
            {
                if (IsUnsignedVectorImmediate(instruction.Opcode))
                    ValidateUnsignedImmediate(instruction.Immediate, 5, nameof(instruction.Immediate));
                else
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

        private static uint EncodeBitmanipUnary(RVInstruction instruction, RVInstructionMetadata metadata, RVTarget target)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            if (instruction.Opcode == RVInstrKind.ZextH)
                return EncodeRRaw(target.Is64Bit ? (byte)0x3B : (byte)0x33, RVRegisters.IntegerIndex(instruction.Rd), 4, RVRegisters.IntegerIndex(instruction.Rs1), 0, 0x04);
            return EncodeIRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1), BitmanipUnaryImmediate(instruction.Opcode, target));
        }

        private static uint EncodeBitmanipShiftI(RVInstruction instruction, RVInstructionMetadata metadata, RVTarget target)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateIntegerRegister(instruction.Rs1, nameof(instruction.Rs1));
            var max = instruction.Opcode == RVInstrKind.Roriw ? 31 : target.Is64Bit ? 63 : 31;
            if (instruction.Immediate < 0 || instruction.Immediate > max)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), instruction.Immediate, "Invalid bitmanip shift amount");
            var shamt = instruction.Immediate;
            var imm12 = instruction.Opcode switch
            {
                RVInstrKind.SlliUw => (0x02 << 6) | shamt,
                RVInstrKind.Rori when target.Is64Bit => (0x18 << 6) | shamt,
                RVInstrKind.Bclri or RVInstrKind.Bexti or RVInstrKind.Binvi or RVInstrKind.Bseti when target.Is64Bit => ((metadata.Funct7 >> 1) << 6) | shamt,
                _ => (metadata.Funct7 << 5) | shamt,
            };
            return EncodeIRaw(metadata.Opcode, RVRegisters.IntegerIndex(instruction.Rd), metadata.Funct3, RVRegisters.IntegerIndex(instruction.Rs1), imm12);
        }

        private static int BitmanipUnaryImmediate(RVInstrKind opcode, RVTarget target)
            => opcode switch
            {
                RVInstrKind.Clz or RVInstrKind.Clzw => 0x600,
                RVInstrKind.Ctz or RVInstrKind.Ctzw => 0x601,
                RVInstrKind.Cpop or RVInstrKind.Cpopw => 0x602,
                RVInstrKind.SextB => 0x604,
                RVInstrKind.SextH => 0x605,
                RVInstrKind.OrcB => 0x287,
                RVInstrKind.Rev8 => target.Is64Bit ? 0x6B8 : 0x698,
                _ => throw new NotSupportedException("Unsupported RISC-V bitmanip unary opcode: " + opcode),
            };

        private static uint EncodeCompressed(RVInstruction instruction, RVTarget target)
        {
            ValidateTarget(instruction.Opcode, RVInstructionTable.Get(instruction.Opcode), target);
            return instruction.Opcode switch
            {
                RVInstrKind.CAddi4Spn => EncodeCAddi4Spn(instruction),
                RVInstrKind.CFld => EncodeCFloatLoadStoreDouble(0, 1, instruction.Rd, instruction.Rs1, RVRegister.Invalid, instruction.Immediate),
                RVInstrKind.CLw => EncodeCLoadStore(0, 2, instruction.Rd, instruction.Rs1, RVRegister.Invalid, instruction.Immediate),
                RVInstrKind.CFlw => EncodeCFloatLoadStore(0, 3, instruction.Rd, instruction.Rs1, RVRegister.Invalid, instruction.Immediate, target),
                RVInstrKind.CFsd => EncodeCFloatLoadStoreDouble(0, 5, RVRegister.Invalid, instruction.Rs1, instruction.Rs2, instruction.Immediate),
                RVInstrKind.CSw => EncodeCLoadStore(0, 6, RVRegister.Invalid, instruction.Rs1, instruction.Rs2, instruction.Immediate),
                RVInstrKind.CFsw => EncodeCFloatLoadStore(0, 7, RVRegister.Invalid, instruction.Rs1, instruction.Rs2, instruction.Immediate, target),
                RVInstrKind.CLd => EncodeCLoadStoreDouble(0, 3, instruction.Rd, instruction.Rs1, RVRegister.Invalid, instruction.Immediate),
                RVInstrKind.CSd => EncodeCLoadStoreDouble(0, 7, RVRegister.Invalid, instruction.Rs1, instruction.Rs2, instruction.Immediate),
                RVInstrKind.CNop => 0x0001U,
                RVInstrKind.CAddi => EncodeCI(1, 0, instruction.Rd, instruction.Immediate, true),
                RVInstrKind.CJal => EncodeCJ(1, instruction.Immediate),
                RVInstrKind.CAddiw => EncodeCI(1, 1, instruction.Rd, instruction.Immediate, false),
                RVInstrKind.CLi => EncodeCI(1, 2, instruction.Rd, instruction.Immediate, false),
                RVInstrKind.CAddi16Sp => EncodeCAddi16Sp(instruction),
                RVInstrKind.CLui => EncodeCI(1, 3, instruction.Rd, instruction.Immediate, true),
                RVInstrKind.CSrli => EncodeCShift(0, instruction.Rd, instruction.Immediate, target),
                RVInstrKind.CSrai => EncodeCShift(1, instruction.Rd, instruction.Immediate, target),
                RVInstrKind.CAndi => EncodeCAndi(instruction),
                RVInstrKind.CSub => EncodeCRegisterArithmetic(0, false, instruction.Rd, instruction.Rs2),
                RVInstrKind.CXor => EncodeCRegisterArithmetic(1, false, instruction.Rd, instruction.Rs2),
                RVInstrKind.COr => EncodeCRegisterArithmetic(2, false, instruction.Rd, instruction.Rs2),
                RVInstrKind.CAnd => EncodeCRegisterArithmetic(3, false, instruction.Rd, instruction.Rs2),
                RVInstrKind.CSubw => EncodeCRegisterArithmetic(0, true, instruction.Rd, instruction.Rs2),
                RVInstrKind.CAddw => EncodeCRegisterArithmetic(1, true, instruction.Rd, instruction.Rs2),
                RVInstrKind.CJ => EncodeCJ(5, instruction.Immediate),
                RVInstrKind.CBeqz => EncodeCB(6, instruction.Rs1, instruction.Immediate),
                RVInstrKind.CBnez => EncodeCB(7, instruction.Rs1, instruction.Immediate),
                RVInstrKind.CSlli => EncodeCSlli(instruction, target),
                RVInstrKind.CLwSp => EncodeCLwSp(instruction),
                RVInstrKind.CLdSp => EncodeCLdSp(instruction),
                RVInstrKind.CJr => EncodeCR(0, instruction.Rs1, RVRegister.X0),
                RVInstrKind.CMv => EncodeCR(0, instruction.Rd, instruction.Rs2),
                RVInstrKind.CEbreak => 0x9002U,
                RVInstrKind.CJalr => EncodeCR(1, instruction.Rs1, RVRegister.X0),
                RVInstrKind.CAdd => EncodeCR(1, instruction.Rd, instruction.Rs2),
                RVInstrKind.CSwSp => EncodeCSwSp(instruction),
                RVInstrKind.CSdSp => EncodeCSdSp(instruction),
                _ => throw new NotSupportedException("Unsupported RISC-V compressed opcode: " + instruction.Opcode),
            };
        }

        private static uint EncodeCAddi4Spn(RVInstruction instruction)
        {
            ValidateCompressedRegister(instruction.Rd, nameof(instruction.Rd));
            if (instruction.Rs1 != RVRegister.X2 && instruction.Rs1 != RVRegister.Invalid)
                throw new ArgumentException("c.addi4spn base register must be sp", nameof(instruction.Rs1));
            var imm = instruction.Immediate;
            ValidateUnsignedImmediate(imm, 10, nameof(instruction.Immediate));
            if (imm == 0 || (imm & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.addi4spn immediate must be non-zero and 4-byte aligned");
            return ((uint)((imm >> 4) & 0x3) << 11)
                | ((uint)((imm >> 6) & 0xF) << 7)
                | ((uint)((imm >> 2) & 0x1) << 6)
                | ((uint)((imm >> 3) & 0x1) << 5)
                | ((uint)CompressedRegisterIndex(instruction.Rd) << 2);
        }

        private static uint EncodeCLoadStore(int quadrant, int funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, int immediate)
        {
            var dataReg = rd != RVRegister.Invalid ? rd : rs2;
            ValidateCompressedRegister(dataReg, rd != RVRegister.Invalid ? nameof(rd) : nameof(rs2));
            ValidateCompressedRegister(rs1, nameof(rs1));
            ValidateUnsignedImmediate(immediate, 7, nameof(immediate));
            if ((immediate & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "c.lw/c.sw immediate must be 4-byte aligned");
            return (uint)quadrant | ((uint)funct3 << 13) | ((uint)((immediate >> 6) & 1) << 5) | ((uint)((immediate >> 3) & 7) << 10) | ((uint)((immediate >> 2) & 1) << 6) | ((uint)CompressedRegisterIndex(rs1) << 7) | ((uint)CompressedRegisterIndex(dataReg) << 2);
        }

        private static uint EncodeCLoadStoreDouble(int quadrant, int funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, int immediate)
        {
            var dataReg = rd != RVRegister.Invalid ? rd : rs2;
            ValidateCompressedRegister(dataReg, rd != RVRegister.Invalid ? nameof(rd) : nameof(rs2));
            ValidateCompressedRegister(rs1, nameof(rs1));
            ValidateUnsignedImmediate(immediate, 8, nameof(immediate));
            if ((immediate & 7) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "c.ld/c.sd immediate must be 8-byte aligned");
            return (uint)quadrant | ((uint)funct3 << 13) | ((uint)((immediate >> 3) & 7) << 10) | ((uint)((immediate >> 6) & 3) << 5) | ((uint)CompressedRegisterIndex(rs1) << 7) | ((uint)CompressedRegisterIndex(dataReg) << 2);
        }

        private static uint EncodeCFloatLoadStore(int quadrant, int funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, int immediate, RVTarget target)
        {
            if (!target.Is32Bit)
                throw new InvalidOperationException("c.flw/c.fsw require RV32 target");
            var dataReg = rd != RVRegister.Invalid ? rd : rs2;
            ValidateCompressedFloatRegister(dataReg, rd != RVRegister.Invalid ? nameof(rd) : nameof(rs2));
            ValidateCompressedRegister(rs1, nameof(rs1));
            ValidateUnsignedImmediate(immediate, 7, nameof(immediate));
            if ((immediate & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "c.flw/c.fsw immediate must be 4-byte aligned");
            return (uint)quadrant | ((uint)funct3 << 13) | ((uint)((immediate >> 6) & 1) << 5) | ((uint)((immediate >> 3) & 7) << 10) | ((uint)((immediate >> 2) & 1) << 6) | ((uint)CompressedRegisterIndex(rs1) << 7) | ((uint)CompressedFloatRegisterIndex(dataReg) << 2);
        }

        private static uint EncodeCFloatLoadStoreDouble(int quadrant, int funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, int immediate)
        {
            var dataReg = rd != RVRegister.Invalid ? rd : rs2;
            ValidateCompressedFloatRegister(dataReg, rd != RVRegister.Invalid ? nameof(rd) : nameof(rs2));
            ValidateCompressedRegister(rs1, nameof(rs1));
            ValidateUnsignedImmediate(immediate, 8, nameof(immediate));
            if ((immediate & 7) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "c.fld/c.fsd immediate must be 8-byte aligned");
            return (uint)quadrant | ((uint)funct3 << 13) | ((uint)((immediate >> 3) & 7) << 10) | ((uint)((immediate >> 6) & 3) << 5) | ((uint)CompressedRegisterIndex(rs1) << 7) | ((uint)CompressedFloatRegisterIndex(dataReg) << 2);
        }

        private static uint EncodeCI(int quadrant, int funct3, RVRegister rd, int immediate, bool nonZeroImmediate)
        {
            ValidateIntegerRegister(rd, nameof(rd));
            ValidateSignedImmediate(immediate, 6, nameof(immediate));
            if (rd == RVRegister.X0)
                throw new ArgumentException("Compressed CI destination must not be x0", nameof(rd));
            if (nonZeroImmediate && immediate == 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "Compressed CI immediate must be non-zero");
            return (uint)quadrant | ((uint)funct3 << 13) | ((uint)((immediate >> 5) & 1) << 12) | ((uint)RVRegisters.IntegerIndex(rd) << 7) | (((uint)immediate & 0x1FU) << 2);
        }

        private static uint EncodeCAddi16Sp(RVInstruction instruction)
        {
            if (instruction.Rd != RVRegister.X2 && instruction.Rs1 != RVRegister.X2)
                throw new ArgumentException("c.addi16sp requires sp", nameof(instruction));
            var imm = instruction.Immediate;
            ValidateSignedImmediate(imm, 10, nameof(instruction.Immediate));
            if (imm == 0 || (imm & 0xF) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.addi16sp immediate must be non-zero and 16-byte aligned");
            return 0x6001U | ((uint)((imm >> 9) & 1) << 12) | ((uint)RVRegisters.IntegerIndex(RVRegister.X2) << 7) | ((uint)((imm >> 4) & 1) << 6) | ((uint)((imm >> 6) & 1) << 5) | ((uint)((imm >> 7) & 3) << 3) | ((uint)((imm >> 5) & 1) << 2);
        }

        private static uint EncodeCShift(int kind, RVRegister rd, int shamt, RVTarget target)
        {
            ValidateCompressedRegister(rd, nameof(rd));
            var max = target.Is64Bit ? 63 : 31;
            if (shamt <= 0 || shamt > max)
                throw new ArgumentOutOfRangeException(nameof(shamt), shamt, "Compressed shift amount must be non-zero and fit XLEN");
            return 0x8001U | ((uint)((shamt >> 5) & 1) << 12) | ((uint)kind << 10) | ((uint)CompressedRegisterIndex(rd) << 7) | (((uint)shamt & 0x1FU) << 2);
        }

        private static uint EncodeCAndi(RVInstruction instruction)
        {
            ValidateCompressedRegister(instruction.Rd, nameof(instruction.Rd));
            ValidateSignedImmediate(instruction.Immediate, 6, nameof(instruction.Immediate));
            return 0x8801U | ((uint)((instruction.Immediate >> 5) & 1) << 12) | ((uint)CompressedRegisterIndex(instruction.Rd) << 7) | (((uint)instruction.Immediate & 0x1FU) << 2);
        }

        private static uint EncodeCRegisterArithmetic(int op, bool word, RVRegister rd, RVRegister rs2)
        {
            ValidateCompressedRegister(rd, nameof(rd));
            ValidateCompressedRegister(rs2, nameof(rs2));
            return 0x8C01U | (word ? 0x1000U : 0U) | ((uint)CompressedRegisterIndex(rd) << 7) | ((uint)op << 5) | ((uint)CompressedRegisterIndex(rs2) << 2);
        }

        private static uint EncodeCJ(int funct3, int immediate)
        {
            ValidateCompressedJumpImmediate(immediate);
            return 0x0001U | ((uint)funct3 << 13) | ((uint)((immediate >> 11) & 1) << 12) | ((uint)((immediate >> 4) & 1) << 11) | ((uint)((immediate >> 8) & 3) << 9) | ((uint)((immediate >> 10) & 1) << 8) | ((uint)((immediate >> 6) & 1) << 7) | ((uint)((immediate >> 7) & 1) << 6) | ((uint)((immediate >> 1) & 7) << 3) | ((uint)((immediate >> 5) & 1) << 2);
        }

        private static uint EncodeCB(int funct3, RVRegister rs1, int immediate)
        {
            ValidateCompressedRegister(rs1, nameof(rs1));
            ValidateCompressedBranchImmediate(immediate);
            return 0x0001U | ((uint)funct3 << 13) | ((uint)((immediate >> 8) & 1) << 12) | ((uint)((immediate >> 3) & 3) << 10) | ((uint)CompressedRegisterIndex(rs1) << 7) | ((uint)((immediate >> 6) & 3) << 5) | ((uint)((immediate >> 1) & 3) << 3) | ((uint)((immediate >> 5) & 1) << 2);
        }

        private static uint EncodeCSlli(RVInstruction instruction, RVTarget target)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            if (instruction.Rd == RVRegister.X0)
                throw new ArgumentException("c.slli destination must not be x0", nameof(instruction.Rd));
            var max = target.Is64Bit ? 63 : 31;
            if (instruction.Immediate <= 0 || instruction.Immediate > max)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), instruction.Immediate, "Compressed shift amount must be non-zero and fit XLEN");
            return 0x0002U | ((uint)((instruction.Immediate >> 5) & 1) << 12) | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7) | (((uint)instruction.Immediate & 0x1FU) << 2);
        }

        private static uint EncodeCLwSp(RVInstruction instruction)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            if (instruction.Rd == RVRegister.X0)
                throw new ArgumentException("c.lwsp destination must not be x0", nameof(instruction.Rd));
            ValidateStackPointer(instruction.Rs1);
            var imm = instruction.Immediate;
            ValidateUnsignedImmediate(imm, 8, nameof(instruction.Immediate));
            if ((imm & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.lwsp immediate must be 4-byte aligned");
            return 0x4002U | ((uint)((imm >> 5) & 1) << 12) | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7) | ((uint)((imm >> 2) & 7) << 4) | ((uint)((imm >> 6) & 3) << 2);
        }

        private static uint EncodeCLdSp(RVInstruction instruction)
        {
            ValidateIntegerRegister(instruction.Rd, nameof(instruction.Rd));
            if (instruction.Rd == RVRegister.X0)
                throw new ArgumentException("c.ldsp destination must not be x0", nameof(instruction.Rd));
            ValidateStackPointer(instruction.Rs1);
            var imm = instruction.Immediate;
            ValidateUnsignedImmediate(imm, 9, nameof(instruction.Immediate));
            if ((imm & 7) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.ldsp immediate must be 8-byte aligned");
            return 0x6002U | ((uint)((imm >> 5) & 1) << 12) | ((uint)RVRegisters.IntegerIndex(instruction.Rd) << 7) | ((uint)((imm >> 3) & 3) << 5) | ((uint)((imm >> 6) & 7) << 2);
        }

        private static uint EncodeCR(int bit12, RVRegister rdRs1, RVRegister rs2)
        {
            ValidateIntegerRegister(rdRs1, nameof(rdRs1));
            ValidateIntegerRegister(rs2, nameof(rs2));
            if (rdRs1 == RVRegister.X0)
                throw new ArgumentException("Compressed CR register must not be x0", nameof(rdRs1));
            return 0x8002U | ((uint)bit12 << 12) | ((uint)RVRegisters.IntegerIndex(rdRs1) << 7) | ((uint)RVRegisters.IntegerIndex(rs2) << 2);
        }

        private static uint EncodeCSwSp(RVInstruction instruction)
        {
            ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
            ValidateStackPointer(instruction.Rs1);
            var imm = instruction.Immediate;
            ValidateUnsignedImmediate(imm, 8, nameof(instruction.Immediate));
            if ((imm & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.swsp immediate must be 4-byte aligned");
            return 0xC002U | ((uint)((imm >> 2) & 0xF) << 9) | ((uint)((imm >> 6) & 0x3) << 7) | ((uint)RVRegisters.IntegerIndex(instruction.Rs2) << 2);
        }

        private static uint EncodeCSdSp(RVInstruction instruction)
        {
            ValidateIntegerRegister(instruction.Rs2, nameof(instruction.Rs2));
            ValidateStackPointer(instruction.Rs1);
            var imm = instruction.Immediate;
            ValidateUnsignedImmediate(imm, 9, nameof(instruction.Immediate));
            if ((imm & 7) != 0)
                throw new ArgumentOutOfRangeException(nameof(instruction.Immediate), imm, "c.sdsp immediate must be 8-byte aligned");
            return 0xE002U | ((uint)((imm >> 3) & 0x7) << 10) | ((uint)((imm >> 6) & 0x7) << 7) | ((uint)RVRegisters.IntegerIndex(instruction.Rs2) << 2);
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

        private static void ValidateCompressedRegister(RVRegister register, string name)
        {
            if (!RVRegisters.IsInteger(register) || register < RVRegister.X8 || register > RVRegister.X15)
                throw new ArgumentException("Expected compressed RISC-V register x8..x15", name);
        }

        private static void ValidateCompressedFloatRegister(RVRegister register, string name)
        {
            if (!RVRegisters.IsFloat(register) || register < RVRegister.F8 || register > RVRegister.F15)
                throw new ArgumentException("Expected compressed RISC-V floating-point register f8..f15", name);
        }

        private static int CompressedRegisterIndex(RVRegister register)
        {
            ValidateCompressedRegister(register, nameof(register));
            return RVRegisters.IntegerIndex(register) - 8;
        }

        private static int CompressedFloatRegisterIndex(RVRegister register)
        {
            ValidateCompressedFloatRegister(register, nameof(register));
            return RVRegisters.FloatIndex(register) - 8;
        }

        private static void ValidateStackPointer(RVRegister register)
        {
            if (register != RVRegister.X2)
                throw new ArgumentException("Expected RISC-V stack pointer register", nameof(register));
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

        internal static bool IsUnsignedVectorImmediate(RVInstrKind opcode)
            => opcode is RVInstrKind.VsllVi or RVInstrKind.VsrlVi or RVInstrKind.VsraVi
                or RVInstrKind.VnsrlWi or RVInstrKind.VnsraWi or RVInstrKind.VrgatherVi;

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

        private static void ValidateCompressedBranchImmediate(int immediate)
        {
            if ((immediate & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "Compressed branch immediate must be 2-byte aligned");
            ValidateSignedImmediate(immediate, 9, nameof(immediate));
        }

        private static void ValidateCompressedJumpImmediate(int immediate)
        {
            if ((immediate & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(immediate), immediate, "Compressed jump immediate must be 2-byte aligned");
            ValidateSignedImmediate(immediate, 12, nameof(immediate));
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

        internal static void WriteInstruction(byte[] bytes, int offset, uint value, int size, TargetEndianness endianness)
        {
            if (size == 2)
            {
                WriteUInt16(bytes, offset, (ushort)value, endianness);
                return;
            }
            WriteUInt32(bytes, offset, value, endianness);
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
            {
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                return;
            }
            bytes[offset] = (byte)(value >> 8);
            bytes[offset + 1] = (byte)value;
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
            if ((bytes.Length & 1) != 0)
                throw new InvalidDataException("RISC-V byte stream length must be 2-byte aligned");

            var builder = ImmutableArray.CreateBuilder<RVInstruction>(bytes.Length / 2);
            for (int i = 0; i < bytes.Length;)
            {
                var halfword = ReadUInt16(bytes, i, target.Endianness);
                if ((halfword & 3) != 3)
                {
                    builder.Add(Decode(halfword, target));
                    i += 2;
                    continue;
                }
                if (i + 4 > bytes.Length)
                    throw new InvalidDataException("Truncated RISC-V 32-bit instruction");
                builder.Add(Decode(ReadUInt32(bytes, i, target.Endianness), target));
                i += 4;
            }
            return builder.MoveToImmutable();
        }

        public static RVInstruction Decode(ushort halfword, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (!target.HasC)
                return RVInstruction.Raw16(halfword);

            try
            {
                return DecodeCompressed(halfword, target);
            }
            catch (InvalidDataException)
            {
                return RVInstruction.Raw16(halfword);
            }
        }

        public static RVInstruction Decode(uint word, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if ((word & 3U) != 3U)
                return Decode((ushort)word, target);

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
                        return DecodeOpImmediate32(word, funct3, funct7, rd, rs1, target);
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
                        return DecodeSystem(word, funct3, rd, rs1, rs2, target);
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

        private static RVInstruction DecodeCompressed(ushort halfword, RVTarget target)
        {
            int quadrant = halfword & 3;
            int funct3 = (halfword >> 13) & 7;
            return quadrant switch
            {
                0 => DecodeCompressedQuadrant0(halfword, funct3, target),
                1 => DecodeCompressedQuadrant1(halfword, funct3, target),
                2 => DecodeCompressedQuadrant2(halfword, funct3, target),
                _ => throw new InvalidDataException("Invalid RISC-V compressed instruction quadrant"),
            };
        }

        private static RVInstruction DecodeCompressedQuadrant0(ushort halfword, int funct3, RVTarget target)
        {
            var rd = CompressedRegister((halfword >> 2) & 7);
            var rs1 = CompressedRegister((halfword >> 7) & 7);
            var rs2 = rd;
            switch (funct3)
            {
                case 0:
                    var imm = ((halfword >> 1) & 0x3C0) | ((halfword >> 7) & 0x30) | ((halfword >> 2) & 0x8) | ((halfword >> 4) & 0x4);
                    if (imm == 0)
                        throw new InvalidDataException("Invalid c.addi4spn immediate");
                    return new RVInstruction(RVInstrKind.CAddi4Spn, rd, RVRegister.X2, RVRegister.Invalid, imm);
                case 1 when target.HasD:
                    return RVInstruction.I(RVInstrKind.CFld, CompressedFloatRegister((halfword >> 2) & 7), rs1, DecodeCLdImmediate(halfword));
                case 2:
                    return RVInstruction.I(RVInstrKind.CLw, rd, rs1, DecodeCLwImmediate(halfword));
                case 3 when target.Is32Bit && target.HasF:
                    return RVInstruction.I(RVInstrKind.CFlw, CompressedFloatRegister((halfword >> 2) & 7), rs1, DecodeCLwImmediate(halfword));
                case 3 when target.Is64Bit:
                    return RVInstruction.I(RVInstrKind.CLd, rd, rs1, DecodeCLdImmediate(halfword));
                case 5 when target.HasD:
                    return RVInstruction.S(RVInstrKind.CFsd, CompressedFloatRegister((halfword >> 2) & 7), rs1, DecodeCLdImmediate(halfword));
                case 6:
                    return RVInstruction.S(RVInstrKind.CSw, rs2, rs1, DecodeCLwImmediate(halfword));
                case 7 when target.Is32Bit && target.HasF:
                    return RVInstruction.S(RVInstrKind.CFsw, CompressedFloatRegister((halfword >> 2) & 7), rs1, DecodeCLwImmediate(halfword));
                case 7 when target.Is64Bit:
                    return RVInstruction.S(RVInstrKind.CSd, rs2, rs1, DecodeCLdImmediate(halfword));
                default:
                    throw new InvalidDataException("Invalid RISC-V compressed quadrant 0 instruction");
            }
        }

        private static RVInstruction DecodeCompressedQuadrant1(ushort halfword, int funct3, RVTarget target)
        {
            var rd = (RVRegister)((halfword >> 7) & 0x1F);
            var imm = SignExtend(((halfword >> 7) & 0x20) | ((halfword >> 2) & 0x1F), 6);
            switch (funct3)
            {
                case 0:
                    return rd == RVRegister.X0 && imm == 0 ? new RVInstruction(RVInstrKind.CNop) : new RVInstruction(RVInstrKind.CAddi, rd, rd, RVRegister.Invalid, imm);
                case 1 when target.Is32Bit:
                    return RVInstruction.J(RVInstrKind.CJal, RVRegister.X1, DecodeCJImmediate(halfword));
                case 1 when target.Is64Bit:
                    return new RVInstruction(RVInstrKind.CAddiw, rd, rd, RVRegister.Invalid, imm);
                case 2:
                    return new RVInstruction(RVInstrKind.CLi, rd, RVRegister.Invalid, RVRegister.Invalid, imm);
                case 3 when rd == RVRegister.X2:
                    return new RVInstruction(RVInstrKind.CAddi16Sp, RVRegister.X2, RVRegister.X2, RVRegister.Invalid, DecodeCAddi16SpImmediate(halfword));
                case 3:
                    return new RVInstruction(RVInstrKind.CLui, rd, RVRegister.Invalid, RVRegister.Invalid, imm);
                case 4:
                    return DecodeCompressedArithmetic(halfword, target);
                case 5:
                    return RVInstruction.J(RVInstrKind.CJ, RVRegister.X0, DecodeCJImmediate(halfword));
                case 6:
                    return RVInstruction.B(RVInstrKind.CBeqz, CompressedRegister((halfword >> 7) & 7), RVRegister.X0, DecodeCBImmediate(halfword));
                case 7:
                    return RVInstruction.B(RVInstrKind.CBnez, CompressedRegister((halfword >> 7) & 7), RVRegister.X0, DecodeCBImmediate(halfword));
                default:
                    throw new InvalidDataException("Invalid RISC-V compressed quadrant 1 instruction");
            }
        }

        private static RVInstruction DecodeCompressedQuadrant2(ushort halfword, int funct3, RVTarget target)
        {
            var rd = (RVRegister)((halfword >> 7) & 0x1F);
            var rs2 = (RVRegister)((halfword >> 2) & 0x1F);
            var shamt = (int)(((halfword >> 7) & 0x20) | ((halfword >> 2) & 0x1F));
            switch (funct3)
            {
                case 0:
                    return new RVInstruction(RVInstrKind.CSlli, rd, rd, RVRegister.Invalid, shamt);
                case 2:
                    return RVInstruction.I(RVInstrKind.CLwSp, rd, RVRegister.X2, DecodeCLwSpImmediate(halfword));
                case 3 when target.Is64Bit:
                    return RVInstruction.I(RVInstrKind.CLdSp, rd, RVRegister.X2, DecodeCLdSpImmediate(halfword));
                case 4 when ((halfword >> 12) & 1) == 0 && rs2 == RVRegister.X0:
                    return new RVInstruction(RVInstrKind.CJr, RVRegister.Invalid, rd, RVRegister.Invalid);
                case 4 when ((halfword >> 12) & 1) == 0:
                    return RVInstruction.R(RVInstrKind.CMv, rd, rd, rs2);
                case 4 when rs2 == RVRegister.X0 && rd == RVRegister.X0:
                    return new RVInstruction(RVInstrKind.CEbreak);
                case 4 when rs2 == RVRegister.X0:
                    return new RVInstruction(RVInstrKind.CJalr, RVRegister.Invalid, rd, RVRegister.Invalid);
                case 4:
                    return RVInstruction.R(RVInstrKind.CAdd, rd, rd, rs2);
                case 6:
                    return RVInstruction.S(RVInstrKind.CSwSp, rs2, RVRegister.X2, DecodeCSwSpImmediate(halfword));
                case 7 when target.Is64Bit:
                    return RVInstruction.S(RVInstrKind.CSdSp, rs2, RVRegister.X2, DecodeCSdSpImmediate(halfword));
                default:
                    throw new InvalidDataException("Invalid RISC-V compressed quadrant 2 instruction");
            }
        }

        private static RVInstruction DecodeCompressedArithmetic(ushort halfword, RVTarget target)
        {
            var rd = CompressedRegister((halfword >> 7) & 7);
            var shamt = (int)(((halfword >> 7) & 0x20) | ((halfword >> 2) & 0x1F));
            var imm = SignExtend(((halfword >> 7) & 0x20) | ((halfword >> 2) & 0x1F), 6);
            var op = (halfword >> 10) & 3;
            if (op == 0)
                return new RVInstruction(RVInstrKind.CSrli, rd, rd, RVRegister.Invalid, shamt);
            if (op == 1)
                return new RVInstruction(RVInstrKind.CSrai, rd, rd, RVRegister.Invalid, shamt);
            if (op == 2)
                return new RVInstruction(RVInstrKind.CAndi, rd, rd, RVRegister.Invalid, imm);
            var rs2 = CompressedRegister((halfword >> 2) & 7);
            var high = (halfword >> 12) & 1;
            var low = (halfword >> 5) & 3;
            var opcode = (high, low) switch
            {
                (0, 0) => RVInstrKind.CSub,
                (0, 1) => RVInstrKind.CXor,
                (0, 2) => RVInstrKind.COr,
                (0, 3) => RVInstrKind.CAnd,
                (1, 0) when target.Is64Bit => RVInstrKind.CSubw,
                (1, 1) when target.Is64Bit => RVInstrKind.CAddw,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid compressed arithmetic instruction");
            return RVInstruction.R(opcode, rd, rd, rs2);
        }

        private static RVRegister CompressedRegister(int value)
            => (RVRegister)(value + 8);

        private static RVRegister CompressedFloatRegister(int value)
            => (RVRegister)((int)RVRegister.F8 + value);

        private static int DecodeCLwImmediate(ushort halfword)
            => (int)(((halfword >> 5) & 0x1) << 6 | ((halfword >> 10) & 0x7) << 3 | ((halfword >> 6) & 0x1) << 2);

        private static int DecodeCLdImmediate(ushort halfword)
            => (int)(((halfword >> 10) & 0x7) << 3 | ((halfword >> 5) & 0x3) << 6);

        private static int DecodeCLwSpImmediate(ushort halfword)
            => (int)(((halfword >> 12) & 1) << 5 | ((halfword >> 4) & 7) << 2 | ((halfword >> 2) & 3) << 6);

        private static int DecodeCLdSpImmediate(ushort halfword)
            => (int)(((halfword >> 12) & 1) << 5 | ((halfword >> 5) & 3) << 3 | ((halfword >> 2) & 7) << 6);

        private static int DecodeCSwSpImmediate(ushort halfword)
            => (int)(((halfword >> 9) & 0xF) << 2 | ((halfword >> 7) & 3) << 6);

        private static int DecodeCSdSpImmediate(ushort halfword)
            => (int)(((halfword >> 10) & 7) << 3 | ((halfword >> 7) & 7) << 6);

        private static int DecodeCAddi16SpImmediate(ushort halfword)
        {
            var imm = ((halfword >> 12) & 1) << 9 | ((halfword >> 6) & 1) << 4 | ((halfword >> 5) & 1) << 6 | ((halfword >> 3) & 3) << 7 | ((halfword >> 2) & 1) << 5;
            return SignExtend((int)imm, 10);
        }

        private static int DecodeCJImmediate(ushort halfword)
        {
            var imm = ((halfword >> 12) & 1) << 11 | ((halfword >> 11) & 1) << 4 | ((halfword >> 9) & 3) << 8 | ((halfword >> 8) & 1) << 10 | ((halfword >> 7) & 1) << 6 | ((halfword >> 6) & 1) << 7 | ((halfword >> 3) & 7) << 1 | ((halfword >> 2) & 1) << 5;
            return SignExtend((int)imm, 12);
        }

        private static int DecodeCBImmediate(ushort halfword)
        {
            var imm = ((halfword >> 12) & 1) << 8 | ((halfword >> 10) & 3) << 3 | ((halfword >> 5) & 3) << 6 | ((halfword >> 3) & 3) << 1 | ((halfword >> 2) & 1) << 5;
            return SignExtend((int)imm, 9);
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
            if (target.HasB && TryDecodeBitmanipImmediate(word, funct3, funct7, rd, rs1, target, out var bitmanip))
                return bitmanip;
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
                (0x20, 7) when target.HasB => RVInstrKind.Andn,
                (0x20, 6) when target.HasB => RVInstrKind.Orn,
                (0x20, 4) when target.HasB => RVInstrKind.Xnor,
                (0x05, 6) when target.HasB => RVInstrKind.Max,
                (0x05, 7) when target.HasB => RVInstrKind.Maxu,
                (0x05, 4) when target.HasB => RVInstrKind.Min,
                (0x05, 5) when target.HasB => RVInstrKind.Minu,
                (0x30, 1) when target.HasB => RVInstrKind.Rol,
                (0x30, 5) when target.HasB => RVInstrKind.Ror,
                (0x10, 2) when target.HasB => RVInstrKind.Sh1Add,
                (0x10, 4) when target.HasB => RVInstrKind.Sh2Add,
                (0x10, 6) when target.HasB => RVInstrKind.Sh3Add,
                (0x24, 1) when target.HasB => RVInstrKind.Bclr,
                (0x24, 5) when target.HasB => RVInstrKind.Bext,
                (0x34, 1) when target.HasB => RVInstrKind.Binv,
                (0x14, 1) when target.HasB => RVInstrKind.Bset,
                (0x05, 1) when target.HasB => RVInstrKind.Clmul,
                (0x05, 2) when target.HasB => RVInstrKind.Clmulr,
                (0x05, 3) when target.HasB => RVInstrKind.Clmulh,
                (0x04, 4) when target.HasB && rs2 == RVRegister.X0 => RVInstrKind.ZextH,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V register arithmetic instruction");
            return RVInstruction.R(opcode, rd, rs1, rs2);
        }

        private static bool TryDecodeBitmanipImmediate(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1, RVTarget target, out RVInstruction instruction)
        {
            var imm12 = (int)((word >> 20) & 0xFFFU);
            if (funct3 == 1)
            {
                switch (imm12)
                {
                    case 0x600: instruction = RVInstruction.R(RVInstrKind.Clz, rd, rs1, RVRegister.X0); return true;
                    case 0x601: instruction = RVInstruction.R(RVInstrKind.Ctz, rd, rs1, RVRegister.X0); return true;
                    case 0x602: instruction = RVInstruction.R(RVInstrKind.Cpop, rd, rs1, RVRegister.X0); return true;
                    case 0x604: instruction = RVInstruction.R(RVInstrKind.SextB, rd, rs1, RVRegister.X0); return true;
                    case 0x605: instruction = RVInstruction.R(RVInstrKind.SextH, rd, rs1, RVRegister.X0); return true;
                }
                if (TryDecodeSingleBitImmediate(word, target, 0x24, RVInstrKind.Bclri, rd, rs1, out instruction)) return true;
                if (TryDecodeSingleBitImmediate(word, target, 0x34, RVInstrKind.Binvi, rd, rs1, out instruction)) return true;
                if (TryDecodeSingleBitImmediate(word, target, 0x14, RVInstrKind.Bseti, rd, rs1, out instruction)) return true;
            }
            if (funct3 == 5)
            {
                if (imm12 == 0x287) { instruction = RVInstruction.R(RVInstrKind.OrcB, rd, rs1, RVRegister.X0); return true; }
                if (imm12 == (target.Is64Bit ? 0x6B8 : 0x698)) { instruction = RVInstruction.R(RVInstrKind.Rev8, rd, rs1, RVRegister.X0); return true; }
                if (funct7 == 0x30 || (target.Is64Bit && ((word >> 26) & 0x3FU) == 0x18U)) { instruction = RVInstruction.I(RVInstrKind.Rori, rd, rs1, (int)((word >> 20) & (target.Is64Bit ? 0x3FU : 0x1FU))); return true; }
                if (TryDecodeSingleBitImmediate(word, target, 0x24, RVInstrKind.Bexti, rd, rs1, out instruction)) return true;
            }
            instruction = default;
            return false;
        }

        private static bool TryDecodeSingleBitImmediate(uint word, RVTarget target, int funct7, RVInstrKind opcode, RVRegister rd, RVRegister rs1, out RVInstruction instruction)
        {
            if (!target.Is64Bit)
            {
                if (((word >> 25) & 0x7FU) == (uint)funct7)
                {
                    instruction = RVInstruction.I(opcode, rd, rs1, (int)((word >> 20) & 0x1FU));
                    return true;
                }
            }
            else if (((word >> 26) & 0x3FU) == (uint)(funct7 >> 1))
            {
                instruction = RVInstruction.I(opcode, rd, rs1, (int)((word >> 20) & 0x3FU));
                return true;
            }
            instruction = default;
            return false;
        }

        private static RVInstruction DecodeOpImmediate32(uint word, byte funct3, byte funct7, RVRegister rd, RVRegister rs1, RVTarget target)
        {
            if (target.HasB)
            {
                if (funct3 == 1 && ((word >> 20) & 0xFFFU) == 0x600U)
                    return RVInstruction.R(RVInstrKind.Clzw, rd, rs1, RVRegister.X0);
                if (funct3 == 1 && ((word >> 20) & 0xFFFU) == 0x601U)
                    return RVInstruction.R(RVInstrKind.Ctzw, rd, rs1, RVRegister.X0);
                if (funct3 == 1 && ((word >> 20) & 0xFFFU) == 0x602U)
                    return RVInstruction.R(RVInstrKind.Cpopw, rd, rs1, RVRegister.X0);
                if (funct3 == 5 && funct7 == 0x30)
                    return RVInstruction.I(RVInstrKind.Roriw, rd, rs1, (int)((word >> 20) & 0x1FU));
                if (funct3 == 1 && ((word >> 26) & 0x3FU) == 0x02U)
                    return RVInstruction.I(RVInstrKind.SlliUw, rd, rs1, (int)((word >> 20) & 0x3FU));
            }
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
                (0x04, 0) when target.HasB => RVInstrKind.AddUw,
                (0x10, 2) when target.HasB => RVInstrKind.Sh1AddUw,
                (0x10, 4) when target.HasB => RVInstrKind.Sh2AddUw,
                (0x10, 6) when target.HasB => RVInstrKind.Sh3AddUw,
                (0x30, 1) when target.HasB => RVInstrKind.Rolw,
                (0x30, 5) when target.HasB => RVInstrKind.Rorw,
                (0x04, 4) when target.HasB && rs2 == RVRegister.X0 => RVInstrKind.ZextH,
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

        private static RVInstruction DecodeSystem(uint word, byte funct3, RVRegister rd, RVRegister rs1, RVRegister rs2, RVTarget target)
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
                    0x11 when target.HasH => RVInstrKind.HfenceVvma,
                    0x31 when target.HasH => RVInstrKind.HfenceGvma,
                    _ => RVInstrKind.Invalid,
                };
                if (fenceOpcode != RVInstrKind.Invalid)
                    return new RVInstruction(fenceOpcode, rs1: rs1, rs2: rs2);
            }

            if (funct3 == 4 && target.HasH)
                return DecodeHypervisorMemory(word, rd, rs1, rs2, target);

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

        private static RVInstruction DecodeHypervisorMemory(uint word, RVRegister rd, RVRegister rs1, RVRegister rs2, RVTarget target)
        {
            byte funct7 = (byte)((word >> 25) & 0x7F);
            if (rd == RVRegister.X0)
            {
                var storeOpcode = funct7 switch
                {
                    0x31 => RVInstrKind.HsvB,
                    0x33 => RVInstrKind.HsvH,
                    0x35 => RVInstrKind.HsvW,
                    0x37 when target.Is64Bit => RVInstrKind.HsvD,
                    _ => RVInstrKind.Invalid,
                };
                if (storeOpcode != RVInstrKind.Invalid)
                    return new RVInstruction(storeOpcode, rs1: rs1, rs2: rs2);
            }

            int selector = RVRegisters.IntegerIndex(rs2);
            var loadOpcode = (funct7, selector) switch
            {
                (0x30, 0) => RVInstrKind.HlvB,
                (0x30, 1) => RVInstrKind.HlvBu,
                (0x32, 0) => RVInstrKind.HlvH,
                (0x32, 1) => RVInstrKind.HlvHu,
                (0x32, 3) => RVInstrKind.HlvxHu,
                (0x34, 0) => RVInstrKind.HlvW,
                (0x34, 1) when target.Is64Bit => RVInstrKind.HlvWu,
                (0x34, 3) => RVInstrKind.HlvxWu,
                (0x36, 0) when target.Is64Bit => RVInstrKind.HlvD,
                _ => RVInstrKind.Invalid,
            };
            if (loadOpcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V hypervisor memory instruction");
            return new RVInstruction(loadOpcode, rd, rs1);
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
                (4, 0) => RVInstrKind.VminuVv,
                (4, 4) => RVInstrKind.VminuVx,
                (5, 0) => RVInstrKind.VminVv,
                (5, 4) => RVInstrKind.VminVx,
                (6, 0) => RVInstrKind.VmaxuVv,
                (6, 4) => RVInstrKind.VmaxuVx,
                (7, 0) => RVInstrKind.VmaxVv,
                (7, 4) => RVInstrKind.VmaxVx,
                (9, 0) => RVInstrKind.VandVv,
                (9, 4) => RVInstrKind.VandVx,
                (9, 3) => RVInstrKind.VandVi,
                (10, 0) => RVInstrKind.VorVv,
                (10, 4) => RVInstrKind.VorVx,
                (10, 3) => RVInstrKind.VorVi,
                (11, 0) => RVInstrKind.VxorVv,
                (11, 4) => RVInstrKind.VxorVx,
                (11, 3) => RVInstrKind.VxorVi,
                (24, 0) => RVInstrKind.VmseqVv,
                (24, 4) => RVInstrKind.VmseqVx,
                (24, 3) => RVInstrKind.VmseqVi,
                (25, 0) => RVInstrKind.VmsneVv,
                (25, 4) => RVInstrKind.VmsneVx,
                (25, 3) => RVInstrKind.VmsneVi,
                (26, 0) => RVInstrKind.VmsltuVv,
                (26, 4) => RVInstrKind.VmsltuVx,
                (27, 0) => RVInstrKind.VmsltVv,
                (27, 4) => RVInstrKind.VmsltVx,
                (28, 0) => RVInstrKind.VmsleuVv,
                (28, 4) => RVInstrKind.VmsleuVx,
                (28, 3) => RVInstrKind.VmsleuVi,
                (29, 0) => RVInstrKind.VmsleVv,
                (29, 4) => RVInstrKind.VmsleVx,
                (29, 3) => RVInstrKind.VmsleVi,
                (30, 4) => RVInstrKind.VmsgtuVx,
                (30, 3) => RVInstrKind.VmsgtuVi,
                (31, 4) => RVInstrKind.VmsgtVx,
                (31, 3) => RVInstrKind.VmsgtVi,
                (32, 2) => RVInstrKind.VdivuVv,
                (32, 6) => RVInstrKind.VdivuVx,
                (33, 2) => RVInstrKind.VdivVv,
                (33, 6) => RVInstrKind.VdivVx,
                (34, 2) => RVInstrKind.VremuVv,
                (34, 6) => RVInstrKind.VremuVx,
                (35, 2) => RVInstrKind.VremVv,
                (35, 6) => RVInstrKind.VremVx,
                (36, 2) => RVInstrKind.VmulhuVv,
                (36, 6) => RVInstrKind.VmulhuVx,
                (37, 0) => RVInstrKind.VsllVv,
                (37, 4) => RVInstrKind.VsllVx,
                (37, 3) => RVInstrKind.VsllVi,
                (37, 2) => RVInstrKind.VmulVv,
                (37, 6) => RVInstrKind.VmulVx,
                (38, 2) => RVInstrKind.VmulhsuVv,
                (38, 6) => RVInstrKind.VmulhsuVx,
                (39, 2) => RVInstrKind.VmulhVv,
                (39, 6) => RVInstrKind.VmulhVx,
                (40, 0) => RVInstrKind.VsrlVv,
                (40, 4) => RVInstrKind.VsrlVx,
                (40, 3) => RVInstrKind.VsrlVi,
                (41, 0) => RVInstrKind.VsraVv,
                (41, 4) => RVInstrKind.VsraVx,
                (41, 3) => RVInstrKind.VsraVi,
                (41, 2) => RVInstrKind.VmaddVv,
                (41, 6) => RVInstrKind.VmaddVx,
                (43, 2) => RVInstrKind.VnmsubVv,
                (43, 6) => RVInstrKind.VnmsubVx,
                (44, 0) => RVInstrKind.VnsrlWv,
                (44, 4) => RVInstrKind.VnsrlWx,
                (44, 3) => RVInstrKind.VnsrlWi,
                (45, 0) => RVInstrKind.VnsraWv,
                (45, 4) => RVInstrKind.VnsraWx,
                (45, 3) => RVInstrKind.VnsraWi,
                (45, 2) => RVInstrKind.VmaccVv,
                (45, 6) => RVInstrKind.VmaccVx,
                (47, 2) => RVInstrKind.VnmsacVv,
                (47, 6) => RVInstrKind.VnmsacVx,
                (48, 0) => RVInstrKind.VrgatherVv,
                (48, 4) => RVInstrKind.VrgatherVx,
                (48, 3) => RVInstrKind.VrgatherVi,
                (0, 1) => RVInstrKind.VfaddVv,
                (0, 5) => RVInstrKind.VfaddVf,
                (2, 1) => RVInstrKind.VfsubVv,
                (2, 5) => RVInstrKind.VfsubVf,
                (4, 1) => RVInstrKind.VfminVv,
                (4, 5) => RVInstrKind.VfminVf,
                (6, 1) => RVInstrKind.VfmaxVv,
                (6, 5) => RVInstrKind.VfmaxVf,
                (8, 1) => RVInstrKind.VfsgnjVv,
                (8, 5) => RVInstrKind.VfsgnjVf,
                (9, 1) => RVInstrKind.VfsgnjnVv,
                (9, 5) => RVInstrKind.VfsgnjnVf,
                (10, 1) => RVInstrKind.VfsgnjxVv,
                (10, 5) => RVInstrKind.VfsgnjxVf,
                (24, 1) => RVInstrKind.VmfeqVv,
                (24, 5) => RVInstrKind.VmfeqVf,
                (25, 1) => RVInstrKind.VmfleVv,
                (25, 5) => RVInstrKind.VmfleVf,
                (27, 1) => RVInstrKind.VmfltVv,
                (27, 5) => RVInstrKind.VmfltVf,
                (28, 1) => RVInstrKind.VmfneVv,
                (28, 5) => RVInstrKind.VmfneVf,
                (29, 5) => RVInstrKind.VmfgtVf,
                (31, 5) => RVInstrKind.VmfgeVf,
                (32, 1) => RVInstrKind.VfdivVv,
                (32, 5) => RVInstrKind.VfdivVf,
                (33, 5) => RVInstrKind.VfrdivVf,
                (36, 1) => RVInstrKind.VfmulVv,
                (36, 5) => RVInstrKind.VfmulVf,
                (39, 5) => RVInstrKind.VfrsubVf,
                _ => RVInstrKind.Invalid,
            };
            if (opcode == RVInstrKind.Invalid)
                throw new InvalidDataException("Invalid RISC-V vector instruction");
            if (funct3 is 0 or 1 or 2)
                return RVInstruction.Vv(opcode, rd, vs2, vs1, vm);
            if (funct3 is 4 or 6)
                return RVInstruction.Vx(opcode, rd, vs2, rs1, vm);
            if (funct3 == 5)
                return RVInstruction.Vx(opcode, rd, vs2, (RVRegister)(32 + (int)((word >> 15) & 0x1F)), vm);
            int immediate = (int)((word >> 15) & 0x1F);
            if (!RiscVCodeEncoder.IsUnsignedVectorImmediate(opcode))
                immediate = SignExtend(immediate, 5);
            return RVInstruction.Vi(opcode, rd, vs2, immediate, vm);
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

        private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
                return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, TargetEndianness endianness)
        {
            if (endianness == TargetEndianness.Little)
                return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            return (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
        }
    }
}
