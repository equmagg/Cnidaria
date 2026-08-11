using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace Cnidaria.RiscV
{
    public sealed class RiscVAssemblyWriterOptions
    {
        public static RiscVAssemblyWriterOptions Default { get; } = new RiscVAssemblyWriterOptions();

        public bool UseAbiRegisterNames { get; set; } = true;
        public bool IncludeLabels { get; set; } = true;
        public bool UsePseudoInstructions { get; set; } = false;
        public bool FormatVectorTypeNames { get; set; } = true;
    }

    public sealed class RiscVAssemblySettings
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Values => _values;

        public RiscVAssemblySettings Define(string name, string value)
        {
            ValidateName(name);
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            _values[name] = value;
            return this;
        }

        public RiscVAssemblySettings Define(string name, ulong value)
            => Define(name, "0x" + value.ToString("x", CultureInfo.InvariantCulture));

        public RiscVAssemblySettings Define(string name, long value)
            => Define(name, value.ToString(CultureInfo.InvariantCulture));

        public RiscVAssemblySettings Define(string name, int value)
            => Define(name, value.ToString(CultureInfo.InvariantCulture));

        public string Expand(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (text.IndexOf("${", StringComparison.Ordinal) < 0)
                return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '$' || i + 1 >= text.Length || text[i + 1] != '{')
                {
                    sb.Append(text[i]);
                    continue;
                }

                int close = text.IndexOf('}', i + 2);
                if (close < 0)
                    throw new FormatException("Unterminated RISC-V assembly setting reference");

                string name = text.Substring(i + 2, close - i - 2);
                ValidateName(name);
                if (!_values.TryGetValue(name, out var value))
                    throw new KeyNotFoundException($"RISC-V assembly setting '{name}' is not defined");

                sb.Append(value);
                i = close;
            }
            return sb.ToString();
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("RISC-V assembly setting name is empty", nameof(name));
            if (!(char.IsLetter(name[0]) || name[0] == '_'))
                throw new ArgumentException($"Invalid RISC-V assembly setting name: {name}", nameof(name));
            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
                    throw new ArgumentException($"Invalid RISC-V assembly setting name: {name}", nameof(name));
            }
        }
    }

    public static class RiscVAssembler
    {
        public static RiscVProgram Assemble(string text, RVTarget target)
            => RiscVAssemblyParser.Parse(text, target, null);

        public static RiscVProgram Assemble(string text, RVTarget target, RiscVAssemblySettings? settings)
            => RiscVAssemblyParser.Parse(text, target, settings);

        public static RiscVProgram Parse(string text, RVTarget target)
            => Assemble(text, target);

        public static RiscVProgram Parse(string text, RVTarget target, RiscVAssemblySettings? settings)
            => Assemble(text, target, settings);
    }

    public static class RiscVDisassembler
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
            => Parse(text, target, null);

        public static RiscVProgram Parse(string text, RVTarget target, RiscVAssemblySettings? settings)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (text.IndexOf("${", StringComparison.Ordinal) >= 0)
                text = (settings ?? new RiscVAssemblySettings()).Expand(text);

            var instructions = ImmutableArray.CreateBuilder<RVInstruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var pc = 0;

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
                    labels.Add(label, pc);
                    line = line.Substring(colon + 1).Trim();
                    if (line.Length == 0)
                        break;
                }

                if (line.Length == 0)
                    continue;

                foreach (var instruction in ParseInstruction(line, lineIndex + 1))
                {
                    instructions.Add(instruction);
                    pc = checked(pc + RVInstructionTable.GetEncodedSize(instruction.Opcode));
                }
            }

            return new RiscVProgram(target, instructions.ToImmutable(), labels);
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

            if (mnemonic == ".hword" || mnemonic == ".half" || mnemonic == ".2byte")
            {
                ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                return One(RVInstruction.Raw16(unchecked((ushort)ParseImmediate(operands[0]))));
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
                case RVInstructionFormat.BitmanipUnary:
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    return One(RVInstruction.R(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), RVRegister.X0));
                case RVInstructionFormat.BitmanipShiftI:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(RVInstruction.I(opcode, ParseGpr(operands[0]), ParseGpr(operands[1]), ParseImmediate(operands[2])));
                case RVInstructionFormat.Compressed:
                    return One(ParseCompressed(opcode, operands, lineNumber));
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
                case RVInstructionFormat.HypervisorLoad:
                    return One(ParseHypervisorLoad(opcode, operands, lineNumber));
                case RVInstructionFormat.HypervisorStore:
                    return One(ParseHypervisorStore(opcode, operands, lineNumber));
                case RVInstructionFormat.Csr:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(new RVInstruction(opcode, ParseGpr(operands[0]), ParseGpr(operands[2]), RVRegister.Invalid, RiscVCsrs.Parse(operands[1])));
                case RVInstructionFormat.CsrImmediate:
                    ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                    return One(new RVInstruction(opcode, ParseGpr(operands[0]), (RVRegister)ParseUimm(operands[2], 5), RVRegister.Invalid, RiscVCsrs.Parse(operands[1])));
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
                    instructions = ParseLoadImmediate(ParseGpr(operands[0]), ParseImmediate64(operands[1]));
                    return true;
                case "la":
                    ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                    instructions = ParseLoadAddress(ParseGpr(operands[0]), operands[1]);
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

        private static IEnumerable<RVInstruction> ParseLoadAddress(RVRegister rd, string symbol)
        {
            return new[]
            {
                RVInstruction.U(RVInstrKind.Auipc, rd, 0).WithSymbol(symbol, RVRelocationKind.AbsoluteUpper20),
                RVInstruction.I(RVInstrKind.Addi, rd, rd, 0).WithSymbol(symbol, RVRelocationKind.AbsoluteLow12),
            };
        }

        private static IEnumerable<RVInstruction> ParseLoadImmediate(RVRegister rd, ulong immediate)
        {
            var signed = unchecked((long)immediate);
            if (signed >= -2048 && signed <= 2047)
                return One(RVInstruction.I(RVInstrKind.Addi, rd, RVRegister.X0, checked((int)signed)));

            var chunks = ImmutableArray.CreateBuilder<int>();
            var value = immediate;
            while (value != 0)
            {
                var chunk = (int)(value & 0xFFFUL);
                if (chunk >= 0x800)
                {
                    chunk -= 0x1000;
                    value += 0x1000UL;
                }
                chunks.Add(chunk);
                value >>= 12;
            }

            var result = ImmutableArray.CreateBuilder<RVInstruction>();
            result.Add(RVInstruction.I(RVInstrKind.Addi, rd, RVRegister.X0, chunks[chunks.Count - 1]));
            for (var i = chunks.Count - 2; i >= 0; i--)
            {
                result.Add(RVInstruction.I(RVInstrKind.Slli, rd, rd, 12));
                if (chunks[i] != 0)
                    result.Add(RVInstruction.I(RVInstrKind.Addi, rd, rd, chunks[i]));
            }
            return result.ToImmutable();
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

        private static RVInstruction ParseCompressed(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            switch (opcode)
            {
                case RVInstrKind.CAddi4Spn:
                    ValidateOperandCount("c.addi4spn", operands, 2, lineNumber);
                    return new RVInstruction(opcode, ParseGpr(operands[0]), RVRegister.X2, RVRegister.Invalid, ParseImmediate(operands[1]));
                case RVInstrKind.CLw:
                case RVInstrKind.CLd:
                case RVInstrKind.CFlw:
                case RVInstrKind.CFld:
                case RVInstrKind.CLwSp:
                case RVInstrKind.CLdSp:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    return ParseCompressedLoad(opcode, operands);
                case RVInstrKind.CSw:
                case RVInstrKind.CSd:
                case RVInstrKind.CFsw:
                case RVInstrKind.CFsd:
                case RVInstrKind.CSwSp:
                case RVInstrKind.CSdSp:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    return ParseCompressedStore(opcode, operands);
                case RVInstrKind.CNop:
                case RVInstrKind.CEbreak:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 0, lineNumber);
                    return new RVInstruction(opcode);
                case RVInstrKind.CAddi:
                case RVInstrKind.CAddiw:
                case RVInstrKind.CSlli:
                case RVInstrKind.CSrli:
                case RVInstrKind.CSrai:
                case RVInstrKind.CAndi:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    var rd = ParseGpr(operands[0]);
                    return new RVInstruction(opcode, rd, rd, RVRegister.Invalid, ParseImmediate(operands[1]));
                case RVInstrKind.CLi:
                case RVInstrKind.CLui:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    return new RVInstruction(opcode, ParseGpr(operands[0]), RVRegister.Invalid, RVRegister.Invalid, ParseImmediate(operands[1]));
                case RVInstrKind.CAddi16Sp:
                    if (operands.Length == 1)
                        return new RVInstruction(opcode, RVRegister.X2, RVRegister.X2, RVRegister.Invalid, ParseImmediate(operands[0]));
                    if (operands.Length == 2)
                    {
                        var sp = ParseGpr(operands[0]);
                        if (sp != RVRegister.X2)
                            throw new FormatException("c.addi16sp requires sp");
                        return new RVInstruction(opcode, RVRegister.X2, RVRegister.X2, RVRegister.Invalid, ParseImmediate(operands[1]));
                    }
                    throw new FormatException($"Invalid c.addi16sp operand count on line {lineNumber}");
                case RVInstrKind.CSub:
                case RVInstrKind.CXor:
                case RVInstrKind.COr:
                case RVInstrKind.CAnd:
                case RVInstrKind.CSubw:
                case RVInstrKind.CAddw:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    var crd = ParseGpr(operands[0]);
                    return RVInstruction.R(opcode, crd, crd, ParseGpr(operands[1]));
                case RVInstrKind.CJ:
                    ValidateOperandCount("c.j", operands, 1, lineNumber);
                    return TryParseImmediate(operands[0], out int jImm) ? RVInstruction.J(opcode, RVRegister.X0, jImm) : RVInstruction.J(opcode, RVRegister.X0, operands[0]);
                case RVInstrKind.CJal:
                    ValidateOperandCount("c.jal", operands, 1, lineNumber);
                    return TryParseImmediate(operands[0], out int jalImm) ? RVInstruction.J(opcode, RVRegister.X1, jalImm) : RVInstruction.J(opcode, RVRegister.X1, operands[0]);
                case RVInstrKind.CBeqz:
                case RVInstrKind.CBnez:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    var rs1 = ParseGpr(operands[0]);
                    return TryParseImmediate(operands[1], out int branchImm) ? RVInstruction.B(opcode, rs1, RVRegister.X0, branchImm) : RVInstruction.B(opcode, rs1, RVRegister.X0, operands[1]);
                case RVInstrKind.CJr:
                case RVInstrKind.CJalr:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 1, lineNumber);
                    return new RVInstruction(opcode, RVRegister.Invalid, ParseGpr(operands[0]), RVRegister.Invalid);
                case RVInstrKind.CMv:
                case RVInstrKind.CAdd:
                    ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
                    return RVInstruction.R(opcode, ParseGpr(operands[0]), ParseGpr(operands[0]), ParseGpr(operands[1]));
                default:
                    throw new FormatException($"Unsupported RISC-V compressed instruction on line {lineNumber}");
            }
        }

        private static RVInstruction ParseCompressedLoad(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var memory = ParseMemoryOperand(operands[1]);
            var rd = opcode is RVInstrKind.CFlw or RVInstrKind.CFld ? ParseFpr(operands[0]) : ParseGpr(operands[0]);
            return RVInstruction.I(opcode, rd, memory.Base, memory.Offset);
        }

        private static RVInstruction ParseCompressedStore(RVInstrKind opcode, ImmutableArray<string> operands)
        {
            var memory = ParseMemoryOperand(operands[1]);
            var rs2 = opcode is RVInstrKind.CFsw or RVInstrKind.CFsd ? ParseFpr(operands[0]) : ParseGpr(operands[0]);
            return RVInstruction.S(opcode, rs2, memory.Base, memory.Offset);
        }

        private static RVInstruction ParseHypervisorLoad(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
            var memory = ParseMemoryOperand(operands[1]);
            if (memory.Offset != 0)
                throw new FormatException($"Hypervisor load does not accept an offset on line {lineNumber}");
            return new RVInstruction(opcode, ParseGpr(operands[0]), memory.Base);
        }

        private static RVInstruction ParseHypervisorStore(RVInstrKind opcode, ImmutableArray<string> operands, int lineNumber)
        {
            ValidateOperandCount(RVInstructionTable.GetMnemonic(opcode), operands, 2, lineNumber);
            var memory = ParseMemoryOperand(operands[1]);
            if (memory.Offset != 0)
                throw new FormatException($"Hypervisor store does not accept an offset on line {lineNumber}");
            return new RVInstruction(opcode, rs1: memory.Base, rs2: ParseGpr(operands[0]));
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
            if (metadata.Funct3 is 0 or 1 or 2)
                return RVInstruction.Vv(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseVreg(operands[2]), unmasked);
            if (metadata.Funct3 is 4 or 6)
                return RVInstruction.Vx(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseGpr(operands[2]), unmasked);
            if (metadata.Funct3 == 5)
                return RVInstruction.Vx(opcode, ParseVreg(operands[0]), ParseVreg(operands[1]), ParseFpr(operands[2]), unmasked);
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
                throw new FormatException($"Expected integer register: {text}");
            return register;
        }

        private static RVRegister ParseFpr(string text)
        {
            var register = RVRegisters.Parse(text);
            if (!RVRegisters.IsFloat(register))
                throw new FormatException($"Expected floating-point register: {text}");
            return register;
        }

        private static RVRegister ParseVreg(string text)
        {
            var register = RVRegisters.Parse(text);
            if (!RVRegisters.IsVector(register))
                throw new FormatException($"Expected vector register: {text}");
            return register;
        }

        private static (int Offset, RVRegister Base) ParseMemoryOperand(string text)
        {
            int open = text.IndexOf('(');
            int close = text.LastIndexOf(')');
            if (open < 0 || close != text.Length - 1 || close <= open + 1)
                throw new FormatException($"Invalid memory operand: {text}");
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
            throw new FormatException("Invalid fence operand count");
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
                        throw new FormatException($"Invalid fence mask: {text}");
                }
            }
            return mask;
        }


        private static int ParseVType(ImmutableArray<string> operands, int start)
        {
            if (start >= operands.Length)
                throw new FormatException("Missing vector type");
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

        private static ulong ParseImmediate64(string text)
        {
            if (TryParseImmediate64(text, out var immediate))
                return immediate;
            throw new FormatException($"Invalid RISC-V immediate: {text}");
        }

        private static bool TryParseImmediate64(string text, out ulong immediate)
        {
            text = text.Trim().Replace("_", string.Empty);
            if (text.Length == 0)
            {
                immediate = 0;
                return false;
            }

            var negative = false;
            if (text[0] == '+')
                text = text.Substring(1);
            else if (text[0] == '-')
            {
                negative = true;
                text = text.Substring(1);
            }

            if (text.Length == 0)
            {
                immediate = 0;
                return false;
            }

            var radix = 10;
            var style = NumberStyles.None;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
                radix = 16;
                style = NumberStyles.HexNumber;
            }
            else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
                radix = 2;
            }

            if (text.Length == 0)
            {
                immediate = 0;
                return false;
            }

            try
            {
                ulong value;
                if (radix == 2)
                {
                    value = 0;
                    foreach (var c in text)
                    {
                        if (c is not '0' and not '1')
                        {
                            immediate = 0;
                            return false;
                        }
                        value = checked((value << 1) | (c == '1' ? 1UL : 0UL));
                    }
                }
                else if (radix == 16)
                {
                    if (!ulong.TryParse(text, style, CultureInfo.InvariantCulture, out value))
                    {
                        immediate = 0;
                        return false;
                    }
                }
                else
                {
                    if (negative)
                    {
                        if (!long.TryParse("-" + text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
                        {
                            immediate = 0;
                            return false;
                        }
                        immediate = unchecked((ulong)signedValue);
                        return true;
                    }

                    if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        immediate = 0;
                        return false;
                    }
                }

                immediate = negative ? unchecked(0UL - value) : value;
                return true;
            }
            catch (OverflowException)
            {
                immediate = 0;
                return false;
            }
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
                return builder.ToImmutable();

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
            return builder.ToImmutable();
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
                throw new FormatException($"Invalid label on line {lineNumber}");
            for (int i = 1; i < label.Length; i++)
            {
                char c = label[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$'))
                    throw new FormatException($"Invalid label on line {lineNumber}");
            }
        }

        private static void ValidateOperandCount(string mnemonic, ImmutableArray<string> operands, int expected, int lineNumber)
        {
            if (operands.Length != expected)
                throw new FormatException($"instruction '{mnemonic}' expects {expected} operands on line {lineNumber}");
        }
    }

    internal static class RiscVAssemblyWriter
    {
        public static string Write(RiscVProgram obj, RiscVAssemblyWriterOptions? options = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            options ??= RiscVAssemblyWriterOptions.Default;
            var sb = new StringBuilder();
            WriteSymbolDeclarations(sb, obj.Symbols, options);
            sb.AppendLine(".section .text");
            WriteTextSection(sb, obj.Text, obj.Symbols, options);
            foreach (var section in obj.DataSections)
            {
                sb.AppendLine();
                WriteDataSection(sb, obj, section, options);
            }
            return sb.ToString();
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
            var pc = 0;
            for (int i = 0; i < text.Instructions.Length; i++)
            {
                if (labelsByPc.TryGetValue(pc, out var labels))
                {
                    labels.Sort(StringComparer.Ordinal);
                    foreach (var label in labels)
                        sb.Append(label).AppendLine(":");
                }
                sb.Append("    ").AppendLine(WriteInstruction(text.Instructions[i], options));
                pc = checked(pc + RVInstructionTable.GetEncodedSize(text.Instructions[i].Opcode));
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
        private static void WriteSymbolDeclarations(StringBuilder sb, ImmutableArray<RVObjectSymbol> symbols, RiscVAssemblyWriterOptions options)
        {
            if (!options.IncludeLabels)
                return;

            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in symbols)
            {
                if (symbol.Binding is not RVObjectSymbolBinding.Global and not RVObjectSymbolBinding.External ||
                    symbol.Kind == RVObjectSymbolKind.Section ||
                    string.IsNullOrEmpty(symbol.Name) ||
                    !emitted.Add(symbol.Name))
                {
                    continue;
                }

                sb.Append(symbol.Binding == RVObjectSymbolBinding.Global ? ".globl " : ".extern ")
                    .AppendLine(symbol.Name);
            }

            if (emitted.Count != 0)
                sb.AppendLine();
        }

        private static void WriteTextSection(
            StringBuilder sb,
            RVTextSection text,
            ImmutableArray<RVObjectSymbol> symbols,
            RiscVAssemblyWriterOptions options)
        {
            Dictionary<int, List<string>>? labelsByPc = null;
            if (options.IncludeLabels)
            {
                labelsByPc = new Dictionary<int, List<string>>();
                foreach (var label in text.Labels)
                    AddLabel(labelsByPc, label.Value, label.Key);
                foreach (var symbol in symbols)
                {
                    if (symbol.Binding != RVObjectSymbolBinding.External &&
                        symbol.Kind != RVObjectSymbolKind.Section &&
                        string.Equals(symbol.SectionName, ".text", StringComparison.Ordinal))
                    {
                        AddLabel(labelsByPc, symbol.Offset, symbol.Name);
                    }
                }
            }

            var pc = 0;
            for (var i = 0; i < text.Instructions.Length; i++)
            {
                WriteLabels(sb, labelsByPc, pc);
                sb.Append("    ").AppendLine(WriteInstruction(text.Instructions[i], options));
                pc = checked(pc + RVInstructionTable.GetEncodedSize(text.Instructions[i].Opcode));
            }

            WriteLabels(sb, labelsByPc, text.SizeInBytes);
        }
        private static void WriteDataSection(
            StringBuilder sb,
            RiscVProgram obj,
            RVDataSection section,
            RiscVAssemblyWriterOptions options)
        {
            sb.Append(".section ").AppendLine(section.Name);
            if (section.Alignment > 1)
                sb.Append(".balign ").AppendLine(section.Alignment.ToString(CultureInfo.InvariantCulture));

            Dictionary<int, List<string>>? labelsByOffset = null;
            if (options.IncludeLabels)
            {
                labelsByOffset = new Dictionary<int, List<string>>();
                foreach (var symbol in obj.Symbols)
                {
                    if (symbol.Binding != RVObjectSymbolBinding.External &&
                        symbol.Kind != RVObjectSymbolKind.Section &&
                        string.Equals(symbol.SectionName, section.Name, StringComparison.Ordinal))
                    {
                        AddLabel(labelsByOffset, symbol.Offset, symbol.Name);
                    }
                }
            }

            var relocationsByOffset = new Dictionary<int, RVObjectRelocation>();
            foreach (var relocation in section.Relocations)
            {
                if (!relocationsByOffset.TryAdd(relocation.Offset, relocation))
                    throw new InvalidOperationException($"Multiple RISC-V data relocations at {section.Name}+0x{relocation.Offset:x}");
            }

            if (section.Kind == RVObjectSectionKind.Bss)
                WriteZeroSection(sb, section.BssSize, labelsByOffset);
            else
                WriteInitializedData(sb, obj.Target, section, labelsByOffset, relocationsByOffset);
        }
        private static void WriteInitializedData(
            StringBuilder sb,
            RVTarget target,
            RVDataSection section,
            Dictionary<int, List<string>>? labelsByOffset,
            Dictionary<int, RVObjectRelocation> relocationsByOffset)
        {
            var offset = 0;
            while (offset < section.Data.Length)
            {
                WriteLabels(sb, labelsByOffset, offset);

                if (relocationsByOffset.TryGetValue(offset, out var relocation))
                {
                    var width = GetDataRelocationWidth(target, relocation.Kind);
                    if (checked(offset + width) > section.Data.Length)
                        throw new InvalidOperationException($"RISC-V data relocation exceeds section bounds at {section.Name}+0x{offset:x}");
                    WriteDataRelocation(sb, relocation, width);
                    offset += width;
                    continue;
                }

                if (section.Data[offset] == 0)
                {
                    var count = 1;
                    while (offset + count < section.Data.Length &&
                           section.Data[offset + count] == 0 &&
                           !HasBoundary(labelsByOffset, relocationsByOffset, offset + count))
                    {
                        count++;
                    }
                    sb.Append("    .zero ").AppendLine(count.ToString(CultureInfo.InvariantCulture));
                    offset += count;
                    continue;
                }

                var byteCount = 1;
                while (byteCount < 16 &&
                       offset + byteCount < section.Data.Length &&
                       section.Data[offset + byteCount] != 0 &&
                       !HasBoundary(labelsByOffset, relocationsByOffset, offset + byteCount))
                {
                    byteCount++;
                }

                sb.Append("    .byte ");
                for (var i = 0; i < byteCount; i++)
                {
                    if (i != 0)
                        sb.Append(", ");
                    sb.Append("0x").Append(section.Data[offset + i].ToString("x2", CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
                offset += byteCount;
            }

            WriteLabels(sb, labelsByOffset, section.Data.Length);
        }
        private static void WriteZeroSection(StringBuilder sb, int size, Dictionary<int, List<string>>? labelsByOffset)
        {
            var offset = 0;
            while (offset < size)
            {
                WriteLabels(sb, labelsByOffset, offset);
                var next = FindNextLabelOffset(labelsByOffset, offset, size);
                var count = next - offset;
                sb.Append("    .zero ").AppendLine(count.ToString(CultureInfo.InvariantCulture));
                offset = next;
            }

            WriteLabels(sb, labelsByOffset, size);
        }

        private static void WriteDataRelocation(StringBuilder sb, RVObjectRelocation relocation, int width)
        {
            sb.Append(width == 8 ? "    .8byte " : "    .4byte ");
            sb.Append(relocation.SymbolName);
            if (relocation.Addend > 0)
                sb.Append(" + ").Append(relocation.Addend.ToString(CultureInfo.InvariantCulture));
            else if (relocation.Addend < 0)
                sb.Append(" - ").Append((-(long)relocation.Addend).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        private static int GetDataRelocationWidth(RVTarget target, RVObjectRelocationKind kind)
            => kind switch
            {
                RVObjectRelocationKind.AbsolutePointer => target.XLen / 8,
                RVObjectRelocationKind.Absolute32 => 4,
                RVObjectRelocationKind.Absolute64 => 8,
                _ => throw new NotSupportedException($"Unsupported RISC-V data relocation kind: {kind}"),
            };

        private static bool HasBoundary(
            Dictionary<int, List<string>>? labelsByOffset,
            Dictionary<int, RVObjectRelocation> relocationsByOffset,
            int offset)
            => (labelsByOffset is not null && labelsByOffset.ContainsKey(offset)) || relocationsByOffset.ContainsKey(offset);
        private static int FindNextLabelOffset(Dictionary<int, List<string>>? labelsByOffset, int offset, int limit)
        {
            if (labelsByOffset is null)
                return limit;

            var next = limit;
            foreach (var pair in labelsByOffset)
            {
                if (pair.Key > offset && pair.Key < next)
                    next = pair.Key;
            }
            return next;
        }

        private static void AddLabel(Dictionary<int, List<string>> labelsByOffset, int offset, string label)
        {
            if (string.IsNullOrEmpty(label))
                return;
            if (!labelsByOffset.TryGetValue(offset, out var labels))
            {
                labels = new List<string>();
                labelsByOffset.Add(offset, labels);
            }
            if (!labels.Contains(label))
                labels.Add(label);
        }

        private static void WriteLabels(StringBuilder sb, Dictionary<int, List<string>>? labelsByOffset, int offset)
        {
            if (labelsByOffset is null || !labelsByOffset.TryGetValue(offset, out var labels))
                return;

            labels.Sort(StringComparer.Ordinal);
            foreach (var label in labels)
                sb.Append(label).AppendLine(":");
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
                    return $".word 0x{unchecked((uint)instruction.Immediate).ToString("X8", CultureInfo.InvariantCulture).ToLowerInvariant()}";
                case RVInstructionFormat.Raw16:
                    return $".hword 0x{unchecked((ushort)instruction.Immediate).ToString("X4", CultureInfo.InvariantCulture).ToLowerInvariant()}";
                case RVInstructionFormat.R:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}, {Reg(instruction.Rs2, options)}";
                case RVInstructionFormat.I:
                    if (RVInstructionTable.IsLoad(instruction.Opcode) || instruction.Opcode == RVInstrKind.Jalr)
                        return $"{mnemonic} {Reg(instruction.Rd, options)}, {ImmOrSymbol(instruction)}(" + Reg(instruction.Rs1, options) + ")";
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}, {ImmOrSymbol(instruction)}";
                case RVInstructionFormat.ShiftI:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}, {instruction.Immediate}";
                case RVInstructionFormat.BitmanipUnary:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}";
                case RVInstructionFormat.BitmanipShiftI:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}, {instruction.Immediate}";
                case RVInstructionFormat.Compressed:
                    return WriteCompressed(mnemonic, instruction, options);
                case RVInstructionFormat.S:
                    return $"{mnemonic} {Reg(instruction.Rs2, options)}, {ImmOrSymbol(instruction)}({Reg(instruction.Rs1, options)})";
                case RVInstructionFormat.FloatLoad:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {ImmOrSymbol(instruction)}({Reg(instruction.Rs1, options)})";
                case RVInstructionFormat.FloatStore:
                    return $"{mnemonic} {Reg(instruction.Rs2, options)}, {ImmOrSymbol(instruction)}({Reg(instruction.Rs1, options)})";
                case RVInstructionFormat.FloatRRR:
                case RVInstructionFormat.FloatCompare:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}, " + Reg(instruction.Rs2, options);
                case RVInstructionFormat.FloatConvertFromInteger:
                case RVInstructionFormat.FloatConvertToInteger:
                case RVInstructionFormat.FloatConvert:
                case RVInstructionFormat.FloatMoveToInteger:
                case RVInstructionFormat.FloatMoveFromInteger:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {Reg(instruction.Rs1, options)}";
                case RVInstructionFormat.B:
                    return $"{mnemonic} {Reg(instruction.Rs1, options)}, {Reg(instruction.Rs2, options)}, {ImmOrSymbol(instruction)}";
                case RVInstructionFormat.U:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {ImmOrSymbol(instruction)}";
                case RVInstructionFormat.J:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {ImmOrSymbol(instruction)}";
                case RVInstructionFormat.Fence:
                    return WriteFence(instruction.Immediate);
                case RVInstructionFormat.System:
                    return mnemonic;
                case RVInstructionFormat.PrivilegedFence:
                    return WritePrivilegedFence(mnemonic, instruction, options);
                case RVInstructionFormat.HypervisorLoad:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, ({Reg(instruction.Rs1, options)})";
                case RVInstructionFormat.HypervisorStore:
                    return $"{mnemonic} {Reg(instruction.Rs2, options)}, ({Reg(instruction.Rs1, options)})";
                case RVInstructionFormat.Csr:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {RiscVCsrs.Format(instruction.Immediate)}, {Reg(instruction.Rs1, options)}";
                case RVInstructionFormat.CsrImmediate:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {RiscVCsrs.Format(instruction.Immediate)}, {(int)instruction.Rs1}";
                case RVInstructionFormat.Amo:
                    return WriteAtomic(mnemonic, instruction, options);
                case RVInstructionFormat.VectorConfig:
                    return WriteVectorConfig(mnemonic, instruction, options);
                case RVInstructionFormat.VectorLoad:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, ({Reg(instruction.Rs1, options)}){MaskSuffix(instruction)}";
                case RVInstructionFormat.VectorStore:
                    return $"{mnemonic} {Reg(instruction.Rs2, options)}, ({Reg(instruction.Rs1, options)}){MaskSuffix(instruction)}";
                case RVInstructionFormat.VectorOp:
                    return WriteVectorOp(mnemonic, metadata, instruction, options);
                default:
                    throw new NotSupportedException($"Unsupported instruction format: {metadata.Format}");
            }
        }

        private static string WriteCompressed(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            switch (instruction.Opcode)
            {
                case RVInstrKind.CNop:
                case RVInstrKind.CEbreak:
                    return mnemonic;
                case RVInstrKind.CAddi4Spn:
                    return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction);
                case RVInstrKind.CLw:
                case RVInstrKind.CLd:
                case RVInstrKind.CFlw:
                case RVInstrKind.CFld:
                case RVInstrKind.CLwSp:
                case RVInstrKind.CLdSp:
                    return $"{mnemonic} {Reg(instruction.Rd, options)}, {ImmOrSymbol(instruction)}({Reg(instruction.Rs1, options)})";
                case RVInstrKind.CSw:
                case RVInstrKind.CSd:
                case RVInstrKind.CFsw:
                case RVInstrKind.CFsd:
                case RVInstrKind.CSwSp:
                case RVInstrKind.CSdSp:
                    return $"{mnemonic} " + Reg(instruction.Rs2, options) + ", " + ImmOrSymbol(instruction) + "(" + Reg(instruction.Rs1, options) + ")";
                case RVInstrKind.CAddi:
                case RVInstrKind.CAddiw:
                case RVInstrKind.CLi:
                case RVInstrKind.CLui:
                case RVInstrKind.CSlli:
                case RVInstrKind.CSrli:
                case RVInstrKind.CSrai:
                case RVInstrKind.CAndi:
                    return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + ImmOrSymbol(instruction);
                case RVInstrKind.CAddi16Sp:
                    return $"{mnemonic} " + ImmOrSymbol(instruction);
                case RVInstrKind.CSub:
                case RVInstrKind.CXor:
                case RVInstrKind.COr:
                case RVInstrKind.CAnd:
                case RVInstrKind.CSubw:
                case RVInstrKind.CAddw:
                case RVInstrKind.CMv:
                case RVInstrKind.CAdd:
                    return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options);
                case RVInstrKind.CJ:
                case RVInstrKind.CJal:
                    return $"{mnemonic} " + ImmOrSymbol(instruction);
                case RVInstrKind.CBeqz:
                case RVInstrKind.CBnez:
                    return $"{mnemonic} " + Reg(instruction.Rs1, options) + ", " + ImmOrSymbol(instruction);
                case RVInstrKind.CJr:
                case RVInstrKind.CJalr:
                    return $"{mnemonic} " + Reg(instruction.Rs1, options);
                default:
                    throw new NotSupportedException($"Unsupported compressed opcode: {instruction.Opcode}");
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
                return $"{mnemonic} " + Reg(instruction.Rs1, options);
            return $"{mnemonic} " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
        }

        private static string WriteAtomic(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            mnemonic += AtomicSuffix(instruction);
            if (instruction.Opcode is RVInstrKind.LrW or RVInstrKind.LrD)
                return $"{mnemonic} " + Reg(instruction.Rd, options) + ", (" + Reg(instruction.Rs1, options) + ")";
            return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", (" + Reg(instruction.Rs1, options) + ")";
        }

        private static string WriteVectorConfig(string mnemonic, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            if (instruction.Opcode == RVInstrKind.Vsetvli)
                return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + FormatVType(instruction.Immediate, options);
            if (instruction.Opcode == RVInstrKind.Vsetivli)
                return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + ((int)instruction.Rs1) + ", " + FormatVType(instruction.Immediate, options);
            return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs1, options) + ", " + Reg(instruction.Rs2, options);
        }

        private static string WriteVectorOp(string mnemonic, RVInstructionMetadata metadata, RVInstruction instruction, RiscVAssemblyWriterOptions options)
        {
            if (metadata.Funct3 == 3)
                return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", " + instruction.Immediate + MaskSuffix(instruction);
            return $"{mnemonic} " + Reg(instruction.Rd, options) + ", " + Reg(instruction.Rs2, options) + ", " + Reg(instruction.Rs1, options) + MaskSuffix(instruction);
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
