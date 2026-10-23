using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Rust;

public sealed class ParserOptions
{
    public RustEdition Edition { get; set; } = RustEdition.Rust2024;
    public int MaxDiagnostics { get; set; } = 512;
    public int MaxNestingDepth { get; set; } = 256;
}

///<summary>Builds a syntax tree from a token stream</summary>
public sealed class Parser
{
    private readonly string _source;
    private readonly ImmutableArray<SyntaxToken> _tokens;
    private readonly int _start, _end;
    private readonly SyntaxToken _endOfFile;
    private readonly List<SyntaxDiagnostic> _diagnostics = new();
    private readonly int _maxDiagnostics;
    private readonly int _maxDepth;
    private int _position;
    private int _depth;
    private SyntaxToken? _splitToken;
    private CompilationUnitSyntax? _root;
    public RustEdition Edition { get; }
    public IReadOnlyList<SyntaxDiagnostic> Diagnostics => _diagnostics;
    public ImmutableArray<SyntaxDiagnostic> LexerDiagnostics { get; }
    public SyntaxToken CurrentToken => Current;

    public Parser(string source, ParserOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Edition = options?.Edition ?? RustEdition.Rust2024;
        _maxDiagnostics = options?.MaxDiagnostics ?? 512;
        _maxDepth = options?.MaxNestingDepth ?? 256;
        if (_maxDiagnostics < 0 || _maxDepth < 1 || _maxDepth > 512)
            throw new ArgumentOutOfRangeException(nameof(options));
        var lexer = new Lexer(source, new LexerOptions { Edition = Edition, MaxDiagnostics = _maxDiagnostics });
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        do { tokens.Add(lexer.NextToken()); } while (tokens[^1].Kind != SyntaxKind.EndOfFileToken);
        _tokens = tokens.ToImmutable();
        _end = _tokens.Length - 1;
        _endOfFile = _tokens[^1];
        LexerDiagnostics = lexer.Diagnostics.ToImmutableArray();
    }

