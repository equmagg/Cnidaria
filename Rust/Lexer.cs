using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Cnidaria.Rust;

public sealed class LexerOptions
{
    public RustEdition Edition { get; set; } = RustEdition.Rust2024;
    public bool IncludeTrivia { get; set; } = true;
    public int MaxDiagnostics { get; set; } = 512;
}

public enum RustEdition : ushort
{
    Rust2015 = 2015, Rust2018 = 2018, Rust2021 = 2021, Rust2024 = 2024
}


///<summary>Stores a diagnostic and its source span</summary>
public readonly struct SyntaxDiagnostic : IDiagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message => _message;
    public TextSpan Position { get; }
    private readonly string _message;
    public SyntaxDiagnostic(DiagnosticSeverity severity, string? message, TextSpan position)
    {
        Severity = severity;
        _message = message ?? string.Empty;
        Position = position;
    }

    public SyntaxDiagnostic(string? message, TextSpan position)
        : this(DiagnosticSeverity.Error, message, position)
    {
    }

    public string GetMessage(string source) => $"{_message} {Position.ToString(source)}";

    public static SyntaxDiagnostic Error(string message, TextSpan position)
        => new SyntaxDiagnostic(DiagnosticSeverity.Error, message, position);

    public static SyntaxDiagnostic Warning(string message, TextSpan position)
        => new SyntaxDiagnostic(DiagnosticSeverity.Warning, message, position);

    public static SyntaxDiagnostic MessageInfo(string message, TextSpan position)
        => new SyntaxDiagnostic(DiagnosticSeverity.Message, message, position);
}

