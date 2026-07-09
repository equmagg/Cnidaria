using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Cnidaria.RiscV
{
    internal sealed class RiscVAssemblyWriterOptions
    {
        public static RiscVAssemblyWriterOptions Default { get; } = new RiscVAssemblyWriterOptions();

        public bool UseAbiRegisterNames { get; set; } = true;
        public bool IncludeLabels { get; set; } = true;
        public bool UsePseudoInstructions { get; set; } = false;
        public bool FormatVectorTypeNames { get; set; } = true;
    }

    internal static class RiscVAssembler
    {
        public static RiscVProgram Assemble(string text, RVTarget target)
            => RiscVAssemblyParser.Parse(text, target);

        public static RiscVProgram Parse(string text, RVTarget target)
            => Assemble(text, target);
    }

    internal static class RiscVDisassembler
    {
        public static string Disassemble(RiscVProgram obj, RiscVAssemblyWriterOptions? options = null)
            => RiscVAssemblyWriter.Write(obj, options);

        public static string Disassemble(RVTextSection text, RiscVAssemblyWriterOptions? options = null)
            => RiscVAssemblyWriter.Write(text, options);

        public static string Disassemble(IEnumerable<RVInstruction> instructions, RiscVAssemblyWriterOptions? options = null)
            => RiscVAssemblyWriter.Write(instructions, options);
    }

    internal static class RiscVAssemblyParser
    {
        public static RiscVProgram Parse(string text, RVTarget target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            var instructions = ImmutableArray.CreateBuilder<RVInstruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = StripComment(lines[lineIndex]).Trim();
                if (line.Length == 0)
                    continue;

                while (true)
                {
                    int colon = FindLabelColon(line);
                    if (colon < 0)
                        break;
                    string label = line.Substring(0, colon).Trim();
                    ValidateLabel(label, lineIndex + 1);
                    if (labels.ContainsKey(label))
                        throw new FormatException($"Duplicate RISC-V label '{label}' on line {lineIndex + 1}");
                    labels.Add(label, instructions.Count * 4);
                    line = line.Substring(colon + 1).Trim();
                    if (line.Length == 0)
                        break;
                }

                if (line.Length == 0)
                    continue;

                foreach (var instruction in ParseInstruction(line, lineIndex + 1))
                    instructions.Add(instruction);
            }

            return new RiscVProgram(target, instructions.MoveToImmutable(), labels);
        }

        private static IEnumerable<RVInstruction> ParseInstruction(string line, int lineNumber)
        {
            int split = FirstWhitespace(line);
            string mnemonic = split < 0 ? line.Trim().ToLowerInvariant() : line.Substring(0, split).Trim().ToLowerInvariant();
            string operandText = split < 0 ? string.Empty : line.Substring(split + 1).Trim();
            var operands = SplitOperands(operandText);

            if (mnemonic == ".word")
            {
                ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                return One(RVInstruction.Raw(unchecked((uint)ParseImmediate(operands[0]))));
            }

            if (TryParsePseudo(mnemonic, operands, lineNumber, out var pseudo))
                return pseudo;

            var atomicFlags = RVInstructionFlags.None;
            string canonicalMnemonic = StripAtomicSuffix(mnemonic, out atomicFlags);
            var opcode = RVInstructionTable.GetOpcode(canonicalMnemonic);
            var metadata = RVInstructionTable.Get(opcode);

            switch (metadata.Format)
            {
                case RVInstructionFormat.R:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(RVInstruction.R(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), ParseGpr(operands[2])));
                case RVInstructionFormat.I:
                    return One(ParseI(opcode, operands, lineNumber));
                case RVInstructionFormat.ShiftI:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(RVInstruction.I(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), ParseImmediate(operands[2])));
                case RVInstructionFormat.S:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(ParseStore(opcode, operands));
                case RVInstructionFormat.FloatLoad:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(ParseFloatLoad(opcode, operands));
                case RVInstructionFormat.FloatStore:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(ParseFloatStore(opcode, operands));
                case RVInstructionFormat.FloatRRR:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(RVInstruction.R(opcode, ParseFpr(operands[0]), ParseFpr(operands[1]), ParseFpr(operands[2])));
                case RVInstructionFormat.FloatCompare:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(RVInstruction.R(opcode, ParseGpr(operands[0]), ParseFpr(operands[1]), ParseFpr(operands[2])));
                case RVInstructionFormat.FloatConvertFromInteger:
                case RVInstructionFormat.FloatMoveFromInteger:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(RVInstruction.R(opcode, ParseFpr(operands[0]), ParseGpr(operands[1]), RVRegister.X0));
                case RVInstructionFormat.FloatConvertToInteger:
                case RVInstructionFormat.FloatMoveToInteger:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(RVInstruction.R(opcode, ParseGpr(operands[0]), ParseFpr(operands[1]), RVRegister.X0));
                case RVInstructionFormat.FloatConvert:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(RVInstruction.R(opcode, ParseFpr(operands[0]), ParseFpr(operands[1]), RVRegister.X0));
                case RVInstructionFormat.B:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(ParseBranch(opcode, operands));
                case RVInstructionFormat.U:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(RVInstruction.U(opcode, ParseGpr(operands[0]), ParseImmediate(operands[1])));
                case RVInstructionFormat.J:
                    return One(ParseJal(operands, lineNumber));
                case RVInstructionFormat.Fence:
                    return One(new RVInstruction(RVInstrKind.Fence, immediate: ParseFenceImmediate(operands)));
                case RVInstructionFormat.System:
                    ValidateOperandCount(mnemonic, operands, 0, lineNumber);
                    return One(new RVInstruction(opcode));
                case RVInstructionFormat.PrivilegedFence:
                    return One(ParsePrivilegedFence(opcode, operands, lineNumber));
                case RVInstructionFormat.Csr:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(new RVInstruction(opcode, ParseGpr(operands[0]), ParseGpr(operands[2]), RVRegister.Invalid, ParseCsr(operands[1])));
                case RVInstructionFormat.CsrImmediate:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(new RVInstruction(opcode, ParseGpr(operands[0]), (RVRegister)ParseUimm(operands[2], 5), RVRegister.Invalid, ParseCsr(operands[1])));
                case RVInstructionFormat.Amo:
                    return One(ParseAtomic(opcode, operands, atomicFlags, lineNumber));
                case RVInstructionFormat.VectorConfig:
                    return One(ParseVectorConfig(opcode, operands, lineNumber));
                case RVInstructionFormat.VectorLoad:
                    return One(ParseVectorLoad(opcode, operands, lineNumber));
                case RVInstructionFormat.VectorStore:
                    return One(ParseVectorStore(opcode, operands, lineNumber));
                case RVInstructionFormat.VectorOp:
                    return One(ParseVectorOp(opcode, metadata, operands, lineNumber));
                default:
                    throw new FormatException($"Unsupported RISC-V instruction format on line {lineNumber}");
            }
        }

        private static IEnumerable<RVInstruction> One(RVInstruction instruction)
        {
            yield return instruction;
        }

        private static bool TryParsePseudo(string mnemonic, ImmutableArray<string> operands, int lineNumber, out IEnumerable<RVInstruction> instructions)
        {
            switch (mnemonic)
            {
                case "nop":
                    ValidateOperandCount(mnemonic, operands, 0, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Addi, RVRegister.X0, RVRegister.X0, 0));
                    return true;
                case "mv":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Addi, ParseGpr(operands[0]), ParseGpr(operands[1]), 0));
                    return true;
                case "not":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Xori, ParseGpr(operands[0]), ParseGpr(operands[1]), -1));
                    return true;
                case "neg":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(RVInstruction.R(RVInstrKind.Sub, ParseGpr(operands[0]), RVRegister.X0, ParseGpr(operands[1])));
                    return true;
                case "negw":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(RVInstruction.R(RVInstrKind.Subw, ParseGpr(operands[0]), RVRegister.X0, ParseGpr(operands[1])));
                    return true;
                case "sext.w":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Addiw, ParseGpr(operands[0]), ParseGpr(operands[1]), 0));
                    return true;
                case "ret":
                    ValidateOperandCount(mnemonic, operands, 0, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));
                    return true;
                case "jr":
                    ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                    instructions = One(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, ParseGpr(operands[0]), 0));
                    return true;
                case "j":
                    ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                    instructions = One(TryParseImmediate(operands[0], out int jimm)
                        ? RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, jimm)
                        : RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, operands[0]));
                    return true;
                case "li":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = ParseLoadImmediate(ParseGpr(operands[0]), ParseImmediate(operands[1]));
                    return true;
                case "beqz":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Beq, ImmutableArray.Create(operands[0], "zero", operands[1])));
                    return true;
                case "bnez":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Bne, ImmutableArray.Create(operands[0], "zero", operands[1])));
                    return true;
                case "blez":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Bge, ImmutableArray.Create("zero", operands[0], operands[1])));
                    return true;
                case "bgez":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Bge, ImmutableArray.Create(operands[0], "zero", operands[1])));
                    return true;
                case "bltz":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Blt, ImmutableArray.Create(operands[0], "zero", operands[1])));
                    return true;
                case "bgtz":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = One(ParseBranch(RVInstrKind.Blt, ImmutableArray.Create("zero", operands[0], operands[1])));
                    return true;
            }

            instructions = Array.Empty<RVInstruction>();
            return false;
        }

        private static IEnumerable<RVInstruction> ParseLoadImmediate(RVRegister rd, int immediate)
        {
            if (immediate >= -2048 && immediate <= 2047)
                return One(RVInstruction.I(RVInstrKind.Addi, rd, RVRegister.X0, immediate));

            int upper = (int)(((long)immediate + 0x800L) >> 12);
            int lower = (int)((long)immediate - ((long)upper << 12));
            return new[]
            {
                RVInstruction.U(RVInstrKind.Lui, rd, upper),
                RVInstruction.I(RVInstrKind.Addi, rd, rd, lower),
            };
        }

        private static RVInstruction ParseI(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            if (RVInstructionTable.IsLoad(opcode))
            {
                ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                var memory = ParseMemoryOperand(operands[1]);
                return RVInstruction.I(opcode, ParseGpr(operands[0]), memory.Base, memory.Offset);
            }
            if (opcode == RVInstrKind.Jalr)
            {
                if (operands.Length == 1)
                    return RVInstruction.I(opcode, RVRegister.X1, ParseGpr(operands[0]), 0);
                if (operands.Length == 2)
                {
                    var memory = ParseMemoryOperand(operands[1]);
                    return RVInstruction.I(opcode, ParseGpr(operands[0]), memory.Base, memory.Offset);
                }
                if (operands.Length == 3)
                    return RVInstruction.I(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), ParseImmediate(operands[2]));
                throw new FormatException($"Invalid jalr operand count on line {lineNumber}");
            }

            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 3, lineNumber);
            return RVInstruction.I(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), ParseImmediate(operands[2]));
        }

        private static RVInstruction ParseStore(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var memory = ParseMemoryOperand(operands[1]);
            return RVInstruction.S(opcode, ParseGpr(operands[0]), memory.Base, memory.Offset);
        }

        private static RVInstruction ParseFloatLoad(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var memory = ParseMemoryOperand(operands[1]);
            return RVInstruction.I(opcode, ParseFpr(operands[0]), memory.Base, memory.Offset);
        }

        private static RVInstruction ParseFloatStore(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var memory = ParseMemoryOperand(operands[1]);
            return RVInstruction.S(opcode, ParseFpr(operands[0]), memory.Base, memory.Offset);
        }

        private static RVInstruction ParseBranch(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var rs1 = ParseGpr(operands[0]);
            var rs2 = ParseGpr(operands[1]);
            if (TryParseImmediate(operands[2], out int immediate))
                return RVInstruction.B(opcode, rs1, rs2, immediate);
            return RVInstruction.B(opcode, rs1, rs2, operands[2]);
        }

        private static RVInstruction ParseJal(ImmutableArray<string> operands, int lineNumber)
        {
            if (operands.Length == 1)
            {
                if (TryParseImmediate(operands[0], out int immediate))
                    return RVInstruction.J(RVInstrKind.Jal, RVRegister.X1, immediate);
                return RVInstruction.J(RVInstrKind.Jal, RVRegister.X1, operands[0]);
            }
            if (operands.Length == 2)
            {
                var rd = ParseGpr(operands[0]);
                if (TryParseImmediate(operands[1], out int immediate))
                    return RVInstruction.J(RVInstrKind.Jal, rd, immediate);
                return RVInstruction.J(RVInstrKind.Jal, rd, operands[1]);
            }
            throw new FormatException($"Invalid jal operand count on line {lineNumber}");
        }

        private static RVInstruction ParsePrivilegedFence(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            if (operands.Length > 2)
                throw new FormatException($"Invalid privileged fence operand count on line {lineNumber}");
            var rs1 = operands.Length >= 1 ? ParseGpr(operands[0]) : RVRegister.X0;
            var rs2 = operands.Length >= 2 ? ParseGpr(operands[1]) : RVRegister.X0;
            return new RVInstruction(opcode, rs1: rs1, rs2: rs2);
        }

        private static RVInstruction ParseAtomic(RVInstrKind opcode, ImmutableArray<string> operands, RVInstructionFlags flags, int lineNumber)
        {
            bool acquire = (flags & RVInstructionFlags.AtomicAcquire) != 0;
            bool release = (flags & RVInstructionFlags.AtomicRelease) != 0;
            if (opcode is RVInstrKind.LrW or RVInstrKind.LrD)
            {
                ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                var memory = ParseMemoryOperand(operands[1]);
                return RVInstruction.Amo(opcode, ParseGpr(operands[0]), memory.Base, RVRegister.X0, acquire, release);
            }
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 3, lineNumber);
            var mem = ParseMemoryOperand(operands[2]);
            return RVInstruction.Amo(opcode, ParseGpr(operands[0]), mem.Base, ParseGpr(operands[1]), acquire, release);
        }

        private static RVInstruction ParseVectorConfig(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            switch (opcode)
            {
                case RVInstrKind.Vsetvli:
                    if (operands.Length < 3)
                        throw new FormatException($"vsetvli expects at least 3 operands on line {lineNumber}");
                    return RVInstruction.Vsetvli(ParseGpr(operands[0]), ParseGpr(operands[1]), ParseVType(operands, 2));
                case RVInstrKind.Vsetivli:
                    if (operands.Length < 3)
                        throw new FormatException($"vsetivli expects at least 3 operands on line {lineNumber}");
                    return RVInstruction.Vsetivli(ParseGpr(operands[0]), ParseUimm(operands[1], 5), ParseVType(operands, 2));
                case RVInstrKind.Vsetvl:
                    ValidateOperandCount("vsetvl", operands, 3, lineNumber);
                    return RVInstruction.Vsetvl(ParseGpr(operands[0]), ParseGpr(operands[1]), ParseGpr(operands[2]));
                default:
                    throw new FormatException($"Invalid vector configuration instruction on line {lineNumber}");
            }
        }

        private static RVInstruction ParseVectorLoad(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            bool unmasked = ParseOptionalVectorMask(ref operands);
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
            var memory = ParseMemoryOperand(operands[1]);
            return RVInstruction.Vl(opcode, ParseVreg(operands[0]), memory.Base, unmasked);
        }

        private static RVInstruction ParseVectorStore(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            bool unmasked = ParseOptionalVectorMask(ref operands);
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
            var memory = ParseMemoryOperand(operands[1]);
            return RVInstruction.Vs(opcode, ParseVreg(operands[0]), memory.Base, unmasked);
        }

        private static RVInstruction ParseVectorOp(RVInstrKind opcode, RVInstructionMetadata metadata, ImmutableArray<string> operands, int lineNumber)
        {
            bool unmasked = ParseOptionalVectorMask(ref operands);
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 3, lineNumber);
            if (metadata.Funct3 == 0)
                return RVInstruction.Vv(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseVreg(operands[2]), unmasked);
            if (metadata.Funct3 == 4)
                return RVInstruction.Vx(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseGpr(operands[2]), unmasked);
            if (metadata.Funct3 == 3)
                return RVInstruction.Vi(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseImmediate(operands[2]), unmasked);
            throw new FormatException($"Unsupported vector operand form on line {lineNumber}");
        }

        private static bool ParseOptionalVectorMask(ref ImmutableArray<string> operands)
        {
            if (operands.Length == 0)
                return true;
            string last = operands[operands.Length - 1].Trim().ToLowerInvariant();
            if (last == "v0.t" || last == "v0")
            {
                operands = operands.RemoveAt(operands.Length - 1);
                return false;
            }
            return true;
        }

        private static string StripAtomicSuffix(string mnemonic, out RVInstructionFlags flags)
        {
            flags = RVInstructionFlags.None;
            if (mnemonic.EndsWith(".aqrl", StringComparison.OrdinalIgnoreCase))
            {
                flags = RVInstructionFlags.AtomicAcquire | RVInstructionFlags.AtomicRelease;
                return mnemonic.Substring(0, mnemonic.Length - 5);
            }
            if (mnemonic.EndsWith(".aq", StringComparison.OrdinalIgnoreCase))
            {
                flags = RVInstructionFlags.AtomicAcquire;
                return mnemonic.Substring(0, mnemonic.Length - 3);
            }
            if (mnemonic.EndsWith(".rl", StringComparison.OrdinalIgnoreCase))
            {
                flags = RVInstructionFlags.AtomicRelease;
                return mnemonic.Substring(0, mnemonic.Length - 3);
            }
            return mnemonic;
        }

        private static RVRegister ParseGpr(string text)
        {
            var register = RVRegisters.Parse(text);
            if (!RVRegisters.IsInteger(register))
                throw new FormatException($"Expected RISC-V integer register: {text}");
            return register;
        }

        private static RVRegister ParseFpr(string text)
        {
            var register = RVRegisters.Parse(text);
            if (!RVRegisters.IsFloat(register))
                throw new FormatException($"Expected RISC-V floating-point register: {text}");
            return register;
        }

        private static RVRegister ParseVreg(string text)
        {
            var register = RVRegisters.Parse(text);
            if (!RVRegisters.IsVector(register))
                throw new FormatException($"Expected RISC-V vector register: {text}");
            return register;
        }

        private static (int Offset, RVRegister Base) ParseMemoryOperand(string text)
        {
            int open = text.IndexOf('(');
            int close = text.LastIndexOf(')');
            if (open < 0 || close != text.Length - 1 || close <= open + 1)
                throw new FormatException($"Invalid RISC-V memory operand: {text}");
            string offsetText = text.Substring(0, open).Trim();
            string baseText = text.Substring(open + 1, close - open - 1).Trim();
            int offset = offsetText.Length == 0 ? 0 : ParseImmediate(offsetText);
            return (offset, ParseGpr(baseText));
        }

        private static int ParseFenceImmediate(ImmutableArray<string> operands)
        {
            if (operands.Length == 0)
                return 0xFF;
            if (operands.Length == 1)
                return ParseUimm(operands[0], 8);
            if (operands.Length == 2)
                return (ParseFenceMask(operands[0]) << 4) | ParseFenceMask(operands[1]);
            throw new FormatException("Invalid RISC-V fence operand count");
        }

        private static int ParseFenceMask(string text)
        {
            int mask = 0;
            foreach (char c in text.Trim().ToLowerInvariant())
            {
                switch (c)
                {
                    case 'i':
                        mask |= 8;
                        break;
                    case 'o':
                        mask |= 4;
                        break;
                    case 'r':
                        mask |= 2;
                        break;
                    case 'w':
                        mask |= 1;
                        break;
                    case '0':
                        break;
                    default:
                        throw new FormatException($"Invalid RISC-V fence mask: {text}");
                }
            }
            return mask;
        }

        private static int ParseCsr(string text)
            => RiscVCsrs.Parse(text);

        private static int ParseVType(ImmutableArray<string> operands, int start)
        {
            if (start >= operands.Length)
                throw new FormatException("Missing RISC-V vector type");
            if (operands.Length - start == 1 && TryParseImmediate(operands[start], out int raw))
                return raw;

            int vsew = -1;
            int vlmul = -1;
            bool tailAgnostic = false;
            bool maskAgnostic = false;

            for (int i = start; i < operands.Length; i++)
            {
                string token = operands[i].Trim().ToLowerInvariant();
                switch (token)
                {
                    case "e8":
                        vsew = 0;
                        break;
                    case "e16":
                        vsew = 1;
                        break;
                    case "e32":
                        vsew = 2;
                        break;
                    case "e64":
                        vsew = 3;
                        break;
                    case "m1":
                        vlmul = 0;
                        break;
                    case "m2":
                        vlmul = 1;
                        break;
                    case "m4":
                        vlmul = 2;
                        break;
                    case "m8":
                        vlmul = 3;
                        break;
                    case "mf8":
                        vlmul = 5;
                        break;
                    case "mf4":
                        vlmul = 6;
                        break;
                    case "mf2":
                        vlmul = 7;
                        break;
                    case "ta":
                        tailAgnostic = true;
                        break;
                    case "tu":
                        tailAgnostic = false;
                        break;
                    case "ma":
                        maskAgnostic = true;
                        break;
                    case "mu":
                        maskAgnostic = false;
                        break;
                    default:
                        throw new FormatException($"Invalid RISC-V vector type token: {operands[i]}");
                }
            }

            if (vsew < 0 || vlmul < 0)
                throw new FormatException("RISC-V vector type requires SEW and LMUL");
            return vlmul | (vsew << 3) | (tailAgnostic ? 1 << 6 : 0) | (maskAgnostic ? 1 << 7 : 0);
        }

        private static int ParseUimm(string text, int bits)
        {
            int value = ParseImmediate(text);
            if (value < 0 || value > ((1 << bits) - 1))
                throw new FormatException($"Unsigned immediate does not fit {bits} bits: {text}");
            return value;
        }

        private static int ParseImmediate(string text)
        {
            if (TryParseImmediate(text, out int immediate))
                return immediate;
            throw new FormatException($"Invalid RISC-V immediate: {text}");
        }

        private static bool TryParseImmediate(string text, out int immediate)
        {
            text = text.Trim().Replace("_", string.Empty);
            if (text.Length == 0)
            {
                immediate = 0;
                return false;
            }

            int sign = 1;
            if (text[0] == '+')
                text = text.Substring(1);
            else if (text[0] == '-')
            {
                sign = -1;
                text = text.Substring(1);
            }

            NumberStyles style = NumberStyles.Integer;
            int radix = 10;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
                style = NumberStyles.HexNumber;
                radix = 16;
            }
            else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
                radix = 2;
            }

            try
            {
                long value;
                if (radix == 2)
                {
                    value = 0;
                    foreach (char c in text)
                    {
                        if (c is not '0' and not '1')
                        {
                            immediate = 0;
                            return false;
                        }
                        value = (value << 1) | (c == '1' ? 1L : 0L);
                    }
                }
                else
                {
                    if (!long.TryParse(text, style, CultureInfo.InvariantCulture, out value))
                    {
                        immediate = 0;
                        return false;
                    }
                }

                value *= sign;
                if (value < int.MinValue || value > uint.MaxValue)
                {
                    immediate = 0;
                    return false;
                }
                immediate = unchecked((int)value);
                return true;
            }
            catch (OverflowException)
            {
                immediate = 0;
                return false;
            }
        }

        private static ImmutableArray<string> SplitOperands(string operandText)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            if (string.IsNullOrWhiteSpace(operandText))
                return builder.MoveToImmutable();

            int start = 0;
            int parenDepth = 0;
            for (int i = 0; i <= operandText.Length; i++)
            {
                bool atEnd = i == operandText.Length;
                char c = atEnd ? '\0' : operandText[i];
                if (!atEnd)
                {
                    if (c == '(')
                        parenDepth++;
                    else if (c == ')')
                        parenDepth--;
                }
                if (atEnd || (c == ',' && parenDepth == 0))
                {
                    string operand = operandText.Substring(start, i - start).Trim();
                    if (operand.Length == 0)
                        throw new FormatException("Empty RISC-V operand");
                    builder.Add(operand);
                    start = i + 1;
                }
            }
            return builder.MoveToImmutable();
        }

        private static string StripComment(string line)
        {
            int index = line.Length;
            int hash = line.IndexOf('#');
            if (hash >= 0 && hash < index)
                index = hash;
            int semi = line.IndexOf(';');
            if (semi >= 0 && semi < index)
                index = semi;
            int slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0 && slash < index)
                index = slash;
            return line.Substring(0, index);
        }

        private static int FirstWhitespace(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i;
            }
            return -1;
        }

        private static int FindLabelColon(string line)
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
                return -1;
            string before = line.Substring(0, colon).Trim();
            if (before.Length == 0 || before.IndexOfAny(new[] { ' ', '\t', ',' }) >= 0)
                return -1;
            return colon;
        }

        private static void ValidateLabel(string label, int lineNumber)
        {
            if (label.Length == 0 || !(char.IsLetter(label[0]) || label[0] == '_' || label[0] == '.'))
                throw new FormatException($"Invalid RISC-V label on line {lineNumber}");
            for (int i = 1; i < label.Length; i++)
            {
                char c = label[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$'))
                    throw new FormatException($"Invalid RISC-V label on line {lineNumber}");
            }
        }

        private static void ValidateOperandCount(string mnemonic, ImmutableArray<string> operands, int expected, int lineNumber)
        {
            if (operands.Length != expected)
                throw new FormatException($"RISC-V instruction '{mnemonic}' expects {expected} operands on line {lineNumber}");
        }
    }

    internal static class RiscVAssemblyWriter
    {
        public static string Write(RiscVProgram obj, RiscVAssemblyWriterOptions? options = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            return Write(obj.Text, options);
        }

        public static string Write(RVTextSection text, RiscVAssemblyWriterOptions? options = null)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            options ??= RiscVAssemblyWriterOptions.Default;

            if (!options.IncludeLabels || text.Labels.Count == 0)
                return Write(text.Instructions, options);

            var labelsByPc = new Dictionary<int, List<string>>();
            foreach (var label in text.Labels)
            {
                if (!labelsByPc.TryGetValue(label.Value, out var list))
                {
                    list = new List<string>();
                    labelsByPc.Add(label.Value, list);
                }
                list.Add(label.Key);
            }

            var sb = new StringBuilder();
            for (int i = 0; i < text.Instructions.Length; i++)
            {
                int pc = i * 4;
                if (labelsByPc.TryGetValue(pc, out var labels))
                {
                    labels.Sort(StringComparer.Ordinal);
                    foreach (var label in labels)
                        sb.Append(label).AppendLine(":");
                }
                sb.Append("    ").AppendLine(WriteInstruction(text.Instructions[i], options));
            }
            return sb.ToString();
        }

        public static string Write(IEnumerable<RVInstruction> instructions, RiscVAssemblyWriterOptions? options = null)
        {
            if (instructions is null)
                throw new ArgumentNullException(nameof(instructions));
            options ??= RiscVAssemblyWriterOptions.Default;
            var sb = new StringBuilder();
            foreach (var instruction in instructions)
                sb.AppendLine(WriteInstruction(instruction, options));
            return sb.ToString();
        }

        public static string WriteInstruction(RVInstruction instruction, RiscVAssemblyWriterOptions? options = null)
        {
            options ??= RiscVAssemblyWriterOptions.Default;
            if (options.UsePseudoInstructions && TryWritePseudo(instruction, options, out var pseudo))
                return pseudo;

            var metadata = RVInstructionTable.Get(instruction.Opcode);
            string mnemonic = RVInstructionTable.GetMnemonic(instruction.Opcode);
            switch (metadata.Format)
            {
                case RVInstructionFormat.Raw:
                    return ".word 0x" + unchecked((uint)instruction.Immediate).ToString("X8", CultureInfo.InvariantCulture).ToLowerInvariant();
                case RVInstructionFormat.R:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
                case RVInstructionFormat.I:
                    if (RVInstructionTable.IsLoad(instruction.Opcode) || instruction.Opcode == RVInstrKind.Jalr)
                        return mnemonic + " " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction) + "(" + Reg(instruction.Rs1, options) + ")";
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + ImmOrSymbol(instruction);
                case RVInstructionFormat.ShiftI:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + instruction.Immediate.ToString();
                case RVInstructionFormat.S:
                    return mnemonic + " " + Reg(instruction.Rs2, options) + ", " + ImmOrSymbol(instruction) + "(" + Reg(instruction.Rs1, options) + ")";
                case RVInstructionFormat.FloatLoad:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction) + "(" + Reg(instruction.Rs1, options) + ")";
                case RVInstructionFormat.FloatStore:
                    return mnemonic + " " + Reg(instruction.Rs2, options) + ", " + ImmOrSymbol(instruction) + "(" + Reg(instruction.Rs1, options) + ")";
                case RVInstructionFormat.FloatRRR:
                case RVInstructionFormat.FloatCompare:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
                case RVInstructionFormat.FloatConvertFromInteger:
                case RVInstructionFormat.FloatConvertToInteger:
                case RVInstructionFormat.FloatConvert:
                case RVInstructionFormat.FloatMoveToInteger:
                case RVInstructionFormat.FloatMoveFromInteger:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options);
                case RVInstructionFormat.B:
                    return mnemonic + " " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options) + ", " + ImmOrSymbol(instruction);
                case RVInstructionFormat.U:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction);
                case RVInstructionFormat.J:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction);
                case RVInstructionFormat.Fence:
                    return WriteFence(instruction.Immediate);
                case RVInstructionFormat.System:
                    return mnemonic;
                case RVInstructionFormat.PrivilegedFence:
                    return WritePrivilegedFence(mnemonic, instruction, options);
                case RVInstructionFormat.Csr:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + RiscVCsrs.Format(instruction.Immediate) + ", " + Reg(instruction.Rs1, options);
                case RVInstructionFormat.CsrImmediate:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", " + RiscVCsrs.Format(instruction.Immediate) + ", " + ((int)instruction.Rs1).ToString();
                case RVInstructionFormat.Amo:
                    return WriteAtomic(mnemonic, instruction, options);
                case RVInstructionFormat.VectorConfig:
                    return WriteVectorConfig(mnemonic, instruction, options);
                case RVInstructionFormat.VectorLoad:
                    return mnemonic + " " + Reg(instruction.Rd, options) + ", (" + Reg(instruction.Rs1, options) + ")" + MaskSuffix(instruction);
                case RVInstructionFormat.VectorStore:
                    return mnemonic + " " + Reg(instruction.Rs2, options) + ", (" + Reg(instruction.Rs1, options) + ")" + MaskSuffix(instruction);
                case RVInstructionFormat.VectorOp:
                    return WriteVectorOp(mnemonic, metadata, instruction, options);
                default:
                    throw new NotSupportedException($"Unsupported RISC-V instruction format: {metadata.Format}");
            }
        }

        private static bool TryWritePseudo(RVInstruction instruction, RiscVAssemblyWriterOptions options, out string text)
        {
            if (instruction.Opcode == RVInstrKind.Addi && instruction.Rd == RVRegister.X0 && instruction.Rs1 == RVRegister.X0 && instruction.Immediate == 0)
            {
                text = "nop";
                return true;
            }
            if (instruction.Opcode == RVInstrKind.Addi && instruction.Immediate == 0 && instruction.Rd != RVRegister.X0)
            {
                text = "mv " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options);
                return true;
            }
            if (instruction.Opcode == RVInstrKind.Jalr && instruction.Rd == RVRegister.X0 && instruction.Rs1 == RVRegister.X1 && instruction.Immediate == 0)
            {
                text = "ret";
                return true;
            }
            if (instruction.Opcode == RVInstrKind.Jal && instruction.Rd == RVRegister.X0)
            {
                text = "j " + ImmOrSymbol(instruction);
                return true;
            }
            text = string.Empty;
            return false;
        }

        private static string WritePrivilegedFence(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            if ((instruction.Rs1 == RVRegister.Invalid || instruction.Rs1 == RVRegister.X0) && (instruction.Rs2 == RVRegister.Invalid || instruction.Rs2 == RVRegister.X0))
                return mnemonic;
            if (instruction.Rs2 == RVRegister.Invalid || instruction.Rs2 == RVRegister.X0)
                return mnemonic + " " + Reg(instruction.Rs1, options);
            return mnemonic + " " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
        }

        private static string WriteAtomic(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            mnemonic += AtomicSuffix(instruction);
            if (instruction.Opcode is RVInstrKind.LrW or RVInstrKind.LrD)
                return mnemonic + " " + Reg(instruction.Rd, options) + ", (" + Reg(instruction.Rs1, options) + ")";
            return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", (" + Reg(instruction.Rs1, options) + ")";
        }

        private static string WriteVectorConfig(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            if (instruction.Opcode == RVInstrKind.Vsetvli)
                return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + FormatVType(instruction.Immediate, options);
            if (instruction.Opcode == RVInstrKind.Vsetivli)
                return mnemonic + " " + Reg(instruction.Rd, options) + ", " + ((int)instruction.Rs1) + ", " + FormatVType(instruction.Immediate, options);
            return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
        }

        private static string WriteVectorOp(string mnemonic, RVInstructionMetadata metadata, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            if (metadata.Funct3 == 0)
                return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", " + Reg(instruction.Rs1, options) + MaskSuffix(instruction);
            if (metadata.Funct3 == 4)
                return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", " + Reg(instruction.Rs1, options) + MaskSuffix(instruction);
            return mnemonic + " " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", " + instruction.Immediate
                + MaskSuffix(instruction);
        }

        private static string Reg(RVRegister register, RiscVAssemblyWriterOptions options)
            => RVRegisters.Format(register, options.UseAbiRegisterNames);

        private static string ImmOrSymbol(RVInstruction instruction)
            => instruction.HasSymbol ? instruction.Symbol! : instruction.Immediate.ToString();

        private static string AtomicSuffix(RVInstruction instruction)
        {
            if (instruction.AtomicAcquire && instruction.AtomicRelease)
                return ".aqrl";
            if (instruction.AtomicAcquire)
                return ".aq";
            if (instruction.AtomicRelease)
                return ".rl";
            return string.Empty;
        }

        private static string MaskSuffix(RVInstruction instruction)
            => instruction.VectorUnmasked ? string.Empty : ", v0.t";

        private static string WriteFence(int immediate)
        {
            if (immediate == 0xFF)
                return "fence";
            int pred = (immediate >> 4) & 0xF;
            int succ = immediate & 0xF;
            return "fence " + FormatFenceMask(pred) + ", " + FormatFenceMask(succ);
        }

        private static string FormatFenceMask(int mask)
        {
            var sb = new StringBuilder();
            if ((mask & 8) != 0)
                sb.Append('i');
            if ((mask & 4) != 0)
                sb.Append('o');
            if ((mask & 2) != 0)
                sb.Append('r');
            if ((mask & 1) != 0)
                sb.Append('w');
            return sb.Length == 0 ? "0" : sb.ToString();
        }

        private static string FormatVType(int vtype, RiscVAssemblyWriterOptions options)
        {
            if (!options.FormatVectorTypeNames)
                return vtype.ToString(CultureInfo.InvariantCulture);
            int vlmul = vtype & 7;
            int vsew = (vtype >> 3) & 7;
            string sew = vsew switch
            {
                0 => "e8",
                1 => "e16",
                2 => "e32",
                3 => "e64",
                _ => "e?" + vsew.ToString(CultureInfo.InvariantCulture),
            };
            string lmul = vlmul switch
            {
                0 => "m1",
                1 => "m2",
                2 => "m4",
                3 => "m8",
                5 => "mf8",
                6 => "mf4",
                7 => "mf2",
                _ => "m?" + vlmul.ToString(CultureInfo.InvariantCulture),
            };
            string tail = (vtype & (1 << 6)) != 0 ? "ta" : "tu";
            string mask = (vtype & (1 << 7)) != 0 ? "ma" : "mu";
            return $"{sew}, {lmul}, {tail}, {mask}";
        }
    }
}