    internal Parser(ImmutableArray<SyntaxToken> tokens, RustEdition edition) : this(tokens, 0, tokens.Length, edition) { }
    internal Parser(ImmutableArray<SyntaxToken> tokens, int start, int end, RustEdition edition)
    {
        _source = "";
        Edition = edition;
        _maxDiagnostics = 512;
        _maxDepth = 256;
        _tokens = tokens;
        _position = _start = start;
        _end = end;
        _endOfFile = new SyntaxToken(SyntaxKind.EndOfFileToken, new TextSpan(end > start ? tokens[end - 1].Span.End : 0, 0), null, null, 0, SyntaxKind.None, [], [], text: "");
        LexerDiagnostics = [];
    }
    internal bool AtEnd => !_splitToken.HasValue && At(SyntaxKind.EndOfFileToken);
    internal int Consumed => _splitToken.HasValue ? -1 : _position - _start;
    internal ImmutableArray<ItemSyntax> ParseItems()
    {
        var items = ImmutableArray.CreateBuilder<ItemSyntax>();
        while (!At(SyntaxKind.EndOfFileToken))
        {
            int start = Mark;
            items.Add(ParseItem());
            if (Mark == start) { Report("Unexpected token."); Take(); }
        }
        return items.ToImmutable();
    }
    internal BlockExpressionSyntax ParseBlockExpression() => ParseBlock();
    internal ItemSyntax ParseItemFragment() => ParseItem();
    internal VisibilitySyntax? ParseVisibilityFragment() => ParseVisibility();
    internal PathSyntax ParsePathFragment() => ParsePath();
    internal PatternSyntax ParsePatternFragment(bool allowOr) => ParsePatternCore(allowOr);
    internal SyntaxNode ParseStatementFragment()
    {
        var parts = ParseAttributes();
        if (ItemStart) return ParseItem(parts);
        if (!At(SyntaxKind.LetKeyword)) return ParseExpressionCore(statement: true);
        parts.Add(Take());
        parts.Add(ParsePatternCore(allowOr: false));
        if (At(SyntaxKind.ColonToken)) parts.Add(ParseTypeAnnotation());
        if (At(SyntaxKind.EqualsToken)) parts.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseExpressionCore()));
        if (Optional(SyntaxKind.ElseKeyword, parts)) parts.Add(ParseBlock());
        return Make<LetStatementSyntax>(SyntaxKind.LetStatement, parts);
    }

    ///<summary>Parses the complete source text</summary>
    public CompilationUnitSyntax Parse()
    {
        if (_root is not null) return _root;
        var parts = new List<SyntaxNodeOrToken>();
        parts.AddRange(ParseAttributes(innerAllowed: true));
        while (!At(SyntaxKind.EndOfFileToken))
        {
            int start = Mark;
            parts.Add(ParseItem());
            EnsureProgress(start, parts);
        }
        parts.Add(Take());
        _root = Make<CompilationUnitSyntax>(SyntaxKind.CompilationUnit, parts);
        return _root;
    }

    public ExpressionSyntax ParseExpression() => ParseExpressionCore();
    public TypeSyntax ParseType() => ParseTypeCore();
    public PatternSyntax ParsePattern() => ParsePatternCore();

    // Token matching and recovery
    private SyntaxToken Token(int index) => index < _end ? _tokens[index] : _endOfFile;
    private SyntaxToken Current => _splitToken ?? Token(_position);
    private SyntaxToken Peek(int offset = 1)
    {
        if (offset == 0) return Current;
        return Token(_position + offset - (_splitToken.HasValue ? 1 : 0));
    }
    private bool At(SyntaxKind kind) => Current.Kind == kind;
    private bool Contextual(SyntaxKind kind) => Current.ContextualKind == kind;
    private bool IsName => Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.RawIdentifierToken;
    private static bool IsLifetime(SyntaxKind kind) => kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken;
    private static bool IsOpen(SyntaxKind kind) => kind is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken;
    private static bool IsClose(SyntaxKind kind) => kind is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken;
    private static SyntaxKind Closing(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenParenToken => SyntaxKind.CloseParenToken,
        SyntaxKind.OpenBracketToken => SyntaxKind.CloseBracketToken,
        _ => SyntaxKind.CloseBraceToken
    };
    private bool EndList(SyntaxKind close) => At(close) || At(SyntaxKind.EndOfFileToken) || IsClose(Current.Kind);
    private SyntaxToken Take()
    {
        var token = Current;
        if (_splitToken.HasValue) _splitToken = null;
        else if (!At(SyntaxKind.EndOfFileToken)) _position++;
        return token;
    }
    private void Report(string message, TextSpan? span = null)
    {
        if (_diagnostics.Count < _maxDiagnostics) _diagnostics.Add(SyntaxDiagnostic.Error(message, span ?? Current.Span));
    }
    private SyntaxToken Missing(SyntaxKind kind)
        => new(kind, new TextSpan(Current.Span.Start, 0), null, null, 0, SyntaxKind.None,
            Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>(), isMissing: true);
    private SyntaxToken Eat(SyntaxKind kind)
    {
        if (At(kind)) return Take();
        if (kind is SyntaxKind.GreaterThanToken or SyntaxKind.LessThanToken or SyntaxKind.AmpersandToken or SyntaxKind.PipeToken && TrySplit(kind, out var head))
            return head;
        Report($"Expected '{SyntaxFacts.GetText(kind) ?? kind.ToString()}'.");
        return Missing(kind);
    }
    private bool TrySplit(SyntaxKind kind, out SyntaxToken head)
    {
        head = default;
        string? whole = SyntaxFacts.GetText(Current.Kind);
        string? first = SyntaxFacts.GetText(kind);
        if (whole is null || first is null || whole.Length <= first.Length || !whole.StartsWith(first, StringComparison.Ordinal) ||
            !SyntaxFacts.TryGetPunctuation(whole.AsSpan(first.Length), out var remainderKind)) return false;
        var token = Current;
        head = token.WithKind(kind, new TextSpan(token.Span.Start, first.Length), first, first, token.LeadingTrivia, Array.Empty<SyntaxTrivia>());
        var tail = token.WithKind(remainderKind, new TextSpan(token.Span.Start + first.Length, token.Span.Length - first.Length),
            whole.Substring(first.Length), whole.Substring(first.Length), Array.Empty<SyntaxTrivia>(), token.TrailingTrivia);
        Take();
        _splitToken = tail;
        return true;
    }
    private SyntaxToken Name()
    {
        if (IsName) return Take();
        Report("Expected an identifier.");
        return Missing(SyntaxKind.IdentifierToken);
    }
    private SyntaxToken ContextualKeyword(SyntaxKind kind)
    {
        SyntaxToken token = Take();
        return new SyntaxToken(kind, token.Span, token.ValueText, token.Value, token.SuffixLength, token.ContextualKind, token.LeadingTrivia, token.TrailingTrivia,
            token.IsMissing, token.Context, token.Text);
    }
    private bool Optional(SyntaxKind kind, List<SyntaxNodeOrToken> parts)
    {
        if (!At(kind)) return false;
        parts.Add(Take());
        return true;
    }
    private static T Make<T>(SyntaxKind kind, IEnumerable<SyntaxNodeOrToken> parts) where T : SyntaxNode
        => (T)SyntaxFactory.Create(kind, parts.ToImmutableArray());
    private static T Make<T>(SyntaxKind kind, params SyntaxNodeOrToken[] parts) where T : SyntaxNode => Make<T>(kind, (IEnumerable<SyntaxNodeOrToken>)parts);
    private int Mark => _position * 2 + (_splitToken.HasValue ? 1 : 0);
    private void EnsureProgress(int start, List<SyntaxNodeOrToken> parts)
    {
        if (Mark == start && !At(SyntaxKind.EndOfFileToken))
        {
            Report("Unexpected token.");
            parts.Add(Make<SyntaxNode>(SyntaxKind.SkippedSyntax, Take()));
        }
    }
    private bool Enter()
    {
        if (_depth == _maxDepth) { Report("Maximum syntax nesting depth exceeded."); return false; }
        _depth++;
        return true;
    }
    private T DepthError<T>(SyntaxKind kind) where T : SyntaxNode
        => Make<T>(kind, At(SyntaxKind.EndOfFileToken) ? Missing(SyntaxKind.IdentifierToken) : Take());
    private bool Greater => Current.Kind is SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken or SyntaxKind.GreaterThanGreaterThanToken or SyntaxKind.GreaterThanGreaterThanEqualsToken;
    private bool Less => Current.Kind is SyntaxKind.LessThanToken or SyntaxKind.LessThanLessThanToken;
    private bool Ampersand => Current.Kind is SyntaxKind.AmpersandToken or SyntaxKind.AmpersandAmpersandToken;
    private bool PathStart => IsName || Current.Kind is SyntaxKind.ColonColonToken or SyntaxKind.SelfValueKeyword or SyntaxKind.SelfTypeKeyword or SyntaxKind.SuperKeyword or SyntaxKind.CrateKeyword || Less;

    // Attributes and token trees
    private List<SyntaxNodeOrToken> ParseAttributes(bool innerAllowed = false)
    {
        var parts = new List<SyntaxNodeOrToken>();
        while (At(SyntaxKind.HashToken) && (!innerAllowed || Peek().Kind == SyntaxKind.BangToken))
        {
            var attribute = new List<SyntaxNodeOrToken> { Take() };
            if (At(SyntaxKind.BangToken))
            {
                if (!innerAllowed) Report("An inner attribute is not allowed here.");
                attribute.Add(Take());
            }
            attribute.Add(Eat(SyntaxKind.OpenBracketToken));
            bool unsafeAttribute = Optional(SyntaxKind.UnsafeKeyword, attribute);
            if (unsafeAttribute) attribute.Add(Eat(SyntaxKind.OpenParenToken));
            attribute.Add(ParsePath(simple: true));
            if (IsOpen(Current.Kind)) attribute.Add(ParseTokenTree());
            else if (At(SyntaxKind.EqualsToken)) attribute.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseExpressionCore()));
            SyntaxKind close = unsafeAttribute ? SyntaxKind.CloseParenToken : SyntaxKind.CloseBracketToken;
            while (!EndList(close))
            {
                Report("Unexpected attribute input.");
                attribute.Add(IsOpen(Current.Kind) ? ParseTokenTree() : Make<SyntaxNode>(SyntaxKind.SkippedSyntax, Take()));
            }
            if (unsafeAttribute) attribute.Add(Eat(SyntaxKind.CloseParenToken));
            attribute.Add(Eat(SyntaxKind.CloseBracketToken));
            parts.Add(Make<AttributeSyntax>(SyntaxKind.Attribute, attribute));
        }
        return parts;
    }
    private TokenTreeSyntax ParseTokenTree()
    {
        if (!Enter()) return DepthError<TokenTreeSyntax>(SyntaxKind.TokenTree);
        try
        {
            var parts = new List<SyntaxNodeOrToken>();
            SyntaxKind open = IsOpen(Current.Kind) ? Current.Kind : SyntaxKind.OpenParenToken;
            parts.Add(Eat(open));
            SyntaxKind close = Closing(open);
            while (!EndList(close)) parts.Add(IsOpen(Current.Kind) ? ParseTokenTree() : Take());
            parts.Add(Eat(close));
            return Make<TokenTreeSyntax>(SyntaxKind.TokenTree, parts);
        }
        finally { _depth--; }
    }
    private VisibilitySyntax? ParseVisibility()
    {
        if (!At(SyntaxKind.PubKeyword)) return null;
        var parts = new List<SyntaxNodeOrToken> { Take() };
        if (Optional(SyntaxKind.OpenParenToken, parts))
        {
            Optional(SyntaxKind.InKeyword, parts);
            parts.Add(ParsePath(simple: true));
            parts.Add(Eat(SyntaxKind.CloseParenToken));
        }
        return Make<VisibilitySyntax>(SyntaxKind.Visibility, parts);
    }
    private MacroExpressionSyntax ParseMacro(PathSyntax path)
        => Make<MacroExpressionSyntax>(SyntaxKind.MacroExpression, path, Eat(SyntaxKind.BangToken), ParseTokenTree());

    // Items and parameters
    private bool ItemStart => Current.Kind switch
    {
        SyntaxKind.PubKeyword or SyntaxKind.ModKeyword or SyntaxKind.UseKeyword or SyntaxKind.FnKeyword or
        SyntaxKind.StructKeyword or SyntaxKind.EnumKeyword or SyntaxKind.TraitKeyword or SyntaxKind.ImplKeyword or
        SyntaxKind.TypeKeyword or SyntaxKind.StaticKeyword or SyntaxKind.ExternKeyword => true,
        SyntaxKind.ConstKeyword => Peek().Kind != SyntaxKind.OpenBraceToken,
        SyntaxKind.AsyncKeyword => Peek().Kind == SyntaxKind.FnKeyword || Peek().Kind == SyntaxKind.UnsafeKeyword,
        SyntaxKind.UnsafeKeyword => Peek().Kind != SyntaxKind.OpenBraceToken,
        _ => Contextual(SyntaxKind.UnionKeyword) && Peek().Kind is SyntaxKind.IdentifierToken or SyntaxKind.RawIdentifierToken ||
            Contextual(SyntaxKind.MacroRulesKeyword) && Peek().Kind == SyntaxKind.BangToken ||
            Contextual(SyntaxKind.SafeKeyword) && Peek().Kind is SyntaxKind.FnKeyword or SyntaxKind.StaticKeyword
    };

    private ItemSyntax ParseItem(List<SyntaxNodeOrToken>? attributes = null, bool allowUnnamedParameters = false)
    {
        if (!Enter()) return DepthError<ItemSyntax>(SyntaxKind.MissingItem);
        try
        {
            var parts = attributes ?? ParseAttributes();
            if (ParseVisibility() is VisibilitySyntax visibility) parts.Add(visibility);
            while (At(SyntaxKind.UnsafeKeyword) || At(SyntaxKind.AsyncKeyword) || Contextual(SyntaxKind.SafeKeyword) && Peek().Kind is SyntaxKind.FnKeyword or SyntaxKind.StaticKeyword ||
                At(SyntaxKind.ConstKeyword) && Peek().Kind is SyntaxKind.FnKeyword or SyntaxKind.UnsafeKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.ExternKeyword)
                parts.Add(Contextual(SyntaxKind.SafeKeyword) ? ContextualKeyword(SyntaxKind.SafeKeyword) : Take());
            if (At(SyntaxKind.ExternKeyword))
            {
                parts.Add(Take());
                if (Optional(SyntaxKind.CrateKeyword, parts))
                {
                    parts.Add(At(SyntaxKind.SelfValueKeyword) ? Take() : Name());
                    if (Optional(SyntaxKind.AsKeyword, parts)) parts.Add(At(SyntaxKind.UnderscoreKeyword) ? Take() : Name());
                    parts.Add(Eat(SyntaxKind.SemicolonToken));
                    return Make<ItemSyntax>(SyntaxKind.ExternCrateItem, parts);
                }
                if (IsString(Current.Kind)) parts.Add(Take());
                if (!At(SyntaxKind.FnKeyword))
                {
                    parts.Add(ParseItemList());
                    return Make<ItemSyntax>(SyntaxKind.ExternBlockItem, parts);
                }
            }
            if (Contextual(SyntaxKind.MacroRulesKeyword) && Peek().Kind == SyntaxKind.BangToken)
            {
                parts.Add(ContextualKeyword(SyntaxKind.MacroRulesKeyword));
                parts.Add(Take());
                parts.Add(Name());
                bool braces = At(SyntaxKind.OpenBraceToken);
                parts.Add(ParseTokenTree());
                if (!braces) parts.Add(Eat(SyntaxKind.SemicolonToken));
                return Make<ItemSyntax>(SyntaxKind.MacroDefinitionItem, parts);
            }
            SyntaxKind kind;
            if (At(SyntaxKind.ModKeyword))
            {
                parts.Add(Take()); parts.Add(Name());
                if (At(SyntaxKind.OpenBraceToken)) parts.Add(ParseItemList());
                else parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = SyntaxKind.ModuleItem;
            }
            else if (At(SyntaxKind.UseKeyword))
            {
                parts.Add(Take()); parts.Add(ParseUseTree()); parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = SyntaxKind.UseItem;
            }
            else if (At(SyntaxKind.FnKeyword))
            {
                parts.Add(Take()); parts.Add(Name());
                AddGenerics(parts);
                parts.Add(ParseParameters(allowUnnamed: allowUnnamedParameters));
                if (At(SyntaxKind.MinusGreaterThanToken)) parts.Add(ParseReturnType());
                AddWhere(parts);
                if (At(SyntaxKind.OpenBraceToken)) parts.Add(ParseBlock());
                else parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = SyntaxKind.FunctionItem;
            }
            else if (At(SyntaxKind.StructKeyword) || Contextual(SyntaxKind.UnionKeyword))
            {
                bool union = Contextual(SyntaxKind.UnionKeyword);
                parts.Add(union ? ContextualKeyword(SyntaxKind.UnionKeyword) : Take()); parts.Add(Name());
                AddGenerics(parts);
                bool tuple = !union && At(SyntaxKind.OpenParenToken);
                if (tuple) parts.Add(ParseFields(tuple: true));
                AddWhere(parts);
                if (!tuple && At(SyntaxKind.OpenBraceToken)) parts.Add(ParseFields(tuple: false));
                else parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = union ? SyntaxKind.UnionItem : SyntaxKind.StructItem;
            }
            else if (At(SyntaxKind.EnumKeyword))
            {
                parts.Add(Take()); parts.Add(Name());
                AddGenerics(parts); AddWhere(parts);
                parts.Add(Eat(SyntaxKind.OpenBraceToken));
                while (!EndList(SyntaxKind.CloseBraceToken))
                {
                    int start = Mark;
                    var variant = ParseAttributes();
                    variant.Add(Name());
                    if (At(SyntaxKind.OpenParenToken) || At(SyntaxKind.OpenBraceToken)) variant.Add(ParseFields(At(SyntaxKind.OpenParenToken)));
                    if (At(SyntaxKind.EqualsToken)) variant.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseExpressionCore()));
                    parts.Add(Make<EnumVariantSyntax>(SyntaxKind.EnumVariant, variant));
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                }
                parts.Add(Eat(SyntaxKind.CloseBraceToken));
                kind = SyntaxKind.EnumItem;
            }
            else if (At(SyntaxKind.TraitKeyword))
            {
                parts.Add(Take()); parts.Add(Name()); AddGenerics(parts);
                if (Optional(SyntaxKind.ColonToken, parts)) AddOptionalBounds(parts);
                if (Optional(SyntaxKind.EqualsToken, parts))
                {
                    parts.Add(ParseBounds()); AddWhere(parts); parts.Add(Eat(SyntaxKind.SemicolonToken));
                }
                else { AddWhere(parts); parts.Add(ParseItemList(allowUnnamedParameters: Edition == RustEdition.Rust2015)); }
                kind = SyntaxKind.TraitItem;
            }
            else if (At(SyntaxKind.ImplKeyword))
            {
                parts.Add(Take());
                if (Less && LooksLikeGenericParameters()) AddGenerics(parts);
                Optional(SyntaxKind.BangToken, parts);
                parts.Add(ParseTypeCore());
                if (Optional(SyntaxKind.ForKeyword, parts)) parts.Add(ParseTypeCore());
                AddWhere(parts);
                parts.Add(ParseItemList());
                kind = SyntaxKind.ImplItem;
            }
            else if (At(SyntaxKind.TypeKeyword))
            {
                parts.Add(Take()); parts.Add(Name()); AddGenerics(parts);
                if (Optional(SyntaxKind.ColonToken, parts)) AddOptionalBounds(parts);
                AddWhere(parts);
                if (At(SyntaxKind.EqualsToken)) parts.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseTypeCore()));
                AddWhere(parts);
                parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = SyntaxKind.TypeAliasItem;
            }
            else if (At(SyntaxKind.ConstKeyword) || At(SyntaxKind.StaticKeyword))
            {
                bool constant = At(SyntaxKind.ConstKeyword);
                parts.Add(Take());
                if (!constant) Optional(SyntaxKind.MutKeyword, parts);
                parts.Add(constant && At(SyntaxKind.UnderscoreKeyword) ? Take() : Name());
                parts.Add(ParseTypeAnnotation());
                if (At(SyntaxKind.EqualsToken)) parts.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseExpressionCore()));
                parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = constant ? SyntaxKind.ConstItem : SyntaxKind.StaticItem;
            }
            else if (PathStart)
            {
                var macro = ParseMacro(ParsePath(simple: true));
                parts.Add(macro);
                if (macro.Body?.OpenToken.Kind != SyntaxKind.OpenBraceToken) parts.Add(Eat(SyntaxKind.SemicolonToken));
                kind = SyntaxKind.MacroInvocationItem;
            }
            else
            {
                Report("Expected an item.");
                parts.Add(At(SyntaxKind.EndOfFileToken) ? Missing(SyntaxKind.IdentifierToken) : Take());
                kind = SyntaxKind.MissingItem;
            }
            return Make<ItemSyntax>(kind, parts);
        }
        finally { _depth--; }
    }

    private bool LooksLikeGenericParameters()
        => IsLifetime(Peek().Kind) || Peek().Kind is SyntaxKind.HashToken or SyntaxKind.ConstKeyword or SyntaxKind.GreaterThanToken ||
            Peek().Kind is SyntaxKind.IdentifierToken or SyntaxKind.RawIdentifierToken &&
            (Peek(2).Kind is SyntaxKind.ColonToken or SyntaxKind.CommaToken or SyntaxKind.EqualsToken || Peek(2).Kind == SyntaxKind.GreaterThanToken && Peek(3).Kind != SyntaxKind.ColonColonToken);
    private void AddGenerics(List<SyntaxNodeOrToken> parts) { if (Less) parts.Add(ParseGenericParameters()); }
    private void AddWhere(List<SyntaxNodeOrToken> parts) { if (At(SyntaxKind.WhereKeyword)) parts.Add(ParseWhereClause()); }

    private ItemListSyntax ParseItemList(bool allowUnnamedParameters = false)
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.OpenBraceToken) };
        parts.AddRange(ParseAttributes(innerAllowed: true));
        while (!EndList(SyntaxKind.CloseBraceToken))
        {
            int start = Mark;
            parts.Add(ParseItem(allowUnnamedParameters: allowUnnamedParameters));
            EnsureProgress(start, parts);
        }
        parts.Add(Eat(SyntaxKind.CloseBraceToken));
        return Make<ItemListSyntax>(SyntaxKind.ItemList, parts);
    }

    private FieldListSyntax ParseFields(bool tuple)
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(tuple ? SyntaxKind.OpenParenToken : SyntaxKind.OpenBraceToken) };
        SyntaxKind close = tuple ? SyntaxKind.CloseParenToken : SyntaxKind.CloseBraceToken;
        while (!EndList(close))
        {
            int start = Mark;
            var field = ParseAttributes();
            if (ParseVisibility() is VisibilitySyntax visibility) field.Add(visibility);
            if (tuple) field.Add(ParseTypeCore());
            else { field.Add(Name()); field.Add(ParseTypeAnnotation()); }
            parts.Add(Make<FieldSyntax>(SyntaxKind.Field, field));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(close));
        return Make<FieldListSyntax>(SyntaxKind.FieldList, parts);
    }

    private ParameterListSyntax ParseParameters(bool closure = false, bool allowUnnamed = false)
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(closure ? SyntaxKind.PipeToken : SyntaxKind.OpenParenToken) };
        SyntaxKind close = closure ? SyntaxKind.PipeToken : SyntaxKind.CloseParenToken;
        while (!EndList(close) && !(closure && At(SyntaxKind.PipePipeToken)))
        {
            int start = Mark;
            var parameter = ParseAttributes();
            bool receiver = !closure && (At(SyntaxKind.SelfValueKeyword) || At(SyntaxKind.MutKeyword) && Peek().Kind == SyntaxKind.SelfValueKeyword);
            if (!closure && Ampersand)
            {
                int offset = IsLifetime(Peek().Kind) ? 2 : 1;
                if (Peek(offset).Kind == SyntaxKind.MutKeyword) offset++;
                receiver = Peek(offset).Kind == SyntaxKind.SelfValueKeyword;
            }
            if (receiver)
            {
                if (Ampersand)
                {
                    parameter.Add(Eat(SyntaxKind.AmpersandToken));
                    if (IsLifetime(Current.Kind)) parameter.Add(Take());
                }
                Optional(SyntaxKind.MutKeyword, parameter);
                parameter.Add(Eat(SyntaxKind.SelfValueKeyword));
                if (At(SyntaxKind.ColonToken)) parameter.Add(ParseTypeAnnotation());
            }
            else if (!Optional(SyntaxKind.DotDotDotToken, parameter))
            {
                int savedPosition = _position;
                SyntaxToken? savedSplitToken = _splitToken;
                int savedDiagnostics = _diagnostics.Count;
                PatternSyntax pattern = ParsePatternCore(allowOr: false);
                if (allowUnnamed && !At(SyntaxKind.ColonToken))
                {
                    _position = savedPosition;
                    _splitToken = savedSplitToken;
                    _diagnostics.RemoveRange(savedDiagnostics, _diagnostics.Count - savedDiagnostics);
                    parameter.Add(ParseTypeCore());
                }
                else
                {
                    parameter.Add(pattern);
                    if (At(SyntaxKind.ColonToken))
                    {
                        if (Peek().Kind == SyntaxKind.DotDotDotToken) { parameter.Add(Take()); parameter.Add(Take()); }
                        else parameter.Add(ParseTypeAnnotation());
                    }
                    else if (!closure) parameter.Add(ParseTypeAnnotation());
                }
            }
            parts.Add(Make<ParameterSyntax>(SyntaxKind.Parameter, parameter));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(close));
        return Make<ParameterListSyntax>(SyntaxKind.ParameterList, parts);
    }

    private UseTreeSyntax ParseUseTree()
    {
        if (!Enter()) return DepthError<UseTreeSyntax>(SyntaxKind.UseTree);
        try
        {
            var parts = new List<SyntaxNodeOrToken>();
            if (PathStart && !Less)
            {
                if (At(SyntaxKind.ColonColonToken) && Peek().Kind == SyntaxKind.OpenBraceToken) parts.Add(Take());
                else parts.Add(ParsePath(simple: true));
                Optional(SyntaxKind.ColonColonToken, parts);
            }
            if (Optional(SyntaxKind.OpenBraceToken, parts))
            {
                while (!EndList(SyntaxKind.CloseBraceToken))
                {
                    int start = Mark;
                    parts.Add(ParseUseTree());
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                }
                parts.Add(Eat(SyntaxKind.CloseBraceToken));
            }
            else if (!Optional(SyntaxKind.StarToken, parts) && parts.Count == 0)
            {
                Report("Expected a use path.");
                parts.Add(Missing(SyntaxKind.IdentifierToken));
            }
            if (Optional(SyntaxKind.AsKeyword, parts)) parts.Add(At(SyntaxKind.UnderscoreKeyword) ? Take() : Name());
            return Make<UseTreeSyntax>(SyntaxKind.UseTree, parts);
        }
        finally { _depth--; }
    }

    // Paths, types and generic constraints
    private PathSyntax ParsePath(bool simple = false, bool expression = false)
    {
        var parts = new List<SyntaxNodeOrToken>();
        if (!simple && Less)
        {
            var qualifier = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.LessThanToken), ParseTypeCore() };
            if (Optional(SyntaxKind.AsKeyword, qualifier)) qualifier.Add(ParsePath());
            qualifier.Add(Eat(SyntaxKind.GreaterThanToken));
            parts.Add(Make<QualifiedPathSyntax>(SyntaxKind.QualifiedPath, qualifier));
            parts.Add(Eat(SyntaxKind.ColonColonToken));
        }
        else Optional(SyntaxKind.ColonColonToken, parts);
        while (true)
        {
            var segment = new List<SyntaxNodeOrToken>();
            segment.Add(Current.Kind is SyntaxKind.SelfValueKeyword or SyntaxKind.SelfTypeKeyword or SyntaxKind.SuperKeyword or SyntaxKind.CrateKeyword ? Take() : Name());
            if (!simple)
            {
                if (At(SyntaxKind.ColonColonToken) && Peek().Kind == SyntaxKind.LessThanToken)
                {
                    segment.Add(Take());
                    segment.Add(ParseGenericArguments());
                }
                else if (!expression && Less) segment.Add(ParseGenericArguments());
                if (!expression && At(SyntaxKind.OpenParenToken))
                {
                    segment.Add(ParseParameterTypes());
                    if (At(SyntaxKind.MinusGreaterThanToken)) segment.Add(ParseReturnType(allowBounds: false));
                }
            }
            parts.Add(Make<PathSegmentSyntax>(SyntaxKind.PathSegment, segment));
            if (!At(SyntaxKind.ColonColonToken) || Peek().Kind is SyntaxKind.StarToken or SyntaxKind.OpenBraceToken) break;
            parts.Add(Take());
        }
        return Make<PathSyntax>(SyntaxKind.Path, parts);
    }

    private TypeSyntax ParseTypeCore(bool allowBounds = true)
    {
        if (!Enter()) return DepthError<TypeSyntax>(SyntaxKind.MissingType);
        try
        {
            var parts = new List<SyntaxNodeOrToken>();
            SyntaxKind kind;
            if (Ampersand)
            {
                parts.Add(Eat(SyntaxKind.AmpersandToken));
                if (IsLifetime(Current.Kind)) parts.Add(Take());
                Optional(SyntaxKind.MutKeyword, parts);
                parts.Add(ParseTypeCore(false));
                kind = SyntaxKind.ReferenceType;
            }
            else if (At(SyntaxKind.StarToken))
            {
                parts.Add(Take());
                parts.Add(At(SyntaxKind.MutKeyword) ? Take() : Eat(SyntaxKind.ConstKeyword));
                parts.Add(ParseTypeCore(false));
                kind = SyntaxKind.PointerType;
            }
            else if (At(SyntaxKind.OpenParenToken))
            {
                parts.Add(Take());
                int count = 0;
                bool comma = false;
                while (!EndList(SyntaxKind.CloseParenToken))
                {
                    int start = Mark;
                    parts.Add(ParseTypeCore());
                    count++;
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                    comma = true;
                }
                parts.Add(Eat(SyntaxKind.CloseParenToken));
                kind = count == 1 && !comma ? SyntaxKind.ParenthesizedType : SyntaxKind.TupleType;
            }
            else if (At(SyntaxKind.OpenBracketToken))
            {
                parts.Add(Take());
                parts.Add(ParseTypeCore());
                kind = SyntaxKind.SliceType;
                if (Optional(SyntaxKind.SemicolonToken, parts))
                {
                    parts.Add(ParseExpressionCore());
                    kind = SyntaxKind.ArrayType;
                }
                parts.Add(Eat(SyntaxKind.CloseBracketToken));
            }
            else if (At(SyntaxKind.ImplKeyword) || DynKeyword)
            {
                kind = At(SyntaxKind.ImplKeyword) ? SyntaxKind.ImplTraitType : SyntaxKind.TraitObjectType;
                parts.Add(At(SyntaxKind.IdentifierToken) ? ContextualKeyword(SyntaxKind.DynKeyword) : Take());
                parts.Add(ParseBounds(allowBounds));
            }
            else if (At(SyntaxKind.FnKeyword) || At(SyntaxKind.UnsafeKeyword) || At(SyntaxKind.ExternKeyword) || At(SyntaxKind.ForKeyword))
            {
                if (At(SyntaxKind.ForKeyword)) parts.Add(ParseLifetimeBinder());
                if (PathStart)
                {
                    parts.Add(ParsePath());
                    var bounds = new List<SyntaxNodeOrToken> { Make<TypeBoundSyntax>(SyntaxKind.TypeBound, parts) };
                    if (allowBounds && Optional(SyntaxKind.PlusToken, bounds) && BoundStart) bounds.AddRange(ParseBounds().Children);
                    parts = new List<SyntaxNodeOrToken> { Make<TypeBoundListSyntax>(SyntaxKind.TypeBoundList, bounds) };
                    kind = SyntaxKind.TraitObjectType;
                }
                else
                {
                    Optional(SyntaxKind.UnsafeKeyword, parts);
                    if (Optional(SyntaxKind.ExternKeyword, parts) && IsString(Current.Kind)) parts.Add(Take());
                    parts.Add(Eat(SyntaxKind.FnKeyword));
                    parts.Add(ParseParameterTypes());
                    if (At(SyntaxKind.MinusGreaterThanToken)) parts.Add(ParseReturnType(allowBounds: false));
                    kind = SyntaxKind.FunctionPointerType;
                }
            }
            else if (At(SyntaxKind.BangToken) || At(SyntaxKind.UnderscoreKeyword))
            {
                kind = At(SyntaxKind.BangToken) ? SyntaxKind.NeverType : SyntaxKind.InferredType;
                parts.Add(Take());
            }
            else if (PathStart)
            {
                var path = ParsePath();
                if (At(SyntaxKind.BangToken))
                {
                    parts.Add(ParseMacro(path));
                    kind = SyntaxKind.MacroType;
                }
                else
                {
                    parts.Add(path);
                    kind = SyntaxKind.PathType;
                }
            }
            else
            {
                Report("Expected a type.");
                parts.Add(Missing(SyntaxKind.IdentifierToken));
                kind = SyntaxKind.MissingType;
            }
            TypeSyntax result = Make<TypeSyntax>(kind, parts);
            if (allowBounds && At(SyntaxKind.PlusToken))
            {
                var first = result is PathTypeSyntax pathType ? Make<TypeBoundSyntax>(SyntaxKind.TypeBound, pathType.Children) : Make<TypeBoundSyntax>(SyntaxKind.TypeBound, result);
                var bounds = new List<SyntaxNodeOrToken> { first, Take() };
                if (BoundStart) bounds.AddRange(ParseBounds().Children);
                result = Make<TypeSyntax>(SyntaxKind.TraitObjectType, Make<TypeBoundListSyntax>(SyntaxKind.TypeBoundList, bounds));
            }
            return result;
        }
        finally { _depth--; }
    }

    private static bool IsString(SyntaxKind kind) => kind is SyntaxKind.StringLiteralToken or SyntaxKind.RawStringLiteralToken;
    private bool DynKeyword => At(SyntaxKind.DynKeyword) || Edition == RustEdition.Rust2015 && At(SyntaxKind.IdentifierToken) && Current.ValueText == "dyn" &&
        (Peek().Kind is SyntaxKind.IdentifierToken or SyntaxKind.RawIdentifierToken or SyntaxKind.SelfValueKeyword or SyntaxKind.SelfTypeKeyword or SyntaxKind.SuperKeyword or SyntaxKind.CrateKeyword or
            SyntaxKind.ForKeyword or SyntaxKind.OpenParenToken or SyntaxKind.QuestionToken || IsLifetime(Peek().Kind));
    private bool BoundStart => PathStart || IsLifetime(Current.Kind) || Current.Kind is SyntaxKind.ForKeyword or SyntaxKind.OpenParenToken or SyntaxKind.QuestionToken or SyntaxKind.TildeToken or SyntaxKind.UseKeyword;
    private void AddOptionalBounds(List<SyntaxNodeOrToken> parts) { if (BoundStart) parts.Add(ParseBounds()); }
    private TypeAnnotationSyntax ParseTypeAnnotation() => Make<TypeAnnotationSyntax>(SyntaxKind.TypeAnnotation, Eat(SyntaxKind.ColonToken), ParseTypeCore());
    private ReturnTypeSyntax ParseReturnType(bool allowBounds = true) => Make<ReturnTypeSyntax>(SyntaxKind.ReturnType, Eat(SyntaxKind.MinusGreaterThanToken), ParseTypeCore(allowBounds));
    private LifetimeBinderSyntax ParseLifetimeBinder() => Make<LifetimeBinderSyntax>(SyntaxKind.LifetimeBinder, Eat(SyntaxKind.ForKeyword), ParseGenericParameters());

    private GenericParameterListSyntax ParseGenericParameters()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.LessThanToken) };
        while (!Greater && !EndList(SyntaxKind.GreaterThanToken))
        {
            int start = Mark;
            var parameter = ParseAttributes();
            bool lifetime = IsLifetime(Current.Kind);
            bool constant = Optional(SyntaxKind.ConstKeyword, parameter);
            parameter.Add(lifetime ? Take() : Name());
            if (At(SyntaxKind.ColonToken))
            {
                if (constant) parameter.Add(ParseTypeAnnotation());
                else { parameter.Add(Take()); AddOptionalBounds(parameter); }
            }
            if (At(SyntaxKind.EqualsToken))
                parameter.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), constant ? ParseConstArgument() : ParseTypeCore()));
            parts.Add(Make<GenericParameterSyntax>(SyntaxKind.GenericParameter, parameter));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(SyntaxKind.GreaterThanToken));
        return Make<GenericParameterListSyntax>(SyntaxKind.GenericParameterList, parts);
    }

    private GenericArgumentListSyntax ParseGenericArguments()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.LessThanToken) };
        while (!Greater && !EndList(SyntaxKind.GreaterThanToken))
        {
            int start = Mark;
            var argument = new List<SyntaxNodeOrToken>();
            if (IsLifetime(Current.Kind)) argument.Add(Take());
            else if (At(SyntaxKind.OpenBraceToken) || At(SyntaxKind.MinusToken) || IsLiteral(Current.Kind)) argument.Add(ParseConstArgument());
            else
            {
                // Type and const arguments share path syntax until binding
                argument.Add(ParseTypeCore());
                if (At(SyntaxKind.EqualsToken))
                {
                    var equals = Take();
                    SyntaxNode value = At(SyntaxKind.OpenBraceToken) || At(SyntaxKind.MinusToken) || IsLiteral(Current.Kind) ? ParseConstArgument() : ParseTypeCore();
                    argument.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, equals, value));
                }
                else if (Optional(SyntaxKind.ColonToken, argument)) argument.Add(ParseBounds());
            }
            parts.Add(Make<GenericArgumentSyntax>(SyntaxKind.GenericArgument, argument));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(SyntaxKind.GreaterThanToken));
        return Make<GenericArgumentListSyntax>(SyntaxKind.GenericArgumentList, parts);
    }

    private ExpressionSyntax ParseConstArgument()
    {
        if (!Enter()) return DepthError<ExpressionSyntax>(SyntaxKind.MissingExpression);
        try
        {
            if (At(SyntaxKind.OpenBraceToken)) return ParseBlock();
            if (At(SyntaxKind.MinusToken)) return Make<ExpressionSyntax>(SyntaxKind.UnaryExpression, Take(), ParseConstArgument());
            if (IsLiteral(Current.Kind)) return Make<ExpressionSyntax>(SyntaxKind.LiteralExpression, Take());
            return Make<ExpressionSyntax>(SyntaxKind.PathExpression, ParsePath(expression: true));
        }
        finally { _depth--; }
    }

    private TypeBoundListSyntax ParseBounds(bool multiple = true)
    {
        var parts = new List<SyntaxNodeOrToken>();
        do
        {
            parts.Add(ParseBound());
            if (!multiple || !Optional(SyntaxKind.PlusToken, parts)) break;
            if (!BoundStart) break;
        } while (!At(SyntaxKind.EndOfFileToken));
        return Make<TypeBoundListSyntax>(SyntaxKind.TypeBoundList, parts);
    }

    private TypeBoundSyntax ParseBound()
    {
        if (!Enter()) return DepthError<TypeBoundSyntax>(SyntaxKind.TypeBound);
        try
        {
            var parts = new List<SyntaxNodeOrToken>();
            if (IsLifetime(Current.Kind)) parts.Add(Take());
            else if (Optional(SyntaxKind.OpenParenToken, parts))
            {
                parts.Add(ParseBound());
                parts.Add(Eat(SyntaxKind.CloseParenToken));
            }
            else if (Optional(SyntaxKind.UseKeyword, parts))
            {
                parts.Add(Eat(SyntaxKind.LessThanToken));
                while (!Greater && !EndList(SyntaxKind.GreaterThanToken))
                {
                    int start = Mark;
                    parts.Add(IsLifetime(Current.Kind) || At(SyntaxKind.SelfTypeKeyword) ? Take() : Name());
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                }
                parts.Add(Eat(SyntaxKind.GreaterThanToken));
            }
            else
            {
                Optional(SyntaxKind.QuestionToken, parts);
                if (Optional(SyntaxKind.TildeToken, parts)) parts.Add(Eat(SyntaxKind.ConstKeyword));
                if (At(SyntaxKind.ForKeyword)) parts.Add(ParseLifetimeBinder());
                parts.Add(ParsePath());
            }
            return Make<TypeBoundSyntax>(SyntaxKind.TypeBound, parts);
        }
        finally { _depth--; }
    }

    private WhereClauseSyntax ParseWhereClause()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.WhereKeyword) };
        while (Current.Kind is not (SyntaxKind.OpenBraceToken or SyntaxKind.SemicolonToken or SyntaxKind.EqualsToken or SyntaxKind.EndOfFileToken) && !IsClose(Current.Kind))
        {
            int start = Mark;
            var predicate = new List<SyntaxNodeOrToken>();
            if (At(SyntaxKind.ForKeyword)) predicate.Add(ParseLifetimeBinder());
            if (IsLifetime(Current.Kind)) predicate.Add(Take());
            else predicate.Add(ParseTypeCore());
            predicate.Add(Eat(SyntaxKind.ColonToken));
            AddOptionalBounds(predicate);
            parts.Add(Make<WherePredicateSyntax>(SyntaxKind.WherePredicate, predicate));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        return Make<WhereClauseSyntax>(SyntaxKind.WhereClause, parts);
    }

    private ParameterTypeListSyntax ParseParameterTypes()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.OpenParenToken) };
        while (!EndList(SyntaxKind.CloseParenToken))
        {
            int start = Mark;
            var parameter = ParseAttributes();
            if ((IsName || At(SyntaxKind.UnderscoreKeyword)) && Peek().Kind == SyntaxKind.ColonToken)
            {
                parameter.Add(Take());
                parameter.Add(Take());
            }
            if (!Optional(SyntaxKind.DotDotDotToken, parameter)) parameter.Add(ParseTypeCore());
            parts.Add(Make<ParameterSyntax>(SyntaxKind.Parameter, parameter));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(SyntaxKind.CloseParenToken));
        return Make<ParameterTypeListSyntax>(SyntaxKind.ParameterTypeList, parts);
    }

    // Patterns
    private static bool IsLiteral(SyntaxKind kind) => SyntaxFacts.IsLiteral(kind) || kind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword;

    private PatternSyntax ParsePatternCore(bool allowOr = true)
    {
        if (!Enter()) return DepthError<PatternSyntax>(SyntaxKind.MissingPattern);
        try
        {
            var parts = new List<SyntaxNodeOrToken>();
            bool leading = allowOr && Optional(SyntaxKind.PipeToken, parts);
            var pattern = ParsePatternRange();
            if (!allowOr || !leading && !At(SyntaxKind.PipeToken)) return pattern;
            parts.Add(pattern);
            while (Optional(SyntaxKind.PipeToken, parts)) parts.Add(ParsePatternRange());
            return Make<PatternSyntax>(SyntaxKind.OrPattern, parts);
        }
        finally { _depth--; }
    }

    private PatternSyntax ParsePatternRange()
    {
        var parts = new List<SyntaxNodeOrToken>();
        if (Current.Kind is not (SyntaxKind.DotDotToken or SyntaxKind.DotDotEqualsToken or SyntaxKind.DotDotDotToken)) parts.Add(ParsePatternAtom());
        if (Current.Kind is SyntaxKind.DotDotToken or SyntaxKind.DotDotEqualsToken or SyntaxKind.DotDotDotToken)
        {
            SyntaxToken op = Take();
            parts.Add(op);
            if (PatternBoundStart) parts.Add(ParsePatternAtom());
            else if (op.Kind != SyntaxKind.DotDotToken)
            {
                Report("An inclusive range pattern requires an upper bound.");
                parts.Add(Make<PatternSyntax>(SyntaxKind.MissingPattern, Missing(SyntaxKind.IdentifierToken)));
            }
            if (parts.Count == 1 && op.Kind == SyntaxKind.DotDotToken) return Make<PatternSyntax>(SyntaxKind.RestPattern, parts);
            return Make<PatternSyntax>(SyntaxKind.RangePattern, parts);
        }
        return (PatternSyntax)parts[0].Node;
    }

    private bool PatternBoundStart => PathStart || IsLiteral(Current.Kind) || At(SyntaxKind.MinusToken) || At(SyntaxKind.ConstKeyword);

    private PatternSyntax ParsePatternAtom()
    {
        var parts = new List<SyntaxNodeOrToken>();
        if (At(SyntaxKind.UnderscoreKeyword)) return Make<PatternSyntax>(SyntaxKind.WildcardPattern, Take());
        if (Ampersand)
        {
            parts.Add(Eat(SyntaxKind.AmpersandToken));
            Optional(SyntaxKind.MutKeyword, parts);
            parts.Add(ParsePatternCore(allowOr: false));
            return Make<PatternSyntax>(SyntaxKind.ReferencePattern, parts);
        }
        if (At(SyntaxKind.BoxKeyword)) return Make<PatternSyntax>(SyntaxKind.BoxPattern, Take(), ParsePatternCore(allowOr: false));
        if (IsLiteral(Current.Kind) || At(SyntaxKind.MinusToken))
        {
            Optional(SyntaxKind.MinusToken, parts);
            if (IsLiteral(Current.Kind)) parts.Add(Take());
            else { Report("Expected a literal in a pattern."); parts.Add(Missing(SyntaxKind.IntegerLiteralToken)); }
            return Make<PatternSyntax>(SyntaxKind.LiteralPattern, parts);
        }
        if (At(SyntaxKind.ConstKeyword)) return Make<PatternSyntax>(SyntaxKind.ConstBlockPattern, Take(), ParseBlock());
        if (At(SyntaxKind.OpenParenToken) || At(SyntaxKind.OpenBracketToken))
        {
            bool tuple = At(SyntaxKind.OpenParenToken);
            parts.Add(Take());
            SyntaxKind close = tuple ? SyntaxKind.CloseParenToken : SyntaxKind.CloseBracketToken;
            int count = 0;
            bool comma = false;
            while (!EndList(close))
            {
                int start = Mark;
                parts.Add(ParsePatternCore()); count++;
                EnsureProgress(start, parts);
                if (!Optional(SyntaxKind.CommaToken, parts)) break;
                comma = true;
            }
            parts.Add(Eat(close));
            return Make<PatternSyntax>(tuple ? count == 1 && !comma ? SyntaxKind.ParenthesizedPattern : SyntaxKind.TuplePattern : SyntaxKind.SlicePattern, parts);
        }
        if (At(SyntaxKind.RefKeyword) || At(SyntaxKind.MutKeyword) || IsName && Peek().Kind is not (SyntaxKind.ColonColonToken or SyntaxKind.OpenParenToken or SyntaxKind.OpenBraceToken or SyntaxKind.BangToken))
        {
            Optional(SyntaxKind.RefKeyword, parts);
            Optional(SyntaxKind.MutKeyword, parts);
            parts.Add(Name());
            if (Optional(SyntaxKind.AtToken, parts)) parts.Add(ParsePatternCore(allowOr: false));
            return Make<PatternSyntax>(SyntaxKind.IdentifierPattern, parts);
        }
        if (PathStart)
        {
            var path = ParsePath(expression: true);
            if (At(SyntaxKind.BangToken)) return Make<PatternSyntax>(SyntaxKind.MacroPattern, ParseMacro(path));
            parts.Add(path);
            if (Optional(SyntaxKind.OpenParenToken, parts))
            {
                while (!EndList(SyntaxKind.CloseParenToken))
                {
                    int start = Mark;
                    parts.Add(ParsePatternCore());
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                }
                parts.Add(Eat(SyntaxKind.CloseParenToken));
                return Make<PatternSyntax>(SyntaxKind.TupleStructPattern, parts);
            }
            if (Optional(SyntaxKind.OpenBraceToken, parts))
            {
                while (!EndList(SyntaxKind.CloseBraceToken))
                {
                    int start = Mark;
                    var field = ParseAttributes();
                    if (Optional(SyntaxKind.DotDotToken, field))
                    {
                        parts.Add(Make<PatternSyntax>(SyntaxKind.RestPattern, field));
                        Optional(SyntaxKind.CommaToken, parts);
                        break;
                    }
                    if ((IsName || At(SyntaxKind.IntegerLiteralToken)) && Peek().Kind == SyntaxKind.ColonToken)
                    {
                        field.Add(Take()); field.Add(Take()); field.Add(ParsePatternCore());
                    }
                    else field.Add(ParsePatternCore(allowOr: false));
                    parts.Add(Make<StructPatternFieldSyntax>(SyntaxKind.StructPatternField, field));
                    EnsureProgress(start, parts);
                    if (!Optional(SyntaxKind.CommaToken, parts)) break;
                }
                parts.Add(Eat(SyntaxKind.CloseBraceToken));
                return Make<PatternSyntax>(SyntaxKind.StructPattern, parts);
            }
            return Make<PatternSyntax>(SyntaxKind.PathPattern, parts);
        }
        Report("Expected a pattern.");
        return Make<PatternSyntax>(SyntaxKind.MissingPattern, Missing(SyntaxKind.IdentifierToken));
    }

    // Expressions
    private static int Precedence(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsToken or SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken or SyntaxKind.StarEqualsToken or
        SyntaxKind.SlashEqualsToken or SyntaxKind.PercentEqualsToken or SyntaxKind.CaretEqualsToken or SyntaxKind.AmpersandEqualsToken or
        SyntaxKind.PipeEqualsToken or SyntaxKind.LessThanLessThanEqualsToken or SyntaxKind.GreaterThanGreaterThanEqualsToken => 1,
        SyntaxKind.DotDotToken or SyntaxKind.DotDotEqualsToken => 2,
        SyntaxKind.PipePipeToken => 3,
        SyntaxKind.AmpersandAmpersandToken => 4,
        SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or
        SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken => 5,
        SyntaxKind.PipeToken => 6,
        SyntaxKind.CaretToken => 7,
        SyntaxKind.AmpersandToken => 8,
        SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken => 9,
        SyntaxKind.PlusToken or SyntaxKind.MinusToken => 10,
        SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken => 11,
        SyntaxKind.AsKeyword => 12,
        _ => 0
    };

    private ExpressionSyntax ParseExpressionCore(int minimum = 1, bool allowStruct = true, bool condition = false, bool statement = false)
    {
        if (!Enter()) return DepthError<ExpressionSyntax>(SyntaxKind.MissingExpression);
        try
        {
            var left = ParsePrefix(allowStruct, condition);
            while (true)
            {
                // Statement blocks require parentheses before a call or binary operator
                bool completeStatement = statement && IsBlockLike(left);
                if (At(SyntaxKind.DotToken)) { left = ParseMemberAccess(left); continue; }
                if (At(SyntaxKind.QuestionToken)) { left = Make<ExpressionSyntax>(SyntaxKind.TryExpression, left, Take()); continue; }
                if (completeStatement) break;
                if (At(SyntaxKind.OpenParenToken)) { left = Make<ExpressionSyntax>(SyntaxKind.CallExpression, left, ParseArguments()); continue; }
                if (At(SyntaxKind.OpenBracketToken))
                {
                    left = Make<ExpressionSyntax>(SyntaxKind.IndexExpression, left, Take(), ParseExpressionCore(), Eat(SyntaxKind.CloseBracketToken));
                    continue;
                }
                int precedence = Precedence(Current.Kind);
                if (precedence == 0 || precedence < minimum) break;
                SyntaxToken op = Take();
                if (precedence == 12)
                {
                    left = Make<ExpressionSyntax>(SyntaxKind.CastExpression, left, op, ParseTypeCore(false));
                    continue;
                }
                if (precedence == 2)
                {
                    if (left.Kind == SyntaxKind.RangeExpression) Report("Range operators cannot be chained.", op.Span);
                    var range = new List<SyntaxNodeOrToken> { left, op };
                    if (ExpressionStart && (allowStruct || !At(SyntaxKind.OpenBraceToken))) range.Add(ParseExpressionCore(3, allowStruct, condition));
                    else if (op.Kind == SyntaxKind.DotDotEqualsToken)
                    {
                        Report("An inclusive range requires an upper bound.");
                        range.Add(Make<ExpressionSyntax>(SyntaxKind.MissingExpression, Missing(SyntaxKind.IdentifierToken)));
                    }
                    left = Make<ExpressionSyntax>(SyntaxKind.RangeExpression, range);
                }
                else
                {
                    if (precedence == 5 && left is BinaryExpressionSyntax binary && Precedence(binary.OperatorToken.Kind) == 5)
                        Report("Comparison operators cannot be chained.", op.Span);
                    var right = ParseExpressionCore(precedence == 1 ? 1 : precedence + 1, allowStruct, condition);
                    left = Make<ExpressionSyntax>(precedence == 1 ? SyntaxKind.AssignmentExpression : SyntaxKind.BinaryExpression, left, op, right);
                }
            }
            return left;
        }
        finally { _depth--; }
    }

    private bool ExpressionStart => PathStart || IsLiteral(Current.Kind) || IsLifetime(Current.Kind) || Current.Kind is
        SyntaxKind.HashToken or SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken or
        SyntaxKind.MinusToken or SyntaxKind.BangToken or SyntaxKind.StarToken or SyntaxKind.AmpersandToken or SyntaxKind.AmpersandAmpersandToken or
        SyntaxKind.DotDotToken or SyntaxKind.DotDotEqualsToken or SyntaxKind.PipeToken or SyntaxKind.PipePipeToken or
        SyntaxKind.MoveKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.UnsafeKeyword or SyntaxKind.ConstKeyword or SyntaxKind.TryKeyword or SyntaxKind.GenKeyword or
        SyntaxKind.IfKeyword or SyntaxKind.MatchKeyword or SyntaxKind.LoopKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword or SyntaxKind.LetKeyword or
        SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword or SyntaxKind.ReturnKeyword or SyntaxKind.YieldKeyword or SyntaxKind.UnderscoreKeyword;

    private static bool IsBlockLike(ExpressionSyntax expression) => expression switch
    {
        BlockExpressionSyntax or IfExpressionSyntax or LoopExpressionSyntax or MatchExpressionSyntax => true,
        MacroExpressionSyntax macro => macro.Body?.OpenToken.Kind == SyntaxKind.OpenBraceToken,
        AttributedExpressionSyntax attributed when attributed.Expression is ExpressionSyntax inner => IsBlockLike(inner),
        _ => false
    };

    private ExpressionSyntax ParsePrefix(bool allowStruct, bool condition)
    {
        if (At(SyntaxKind.HashToken))
        {
            var attributes = ParseAttributes();
            attributes.Add(ParseExpressionCore(13, allowStruct, condition));
            return Make<ExpressionSyntax>(SyntaxKind.AttributedExpression, attributes);
        }
        if (IsLiteral(Current.Kind)) return Make<ExpressionSyntax>(SyntaxKind.LiteralExpression, Take());
        if (At(SyntaxKind.UnderscoreKeyword)) return Make<ExpressionSyntax>(SyntaxKind.UnderscoreExpression, Take());
        if (Ampersand || Current.Kind is SyntaxKind.MinusToken or SyntaxKind.BangToken or SyntaxKind.StarToken)
        {
            var parts = new List<SyntaxNodeOrToken>();
            bool borrow = Ampersand;
            parts.Add(borrow ? Eat(SyntaxKind.AmpersandToken) : Take());
            if (borrow)
            {
                if (Contextual(SyntaxKind.RawKeyword) && Peek().Kind is SyntaxKind.ConstKeyword or SyntaxKind.MutKeyword)
                {
                    parts.Add(Take()); parts.Add(Take());
                }
                else Optional(SyntaxKind.MutKeyword, parts);
            }
            parts.Add(ParseExpressionCore(13, allowStruct, condition));
            return Make<ExpressionSyntax>(SyntaxKind.UnaryExpression, parts);
        }
        if (At(SyntaxKind.DotDotToken) || At(SyntaxKind.DotDotEqualsToken))
        {
            SyntaxToken op = Take();
            var parts = new List<SyntaxNodeOrToken> { op };
            if (ExpressionStart && (allowStruct || !At(SyntaxKind.OpenBraceToken))) parts.Add(ParseExpressionCore(3, allowStruct, condition));
            else if (op.Kind == SyntaxKind.DotDotEqualsToken)
            {
                Report("An inclusive range requires an upper bound.");
                parts.Add(Make<ExpressionSyntax>(SyntaxKind.MissingExpression, Missing(SyntaxKind.IdentifierToken)));
            }
            return Make<ExpressionSyntax>(SyntaxKind.RangeExpression, parts);
        }
        if (At(SyntaxKind.OpenParenToken)) return ParseTupleExpression();
        if (At(SyntaxKind.OpenBracketToken)) return ParseArrayExpression();
        if (At(SyntaxKind.OpenBraceToken)) return ParseBlock();
        if (At(SyntaxKind.IfKeyword)) return ParseIf();
        if (At(SyntaxKind.MatchKeyword)) return ParseMatch();
        if (At(SyntaxKind.ForKeyword) && Peek().Kind == SyntaxKind.LessThanToken) return ParseClosure(allowStruct);
        if (Current.Kind is SyntaxKind.LoopKeyword or SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword) return ParseLoop();
        if (IsLifetime(Current.Kind))
        {
            var label = new List<SyntaxNodeOrToken> { Take(), Eat(SyntaxKind.ColonToken) };
            return At(SyntaxKind.OpenBraceToken) ? ParseBlock(label) : ParseLoop(label);
        }
        if (At(SyntaxKind.LetKeyword))
        {
            if (!condition) Report("A let expression is only allowed in a condition.");
            return Make<ExpressionSyntax>(SyntaxKind.LetExpression, Take(), ParsePatternCore(), Eat(SyntaxKind.EqualsToken), ParseExpressionCore(5, allowStruct));
        }
        if (Current.Kind is SyntaxKind.ReturnKeyword or SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword or SyntaxKind.YieldKeyword)
        {
            SyntaxToken keyword = Take();
            var parts = new List<SyntaxNodeOrToken> { keyword };
            if (keyword.Kind is SyntaxKind.BreakKeyword or SyntaxKind.ContinueKeyword && IsLifetime(Current.Kind)) parts.Add(Take());
            if (keyword.Kind != SyntaxKind.ContinueKeyword && ExpressionStart && (allowStruct || !At(SyntaxKind.OpenBraceToken)))
                parts.Add(ParseExpressionCore(1, allowStruct));
            return Make<ExpressionSyntax>(SyntaxKind.ControlFlowExpression, parts);
        }
        if (Current.Kind is SyntaxKind.PipeToken or SyntaxKind.PipePipeToken or SyntaxKind.MoveKeyword ||
            At(SyntaxKind.AsyncKeyword) && (Peek().Kind != SyntaxKind.OpenBraceToken && !(Peek().Kind == SyntaxKind.MoveKeyword && Peek(2).Kind == SyntaxKind.OpenBraceToken)))
            return ParseClosure(allowStruct);
        if (Current.Kind is SyntaxKind.UnsafeKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.ConstKeyword or SyntaxKind.TryKeyword or SyntaxKind.GenKeyword)
        {
            var parts = new List<SyntaxNodeOrToken> { Take() };
            if (parts[0].Token.Kind is SyntaxKind.AsyncKeyword or SyntaxKind.GenKeyword) Optional(SyntaxKind.MoveKeyword, parts);
            return ParseBlock(parts);
        }
        if (PathStart)
        {
            var path = ParsePath(expression: true);
            if (At(SyntaxKind.BangToken)) return ParseMacro(path);
            if (allowStruct && At(SyntaxKind.OpenBraceToken)) return ParseStructExpression(path);
            return Make<ExpressionSyntax>(SyntaxKind.PathExpression, path);
        }
        Report("Expected an expression.");
        return Make<ExpressionSyntax>(SyntaxKind.MissingExpression, Missing(SyntaxKind.IdentifierToken));
    }

    private ExpressionSyntax ParseMemberAccess(ExpressionSyntax receiver)
    {
        SyntaxToken dot = Eat(SyntaxKind.DotToken);
        if (At(SyntaxKind.AwaitKeyword)) return Make<ExpressionSyntax>(SyntaxKind.AwaitExpression, receiver, dot, Take());
        if (At(SyntaxKind.FloatingLiteralToken) && Current.SuffixLength == 0)
        {
            // Split tuple indices in x.0.1 without losing spans or trivia
            string text = Current.GetText(_source);
            int point = text.IndexOf('.');
            if (point > 0 && text.All(c => c is >= '0' and <= '9' or '.'))
            {
                SyntaxToken number = Take();
                SyntaxToken first = TupleIndex(number, 0, point, number.LeadingTrivia, Array.Empty<SyntaxTrivia>());
                receiver = Make<ExpressionSyntax>(SyntaxKind.MemberAccessExpression, receiver, dot, first);
                var innerDot = number.WithKind(SyntaxKind.DotToken, new TextSpan(number.Span.Start + point, 1), null, ".",
                    Array.Empty<SyntaxTrivia>(), point == text.Length - 1 ? number.TrailingTrivia : Array.Empty<SyntaxTrivia>());
                if (point == text.Length - 1) _splitToken = innerDot;
                else receiver = Make<ExpressionSyntax>(SyntaxKind.MemberAccessExpression, receiver, innerDot,
                    TupleIndex(number, point + 1, text.Length - point - 1, Array.Empty<SyntaxTrivia>(), number.TrailingTrivia));
                return receiver;
            }
        }
        var parts = new List<SyntaxNodeOrToken> { receiver, dot };
        if (At(SyntaxKind.IntegerLiteralToken))
        {
            var index = Take();
            ValidateTupleIndex(index.GetText(_source), index.Span);
            parts.Add(index);
        }
        else parts.Add(Name());
        if (Optional(SyntaxKind.ColonColonToken, parts)) parts.Add(ParseGenericArguments());
        return Make<ExpressionSyntax>(SyntaxKind.MemberAccessExpression, parts);
    }

    private SyntaxToken TupleIndex(SyntaxToken original, int offset, int length, SyntaxTrivia[] leading, SyntaxTrivia[] trailing)
    {
        var span = new TextSpan(original.Span.Start + offset, length);
        string text = original.GetText(_source).Substring(offset, length);
        ValidateTupleIndex(text, span);
        ulong.TryParse(text, out ulong value);
        return original.WithKind(SyntaxKind.IntegerLiteralToken, span, null, text, leading, trailing, value);
    }

    private void ValidateTupleIndex(string text, TextSpan span)
    {
        if (text.Length == 0 || text.Length > 1 && text[0] == '0' || text.Any(c => c is < '0' or > '9'))
            Report("A tuple index must be an unsuffixed decimal integer without leading zeros.", span);
    }

    private ArgumentListSyntax ParseArguments()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.OpenParenToken) };
        while (!EndList(SyntaxKind.CloseParenToken))
        {
            int start = Mark;
            parts.Add(ParseExpressionCore());
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(SyntaxKind.CloseParenToken));
        return Make<ArgumentListSyntax>(SyntaxKind.ArgumentList, parts);
    }

    private ExpressionSyntax ParseTupleExpression()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.OpenParenToken) };
        parts.AddRange(ParseAttributes(innerAllowed: true));
        int count = 0;
        bool comma = false;
        while (!EndList(SyntaxKind.CloseParenToken))
        {
            int start = Mark;
            parts.Add(ParseExpressionCore()); count++;
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
            comma = true;
        }
        parts.Add(Eat(SyntaxKind.CloseParenToken));
        return Make<ExpressionSyntax>(count == 1 && !comma ? SyntaxKind.ParenthesizedExpression : SyntaxKind.TupleExpression, parts);
    }

    private ExpressionSyntax ParseArrayExpression()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.OpenBracketToken) };
        parts.AddRange(ParseAttributes(innerAllowed: true));
        SyntaxKind kind = SyntaxKind.ArrayExpression;
        if (!At(SyntaxKind.CloseBracketToken))
        {
            parts.Add(ParseExpressionCore());
            if (Optional(SyntaxKind.SemicolonToken, parts))
            {
                parts.Add(ParseExpressionCore()); kind = SyntaxKind.RepeatArrayExpression;
            }
            else
            {
                while (Optional(SyntaxKind.CommaToken, parts) && !EndList(SyntaxKind.CloseBracketToken))
                {
                    int start = Mark;
                    parts.Add(ParseExpressionCore());
                    EnsureProgress(start, parts);
                }
            }
        }
        parts.Add(Eat(SyntaxKind.CloseBracketToken));
        return Make<ExpressionSyntax>(kind, parts);
    }

    private StructExpressionSyntax ParseStructExpression(PathSyntax path)
    {
        var parts = new List<SyntaxNodeOrToken> { path, Eat(SyntaxKind.OpenBraceToken) };
        parts.AddRange(ParseAttributes(innerAllowed: true));
        while (!EndList(SyntaxKind.CloseBraceToken))
        {
            int start = Mark;
            if (Optional(SyntaxKind.DotDotToken, parts))
            {
                parts.Add(ParseExpressionCore());
                Optional(SyntaxKind.CommaToken, parts);
                break;
            }
            var field = ParseAttributes();
            bool numeric = At(SyntaxKind.IntegerLiteralToken);
            field.Add(numeric ? Take() : Name());
            if (Optional(SyntaxKind.ColonToken, field)) field.Add(ParseExpressionCore());
            else if (numeric) { field.Add(Eat(SyntaxKind.ColonToken)); field.Add(ParseExpressionCore()); }
            parts.Add(Make<StructExpressionFieldSyntax>(SyntaxKind.StructExpressionField, field));
            EnsureProgress(start, parts);
            if (!Optional(SyntaxKind.CommaToken, parts)) break;
        }
        parts.Add(Eat(SyntaxKind.CloseBraceToken));
        return Make<StructExpressionSyntax>(SyntaxKind.StructExpression, parts);
    }

    // Blocks and control flow
    private BlockExpressionSyntax ParseBlock(List<SyntaxNodeOrToken>? prefix = null)
    {
        if (!Enter()) return DepthError<BlockExpressionSyntax>(SyntaxKind.BlockExpression);
        try
        {
            var parts = prefix ?? new List<SyntaxNodeOrToken>();
            parts.Add(Eat(SyntaxKind.OpenBraceToken));
            parts.AddRange(ParseAttributes(innerAllowed: true));
            while (!EndList(SyntaxKind.CloseBraceToken))
            {
                int start = Mark;
                if (At(SyntaxKind.SemicolonToken)) parts.Add(Make<StatementSyntax>(SyntaxKind.EmptyStatement, Take()));
                else
                {
                    var attributes = ParseAttributes();
                    if (ItemStart) parts.Add(Make<StatementSyntax>(SyntaxKind.ItemStatement, ParseItem(attributes)));
                    else if (At(SyntaxKind.LetKeyword)) parts.Add(ParseLetStatement(attributes));
                    else
                    {
                        ExpressionSyntax expression = ParseExpressionCore(statement: true);
                        if (attributes.Count != 0)
                        {
                            attributes.Add(expression);
                            expression = Make<ExpressionSyntax>(SyntaxKind.AttributedExpression, attributes);
                        }
                        if (At(SyntaxKind.SemicolonToken)) parts.Add(Make<StatementSyntax>(SyntaxKind.ExpressionStatement, expression, Take()));
                        else if (At(SyntaxKind.CloseBraceToken)) { parts.Add(expression); break; }
                        else if (IsBlockLike(expression)) parts.Add(Make<StatementSyntax>(SyntaxKind.ExpressionStatement, expression));
                        else parts.Add(Make<StatementSyntax>(SyntaxKind.ExpressionStatement, expression, Eat(SyntaxKind.SemicolonToken)));
                    }
                }
                EnsureProgress(start, parts);
            }
            parts.Add(Eat(SyntaxKind.CloseBraceToken));
            return Make<BlockExpressionSyntax>(SyntaxKind.BlockExpression, parts);
        }
        finally { _depth--; }
    }

    private LetStatementSyntax ParseLetStatement(List<SyntaxNodeOrToken> parts)
    {
        parts.Add(Eat(SyntaxKind.LetKeyword));
        parts.Add(ParsePatternCore(allowOr: false));
        if (At(SyntaxKind.ColonToken)) parts.Add(ParseTypeAnnotation());
        if (At(SyntaxKind.EqualsToken)) parts.Add(Make<EqualsValueClauseSyntax>(SyntaxKind.EqualsValueClause, Take(), ParseExpressionCore()));
        if (Optional(SyntaxKind.ElseKeyword, parts)) parts.Add(ParseBlock());
        parts.Add(Eat(SyntaxKind.SemicolonToken));
        return Make<LetStatementSyntax>(SyntaxKind.LetStatement, parts);
    }

    private IfExpressionSyntax ParseIf()
    {
        if (!Enter()) return DepthError<IfExpressionSyntax>(SyntaxKind.IfExpression);
        try
        {
            var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.IfKeyword), ParseExpressionCore(allowStruct: false, condition: true), ParseBlock() };
            if (Optional(SyntaxKind.ElseKeyword, parts)) parts.Add(At(SyntaxKind.IfKeyword) ? ParseIf() : ParseBlock());
            return Make<IfExpressionSyntax>(SyntaxKind.IfExpression, parts);
        }
        finally { _depth--; }
    }

    private LoopExpressionSyntax ParseLoop(List<SyntaxNodeOrToken>? prefix = null)
    {
        var parts = prefix ?? new List<SyntaxNodeOrToken>();
        if (Optional(SyntaxKind.ForKeyword, parts))
        {
            parts.Add(ParsePatternCore()); parts.Add(Eat(SyntaxKind.InKeyword));
            parts.Add(ParseExpressionCore(allowStruct: false));
        }
        else if (Optional(SyntaxKind.WhileKeyword, parts)) parts.Add(ParseExpressionCore(allowStruct: false, condition: true));
        else parts.Add(Eat(SyntaxKind.LoopKeyword));
        parts.Add(ParseBlock());
        return Make<LoopExpressionSyntax>(SyntaxKind.LoopExpression, parts);
    }

    private MatchExpressionSyntax ParseMatch()
    {
        var parts = new List<SyntaxNodeOrToken> { Eat(SyntaxKind.MatchKeyword), ParseExpressionCore(allowStruct: false), Eat(SyntaxKind.OpenBraceToken) };
        parts.AddRange(ParseAttributes(innerAllowed: true));
        while (!EndList(SyntaxKind.CloseBraceToken))
        {
            int start = Mark;
            var arm = ParseAttributes();
            arm.Add(ParsePatternCore());
            if (Optional(SyntaxKind.IfKeyword, arm)) arm.Add(ParseExpressionCore());
            arm.Add(Eat(SyntaxKind.EqualsGreaterThanToken));
            ExpressionSyntax body = ParseExpressionCore(statement: true);
            arm.Add(body);
            if (!Optional(SyntaxKind.CommaToken, arm) && !At(SyntaxKind.CloseBraceToken) && !IsBlockLike(body)) arm.Add(Eat(SyntaxKind.CommaToken));
            parts.Add(Make<MatchArmSyntax>(SyntaxKind.MatchArm, arm));
            EnsureProgress(start, parts);
        }
        parts.Add(Eat(SyntaxKind.CloseBraceToken));
        return Make<MatchExpressionSyntax>(SyntaxKind.MatchExpression, parts);
    }

    private ClosureExpressionSyntax ParseClosure(bool allowStruct)
    {
        var parts = new List<SyntaxNodeOrToken>();
        if (At(SyntaxKind.ForKeyword)) parts.Add(ParseLifetimeBinder());
        Optional(SyntaxKind.AsyncKeyword, parts);
        Optional(SyntaxKind.MoveKeyword, parts);
        parts.Add(ParseParameters(closure: true));
        if (At(SyntaxKind.MinusGreaterThanToken)) { parts.Add(ParseReturnType()); parts.Add(ParseBlock()); }
        else parts.Add(ParseExpressionCore(allowStruct: allowStruct));
        return Make<ClosureExpressionSyntax>(SyntaxKind.ClosureExpression, parts);
    }
}