public readonly struct SyntaxTrivia
{
    public SyntaxKind Kind { get; }
    public TextSpan Span { get; }

    public SyntaxTrivia(SyntaxKind kind, TextSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public string GetText(string source) => source.Substring(Span.Start, Span.Length);
}

/// <summary>Stores the token kind, source span, decoded value and trivia.</summary>
public readonly struct SyntaxToken
{
    public SyntaxKind Kind { get; }
    public SyntaxKind ContextualKind { get; }
    public TextSpan Span { get; }
    public string? ValueText { get; }
    public object? Value { get; }
    public int SuffixLength { get; }
    public SyntaxTrivia[] LeadingTrivia { get; }
    public SyntaxTrivia[] TrailingTrivia { get; }
    public bool IsMissing { get; }
    /// <summary>Hygiene mark of the macro expansion that produced the token, zero for source tokens.</summary>
    public int Context { get; }
    internal string? Text { get; }

    public TextSpan FullSpan
    {
        get
        {
            int start = LeadingTrivia is not { Length: > 0 } ? Span.Start : LeadingTrivia[0].Span.Start;
            int end = TrailingTrivia is not { Length: > 0 } ? Span.End : TrailingTrivia[TrailingTrivia.Length - 1].Span.End;
            return new TextSpan(start, end - start);
        }
    }

    public SyntaxToken(
        SyntaxKind kind,
        TextSpan span,
        string? valueText,
        object? value,
        int suffixLength,
        SyntaxKind contextualKind,
        SyntaxTrivia[] leadingTrivia,
        SyntaxTrivia[] trailingTrivia,
        bool isMissing = false,
        int context = 0,
        string? text = null)
    {
        Kind = kind;
        Span = span;
        ValueText = valueText;
        Value = value;
        SuffixLength = suffixLength;
        ContextualKind = contextualKind;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        IsMissing = isMissing;
        Context = context;
        Text = text;
    }

    // Expanded tokens carry their own text because their spans index the invocation, not the text they were written in
    public string GetText(string source) => Text ?? source.Substring(Span.Start, Span.Length);

    public ReadOnlySpan<char> GetSuffix(string source)
        => Text is not null ? Text.AsSpan(Text.Length - SuffixLength) : source.AsSpan(Span.End - SuffixLength, SuffixLength);

    internal SyntaxToken Relocate(TextSpan span, string text, int context)
        => new(Kind, span, ValueText, Value, SuffixLength, ContextualKind, [], [], IsMissing, context, text);
    internal SyntaxToken WithKind(SyntaxKind kind, TextSpan span, string? valueText, string? text, SyntaxTrivia[] leading, SyntaxTrivia[] trailing, object? value = null, int suffix = 0)
        => new(kind, span, valueText, value, suffix, SyntaxKind.None, leading, trailing, false, Context, Text is null ? null : text);

    public override string ToString() => $"{Kind} {Span}";
}

/// <summary>
/// Streaming Rust lexer.
/// </summary>
public sealed class Lexer
{
    private readonly string _text;
    private readonly LexerOptions _options;
    private readonly List<SyntaxDiagnostic> _diagnostics = new();
    private int _position;
    private SyntaxToken? _endOfFile;

    public Lexer(string text, LexerOptions? options = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _options = options is null ? new LexerOptions() : new LexerOptions
        {
            Edition = options.Edition,
            IncludeTrivia = options.IncludeTrivia,
            MaxDiagnostics = options.MaxDiagnostics
        };
        if (_options.Edition is not (RustEdition.Rust2015 or RustEdition.Rust2018 or RustEdition.Rust2021 or RustEdition.Rust2024))
            throw new ArgumentOutOfRangeException(nameof(options), "Unsupported Rust edition.");
        if (_options.MaxDiagnostics < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDiagnostics must be nonnegative.");
    }

    public string SourceText => _text;
    public int Position => _position;
    public IReadOnlyList<SyntaxDiagnostic> Diagnostics => _diagnostics;

    public SyntaxToken NextToken()
    {
        if (_endOfFile.HasValue)
            return _endOfFile.Value;

        SyntaxTrivia[] leading = ScanTrivia(stopAtFirstLineBreak: false);
        int start = _position;
        if (start == _text.Length)
        {
            var eof = new SyntaxToken(SyntaxKind.EndOfFileToken,
                new TextSpan(start, 0), null, null, 0, SyntaxKind.None,
                leading, Array.Empty<SyntaxTrivia>());
            _endOfFile = eof;
            return eof;
        }

        TokenData data = ScanCore();
        if (_position <= start)
            throw new InvalidOperationException("Lexer made no progress.");

        var span = new TextSpan(start, _position - start);
        SyntaxTrivia[] trailing = ScanTrivia(stopAtFirstLineBreak: true);
        return new SyntaxToken(data.Kind, span, data.ValueText, data.Value,
            data.SuffixLength, data.ContextualKind, leading, trailing);
    }

    private readonly struct TokenData
    {
        internal readonly SyntaxKind Kind;
        internal readonly string? ValueText;
        internal readonly object? Value;
        internal readonly int SuffixLength;
        internal readonly SyntaxKind ContextualKind;

        internal TokenData(SyntaxKind kind, string? valueText = null, object? value = null,
            int suffixLength = 0, SyntaxKind contextualKind = SyntaxKind.None)
        {
            Kind = kind;
            ValueText = valueText;
            Value = value;
            SuffixLength = suffixLength;
            ContextualKind = contextualKind;
        }
    }

    private char Peek(int offset = 0)
    {
        long index = (long)_position + offset;
        return index >= 0 && index < _text.Length ? _text[(int)index] : '\0';
    }

    private bool AtEnd => _position >= _text.Length;
    private bool Has(int offset = 0) => (long)_position + offset < _text.Length;
    private bool StartsWith(string text) => _text.AsSpan(_position).StartsWith(text.AsSpan(), StringComparison.Ordinal);
    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    private static bool IsHex(char c) => IsAsciiDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';
    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool IsWhitespace(char c)
        => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u0085' or '\u200E' or '\u200F' or '\u2028' or '\u2029';
    private static bool IsLineBreak(char c) => c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    // Identifier scanners operate on Unicode scalar values
    private int CodePointAt(int position, out int width)
    {
        if (position >= _text.Length)
        {
            width = 0;
            return -1;
        }
        char c = _text[position];
        if (char.IsHighSurrogate(c) && position + 1 < _text.Length && char.IsLowSurrogate(_text[position + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(c, _text[position + 1]);
        }
        width = 1;
        return char.IsSurrogate(c) ? -1 : c;
    }

    private bool XidStart(int position)
    {
        int cp = CodePointAt(position, out _);
        return cp == '_' || (cp >= 0 && UnicodeXid.IsStart(cp));
    }
    private bool XidContinue(int position)
    {
        int cp = CodePointAt(position, out _);
        return cp >= 0 && (cp == '_' || UnicodeXid.IsContinue(cp));
    }
    private void ConsumeIdentifier()
    {
        if (!XidStart(_position)) return;
        CodePointAt(_position, out int width);
        _position += width;
        while (!AtEnd && XidContinue(_position))
        {
            CodePointAt(_position, out width);
            _position += width;
        }
    }
    private string NormalizeIdentifier(int start, int length)
        => _text.Substring(start, length).Normalize(NormalizationForm.FormC);

    private void Error(int start, int length, string message)
    {
        if (_diagnostics.Count < _options.MaxDiagnostics)
            _diagnostics.Add(new SyntaxDiagnostic(DiagnosticSeverity.Error, message,
                new TextSpan(Math.Clamp(start, 0, _text.Length), Math.Clamp(length, 0, _text.Length - Math.Clamp(start, 0, _text.Length)))));
    }

    private SyntaxTrivia[] ScanTrivia(bool stopAtFirstLineBreak)
    {
        List<SyntaxTrivia>? result = null;
        while (TryScanTrivia(out var trivia, out bool containsLineBreak))
        {
            if (_options.IncludeTrivia)
            {
                result ??= new List<SyntaxTrivia>();
                result.Add(trivia);
            }
            if (stopAtFirstLineBreak && containsLineBreak)
                break;
        }
        return result is null ? Array.Empty<SyntaxTrivia>() : result.ToArray();
    }

    private bool TryScanTrivia(out SyntaxTrivia trivia, out bool lineBreak)
    {
        trivia = default;
        lineBreak = false;
        if (AtEnd) return false;
        int start = _position;
        SyntaxKind kind;

        if (start == 0 && Peek() == '\uFEFF')
        {
            _position++;
            kind = SyntaxKind.BomTrivia;
        }
        else if ((start == 0 || (start == 1 && _text[0] == '\uFEFF'))
                 && StartsWith("#!") && !LooksLikeInnerAttribute(_position + 2))
        {
            kind = SyntaxKind.ShebangTrivia;
            while (!AtEnd && Peek() != '\n') _position++;
            // The LF belongs to the shebang
            if (!AtEnd) _position++;
            lineBreak = true;
        }
        else if (StartsWith("//"))
        {
            kind = StartsWith("//!") ? SyntaxKind.InnerLineDocCommentTrivia
                 : StartsWith("///") && Peek(3) != '/' ? SyntaxKind.OuterLineDocCommentTrivia
                 : SyntaxKind.SingleLineCommentTrivia;
            _position += 2;
            while (!AtEnd && Peek() != '\n' && Peek() != '\r') _position++;
        }
        else if (StartsWith("/*"))
        {
            kind = StartsWith("/*!") ? SyntaxKind.InnerBlockDocCommentTrivia
                 : StartsWith("/**") && Peek(3) != '*' && Peek(3) != '/'
                     ? SyntaxKind.OuterBlockDocCommentTrivia : SyntaxKind.MultiLineCommentTrivia;
            _position += 2;
            int depth = 1;
            while (!AtEnd && depth > 0)
            {
                if (StartsWith("/*"))
                {
                    depth++;
                    _position += 2;
                }
                else if (StartsWith("*/"))
                {
                    depth--;
                    _position += 2;
                }
                else _position++;
            }
            if (depth != 0) Error(start, _position - start, "Unterminated block comment.");
            for (int i = start; i < _position; i++)
                if (IsLineBreak(_text[i]))
                {
                    lineBreak = true;
                    break;
                }
        }
        else if (IsWhitespace(Peek()))
        {
            if (IsLineBreak(Peek()))
            {
                kind = SyntaxKind.EndOfLineTrivia;
                char first = _text[_position++];
                if (first == '\r' && Peek() == '\n') _position++;
                lineBreak = true;
            }
            else
            {
                kind = SyntaxKind.WhitespaceTrivia;
                do
                {
                    _position++;
                }
                while (!AtEnd && IsWhitespace(Peek()) && !IsLineBreak(Peek()));
            }
        }
        else return false;
        trivia = new SyntaxTrivia(kind, new TextSpan(start, _position - start));
        return true;
    }

    // Shebang must not swallow #![...]
    private bool LooksLikeInnerAttribute(int p)
    {
        while (p < _text.Length)
        {
            if (IsWhitespace(_text[p]))
            {
                p++;
                continue;
            }
            if (p + 1 < _text.Length && _text[p] == '/' && _text[p + 1] == '/')
            {
                // Documentation comments arent ignored
                if (p + 2 < _text.Length && _text[p + 2] == '!') break;
                if (p + 3 < _text.Length && _text[p + 2] == '/' && _text[p + 3] != '/') break;
                p += 2;
                while (p < _text.Length && _text[p] != '\n') p++;
                continue;
            }
            if (p + 1 < _text.Length && _text[p] == '/' && _text[p + 1] == '*')
            {
                if (p + 2 < _text.Length && _text[p + 2] == '!') break;
                if (p + 3 < _text.Length && _text[p + 2] == '*' && _text[p + 3] != '*') break;
                p += 2;
                int depth = 1;
                while (p < _text.Length && depth > 0)
                {
                    if (p + 1 < _text.Length && _text[p] == '/' && _text[p + 1] == '*')
                    {
                        p += 2;
                        depth++;
                    }
                    else if (p + 1 < _text.Length && _text[p] == '*' && _text[p + 1] == '/')
                    {
                        p += 2;
                        depth--;
                    }
                    else p++;
                }
                if (depth != 0) return false;
                continue;
            }
            break;
        }
        return p < _text.Length && _text[p] == '[';
    }

    private TokenData ScanCore()
    {
        int start = _position;
        char c = Peek();
        // Prefixed literals have priority over identifiers/keywords
        if (c == 'b')
        {
            if (Peek(1) == '\'') return ScanQuoted(SyntaxKind.ByteLiteralToken, 1, true, false);
            if (Peek(1) == '"') return ScanQuoted(SyntaxKind.ByteStringLiteralToken, 1, true, false);
            if (Peek(1) == 'r' && (Peek(2) is '"' or '#'))
                return ScanRaw(SyntaxKind.RawByteStringLiteralToken, 2, true, false);
        }
        if (c == 'c' && _options.Edition >= RustEdition.Rust2021)
        {
            if (Peek(1) == '"') return ScanQuoted(SyntaxKind.CStringLiteralToken, 1, false, true);
            if (Peek(1) == 'r' && (Peek(2) is '"' or '#'))
                return ScanRaw(SyntaxKind.RawCStringLiteralToken, 2, false, true);
        }
        if (c == 'r')
        {
            if (Peek(1) == '"') return ScanRaw(SyntaxKind.RawStringLiteralToken, 1, false, false);
            if (Peek(1) == '#')
            {
                if (Peek(2) == '"' || Peek(2) == '#')
                    return ScanRaw(SyntaxKind.RawStringLiteralToken, 1, false, false);
                if (XidStart(_position + 2)) return ScanIdentifier(raw: true);
                _position += 2;
                Error(start, 2, "Expected an identifier or raw string after 'r#'.");
                return new TokenData(SyntaxKind.BadToken);
            }
        }
        if (c == '"') return ScanQuoted(SyntaxKind.StringLiteralToken, 0, false, false);
        if (c == '\'') return ScanApostrophe();
        if (IsAsciiDigit(c)) return ScanNumber();
        if (XidStart(_position)) return ScanIdentifier(raw: false);

        if (c == '#' && _options.Edition >= RustEdition.Rust2024)
        {
            int p = start;
            while (p < _text.Length && _text[p] == '#') p++;
            if (p < _text.Length && _text[p] == '"')
            {
                _position = p;
                var literal = ScanQuoted(SyntaxKind.StringLiteralToken, 0, false, false);
                Error(start, p - start, "Guarded string syntax is reserved in Rust 2024.");
                return new TokenData(SyntaxKind.ReservedGuardedStringToken, literal.ValueText, literal.Value, literal.SuffixLength);
            }
            if (p - start >= 2)
            {
                _position = p;
                Error(start, p - start, "Two or more adjacent '#' characters are reserved in Rust 2024.");
                return new TokenData(SyntaxKind.ReservedPoundsToken);
            }
        }

        // Maximal munch for the Rust punctuation grammar
        for (int length = Math.Min(3, _text.Length - start); length >= 1; length--)
            if (SyntaxFacts.TryGetPunctuation(_text.AsSpan(start, length), out var punct))
            {
                _position += length;
                return new TokenData(punct);
            }
        CodePointAt(_position, out int width);
        _position += width;
        Error(start, width, "Unexpected character in Rust source.");
        return new TokenData(SyntaxKind.BadToken);
    }

    private TokenData ScanIdentifier(bool raw)
    {
        int start = _position;
        if (raw) _position += 2; // 'r#'
        int nameStart = _position;
        ConsumeIdentifier();
        string normalized = NormalizeIdentifier(nameStart, _position - nameStart);
        if (raw)
        {
            if (SyntaxFacts.IsForbiddenRawIdentifier(normalized))
                Error(start, _position - start, "This special identifier cannot be written as a raw identifier.");
            return new TokenData(SyntaxKind.RawIdentifierToken, normalized);
        }
        SyntaxKind kind = SyntaxFacts.GetKeywordKind(normalized, _options.Edition);
        if (normalized == "_") kind = SyntaxKind.UnderscoreKeyword;
        if (_options.Edition >= RustEdition.Rust2021 &&
            (Peek() is '#' or '"' or '\''))
        {
            // A bare prefix is reserved, but the following token can still be lexed separately for error recovery
            Error(start, _position - start, "Reserved literal/lifetime prefix; insert whitespace or use a supported prefix.");
            return new TokenData(SyntaxKind.ReservedPrefixToken, normalized);
        }
        object? value = kind == SyntaxKind.TrueKeyword ? true : kind == SyntaxKind.FalseKeyword ? false : null;
        return new TokenData(kind, kind == SyntaxKind.IdentifierToken ? normalized : null, value,
            contextualKind: kind == SyntaxKind.IdentifierToken
                ? SyntaxFacts.GetContextualKeywordKind(normalized) : SyntaxKind.None);
    }

    private TokenData ScanApostrophe()
    {
        int start = _position;
        int nameStart = start + 1;
        bool raw = _options.Edition >= RustEdition.Rust2021 &&
                   nameStart + 1 < _text.Length && _text[nameStart] == 'r' && _text[nameStart + 1] == '#';
        if (raw) nameStart += 2;
        if (XidStart(nameStart))
        {
            int p = nameStart;
            CodePointAt(p, out int width);
            p += width;
            while (p < _text.Length && XidContinue(p))
            {
                CodePointAt(p, out width);
                p += width;
            }
            // A closing quote makes this a character literal
            if (p < _text.Length && _text[p] != '\'')
            {
                _position = p;
                string name = NormalizeIdentifier(nameStart, p - nameStart);
                if (raw && SyntaxFacts.IsForbiddenRawIdentifier(name))
                    Error(start, p - start, "This special name cannot be written as a raw lifetime.");
                if (_options.Edition >= RustEdition.Rust2021 && Peek() == '#' && !raw)
                {
                    Error(start, p - start, "Reserved lifetime prefix.");
                    return new TokenData(SyntaxKind.ReservedLifetimePrefixToken, name);
                }
                return new TokenData(raw ? SyntaxKind.RawLifetimeToken : SyntaxKind.LifetimeToken, name);
            }
            if (p == _text.Length)
            {
                _position = p;
                string name = NormalizeIdentifier(nameStart, p - nameStart);
                if (raw && SyntaxFacts.IsForbiddenRawIdentifier(name))
                    Error(start, p - start, "This special name cannot be written as a raw lifetime.");
                return new TokenData(raw ? SyntaxKind.RawLifetimeToken : SyntaxKind.LifetimeToken,
                    name);
            }
        }
        return ScanQuoted(SyntaxKind.CharacterLiteralToken, 0, false, false);
    }

    private TokenData ScanNumber()
    {
        int start = _position;
        int radix = 10;
        bool isFloat = false;
        if (Peek() == '0' && Peek(1) is 'x' or 'o' or 'b')
        {
            radix = Peek(1) == 'x' ? 16 : Peek(1) == 'o' ? 8 : 2;
            _position += 2;
            int digitCount = 0;
            while (!AtEnd)
            {
                char c = Peek();
                if (c == '_')
                {
                    _position++;
                    continue;
                }
                if (radix == 16 ? IsHex(c) : radix == 8 ? c is >= '0' and <= '7' : c is '0' or '1')
                {
                    _position++;
                    digitCount++;
                    continue;
                }
                // Base-invalid decimal digits stay with the number for targeted diagnostics
                if (IsAsciiDigit(c))
                {
                    int invalid = _position;
                    do
                    {
                        _position++;
                    }
                    while (IsAsciiDigit(Peek()) || Peek() == '_');
                    Error(invalid, _position - invalid, $"Invalid base-{radix} digit.");
                }
                break;
            }
            if (digitCount == 0) Error(start, _position - start, $"Expected digits after base-{radix} prefix.");
            if (radix != 16 && (Peek() is 'e' or 'E'))
                Error(_position, 1, "Binary and octal integer suffixes cannot begin with 'e' or 'E'.");
            if (Peek() == '.' && Peek(1) != '.' && Peek(1) != '_' && !XidStart(_position + 1))
                Error(start, _position - start + 1, "Non-decimal floating-point literals are not supported.");
        }
        else
        {
            ConsumeDecimalDigits();
            if (Peek() == '.' && Peek(1) != '.' && Peek(1) != '_' && !XidStart(_position + 1))
            {
                isFloat = true;
                _position++;
                if (IsAsciiDigit(Peek())) ConsumeDecimalDigits();
            }
            if (Peek() is 'e' or 'E')
            {
                isFloat = true;
                int exponent = _position++;
                if (Peek() is '+' or '-') _position++;
                while (Peek() == '_') _position++;
                if (!IsAsciiDigit(Peek()))
                    Error(exponent, _position - exponent, "Floating-point exponent needs a decimal digit.");
                else ConsumeDecimalDigits();
            }
        }
        int coreEnd = _position;
        int suffixLength = ConsumeSuffix();
        ReadOnlySpan<char> core = _text.AsSpan(start, coreEnd - start);
        ReadOnlySpan<char> suffix = _text.AsSpan(coreEnd, suffixLength);
        object? value = ComputeNumericValue(core, suffix, radix, isFloat);
        return new TokenData(isFloat ? SyntaxKind.FloatingLiteralToken : SyntaxKind.IntegerLiteralToken,
            value: value, suffixLength: suffixLength);
    }

    private static object? ComputeNumericValue(ReadOnlySpan<char> core, ReadOnlySpan<char> suffix,
        int radix, bool isFloat)
    {
        if (suffix.Length > 0 && suffix[0] == '_')
            suffix = suffix.Slice(1);

        if (isFloat || suffix.SequenceEqual("f32") || suffix.SequenceEqual("f64"))
        {
            if (radix != 10)
                return null;

            string? cleaned = core.IndexOf('_') >= 0 ? core.ToString().Replace("_", "") : null;
            ReadOnlySpan<char> digits = cleaned is null ? core : cleaned.AsSpan();
            if (suffix.SequenceEqual("f32"))
                return float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out float single)
                    ? single : null;

            return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number : null;
        }

        ReadOnlySpan<char> digitsSpan = radix == 10 ? core : core.Slice(2);
        UInt128 numberValue = 0;
        bool hasDigit = false;
        foreach (char digitChar in digitsSpan)
        {
            if (digitChar == '_')
                continue;

            int digit = IsAsciiDigit(digitChar) ? digitChar - '0'
                : IsHex(digitChar) ? HexValue(digitChar) : -1;
            if (digit < 0 || digit >= radix)
                return null;

            hasDigit = true;
            // Overflow belongs to literal expression validation
            if (numberValue > (UInt128.MaxValue - (uint)digit) / (uint)radix)
                return null;

            numberValue = numberValue * (uint)radix + (uint)digit;
        }

        if (!hasDigit)
            return null;

        return numberValue <= ulong.MaxValue ? (object)(ulong)numberValue : numberValue;
    }

    private void ConsumeDecimalDigits()
    {
        while (IsAsciiDigit(Peek()) || Peek() == '_')
            _position++;
    }

    private int ConsumeSuffix()
    {
        int start = _position;
        if (Peek() == '_' && !XidContinue(_position + 1))
            return 0;
        if (!XidStart(_position))
            return 0;

        ConsumeIdentifier();
        return _position - start;
    }

    private TokenData ScanQuoted(SyntaxKind kind, int prefixLength, bool byteMode, bool cMode)
    {
        int start = _position;
        _position += prefixLength;
        char delimiter = kind is SyntaxKind.CharacterLiteralToken or SyntaxKind.ByteLiteralToken ? '\'' : '"';
        _position++; // opening quote
        StringBuilder? decoded = byteMode || cMode ? null : new StringBuilder();
        List<byte>? bytes = byteMode || cMode ? new List<byte>() : null;
        int scalarCount = 0;
        bool closed = false;
        while (!AtEnd)
        {
            char c = Peek();
            if (c == delimiter)
            {
                _position++;
                closed = true;
                break;
            }
            if (delimiter == '\'' && c is '\r' or '\n') break;
            if (c == '\\')
            {
                int escStart = _position++;
                if (AtEnd)
                {
                    Error(escStart, 1, "Incomplete escape sequence.");
                    break;
                }
                if (delimiter == '"' && (Peek() == '\n' || (Peek() == '\r' && Peek(1) == '\n')))
                {
                    if (Peek() == '\r') _position++;
                    _position++; // LF
                    while (!AtEnd && Peek() is ' ' or '\t' or '\n' or '\r') _position++;
                    continue;
                }
                bool directByte = (byteMode || cMode) && Peek() == 'x';
                int scalar = ReadEscape(byteMode, cMode, escStart);
                if (scalar >= 0)
                {
                    if (directByte && bytes is not null)
                    {
                        bytes.Add((byte)scalar);
                        if (cMode && scalar == 0)
                            Error(escStart, _position - escStart, "C strings cannot contain NUL bytes.");
                    }
                    else AppendScalar(scalar, decoded, bytes, cMode, escStart);
                    scalarCount++;
                }
                continue;
            }
            int cp = CodePointAt(_position, out int width);
            int charStart = _position;
            if (cp == '\r' && Peek(1) == '\n')
            {
                cp = '\n';
                width = 2;
            }
            _position += width;
            if (cp < 0)
            {
                Error(charStart, width, "Invalid UTF-16 surrogate in literal.");
                continue;
            }
            if (cp == '\r') Error(charStart, width, "Bare CR is not permitted in Rust literals.");
            if (delimiter == '\'' && (cp == '\t' || cp == '\n'))
                Error(charStart, width, "Character literals cannot contain raw tabs or line breaks.");
            if (byteMode && cp > 0x7F)
                Error(charStart, width, "A byte literal may only contain unescaped ASCII characters.");
            AppendScalar(cp, decoded, bytes, cMode, charStart);
            scalarCount++;
        }
        if (!closed) Error(start, _position - start, "Unterminated quoted literal.");
        if (delimiter == '\'' && scalarCount != 1)
            Error(start, _position - start, "Character/byte literal must contain exactly one character.");
        int suffixLength = closed ? ConsumeSuffix() : 0;
        object? value = bytes is not null
            ? (delimiter == '\'' && byteMode ? (object)(bytes.Count != 0 ? bytes[0] : (byte)0)
                : cMode ? AppendNul(bytes) : bytes.ToArray())
            : decoded!.ToString();
        return new TokenData(kind, value: value, suffixLength: suffixLength);
    }

    private static byte[] AppendNul(List<byte> bytes)
    {
        bytes.Add(0);
        return bytes.ToArray();
    }

    private void AppendScalar(int scalar, StringBuilder? decoded, List<byte>? bytes, bool cMode, int start)
    {
        if (scalar == 0 && cMode) Error(start, 1, "C string literals cannot contain embedded NUL bytes.");
        if (scalar < 0 || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF)) return;
        if (decoded is not null)
        {
            if (scalar <= 0xFFFF)
                decoded.Append((char)scalar);
            else
                decoded.Append(char.ConvertFromUtf32(scalar));
        }

        if (bytes is not null)
        {
            Span<byte> buffer = stackalloc byte[4];
            int count = new Rune(scalar).EncodeToUtf8(buffer);
            for (int i = 0; i < count; i++)
                bytes.Add(buffer[i]);
        }
    }

    private int ReadEscape(bool byteMode, bool cMode, int start)
    {
        char esc = Peek();
        _position++;
        switch (esc)
        {
            case 'n': return '\n';
            case 'r': return '\r';
            case 't': return '\t';
            case '0': return 0;
            case '\\': return '\\';
            case '\'': return '\'';
            case '"': return '"';
            case 'x':
                {
                    int value = 0, digits = 0;
                    while (digits < 2 && IsHex(Peek()))
                    {
                        value = (value << 4) | HexValue(Peek());
                        _position++;
                        digits++;
                    }
                    if (digits != 2) Error(start, _position - start, "\\x escapes require exactly two hex digits.");
                    if (!byteMode && !cMode && value > 0x7F)
                        Error(start, _position - start, "Unicode \\x escapes cannot exceed 0x7F.");
                    return value;
                }
            case 'u':
                {
                    if (byteMode) Error(start, _position - start, "Unicode escapes are not allowed in byte literals.");
                    if (Peek() != '{')
                    {
                        Error(start, _position - start, "Expected '{' after \\u.");
                        return -1;
                    }
                    _position++;
                    int value = 0, digits = 0;
                    bool bad = false;
                    while (!AtEnd && Peek() != '}' && Peek() != '\'' && Peek() != '"' && !IsLineBreak(Peek()))
                    {
                        char c = Peek();
                        _position++;
                        if (c == '_')
                        {
                            if (digits == 0) bad = true;
                            continue;
                        }
                        if (!IsHex(c))
                        {
                            bad = true;
                            continue;
                        }
                        if (++digits > 6)
                        {
                            bad = true;
                            continue;
                        }
                        value = (value << 4) | HexValue(c);
                    }
                    if (Peek() == '}') _position++;
                    else bad = true;
                    if (digits == 0 || bad || value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF)
                    {
                        Error(start, _position - start, "Invalid Unicode scalar escape.");
                        return -1;
                    }
                    return value;
                }
            default:
                Error(start, _position - start, $"Unknown escape sequence '\\{esc}'.");
                return esc;
        }
    }

    private TokenData ScanRaw(SyntaxKind kind, int prefixLength, bool byteMode, bool cMode)
    {
        int start = _position;
        _position += prefixLength;
        int hashes = 0;
        while (Peek() == '#')
        {
            _position++;
            hashes++;
        }
        if (hashes > 255)
            Error(start, _position - start, "Raw string delimiters may contain at most 255 '#'.");
        if (Peek() != '"')
        {
            Error(start, _position - start, "Expected a double quote in raw string delimiter.");
            return new TokenData(SyntaxKind.BadToken);
        }
        _position++;
        int bodyStart = _position;
        int bodyEnd = _position;
        bool closed = false;
        while (!AtEnd)
        {
            if (Peek() == '"')
            {
                int i = 0;
                while (i < hashes && Has(i + 1) && Peek(i + 1) == '#') i++;
                if (i == hashes)
                {
                    bodyEnd = _position;
                    _position += hashes + 1;
                    closed = true;
                    break;
                }
            }
            int cp = CodePointAt(_position, out int width);
            if (cp < 0) Error(_position, width, "Invalid UTF-16 surrogate in raw string.");
            if (cp == '\r' && Peek(1) != '\n') Error(_position, width, "Bare CR is not permitted in raw strings.");
            if (cMode && cp == 0) Error(_position, width, "Raw C strings cannot contain NUL bytes.");
            if (byteMode && cp > 0x7F) Error(_position, width, "Raw byte strings must contain ASCII characters.");
            _position += width;
        }
        if (!closed)
        {
            bodyEnd = _position;
            Error(start, _position - start, "Unterminated raw string literal.");
        }
        int suffixLength = closed ? ConsumeSuffix() : 0;
        string body = _text.Substring(bodyStart, bodyEnd - bodyStart).Replace("\r\n", "\n");
        object value;
        if (byteMode || cMode)
        {
            int byteCount = Encoding.UTF8.GetByteCount(body);
            byte[] result = new byte[byteCount + (cMode ? 1 : 0)];
            Encoding.UTF8.GetBytes(body, result);
            value = result;
        }
        else
        {
            value = body.ToString();
        }
        return new TokenData(kind, value: value, suffixLength: suffixLength);
    }
}
