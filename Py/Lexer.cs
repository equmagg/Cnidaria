using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Cnidaria.Python
{

    public enum SyntaxKind : ushort
    {
        None = 0,

        EndOfFileToken,
        BadToken,
        IdentifierToken,
        NumberToken,
        StringToken,
        NewLineToken,
        IndentToken,
        DedentToken,

        FStringStartToken,
        FStringMiddleToken,
        FStringEndToken,
        TStringStartToken,
        TStringMiddleToken,
        TStringEndToken,

        LeftParenthesisToken,
        RightParenthesisToken,
        LeftBracketToken,
        RightBracketToken,
        LeftBraceToken,
        RightBraceToken,
        ColonToken,
        CommaToken,
        SemicolonToken,
        PlusToken,
        MinusToken,
        StarToken,
        SlashToken,
        PipeToken,
        AmpersandToken,
        LessToken,
        GreaterToken,
        EqualToken,
        DotToken,
        PercentToken,
        TildeToken,
        CaretToken,
        AtToken,
        ExclamationToken,

        EqualEqualToken,
        NotEqualToken,
        LessEqualToken,
        GreaterEqualToken,
        LeftShiftToken,
        RightShiftToken,
        DoubleStarToken,
        PlusEqualToken,
        MinusEqualToken,
        StarEqualToken,
        SlashEqualToken,
        PercentEqualToken,
        AmpersandEqualToken,
        PipeEqualToken,
        CaretEqualToken,
        LeftShiftEqualToken,
        RightShiftEqualToken,
        DoubleStarEqualToken,
        DoubleSlashToken,
        DoubleSlashEqualToken,
        AtEqualToken,
        ArrowToken,
        EllipsisToken,
        ColonEqualToken,

        FalseKeyword,
        NoneKeyword,
        TrueKeyword,
        AndKeyword,
        AsKeyword,
        AssertKeyword,
        AsyncKeyword,
        AwaitKeyword,
        BreakKeyword,
        ClassKeyword,
        ContinueKeyword,
        DefKeyword,
        DelKeyword,
        ElifKeyword,
        ElseKeyword,
        ExceptKeyword,
        FinallyKeyword,
        ForKeyword,
        FromKeyword,
        GlobalKeyword,
        IfKeyword,
        ImportKeyword,
        InKeyword,
        IsKeyword,
        LambdaKeyword,
        NonlocalKeyword,
        NotKeyword,
        OrKeyword,
        PassKeyword,
        RaiseKeyword,
        ReturnKeyword,
        TryKeyword,
        WhileKeyword,
        WithKeyword,
        YieldKeyword,

        // Nodes
        CompilationUnit,
        SyntaxList,
        SeparatedSyntaxList,
        SkippedTokens,
        ErrorStatement,

        SimpleStatementList,
        Suite,
        ExpressionStatement,
        AssignmentStatement,
        AnnotatedAssignmentStatement,
        AugmentedAssignmentStatement,
        ReturnStatement,
        YieldStatement,
        RaiseStatement,
        PassStatement,
        BreakStatement,
        ContinueStatement,
        AssertStatement,
        DeleteStatement,
        GlobalStatement,
        NonlocalStatement,
        ImportStatement,
        FromImportStatement,
        ImportAlias,
        DottedName,
        TypeAliasStatement,
        IfStatement,
        ElifClause,
        ElseClause,
        WhileStatement,
        ForStatement,
        ParameterList,
        Parameter,
        TypeParameterList,
        TypeParameter,
        DecoratorList,
        Decorator,
        FunctionDefinition,
        ClassDefinition,
        WithStatement,
        WithItem,
        TryStatement,
        ExceptClause,
        ExceptStarClause,
        ExceptionTypeList,
        FinallyClause,
        MatchStatement,
        CaseClause,

        NameExpression,
        LiteralExpression,
        StringConcatenationExpression,
        FStringExpression,
        TStringExpression,
        Interpolation,
        ConversionClause,
        FormatSpecClause,
        ParenthesizedExpression,
        TupleExpression,
        ListExpression,
        SetExpression,
        DictionaryExpression,
        KeyValuePair,
        StarredExpression,
        DoubleStarredExpression,
        NamedExpression,
        ConditionalExpression,
        LambdaExpression,
        YieldExpression,
        AwaitExpression,
        UnaryExpression,
        BinaryExpression,
        ComparisonExpression,
        AttributeExpression,
        CallExpression,
        ArgumentList,
        KeywordArgument,
        SubscriptExpression,
        SliceExpression,
        SliceList,
        GeneratorExpression,
        ListComprehensionExpression,
        SetComprehensionExpression,
        DictionaryComprehensionExpression,
        ComprehensionClauseList,
        ForComprehensionClause,
        IfComprehensionClause,

        OrPattern,
        AsPattern,
        CapturePattern,
        WildcardPattern,
        LiteralPattern,
        ValuePattern,
        GroupPattern,
        SequencePattern,
        StarPattern,
        MappingPattern,
        MappingPatternItem,
        DoubleStarPattern,
        ClassPattern,
        KeywordPattern,
        ErrorPattern,
        MissingPattern,

        ErrorExpression,
        MissingExpression,

        WhitespaceTrivia,
        CommentTrivia,
        EndOfLineTrivia,
        LineContinuationTrivia,
        SkippedTextTrivia,
    }

    internal enum LexerDiagnosticCode : byte
    {
        UnexpectedCharacter,
        NullCharacter,
        InconsistentIndentation,
        AmbiguousTabIndentation,
        UnmatchedClosingDelimiter,
        MismatchedClosingDelimiter,
        UnclosedDelimiter,
        InvalidNumericLiteral,
        UnterminatedStringLiteral,
        UnterminatedFormattedString,
        SingleRightBraceInFormattedString,
    }

    public readonly record struct TextSpan(int Start, int Length)
    {
        public int End => checked(Start + Length);

        public static TextSpan FromBounds(int start, int end)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end));

            return new TextSpan(start, end - start);
        }
    }


    internal static class SyntaxFacts
    {
        private static readonly FrozenDictionary<string, SyntaxKind> s_hardKeywords =
            new Dictionary<string, SyntaxKind>(StringComparer.Ordinal)
            {
                ["False"] = SyntaxKind.FalseKeyword,
                ["None"] = SyntaxKind.NoneKeyword,
                ["True"] = SyntaxKind.TrueKeyword,
                ["and"] = SyntaxKind.AndKeyword,
                ["as"] = SyntaxKind.AsKeyword,
                ["assert"] = SyntaxKind.AssertKeyword,
                ["async"] = SyntaxKind.AsyncKeyword,
                ["await"] = SyntaxKind.AwaitKeyword,
                ["break"] = SyntaxKind.BreakKeyword,
                ["class"] = SyntaxKind.ClassKeyword,
                ["continue"] = SyntaxKind.ContinueKeyword,
                ["def"] = SyntaxKind.DefKeyword,
                ["del"] = SyntaxKind.DelKeyword,
                ["elif"] = SyntaxKind.ElifKeyword,
                ["else"] = SyntaxKind.ElseKeyword,
                ["except"] = SyntaxKind.ExceptKeyword,
                ["finally"] = SyntaxKind.FinallyKeyword,
                ["for"] = SyntaxKind.ForKeyword,
                ["from"] = SyntaxKind.FromKeyword,
                ["global"] = SyntaxKind.GlobalKeyword,
                ["if"] = SyntaxKind.IfKeyword,
                ["import"] = SyntaxKind.ImportKeyword,
                ["in"] = SyntaxKind.InKeyword,
                ["is"] = SyntaxKind.IsKeyword,
                ["lambda"] = SyntaxKind.LambdaKeyword,
                ["nonlocal"] = SyntaxKind.NonlocalKeyword,
                ["not"] = SyntaxKind.NotKeyword,
                ["or"] = SyntaxKind.OrKeyword,
                ["pass"] = SyntaxKind.PassKeyword,
                ["raise"] = SyntaxKind.RaiseKeyword,
                ["return"] = SyntaxKind.ReturnKeyword,
                ["try"] = SyntaxKind.TryKeyword,
                ["while"] = SyntaxKind.WhileKeyword,
                ["with"] = SyntaxKind.WithKeyword,
                ["yield"] = SyntaxKind.YieldKeyword,
            }.ToFrozenDictionary(StringComparer.Ordinal);

        public static SyntaxKind GetIdentifierOrKeywordKind(ReadOnlySpan<char> text)
        {
            // match, case, type and _ intentionally remain IdentifierToken
            if (text.Length is < 2 or > 8 || !IsAscii(text))
                return SyntaxKind.IdentifierToken;

            return s_hardKeywords.TryGetValue(text.ToString(), out var kind)
                ? kind
                : SyntaxKind.IdentifierToken;
        }

        public static bool IsIdentifierStartAt(string source, int position, out int width)
        {
            if (!TryReadRune(source, position, out var rune, out width))
                return false;

            return rune.Value == '_' || IsNfkcClosed(rune, firstMustBeStart: true);
        }

        public static bool IsIdentifierContinueAt(string source, int position, out int width)
        {
            if (!TryReadRune(source, position, out var rune, out width))
                return false;

            return rune.Value == '_' || IsNfkcClosed(rune, firstMustBeStart: false);
        }

        private static bool TryReadRune(string source, int position, out Rune rune, out int width)
        {
            if ((uint)position >= (uint)source.Length)
            {
                rune = default;
                width = 0;
                return false;
            }

            return Rune.DecodeFromUtf16(source.AsSpan().Slice(position), out rune, out width) ==
                OperationStatus.Done;
        }

        private static bool IsNfkcClosed(Rune rune, bool firstMustBeStart)
        {
            var normalized = rune.ToString().Normalize(NormalizationForm.FormKC);
            var index = 0;
            var first = true;

            while (index < normalized.Length)
            {
                if (Rune.DecodeFromUtf16(normalized.AsSpan(index), out var current, out var consumed) !=
                    OperationStatus.Done)
                {
                    return false;
                }

                var category = Rune.GetUnicodeCategory(current);
                var valid = first && firstMustBeStart
                    ? IsBaseStart(category)
                    : IsBaseContinue(category);

                if (!valid)
                    return false;

                first = false;
                index += consumed;
            }

            return !first;
        }

        private static bool IsBaseStart(UnicodeCategory category) => category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber;

        private static bool IsBaseContinue(UnicodeCategory category) =>
            IsBaseStart(category) || category is
                UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.DecimalDigitNumber or
                UnicodeCategory.ConnectorPunctuation;

        private static bool IsAscii(ReadOnlySpan<char> text)
        {
            foreach (var ch in text)
            {
                if (ch > 0x7f)
                    return false;
            }

            return true;
        }
    }

    internal static class NumberValidator
    {
        public static bool IsValid(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
                return false;

            var imaginary = text[^1] is 'j' or 'J';
            var core = imaginary ? text[..^1] : text;
            if (core.IsEmpty)
                return false;

            if (imaginary)
                return IsFloat(core) || IsDecimalDigitPart(core);

            return IsInteger(core) || IsFloat(core);
        }

        private static bool IsInteger(ReadOnlySpan<char> text)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return IsBasedDigitPart(text[2..], IsHexDigit);

            if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                return IsBasedDigitPart(text[2..], static c => c is >= '0' and <= '7');

            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return IsBasedDigitPart(text[2..], static c => c is '0' or '1');

            if (!IsDecimalDigitPart(text))
                return false;

            if (text[0] != '0')
                return true;

            foreach (var ch in text[1..])
            {
                if (ch is not ('0' or '_'))
                    return false;
            }

            return true;
        }

        private static bool IsFloat(ReadOnlySpan<char> text)
        {
            var exponentIndex = FindSingleExponent(text);
            if (exponentIndex == int.MinValue)
                return false;

            var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
            var exponent = exponentIndex < 0 ? default : text[(exponentIndex + 1)..];

            if (exponentIndex >= 0)
            {
                if (exponent.IsEmpty)
                    return false;

                if (exponent[0] is '+' or '-')
                    exponent = exponent[1..];

                if (!IsDecimalDigitPart(exponent))
                    return false;
            }

            var dotIndex = mantissa.IndexOf('.');
            if (dotIndex < 0)
                return exponentIndex >= 0 && IsDecimalDigitPart(mantissa);

            if (mantissa[(dotIndex + 1)..].Contains('.'))
                return false;

            var before = mantissa[..dotIndex];
            var after = mantissa[(dotIndex + 1)..];

            if (!before.IsEmpty && !IsDecimalDigitPart(before))
                return false;

            if (!after.IsEmpty && !IsDecimalDigitPart(after))
                return false;

            return !before.IsEmpty || !after.IsEmpty;
        }

        private static bool IsBasedDigitPart(ReadOnlySpan<char> text, Func<char, bool> isDigit)
        {
            if (!text.IsEmpty && text[0] == '_')
                text = text[1..];

            return IsDigitPart(text, isDigit);
        }

        private static bool IsDecimalDigitPart(ReadOnlySpan<char> text) =>
            IsDigitPart(text, char.IsAsciiDigit);

        private static bool IsDigitPart(ReadOnlySpan<char> text, Func<char, bool> isDigit)
        {
            if (text.IsEmpty)
                return false;

            var previousWasUnderscore = false;
            var sawDigit = false;

            foreach (var ch in text)
            {
                if (ch == '_')
                {
                    if (!sawDigit || previousWasUnderscore)
                        return false;

                    previousWasUnderscore = true;
                    continue;
                }

                if (!isDigit(ch))
                    return false;

                previousWasUnderscore = false;
                sawDigit = true;
            }

            return sawDigit && !previousWasUnderscore;
        }

        private static int FindSingleExponent(ReadOnlySpan<char> text)
        {
            var result = -1;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('e' or 'E'))
                    continue;

                if (result >= 0)
                    return int.MinValue;

                result = i;
            }

            return result;
        }

        private static bool IsHexDigit(char ch) =>
            char.IsAsciiDigit(ch) || ch is >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    /// <remarks> Target Python 3.14.6 </remarks>
    internal sealed class Lexer
    {
        private readonly record struct PendingDiagnostic(
            LexerDiagnosticCode Code,
            TextSpan Span,
            string Message);

        private const int TabSize = 8;
        private const int AlternateTabSize = 1;

        private readonly string _source;
        private readonly Queue<GreenToken> _pendingTokens = new();
        private readonly List<GreenTrivia> _leadingTrivia = new();
        private readonly List<IndentLevel> _indentStack = [new(0, 0)];
        private readonly List<DelimiterFrame> _delimiterStack = new();
        private readonly List<FormattedStringFrame> _formattedStrings = new();

        private int _position;
        private bool _atBeginningOfLine = true;
        private bool _logicalLineHasCode;
        private bool _explicitContinuation;
        private bool _implicitFinalNewLineEmitted;
        private bool _eofEmitted;

        internal Lexer(string source) =>
            _source = source ?? throw new ArgumentNullException(nameof(source));

        internal GreenToken NextToken()
        {
            if (_pendingTokens.Count != 0)
                return _pendingTokens.Dequeue();

            if (_eofEmitted)
                return CreateToken(SyntaxKind.EndOfFileToken, _source.Length, 0);

            if (TryLexFormattedText(out var formattedToken))
                return FinishToken(formattedToken, allowTrailingTrivia: IsFormattedEnd(formattedToken.Kind));

            ScanLeadingTriviaAndIndentation();

            if (_pendingTokens.Count != 0)
                return _pendingTokens.Dequeue();

            if (TryRecoverFormattedExpressionAtEnd(out var recoveryToken))
                return recoveryToken;

            if (IsAtEnd)
                return LexEndOfFile();

            var leading = TakeLeadingTrivia();

            if (TryLexFormattedExpressionBoundary(leading, out formattedToken))
                return FinishToken(formattedToken, allowTrailingTrivia: false);

            var token = LexCoreToken(leading);
            var allowTrailing = token.Kind is not (
                SyntaxKind.NewLineToken or
                SyntaxKind.IndentToken or
                SyntaxKind.DedentToken or
                SyntaxKind.FStringStartToken or
                SyntaxKind.FStringMiddleToken or
                SyntaxKind.TStringStartToken or
                SyntaxKind.TStringMiddleToken);

            return FinishToken(token, allowTrailing);
        }

        private GreenToken FinishToken(GreenToken token, bool allowTrailingTrivia)
        {
            if (token.Kind is not (
                SyntaxKind.NewLineToken or
                SyntaxKind.IndentToken or
                SyntaxKind.DedentToken or
                SyntaxKind.EndOfFileToken))
            {
                _logicalLineHasCode = true;
            }

            return allowTrailingTrivia
                ? WithTrailingTrivia(token, ScanTrailingTrivia())
                : token;
        }

        private bool TryLexFormattedText(out GreenToken token)
        {
            token = null!;
            if (_formattedStrings.Count == 0)
                return false;

            var frame = _formattedStrings[^1];
            if (frame.Mode == FormattedStringMode.Expression)
                return false;

            token = LexFormattedText(frame);
            return true;
        }

        private GreenToken LexFormattedText(FormattedStringFrame frame)
        {
            var start = _position;

            while (true)
            {
                if (IsAtEnd)
                {
                    if (_position > start)
                        return CreateFormattedMiddle(frame, start, _position - start);

                    if (frame.Mode == FormattedStringMode.FormatSpec)
                        return RecoverMissingFormattedRightBrace(frame);

                    _formattedStrings.RemoveAt(_formattedStrings.Count - 1);
                    return CreateMissingFormattedEnd(frame, "unterminated formatted string literal");
                }

                if (IsFormattedClosingQuote(frame))
                {
                    if (_position > start)
                        return CreateFormattedMiddle(frame, start, _position - start);

                    if (frame.Mode == FormattedStringMode.FormatSpec)
                        return RecoverMissingFormattedRightBrace(frame);

                    var endStart = _position;
                    _position += frame.QuoteWidth;
                    _formattedStrings.RemoveAt(_formattedStrings.Count - 1);
                    return CreateToken(frame.EndKind, endStart, frame.QuoteWidth);
                }

                if (!frame.TripleQuoted && IsNewLineAt(_position, out _))
                {
                    if (_position > start)
                        return CreateFormattedMiddle(frame, start, _position - start);

                    if (frame.Mode == FormattedStringMode.FormatSpec)
                        return RecoverMissingFormattedRightBrace(frame);

                    _formattedStrings.RemoveAt(_formattedStrings.Count - 1);
                    return CreateMissingFormattedEnd(frame, "unterminated formatted string literal");
                }

                if (Current == '{')
                {
                    if (Peek(1) == '{')
                    {
                        _position += 2;
                        continue;
                    }

                    if (_position > start)
                        return CreateFormattedMiddle(frame, start, _position - start);

                    var bracePosition = _position++;
                    var baseline = _delimiterStack.Count;
                    _delimiterStack.Add(new DelimiterFrame(SyntaxKind.LeftBraceToken, bracePosition));
                    frame.Fields.Add(new ReplacementField(baseline, frame.Mode));
                    frame.Mode = FormattedStringMode.Expression;
                    return CreateToken(SyntaxKind.LeftBraceToken, bracePosition, 1);
                }

                if (Current == '}')
                {
                    if (frame.Mode == FormattedStringMode.Text)
                    {
                        if (Peek(1) == '}')
                        {
                            _position += 2;
                            continue;
                        }

                        if (_position > start)
                            return CreateFormattedMiddle(frame, start, _position - start);

                        var badPosition = _position++;
                        return CreateFormattedMiddle(
                            frame,
                            badPosition,
                            1,
                            [Diagnostic(
                            LexerDiagnosticCode.SingleRightBraceInFormattedString,
                            badPosition,
                            1,
                            "single '}' is not allowed in a formatted string")]);
                    }

                    if (_position > start)
                        return CreateFormattedMiddle(frame, start, _position - start);

                    return CloseFormattedReplacementField(frame);
                }

                if (Current == '\\')
                {
                    _position++;
                    if (!IsAtEnd)
                    {
                        if (IsNewLineAt(_position, out var newLineWidth))
                            _position += newLineWidth;
                        else
                            _position++;
                    }

                    continue;
                }

                _position++;
            }
        }

        private bool TryLexFormattedExpressionBoundary(
            ImmutableArray<GreenTrivia> leading,
            out GreenToken token)
        {
            token = null!;
            if (_formattedStrings.Count == 0)
                return false;

            var frame = _formattedStrings[^1];
            if (frame.Mode != FormattedStringMode.Expression || frame.Fields.Count == 0)
                return false;

            var field = frame.Fields[^1];
            var atFieldRoot = _delimiterStack.Count == field.DelimiterBaseline + 1;
            if (!atFieldRoot)
                return false;

            if (Current == '}')
            {
                token = CloseFormattedReplacementField(frame, leading);
                return true;
            }

            if (Current == ':' && Peek(1) != '=')
            {
                var start = _position++;
                frame.Mode = FormattedStringMode.FormatSpec;
                token = CreateToken(SyntaxKind.ColonToken, start, 1, leading);
                return true;
            }

            return false;
        }

        private bool TryRecoverFormattedExpressionAtEnd(out GreenToken token)
        {
            token = null!;
            if (!IsAtEnd || _formattedStrings.Count == 0)
                return false;

            var frame = _formattedStrings[^1];
            if (frame.Mode != FormattedStringMode.Expression || frame.Fields.Count == 0)
                return false;

            token = RecoverMissingFormattedRightBrace(frame, TakeLeadingTrivia());
            return true;
        }

        private GreenToken CloseFormattedReplacementField(
            FormattedStringFrame frame,
            ImmutableArray<GreenTrivia> leading = default)
        {
            var start = _position++;
            PopCurrentReplacementDelimiter(frame);
            var field = frame.Fields[^1];
            frame.Fields.RemoveAt(frame.Fields.Count - 1);
            frame.Mode = field.ReturnMode;
            return CreateToken(SyntaxKind.RightBraceToken, start, 1, leading);
        }

        private GreenToken RecoverMissingFormattedRightBrace(
            FormattedStringFrame frame,
            ImmutableArray<GreenTrivia> leading = default)
        {
            var field = frame.Fields[^1];
            PopCurrentReplacementDelimiter(frame);
            frame.Fields.RemoveAt(frame.Fields.Count - 1);
            frame.Mode = field.ReturnMode;

            return CreateToken(
                SyntaxKind.RightBraceToken,
                _position,
                0,
                leading,
                diagnostics: [Diagnostic(
                LexerDiagnosticCode.UnterminatedFormattedString,
                _position,
                0,
                "formatted string replacement field is missing '}'")]);
        }

        private void PopCurrentReplacementDelimiter(FormattedStringFrame frame)
        {
            var field = frame.Fields[^1];
            while (_delimiterStack.Count > field.DelimiterBaseline)
                _delimiterStack.RemoveAt(_delimiterStack.Count - 1);
        }

        private GreenToken CreateMissingFormattedEnd(FormattedStringFrame frame, string message) =>
            CreateToken(
                frame.EndKind,
                _position,
                0,
                diagnostics: [Diagnostic(
                LexerDiagnosticCode.UnterminatedFormattedString,
                _position,
                0,
                message)]);

        private GreenToken CreateFormattedMiddle(
            FormattedStringFrame frame,
            int start,
            int length,
            ImmutableArray<PendingDiagnostic> diagnostics = default) =>
            CreateToken(frame.MiddleKind, start, length, diagnostics: diagnostics);

        private bool IsFormattedClosingQuote(FormattedStringFrame frame)
        {
            if (Current != frame.Quote)
                return false;

            if (!frame.TripleQuoted)
                return true;

            return Peek(1) == frame.Quote && Peek(2) == frame.Quote;
        }

        private static bool IsFormattedEnd(SyntaxKind kind) =>
            kind is SyntaxKind.FStringEndToken or SyntaxKind.TStringEndToken;

        private void ScanLeadingTriviaAndIndentation()
        {
            while (!IsAtEnd)
            {
                if (_atBeginningOfLine)
                {
                    ScanBeginningOfLine();
                    if (_pendingTokens.Count != 0 || IsAtEnd)
                        return;
                }

                if (IsHorizontalWhitespace(Current))
                {
                    ScanWhitespaceTrivia(_leadingTrivia);
                    continue;
                }

                if (Current == '#')
                {
                    ScanCommentTrivia(_leadingTrivia);
                    continue;
                }

                if (Current == '\\' && IsNewLineAt(_position + 1, out var continuationWidth))
                {
                    var start = _position;
                    _position += 1 + continuationWidth;
                    _leadingTrivia.Add(CreateTrivia(
                        SyntaxKind.LineContinuationTrivia,
                        start,
                        _position - start));
                    _atBeginningOfLine = true;
                    _explicitContinuation = true;
                    continue;
                }

                if (IsNewLineAt(_position, out var newLineWidth))
                {
                    if (_delimiterStack.Count == 0 && _logicalLineHasCode && !_explicitContinuation)
                        return;

                    _leadingTrivia.Add(CreateTrivia(SyntaxKind.EndOfLineTrivia, _position, newLineWidth));
                    _position += newLineWidth;
                    _atBeginningOfLine = true;

                    if (_delimiterStack.Count == 0)
                    {
                        _logicalLineHasCode = false;
                        _explicitContinuation = false;
                    }

                    continue;
                }

                return;
            }
        }

        private void ScanBeginningOfLine()
        {
            var indentationStart = _position;
            var column = 0;
            var alternateColumn = 0;

            while (!IsAtEnd)
            {
                switch (Current)
                {
                    case ' ':
                        column++;
                        alternateColumn++;
                        _position++;
                        continue;

                    case '\t':
                        column = (column / TabSize + 1) * TabSize;
                        alternateColumn =
                            (alternateColumn / AlternateTabSize + 1) * AlternateTabSize;
                        _position++;
                        continue;

                    case '\f':
                        column = 0;
                        alternateColumn = 0;
                        _position++;
                        continue;
                }

                break;
            }

            if (_position > indentationStart)
            {
                _leadingTrivia.Add(CreateTrivia(
                    SyntaxKind.WhitespaceTrivia,
                    indentationStart,
                    _position - indentationStart));
            }

            if (IsAtEnd || Current == '#' || IsNewLineAt(_position, out _))
            {
                _atBeginningOfLine = false;
                return;
            }

            var suppressIndentation = _delimiterStack.Count != 0 || _explicitContinuation;
            _atBeginningOfLine = false;

            if (suppressIndentation)
            {
                _explicitContinuation = false;
                return;
            }

            var top = _indentStack[^1];
            if (column == top.Column)
            {
                if (alternateColumn != top.AlternateColumn)
                {
                    AddPendingStructuralToken(
                        SyntaxKind.BadToken,
                        LexerDiagnosticCode.AmbiguousTabIndentation,
                        "inconsistent use of tabs and spaces in indentation");
                }

                return;
            }

            if (column > top.Column)
            {
                if (alternateColumn <= top.AlternateColumn)
                {
                    AddPendingStructuralToken(
                        SyntaxKind.BadToken,
                        LexerDiagnosticCode.AmbiguousTabIndentation,
                        "inconsistent use of tabs and spaces in indentation");
                    return;
                }

                _indentStack.Add(new IndentLevel(column, alternateColumn));
                AddPendingStructuralToken(SyntaxKind.IndentToken);
                return;
            }

            while (_indentStack.Count > 1 && column < _indentStack[^1].Column)
            {
                _indentStack.RemoveAt(_indentStack.Count - 1);
                AddPendingStructuralToken(SyntaxKind.DedentToken);
            }

            top = _indentStack[^1];
            if (column != top.Column)
            {
                AddPendingStructuralToken(
                    SyntaxKind.BadToken,
                    LexerDiagnosticCode.InconsistentIndentation,
                    "unindent does not match any outer indentation level");
                return;
            }

            if (alternateColumn != top.AlternateColumn)
            {
                AddPendingStructuralToken(
                    SyntaxKind.BadToken,
                    LexerDiagnosticCode.AmbiguousTabIndentation,
                    "inconsistent use of tabs and spaces in indentation");
            }
        }

        private GreenToken LexCoreToken(ImmutableArray<GreenTrivia> leading)
        {
            var start = _position;

            if (IsNewLineAt(_position, out var newLineWidth))
            {
                _position += newLineWidth;
                _atBeginningOfLine = true;
                _logicalLineHasCode = false;
                _explicitContinuation = false;
                return CreateToken(SyntaxKind.NewLineToken, start, newLineWidth, leading);
            }

            if (Current == '\0')
            {
                _position++;
                return CreateToken(
                    SyntaxKind.BadToken,
                    start,
                    1,
                    leading,
                    diagnostics: [Diagnostic(
                    LexerDiagnosticCode.NullCharacter,
                    start,
                    1,
                    "source code cannot contain null bytes")]);
            }

            if (TryLexString(start, leading, out var stringToken))
                return stringToken;

            if (char.IsAsciiDigit(Current) ||
                (Current == '.' && char.IsAsciiDigit(Peek(1))))
            {
                return LexNumber(start, leading);
            }

            if (SyntaxFacts.IsIdentifierStartAt(_source, _position, out var width))
            {
                _position += width;
                while (SyntaxFacts.IsIdentifierContinueAt(_source, _position, out width))
                    _position += width;

                var span = TextSpan.FromBounds(start, _position);
                var kind = SyntaxFacts.GetIdentifierOrKeywordKind(_source.AsSpan(span.Start, span.Length));
                return CreateToken(kind, span.Start, span.Length, leading);
            }

            return LexOperatorOrBadToken(start, leading);
        }

        private GreenToken LexNumber(int start, ImmutableArray<GreenTrivia> leading)
        {
            if (Current == '0' && char.ToLowerInvariant(Peek(1)) is 'x' or 'o' or 'b')
            {
                _position += 2;
                while (char.IsAsciiLetterOrDigit(Current) || Current == '_')
                    _position++;
            }
            else
            {
                if (Current == '.')
                    _position++;

                ScanDecimalDigitsAndUnderscores();

                if (Current == '.' && Peek(1) != '.')
                {
                    _position++;
                    ScanDecimalDigitsAndUnderscores();
                }

                if (Current is 'e' or 'E')
                {
                    _position++;
                    if (Current is '+' or '-')
                        _position++;
                    ScanDecimalDigitsAndUnderscores();
                }

                if (Current is 'j' or 'J')
                    _position++;

                while (char.IsAsciiLetterOrDigit(Current) || Current == '_')
                    _position++;
            }

            var span = TextSpan.FromBounds(start, _position);
            var diagnostics = NumberValidator.IsValid(_source.AsSpan(span.Start, span.Length))
                ? ImmutableArray<PendingDiagnostic>.Empty
                : [Diagnostic(
                LexerDiagnosticCode.InvalidNumericLiteral,
                span.Start,
                span.Length,
                "invalid numeric literal")];

            return CreateToken(
                SyntaxKind.NumberToken,
                span.Start,
                span.Length,
                leading,
                diagnostics: diagnostics);
        }

        private void ScanDecimalDigitsAndUnderscores()
        {
            while (char.IsAsciiDigit(Current) || Current == '_')
                _position++;
        }

        private bool TryLexString(
            int start,
            ImmutableArray<GreenTrivia> leading,
            out GreenToken token)
        {
            token = null!;

            if (!TryReadStringPrefix(start, out var prefixLength, out var flags))
                return false;

            var quotePosition = start + prefixLength;
            var quote = _source[quotePosition];
            var triple =
                PeekAbsolute(quotePosition + 1) == quote &&
                PeekAbsolute(quotePosition + 2) == quote;
            var quoteWidth = triple ? 3 : 1;
            _position = quotePosition + quoteWidth;

            if ((flags & StringFlags.Formatted) != 0)
            {
                var template = (flags & StringFlags.Template) != 0;
                var frame = new FormattedStringFrame(
                    quote,
                    quoteWidth,
                    triple,
                    raw: (flags & StringFlags.Raw) != 0,
                    template);
                _formattedStrings.Add(frame);

                token = CreateToken(frame.StartKind, start, _position - start, leading);
                return true;
            }

            var terminated = false;
            while (!IsAtEnd)
            {
                if (Current == quote)
                {
                    if (!triple)
                    {
                        _position++;
                        terminated = true;
                        break;
                    }

                    if (Peek(1) == quote && Peek(2) == quote)
                    {
                        _position += 3;
                        terminated = true;
                        break;
                    }
                }

                if (Current == '\\')
                {
                    _position++;
                    if (!IsAtEnd)
                    {
                        if (IsNewLineAt(_position, out var escapedNewLineWidth))
                            _position += escapedNewLineWidth;
                        else
                            _position++;
                    }
                    continue;
                }

                if (!triple && IsNewLineAt(_position, out _))
                    break;

                _position++;
            }

            var span = TextSpan.FromBounds(start, _position);
            var diagnostics = terminated
                ? ImmutableArray<PendingDiagnostic>.Empty
                : [Diagnostic(
                LexerDiagnosticCode.UnterminatedStringLiteral,
                span.Start,
                span.Length,
                "unterminated string literal")];

            token = CreateToken(
                SyntaxKind.StringToken,
                span.Start,
                span.Length,
                leading,
                diagnostics: diagnostics);
            return true;
        }

        private bool TryReadStringPrefix(int start, out int prefixLength, out StringFlags flags)
        {
            prefixLength = 0;
            flags = StringFlags.None;

            if (PeekAbsolute(start) is '\'' or '"')
                return true;

            var first = char.ToLowerInvariant(PeekAbsolute(start));
            var second = char.ToLowerInvariant(PeekAbsolute(start + 1));

            if (IsSingleStringPrefix(first) &&
                (PeekAbsolute(start + 1) is '\'' or '"'))
            {
                prefixLength = 1;
                flags = FlagsForPrefix(first);
                return true;
            }

            if (IsValidDoublePrefix(first, second) &&
                (PeekAbsolute(start + 2) is '\'' or '"'))
            {
                prefixLength = 2;
                flags = FlagsForPrefix(first) | FlagsForPrefix(second);
                return true;
            }

            return false;
        }

        private GreenToken LexOperatorOrBadToken(
            int start,
            ImmutableArray<GreenTrivia> leading)
        {
            var (kind, width) = Current switch
            {
                '(' => (SyntaxKind.LeftParenthesisToken, 1),
                ')' => (SyntaxKind.RightParenthesisToken, 1),
                '[' => (SyntaxKind.LeftBracketToken, 1),
                ']' => (SyntaxKind.RightBracketToken, 1),
                '{' => (SyntaxKind.LeftBraceToken, 1),
                '}' => (SyntaxKind.RightBraceToken, 1),
                ':' when Peek(1) == '=' => (SyntaxKind.ColonEqualToken, 2),
                ':' => (SyntaxKind.ColonToken, 1),
                ',' => (SyntaxKind.CommaToken, 1),
                ';' => (SyntaxKind.SemicolonToken, 1),
                '+' when Peek(1) == '=' => (SyntaxKind.PlusEqualToken, 2),
                '+' => (SyntaxKind.PlusToken, 1),
                '-' when Peek(1) == '>' => (SyntaxKind.ArrowToken, 2),
                '-' when Peek(1) == '=' => (SyntaxKind.MinusEqualToken, 2),
                '-' => (SyntaxKind.MinusToken, 1),
                '*' when Peek(1) == '*' && Peek(2) == '=' => (SyntaxKind.DoubleStarEqualToken, 3),
                '*' when Peek(1) == '*' => (SyntaxKind.DoubleStarToken, 2),
                '*' when Peek(1) == '=' => (SyntaxKind.StarEqualToken, 2),
                '*' => (SyntaxKind.StarToken, 1),
                '/' when Peek(1) == '/' && Peek(2) == '=' => (SyntaxKind.DoubleSlashEqualToken, 3),
                '/' when Peek(1) == '/' => (SyntaxKind.DoubleSlashToken, 2),
                '/' when Peek(1) == '=' => (SyntaxKind.SlashEqualToken, 2),
                '/' => (SyntaxKind.SlashToken, 1),
                '|' when Peek(1) == '=' => (SyntaxKind.PipeEqualToken, 2),
                '|' => (SyntaxKind.PipeToken, 1),
                '&' when Peek(1) == '=' => (SyntaxKind.AmpersandEqualToken, 2),
                '&' => (SyntaxKind.AmpersandToken, 1),
                '<' when Peek(1) == '<' && Peek(2) == '=' => (SyntaxKind.LeftShiftEqualToken, 3),
                '<' when Peek(1) == '<' => (SyntaxKind.LeftShiftToken, 2),
                '<' when Peek(1) == '=' => (SyntaxKind.LessEqualToken, 2),
                '<' => (SyntaxKind.LessToken, 1),
                '>' when Peek(1) == '>' && Peek(2) == '=' => (SyntaxKind.RightShiftEqualToken, 3),
                '>' when Peek(1) == '>' => (SyntaxKind.RightShiftToken, 2),
                '>' when Peek(1) == '=' => (SyntaxKind.GreaterEqualToken, 2),
                '>' => (SyntaxKind.GreaterToken, 1),
                '=' when Peek(1) == '=' => (SyntaxKind.EqualEqualToken, 2),
                '=' => (SyntaxKind.EqualToken, 1),
                '!' when Peek(1) == '=' => (SyntaxKind.NotEqualToken, 2),
                '!' => (SyntaxKind.ExclamationToken, 1),
                '.' when Peek(1) == '.' && Peek(2) == '.' => (SyntaxKind.EllipsisToken, 3),
                '.' => (SyntaxKind.DotToken, 1),
                '%' when Peek(1) == '=' => (SyntaxKind.PercentEqualToken, 2),
                '%' => (SyntaxKind.PercentToken, 1),
                '~' => (SyntaxKind.TildeToken, 1),
                '^' when Peek(1) == '=' => (SyntaxKind.CaretEqualToken, 2),
                '^' => (SyntaxKind.CaretToken, 1),
                '@' when Peek(1) == '=' => (SyntaxKind.AtEqualToken, 2),
                '@' => (SyntaxKind.AtToken, 1),
                _ => (SyntaxKind.BadToken, 1),
            };

            _position += width;
            ImmutableArray<PendingDiagnostic> diagnostics;

            if (kind == SyntaxKind.BadToken)
            {
                diagnostics = [Diagnostic(
                LexerDiagnosticCode.UnexpectedCharacter,
                start,
                width,
                $"unexpected character U+{(int)_source[start]:X4}")];
            }
            else
            {
                diagnostics = UpdateDelimiterState(kind, start, width);
            }

            return CreateToken(kind, start, width, leading, diagnostics: diagnostics);
        }

        private ImmutableArray<PendingDiagnostic> UpdateDelimiterState(
            SyntaxKind kind,
            int start,
            int width)
        {
            if (kind is SyntaxKind.LeftParenthesisToken or
                SyntaxKind.LeftBracketToken or
                SyntaxKind.LeftBraceToken)
            {
                _delimiterStack.Add(new DelimiterFrame(kind, start));
                return [];
            }

            if (kind is not (
                SyntaxKind.RightParenthesisToken or
                SyntaxKind.RightBracketToken or
                SyntaxKind.RightBraceToken))
            {
                return [];
            }

            if (_delimiterStack.Count == 0)
            {
                return [Diagnostic(
                LexerDiagnosticCode.UnmatchedClosingDelimiter,
                start,
                width,
                "unmatched closing delimiter")];
            }

            var expectedOpen = kind switch
            {
                SyntaxKind.RightParenthesisToken => SyntaxKind.LeftParenthesisToken,
                SyntaxKind.RightBracketToken => SyntaxKind.LeftBracketToken,
                SyntaxKind.RightBraceToken => SyntaxKind.LeftBraceToken,
                _ => throw new UnreachableException(),
            };

            var actualOpen = _delimiterStack[^1];
            _delimiterStack.RemoveAt(_delimiterStack.Count - 1);

            if (actualOpen.Kind == expectedOpen)
                return [];

            return [Diagnostic(
            LexerDiagnosticCode.MismatchedClosingDelimiter,
            start,
            width,
            $"closing delimiter does not match delimiter opened at offset {actualOpen.Position}")];
        }

        private ImmutableArray<GreenTrivia> ScanTrailingTrivia()
        {
            List<GreenTrivia>? result = null;

            while (!IsAtEnd)
            {
                if (IsHorizontalWhitespace(Current))
                {
                    result ??= [];
                    ScanWhitespaceTrivia(result);
                    continue;
                }

                if (Current == '#')
                {
                    result ??= [];
                    ScanCommentTrivia(result);
                    continue;
                }

                if (Current == '\\' && IsNewLineAt(_position + 1, out var continuationWidth))
                {
                    result ??= [];
                    var start = _position;
                    _position += 1 + continuationWidth;
                    result.Add(CreateTrivia(
                        SyntaxKind.LineContinuationTrivia,
                        start,
                        _position - start));
                    _atBeginningOfLine = true;
                    _explicitContinuation = true;
                    break;
                }

                if (IsNewLineAt(_position, out var newLineWidth) && _delimiterStack.Count != 0)
                {
                    result ??= [];
                    result.Add(CreateTrivia(SyntaxKind.EndOfLineTrivia, _position, newLineWidth));
                    _position += newLineWidth;
                    _atBeginningOfLine = true;
                    break;
                }

                break;
            }

            return result is null ? [] : [.. result];
        }

        private GreenToken LexEndOfFile()
        {
            if (_logicalLineHasCode && !_implicitFinalNewLineEmitted)
            {
                _implicitFinalNewLineEmitted = true;
                _logicalLineHasCode = false;
                return CreateToken(
                    SyntaxKind.NewLineToken,
                    _position,
                    0,
                    TakeLeadingTrivia());
            }

            if (_indentStack.Count > 1)
            {
                while (_indentStack.Count > 1)
                {
                    _indentStack.RemoveAt(_indentStack.Count - 1);
                    AddPendingStructuralToken(SyntaxKind.DedentToken);
                }

                return _pendingTokens.Dequeue();
            }

            ImmutableArray<PendingDiagnostic> diagnostics = [];
            if (_delimiterStack.Count != 0)
            {
                var open = _delimiterStack[^1];
                diagnostics = [Diagnostic(
                LexerDiagnosticCode.UnclosedDelimiter,
                open.Position,
                1,
                "delimiter was never closed")];
            }

            _eofEmitted = true;
            return CreateToken(
                SyntaxKind.EndOfFileToken,
                _position,
                0,
                TakeLeadingTrivia(),
                diagnostics: diagnostics);
        }

        private void ScanWhitespaceTrivia(List<GreenTrivia> destination)
        {
            var start = _position;
            while (!IsAtEnd && IsHorizontalWhitespace(Current))
                _position++;

            destination.Add(CreateTrivia(
                SyntaxKind.WhitespaceTrivia,
                start,
                _position - start));
        }

        private void ScanCommentTrivia(List<GreenTrivia> destination)
        {
            var start = _position;
            while (!IsAtEnd && !IsNewLineAt(_position, out _))
                _position++;

            destination.Add(CreateTrivia(
                SyntaxKind.CommentTrivia,
                start,
                _position - start));
        }

        private void AddPendingStructuralToken(
            SyntaxKind kind,
            LexerDiagnosticCode? diagnosticCode = null,
            string? message = null)
        {
            var diagnostics = diagnosticCode is null
                ? ImmutableArray<PendingDiagnostic>.Empty
                : [Diagnostic(diagnosticCode.Value, _position, 0, message!)];

            _pendingTokens.Enqueue(CreateToken(
                kind,
                _position,
                0,
                TakeLeadingTrivia(),
                diagnostics: diagnostics));
        }

        private ImmutableArray<GreenTrivia> TakeLeadingTrivia()
        {
            if (_leadingTrivia.Count == 0)
                return [];

            var result = _leadingTrivia.ToImmutableArray();
            _leadingTrivia.Clear();
            return result;
        }

        private static GreenToken WithTrailingTrivia(
            GreenToken token,
            ImmutableArray<GreenTrivia> trailingTrivia) =>
            trailingTrivia.IsEmpty
                ? token
                : new GreenToken(
                    token.Kind,
                    token.Text,
                    token.LeadingTrivia,
                    trailingTrivia,
                    token.Diagnostics,
                    token.IsMissing);

        private GreenToken CreateToken(
            SyntaxKind kind,
            int start,
            int length,
            ImmutableArray<GreenTrivia> leadingTrivia = default,
            ImmutableArray<GreenTrivia> trailingTrivia = default,
            ImmutableArray<PendingDiagnostic> diagnostics = default)
        {
            var normalizedDiagnostics = NormalizeDiagnostics(start, leadingTrivia, diagnostics);
            return new GreenToken(
                kind,
                _source.Substring(start, length),
                leadingTrivia,
                trailingTrivia,
                normalizedDiagnostics);
        }

        private GreenTrivia CreateTrivia(SyntaxKind kind, int start, int length) =>
            new(kind, _source.Substring(start, length));

        private static ImmutableArray<GreenDiagnostic> NormalizeDiagnostics(
            int tokenStart,
            ImmutableArray<GreenTrivia> leadingTrivia,
            ImmutableArray<PendingDiagnostic> diagnostics)
        {
            if (diagnostics.IsDefaultOrEmpty)
                return [];

            var leadingWidth = 0;
            if (!leadingTrivia.IsDefaultOrEmpty)
            {
                foreach (var trivia in leadingTrivia)
                    leadingWidth = checked(leadingWidth + trivia.FullWidth);
            }

            var fullStart = tokenStart - leadingWidth;
            var builder = ImmutableArray.CreateBuilder<GreenDiagnostic>(diagnostics.Length);
            foreach (var diagnostic in diagnostics)
            {
                builder.Add(new GreenDiagnostic(
                    SyntaxDiagnosticCode.LexicalError,
                    diagnostic.Span.Start - fullStart,
                    diagnostic.Span.Length,
                    diagnostic.Message));
            }

            return builder.MoveToImmutable();
        }

        private static PendingDiagnostic Diagnostic(
            LexerDiagnosticCode code,
            int start,
            int length,
            string message) =>
            new(code, new TextSpan(start, length), message);

        private bool IsAtEnd => _position >= _source.Length;
        private char Current => Peek(0);

        private char Peek(int offset) => PeekAbsolute(_position + offset);

        private char PeekAbsolute(int index) =>
            (uint)index < (uint)_source.Length ? _source[index] : '\uffff';

        private bool IsNewLineAt(int position, out int width)
        {
            var ch = PeekAbsolute(position);
            if (ch == '\r')
            {
                width = PeekAbsolute(position + 1) == '\n' ? 2 : 1;
                return true;
            }

            if (ch == '\n')
            {
                width = 1;
                return true;
            }

            width = 0;
            return false;
        }

        private static bool IsHorizontalWhitespace(char ch) => ch is ' ' or '\t' or '\f';

        private static bool IsSingleStringPrefix(char ch) =>
            ch is 'r' or 'u' or 'b' or 'f' or 't';

        private static bool IsValidDoublePrefix(char first, char second) =>
            (first, second) is
                ('b', 'r') or ('r', 'b') or
                ('f', 'r') or ('r', 'f') or
                ('t', 'r') or ('r', 't');

        private static StringFlags FlagsForPrefix(char ch) => ch switch
        {
            'r' => StringFlags.Raw,
            'b' => StringFlags.Bytes,
            'f' => StringFlags.Formatted,
            't' => StringFlags.Formatted | StringFlags.Template,
            _ => StringFlags.None,
        };

        private readonly struct IndentLevel
        {
            public readonly int Column;
            public readonly int AlternateColumn;
            public IndentLevel(int column, int alternateColumn)
            {
                Column = column;
                AlternateColumn = alternateColumn;
            }
        }
        private readonly struct DelimiterFrame
        {
            public readonly SyntaxKind Kind;
            public readonly int Position;
            public DelimiterFrame(SyntaxKind kind, int position)
            {
                Kind = kind;
                Position = position;
            }
        }
        private readonly struct ReplacementField
        {
            public readonly int DelimiterBaseline;
            public readonly FormattedStringMode ReturnMode;
            public ReplacementField(int delimiterBaseline, FormattedStringMode returnMode)
            {
                DelimiterBaseline = delimiterBaseline;
                ReturnMode = returnMode;
            }
        }

        private sealed class FormattedStringFrame
        {
            public FormattedStringFrame(
                char quote,
                int quoteWidth,
                bool tripleQuoted,
                bool raw,
                bool template)
            {
                Quote = quote;
                QuoteWidth = quoteWidth;
                TripleQuoted = tripleQuoted;
                Raw = raw;
                Template = template;
            }

            public char Quote { get; }
            public int QuoteWidth { get; }
            public bool TripleQuoted { get; }
            public bool Raw { get; }
            public bool Template { get; }
            public FormattedStringMode Mode { get; set; }
            public List<ReplacementField> Fields { get; } = new();

            public SyntaxKind StartKind => Template
                ? SyntaxKind.TStringStartToken
                : SyntaxKind.FStringStartToken;

            public SyntaxKind MiddleKind => Template
                ? SyntaxKind.TStringMiddleToken
                : SyntaxKind.FStringMiddleToken;

            public SyntaxKind EndKind => Template
                ? SyntaxKind.TStringEndToken
                : SyntaxKind.FStringEndToken;
        }

        private enum FormattedStringMode
        {
            Text,
            Expression,
            FormatSpec,
        }

        [Flags]
        private enum StringFlags
        {
            None = 0,
            Raw = 1,
            Bytes = 2,
            Formatted = 4,
            Template = 8,
        }
    }
}
