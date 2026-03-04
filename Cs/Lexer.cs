using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Cnidaria.Cs
{
    public enum TriviaKind
    {
        WhitespaceTrivia,
        EndOfLineTrivia,
        SingleLineCommentTrivia,
        MultiLineCommentTrivia,
        SingleLineDocCommentTrivia,
        MultiLineDocCommentTrivia,
        PreprocessorDirectiveTrivia,

        SkippedTokensTrivia
    }
    public readonly struct TextSpan
    {
        public readonly int Start;
        public readonly int Length;
        public int End => Start + Length;

        public TextSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }
        public static bool operator ==(TextSpan left, TextSpan right) => left.Start == right.Start && left.Length == right.Length;
        public static bool operator !=(TextSpan left, TextSpan right) => !(left == right);
        public override string ToString() => $"[{Start}..{End})";
        public string ToString(string souce)
        {
            var res = GetLineAndColumn(souce, Start);
            return $"[{res.line}, {res.column}]";
        }
        public override bool Equals(object? obj)
        {
            return obj != null && obj is TextSpan t && t.Start == this.Start && t.Length == this.Length;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Start, Length);
        }

        public static (int line, int column) GetLineAndColumn(string text, int index)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            if ((uint)index > (uint)text.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int line = 1;
            int column = 1;

            for (int i = 0; i < index; i++)
            {
                char c = text[i];

                if (c == '\n')
                {
                    line++;
                    column = 1;
                }
                else if (c == '\r')
                {
                    line++;
                    column = 1;

                    if (i + 1 < index && text[i + 1] == '\n')
                        i++;
                }
                else
                {
                    column++;
                }
            }

            return (line, column);
        }
    }
    public readonly struct SyntaxDiagnostic : IDiagnostic
    {
        public readonly int Position;
        public readonly string Message;

        public SyntaxDiagnostic(int position, string message)
        {
            Position = position;
            Message = message;
        }
        public override string ToString() => $"{Position}: {Message}";
        public string ToString(string source) => $"{GetLineNumber(source, Position)}: {Message}";
        public string GetMessage() => $"Parser error: {this.ToString()}";
        public string GetMessage(string source) => $"Parser error: {this.ToString(source)}";
        public DiagnosticSeverity GetSeverity() => DiagnosticSeverity.Error;
        public static int GetLineNumber(string text, int index)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            if ((uint)index > (uint)text.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int line = 1;

            for (int i = 0; i < index; i++)
            {
                char c = text[i];

                if (c == '\n')
                {
                    line++;
                }
                else if (c == '\r')
                {
                    line++;

                    if (i + 1 < index && text[i + 1] == '\n')
                        i++;
                }
            }

            return line;
        }
    }
    public readonly struct SyntaxTrivia
    {
        public readonly TriviaKind Kind;
        public readonly TextSpan Span;

        public SyntaxTrivia(TriviaKind kind, TextSpan span)
        {
            Kind = kind;
            Span = span;
        }

        public string GetText(string source) => source.Substring(Span.Start, Span.Length);
        public override string ToString() => $"{Kind} {Span}";
    }
    public readonly struct SyntaxToken
    {
        public readonly SyntaxKind Kind;
        public readonly SyntaxKind ContextualKind;
        public readonly TextSpan Span;
        public readonly string? ValueText; // decoded identifier
        public readonly object? Value; // actual value of a represented object
        public readonly SyntaxTrivia[] LeadingTrivia;
        public readonly SyntaxTrivia[] TrailingTrivia;
        public SyntaxToken(
        SyntaxKind kind,
        TextSpan span,
        string? valueText,
        object? value,
        SyntaxTrivia[] leadingTrivia,
        SyntaxTrivia[] trailingTrivia)
        : this(kind, kind, span, valueText, value, leadingTrivia, trailingTrivia)
        {
        }

        public SyntaxToken(
            SyntaxKind kind,
            SyntaxKind contextualKind,
            TextSpan span,
            string? valueText,
            object? value,
            SyntaxTrivia[] leadingTrivia,
            SyntaxTrivia[] trailingTrivia)
        {
            Kind = kind;
            ContextualKind = contextualKind;
            Span = span;
            ValueText = valueText;
            Value = value;
            LeadingTrivia = leadingTrivia ?? Array.Empty<SyntaxTrivia>();
            TrailingTrivia = trailingTrivia ?? Array.Empty<SyntaxTrivia>();
        }

        public string GetText(string source) => source.Substring(Span.Start, Span.Length);
        public override string ToString() => $"{Kind} {Span}";
    }
    public sealed class LexerOptions
    {
        public bool IncludeTrivia { get; set; } = true;
    }
    internal static partial class SyntaxFacts
    {
        public static int GetUnaryOperatorPrecedence(SyntaxKind kind) => kind switch
        {
            SyntaxKind.PlusPlusToken => 12,
            SyntaxKind.MinusMinusToken => 12,

            SyntaxKind.PlusToken => 12,
            SyntaxKind.MinusToken => 12,
            SyntaxKind.ExclamationToken => 12,
            SyntaxKind.TildeToken => 12,

            SyntaxKind.CaretToken => 12,

            SyntaxKind.AmpersandToken => 12,
            SyntaxKind.AsteriskToken => 12,

            SyntaxKind.RefKeyword => 12,

            _ => 0
        };

        public static int GetBinaryOperatorPrecedence(SyntaxKind kind) => kind switch
        {
            // multiplicative
            SyntaxKind.AsteriskToken => 10,
            SyntaxKind.SlashToken => 10,
            SyntaxKind.PercentToken => 10,

            // additive
            SyntaxKind.PlusToken => 9,
            SyntaxKind.MinusToken => 9,

            // shift
            SyntaxKind.LessThanLessThanToken => 8,
            SyntaxKind.GreaterThanGreaterThanToken => 8,
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => 8,

            // relational / type testing
            SyntaxKind.LessThanToken => 7,
            SyntaxKind.LessThanEqualsToken => 7,
            SyntaxKind.GreaterThanToken => 7,
            SyntaxKind.GreaterThanEqualsToken => 7,
            SyntaxKind.IsKeyword => 7,
            SyntaxKind.AsKeyword => 7,

            // equality
            SyntaxKind.EqualsEqualsToken => 6,
            SyntaxKind.ExclamationEqualsToken => 6,

            // bitwise
            SyntaxKind.AmpersandToken => 5,
            SyntaxKind.CaretToken => 4,
            SyntaxKind.BarToken => 3,

            // logical
            SyntaxKind.AmpersandAmpersandToken => 2,
            SyntaxKind.BarBarToken => 1,

            _ => 0
        };

    }
    internal static partial class SyntaxFacts
    {
        public static readonly Dictionary<string, SyntaxKind> ReservedKeywords = new(StringComparer.Ordinal)
        {
            // Reserved
            ["abstract"] = SyntaxKind.AbstractKeyword,
            ["as"] = SyntaxKind.AsKeyword,
            ["base"] = SyntaxKind.BaseKeyword,
            ["bool"] = SyntaxKind.BoolKeyword,
            ["break"] = SyntaxKind.BreakKeyword,
            ["byte"] = SyntaxKind.ByteKeyword,
            ["case"] = SyntaxKind.CaseKeyword,
            ["catch"] = SyntaxKind.CatchKeyword,
            ["char"] = SyntaxKind.CharKeyword,
            ["checked"] = SyntaxKind.CheckedKeyword,
            ["class"] = SyntaxKind.ClassKeyword,
            ["const"] = SyntaxKind.ConstKeyword,
            ["continue"] = SyntaxKind.ContinueKeyword,
            ["decimal"] = SyntaxKind.DecimalKeyword,
            ["default"] = SyntaxKind.DefaultKeyword,
            ["delegate"] = SyntaxKind.DelegateKeyword,
            ["do"] = SyntaxKind.DoKeyword,
            ["double"] = SyntaxKind.DoubleKeyword,
            ["else"] = SyntaxKind.ElseKeyword,
            ["enum"] = SyntaxKind.EnumKeyword,
            ["event"] = SyntaxKind.EventKeyword,
            ["explicit"] = SyntaxKind.ExplicitKeyword,
            ["extern"] = SyntaxKind.ExternKeyword,
            ["false"] = SyntaxKind.FalseKeyword,
            ["finally"] = SyntaxKind.FinallyKeyword,
            ["fixed"] = SyntaxKind.FixedKeyword,
            ["float"] = SyntaxKind.FloatKeyword,
            ["for"] = SyntaxKind.ForKeyword,
            ["foreach"] = SyntaxKind.ForEachKeyword,
            ["goto"] = SyntaxKind.GotoKeyword,
            ["if"] = SyntaxKind.IfKeyword,
            ["implicit"] = SyntaxKind.ImplicitKeyword,
            ["in"] = SyntaxKind.InKeyword,
            ["int"] = SyntaxKind.IntKeyword,
            ["interface"] = SyntaxKind.InterfaceKeyword,
            ["internal"] = SyntaxKind.InternalKeyword,
            ["is"] = SyntaxKind.IsKeyword,
            ["lock"] = SyntaxKind.LockKeyword,
            ["long"] = SyntaxKind.LongKeyword,
            ["namespace"] = SyntaxKind.NamespaceKeyword,
            ["new"] = SyntaxKind.NewKeyword,
            ["null"] = SyntaxKind.NullKeyword,
            ["object"] = SyntaxKind.ObjectKeyword,
            ["operator"] = SyntaxKind.OperatorKeyword,
            ["out"] = SyntaxKind.OutKeyword,
            ["override"] = SyntaxKind.OverrideKeyword,
            ["params"] = SyntaxKind.ParamsKeyword,
            ["private"] = SyntaxKind.PrivateKeyword,
            ["protected"] = SyntaxKind.ProtectedKeyword,
            ["public"] = SyntaxKind.PublicKeyword,
            ["readonly"] = SyntaxKind.ReadOnlyKeyword,
            ["ref"] = SyntaxKind.RefKeyword,
            ["return"] = SyntaxKind.ReturnKeyword,
            ["sbyte"] = SyntaxKind.SByteKeyword,
            ["sealed"] = SyntaxKind.SealedKeyword,
            ["short"] = SyntaxKind.ShortKeyword,
            ["sizeof"] = SyntaxKind.SizeOfKeyword,
            ["stackalloc"] = SyntaxKind.StackAllocKeyword,
            ["static"] = SyntaxKind.StaticKeyword,
            ["string"] = SyntaxKind.StringKeyword,
            ["struct"] = SyntaxKind.StructKeyword,
            ["switch"] = SyntaxKind.SwitchKeyword,
            ["this"] = SyntaxKind.ThisKeyword,
            ["throw"] = SyntaxKind.ThrowKeyword,
            ["true"] = SyntaxKind.TrueKeyword,
            ["try"] = SyntaxKind.TryKeyword,
            ["typeof"] = SyntaxKind.TypeOfKeyword,
            ["uint"] = SyntaxKind.UIntKeyword,
            ["ulong"] = SyntaxKind.ULongKeyword,
            ["unchecked"] = SyntaxKind.UncheckedKeyword,
            ["unsafe"] = SyntaxKind.UnsafeKeyword,
            ["ushort"] = SyntaxKind.UShortKeyword,
            ["using"] = SyntaxKind.UsingKeyword,
            ["virtual"] = SyntaxKind.VirtualKeyword,
            ["void"] = SyntaxKind.VoidKeyword,
            ["volatile"] = SyntaxKind.VolatileKeyword,
            ["while"] = SyntaxKind.WhileKeyword,
        };

        public static readonly Dictionary<string, SyntaxKind> ContextualKeywords = new(StringComparer.Ordinal)
        {
            // Contextual
            ["add"] = SyntaxKind.AddKeyword,
            ["allows"] = SyntaxKind.AllowsKeyword,
            ["alias"] = SyntaxKind.AliasKeyword,
            ["and"] = SyntaxKind.AndKeyword,
            ["ascending"] = SyntaxKind.AscendingKeyword,
            ["async"] = SyntaxKind.AsyncKeyword,
            ["await"] = SyntaxKind.AwaitKeyword,
            ["assembly"] = SyntaxKind.AssemblyKeyword,
            ["by"] = SyntaxKind.ByKeyword,
            ["descending"] = SyntaxKind.DescendingKeyword,
            ["equals"] = SyntaxKind.EqualsKeyword,
            ["extension"] = SyntaxKind.ExtensionKeyword,
            ["field"] = SyntaxKind.FieldKeyword,
            ["file"] = SyntaxKind.FileKeyword,
            ["from"] = SyntaxKind.FromKeyword,
            ["get"] = SyntaxKind.GetKeyword,
            ["global"] = SyntaxKind.GlobalKeyword,
            ["group"] = SyntaxKind.GroupKeyword,
            ["init"] = SyntaxKind.InitKeyword,
            ["into"] = SyntaxKind.IntoKeyword,
            ["join"] = SyntaxKind.JoinKeyword,
            ["type"] = SyntaxKind.TypeKeyword,
            ["let"] = SyntaxKind.LetKeyword,
            ["managed"] = SyntaxKind.ManagedKeyword,
            ["method"] = SyntaxKind.MethodKeyword,
            ["module"] = SyntaxKind.ModuleKeyword,
            ["nameof"] = SyntaxKind.NameOfKeyword,
            ["not"] = SyntaxKind.NotKeyword,
            ["on"] = SyntaxKind.OnKeyword,
            ["or"] = SyntaxKind.OrKeyword,
            ["orderby"] = SyntaxKind.OrderByKeyword,
            ["param"] = SyntaxKind.ParamKeyword,
            ["partial"] = SyntaxKind.PartialKeyword,
            ["property"] = SyntaxKind.PropertyKeyword,
            ["record"] = SyntaxKind.RecordKeyword,
            ["remove"] = SyntaxKind.RemoveKeyword,
            ["required"] = SyntaxKind.RequiredKeyword,
            ["scoped"] = SyntaxKind.ScopedKeyword,
            ["select"] = SyntaxKind.SelectKeyword,
            ["set"] = SyntaxKind.SetKeyword,
            ["unmanaged"] = SyntaxKind.UnmanagedKeyword,
            ["var"] = SyntaxKind.VarKeyword,
            ["when"] = SyntaxKind.WhenKeyword,
            ["where"] = SyntaxKind.WhereKeyword,
            ["with"] = SyntaxKind.WithKeyword,
            ["yield"] = SyntaxKind.YieldKeyword,
        };
        // Longest match operator table
        public static readonly (string Text, SyntaxKind Kind)[] Operators =
        {

            (">>>=", SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken),
            ("??=",  SyntaxKind.QuestionQuestionEqualsToken),


            (">>>",  SyntaxKind.GreaterThanGreaterThanGreaterThanToken),
            ("<<=",  SyntaxKind.LessThanLessThanEqualsToken),
            (">>=",  SyntaxKind.GreaterThanGreaterThanEqualsToken),
            ("=>",   SyntaxKind.EqualsGreaterThanToken),
            ("->",   SyntaxKind.MinusGreaterThanToken),


            ("++", SyntaxKind.PlusPlusToken),
            ("--", SyntaxKind.MinusMinusToken),

            ("+=", SyntaxKind.PlusEqualsToken),
            ("-=", SyntaxKind.MinusEqualsToken),
            ("*=", SyntaxKind.AsteriskEqualsToken),
            ("/=", SyntaxKind.SlashEqualsToken),
            ("%=", SyntaxKind.PercentEqualsToken),

            ("&=", SyntaxKind.AmpersandEqualsToken),
            ("|=", SyntaxKind.BarEqualsToken),
            ("^=", SyntaxKind.CaretEqualsToken),

            ("==", SyntaxKind.EqualsEqualsToken),
            ("!=", SyntaxKind.ExclamationEqualsToken),
            ("<=", SyntaxKind.LessThanEqualsToken),
            (">=", SyntaxKind.GreaterThanEqualsToken),

            ("&&", SyntaxKind.AmpersandAmpersandToken),
            ("||", SyntaxKind.BarBarToken),

            ("<<", SyntaxKind.LessThanLessThanToken),
            (">>", SyntaxKind.GreaterThanGreaterThanToken),

            ("??", SyntaxKind.QuestionQuestionToken),
            ("..", SyntaxKind.DotDotToken),
            ("::", SyntaxKind.ColonColonToken),


            ("(", SyntaxKind.OpenParenToken),
            (")", SyntaxKind.CloseParenToken),
            ("[", SyntaxKind.OpenBracketToken),
            ("]", SyntaxKind.CloseBracketToken),
            ("{", SyntaxKind.OpenBraceToken),
            ("}", SyntaxKind.CloseBraceToken),

            (".", SyntaxKind.DotToken),
            (",", SyntaxKind.CommaToken),
            (":", SyntaxKind.ColonToken),
            (";", SyntaxKind.SemicolonToken),

            ("+", SyntaxKind.PlusToken),
            ("-", SyntaxKind.MinusToken),
            ("*", SyntaxKind.AsteriskToken),
            ("/", SyntaxKind.SlashToken),
            ("%", SyntaxKind.PercentToken),

            ("&", SyntaxKind.AmpersandToken),
            ("|", SyntaxKind.BarToken),
            ("^", SyntaxKind.CaretToken),
            ("~", SyntaxKind.TildeToken),
            ("!", SyntaxKind.ExclamationToken),

            ("=", SyntaxKind.EqualsToken),
            ("<", SyntaxKind.LessThanToken),
            (">", SyntaxKind.GreaterThanToken),
            ("?", SyntaxKind.QuestionToken),
        };
        public static bool TryGetReservedKeyword(string identifierValueText, out SyntaxKind kind)
        => ReservedKeywords.TryGetValue(identifierValueText, out kind);

        public static bool TryGetContextualKeyword(string identifierValueText, out SyntaxKind kind)
            => ContextualKeywords.TryGetValue(identifierValueText, out kind);
        public static bool IsReservedKeyword(SyntaxKind kind)
        => kind >= SyntaxKind.BoolKeyword && kind <= SyntaxKind.ImplicitKeyword;

        public static bool IsContextualKeyword(SyntaxKind kind)
            => kind >= SyntaxKind.YieldKeyword && kind <= SyntaxKind.ExtensionKeyword;

        public static bool IsKeyword(SyntaxKind kind)
            => IsReservedKeyword(kind) || IsContextualKeyword(kind);

        public static bool IsPreprocessorKeyword(SyntaxKind kind) => false;
    }
    public static class CSharpExtensions
    {
        public static SyntaxKind Kind(this SyntaxToken token) => token.Kind;
        public static SyntaxKind ContextualKind(this SyntaxToken token) => token.ContextualKind;

        public static bool IsContextualKeyword(this SyntaxToken token)
            => token.Kind == SyntaxKind.IdentifierToken && SyntaxFacts.IsContextualKeyword(token.ContextualKind);

        public static bool IsReservedKeyword(this SyntaxToken token)
            => SyntaxFacts.IsReservedKeyword(token.Kind);

        public static bool IsKeyword(this SyntaxToken token)
            => token.IsReservedKeyword() || token.IsContextualKeyword();

        public static bool IsPreprocessorKeyword(this SyntaxToken token)
            => SyntaxFacts.IsPreprocessorKeyword(token.Kind);

        public static bool IsNone(this SyntaxToken t) => t.Kind == SyntaxKind.None;
    }
    public sealed class Lexer
    {
        internal enum LexMode
        {
            Normal,
            InterpolatedText,
            InterpolationExpression,
            InterpolationFormat
        }
        private struct InterpolatedState
        {
            public bool IsVerbatim;
            public bool IsRaw;
            public bool IsRawMultiLine;
            public int Dollars;
            public int QuoteCount;
        }
        private readonly struct ModeFrame
        {
            public readonly LexMode Mode;
            public readonly InterpolatedState State;
            public readonly int BraceDepth;

            public ModeFrame(LexMode mode, InterpolatedState state, int braceDepth)
            {
                Mode = mode;
                State = state;
                BraceDepth = braceDepth;
            }
        }

        private readonly Stack<ModeFrame> _modeStack = new();
        private readonly string _text;
        private readonly LexerOptions _options;

        private int _pos;
        private bool _atStartOfLine = true;
        private LexMode _mode = LexMode.Normal;
        private InterpolatedState _is;
        private int _interpBraceDepth;
        public List<SyntaxDiagnostic> Diagnostics { get; } = new List<SyntaxDiagnostic>();

        public Lexer(string text, LexerOptions? options = null)
        {
            _text = text ?? string.Empty;
            _options = options ?? new LexerOptions();
        }
        private void PushFrame()
        {
            _modeStack.Push(new ModeFrame(_mode, _is, _interpBraceDepth));
        }
        private void PopFrameOrNormal()
        {
            if (_modeStack.Count == 0)
            {
                _mode = LexMode.Normal;
                _is = default;
                _interpBraceDepth = 0;
                return;
            }

            var f = _modeStack.Pop();
            _mode = f.Mode;
            _is = f.State;
            _interpBraceDepth = f.BraceDepth;
        }
        public IEnumerable<SyntaxToken> LexAll()
        {
            while (true)
            {
                var t = Lex();
                yield return t;
                if (t.Kind == SyntaxKind.EndOfFileToken)
                    yield break;
            }
        }

        public SyntaxToken Lex()
        {
            if (_mode == LexMode.InterpolatedText)
                return LexInterpolatedTextToken();

            if (_mode == LexMode.InterpolationExpression)
                return LexInterpolationExpressionToken();

            if (_mode == LexMode.InterpolationFormat)
                return LexInterpolationFormatToken();

            // Normal mode
            SyntaxTrivia[] leading = Array.Empty<SyntaxTrivia>();
            SyntaxTrivia[] trailing = Array.Empty<SyntaxTrivia>();

            if (_options.IncludeTrivia)
                leading = ReadTrivia(isLeading: true);

            // Try start interpolated string
            if (TryStartInterpolatedString(leading, out var startTok))
            {
                return startTok;
            }

            var token = ReadTokenCore(leading);

            if (_options.IncludeTrivia)
                trailing = ReadTrivia(isLeading: false);

            return new SyntaxToken(
                token.Kind,
                token.ContextualKind,
                token.Span,
                token.ValueText,
                token.Value,
                token.LeadingTrivia,
                trailing);
        }
        internal void EnterInterpolationFormat()
        {
            if (_mode == LexMode.InterpolationExpression)
                _mode = LexMode.InterpolationFormat;
        }
        private SyntaxToken LexInterpolationFormatToken()
        {
            int start = _pos;

            if (IsAtEnd())
            {
                Diagnostics.Add(new SyntaxDiagnostic(_pos, "Unterminated interpolation format."));
                _mode = LexMode.Normal;
                return new SyntaxToken(SyntaxKind.InterpolatedStringTextToken, new TextSpan(start, 0), null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            if (_is.IsRaw)
            {
                // Raw interpolated
                while (!IsAtEnd())
                {
                    char c = Current();

                    if (c == '}')
                    {
                        int run = CountWhileFrom(_pos, '}');
                        if (run >= _is.Dollars)
                        {
                            break;
                        }

                        Diagnostics.Add(new SyntaxDiagnostic(_pos, "Curly braces are not allowed in interpolation format specifier."));
                        _pos++; // progress
                        continue;
                    }

                    if (c == '{')
                    {
                        Diagnostics.Add(new SyntaxDiagnostic(_pos, "Curly braces are not allowed in interpolation format specifier."));
                        _pos++; // progress
                        continue;
                    }

                    if (IsNewLineChar(c))
                    {
                        ConsumeNewLine();
                        continue;
                    }

                    _pos++;
                }
            }
            else
            {
                while (!IsAtEnd())
                {
                    char c = Current();

                    if (c == '}')
                    {
                        // first closing brace ends the interpolation
                        break;
                    }

                    if (c == '{')
                    {
                        Diagnostics.Add(new SyntaxDiagnostic(_pos, "Curly braces are not allowed in interpolation format specifier."));
                        _pos++; // progress
                        continue;
                    }

                    if (!_is.IsVerbatim && IsNewLineChar(c))
                    {
                        Diagnostics.Add(new SyntaxDiagnostic(_pos, "Newline in interpolated string format (non-verbatim)."));
                        break;
                    }

                    if (IsNewLineChar(c))
                    {
                        ConsumeNewLine();
                        continue;
                    }

                    _pos++;
                }
            }
            _mode = LexMode.InterpolationExpression;

            var span = new TextSpan(start, _pos - start);
            var raw = _text.AsSpan(span.Start, span.Length);
            string decoded;

            if (_is.IsRaw)
                decoded = raw.ToString();
            else if (_is.IsVerbatim)
                decoded = DecodeInterpolatedVerbatimText(raw);
            else
                decoded = DecodeInterpolatedRegularText(raw);

            return new SyntaxToken(
                SyntaxKind.InterpolatedStringTextToken,
                span,
                decoded,
                decoded,
                Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
        }
        private bool TryStartInterpolatedString(SyntaxTrivia[] leadingTrivia, out SyntaxToken token)
        {
            token = default;

            int start = _pos;
            if (IsAtEnd())
                return false;

            if (Current() == '$')
            {
                int dollars = CountWhile('$');

                // interpolated raw
                if (Peek(dollars) == '"' && Peek(dollars + 1) == '"' && Peek(dollars + 2) == '"')
                {
                    int quoteCount = CountWhileFrom(_pos + dollars, '"'); // >= 3
                    PushFrame();

                    _is = new InterpolatedState
                    {
                        IsRaw = true,
                        IsVerbatim = false,
                        Dollars = dollars,
                        QuoteCount = quoteCount,
                        IsRawMultiLine = false
                    };

                    int afterOpen = _pos + dollars + quoteCount;

                    // Determine multiline
                    int p = afterOpen;
                    while (p < _text.Length && IsWhitespaceButNotNewLine(_text[p]))
                        p++;

                    bool isMulti = (p < _text.Length) && IsNewLineChar(_text[p]);

                    if (isMulti)
                    {
                        _is.IsRawMultiLine = true;

                        _pos = p;
                        ConsumeNewLine();
                    }
                    else
                    {
                        _pos = afterOpen;
                    }

                    _mode = LexMode.InterpolatedText;
                    _interpBraceDepth = 0;

                    token = new SyntaxToken(
                        isMulti ? SyntaxKind.InterpolatedMultiLineRawStringStartToken
                                : SyntaxKind.InterpolatedSingleLineRawStringStartToken,
                        new TextSpan(start, _pos - start),
                        null, null,
                        leadingTrivia,
                        Array.Empty<SyntaxTrivia>());

                    return true;
                }

                // regular interpolated
                if (dollars == 1 && Peek(1) == '"')
                {
                    PushFrame();

                    _is = new InterpolatedState
                    {
                        IsRaw = false,
                        IsVerbatim = false,
                        Dollars = 1,
                        QuoteCount = 1
                    };

                    _pos += 2; // $"
                    _mode = LexMode.InterpolatedText;
                    _interpBraceDepth = 0;

                    token = new SyntaxToken(
                        SyntaxKind.InterpolatedStringStartToken,
                        new TextSpan(start, 2),
                        null, null,
                        leadingTrivia,
                        Array.Empty<SyntaxTrivia>());

                    return true;
                }

                // verbatim interpolated
                if (dollars == 1 && Peek(1) == '@' && Peek(2) == '"')
                {
                    PushFrame();

                    _is = new InterpolatedState
                    {
                        IsRaw = false,
                        IsVerbatim = true,
                        Dollars = 1,
                        QuoteCount = 1
                    };

                    _pos += 3; // $@"
                    _mode = LexMode.InterpolatedText;
                    _interpBraceDepth = 0;

                    token = new SyntaxToken(
                        SyntaxKind.InterpolatedVerbatimStringStartToken,
                        new TextSpan(start, 3),
                        null, null,
                        leadingTrivia,
                        Array.Empty<SyntaxTrivia>());

                    return true;
                }

                return false;
            }
            // @$"
            if (Current() == '@' && Peek(1) == '$' && Peek(2) == '"')
            {
                PushFrame();

                _is = new InterpolatedState
                {
                    IsRaw = false,
                    IsVerbatim = true,
                    Dollars = 1,
                    QuoteCount = 1
                };

                _pos += 3; // @$"
                _mode = LexMode.InterpolatedText;
                _interpBraceDepth = 0;

                token = new SyntaxToken(
                    SyntaxKind.InterpolatedVerbatimStringStartToken,
                    new TextSpan(start, 3),
                    null, null,
                    leadingTrivia,
                    Array.Empty<SyntaxTrivia>());

                return true;
            }

            return false;
        }
        private SyntaxToken LexInterpolationExpressionToken()
        {
            SyntaxTrivia[] leading = Array.Empty<SyntaxTrivia>();
            if (_options.IncludeTrivia)
                leading = ReadTrivia(isLeading: true);
            if (_interpBraceDepth == 0 && IsAtInterpolationEndDelimiter())
            {
                int start = _pos;
                int len = _is.IsRaw ? _is.Dollars : 1;
                _pos += len;

                _mode = LexMode.InterpolatedText;
                return new SyntaxToken(SyntaxKind.CloseBraceToken, new TextSpan(start, len), null, null, leading, Array.Empty<SyntaxTrivia>());
            }
            if (TryStartInterpolatedString(leading, out var startTok))
                return startTok;

            var core = ReadTokenCore(leading);

            SyntaxTrivia[] trailing = Array.Empty<SyntaxTrivia>();
            if (_options.IncludeTrivia)
                trailing = ReadTrivia(isLeading: false);

            var tok = new SyntaxToken(
                core.Kind,
                core.ContextualKind,
                core.Span,
                core.ValueText,
                core.Value,
                core.LeadingTrivia,
                trailing);


            if (tok.Kind == SyntaxKind.OpenBraceToken)
                _interpBraceDepth++;
            else if (tok.Kind == SyntaxKind.CloseBraceToken)
            {
                if (_interpBraceDepth > 0)
                    _interpBraceDepth--;
                else
                {
                    // keep going
                }
            }

            return tok;
        }
        private bool IsAtInterpolationEndDelimiter()
        {
            if (IsAtEnd())
                return false;

            if (_is.IsRaw)
            {
                for (int i = 0; i < _is.Dollars; i++)
                {
                    if (Peek(i) != '}')
                        return false;
                }
                return true;
            }

            return Current() == '}';
        }
        private SyntaxToken LexInterpolatedTextToken()
        {
            if (IsAtEnd())
            {
                Diagnostics.Add(new SyntaxDiagnostic(_pos, "Unterminated interpolated string literal."));
                _mode = LexMode.Normal;
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(_pos, 0), null, null, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            if (_is.IsRaw)
                return LexInterpolatedRawTextToken();

            return _is.IsVerbatim ? LexInterpolatedVerbatimTextToken() : LexInterpolatedRegularTextToken();
        }
        private SyntaxToken LexInterpolatedRegularTextToken()
        {
            int start = _pos;

            // Interpolation start
            if (Current() == '{' && Peek(1) != '{')
            {
                _pos++;
                _mode = LexMode.InterpolationExpression;
                _interpBraceDepth = 0;

                return new SyntaxToken(SyntaxKind.OpenBraceToken, new TextSpan(start, 1), null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            // End
            if (Current() == '"')
            {
                _pos++; // closing "
                TryConsumeU8Suffix();
                PopFrameOrNormal();

                return new SyntaxToken(SyntaxKind.InterpolatedStringEndToken, new TextSpan(start, _pos - start), null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            // read text until next special point
            while (!IsAtEnd())
            {
                char c = Current();

                if (c == '"')
                    break;

                if (c == '{')
                {
                    if (Peek(1) == '{')
                    {
                        _pos += 2; // escaped {{
                        continue;
                    }
                    break; // start interpolation
                }

                if (c == '}')
                {
                    if (Peek(1) == '}')
                    {
                        _pos += 2; // escaped }}
                        continue;
                    }

                    // Unescaped '}'
                    Diagnostics.Add(new SyntaxDiagnostic(_pos, "Unescaped '}' in interpolated string text."));
                    _pos++;
                    continue;
                }

                // Allow newlines for recovery
                if (IsNewLineChar(c))
                {
                    Diagnostics.Add(new SyntaxDiagnostic(_pos, "Newline in interpolated string (non-verbatim)."));
                    ConsumeNewLine(); // advances _pos
                    continue;
                }

                if (c == '\\')
                {
                    // escaped sequence inside regular string
                    _pos++;
                    if (!IsAtEnd())
                    {
                        if (Current() == 'u') { _pos++; ConsumeHexDigits(4); }
                        else if (Current() == 'U') { _pos++; ConsumeHexDigits(8); }
                        else _pos++;
                    }
                    continue;
                }

                _pos++;
            }

            int len = _pos - start;
            if (len == 0)
            {
                // Force progress
                _pos++;
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 1), null, null, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            var span = new TextSpan(start, len);
            var raw = _text.AsSpan(span.Start, span.Length);
            var decoded = DecodeInterpolatedRegularText(raw);

            return new SyntaxToken(
                SyntaxKind.InterpolatedStringTextToken,
                span,
                decoded,
                decoded,
                Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken LexInterpolatedVerbatimTextToken()
        {
            int start = _pos;

            // Interpolation start
            if (Current() == '{' && Peek(1) != '{')
            {
                _pos++;
                _mode = LexMode.InterpolationExpression;
                _interpBraceDepth = 0;

                return new SyntaxToken(SyntaxKind.OpenBraceToken, new TextSpan(start, 1), null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            // End
            if (Current() == '"' && Peek(1) != '"')
            {
                _pos++; // closing "
                TryConsumeU8Suffix();
                PopFrameOrNormal();

                return new SyntaxToken(SyntaxKind.InterpolatedStringEndToken, new TextSpan(start, _pos - start), null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            // Text scan
            while (!IsAtEnd())
            {
                char c = Current();

                if (c == '"')
                {
                    if (Peek(1) == '"')
                    {
                        _pos += 2; // escaped quote ""
                        continue;
                    }
                    break; // end
                }

                if (c == '{')
                {
                    if (Peek(1) == '{') { _pos += 2; continue; }
                    break;
                }

                if (c == '}')
                {
                    if (Peek(1) == '}') { _pos += 2; continue; }
                    Diagnostics.Add(new SyntaxDiagnostic(_pos, "Unescaped '}' in interpolated string text."));
                    _pos++;
                    continue;
                }

                if (IsNewLineChar(c))
                {
                    ConsumeNewLine();
                    continue;
                }

                _pos++;
            }
            int len = _pos - start;
            if (len == 0)
            {
                _pos++;
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 1), null, null, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            var span = new TextSpan(start, len);
            var raw = _text.AsSpan(span.Start, span.Length);
            var decoded = DecodeInterpolatedVerbatimText(raw);

            return new SyntaxToken(
                SyntaxKind.InterpolatedStringTextToken,
                span,
                decoded,
                decoded,
                Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken LexInterpolatedRawTextToken()
        {
            int start = _pos;

            if (TryGetRawMultiLineEndLength(_pos, out int endLen))
            {
                _pos += endLen;
                PopFrameOrNormal();

                return new SyntaxToken(
                    SyntaxKind.InterpolatedRawStringEndToken,
                    new TextSpan(start, endLen),
                    null, null,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }

            if (!_is.IsRawMultiLine && Current() == '"')
            {
                int run = CountWhileFrom(_pos, '"');
                if (run >= _is.QuoteCount)
                {
                    _pos += _is.QuoteCount;
                    TryConsumeU8Suffix();
                    PopFrameOrNormal();

                    return new SyntaxToken(
                        SyntaxKind.InterpolatedRawStringEndToken,
                        new TextSpan(start, _pos - start),
                        null, null,
                        Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
                }
            }

            // Interpolation start delimiter for raw
            if (Current() == '{')
            {
                int run = CountWhileFrom(_pos, '{');
                if (run >= _is.Dollars)
                {
                    int extra = run - _is.Dollars;
                    if (extra > 0)
                    {
                        _pos += extra;
                        var span = new TextSpan(start, extra);
                        var raw = _text.AsSpan(span.Start, span.Length);
                        var decoded = raw.ToString();

                        return new SyntaxToken(
                            SyntaxKind.InterpolatedStringTextToken,
                            span,
                            decoded,
                            decoded,
                            Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
                    }

                    _pos += _is.Dollars;
                    _mode = LexMode.InterpolationExpression;
                    _interpBraceDepth = 0;

                    return new SyntaxToken(
                        SyntaxKind.OpenBraceToken,
                        new TextSpan(start, _is.Dollars),
                        null, null,
                        Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
                }
            }

            // Text scan
            while (!IsAtEnd())
            {
                if (TryGetRawMultiLineEndLength(_pos, out _))
                    break;

                char c = Current();

                if (!_is.IsRawMultiLine && c == '"')
                {
                    int run = CountWhileFrom(_pos, '"');
                    if (run >= _is.QuoteCount)
                        break;
                    _pos++;
                    continue;
                }

                if (c == '{')
                {
                    int run = CountWhileFrom(_pos, '{');
                    if (run >= _is.Dollars)
                        break;
                    _pos++;
                    continue;
                }

                if (IsNewLineChar(c))
                {
                    ConsumeNewLine();
                    continue;
                }

                _pos++;
            }
            {
                int len = _pos - start;
                if (len == 0)
                {
                    _pos++;
                    return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 1), null, null,
                        Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
                }

                var span = new TextSpan(start, len);
                var raw = _text.AsSpan(span.Start, span.Length);
                var decoded = raw.ToString();

                return new SyntaxToken(
                    SyntaxKind.InterpolatedStringTextToken,
                    span,
                    decoded,
                    decoded,
                    Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            }
        }
        private static string DecodeInterpolatedRegularText(ReadOnlySpan<char> raw)
        {
            if (raw.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                // Escaped braces
                if (c == '{' && i + 1 < raw.Length && raw[i + 1] == '{') { sb.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < raw.Length && raw[i + 1] == '}') { sb.Append('}'); i++; continue; }

                // Regular string escape sequences
                if (c == '\\')
                {
                    int j = i + 1;
                    if (!TryDecodeEscape(raw, ref j, allowSurrogatePairInString: true, out var decoded))
                    {
                        // Keep original for recovery
                        sb.Append('\\');
                    }
                    else
                    {
                        sb.Append(decoded);
                        i = j - 1;
                    }
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
        private static string DecodeInterpolatedVerbatimText(ReadOnlySpan<char> raw)
        {
            if (raw.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                // Escaped braces
                if (c == '{' && i + 1 < raw.Length && raw[i + 1] == '{') { sb.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < raw.Length && raw[i + 1] == '}') { sb.Append('}'); i++; continue; }

                // Escaped quote
                if (c == '"' && i + 1 < raw.Length && raw[i + 1] == '"') { sb.Append('"'); i++; continue; }

                sb.Append(c);
            }

            return sb.ToString();
        }
        private bool TryGetRawMultiLineEndLength(int pos, out int length)
        {
            length = 0;

            if (!_is.IsRaw || !_is.IsRawMultiLine)
                return false;

            if (pos >= _text.Length)
                return false;

            char c = _text[pos];
            if (!IsNewLineChar(c))
                return false;

            int p = pos;

            // newline length
            if (c == '\r' && (p + 1) < _text.Length && _text[p + 1] == '\n')
                p += 2;
            else
                p += 1;

            // indentafter newline
            while (p < _text.Length && IsWhitespaceButNotNewLine(_text[p]))
                p++;

            // quotes
            int run = 0;
            while ((p + run) < _text.Length && _text[p + run] == '"')
                run++;

            if (run < _is.QuoteCount)
                return false;

            p += _is.QuoteCount;

            // optional u8 suffix
            if ((p + 1) < _text.Length && (_text[p] == 'u' || _text[p] == 'U') && _text[p + 1] == '8')
                p += 2;

            length = p - pos;
            return true;
        }
        private SyntaxToken ReadTokenCore(SyntaxTrivia[] leadingTrivia)
        {
            int start = _pos;

            if (IsAtEnd())
                return new SyntaxToken(SyntaxKind.EndOfFileToken, new TextSpan(_pos, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());

            char c = Current();

            _atStartOfLine = false;

            // Strings / chars / interpolated / raw
            if (c == '\'')
                return ReadCharLiteral(start, leadingTrivia);

            if (c == '"' || c == '@' || c == '$')
            {
                var s = TryReadStringLike(start, leadingTrivia);
                if (s.Kind != SyntaxKind.BadToken || s.Span.Length > 0)
                    return s;
                // else fallthrough to operators
            }

            // Identifier / keyword
            if (c == '@' || c == '_' || c == '\\' || IsIdentifierStartChar(c))
                return ReadIdentifierOrKeyword(start, leadingTrivia);

            // Numeric literal
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
                return ReadNumberLiteral(start, leadingTrivia);

            // Operators / punctuators
            var op = ReadOperatorOrPunctuator(start, leadingTrivia);
            if (op.Kind != SyntaxKind.BadToken)
                return op;

            // Unknown
            Diagnostics.Add(new SyntaxDiagnostic(_pos, $"Unexpected character '{c}' (U+{((int)c):X4})."));
            _pos++;
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 1), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken ReadOperatorOrPunctuator(int start, SyntaxTrivia[] leadingTrivia)
        {
            foreach (var (text, kind) in SyntaxFacts.Operators)
            {
                if (Match(text))
                {
                    _pos += text.Length;
                    return new SyntaxToken(kind, new TextSpan(start, text.Length), text, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
                }
            }
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken ReadIdentifierOrKeyword(int start, SyntaxTrivia[] leadingTrivia)
        {
            bool isVerbatim = false;

            if (Current() == '@')
            {
                // @identifier
                if (Peek(1) == '"')
                {
                    // handled by string literal reader
                }
                else
                {
                    isVerbatim = true;
                    _pos++;
                }
            }

            StringBuilder? sb = null;
            int valueStart = _pos;

            // First char
            if (!TryConsumeIdentifierPart(ref sb, valueStart, first: true))
            {
                // verbatim without valid identifier start
                int len = Math.Max(1, _pos - start);
                Diagnostics.Add(new SyntaxDiagnostic(start, "Invalid identifier start."));
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, len), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
            }

            // Rest
            while (TryConsumeIdentifierPart(ref sb, valueStart, first: false))
            {
            }

            int end = _pos;
            var span = new TextSpan(start, end - start);

            bool hadUnicodeEscapes = sb != null;

            string valueText = hadUnicodeEscapes
                ? sb!.ToString()
                : _text.Substring(valueStart, end - valueStart);


            if (!isVerbatim)
            {
                if (SyntaxFacts.TryGetReservedKeyword(valueText, out var reserved))
                {
                    object? kwValue = reserved switch
                    {
                        SyntaxKind.TrueKeyword => true,
                        SyntaxKind.FalseKeyword => false,
                        SyntaxKind.NullKeyword => null,
                        _ => null
                    };

                    return new SyntaxToken(reserved, span, valueText, kwValue, leadingTrivia, Array.Empty<SyntaxTrivia>());
                }

                if (SyntaxFacts.TryGetContextualKeyword(valueText, out var contextual))
                {
                    return new SyntaxToken(
                        SyntaxKind.IdentifierToken,
                        contextual,
                        span,
                        valueText,
                        null,
                        leadingTrivia,
                        Array.Empty<SyntaxTrivia>());
                }
            }

            // Identifiers expose their decoded name
            return new SyntaxToken(SyntaxKind.IdentifierToken, span, valueText, valueText, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private bool TryConsumeIdentifierPart(ref StringBuilder? sb, int valueStart, bool first)
        {
            if (IsAtEnd())
                return false;

            if (Current() == '\\' && (Peek(1) == 'u' || Peek(1) == 'U'))
            {
                if (TryReadUnicodeEscape(out var escaped, out var consumed))
                {
                    if (first)
                    {
                        if (!IsIdentifierStartChar(escaped) && escaped != '_')
                            return false;
                    }
                    else
                    {
                        if (!IsIdentifierPartChar(escaped) && escaped != '_')
                            return false;
                    }

                    EnsureSb(ref sb, valueStart);
                    sb!.Append(escaped);
                    _pos += consumed;
                    return true;
                }

                return false;
            }

            char c = Current();
            if (first)
            {
                if (!(c == '_' || IsIdentifierStartChar(c)))
                    return false;
            }
            else
            {
                if (!(c == '_' || IsIdentifierPartChar(c)))
                    return false;
            }

            if (sb != null)
                sb.Append(c);

            _pos++;
            return true;
        }

        private void EnsureSb(ref StringBuilder? sb, int valueStart)
        {
            if (sb != null)
                return;

            sb = new StringBuilder(capacity: 32);

            int prefixLen = _pos - valueStart;
            if (prefixLen > 0)
                sb.Append(_text, valueStart, prefixLen);
        }
        private SyntaxToken ReadNumberLiteral(int start, SyntaxTrivia[] leadingTrivia)
        {
            bool isReal = false;

            if (Current() == '.')
            {
                isReal = true;
                _pos++; // consume dot
                ReadDigitsWithSeparators(baseKind: 10, requireAtLeastOneDigit: true);
                ReadExponentPartIfAny();
                ReadRealSuffixIfAny();
                goto ComputeTokenValue;
            }

            // base prefix?
            if (Current() == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
            {
                _pos += 2;
                ReadDigitsWithSeparators(baseKind: 16, requireAtLeastOneDigit: true);
                ReadIntegerSuffixIfAny();
                goto ComputeTokenValue;
            }

            if (Current() == '0' && (Peek(1) == 'b' || Peek(1) == 'B'))
            {
                _pos += 2;
                ReadDigitsWithSeparators(baseKind: 2, requireAtLeastOneDigit: true);
                ReadIntegerSuffixIfAny();
                goto ComputeTokenValue;
            }

            // decimal / real
            ReadDigitsWithSeparators(baseKind: 10, requireAtLeastOneDigit: true);

            if (Current() == '.' && char.IsDigit(Peek(1)))
            {
                isReal = true;
                _pos++; // skip dot
                ReadDigitsWithSeparators(baseKind: 10, requireAtLeastOneDigit: true);
            }

            if (ReadExponentPartIfAny())
                isReal = true;

            if (!isReal && !IsAtEnd())
            {
                char s = Current();
                if (s == 'f' || s == 'F' || s == 'd' || s == 'D' || s == 'm' || s == 'M')
                    isReal = true;
            }

            if (isReal)
                ReadRealSuffixIfAny();
            else
                ReadIntegerSuffixIfAny();

        ComputeTokenValue:

            var span = new TextSpan(start, _pos - start);
            var text = _text.AsSpan(span.Start, span.Length);
            if (!TryComputeNumericValue(text, out var val, out var vt, out var err))
            {
                if (err != null)
                    Diagnostics.Add(new SyntaxDiagnostic(start, err));

                return new SyntaxToken(SyntaxKind.NumericLiteralToken, span, null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
            }

            return new SyntaxToken(SyntaxKind.NumericLiteralToken, span, vt, val, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private void ReadDigitsWithSeparators(int baseKind, bool requireAtLeastOneDigit)
        {
            int digits = 0;
            bool lastWasUnderscore = false;

            while (!IsAtEnd())
            {
                char c = Current();
                if (c == '_')
                {
                    // underscore must be between digits
                    if (digits == 0 || lastWasUnderscore)
                        break;
                    lastWasUnderscore = true;
                    _pos++;
                    continue;
                }

                bool isDigit = baseKind switch
                {
                    10 => char.IsDigit(c),
                    16 => IsHexDigit(c),
                    2 => (c == '0' || c == '1'),
                    _ => false
                };

                if (!isDigit)
                    break;

                digits++;
                lastWasUnderscore = false;
                _pos++;
            }

            // Trailing underscore
            if (lastWasUnderscore)
            {
                // rewind underscore
                _pos--;
                Diagnostics.Add(new SyntaxDiagnostic(_pos, "Trailing '_' in numeric literal."));
            }

            if (requireAtLeastOneDigit && digits == 0)
                Diagnostics.Add(new SyntaxDiagnostic(_pos, "Expected digits in numeric literal."));
        }
        private bool ReadExponentPartIfAny()
        {
            int save = _pos;
            if (IsAtEnd())
                return false;

            char c = Current();
            if (c != 'e' && c != 'E')
                return false;

            _pos++; // skip e/E

            if (Current() == '+' || Current() == '-')
                _pos++;

            int beforeDigits = _pos;
            ReadDigitsWithSeparators(baseKind: 10, requireAtLeastOneDigit: true);
            if (_pos == beforeDigits)
            {
                _pos = save;
                return false;
            }
            return true;
        }
        private void ReadRealSuffixIfAny()
        {
            if (IsAtEnd())
                return;

            char c = Current();
            if (c == 'f' || c == 'F' || c == 'd' || c == 'D' || c == 'm' || c == 'M')
            {
                _pos++;
            }
        }
        private void ReadIntegerSuffixIfAny()
        {
            int save = _pos;
            if (IsAtEnd())
                return;

            char c1 = Current();
            char c2 = Peek(1);

            bool isU1 = (c1 == 'u' || c1 == 'U');
            bool isL1 = (c1 == 'l' || c1 == 'L');

            if (isU1 && (c2 == 'l' || c2 == 'L'))
            {
                _pos += 2;
                return;
            }
            if (isL1 && (c2 == 'u' || c2 == 'U'))
            {
                _pos += 2;
                return;
            }
            if (isU1 || isL1)
            {
                _pos += 1;
                return;
            }

            _pos = save;
        }
        private SyntaxToken TryReadStringLike(int start, SyntaxTrivia[] leadingTrivia)
        {
            int p = _pos;

            // Interpolated raw
            if (Current() == '$')
            {
                int dollars = CountWhile('$');
                if (Peek(dollars) == '"' && Peek(dollars + 1) == '"' && Peek(dollars + 2) == '"')
                {
                    _pos += dollars;
                    return ReadRawStringLiteral(start, leadingTrivia);
                }

                // Regular interpolated
                if (dollars == 1)
                {
                    if (Peek(1) == '"')
                        return ReadRegularStringLiteral(start, leadingTrivia, isInterpolated: true, isVerbatim: false);

                    if (Peek(1) == '@' && Peek(2) == '"')
                    {
                        _pos += 2; // consume $@
                        return ReadVerbatimStringLiteral(start, leadingTrivia, isInterpolated: true);
                    }
                }

                // Not a string start
                _pos = p;
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
            }

            // Verbatim interpolated
            if (Current() == '@' && Peek(1) == '$' && Peek(2) == '"')
            {
                _pos += 3; // consume @$"
                return ReadVerbatimStringLiteral(start, leadingTrivia, isInterpolated: true);
            }

            // Verbatim
            if (Current() == '@' && Peek(1) == '"')
            {
                _pos += 2; // consume @"
                return ReadVerbatimStringLiteral(start, leadingTrivia, isInterpolated: false);
            }

            // Raw
            if (Current() == '"' && Peek(1) == '"' && Peek(2) == '"')
            {
                return ReadRawStringLiteral(start, leadingTrivia);
            }

            // Regular
            if (Current() == '"')
                return ReadRegularStringLiteral(start, leadingTrivia, isInterpolated: false, isVerbatim: false);

            _pos = p;
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken ReadRegularStringLiteral(int start, SyntaxTrivia[] leadingTrivia, bool isInterpolated, bool isVerbatim)
        {
            if (Current() != '"')
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());

            _pos++; // opening "
            while (!IsAtEnd())
            {
                char c = Current();
                if (c == '"')
                {
                    _pos++; // closing "
                    // optional u8 suffix
                    bool isU8 = TryConsumeU8Suffix();
                    var kind = isU8 ? SyntaxKind.Utf8StringLiteralToken
                                    : (SyntaxKind.StringLiteralToken);
                    var span = new TextSpan(start, _pos - start);
                    var text = _text.AsSpan(span.Start, span.Length);
                    string? vt = null;
                    object? val = null;

                    if (TryComputeStringValue(text, out var s) || TryComputeRawStringValue(text, out s))
                    {
                        vt = s;
                        val = s;
                    }
                    else
                    {
                        Diagnostics.Add(new SyntaxDiagnostic(start, "String literal value could not be computed."));
                    }

                    return new SyntaxToken(kind, span, vt, val, leadingTrivia, Array.Empty<SyntaxTrivia>());
                }
                if (IsNewLineChar(c))
                {
                    Diagnostics.Add(new SyntaxDiagnostic(_pos, "Newline in regular string literal."));
                    break;
                }

                if (c == '\\')
                {
                    // escape
                    _pos++;
                    if (!IsAtEnd())
                    {
                        if (Current() == 'u')
                        {
                            _pos++;
                            ConsumeHexDigits(4);
                        }
                        else if (Current() == 'U')
                        {
                            _pos++;
                            ConsumeHexDigits(8);
                        }
                        else if (Current() == 'x')
                        {
                            _pos++;
                            ConsumeHexDigitsVariable(minCount: 1, maxCount: 4);
                        }
                        else
                        {
                            _pos++; // simple escape
                        }
                    }
                    continue;
                }

                _pos++;
            }
            Diagnostics.Add(new SyntaxDiagnostic(start, "Unterminated string literal."));
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, _pos - start), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken ReadVerbatimStringLiteral(int start, SyntaxTrivia[] leadingTrivia, bool isInterpolated)
        {
            while (!IsAtEnd())
            {
                char c = Current();
                if (c == '"')
                {
                    if (Peek(1) == '"')
                    {
                        // doubled quote inside
                        _pos += 2;
                        continue;
                    }

                    _pos++; // closing "
                    bool isU8 = TryConsumeU8Suffix();
                    var kind = isU8 ? SyntaxKind.Utf8StringLiteralToken
                                    : SyntaxKind.StringLiteralToken;

                    // Compute decoded value
                    var span = new TextSpan(start, _pos - start);
                    var text = _text.AsSpan(span.Start, span.Length);

                    string? vt = null;
                    object? val = null;
                    if (TryComputeStringValue(text, out var s) || TryComputeRawStringValue(text, out s))
                    {
                        vt = s;
                        val = s;
                    }
                    else
                    {
                        Diagnostics.Add(new SyntaxDiagnostic(start, "Verbatim string literal value could not be computed."));
                    }

                    return new SyntaxToken(kind, span, vt, val, leadingTrivia, Array.Empty<SyntaxTrivia>());
                }

                _pos++;
            }

            Diagnostics.Add(new SyntaxDiagnostic(start, "Unterminated verbatim string literal."));
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, _pos - start), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxToken ReadRawStringLiteral(int start, SyntaxTrivia[] leadingTrivia)
        {
            int quoteCount = CountWhileFrom(_pos, '"');
            if (quoteCount < 3)
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, 0), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());

            _pos += quoteCount; // skip opening quotes
            bool isMultiLine = false;
            {
                int p = _pos;
                while (p < _text.Length && IsWhitespaceButNotNewLine(_text[p]))
                    p++;
                if (p < _text.Length && IsNewLineChar(_text[p]))
                    isMultiLine = true;
            }

            while (!IsAtEnd())
            {
                if (Current() == '"')
                {
                    int run = CountWhileFrom(_pos, '"');
                    if (run >= quoteCount)
                    {
                        _pos += quoteCount; // consume closing quotes
                        bool isU8 = TryConsumeU8Suffix();
                        SyntaxKind kind;
                        if (isU8)
                            kind = isMultiLine ? SyntaxKind.Utf8MultiLineRawStringLiteralToken
                                : SyntaxKind.Utf8SingleLineRawStringLiteralToken;
                        else
                            kind = isMultiLine ? SyntaxKind.MultiLineRawStringLiteralToken
                                : SyntaxKind.SingleLineRawStringLiteralToken;

                        var span = new TextSpan(start, _pos - start);
                        var text = _text.AsSpan(span.Start, span.Length);

                        string? vt = null;
                        object? val = null;

                        if (TryComputeRawStringValue(text, out var s))
                        {
                            vt = s;
                            val = s;
                        }
                        else
                        {
                            Diagnostics.Add(new SyntaxDiagnostic(start, "Raw string literal value could not be computed."));
                        }

                        return new SyntaxToken(kind, span, vt, val, leadingTrivia, Array.Empty<SyntaxTrivia>());
                    }
                }
                _pos++;
            }

            Diagnostics.Add(new SyntaxDiagnostic(start, "Unterminated raw string literal."));
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, _pos - start), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private bool TryConsumeU8Suffix()
        {
            if (IsAtEnd())
                return false;

            if ((Current() == 'u' || Current() == 'U') && Peek(1) == '8')
            {
                _pos += 2;
                return true;
            }
            return false;
        }
        private SyntaxToken ReadCharLiteral(int start, SyntaxTrivia[] leadingTrivia)
        {
            _pos++; // opening '
            if (IsAtEnd() || IsNewLineChar(Current()))
            {
                Diagnostics.Add(new SyntaxDiagnostic(start, "Unterminated character literal."));
                return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, _pos - start), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
            }

            if (Current() == '\\')
            {
                _pos++;
                if (!IsAtEnd())
                {
                    if (Current() == 'u')
                    {
                        _pos++;
                        ConsumeHexDigits(4);
                    }
                    else if (Current() == 'U')
                    {
                        _pos++;
                        ConsumeHexDigits(8);
                    }
                    else if (Current() == 'x')
                    {
                        _pos++;
                        ConsumeHexDigitsVariable(minCount: 1, maxCount: 4);
                    }
                    else
                    {
                        _pos++; // simple escape
                    }
                }
            }
            else
            {
                _pos++; // one char
            }

            if (Current() == '\'')
            {
                _pos++; // closing '
                var span = new TextSpan(start, _pos - start);
                var text = _text.AsSpan(span.Start, span.Length);

                if (TryComputeCharValue(text, out var ch))
                    return new SyntaxToken(SyntaxKind.CharacterLiteralToken, span, ch.ToString(), ch, leadingTrivia, Array.Empty<SyntaxTrivia>());

                Diagnostics.Add(new SyntaxDiagnostic(start, "Invalid character literal."));
                return new SyntaxToken(SyntaxKind.CharacterLiteralToken, span, null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
            }

            Diagnostics.Add(new SyntaxDiagnostic(start, "Invalid or unterminated character literal."));
            while (!IsAtEnd() && !IsNewLineChar(Current()) && Current() != '\'')
                _pos++;
            if (Current() == '\'')
                _pos++;
            return new SyntaxToken(SyntaxKind.BadToken, new TextSpan(start, _pos - start), null, null, leadingTrivia, Array.Empty<SyntaxTrivia>());
        }
        private SyntaxTrivia[] ReadTrivia(bool isLeading)
        {
            var list = new List<SyntaxTrivia>(capacity: 8);

            while (!IsAtEnd())
            {
                int start = _pos;
                char c = Current();

                // New line
                if (IsNewLineChar(c))
                {
                    int len = ConsumeNewLine();
                    list.Add(new SyntaxTrivia(TriviaKind.EndOfLineTrivia, new TextSpan(start, len)));
                    _atStartOfLine = true;

                    if (!isLeading)
                        break;

                    continue;
                }
                // Preprocessor directive
                if (_atStartOfLine && IsWhitespaceButNotNewLine(c))
                {
                    // consume whitespace
                    int wsStart = _pos;
                    while (!IsAtEnd() && IsWhitespaceButNotNewLine(Current()))
                        _pos++;
                    list.Add(new SyntaxTrivia(TriviaKind.WhitespaceTrivia, new TextSpan(wsStart, _pos - wsStart)));
                    continue;
                }

                if (_atStartOfLine && Current() == '#')
                {
                    int dirStart = _pos;
                    while (!IsAtEnd() && !IsNewLineChar(Current()))
                        _pos++;
                    list.Add(new SyntaxTrivia(TriviaKind.PreprocessorDirectiveTrivia, new TextSpan(dirStart, _pos - dirStart)));
                    _atStartOfLine = false;
                    continue;
                }
                // Whitespace
                if (IsWhitespaceButNotNewLine(c))
                {
                    int wsStart = _pos;
                    while (!IsAtEnd() && IsWhitespaceButNotNewLine(Current()))
                        _pos++;
                    list.Add(new SyntaxTrivia(TriviaKind.WhitespaceTrivia, new TextSpan(wsStart, _pos - wsStart)));
                    continue;
                }
                // Comments
                if (c == '/' && Peek(1) == '/')
                {
                    TriviaKind kind = (Peek(2) == '/') ? TriviaKind.SingleLineDocCommentTrivia : TriviaKind.SingleLineCommentTrivia;
                    _pos += 2;
                    while (!IsAtEnd() && !IsNewLineChar(Current()))
                        _pos++;
                    list.Add(new SyntaxTrivia(kind, new TextSpan(start, _pos - start)));
                    _atStartOfLine = false;

                    continue;
                }
                if (c == '/' && Peek(1) == '*')
                {
                    TriviaKind kind = (Peek(2) == '*') ? TriviaKind.MultiLineDocCommentTrivia : TriviaKind.MultiLineCommentTrivia;
                    _pos += 2;
                    while (!IsAtEnd())
                    {
                        if (Current() == '*' && Peek(1) == '/')
                        {
                            _pos += 2;
                            break;
                        }
                        _pos++;
                    }
                    list.Add(new SyntaxTrivia(kind, new TextSpan(start, _pos - start)));
                    _atStartOfLine = false;
                    continue;
                }

                // Not trivia
                break;
            }
            return list.Count == 0 ? Array.Empty<SyntaxTrivia>() : list.ToArray();
        }
        private bool TryComputeNumericValue(ReadOnlySpan<char> tokenText, out object? value, out string? valueText, out string? error)
        {
            value = null;
            valueText = null;
            error = null;

            if (tokenText.IsEmpty)
            {
                error = "Empty numeric literal.";
                return false;
            }
            ReadOnlySpan<char> core = tokenText;
            ReadOnlySpan<char> suffix = default;

            bool startsWith0 = tokenText.Length >= 2 && tokenText[0] == '0';
            bool isHexLiteral = startsWith0 && (tokenText[1] == 'x' || tokenText[1] == 'X');
            bool isBinaryLiteral = startsWith0 && (tokenText[1] == 'b' || tokenText[1] == 'B');
            bool isNonDecimalIntegerLiteral = isHexLiteral || isBinaryLiteral;

            // Split suffix
            if (tokenText.Length >= 2)
            {
                char a = ToLowerAscii(tokenText[^2]);
                char b = ToLowerAscii(tokenText[^1]);

                if ((a == 'u' && b == 'l') || (a == 'l' && b == 'u'))
                {
                    suffix = tokenText.Slice(tokenText.Length - 2, 2);
                    core = tokenText.Slice(0, tokenText.Length - 2);
                }
                else if (a == 'u' && b == 'n')
                {
                    error = "Native integer suffix 'un' is not supported.";
                    return false;
                }
            }
            if (suffix.IsEmpty && tokenText.Length >= 1)
            {
                char b = ToLowerAscii(tokenText[^1]);
                if (b is 'u' or 'l')
                {
                    suffix = tokenText.Slice(tokenText.Length - 1, 1);
                    core = tokenText.Slice(0, tokenText.Length - 1);
                }
                else if (!isNonDecimalIntegerLiteral && b is 'f' or 'd' or 'm')
                {
                    suffix = tokenText.Slice(tokenText.Length - 1, 1);
                    core = tokenText.Slice(0, tokenText.Length - 1);
                }
                else if (b == 'n')
                {
                    error = "Native integer suffix 'n' is not supported.";
                    return false;
                }
            }

            bool hasDot = false;
            bool hasExp = false;

            for (int i = 0; i < core.Length; i++)
            {
                char c = core[i];
                if (c == '.')
                {
                    hasDot = true;
                }
                else if (!isNonDecimalIntegerLiteral && (c == 'e' || c == 'E'))
                {
                    hasExp = true;
                }
            }

            bool isRealSuffix =
                !isNonDecimalIntegerLiteral &&
                suffix.Length == 1 &&
                (ToLowerAscii(suffix[0]) is 'f' or 'd' or 'm');

            bool isReal =
                !isNonDecimalIntegerLiteral &&
                (hasDot || hasExp || isRealSuffix);

            if (isReal)
                return TryComputeReal(core, suffix, out value, out valueText, out error);

            return TryComputeInteger(core, suffix, out value, out valueText, out error);

            static char ToLowerAscii(char c) => (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c;
            static bool TryComputeReal(ReadOnlySpan<char> core, ReadOnlySpan<char> suffix, out object? value, out string? valueText, out string? error)
            {
                value = null;
                valueText = null;
                error = null;

                string cleaned = RemoveUnderscores(core);

                char sfx = suffix.Length == 1 ? ToLowerAscii(suffix[0]) : '\0';

                if (sfx == 'm')
                {
                    if (!decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
                    {
                        error = "Invalid decimal literal.";
                        return false;
                    }
                    value = dec;
                    valueText = dec.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                if (sfx == 'f')
                {
                    if (!float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    {
                        error = "Invalid float literal.";
                        return false;
                    }
                    value = f;
                    valueText = f.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }

                // default double
                if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    error = "Invalid double literal.";
                    return false;
                }
                value = d;
                valueText = d.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }
            static bool TryComputeInteger(ReadOnlySpan<char> core, ReadOnlySpan<char> suffix, out object? value, out string? valueText, out string? error)
            {
                value = null;
                valueText = null;
                error = null;

                int numberBase = 10;
                ReadOnlySpan<char> digits = core;

                if (digits.Length >= 2 && digits[0] == '0' && (digits[1] == 'x' || digits[1] == 'X'))
                {
                    numberBase = 16;
                    digits = digits.Slice(2);
                }
                else if (digits.Length >= 2 && digits[0] == '0' && (digits[1] == 'b' || digits[1] == 'B'))
                {
                    numberBase = 2;
                    digits = digits.Slice(2);
                }

                string cleanedDigits = RemoveUnderscores(digits);
                if (cleanedDigits.Length == 0)
                {
                    error = "Expected digits in numeric literal.";
                    return false;
                }

                if (!TryParseUnsignedBigInteger(cleanedDigits.AsSpan(), numberBase, out var bi))
                {
                    error = "Invalid digits for numeric base.";
                    return false;
                }

                // Suffix dispatch
                if (suffix.Length == 1)
                {
                    char sfx = ToLowerAscii(suffix[0]);
                    if (sfx == 'u')
                    {
                        if (bi <= uint.MaxValue)
                            value = (uint)bi;
                        else if (bi <= ulong.MaxValue)
                            value = (ulong)bi;
                        else { error = "Integer literal is too large (unsigned)."; return false; }
                    }
                    else if (sfx == 'l')
                    {
                        if (bi <= long.MaxValue)
                            value = (long)bi;
                        else if (bi <= ulong.MaxValue)
                            value = (ulong)bi;
                        else
                        {
                            error = "Integer literal is too large (long/ulong).";
                            return false;
                        }
                    }
                    else
                    {
                        error = "Unsupported integer suffix.";
                        return false;
                    }
                }
                else if (suffix.Length == 2)
                {
                    char a = ToLowerAscii(suffix[0]);
                    char b = ToLowerAscii(suffix[1]);
                    if ((a == 'u' && b == 'l') || (a == 'l' && b == 'u'))
                    {
                        if (bi <= ulong.MaxValue)
                            value = (ulong)bi;
                        else { error = "Integer literal is too large (ulong)."; return false; }
                    }
                    else
                    {
                        error = "Unsupported integer suffix.";
                        return false;
                    }
                }
                else
                {
                    if (bi <= int.MaxValue)
                        value = (int)bi;
                    else if (bi <= uint.MaxValue)
                        value = (uint)bi;
                    else if (bi <= long.MaxValue)
                        value = (long)bi;
                    else if (bi <= ulong.MaxValue)
                        value = (ulong)bi;
                    else { error = "Integer literal is too large."; return false; }
                }

                valueText = ((IFormattable)value!).ToString(null, CultureInfo.InvariantCulture);
                return true;
            }
            static bool TryParseUnsignedBigInteger(ReadOnlySpan<char> s, int numberBase, out BigInteger value)
            {
                value = BigInteger.Zero;

                for (int i = 0; i < s.Length; i++)
                {
                    int d = DigitValue(s[i]);
                    if (d < 0 || d >= numberBase)
                        return false;

                    value = value * numberBase + d;
                }

                return true;

                static int DigitValue(char c)
                {
                    if (c >= '0' && c <= '9') return c - '0';
                    if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
                    if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
                    return -1;
                }
            }
            static string RemoveUnderscores(ReadOnlySpan<char> s)
            {
                int idx = s.IndexOf('_');
                if (idx < 0)
                    return s.ToString();

                char[] tmp = new char[s.Length];
                int n = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c != '_')
                        tmp[n++] = c;
                }
                return new string(tmp, 0, n);
            }
        }
        private static bool TryComputeRawStringValue(ReadOnlySpan<char> tokenText, out string value)
        {
            value = string.Empty;

            // Strip optional u8 suffix
            if (tokenText.Length >= 2 && (tokenText[^2] == 'u' || tokenText[^2] == 'U') && tokenText[^1] == '8')
                tokenText = tokenText.Slice(0, tokenText.Length - 2);

            if (tokenText.Length < 6)
                return false;

            int quoteCount = 0;
            while (quoteCount < tokenText.Length && tokenText[quoteCount] == '"')
                quoteCount++;

            if (quoteCount < 3)
                return false;

            for (int i = 0; i < quoteCount; i++)
            {
                if (tokenText[tokenText.Length - 1 - i] != '"')
                    return false;
            }

            var inner = tokenText.Slice(quoteCount, tokenText.Length - 2 * quoteCount);

            bool hasNewLine = false;
            for (int i = 0; i < inner.Length; i++)
            {
                if (IsNewLineChar(inner[i])) { hasNewLine = true; break; }
            }
            if (!hasNewLine)
            {
                value = inner.ToString();
                return true;
            }

            int i0 = 0;

            while (i0 < inner.Length && IsWhitespaceButNotNewLine(inner[i0]))
                i0++;

            if (i0 < inner.Length && IsNewLineChar(inner[i0]))
                i0 += ConsumeNewLineSpan(inner, i0);

            int lastNl = -1;
            for (int i = inner.Length - 1; i >= i0; i--)
            {
                if (IsNewLineChar(inner[i]))
                {
                    lastNl = i;
                    break;
                }
            }
            if (lastNl < 0)
                return false;

            int lastNlLen = ConsumeNewLineSpan(inner, lastNl);
            int indentStart = lastNl + lastNlLen;

            var indent = inner.Slice(indentStart);
            for (int i = 0; i < indent.Length; i++)
            {
                if (!IsWhitespaceButNotNewLine(indent[i]))
                    return false;
            }

            var contentPart = inner.Slice(i0, lastNl - i0);

            var sb = new StringBuilder(contentPart.Length);

            int p = 0;
            bool atLineStart = true;

            while (p < contentPart.Length)
            {
                if (atLineStart && indent.Length > 0)
                {
                    if (contentPart.Slice(p).StartsWith(indent, StringComparison.Ordinal))
                    {
                        p += indent.Length;
                    }
                    else
                    {
                        if (!IsNewLineChar(contentPart[p]))
                            return false;
                    }
                }

                char c = contentPart[p];
                sb.Append(c);

                if (IsNewLineChar(c))
                {
                    p += ConsumeNewLineSpan(contentPart, p);
                    atLineStart = true;
                    continue;
                }

                atLineStart = false;
                p++;
            }

            value = sb.ToString();
            return true;

            static int ConsumeNewLineSpan(ReadOnlySpan<char> s, int pos)
            {
                if (pos < s.Length && s[pos] == '\r' && pos + 1 < s.Length && s[pos + 1] == '\n')
                    return 2;
                return 1;
            }
        }

        private static bool TryComputeCharValue(ReadOnlySpan<char> tokenText, out char value)
        {
            value = default;

            if (tokenText.Length < 3 || tokenText[0] != '\'' || tokenText[^1] != '\'')
                return false;

            var inner = tokenText.Slice(1, tokenText.Length - 2);

            if (inner.Length == 1)
            {
                value = inner[0];
                return true;
            }

            if (inner.Length >= 2 && inner[0] == '\\')
            {
                int i = 1;
                if (!TryDecodeEscape(inner, ref i, allowSurrogatePairInString: false, out var decoded))
                    return false;

                if (decoded.Length != 1)
                    return false;

                value = decoded[0];
                return i == inner.Length;
            }

            return false;
        }

        private static bool TryComputeStringValue(ReadOnlySpan<char> tokenText, out string value)
        {
            value = string.Empty;

            // Strip optional u8 suffix
            if (tokenText.Length >= 2 && (tokenText[^2] == 'u' || tokenText[^2] == 'U') && tokenText[^1] == '8')
                tokenText = tokenText.Slice(0, tokenText.Length - 2);

            // Strip optional $
            while (tokenText.Length > 0 && tokenText[0] == '$')
                tokenText = tokenText.Slice(1);

            bool verbatim = false;

            // @"..."
            if (tokenText.Length >= 2 && tokenText[0] == '@' && tokenText[1] == '"')
            {
                verbatim = true;
                tokenText = tokenText.Slice(1);
            }

            // raw strings
            if (tokenText.Length >= 3 && tokenText[0] == '"' && tokenText[1] == '"' && tokenText[2] == '"')
                return false;

            if (tokenText.Length < 2 || tokenText[0] != '"' || tokenText[^1] != '"')
                return false;

            var inner = tokenText.Slice(1, tokenText.Length - 2);

            if (verbatim)
            {
                // "" -> "
                var sb = new StringBuilder(inner.Length);
                for (int i = 0; i < inner.Length; i++)
                {
                    char c = inner[i];
                    if (c == '"' && i + 1 < inner.Length && inner[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                value = sb.ToString();
                return true;
            }
            else
            {
                var sb = new StringBuilder(inner.Length);
                for (int i = 0; i < inner.Length; i++)
                {
                    char c = inner[i];
                    if (c == '\\')
                    {
                        i++;
                        if (!TryDecodeEscape(inner, ref i, allowSurrogatePairInString: true, out var decoded))
                            return false;

                        sb.Append(decoded);
                        i--;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                value = sb.ToString();
                return true;
            }
        }
        private void ConsumeHexDigitsVariable(int minCount, int maxCount)
        {
            int firstDigitPos = _pos;
            int count = 0;

            while (count < maxCount && IsHexDigit(Current()))
            {
                _pos++;
                count++;
            }

            if (count < minCount)
                Diagnostics.Add(new SyntaxDiagnostic(firstDigitPos, "Invalid hex digit in escape sequence."));
        }
        private static bool TryDecodeEscape(ReadOnlySpan<char> s, ref int i, bool allowSurrogatePairInString, out string decoded)
        {
            decoded = string.Empty;
            if ((uint)i >= (uint)s.Length) return false;

            char c = s[i++];
            switch (c)
            {
                case '\'': decoded = "'"; return true;
                case '"': decoded = "\""; return true;
                case '\\': decoded = "\\"; return true;
                case '0': decoded = "\0"; return true;
                case 'a': decoded = "\a"; return true;
                case 'b': decoded = "\b"; return true;
                case 'f': decoded = "\f"; return true;
                case 'n': decoded = "\n"; return true;
                case 'r': decoded = "\r"; return true;
                case 't': decoded = "\t"; return true;
                case 'v': decoded = "\v"; return true;

                case 'x':
                    {
                        // 1-4 hex digits
                        int val = 0, digits = 0;
                        while (digits < 4 && (uint)i < (uint)s.Length)
                        {
                            int h = Hex(s[i]);
                            if (h < 0) break;
                            val = (val << 4) | h;
                            i++; digits++;
                        }
                        if (digits == 0) return false;
                        decoded = ((char)val).ToString();
                        return true;
                    }

                case 'u':
                    {
                        if (i + 4 > s.Length) return false;
                        int val = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int h = Hex(s[i++]);
                            if (h < 0) return false;
                            val = (val << 4) | h;
                        }
                        decoded = ((char)val).ToString();
                        return true;
                    }

                case 'U':
                    {
                        if (i + 8 > s.Length) return false;
                        int code = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            int h = Hex(s[i++]);
                            if (h < 0) return false;
                            code = (code << 4) | h;
                        }

                        if (code <= 0xFFFF)
                        {
                            decoded = ((char)code).ToString();
                            return true;
                        }

                        if (!allowSurrogatePairInString || code > 0x10FFFF)
                            return false;

                        code -= 0x10000;
                        char hi = (char)(0xD800 + (code >> 10));
                        char lo = (char)(0xDC00 + (code & 0x3FF));
                        decoded = new string(new[] { hi, lo });
                        return true;
                    }

                default:
                    return false;
            }

            static int Hex(char ch)
            {
                if (ch >= '0' && ch <= '9') return ch - '0';
                if (ch >= 'a' && ch <= 'f') return 10 + (ch - 'a');
                if (ch >= 'A' && ch <= 'F') return 10 + (ch - 'A');
                return -1;
            }
        }

        #region Helpers
        private bool IsAtEnd() => _pos >= _text.Length;
        private char Current() => _pos < _text.Length ? _text[_pos] : '\0';
        private char Peek(int offset) => (_pos + offset) < _text.Length ? _text[_pos + offset] : '\0';

        private bool Match(string s)
        {
            if (_pos + s.Length > _text.Length)
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (_text[_pos + i] != s[i])
                    return false;
            }
            return true;
        }

        private int CountWhile(char ch)
        {
            int i = 0;
            while ((_pos + i) < _text.Length && _text[_pos + i] == ch)
                i++;
            return i;
        }

        private int CountWhileFrom(int pos, char ch)
        {
            int i = 0;
            while ((pos + i) < _text.Length && _text[pos + i] == ch)
                i++;
            return i;
        }

        private static bool IsWhitespaceButNotNewLine(char c)
            => char.IsWhiteSpace(c) && !IsNewLineChar(c);

        private static bool IsNewLineChar(char c)
            => c == '\r' || c == '\n' || c == '\u0085' || c == '\u2028' || c == '\u2029';

        private int ConsumeNewLine()
        {
            char c = Current();
            if (c == '\r' && Peek(1) == '\n')
            {
                _pos += 2;
                return 2;
            }
            _pos++;
            return 1;
        }

        private static bool IsHexDigit(char c)
            => (c >= '0' && c <= '9') ||
               (c >= 'a' && c <= 'f') ||
               (c >= 'A' && c <= 'F');

        private void ConsumeHexDigits(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsHexDigit(Current()))
                {
                    Diagnostics.Add(new SyntaxDiagnostic(_pos, "Invalid hex digit in escape sequence."));
                    return;
                }
                _pos++;
            }
        }
        private bool TryReadUnicodeEscape(out char ch, out int consumed)
        {
            ch = '\0';
            consumed = 0;

            if (Current() != '\\')
                return false;

            char kind = Peek(1);
            if (kind != 'u' && kind != 'U')
                return false;

            int digits = (kind == 'u') ? 4 : 8;

            int val = 0;
            for (int i = 0; i < digits; i++)
            {
                char d = Peek(2 + i);
                int x;
                if (d >= '0' && d <= '9') x = d - '0';
                else if (d >= 'a' && d <= 'f') x = 10 + (d - 'a');
                else if (d >= 'A' && d <= 'F') x = 10 + (d - 'A');
                else return false;

                unchecked { val = (val << 4) | x; }
            }

            if (digits == 8 && val > 0xFFFF)
                ch = '\uFFFD';
            else
                ch = (char)val;

            consumed = 2 + digits;
            return true;
        }
        private static bool IsIdentifierStartChar(char c)
        {
            if (c == '_')
                return true;

            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.UppercaseLetter ||
                   cat == UnicodeCategory.LowercaseLetter ||
                   cat == UnicodeCategory.TitlecaseLetter ||
                   cat == UnicodeCategory.ModifierLetter ||
                   cat == UnicodeCategory.OtherLetter ||
                   cat == UnicodeCategory.LetterNumber;
        }

        private static bool IsIdentifierPartChar(char c)
        {
            if (IsIdentifierStartChar(c))
                return true;

            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.DecimalDigitNumber ||
                   cat == UnicodeCategory.ConnectorPunctuation ||
                   cat == UnicodeCategory.NonSpacingMark ||
                   cat == UnicodeCategory.SpacingCombiningMark ||
                   cat == UnicodeCategory.Format;
        }
        #endregion

        internal readonly struct Snapshot
        {
            public readonly int Pos;
            public readonly bool AtStartOfLine;

            public readonly LexMode Mode;

            // InterpolatedState
            public readonly bool IsVerbatim;
            public readonly bool IsRaw;
            public readonly bool IsRawMultiLine;
            public readonly int Dollars;
            public readonly int QuoteCount;

            public readonly int InterpBraceDepth;

            // Stack frames flattened
            public readonly FrameSnapshot[] Frames;

            public readonly int DiagnosticCount;

            public Snapshot(
                int pos,
                bool atStartOfLine,
                LexMode mode,
                bool isVerbatim,
                bool isRaw,
                bool isRawMultiLine,
                int dollars,
                int quoteCount,
                int interpBraceDepth,
                FrameSnapshot[] frames,
                int diagnosticCount)
            {
                Pos = pos;
                AtStartOfLine = atStartOfLine;
                Mode = mode;
                IsVerbatim = isVerbatim;
                IsRaw = isRaw;
                IsRawMultiLine = isRawMultiLine;
                Dollars = dollars;
                QuoteCount = quoteCount;
                InterpBraceDepth = interpBraceDepth;
                Frames = frames ?? Array.Empty<FrameSnapshot>();
                DiagnosticCount = diagnosticCount;
            }
        }
        internal readonly struct FrameSnapshot
        {
            public readonly LexMode Mode;
            public readonly bool IsVerbatim;
            public readonly bool IsRaw;
            public readonly bool IsRawMultiLine;
            public readonly int Dollars;
            public readonly int QuoteCount;
            public readonly int BraceDepth;

            public FrameSnapshot(LexMode mode, bool isVerbatim, bool isRaw, bool isRawMultiLine, int dollars, int quoteCount, int braceDepth)
            {
                Mode = mode;
                IsVerbatim = isVerbatim;
                IsRaw = isRaw;
                IsRawMultiLine = isRawMultiLine;
                Dollars = dollars;
                QuoteCount = quoteCount;
                BraceDepth = braceDepth;
            }
        }
        internal Snapshot CaptureSnapshot()
        {
            var frames = _modeStack.ToArray();
            var snap = new FrameSnapshot[frames.Length];

            for (int i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                snap[i] = new FrameSnapshot(
                    f.Mode,
                    f.State.IsVerbatim,
                    f.State.IsRaw,
                    f.State.IsRawMultiLine,
                    f.State.Dollars,
                    f.State.QuoteCount,
                    f.BraceDepth);
            }

            return new Snapshot(
                _pos,
                _atStartOfLine,
                _mode,
                _is.IsVerbatim,
                _is.IsRaw,
                _is.IsRawMultiLine,
                _is.Dollars,
                _is.QuoteCount,
                _interpBraceDepth,
                snap,
                Diagnostics.Count);
        }
        internal void RestoreSnapshot(Snapshot s)
        {
            _pos = s.Pos;
            _atStartOfLine = s.AtStartOfLine;
            _mode = (LexMode)s.Mode;

            _is = new InterpolatedState
            {
                IsVerbatim = s.IsVerbatim,
                IsRaw = s.IsRaw,
                IsRawMultiLine = s.IsRawMultiLine,
                Dollars = s.Dollars,
                QuoteCount = s.QuoteCount
            };

            _interpBraceDepth = s.InterpBraceDepth;

            _modeStack.Clear();

            for (int i = s.Frames.Length - 1; i >= 0; i--)
            {
                var f = s.Frames[i];
                var st = new InterpolatedState
                {
                    IsVerbatim = f.IsVerbatim,
                    IsRaw = f.IsRaw,
                    IsRawMultiLine = f.IsRawMultiLine,
                    Dollars = f.Dollars,
                    QuoteCount = f.QuoteCount
                };
                _modeStack.Push(new ModeFrame((LexMode)f.Mode, st, f.BraceDepth));
            }

            // Roll back diagnostics
            if (Diagnostics.Count > s.DiagnosticCount)
                Diagnostics.RemoveRange(s.DiagnosticCount, Diagnostics.Count - s.DiagnosticCount);
        }
    }
    internal sealed class SlidingTokenWindow
    {
        public readonly struct TokenWindowMark
        {
            public readonly int Index;
            public readonly int InjectedIndex;
            public readonly SyntaxToken[] Injected;
            public readonly int LogicalPosition;

            public TokenWindowMark(int index, int injectedIndex, SyntaxToken[] injected, int logicalPosition)
            {
                Index = index;
                InjectedIndex = injectedIndex;
                Injected = injected ?? Array.Empty<SyntaxToken>();
                LogicalPosition = logicalPosition;
            }
        }
        private readonly Lexer _lexer;

        private readonly List<SyntaxToken> _tokens = new();
        private readonly List<Lexer.Snapshot> _snapshots = new();

        private int _index;
        private readonly List<SyntaxToken> _injected = new();
        private int _injectedIndex;

        private int _logicalPosition;
        public int Position => _index;
        public SyntaxToken Current => Peek(0);
        public SyntaxKind CurrentKind => Current.Kind;
        public IReadOnlyList<SyntaxDiagnostic> LexerDiagnostics => _lexer.Diagnostics;
        public int Mark() => _index;
        public SlidingTokenWindow(Lexer lexer)
        {
            _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
        }


        public SyntaxToken Peek(int offset)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));

            int injectedRemaining = _injected.Count - _injectedIndex;
            if (offset < injectedRemaining)
                return _injected[_injectedIndex + offset];

            offset -= injectedRemaining;
            Ensure(_index + offset);
            return _tokens[_index + offset];
        }

        public SyntaxToken EatToken()
        {
            _logicalPosition++;

            int injectedRemaining = _injected.Count - _injectedIndex;
            if (injectedRemaining > 0)
            {
                var t = _injected[_injectedIndex++];
                if (_injectedIndex == _injected.Count)
                {
                    _injected.Clear();
                    _injectedIndex = 0;
                }
                return t;
            }

            var tok = CurrentFromBuffer();
            _index++;
            return tok;
        }
        public TokenWindowMark MarkState()
        => new TokenWindowMark(_index, _injectedIndex, _injected.ToArray(), _logicalPosition);

        public void Reset(TokenWindowMark mark)
        {
            _index = mark.Index;
            _injected.Clear();
            _injected.AddRange(mark.Injected);
            _injectedIndex = mark.InjectedIndex;
            _logicalPosition = mark.LogicalPosition;
        }

        public void Reset(int mark)
        {
            _index = mark;
            _injected.Clear();
            _injectedIndex = 0;
            _logicalPosition = mark;
        }
        private SyntaxToken CurrentFromBuffer()
        {
            Ensure(_index);
            return _tokens[_index];
        }

        public void PrependInjected(params SyntaxToken[] tokens)
        {
            if (tokens == null || tokens.Length == 0) return;

            if (_injected.Count == 0)
            {
                _injected.AddRange(tokens);
                _injectedIndex = 0;
                return;
            }
            // Prepend before remaining injected tokens
            var remaining = _injected.GetRange(_injectedIndex, _injected.Count - _injectedIndex);
            _injected.Clear();
            _injectedIndex = 0;
            _injected.AddRange(tokens);
            _injected.AddRange(remaining);
        }
        public void TruncateTo(int index)
        {
            if (index < 0 || index > _tokens.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == _tokens.Count) return;

            _lexer.RestoreSnapshot(_snapshots[index]);

            _tokens.RemoveRange(index, _tokens.Count - index);
            _snapshots.RemoveRange(index, _snapshots.Count - index);

            if (_index > index) _index = index;

            _injected.Clear();
            _injectedIndex = 0;
            _logicalPosition = Math.Min(_logicalPosition, index);
        }

        private void Ensure(int i)
        {
            while (_tokens.Count <= i)
            {
                _snapshots.Add(_lexer.CaptureSnapshot());
                _tokens.Add(_lexer.Lex());
            }
        }
        public void EnterInterpolationFormat()
        {
            TruncateTo(_index);
            _lexer.EnterInterpolationFormat();
        }

    }
}
