using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.X86
{
    public sealed class X86AssemblyWriterOptions
    {
        public static X86AssemblyWriterOptions Default { get; } = new X86AssemblyWriterOptions();

        public bool IncludeLabels { get; set; } = true;
        public bool UseHexImmediates { get; set; } = true;
        public bool IncludeMemorySize { get; set; } = true;
    }

    public static class X86Assembler
    {
        public static X86Program Assemble(string text, X86Target target)
            => X86AssemblyParser.Parse(text, target);

        public static X86Program Parse(string text, X86Target target)
            => Assemble(text, target);
    }

    public static class X86Disassembler
    {
        public static string Disassemble(X86Program obj, X86AssemblyWriterOptions? options = null)
            => X86AssemblyWriter.Write(obj, options);

        public static string Disassemble(X86TextSection text, X86AssemblyWriterOptions? options = null)
            => X86AssemblyWriter.Write(text, options);

        public static string Disassemble(IEnumerable<X86Instruction> instructions, X86AssemblyWriterOptions? options = null)
            => X86AssemblyWriter.Write(instructions, options);
    }

    internal static class X86AssemblyParser
    {
        public static X86Program Parse(string text, X86Target target)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            var instructions = ImmutableArray.CreateBuilder<X86Instruction>();
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            var position = 0;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = StripComment(lines[lineIndex]).Trim();
                if (line.Length == 0)
                    continue;

                while (true)
                {
                    var colon = FindLabelColon(line);
                    if (colon < 0)
                        break;

                    var label = line.Substring(0, colon).Trim();
                    ValidateLabel(label, lineIndex + 1);
                    if (labels.ContainsKey(label))
                        throw new FormatException($"Duplicate x86 label '{label}' on line {lineIndex + 1}");
                    labels.Add(label, position);
                    line = line.Substring(colon + 1).Trim();
                    if (line.Length == 0)
                        break;
                }

                if (line.Length == 0)
                    continue;

                foreach (var instruction in ParseInstruction(line, target, lineIndex + 1))
                {
                    instructions.Add(instruction);
                    position = checked(position + X86CodeEncoder.GetEncodedLength(instruction, target));
                }
            }

            return new X86Program(target, instructions.ToImmutable(), labels);
        }

        private static IEnumerable<X86Instruction> ParseInstruction(string line, X86Target target, int lineNumber)
        {
            var split = FirstWhitespace(line);
            var mnemonic = split < 0 ? line.Trim().ToLowerInvariant() : line.Substring(0, split).Trim().ToLowerInvariant();
            var operandText = split < 0 ? string.Empty : line.Substring(split + 1).Trim();
            var operands = SplitOperands(operandText);

            if (mnemonic is ".intel_syntax" or ".text" or "section")
                yield break;
            if (mnemonic is ".globl" or ".global" or "global" or "extern")
                yield break;

            if (mnemonic is ".byte" or "db")
            {
                foreach (var value in operands)
                    yield return X86Instruction.Raw(new[] { unchecked((byte)ParseInteger(value)) });
                yield break;
            }
            if (mnemonic is ".word" or "dw")
            {
                foreach (var value in operands)
                {
                    var raw = new byte[2];
                    WriteLittle(raw, 0, ParseInteger(value), 2);
                    yield return X86Instruction.Raw(raw);
                }
                yield break;
            }
            if (mnemonic is ".dword" or ".long" or "dd")
            {
                foreach (var value in operands)
                {
                    var raw = new byte[4];
                    WriteLittle(raw, 0, ParseInteger(value), 4);
                    yield return X86Instruction.Raw(raw);
                }
                yield break;
            }
            if (mnemonic is ".qword" or "dq")
            {
                foreach (var value in operands)
                {
                    var raw = new byte[8];
                    WriteLittle(raw, 0, ParseInteger(value), 8);
                    yield return X86Instruction.Raw(raw);
                }
                yield break;
            }

            if (mnemonic == "jz")
                mnemonic = "je";
            else if (mnemonic == "jnz")
                mnemonic = "jne";
            else if (mnemonic == "jc")
                mnemonic = "jb";
            else if (mnemonic == "jnc")
                mnemonic = "jae";

            if (mnemonic == "jmp")
            {
                ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                yield return X86Instruction.Branch(X86InstrKind.Jmp, ParseBranchOperand(operands[0], target));
                yield break;
            }

            if (X86Conditions.TryParseConditionalMnemonic(mnemonic, "j", out var branchCondition))
            {
                ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                yield return X86Instruction.ConditionalBranch(branchCondition, ParseBranchOperand(operands[0], target));
                yield break;
            }

            if (X86Conditions.TryParseConditionalMnemonic(mnemonic, "set", out var setCondition))
            {
                ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                yield return X86Instruction.Setcc(setCondition, ParseOperand(operands[0], target, 1));
                yield break;
            }

            if (X86Conditions.TryParseConditionalMnemonic(mnemonic, "cmov", out var cmovCondition))
            {
                ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                var dst = ParseOperand(operands[0], target);
                var src = ParseOperand(operands[1], target, dst.Size);
                yield return X86Instruction.Cmovcc(cmovCondition, dst, src);
                yield break;
            }

            var opcode = X86InstructionTable.GetOpcode(mnemonic);
            switch (opcode)
            {
                case X86InstrKind.Nop:
                case X86InstrKind.Ret:
                case X86InstrKind.Cdq:
                case X86InstrKind.Cqo:
                case X86InstrKind.Cbw:
                case X86InstrKind.Cwde:
                case X86InstrKind.Cdqe:
                case X86InstrKind.Leave:
                case X86InstrKind.Int3:
                case X86InstrKind.Ud2:
                case X86InstrKind.Syscall:
                case X86InstrKind.Vzeroupper:
                case X86InstrKind.Vzeroall:
                    ValidateOperandCount(mnemonic, operands, 0, lineNumber);
                    yield return new X86Instruction(opcode);
                    break;
                case X86InstrKind.Push:
                case X86InstrKind.Pop:
                case X86InstrKind.Inc:
                case X86InstrKind.Dec:
                case X86InstrKind.Neg:
                case X86InstrKind.Not:
                case X86InstrKind.Mul:
                case X86InstrKind.Div:
                case X86InstrKind.Idiv:
                    ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                    yield return X86Instruction.Unary(opcode, ParseOperand(operands[0], target));
                    break;
                case X86InstrKind.Call:
                    ValidateOperandCount(mnemonic, operands, 1, lineNumber);
                    yield return X86Instruction.Branch(opcode, ParseBranchOperand(operands[0], target));
                    break;
                case X86InstrKind.Imul:
                    if (operands.Length == 1)
                    {
                        yield return X86Instruction.Unary(opcode, ParseOperand(operands[0], target));
                    }
                    else if (operands.Length == 2)
                    {
                        var dst = ParseOperand(operands[0], target);
                        var src = ParseOperand(operands[1], target, dst.Size);
                        yield return X86Instruction.Binary(opcode, dst, src);
                    }
                    else if (operands.Length == 3)
                    {
                        var dst = ParseOperand(operands[0], target);
                        var src = ParseOperand(operands[1], target, dst.Size);
                        var imm = ParseOperand(operands[2], target, dst.Size);
                        yield return X86Instruction.Ternary(opcode, dst, src, imm);
                    }
                    else
                    {
                        throw new FormatException($"Invalid imul operand count on line {lineNumber}");
                    }
                    break;
                default:
                    if (X86InstructionTable.Get(opcode).Format == X86InstructionFormat.Ternary)
                    {
                        ValidateOperandCount(mnemonic, operands, 3, lineNumber);
                        var dst = ParseOperand(operands[0], target);
                        var src0 = ParseOperand(operands[1], target, dst.Size);
                        var src1DefaultSize = dst.Size != 0 ? dst.Size : src0.Size;
                        var src1 = ParseOperand(operands[2], target, src1DefaultSize);
                        if (dst.Size == 0 && src0.Size != 0)
                            dst = dst.WithSize(src0.Size);
                        if (src0.Size == 0 && dst.Size != 0)
                            src0 = src0.WithSize(dst.Size);
                        if (src1.Size == 0 && dst.Size != 0 && src1.Kind != X86OperandKind.Immediate && src1.Kind != X86OperandKind.Symbol)
                            src1 = src1.WithSize(dst.Size);
                        yield return X86Instruction.Ternary(opcode, dst, src0, src1);
                    }
                    else
                    {
                        ValidateOperandCount(mnemonic, operands, 2, lineNumber);
                        var dst = ParseOperand(operands[0], target);
                        var srcDefaultSize = dst.Kind is X86OperandKind.Register or X86OperandKind.Memory ? dst.Size : 0;
                        var src = ParseOperand(operands[1], target, srcDefaultSize);
                        if (dst.Size == 0 && src.Size != 0)
                            dst = dst.WithSize(src.Size);
                        if (src.Size == 0 && dst.Size != 0 && src.Kind != X86OperandKind.Immediate && src.Kind != X86OperandKind.Symbol)
                            src = src.WithSize(dst.Size);
                        yield return X86Instruction.Binary(opcode, dst, src);
                    }
                    break;
            }
        }

        private static X86Operand ParseBranchOperand(string text, X86Target target)
        {
            text = text.Trim();
            if (X86Registers.TryParse(text, out var register, out var size))
                return X86Operand.RegisterOperand(register, size == 0 ? target.XLen / 8 : size);
            if (text.StartsWith("[", StringComparison.Ordinal) || ContainsMemorySizePrefix(text))
                return ParseOperand(text, target, target.XLen / 8);
            if (TryParseInteger(text, out var immediate))
                return X86Operand.ImmediateOperand(immediate, 4);
            return X86Operand.SymbolOperand(text, 4, X86ObjectRelocationKind.Relative32);
        }

        private static X86Operand ParseOperand(string text, X86Target target, int defaultSize = 0)
        {
            text = text.Trim();
            if (text.Length == 0)
                throw new FormatException("Empty x86 operand");

            var explicitSize = ParseSizePrefix(ref text);
            var size = explicitSize != 0 ? explicitSize : defaultSize;

            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
                return ParseMemory(text.Substring(1, text.Length - 2), size);

            if (X86Registers.TryParse(text, out var register, out var registerSize))
                return X86Operand.RegisterOperand(register, registerSize);

            if (TryParseInteger(text, out var immediate))
                return X86Operand.ImmediateOperand(immediate, size);

            return X86Operand.SymbolOperand(text, size == 0 ? target.XLen / 8 : size, X86ObjectRelocationKind.AbsolutePointer);
        }

        private static X86Operand ParseMemory(string expression, int size)
        {
            expression = expression.Trim();
            if (expression.Length == 0)
                throw new FormatException("Empty x86 memory operand");

            var baseRegister = X86Register.Invalid;
            var indexRegister = X86Register.Invalid;
            var scale = 1;
            long displacement = 0;
            string? symbol = null;
            var ripRelative = false;

            foreach (var term in SplitMemoryTerms(expression))
            {
                var text = term.Text;
                var sign = term.Sign;
                if (text.Length == 0)
                    continue;

                var star = text.IndexOf('*');
                if (star >= 0)
                {
                    var regText = text.Substring(0, star).Trim();
                    var scaleText = text.Substring(star + 1).Trim();
                    var reg = X86Registers.Parse(regText, out _);
                    if (!X86Registers.IsGeneral(reg))
                        throw new FormatException("x86 memory index must be a general register");
                    if (sign < 0)
                        throw new FormatException("Negative x86 memory index is not encodable");
                    indexRegister = reg;
                    scale = checked((int)ParseInteger(scaleText));
                    continue;
                }

                if (X86Registers.TryParse(text, out var register, out _))
                {
                    if (register == X86Register.Rip)
                    {
                        ripRelative = true;
                        baseRegister = X86Register.Rip;
                        continue;
                    }
                    if (!X86Registers.IsGeneral(register))
                        throw new FormatException("x86 memory base must be a general register");
                    if (sign < 0)
                        throw new FormatException("Negative x86 memory base is not encodable");
                    if (baseRegister == X86Register.Invalid)
                        baseRegister = register;
                    else if (indexRegister == X86Register.Invalid)
                        indexRegister = register;
                    else
                        throw new FormatException("Too many x86 memory registers");
                    continue;
                }

                if (TryParseInteger(text, out var immediate))
                {
                    displacement = checked(displacement + sign * immediate);
                    continue;
                }

                if (sign < 0)
                    throw new FormatException("Negative x86 memory symbol is not supported");
                if (symbol is not null)
                    throw new FormatException("Too many x86 memory symbols");
                symbol = text;
            }

            var relocationKind = symbol is null
                ? X86ObjectRelocationKind.None
                : ripRelative ? X86ObjectRelocationKind.RipRelative32 : X86ObjectRelocationKind.Absolute32;
            return X86Operand.Memory(baseRegister, displacement, size, indexRegister, scale, symbol, relocationKind, 0, ripRelative);
        }

        private static int ParseSizePrefix(ref string text)
        {
            var original = text.TrimStart();
            var lower = original.ToLowerInvariant();
            var prefixes = new[]
            {
                new SizePrefix("byte ptr", 1),
                new SizePrefix("word ptr", 2),
                new SizePrefix("dword ptr", 4),
                new SizePrefix("qword ptr", 8),
                new SizePrefix("xmmword ptr", 16),
                new SizePrefix("ymmword ptr", 32),
                new SizePrefix("byte", 1),
                new SizePrefix("word", 2),
                new SizePrefix("dword", 4),
                new SizePrefix("qword", 8),
                new SizePrefix("xmmword", 16),
                new SizePrefix("ymmword", 32),
            };

            foreach (var prefix in prefixes)
            {
                if (lower.StartsWith(prefix.Text, StringComparison.Ordinal) && (lower.Length == prefix.Text.Length || char.IsWhiteSpace(lower[prefix.Text.Length]) || lower[prefix.Text.Length] == '['))
                {
                    text = original.Substring(prefix.Text.Length).TrimStart();
                    return prefix.Size;
                }
            }

            text = original;
            return 0;
        }

        private static bool ContainsMemorySizePrefix(string text)
        {
            var trimmed = text.TrimStart().ToLowerInvariant();
            return trimmed.StartsWith("byte", StringComparison.Ordinal) ||
                trimmed.StartsWith("word", StringComparison.Ordinal) ||
                trimmed.StartsWith("dword", StringComparison.Ordinal) ||
                trimmed.StartsWith("qword", StringComparison.Ordinal) ||
                trimmed.StartsWith("xmmword", StringComparison.Ordinal) ||
                trimmed.StartsWith("ymmword", StringComparison.Ordinal);
        }

        private static ImmutableArray<MemoryTerm> SplitMemoryTerms(string text)
        {
            var builder = ImmutableArray.CreateBuilder<MemoryTerm>();
            var start = 0;
            var sign = 1;
            for (var i = 0; i <= text.Length; i++)
            {
                var atEnd = i == text.Length;
                var c = atEnd ? '\0' : text[i];
                if (!atEnd && c != '+' && c != '-')
                    continue;

                var term = text.Substring(start, i - start).Trim();
                if (term.Length != 0)
                    builder.Add(new MemoryTerm(sign, term));
                if (!atEnd)
                {
                    sign = c == '-' ? -1 : 1;
                    start = i + 1;
                }
            }
            return builder.ToImmutable();
        }

        private static ImmutableArray<string> SplitOperands(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ImmutableArray<string>.Empty;

            var builder = ImmutableArray.CreateBuilder<string>();
            var start = 0;
            var bracketDepth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '[')
                    bracketDepth++;
                else if (c == ']')
                    bracketDepth--;
                else if (c == ',' && bracketDepth == 0)
                {
                    builder.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            builder.Add(text.Substring(start).Trim());
            return builder.ToImmutable();
        }

        private static string StripComment(string line)
        {
            var bracketDepth = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '[')
                    bracketDepth++;
                else if (c == ']')
                    bracketDepth--;
                else if ((c == ';' || c == '#') && bracketDepth == 0)
                    return line.Substring(0, i);
            }
            return line;
        }

        private static int FindLabelColon(string line)
        {
            var bracketDepth = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '[')
                    bracketDepth++;
                else if (c == ']')
                    bracketDepth--;
                else if (c == ':' && bracketDepth == 0)
                    return i;
                else if (char.IsWhiteSpace(c) && bracketDepth == 0)
                    return -1;
            }
            return -1;
        }

        private static int FirstWhitespace(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i;
            }
            return -1;
        }

        private static void ValidateLabel(string label, int lineNumber)
        {
            if (label.Length == 0)
                throw new FormatException($"Empty x86 label on line {lineNumber}");
            if (!IsLabelStart(label[0]))
                throw new FormatException($"Invalid x86 label '{label}' on line {lineNumber}");
            for (var i = 1; i < label.Length; i++)
            {
                if (!IsLabelPart(label[i]))
                    throw new FormatException($"Invalid x86 label '{label}' on line {lineNumber}");
            }
        }

        private static bool IsLabelStart(char c)
            => char.IsLetter(c) || c == '_' || c == '.';

        private static bool IsLabelPart(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$';

        private static void ValidateOperandCount(string mnemonic, ImmutableArray<string> operands, int expected, int lineNumber)
        {
            if (operands.Length != expected)
                throw new FormatException($"x86 instruction '{mnemonic}' expects {expected} operands on line {lineNumber}, got {operands.Length}");
        }

        private static long ParseInteger(string text)
        {
            if (TryParseInteger(text, out var value))
                return value;
            throw new FormatException("Invalid x86 integer literal: " + text);
        }

        private static bool TryParseInteger(string text, out long value)
        {
            text = text.Trim().Replace("_", string.Empty);
            if (text.StartsWith("+", StringComparison.Ordinal))
                text = text.Substring(1);
            var negative = false;
            if (text.StartsWith("-", StringComparison.Ordinal))
            {
                negative = true;
                text = text.Substring(1);
            }
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var ok = ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unsigned);
                value = negative ? checked(-(long)unsigned) : unchecked((long)unsigned);
                return ok;
            }
            if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase) && text.Length > 1)
            {
                var ok = ulong.TryParse(text.Substring(0, text.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unsigned);
                value = negative ? checked(-(long)unsigned) : unchecked((long)unsigned);
                return ok;
            }
            var parsed = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            if (negative)
                value = checked(-value);
            return parsed;
        }

        private static void WriteLittle(byte[] raw, int offset, long value, int size)
        {
            for (var i = 0; i < size; i++)
                raw[offset + i] = (byte)(value >> (i * 8));
        }

        private readonly struct SizePrefix
        {
            public string Text { get; }
            public int Size { get; }

            public SizePrefix(string text, int size)
            {
                Text = text;
                Size = size;
            }
        }

        private readonly struct MemoryTerm
        {
            public int Sign { get; }
            public string Text { get; }

            public MemoryTerm(int sign, string text)
            {
                Sign = sign;
                Text = text;
            }
        }
    }

    internal static class X86AssemblyWriter
    {
        public static string Write(X86Program obj, X86AssemblyWriterOptions? options = null)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));
            return Write(obj.Text, options, obj.Target);
        }

        public static string Write(X86TextSection text, X86AssemblyWriterOptions? options = null)
            => Write(text, options, X86Target.X64SysV);

        public static string Write(IEnumerable<X86Instruction> instructions, X86AssemblyWriterOptions? options = null)
            => Write(new X86TextSection(instructions), options);

        private static string Write(X86TextSection text, X86AssemblyWriterOptions? options, X86Target target)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            options ??= X86AssemblyWriterOptions.Default;
            var sb = new StringBuilder();
            var labelsByOffset = text.Labels.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).OrderBy(static s => s, StringComparer.Ordinal).ToImmutableArray());
            var position = 0;
            foreach (var instruction in text.Instructions)
            {
                if (options.IncludeLabels && labelsByOffset.TryGetValue(position, out var labels))
                {
                    foreach (var label in labels)
                        sb.Append(label).AppendLine(":");
                }
                sb.Append("    ").AppendLine(WriteInstruction(instruction, options));
                position = checked(position + X86CodeEncoder.GetEncodedLength(instruction, target));
            }
            if (options.IncludeLabels && labelsByOffset.TryGetValue(position, out var endLabels))
            {
                foreach (var label in endLabels)
                    sb.Append(label).AppendLine(":");
            }
            return sb.ToString();
        }

        public static string WriteInstruction(X86Instruction instruction, X86AssemblyWriterOptions? options = null)
        {
            options ??= X86AssemblyWriterOptions.Default;
            if (instruction.Opcode == X86InstrKind.Raw)
                return ".byte " + string.Join(", ", instruction.RawBytes.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture).ToLowerInvariant()));

            var mnemonic = instruction.Opcode switch
            {
                X86InstrKind.Jcc => "j" + X86Conditions.Format(instruction.Condition),
                X86InstrKind.Setcc => "set" + X86Conditions.Format(instruction.Condition),
                X86InstrKind.Cmovcc => "cmov" + X86Conditions.Format(instruction.Condition),
                _ => X86InstructionTable.GetMnemonic(instruction.Opcode),
            };

            var operands = new List<string>();
            if (instruction.Operand0.Kind != X86OperandKind.None)
                operands.Add(WriteOperand(instruction.Operand0, options));
            if (instruction.Operand1.Kind != X86OperandKind.None)
                operands.Add(WriteOperand(instruction.Operand1, options));
            if (instruction.Operand2.Kind != X86OperandKind.None)
                operands.Add(WriteOperand(instruction.Operand2, options));

            return operands.Count == 0 ? mnemonic : mnemonic + " " + string.Join(", ", operands);
        }

        private static string WriteOperand(X86Operand operand, X86AssemblyWriterOptions options)
        {
            return operand.Kind switch
            {
                X86OperandKind.Register => X86Registers.Format(operand.Register, operand.Size),
                X86OperandKind.Immediate => FormatInteger(operand.Immediate, options),
                X86OperandKind.Symbol => operand.Symbol ?? string.Empty,
                X86OperandKind.Memory => WriteMemory(operand, options),
                _ => string.Empty,
            };
        }

        private static string WriteMemory(X86Operand operand, X86AssemblyWriterOptions options)
        {
            var sb = new StringBuilder();
            if (options.IncludeMemorySize && operand.Size != 0)
                sb.Append(SizePrefix(operand.Size)).Append(" ptr ");
            sb.Append('[');

            var first = true;
            void Add(string part)
            {
                if (!first)
                    sb.Append(" + ");
                first = false;
                sb.Append(part);
            }

            if (operand.IsRipRelative || operand.BaseRegister == X86Register.Rip)
                Add("rip");
            else if (X86Registers.IsGeneral(operand.BaseRegister))
                Add(X86Registers.Format(operand.BaseRegister, 8));

            if (X86Registers.IsGeneral(operand.IndexRegister))
            {
                var text = X86Registers.Format(operand.IndexRegister, 8);
                if (operand.Scale != 1)
                    text += "*" + operand.Scale.ToString(CultureInfo.InvariantCulture);
                Add(text);
            }

            if (operand.HasSymbol)
                Add(operand.Symbol!);

            if (operand.Displacement != 0 || first)
            {
                if (operand.Displacement < 0 && !first)
                {
                    sb.Append(" - ");
                    sb.Append(FormatInteger(-operand.Displacement, options));
                }
                else
                {
                    if (!first)
                        sb.Append(" + ");
                    sb.Append(FormatInteger(operand.Displacement, options));
                }
                first = false;
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string SizePrefix(int size)
        {
            return size switch
            {
                1 => "byte",
                2 => "word",
                4 => "dword",
                8 => "qword",
                16 => "xmmword",
                32 => "ymmword",
                _ => size.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static string FormatInteger(long value, X86AssemblyWriterOptions options)
        {
            if (!options.UseHexImmediates || value < 0 && value > -10 || value >= 0 && value < 10)
                return value.ToString(CultureInfo.InvariantCulture);
            if (value < 0)
                return "-0x" + (-value).ToString("X", CultureInfo.InvariantCulture).ToLowerInvariant();
            return "0x" + value.ToString("X", CultureInfo.InvariantCulture).ToLowerInvariant();
        }
    }
}
