using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Cnidaria.C
{
    internal sealed class InlineAsmFormattedOperand
    {
        private readonly Func<char?, string> _format;

        public string? Name { get; }

        public InlineAsmFormattedOperand(string? name, Func<char?, string> format)
        {
            Name = string.IsNullOrEmpty(name) ? null : name;
            _format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public string Format(char? modifier) => _format(modifier);
    }

    internal static class InlineAsmTemplateExpander
    {
        public static string Expand(
            string template,
            IReadOnlyList<InlineAsmFormattedOperand> operands,
            IReadOnlyDictionary<string, int> namedOperands,
            IReadOnlyList<string> labels,
            IReadOnlyDictionary<string, int> namedLabels,
            string uniqueSuffix)
        {
            var sb = new StringBuilder();
            template ??= string.Empty;
            for (var i = 0; i < template.Length; i++)
            {
                var ch = template[i];
                if (ch != '%')
                {
                    sb.Append(ch);
                    continue;
                }

                if (i + 1 >= template.Length)
                {
                    sb.Append('%');
                    continue;
                }

                var next = template[++i];
                if (next == '%')
                {
                    sb.Append('%');
                    continue;
                }

                if (next == '=')
                {
                    sb.Append(uniqueSuffix);
                    continue;
                }

                if (next == 'l')
                {
                    if (i + 1 < template.Length && template[i + 1] == '[')
                    {
                        i++;
                        if (TryReadBracketedName(template, ref i, out var labelName) && namedLabels.TryGetValue(labelName, out var labelByName))
                        {
                            sb.Append(labels[labelByName]);
                            continue;
                        }
                    }

                    if (TryReadNumber(template, ref i, firstDigitAlreadyRead: false, out var numericLabel))
                    {
                        var labelIndex = numericLabel >= operands.Count ? numericLabel - operands.Count : numericLabel;
                        sb.Append(labelIndex >= 0 && labelIndex < labels.Count ? labels[labelIndex] : string.Empty);
                        continue;
                    }

                    sb.Append("%l");
                    continue;
                }

                char? modifier = null;
                if (!char.IsDigit(next) && next != '[')
                {
                    modifier = next;
                    if (i + 1 >= template.Length)
                    {
                        sb.Append('%').Append(next);
                        continue;
                    }
                    next = template[++i];
                }

                if (next == '[')
                {
                    if (TryReadBracketedName(template, ref i, out var name) && namedOperands.TryGetValue(name, out var index))
                    {
                        sb.Append(operands[index].Format(modifier));
                        continue;
                    }

                    sb.Append("%[");
                    continue;
                }

                if (char.IsDigit(next))
                {
                    if (TryReadNumber(template, ref i, firstDigitAlreadyRead: true, out var index) && index >= 0 && index < operands.Count)
                    {
                        sb.Append(operands[index].Format(modifier));
                        continue;
                    }
                }

                sb.Append('%');
                if (modifier.HasValue)
                    sb.Append(modifier.Value);
                sb.Append(next);
            }

            return sb.ToString();
        }

        private static bool TryReadBracketedName(string text, ref int index, out string name)
        {
            var start = index + 1;
            var end = text.IndexOf(']', start);
            if (end < 0)
            {
                name = string.Empty;
                return false;
            }

            name = text.Substring(start, end - start);
            index = end;
            return true;
        }

        private static bool TryReadNumber(string text, ref int index, bool firstDigitAlreadyRead, out int value)
        {
            var start = firstDigitAlreadyRead ? index : index + 1;
            if (start >= text.Length || !char.IsDigit(text[start]))
            {
                value = 0;
                return false;
            }

            var end = start;
            while (end + 1 < text.Length && char.IsDigit(text[end + 1]))
                end++;

            value = int.Parse(text.Substring(start, end - start + 1), CultureInfo.InvariantCulture);
            index = end;
            return true;
        }
    }

    internal enum InlineAsmOperandStorage : byte
    {
        Register,
        Memory,
        Immediate,
    }

    internal static class InlineAsmConstraints
    {
        public static string StripPrefixes(string constraint)
        {
            if (string.IsNullOrEmpty(constraint))
                return string.Empty;

            var index = SkipModifiers(constraint, 0);
            if (index < constraint.Length && (constraint[index] == '=' || constraint[index] == '+'))
            {
                var marker = constraint[index++];
                index = SkipModifiers(constraint, index);
                return marker + constraint.Substring(index);
            }

            return constraint.Substring(index);
        }

        public static bool IsEarlyClobber(string constraint)
        {
            if (string.IsNullOrEmpty(constraint))
                return false;

            var index = 0;
            while (index < constraint.Length && IsModifier(constraint[index]))
            {
                if (constraint[index] == '&')
                    return true;
                index++;
            }

            if (index < constraint.Length && (constraint[index] == '=' || constraint[index] == '+'))
                index++;

            while (index < constraint.Length && IsModifier(constraint[index]))
            {
                if (constraint[index] == '&')
                    return true;
                index++;
            }

            return false;
        }

        public static string? MatchingOperand(string constraint)
        {
            var text = OperandConstraint(constraint).Trim();
            if (text.Length == 0)
                return null;

            var index = 0;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;
            if (index == text.Length && index != 0)
                return text;

            if (text.Length >= 3 && text[0] == '[' && text[text.Length - 1] == ']')
                return text.Substring(1, text.Length - 2).Trim();

            return null;
        }

        private static int SkipModifiers(string constraint, int index)
        {
            while (index < constraint.Length && IsModifier(constraint[index]))
                index++;
            return index;
        }

        private static bool IsModifier(char ch)
            => ch == '&' || ch == '%' || ch == '!';

        public static bool IsOutput(string constraint)
        {
            var stripped = StripPrefixes(constraint);
            return stripped.Length != 0 && (stripped[0] == '=' || stripped[0] == '+');
        }

        public static bool IsReadWrite(string constraint)
        {
            var stripped = StripPrefixes(constraint);
            return stripped.Length != 0 && stripped[0] == '+';
        }

        public static string OperandConstraint(string constraint)
        {
            var stripped = StripPrefixes(constraint);
            if (stripped.Length != 0 && (stripped[0] == '=' || stripped[0] == '+'))
                stripped = stripped.Substring(1);
            return stripped;
        }

        public static string? ExplicitRegisterName(string constraint)
        {
            var text = OperandConstraint(constraint);
            var start = text.IndexOf('{');
            if (start < 0)
                return null;
            var end = text.IndexOf('}', start + 1);
            if (end <= start + 1)
                return null;
            return text.Substring(start + 1, end - start - 1).Trim();
        }

        public static bool HasExplicitRegister(string constraint)
            => ExplicitRegisterName(constraint) is not null;

        public static bool AllowsMemory(string constraint)
            => OperandConstraint(constraint).IndexOf('m') >= 0;

        public static bool AllowsImmediate(string constraint)
        {
            var c = OperandConstraint(constraint);
            return c.IndexOf('i') >= 0 || c.IndexOf('n') >= 0 || c.IndexOf('I') >= 0 || c.IndexOf('J') >= 0 || c.IndexOf('K') >= 0 || c.IndexOf('L') >= 0 || c.IndexOf('M') >= 0 || c.IndexOf('N') >= 0 || c.IndexOf('O') >= 0 || c.IndexOf('P') >= 0;
        }

        public static bool AllowsFloatingRegister(string constraint)
        {
            var c = OperandConstraint(constraint);
            return c.IndexOf('f') >= 0 || c.IndexOf('x') >= 0 || c.IndexOf('Y') >= 0;
        }

        public static bool AllowsGeneralRegister(string constraint)
        {
            var c = OperandConstraint(constraint);
            if (string.IsNullOrEmpty(c))
                return true;
            foreach (var ch in c)
            {
                if (ch is 'r' or 'g' or 'q' or 'a' or 'b' or 'c' or 'd' or 'S' or 'D' or 'R' or 'l')
                    return true;
            }
            return false;
        }

        public static InlineAsmOperandStorage PreferredStorage(string constraint, QualifiedType type)
        {
            if (HasExplicitRegister(constraint))
                return InlineAsmOperandStorage.Register;
            if (AllowsMemory(constraint) && !AllowsGeneralRegister(constraint) && !AllowsFloatingRegister(constraint) && !AllowsImmediate(constraint))
                return InlineAsmOperandStorage.Memory;
            if (AllowsImmediate(constraint) && !AllowsGeneralRegister(constraint) && !AllowsFloatingRegister(constraint) && !AllowsMemory(constraint))
                return InlineAsmOperandStorage.Immediate;
            return InlineAsmOperandStorage.Register;
        }

        public static bool HasMemoryClobber(ImmutableArray<string> clobbers)
            => ContainsClobber(clobbers, "memory");

        public static bool HasCcClobber(ImmutableArray<string> clobbers)
            => ContainsClobber(clobbers, "cc");

        private static bool ContainsClobber(ImmutableArray<string> clobbers, string value)
        {
            if (clobbers.IsDefault)
                return false;

            for (var i = 0; i < clobbers.Length; i++)
            {
                if (string.Equals(clobbers[i], value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
