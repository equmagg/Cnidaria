using System;
using System.Collections.Generic;
using System.Linq;

namespace Cnidaria.Cs
{
    [Flags]
    internal enum ParseContext
    {
        None = 0,

        // Structural
        CompilationUnit = 1 << 0,
        NamespaceMembers = 1 << 1,
        TypeMembers = 1 << 2,
        Statement = 1 << 3,
        Expression = 1 << 4,
        Type = 1 << 5,

        // Feature flags
        Async = 1 << 16,
        Iterator = 1 << 17,
        Unsafe = 1 << 18,

    }
    internal enum ModifierContext
    {
        Member,
        Type,
        Local,
        LocalFunction,
        Accessor,
        Parameter,
    }
    internal sealed class ParseContextStack
    {
        private ParseContext _combined;
        private readonly List<ParseContext> _stack = new();

        public ParseContext Combined => _combined;
        public bool HasAny(ParseContext flags) => (_combined & flags) != 0;
        public bool HasAll(ParseContext flags) => (_combined & flags) == flags;
        public bool Has(ParseContext flags) => (_combined & flags) != 0;

        public ContextMark Mark() => new ContextMark(_stack.Count, _combined);

        public void Reset(in ContextMark mark)
        {
            if (_stack.Count > mark.Depth)
                _stack.RemoveRange(mark.Depth, _stack.Count - mark.Depth);

            _combined = mark.Combined;
        }
        public Scope Push(ParseContext flags)
        {
            if (flags == ParseContext.None)
                return default;

            int depth = _stack.Count;
            _stack.Add(flags);
            _combined |= flags;
            return new Scope(this, depth);
        }

        private void PopTo(int depth)
        {
            if ((uint)depth > (uint)_stack.Count)
                throw new ArgumentOutOfRangeException(nameof(depth));

            if (_stack.Count == depth)
                return;

            _stack.RemoveRange(depth, _stack.Count - depth);

            ParseContext c = ParseContext.None;
            for (int i = 0; i < _stack.Count; i++)
                c |= _stack[i];

            _combined = c;
        }

        internal readonly struct ContextMark
        {
            public readonly int Depth;
            public readonly ParseContext Combined;
            public ContextMark(int depth, ParseContext combined) { Depth = depth; Combined = combined; }
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly ParseContextStack? _owner;
            private readonly int _depth;

            internal Scope(ParseContextStack owner, int depth) { _owner = owner; _depth = depth; }
            public void Dispose() => _owner?.PopTo(_depth);
        }
    }
    public sealed class Parser
    {
        private readonly struct ResetPoint
        {
            public readonly SlidingTokenWindow.TokenWindowMark TokenMark;
            public readonly ParseContextStack.ContextMark CtxMark;
            public readonly int DiagnosticCount;

            public ResetPoint(
                SlidingTokenWindow.TokenWindowMark tokenMark,
                ParseContextStack.ContextMark ctxMark,
                int diagnosticCount)
            {
                TokenMark = tokenMark;
                CtxMark = ctxMark;
                DiagnosticCount = diagnosticCount;
            }
        }

        private readonly SlidingTokenWindow _tokens;
        private readonly List<SyntaxDiagnostic> _diagnostics = new();
        private readonly ParseContextStack _ctx = new();
        public IReadOnlyList<SyntaxDiagnostic> Diagnostics => _diagnostics;
        public IReadOnlyList<SyntaxDiagnostic> LexerDiagnostics => _tokens.LexerDiagnostics;

        public Parser(string text, LexerOptions? lexerOptions = null)
        {
            var lexer = new Lexer(text, lexerOptions ?? new LexerOptions());
            _tokens = new SlidingTokenWindow(lexer);
        }
        public CompilationUnitSyntax Parse()
        {
            using var __ = _ctx.Push(ParseContext.CompilationUnit);
            return ParseCompilationUnit();
        }

        // helpers
        private ResetPoint GetResetPoint()
            => new ResetPoint(_tokens.MarkState(), _ctx.Mark(), _diagnostics.Count);
        private bool Probe(Func<bool> scan, ParseContext tempContext = ParseContext.None, bool requireProgress = true)
        {
            var rp = GetResetPoint();
            int startPos = _tokens.Position;

            using var __ = _ctx.Push(tempContext);

            bool ok = scan();

            if (requireProgress && _tokens.Position == startPos)
                ok = false;
            Reset(rp);

            return ok;
        }
        private bool TryParse<T>(
            Func<T> parse,
            out T result,
            ParseContext tempContext = ParseContext.None,
            bool requireProgress = true,
            bool requireNoNewDiagnostics = true,
            Func<T, bool>? validateNode = null,
            Func<bool>? validateAfter = null)
        {
            var rp = GetResetPoint();
            int startPos = _tokens.Position;
            int startDiag = rp.DiagnosticCount;

            using var __ = _ctx.Push(tempContext);

            T node = parse();

            bool ok = true;

            if (requireProgress && _tokens.Position == startPos)
                ok = false;

            if (requireNoNewDiagnostics && _diagnostics.Count != startDiag)
                ok = false;

            if (ok && validateNode != null && !validateNode(node))
                ok = false;

            if (ok && validateAfter != null && !validateAfter())
                ok = false;

            if (!ok)
            {
                Reset(rp);
                result = default!;
                return false;
            }

            result = node;
            return true;
        }
        private void Reset(in ResetPoint rp)
        {
            _tokens.Reset(rp.TokenMark);
            _ctx.Reset(rp.CtxMark);
            RollbackDiagnostics(rp.DiagnosticCount);
        }
        private SyntaxToken MatchToken(SyntaxKind kind)
        {
            if (_tokens.CurrentKind == kind)
                return _tokens.EatToken();

            // missing token
            var pos = _tokens.Current.Span.Start;
            _diagnostics.Add(new SyntaxDiagnostic(pos, $"Expected token '{kind}', found '{_tokens.CurrentKind}'."));
            return CreateMissingToken(kind, pos);
        }
        private void RollbackDiagnostics(int count)
        {
            if (_diagnostics.Count > count)
                _diagnostics.RemoveRange(count, _diagnostics.Count - count);
        }
        private bool IsInAsyncContext() => _ctx.Has(ParseContext.Async);

        private bool IsAwaitKeyword()
            => IsInAsyncContext() && IsCurrentContextual(SyntaxKind.AwaitKeyword);

        private bool IsYieldKeywordInStatementContext()
            => _ctx.Has(ParseContext.Statement) && IsCurrentContextual(SyntaxKind.YieldKeyword);
        private SyntaxToken MatchContextualKeyword(SyntaxKind contextualKind)
        {
            var t = _tokens.Current;

            if (t.Kind == SyntaxKind.IdentifierToken && t.ContextualKind == contextualKind)
                return _tokens.EatToken();

            var pos = t.Span.Start;
            _diagnostics.Add(new SyntaxDiagnostic(pos, $"Expected contextual keyword '{contextualKind}'."));
            return CreateMissingContextualToken(contextualKind, pos);
        }
        private void EatAsSkippedToken(string message)
        {
            var t = _tokens.Current;
            _diagnostics.Add(new SyntaxDiagnostic(t.Span.Start, message));
            _tokens.EatToken();
        }
        private bool TryEatToken(SyntaxKind kind, out SyntaxToken token)
        {
            if (_tokens.Current.Kind == kind)
            {
                token = _tokens.EatToken();
                return true;
            }

            token = default;
            return false;
        }
        private SyntaxToken EatOptionalToken(SyntaxKind kind)
            => TryEatToken(kind, out var t) ? t : default;
        private SyntaxToken EatOptionalContextualKeyword(SyntaxKind contextualKind)
            => TryEatContextualKeyword(contextualKind, out var t) ? t : default;
        private bool TryEatContextualKeyword(SyntaxKind contextualKind, out SyntaxToken token)
        {
            if (IsCurrentContextual(contextualKind))
            {
                token = _tokens.EatToken();
                return true;
            }

            token = default;
            return false;
        }
        private bool IsCurrentContextual(SyntaxKind contextualKind)
            => _tokens.Current.Kind == SyntaxKind.IdentifierToken && _tokens.Current.ContextualKind == contextualKind;
        private static SyntaxToken CreateMissingToken(SyntaxKind kind, int position)
        {
            return new SyntaxToken(
                kind,
                new TextSpan(position, 0),
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());
        }

        private static SyntaxToken CreateMissingContextualToken(SyntaxKind contextualKind, int position)
        {
            return new SyntaxToken(
                SyntaxKind.IdentifierToken,
                contextualKind,
                new TextSpan(position, 0),
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());
        }
        private IdentifierNameSyntax CreateMissingIdentifierName(int position)
        {
            var id = new SyntaxToken(
                SyntaxKind.IdentifierToken,
                new TextSpan(position, 0),
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: Array.Empty<SyntaxTrivia>());

            return new IdentifierNameSyntax(id);
        }
        private CompilationUnitSyntax ParseCompilationUnit()
        {
            var externs = new List<ExternAliasDirectiveSyntax>();
            var usings = new List<UsingDirectiveSyntax>();
            var attributeLists = new List<AttributeListSyntax>();
            var members = new List<MemberDeclarationSyntax>();

            while (_tokens.CurrentKind == SyntaxKind.ExternKeyword)
                externs.Add(ParseExternAliasDirective());

            while (TryParseUsingDirective(out var u))
                usings.Add(u);
            while (IsGlobalAttributeListStart())
                attributeLists.Add(ParseAttributeList());

            bool seenNonGlobalMember = false;
            bool seenTopLevelStatement = false;
            bool seenFileScopedNamespace = false;

            while (_tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                if (TryParseUsingDirective(out var misplacedUsing))
                {
                    _diagnostics.Add(new SyntaxDiagnostic(
                        misplacedUsing.Span.Start,
                        "using directives must precede all other elements defined in the file."));
                    usings.Add(misplacedUsing);
                    continue;
                }

                MemberDeclarationSyntax member = ParseCompilationUnitMember(
                    ref seenNonGlobalMember,
                    ref seenTopLevelStatement,
                    ref seenFileScopedNamespace);

                members.Add(member);

                if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Parser made no progress in compilation unit member parsing.");
            }

            var eof = MatchToken(SyntaxKind.EndOfFileToken);

            return new CompilationUnitSyntax(
                new SyntaxList<AttributeListSyntax>(attributeLists.ToArray()),
                new SyntaxList<ExternAliasDirectiveSyntax>(externs.ToArray()),
                new SyntaxList<UsingDirectiveSyntax>(usings.ToArray()),
                new SyntaxList<MemberDeclarationSyntax>(members.ToArray()),
                eof);
        }
        private MemberDeclarationSyntax ParseCompilationUnitMember(
            ref bool seenNonGlobalMember, ref bool seenTopLevelStatement, ref bool seenFileScopedNamespace)
        {
            var mark = _tokens.MarkState();
            int diagStart = _diagnostics.Count;

            var attrs = ParseAttributeLists();

            if (_tokens.CurrentKind == SyntaxKind.NamespaceKeyword)
            {
                int nsStart = _tokens.Current.Span.Start;
                var ns = ParseNamespaceDeclaration(attrs);

                if (ns.Kind == SyntaxKind.FileScopedNamespaceDeclaration)
                {
                    if (seenTopLevelStatement)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(
                            nsStart,
                            "A compilation unit cannot contain both a file-scoped namespace declaration and top-level statements."));
                    }

                    if (seenNonGlobalMember)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(
                            nsStart,
                            "type declarations cannot precede a file-scoped namespace declaration."));
                    }

                    if (seenFileScopedNamespace)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(
                            nsStart,
                            "A source file cannot contain multiple file-scoped namespace declarations."));
                    }

                    seenFileScopedNamespace = true;
                }
                else
                {
                    // block namespace
                    if (seenFileScopedNamespace)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(
                            nsStart,
                            "A source file cannot contain both a file-scoped namespace declaration and a block namespace declaration."));
                    }
                }

                seenNonGlobalMember = true;
                return ns;
            }

            // type decl
            var modifiers = ParseModifiers(ModifierContext.Type);

            if (IsTypeDeclarationKeyword(_tokens.CurrentKind))
            {
                seenNonGlobalMember = true;
                return ParseTypeDeclarationAfterModifiers(attrs, modifiers);
            }

            _tokens.Reset(mark);
            RollbackDiagnostics(diagStart);

            if (seenNonGlobalMember)
            {
                _diagnostics.Add(new SyntaxDiagnostic(
                    _tokens.Current.Span.Start,
                    "Top-level statements must precede namespace and type declarations."));
            }

            if (seenFileScopedNamespace)
            {
                _diagnostics.Add(new SyntaxDiagnostic(
                    _tokens.Current.Span.Start,
                    "A compilation unit cannot contain both a file-scoped namespace declaration and top-level statements."));
            }

            seenTopLevelStatement = true;

            using var __ = _ctx.Push(ParseContext.Async);
            return new GlobalStatementSyntax(SyntaxList<AttributeListSyntax>.Empty, ParseStatement());
        }
        private MemberDeclarationSyntax ParseNamespaceMemberDeclaration()
        {
            var attrs = ParseAttributeLists();

            if (_tokens.CurrentKind == SyntaxKind.NamespaceKeyword)
                return ParseNamespaceDeclaration(attrs);

            var mark = _tokens.MarkState();
            int diagStart = _diagnostics.Count;

            var modifiers = ParseModifiers(ModifierContext.Type);

            if (IsTypeDeclarationKeyword(_tokens.CurrentKind))
                return ParseTypeDeclarationAfterModifiers(attrs, modifiers);

            _tokens.Reset(mark);
            RollbackDiagnostics(diagStart);

            _diagnostics.Add(new SyntaxDiagnostic(
                _tokens.Current.Span.Start,
                "A namespace cannot directly contain statements."));

            return new GlobalStatementSyntax(SyntaxList<AttributeListSyntax>.Empty, ParseStatement());
        }
        private MemberDeclarationSyntax ParseMemberDeclaration()
        {
            var attrs = ParseAttributeLists();

            if (_tokens.CurrentKind == SyntaxKind.NamespaceKeyword)
                return ParseNamespaceDeclaration(attrs);

            var mark = _tokens.MarkState();
            int diagStart = _diagnostics.Count;

            var modifiers = ParseModifiers(ModifierContext.Type);

            if (_tokens.CurrentKind == SyntaxKind.DelegateKeyword)
                return ParseDelegateDeclarationAfterModifiers(attrs, modifiers);

            if (IsTypeDeclarationKeyword(_tokens.CurrentKind))
                return ParseTypeDeclarationAfterModifiers(attrs, modifiers);

            _tokens.Reset(mark);
            RollbackDiagnostics(diagStart);

            return new GlobalStatementSyntax(SyntaxList<AttributeListSyntax>.Empty, ParseStatement());
        }
        private SyntaxTokenList ParseModifiers(ModifierContext ctx)
        {
            var list = new List<SyntaxToken>();
            while (IsModifierToken(_tokens.Current, ctx))
                list.Add(_tokens.EatToken());
            return new SyntaxTokenList(list.ToArray());
        }
        private bool IsGlobalAttributeListStart()
        {
            // [assembly: ...] or [module: ...]
            if (_tokens.CurrentKind != SyntaxKind.OpenBracketToken)
                return false;

            var t1 = _tokens.Peek(1).Kind;
            var t2 = _tokens.Peek(2).Kind;
            return (t1 == SyntaxKind.AssemblyKeyword || t1 == SyntaxKind.ModuleKeyword) && t2 == SyntaxKind.ColonToken;
        }
        private SyntaxList<AttributeListSyntax> ParseAttributeLists()
        {
            var lists = new List<AttributeListSyntax>();
            while (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                lists.Add(ParseAttributeList());
            return new SyntaxList<AttributeListSyntax>(lists.ToArray());
        }
        private AttributeListSyntax ParseAttributeList()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);

            AttributeTargetSpecifierSyntax? target = null;
            if (_tokens.Peek(1).Kind == SyntaxKind.ColonToken)
            {
                var id = _tokens.EatToken();
                var colon = MatchToken(SyntaxKind.ColonToken);
                target = new AttributeTargetSpecifierSyntax(id, colon);
            }

            var attrs = ParseSeparatedAttributes(closeKind: SyntaxKind.CloseBracketToken);
            var close = MatchToken(SyntaxKind.CloseBracketToken);

            return new AttributeListSyntax(open, target, attrs, close);
        }
        private SeparatedSyntaxList<AttributeSyntax> ParseSeparatedAttributes(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == closeKind)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Attribute list cannot be empty."));
                var missingName = (NameSyntax)CreateMissingIdentifierName(_tokens.Current.Span.Start);
                list.Add(new SyntaxNodeOrToken(new AttributeSyntax(missingName, argumentList: null)));
                return new SeparatedSyntaxList<AttributeSyntax>(list.ToArray());
            }

            while (_tokens.CurrentKind != closeKind && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var a = ParseAttribute();
                list.Add(new SyntaxNodeOrToken(a));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    if (_tokens.CurrentKind == closeKind) // allow trailing comma for recovery
                        break;
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in attribute parsing.");
                break;
            }

            return new SeparatedSyntaxList<AttributeSyntax>(list.ToArray());
        }
        private AttributeSyntax ParseAttribute()
        {
            var name = ParseName();
            AttributeArgumentListSyntax? args = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                args = ParseAttributeArgumentList();
            return new AttributeSyntax(name, args);
        }
        private AttributeArgumentListSyntax ParseAttributeArgumentList()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var args = ParseSeparatedAttributeArguments(closeKind: SyntaxKind.CloseParenToken);
            var close = MatchToken(SyntaxKind.CloseParenToken);
            return new AttributeArgumentListSyntax(open, args, close);
        }
        private SeparatedSyntaxList<AttributeArgumentSyntax> ParseSeparatedAttributeArguments(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == closeKind)
                return new SeparatedSyntaxList<AttributeArgumentSyntax>(list.ToArray());

            while (_tokens.CurrentKind != closeKind && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var a = ParseAttributeArgument();
                list.Add(new SyntaxNodeOrToken(a));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    if (_tokens.CurrentKind == closeKind)
                        break;
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in attribute argument parsing.");
                break;
            }

            return new SeparatedSyntaxList<AttributeArgumentSyntax>(list.ToArray());
        }
        private AttributeArgumentSyntax ParseAttributeArgument()
        {
            NameEqualsSyntax? nameEquals = null;
            NameColonSyntax? nameColon = null;

            // name = expr
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken && _tokens.Peek(1).Kind == SyntaxKind.EqualsToken)
            {
                var id = new IdentifierNameSyntax(_tokens.EatToken());
                var eq = _tokens.EatToken();
                nameEquals = new NameEqualsSyntax(id, eq);
            }
            // name: expr
            else if (_tokens.CurrentKind == SyntaxKind.IdentifierToken && _tokens.Peek(1).Kind == SyntaxKind.ColonToken)
            {
                var id = new IdentifierNameSyntax(_tokens.EatToken());
                var colon = _tokens.EatToken();
                nameColon = new NameColonSyntax(id, colon);
            }

            var expr = ParseExpression();
            return new AttributeArgumentSyntax(nameEquals, nameColon, expr);
        }
        // directives
        private ExternAliasDirectiveSyntax ParseExternAliasDirective()
        {
            var externKeyword = MatchToken(SyntaxKind.ExternKeyword);

            var aliasKeyword = MatchContextualKeyword(SyntaxKind.AliasKeyword);

            var id = MatchToken(SyntaxKind.IdentifierToken);
            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new ExternAliasDirectiveSyntax(externKeyword, aliasKeyword, id, semi);
        }
        private bool TryParseUsingDirective(out UsingDirectiveSyntax directive)
        {
            return TryParse(
                parse: ParseUsingDirective,
                out directive,
                tempContext: ParseContext.None,
                requireProgress: true,
                requireNoNewDiagnostics: true);
        }
        private UsingDirectiveSyntax ParseUsingDirective()
        {
            // global?
            var globalKeyword = EatOptionalContextualKeyword(SyntaxKind.GlobalKeyword);

            var usingKeyword = MatchToken(SyntaxKind.UsingKeyword);

            // static?
            var staticKeyword = EatOptionalToken(SyntaxKind.StaticKeyword);

            // alias?
            NameEqualsSyntax? alias = null;
            if (_tokens.Current.Kind == SyntaxKind.IdentifierToken && _tokens.Peek(1).Kind == SyntaxKind.EqualsToken)
            {
                var id = new IdentifierNameSyntax(_tokens.EatToken());
                var eq = _tokens.EatToken();
                alias = new NameEqualsSyntax(id, eq);

                if (_tokens.Current.Kind == SyntaxKind.StaticKeyword)
                    _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "using alias with 'static' is not supported."));
            }

            var name = ParseName();
            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new UsingDirectiveSyntax(globalKeyword, usingKeyword, staticKeyword, alias, name, semi);
        }


        // namespaces
        private MemberDeclarationSyntax ParseNamespaceDeclaration(SyntaxList<AttributeListSyntax> attributeLists)
        {
            using var __ = _ctx.Push(ParseContext.NamespaceMembers);
            var nsKeyword = MatchToken(SyntaxKind.NamespaceKeyword);
            var name = ParseName();

            // file scoped
            if (_tokens.CurrentKind == SyntaxKind.SemicolonToken)
            {
                var semi = _tokens.EatToken();

                var externs = new List<ExternAliasDirectiveSyntax>();
                var usings = new List<UsingDirectiveSyntax>();
                var members = new List<MemberDeclarationSyntax>();

                while (_tokens.CurrentKind == SyntaxKind.ExternKeyword)
                    externs.Add(ParseExternAliasDirective());

                while (TryParseUsingDirective(out var u))
                    usings.Add(u);

                while (_tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                {
                    var start = _tokens.Position;
                    members.Add(ParseNamespaceMemberDeclaration());
                    if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                        EatAsSkippedToken("Parser made no progress in file-scoped namespace member parsing.");
                }

                return new FileScopedNamespaceDeclarationSyntax(
                    attributeLists, nsKeyword, name, semi,
                    new SyntaxList<ExternAliasDirectiveSyntax>(externs.ToArray()),
                    new SyntaxList<UsingDirectiveSyntax>(usings.ToArray()),
                    new SyntaxList<MemberDeclarationSyntax>(members.ToArray()));
            }

            // block namespace
            var open = MatchToken(SyntaxKind.OpenBraceToken);

            var externs2 = new List<ExternAliasDirectiveSyntax>();
            var usings2 = new List<UsingDirectiveSyntax>();
            var members2 = new List<MemberDeclarationSyntax>();

            while (_tokens.CurrentKind == SyntaxKind.ExternKeyword)
                externs2.Add(ParseExternAliasDirective());


            while (TryParseUsingDirective(out var u))
                usings2.Add(u);

            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                members2.Add(ParseMemberDeclaration());
                if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Parser made no progress in namespace member parsing.");
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            var semi2 = EatOptionalToken(SyntaxKind.SemicolonToken);

            return new NamespaceDeclarationSyntax(
                attributeLists,
                nsKeyword,
                name,
                open,
                new SyntaxList<ExternAliasDirectiveSyntax>(externs2.ToArray()),
                new SyntaxList<UsingDirectiveSyntax>(usings2.ToArray()),
                new SyntaxList<MemberDeclarationSyntax>(members2.ToArray()),
                close,
                semi2);
        }
        // type decl
        private MemberDeclarationSyntax ParseTypeDeclarationAfterModifiers(
            SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers)
        {
            return _tokens.CurrentKind switch
            {
                SyntaxKind.ClassKeyword => ParseClassDeclaration(attributeLists, modifiers),
                SyntaxKind.StructKeyword => ParseStructDeclaration(attributeLists, modifiers),
                SyntaxKind.InterfaceKeyword => ParseInterfaceDeclaration(attributeLists, modifiers),
                SyntaxKind.EnumKeyword => ParseEnumDeclaration(attributeLists, modifiers),
                _ => throw new InvalidOperationException($"Not a type declaration: {_tokens.CurrentKind}")
            };
        }
        private EnumDeclarationSyntax ParseEnumDeclaration(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers)
        {
            if (modifiers.Count == 0)
                modifiers = ParseModifiers(ModifierContext.Type);

            var enumKeyword = MatchToken(SyntaxKind.EnumKeyword);
            var id = MatchToken(SyntaxKind.IdentifierToken);

            BaseListSyntax? baseList = null;
            if (_tokens.CurrentKind == SyntaxKind.ColonToken)
                baseList = ParseBaseList(); // underlying type

            var open = MatchToken(SyntaxKind.OpenBraceToken);

            var list = new List<SyntaxNodeOrToken>();

            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var m = ParseEnumMemberDeclaration();
                list.Add(new SyntaxNodeOrToken(m));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));

                    // allow trailing comma
                    if (_tokens.CurrentKind == SyntaxKind.CloseBraceToken)
                        break;

                    continue;
                }

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in enum member parsing.");
                    break;
                }

                break;
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            var semi = EatOptionalToken(SyntaxKind.SemicolonToken);

            return new EnumDeclarationSyntax(
                attributeLists,
                modifiers,
                enumKeyword,
                id,
                baseList,
                open,
                new SeparatedSyntaxList<EnumMemberDeclarationSyntax>(list.ToArray()),
                close,
                semi);
        }
        private EnumMemberDeclarationSyntax ParseEnumMemberDeclaration()
        {
            var attrs = ParseAttributeLists();
            var id = MatchToken(SyntaxKind.IdentifierToken);

            EqualsValueClauseSyntax? equalsValue = null;
            if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
                equalsValue = ParseEqualsValueClause();

            return new EnumMemberDeclarationSyntax(attrs, id, equalsValue);
        }
        private StructDeclarationSyntax ParseStructDeclaration(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers)
        {
            if (modifiers.Count == 0)
                modifiers = ParseModifiers(ModifierContext.Type);

            var structKeyword = MatchToken(SyntaxKind.StructKeyword);
            var id = MatchToken(SyntaxKind.IdentifierToken);

            TypeParameterListSyntax? typeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                typeParams = ParseTypeParameterList();

            BaseListSyntax? baseList = null;
            if (_tokens.CurrentKind == SyntaxKind.ColonToken)
                baseList = ParseBaseList();
            var constraintClauses = ParseTypeParameterConstraintClauses();
            var open = MatchToken(SyntaxKind.OpenBraceToken);

            using var __ = _ctx.Push(ParseContext.TypeMembers);
            var members = new List<MemberDeclarationSyntax>();
            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                var m = ParseClassMemberDeclaration(classNameToken: id);
                members.Add(m);

                if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Parser made no progress in struct member parsing.");
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            var semi = EatOptionalToken(SyntaxKind.SemicolonToken);

            return new StructDeclarationSyntax(
                attributeLists,
                modifiers,
                structKeyword,
                id,
                typeParams,
                baseList,
                constraintClauses,
                open,
                new SyntaxList<MemberDeclarationSyntax>(members.ToArray()),
                close,
                semi);
        }
        private InterfaceDeclarationSyntax ParseInterfaceDeclaration(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers)
        {
            if (modifiers.Count == 0)
                modifiers = ParseModifiers(ModifierContext.Type);

            var interfaceKeyword = MatchToken(SyntaxKind.InterfaceKeyword);
            var id = MatchToken(SyntaxKind.IdentifierToken);

            TypeParameterListSyntax? typeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                typeParams = ParseTypeParameterList();

            BaseListSyntax? baseList = null;
            if (_tokens.CurrentKind == SyntaxKind.ColonToken)
                baseList = ParseBaseList();
            var constraintClauses = ParseTypeParameterConstraintClauses();
            var open = MatchToken(SyntaxKind.OpenBraceToken);

            using var __ = _ctx.Push(ParseContext.TypeMembers);

            var members = new List<MemberDeclarationSyntax>();
            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                var m = ParseClassMemberDeclaration(classNameToken: id);
                members.Add(m);

                if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Parser made no progress in interface member parsing.");
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            var semi = EatOptionalToken(SyntaxKind.SemicolonToken);

            return new InterfaceDeclarationSyntax(
                attributeLists,
                modifiers,
                interfaceKeyword,
                id,
                typeParams,
                baseList,
                constraintClauses,
                open,
                new SyntaxList<MemberDeclarationSyntax>(members.ToArray()),
                close,
                semi);
        }
        //classes
        private ClassDeclarationSyntax ParseClassDeclaration(SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers)
        {
            if (modifiers.Count == 0)
                modifiers = ParseModifiers(ModifierContext.Type);

            var classKeyword = MatchToken(SyntaxKind.ClassKeyword);
            var id = MatchToken(SyntaxKind.IdentifierToken);

            TypeParameterListSyntax? typeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                typeParams = ParseTypeParameterList();

            BaseListSyntax? baseList = null;
            if (_tokens.CurrentKind == SyntaxKind.ColonToken)
                baseList = ParseBaseList();

            var constraintClauses = ParseTypeParameterConstraintClauses();

            var open = MatchToken(SyntaxKind.OpenBraceToken);

            using var __ = _ctx.Push(ParseContext.TypeMembers);

            var members = new List<MemberDeclarationSyntax>();
            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                var m = ParseClassMemberDeclaration(classNameToken: id);
                members.Add(m);

                if (_tokens.Position == start && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Parser made no progress in class member parsing.");
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            var semi = EatOptionalToken(SyntaxKind.SemicolonToken);

            return new ClassDeclarationSyntax(
                attributeLists,
                modifiers,
                classKeyword,
                id,
                typeParams,
                baseList,
                constraintClauses,
                open,
                new SyntaxList<MemberDeclarationSyntax>(members.ToArray()),
                close,
                semi);
        }
        private MemberDeclarationSyntax ParseClassMemberDeclaration(SyntaxToken classNameToken)
        {
            var attrs = ParseAttributeLists();
            var modifiers = ParseModifiers(ModifierContext.Member);

            // delegate
            if (_tokens.CurrentKind == SyntaxKind.DelegateKeyword)
                return ParseDelegateDeclarationAfterModifiers(attrs, modifiers);

            // nested type
            if (IsTypeDeclarationKeyword(_tokens.CurrentKind))
                return ParseTypeDeclarationAfterModifiers(attrs, modifiers);

            // constructor
            if (IsConstructorDeclarationStart(classNameToken))
            {
                var id2 = MatchToken(SyntaxKind.IdentifierToken);
                return ParseConstructorDeclarationAfterHeader(attrs, modifiers, id2);
            }
            // conversion operator
            if ((_tokens.CurrentKind == SyntaxKind.ImplicitKeyword
                || _tokens.CurrentKind == SyntaxKind.ExplicitKeyword) &&
                _tokens.Peek(1).Kind == SyntaxKind.OperatorKeyword)
            {
                return ParseConversionOperatorDeclarationAfterModifiers(attrs, modifiers);
            }

            var type = ParseType();

            if (_tokens.CurrentKind == SyntaxKind.OperatorKeyword)
                return ParseOperatorDeclarationAfterHeader(attrs, modifiers, type);

            ExplicitInterfaceSpecifierSyntax? explicitInterface = null;
            SyntaxToken explicitMemberId = default;
            SyntaxToken explicitThisKeyword = default;
            bool hasExplicitInterface = TryParseExplicitInterfaceSpecifier(out explicitInterface, out explicitMemberId, out explicitThisKeyword);
            if (explicitThisKeyword.Kind == SyntaxKind.ThisKeyword)
            {
                return ParseIndexerDeclarationAfterHeader(attrs, modifiers, type, explicitInterface, explicitThisKeyword);
            }
            if (!hasExplicitInterface && _tokens.CurrentKind == SyntaxKind.ThisKeyword && _tokens.Peek(1).Kind == SyntaxKind.OpenBracketToken)
            {
                var thisKeyword = MatchToken(SyntaxKind.ThisKeyword);
                return ParseIndexerDeclarationAfterHeader(attrs, modifiers, type, explicitInterfaceSpecifier: null, thisKeyword: thisKeyword);
            }
            var id = hasExplicitInterface ? explicitMemberId : MatchToken(SyntaxKind.IdentifierToken);
            TypeParameterListSyntax? methodTypeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                methodTypeParams = ParseTypeParameterList();

            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                return ParseMethodDeclarationAfterHeader(attrs, modifiers, type, explicitInterface, id, methodTypeParams);

            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken || _tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
                return ParsePropertyDeclarationAfterHeader(attrs, modifiers, type, explicitInterface, id);

            return ParseFieldDeclarationAfterHeader(attrs, modifiers, type, id);
        }
        private PropertyDeclarationSyntax ParsePropertyDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax type,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken id)
        {
            if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
            {
                var exprBody = ParseArrowExpressionClause();
                var semi = MatchToken(SyntaxKind.SemicolonToken);
                return new PropertyDeclarationSyntax(
                    attributeLists,
                    modifiers,
                    type,
                    explicitInterfaceSpecifier,
                    id,
                    accessorList: null,
                    expressionBody: exprBody,
                    initializer: null,
                    semicolonToken: semi);
            }
            var accessorList = ParseAccessorList();

            EqualsValueClauseSyntax? init = null;
            SyntaxToken semi2 = default; // optional

            if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
            {
                init = ParseEqualsValueClause();
                semi2 = MatchToken(SyntaxKind.SemicolonToken);
            }

            return new PropertyDeclarationSyntax(
                attributeLists,
                modifiers,
                type,
                explicitInterfaceSpecifier,
                id,
                accessorList,
                expressionBody: null,
                initializer: init,
                semicolonToken: semi2);
        }
        private AccessorListSyntax ParseAccessorList()
        {
            var open = MatchToken(SyntaxKind.OpenBraceToken);

            var accessors = new List<AccessorDeclarationSyntax>();

            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                accessors.Add(ParseAccessorDeclaration());

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in accessor parsing.");
                    break;
                }
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);

            return new AccessorListSyntax(open, new SyntaxList<AccessorDeclarationSyntax>(accessors.ToArray()), close);
        }
        private AccessorDeclarationSyntax ParseAccessorDeclaration()
        {
            var attributeLists = ParseAttributeLists();
            var modifiers = ParseModifiers(ModifierContext.Accessor);

            SyntaxToken kw;
            SyntaxKind kind;

            if (IsCurrentContextual(SyntaxKind.GetKeyword))
            {
                kw = _tokens.EatToken();
                kind = SyntaxKind.GetAccessorDeclaration;
            }
            else if (IsCurrentContextual(SyntaxKind.SetKeyword))
            {
                kw = _tokens.EatToken();
                kind = SyntaxKind.SetAccessorDeclaration;
            }
            else if (IsCurrentContextual(SyntaxKind.InitKeyword))
            {
                kw = _tokens.EatToken();
                kind = SyntaxKind.InitAccessorDeclaration;
            }
            else
            {
                kw = MatchContextualKeyword(SyntaxKind.GetKeyword);
                kind = SyntaxKind.GetAccessorDeclaration;
            }

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default;

            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                body = ParseBlock();
            }
            else if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
            {
                exprBody = ParseArrowExpressionClause();
                semi = MatchToken(SyntaxKind.SemicolonToken);
            }
            else
            {
                semi = MatchToken(SyntaxKind.SemicolonToken);
            }

            return new AccessorDeclarationSyntax(
                kind,
                attributeLists,
                modifiers,
                kw,
                body,
                exprBody,
                semi);
        }
        private IndexerDeclarationSyntax ParseIndexerDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax type,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken thisKeyword)
        {
            var parameterList = ParseBracketedParameterList();

            if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
            {
                var exprBody = ParseArrowExpressionClause();
                var semi = MatchToken(SyntaxKind.SemicolonToken);

                return new IndexerDeclarationSyntax(
                    attributeLists,
                    modifiers,
                    type,
                    explicitInterfaceSpecifier,
                    thisKeyword,
                    parameterList,
                    accessorList: null,
                    expressionBody: exprBody,
                    semicolonToken: semi);
            }

            var accessorList = ParseAccessorList();
            return new IndexerDeclarationSyntax(
                attributeLists,
                modifiers,
                type,
                explicitInterfaceSpecifier,
                thisKeyword,
                parameterList,
                accessorList,
                expressionBody: null,
                semicolonToken: default);
        }
        private BracketedParameterListSyntax ParseBracketedParameterList()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);
            var parameters = ParseSeparatedParameters(closeKind: SyntaxKind.CloseBracketToken);
            var close = MatchToken(SyntaxKind.CloseBracketToken);
            return new BracketedParameterListSyntax(open, parameters, close);
        }
        private OperatorDeclarationSyntax ParseOperatorDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType)
        {
            var operatorKeyword = MatchToken(SyntaxKind.OperatorKeyword);
            var checkedKeyword = EatOptionalToken(SyntaxKind.CheckedKeyword);
            var operatorToken = ParseOverloadableOperatorToken();
            var parameters = ParseParameterList();

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default;

            var features = GetMethodLikeBodyParseContext(modifiers, allowAsync: false);
            using var __ = _ctx.Push(features);

            ParseMemberBody(out body, out exprBody, out semi);

            return new OperatorDeclarationSyntax(
                attributeLists,
                modifiers,
                returnType,
                operatorKeyword,
                checkedKeyword,
                operatorToken,
                parameters,
                body,
                exprBody,
                semi);
        }
        private ConversionOperatorDeclarationSyntax ParseConversionOperatorDeclarationAfterModifiers(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers)
        {
            var implicitOrExplicitKeyword = _tokens.CurrentKind == SyntaxKind.ImplicitKeyword
        ? MatchToken(SyntaxKind.ImplicitKeyword)
        : MatchToken(SyntaxKind.ExplicitKeyword);

            var operatorKeyword = MatchToken(SyntaxKind.OperatorKeyword);
            var checkedKeyword = EatOptionalToken(SyntaxKind.CheckedKeyword);
            var type = ParseType();
            var parameters = ParseParameterList();

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default;

            var features = GetMethodLikeBodyParseContext(modifiers, allowAsync: false);
            using var __ = _ctx.Push(features);

            ParseMemberBody(out body, out exprBody, out semi);

            return new ConversionOperatorDeclarationSyntax(
                attributeLists,
                modifiers,
                implicitOrExplicitKeyword,
                operatorKeyword,
                checkedKeyword,
                type,
                parameters,
                body,
                exprBody,
                semi);
        }
        private DelegateDeclarationSyntax ParseDelegateDeclarationAfterModifiers(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers)
        {
            var delegateKeyword = MatchToken(SyntaxKind.DelegateKeyword);
            var returnType = ParseType();
            var id = MatchToken(SyntaxKind.IdentifierToken);

            TypeParameterListSyntax? typeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                typeParams = ParseTypeParameterList();

            var parameters = ParseParameterList();
            var constraintClauses = ParseTypeParameterConstraintClauses();
            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new DelegateDeclarationSyntax(
                attributeLists,
                modifiers,
                delegateKeyword,
                returnType,
                id,
                typeParams,
                parameters,
                constraintClauses,
                semi);
        }
        private static ParseContext GetMethodLikeBodyParseContext(SyntaxTokenList modifiers, bool allowAsync)
        {
            ParseContext features = ParseContext.None;

            if (allowAsync && HasContextualModifier(modifiers, SyntaxKind.AsyncKeyword))
                features |= ParseContext.Async;

            if (HasModifier(modifiers, SyntaxKind.UnsafeKeyword))
                features |= ParseContext.Unsafe;

            return features;
        }
        private ConstructorDeclarationSyntax ParseConstructorDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers, SyntaxToken id)
        {
            var parameters = ParseParameterList();

            var init = ParseConstructorInitializerOptional();

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default; // optional

            ParseMemberBody(out body, out exprBody, out semi);

            return new ConstructorDeclarationSyntax(attributeLists, modifiers, id, parameters, init, body, exprBody, semi);
        }
        private void ParseMemberBody(
            out BlockSyntax? body,
            out ArrowExpressionClauseSyntax? exprBody,
            out SyntaxToken semicolonToken)
        {
            body = null;
            exprBody = null;
            semicolonToken = default;

            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                body = ParseBlock();
            }
            else if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
            {
                exprBody = ParseArrowExpressionClause();
                semicolonToken = MatchToken(SyntaxKind.SemicolonToken);
            }
            else
            {
                semicolonToken = MatchToken(SyntaxKind.SemicolonToken);
            }
        }
        private MethodDeclarationSyntax ParseMethodDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken id,
            TypeParameterListSyntax? typeParams)
        {
            var parameters = ParseParameterList();
            var constraintClauses = ParseTypeParameterConstraintClauses();
            ParseContext features = ParseContext.None;
            if (HasContextualModifier(modifiers, SyntaxKind.AsyncKeyword))
                features |= ParseContext.Async;
            if (modifiers.Any(m => m.Kind == SyntaxKind.UnsafeKeyword))
                features |= ParseContext.Unsafe;

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default;

            using var __ = _ctx.Push(features);

            ParseMemberBody(out body, out exprBody, out semi);


            return new MethodDeclarationSyntax(
                attributeLists,
                modifiers,
                returnType,
                explicitInterfaceSpecifier,
                id,
                typeParams,
                parameters,
                constraintClauses,
                body,
                exprBody,
                semi);
        }
        private bool TryParseExplicitInterfaceSpecifier(
            out ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            out SyntaxToken memberIdentifier,
            out SyntaxToken thisKeyword)
        {
            explicitInterfaceSpecifier = null;
            memberIdentifier = default;
            thisKeyword = default;

            if (_tokens.CurrentKind != SyntaxKind.IdentifierToken || _tokens.Peek(1).Kind != SyntaxKind.DotToken)
                return false;

            NameSyntax name = ParseSimpleName();
            while (_tokens.CurrentKind == SyntaxKind.DotToken)
            {
                if (_tokens.Peek(1).Kind == SyntaxKind.ThisKeyword)
                {
                    var dot = _tokens.EatToken();
                    thisKeyword = MatchToken(SyntaxKind.ThisKeyword);
                    explicitInterfaceSpecifier = new ExplicitInterfaceSpecifierSyntax(name, dot);
                    return true;
                }
                if (_tokens.Peek(1).Kind != SyntaxKind.IdentifierToken)
                    return false;

                bool isQualifierDot = Probe(
                    scan: () =>
                    {
                        _tokens.EatToken(); // dot
                        ParseSimpleName();
                        return _tokens.CurrentKind == SyntaxKind.DotToken;
                    },
                    requireProgress: true);

                if (isQualifierDot)
                {
                    var dot = _tokens.EatToken();
                    var right = ParseSimpleName();
                    name = new QualifiedNameSyntax(name, dot, right);
                    continue;
                }

                var explicitDot = _tokens.EatToken();
                memberIdentifier = MatchToken(SyntaxKind.IdentifierToken);
                explicitInterfaceSpecifier = new ExplicitInterfaceSpecifierSyntax(name, explicitDot);
                return true;
            }
            return false;
        }
        private static bool HasModifier(SyntaxTokenList mods, SyntaxKind kind)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].Kind == kind)
                    return true;
            }

            return false;
        }

        private static bool HasContextualModifier(SyntaxTokenList mods, SyntaxKind contextualKind)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var t = mods[i];
                if (t.Kind == SyntaxKind.IdentifierToken && t.ContextualKind == contextualKind)
                    return true;
            }
            return false;
        }
        private FieldDeclarationSyntax ParseFieldDeclarationAfterHeader(
            SyntaxList<AttributeListSyntax> attributeLists, SyntaxTokenList modifiers, TypeSyntax type, SyntaxToken firstIdentifier)
        {
            var vars = ParseVariableDeclarators(firstIdentifier, closeKind: SyntaxKind.SemicolonToken);
            var decl = new VariableDeclarationSyntax(type, vars);
            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new FieldDeclarationSyntax(attributeLists, modifiers, decl, semi);
        }
        private SyntaxToken ParseOverloadableOperatorToken()
        {
            if (IsOverloadableOperatorToken(_tokens.CurrentKind))
                return _tokens.EatToken();

            var pos = _tokens.Current.Span.Start;
            _diagnostics.Add(new SyntaxDiagnostic(pos, $"Expected overloadable operator token, found '{_tokens.CurrentKind}'."));
            return CreateMissingToken(SyntaxKind.PlusToken, pos);
        }
        private ArrowExpressionClauseSyntax ParseArrowExpressionClause()
        {
            var arrow = MatchToken(SyntaxKind.EqualsGreaterThanToken);
            var expr = ParseExpression();
            return new ArrowExpressionClauseSyntax(arrow, expr);
        }
        private SwitchExpressionSyntax ParseSwitchExpressionAfterGoverningExpression(ExpressionSyntax governing)
        {
            var switchKeyword = MatchToken(SyntaxKind.SwitchKeyword);
            var openBrace = MatchToken(SyntaxKind.OpenBraceToken);

            var arms = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == SyntaxKind.CloseBraceToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(openBrace.Span.End, "Switch expression must contain at least one arm."));
            }
            else
            {
                while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                       _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                {
                    var start = _tokens.Position;

                    var arm = ParseSwitchExpressionArm();
                    arms.Add(new SyntaxNodeOrToken(arm));

                    if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                    {
                        arms.Add(new SyntaxNodeOrToken(_tokens.EatToken()));

                        // allow trailing comma
                        if (_tokens.CurrentKind == SyntaxKind.CloseBraceToken)
                            break;

                        continue;
                    }

                    if (_tokens.Position == start)
                    {
                        EatAsSkippedToken("Parser made no progress in switch expression arms parsing.");
                        break;
                    }

                    break;
                }
            }

            var closeBrace = MatchToken(SyntaxKind.CloseBraceToken);

            return new SwitchExpressionSyntax(
                governing,
                switchKeyword,
                openBrace,
                new SeparatedSyntaxList<SwitchExpressionArmSyntax>(arms.ToArray()),
                closeBrace);
        }
        private SwitchExpressionArmSyntax ParseSwitchExpressionArm()
        {
            var pattern = ParsePatternCore();

            WhenClauseSyntax? whenClause = null;
            if (_tokens.CurrentKind == SyntaxKind.WhenKeyword)
                whenClause = ParseWhenClause();

            var arrow = MatchToken(SyntaxKind.EqualsGreaterThanToken);

            ExpressionSyntax expr;
            if (_tokens.CurrentKind == SyntaxKind.CommaToken || _tokens.CurrentKind == SyntaxKind.CloseBraceToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(arrow.Span.End, "Expected expression after '=>'."));
                expr = CreateMissingIdentifierName(arrow.Span.End);
            }
            else
            {
                expr = ParseExpression();
            }

            return new SwitchExpressionArmSyntax(pattern, whenClause, arrow, expr);
        }
        private AnonymousMethodExpressionSyntax ParseAnonymousMethodExpression()
        {
            SyntaxToken asyncKeyword = default;
            if (IsCurrentContextual(SyntaxKind.AsyncKeyword) && _tokens.Peek(1).Kind == SyntaxKind.DelegateKeyword)
                asyncKeyword = _tokens.EatToken();

            var delegateKeyword = MatchToken(SyntaxKind.DelegateKeyword);

            ParameterListSyntax? parameterList = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                parameterList = ParseParameterList();

            using var __ = _ctx.Push(asyncKeyword.Span.Length != 0 ? ParseContext.Async : ParseContext.None);
            var block = ParseBlock();

            return new AnonymousMethodExpressionSyntax(asyncKeyword, delegateKeyword, parameterList, block);
        }
        private ExpressionSyntax ParseLambdaExpression()
        {
            SyntaxToken staticKeyword = default;
            if (_tokens.CurrentKind == SyntaxKind.StaticKeyword)
                staticKeyword = _tokens.EatToken();

            SyntaxToken asyncKeyword = default;
            if (IsCurrentContextual(SyntaxKind.AsyncKeyword) && _tokens.Peek(1).Kind != SyntaxKind.EqualsGreaterThanToken)
                asyncKeyword = _tokens.EatToken();

            // simple
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken &&
                _tokens.Peek(1).Kind == SyntaxKind.EqualsGreaterThanToken)
            {
                var id = _tokens.EatToken();
                var parameter = new ParameterSyntax(default, SyntaxTokenList.Empty, type: null, identifier: id, @default: null);
                var arrow = _tokens.EatToken();

                using var __ = _ctx.Push(asyncKeyword.Span.Length != 0 ? ParseContext.Async : ParseContext.None);

                SyntaxNode body = _tokens.CurrentKind == SyntaxKind.OpenBraceToken
                    ? (SyntaxNode)ParseBlock()
                    : (SyntaxNode)ParseExpression();

                return new SimpleLambdaExpressionSyntax(staticKeyword, asyncKeyword, parameter, arrow, body);
            }

            // parenthesized
            var parameterList = ParseLambdaParameterList();
            var arrow2 = MatchToken(SyntaxKind.EqualsGreaterThanToken);

            using var __2 = _ctx.Push(asyncKeyword.Span.Length != 0 ? ParseContext.Async : ParseContext.None);

            SyntaxNode body2 = _tokens.CurrentKind == SyntaxKind.OpenBraceToken
                ? (SyntaxNode)ParseBlock()
                : (SyntaxNode)ParseExpression();

            return new ParenthesizedLambdaExpressionSyntax(staticKeyword, asyncKeyword, parameterList, arrow2, body2);
        }
        // parameters
        private ParameterListSyntax ParseLambdaParameterList()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var parameters = ParseSeparatedLambdaParameters(closeKind: SyntaxKind.CloseParenToken);
            var close = MatchToken(SyntaxKind.CloseParenToken);
            return new ParameterListSyntax(open, parameters, close);
        }
        private SeparatedSyntaxList<ParameterSyntax> ParseSeparatedLambdaParameters(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == closeKind)
                return new SeparatedSyntaxList<ParameterSyntax>(list.ToArray());

            while (_tokens.CurrentKind != closeKind &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var p = ParseParameter(allowTypeOmitted: true);
                list.Add(new SyntaxNodeOrToken(p));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in lambda parameter parsing.");

                break;
            }

            return new SeparatedSyntaxList<ParameterSyntax>(list.ToArray());
        }
        private ParameterListSyntax ParseParameterList()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var parameters = ParseSeparatedParameters(closeKind: SyntaxKind.CloseParenToken);
            var close = MatchToken(SyntaxKind.CloseParenToken);
            return new ParameterListSyntax(open, parameters, close);
        }
        private TypeParameterListSyntax ParseTypeParameterList()
        {
            var lt = MatchToken(SyntaxKind.LessThanToken);

            var list = new List<SyntaxNodeOrToken>();
            list.Add(new SyntaxNodeOrToken(ParseTypeParameter()));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                list.Add(new SyntaxNodeOrToken(ParseTypeParameter()));
            }

            var gt = EatGreaterThanTokenForTypeArgs();
            return new TypeParameterListSyntax(lt, new SeparatedSyntaxList<TypeParameterSyntax>(list.ToArray()), gt);
        }
        private SyntaxList<TypeParameterConstraintClauseSyntax> ParseTypeParameterConstraintClauses()
        {
            var clauses = new List<TypeParameterConstraintClauseSyntax>();

            while (IsCurrentContextual(SyntaxKind.WhereKeyword))
            {
                var start = _tokens.Position;
                clauses.Add(ParseTypeParameterConstraintClause());

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in type parameter constraint clause parsing.");
                    break;
                }
            }

            return new SyntaxList<TypeParameterConstraintClauseSyntax>(clauses.ToArray());
        }
        private TypeParameterConstraintClauseSyntax ParseTypeParameterConstraintClause()
        {
            var whereKeyword = MatchContextualKeyword(SyntaxKind.WhereKeyword);
            var name = MatchToken(SyntaxKind.IdentifierToken);
            var colon = MatchToken(SyntaxKind.ColonToken);

            var list = new List<SyntaxNodeOrToken>();
            if (IsTypeParameterConstraintStart())
            {
                list.Add(new SyntaxNodeOrToken(ParseTypeParameterConstraint()));
            }
            else
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected type parameter constraint after ':'."));
                list.Add(new SyntaxNodeOrToken(new TypeConstraintSyntax((TypeSyntax)CreateMissingIdentifierName(colon.Span.End))));
            }

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));

                if (IsTypeParameterConstraintStart())
                {
                    list.Add(new SyntaxNodeOrToken(ParseTypeParameterConstraint()));
                    continue;
                }

                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected type parameter constraint after ','."));
                list.Add(new SyntaxNodeOrToken(new TypeConstraintSyntax((TypeSyntax)CreateMissingIdentifierName(comma.Span.End))));
            }
            return new TypeParameterConstraintClauseSyntax(
                whereKeyword,
                name,
                colon,
                new SeparatedSyntaxList<TypeParameterConstraintSyntax>(list.ToArray()));
        }
        private bool IsTypeParameterConstraintStart()
        {
            if (_tokens.CurrentKind == SyntaxKind.ClassKeyword ||
                _tokens.CurrentKind == SyntaxKind.StructKeyword ||
                _tokens.CurrentKind == SyntaxKind.NewKeyword ||
                _tokens.CurrentKind == SyntaxKind.DefaultKeyword)
                return true;

            if (IsCurrentContextual(SyntaxKind.AllowsKeyword) ||
                IsCurrentContextual(SyntaxKind.UnmanagedKeyword) ||
                (_tokens.Current.Kind == SyntaxKind.IdentifierToken && _tokens.Current.ValueText == "notnull"))
                return true;

            return Probe(scan: ScanType, tempContext: ParseContext.Type);
        }

        private TypeParameterConstraintSyntax ParseTypeParameterConstraint()
        {
            if (_tokens.CurrentKind == SyntaxKind.ClassKeyword || _tokens.CurrentKind == SyntaxKind.StructKeyword)
            {
                var keyword = _tokens.EatToken();

                var kind = keyword.Kind == SyntaxKind.ClassKeyword
                    ? SyntaxKind.ClassConstraint
                    : SyntaxKind.StructConstraint;

                var question = keyword.Kind == SyntaxKind.ClassKeyword
                    ? EatOptionalToken(SyntaxKind.QuestionToken)
                    : default;

                return new ClassOrStructConstraintSyntax(
                    kind,
                    keyword,
                    question);
            }

            if (IsCurrentContextual(SyntaxKind.AllowsKeyword))
            {
                return ParseAllowsConstraintClause();
            }

            if (_tokens.CurrentKind == SyntaxKind.NewKeyword)
            {
                var @new = _tokens.EatToken();
                var openParen = MatchToken(SyntaxKind.OpenParenToken);
                var closeParen = MatchToken(SyntaxKind.CloseParenToken);
                return new ConstructorConstraintSyntax(@new, openParen, closeParen);
            }

            if (_tokens.CurrentKind == SyntaxKind.DefaultKeyword)
            {
                var @default = _tokens.EatToken();
                return new DefaultConstraintSyntax(@default);
            }

            var type = ParseType();
            return new TypeConstraintSyntax(type);
        }
        private AllowsConstraintClauseSyntax ParseAllowsConstraintClause()
        {
            var allowsKeyword = MatchContextualKeyword(SyntaxKind.AllowsKeyword);

            var list = new List<SyntaxNodeOrToken>();
            list.Add(new SyntaxNodeOrToken(ParseAllowsConstraint()));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));
                list.Add(new SyntaxNodeOrToken(ParseAllowsConstraint()));
            }

            return new AllowsConstraintClauseSyntax(
                allowsKeyword,
                new SeparatedSyntaxList<AllowsConstraintSyntax>(list.ToArray()));
        }

        private AllowsConstraintSyntax ParseAllowsConstraint()
        {
            var refKeyword = MatchToken(SyntaxKind.RefKeyword);
            var structKeyword = MatchToken(SyntaxKind.StructKeyword);
            return new RefStructConstraintSyntax(refKeyword, structKeyword);
        }
        private TypeParameterSyntax ParseTypeParameter()
        {
            var attrs = ParseAttributeLists();

            SyntaxToken variance = default;
            if (_tokens.CurrentKind == SyntaxKind.InKeyword || _tokens.CurrentKind == SyntaxKind.OutKeyword)
                variance = _tokens.EatToken();

            var id = MatchToken(SyntaxKind.IdentifierToken);
            return new TypeParameterSyntax(attrs, variance, id);
        }

        private BaseListSyntax ParseBaseList()
        {
            var colon = MatchToken(SyntaxKind.ColonToken);

            var items = new List<SyntaxNodeOrToken>();
            items.Add(new SyntaxNodeOrToken(ParseBaseType()));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                items.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                items.Add(new SyntaxNodeOrToken(ParseBaseType()));
            }

            return new BaseListSyntax(colon, new SeparatedSyntaxList<BaseTypeSyntax>(items.ToArray()));
        }

        private BaseTypeSyntax ParseBaseType()
        {
            var t = ParseType();
            return new SimpleBaseTypeSyntax(t);
        }
        private ConstructorInitializerSyntax? ParseConstructorInitializerOptional()
        {
            if (_tokens.CurrentKind != SyntaxKind.ColonToken)
                return null;

            var colon = _tokens.EatToken();

            SyntaxToken thisOrBase;
            SyntaxKind kind;

            if (_tokens.CurrentKind == SyntaxKind.ThisKeyword)
            {
                thisOrBase = _tokens.EatToken();
                kind = SyntaxKind.ThisConstructorInitializer;
            }
            else if (_tokens.CurrentKind == SyntaxKind.BaseKeyword)
            {
                thisOrBase = _tokens.EatToken();
                kind = SyntaxKind.BaseConstructorInitializer;
            }
            else
            {
                // recover
                thisOrBase = MatchToken(SyntaxKind.ThisKeyword);
                kind = SyntaxKind.ThisConstructorInitializer;
            }

            var args = ParseArgumentList();
            return new ConstructorInitializerSyntax(kind, colon, thisOrBase, args);
        }
        private SeparatedSyntaxList<ParameterSyntax> ParseSeparatedParameters(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            while (_tokens.CurrentKind != closeKind &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var p = ParseParameter(allowTypeOmitted: false);
                list.Add(new SyntaxNodeOrToken(p));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in parameter parsing.");

                break;
            }

            return new SeparatedSyntaxList<ParameterSyntax>(list.ToArray());
        }
        private ParameterSyntax ParseParameter(bool allowTypeOmitted)
        {
            var attrs = ParseAttributeLists();
            var modifiers = ParseModifiers(ModifierContext.Parameter);

            if (allowTypeOmitted)
            {
                // Try parse typed parameter
                var mark = _tokens.MarkState();
                int diagStart = _diagnostics.Count;

                var maybeType = ParseType();

                if (_tokens.CurrentKind == SyntaxKind.IdentifierToken)
                {
                    var id2 = _tokens.EatToken();
                    EqualsValueClauseSyntax? def2 = null;
                    if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
                        def2 = ParseEqualsValueClause();

                    return new ParameterSyntax(attrs, modifiers, maybeType, id2, def2);
                }

                // Not typed
                _tokens.Reset(mark);
                RollbackDiagnostics(diagStart);

                var id = MatchToken(SyntaxKind.IdentifierToken);

                EqualsValueClauseSyntax? def = null;
                if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
                    def = ParseEqualsValueClause();

                return new ParameterSyntax(attrs, modifiers, type: null, id, def);
            }
            else
            {
                var type = ParseType();
                var id = MatchToken(SyntaxKind.IdentifierToken);

                EqualsValueClauseSyntax? def = null;
                if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
                    def = ParseEqualsValueClause();

                return new ParameterSyntax(attrs, modifiers, type, id, def);
            }
        }
        // =statements=
        private StatementSyntax ParseStatement()
        {
            using var __ = _ctx.Push(ParseContext.Statement);
            if (IsYieldKeywordInStatementContext())
                return ParseYieldStatement();
            if (IsAwaitKeyword() && _tokens.Peek(1).Kind == SyntaxKind.ForEachKeyword)
            {
                var awaitKeyword = _tokens.EatToken();
                return ParseForEachStatement(awaitKeyword);
            }

            if (IsAwaitKeyword() && _tokens.Peek(1).Kind == SyntaxKind.UsingKeyword)
            {
                var awaitKeyword = _tokens.EatToken();
                return ParseUsingLikeStatement(awaitKeyword);
            }

            if (_tokens.CurrentKind == SyntaxKind.CheckedKeyword && _tokens.Peek(1).Kind == SyntaxKind.OpenBraceToken)
                return ParseCheckedStatement(isChecked: true);

            if (_tokens.CurrentKind == SyntaxKind.UncheckedKeyword && _tokens.Peek(1).Kind == SyntaxKind.OpenBraceToken)
                return ParseCheckedStatement(isChecked: false);

            return _tokens.CurrentKind switch
            {
                SyntaxKind.OpenBraceToken => ParseBlock(),
                SyntaxKind.IfKeyword => ParseIfStatement(),
                SyntaxKind.SwitchKeyword => ParseSwitchStatement(),
                SyntaxKind.WhileKeyword => ParseWhileStatement(),
                SyntaxKind.ForKeyword => ParseForStatement(),
                SyntaxKind.ForEachKeyword => ParseForEachStatement(default),
                SyntaxKind.DoKeyword => ParseDoStatement(),
                SyntaxKind.ThrowKeyword => ParseThrowStatement(),
                SyntaxKind.UsingKeyword => ParseUsingLikeStatement(default),

                SyntaxKind.UnsafeKeyword => ParseUnsafeStatement(),
                SyntaxKind.FixedKeyword => ParseFixedStatement(),

                SyntaxKind.TryKeyword => ParseTryStatement(),
                SyntaxKind.GotoKeyword => ParseGotoStatement(),
                SyntaxKind.BreakKeyword => ParseBreakStatement(),
                SyntaxKind.ContinueKeyword => ParseContinueStatement(),
                SyntaxKind.ReturnKeyword => ParseReturnStatement(),
                SyntaxKind.SemicolonToken => ParseEmptyStatement(),
                _ => ParseStatementCore(),
            };
        }
        private EmptyStatementSyntax ParseEmptyStatement()
        {
            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new EmptyStatementSyntax(semi);
        }

        private ExpressionStatementSyntax ParseExpressionStatement()
        {
            var expr = ParseExpression();
            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new ExpressionStatementSyntax(expr, semi);
        }
        private BlockSyntax ParseBlock()
        {
            var open = MatchToken(SyntaxKind.OpenBraceToken);
            var statements = new List<StatementSyntax>();

            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                int start = _tokens.Position;
                statements.Add(ParseStatement());
                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in block statement parsing.");
                }
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            return new BlockSyntax(open, new SyntaxList<StatementSyntax>(statements.ToArray()), close);
        }
        private StatementSyntax ParseStatementCore()
        {
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken &&
                _tokens.Peek(1).Kind == SyntaxKind.ColonToken)
            {
                return ParseLabeledStatement();
            }

            if (TryParseLocalFunctionStatement(out var localFunc))
                return localFunc;

            if (TryParseLocalDeclarationStatement(out var decl))
                return decl;

            return ParseExpressionStatement();
        }
        private LabeledStatementSyntax ParseLabeledStatement()
        {
            var id = MatchToken(SyntaxKind.IdentifierToken);
            var colon = MatchToken(SyntaxKind.ColonToken);
            var stmt = ParseStatement();
            return new LabeledStatementSyntax(id, colon, stmt);
        }
        private bool TryParseLocalDeclarationStatement(out LocalDeclarationStatementSyntax stmt)
        {
            return TryParse(
                parse: ParseLocalDeclarationStatement,
                out stmt,
                tempContext: ParseContext.Statement,
                requireProgress: true,
                requireNoNewDiagnostics: true);
        }
        private bool TryParseLocalFunctionStatement(out LocalFunctionStatementSyntax stmt)
        {
            return TryParse(
                parse: ParseLocalFunctionStatement,
                out stmt,
                tempContext: ParseContext.Statement,
                requireProgress: true,
                requireNoNewDiagnostics: true);
        }

        private LocalDeclarationStatementSyntax ParseLocalDeclarationStatement()
        {
            var modifiers = ParseModifiers(ModifierContext.Local);

            var type = ParseType();
            var firstId = MatchToken(SyntaxKind.IdentifierToken);

            var vars = ParseVariableDeclarators(firstId, closeKind: SyntaxKind.SemicolonToken);
            var decl = new VariableDeclarationSyntax(type, vars);
            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new LocalDeclarationStatementSyntax(
                awaitKeyword: default,
                usingKeyword: default,
                modifiers: modifiers,
                declaration: decl,
                semicolonToken: semi);
        }
        private LocalFunctionStatementSyntax ParseLocalFunctionStatement()
        {
            var attrs = ParseAttributeLists();
            var modifiers = ParseModifiers(ModifierContext.LocalFunction);

            var returnType = ParseType();
            var id = MatchToken(SyntaxKind.IdentifierToken);

            TypeParameterListSyntax? typeParams = null;
            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
                typeParams = ParseTypeParameterList();

            var parameters = ParseParameterList();
            var constraintClauses = ParseTypeParameterConstraintClauses();
            ParseContext features = ParseContext.None;
            bool containsContextualModifier = false;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var t = modifiers[i];
                if (t.Kind == SyntaxKind.IdentifierToken && t.ContextualKind == SyntaxKind.AsyncKeyword)
                    containsContextualModifier = true;
            }
            if (containsContextualModifier)
                features |= ParseContext.Async;

            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i].Kind == SyntaxKind.UnsafeKeyword)
                    features |= ParseContext.Unsafe;

            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? exprBody = null;
            SyntaxToken semi = default;

            using var __ = _ctx.Push(features);

            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                body = ParseBlock();
            }
            else if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken)
            {
                exprBody = ParseArrowExpressionClause();
                semi = MatchToken(SyntaxKind.SemicolonToken);
            }
            else
            {
                semi = MatchToken(SyntaxKind.SemicolonToken);
            }

            return new LocalFunctionStatementSyntax(
                attrs, modifiers, returnType, id, typeParams, parameters, constraintClauses, body, exprBody, semi);
        }
        private TryStatementSyntax ParseTryStatement()
        {
            var tryKeyword = MatchToken(SyntaxKind.TryKeyword);
            var block = ParseBlock();

            var catches = new List<CatchClauseSyntax>();
            while (_tokens.CurrentKind == SyntaxKind.CatchKeyword)
                catches.Add(ParseCatchClause());

            FinallyClauseSyntax? finallyClause = null;
            if (_tokens.CurrentKind == SyntaxKind.FinallyKeyword)
                finallyClause = ParseFinallyClause();

            if (catches.Count == 0 && finallyClause is null)
            {
                _diagnostics.Add(new SyntaxDiagnostic(
                    tryKeyword.Span.Start,
                    "A try statement must have at least one catch clause or a finally clause."));
            }

            return new TryStatementSyntax(
                tryKeyword,
                block,
                new SyntaxList<CatchClauseSyntax>(catches.ToArray()),
                finallyClause);
        }
        private CatchClauseSyntax ParseCatchClause()
        {
            var catchKeyword = MatchToken(SyntaxKind.CatchKeyword);

            CatchDeclarationSyntax? decl = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                decl = ParseCatchDeclaration();

            CatchFilterClauseSyntax? filter = null;
            if (_tokens.CurrentKind == SyntaxKind.WhenKeyword)
                filter = ParseCatchFilterClause();

            var block = ParseBlock();

            return new CatchClauseSyntax(catchKeyword, decl, filter, block);
        }

        private CatchDeclarationSyntax ParseCatchDeclaration()
        {
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            TypeSyntax type;
            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(openParen.Span.End, "Expected type in catch declaration."));
                type = CreateMissingIdentifierName(openParen.Span.End);
            }
            else
            {
                type = ParseType();
            }

            // Identifier is optional
            SyntaxToken identifier = default;
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken)
                identifier = _tokens.EatToken();

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new CatchDeclarationSyntax(openParen, type, identifier, closeParen);
        }
        private CatchFilterClauseSyntax ParseCatchFilterClause()
        {
            var whenKeyword = MatchToken(SyntaxKind.WhenKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);

            ExpressionSyntax filterExpr;
            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(openParen.Span.End, "Expected expression in catch filter."));
                filterExpr = CreateMissingIdentifierName(openParen.Span.End);
            }
            else
            {
                filterExpr = ParseExpression();
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            return new CatchFilterClauseSyntax(whenKeyword, openParen, filterExpr, closeParen);
        }

        private FinallyClauseSyntax ParseFinallyClause()
        {
            var finallyKeyword = MatchToken(SyntaxKind.FinallyKeyword);
            var block = ParseBlock();
            return new FinallyClauseSyntax(finallyKeyword, block);
        }
        private ReturnStatementSyntax ParseReturnStatement()
        {
            var ret = MatchToken(SyntaxKind.ReturnKeyword);

            ExpressionSyntax? expr = null;
            if (_tokens.CurrentKind != SyntaxKind.SemicolonToken && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                expr = ParseExpression();

            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new ReturnStatementSyntax(ret, expr, semi);
        }
        private BreakStatementSyntax ParseBreakStatement()
        {
            var kw = MatchToken(SyntaxKind.BreakKeyword);
            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new BreakStatementSyntax(kw, semi);
        }

        private ContinueStatementSyntax ParseContinueStatement()
        {
            var kw = MatchToken(SyntaxKind.ContinueKeyword);
            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new ContinueStatementSyntax(kw, semi);
        }
        private GotoStatementSyntax ParseGotoStatement()
        {
            var gotoKeyword = MatchToken(SyntaxKind.GotoKeyword);

            if (_tokens.CurrentKind == SyntaxKind.CaseKeyword)
            {
                var caseKeyword = _tokens.EatToken();

                ExpressionSyntax expr;
                if (_tokens.CurrentKind == SyntaxKind.SemicolonToken)
                {
                    _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected expression after 'goto case'."));
                    expr = CreateMissingIdentifierName(_tokens.Current.Span.Start);
                }
                else
                {
                    expr = ParseExpression();
                }

                var semi = MatchToken(SyntaxKind.SemicolonToken);
                return new GotoStatementSyntax(SyntaxKind.GotoCaseStatement, gotoKeyword, caseKeyword, expr, semi);
            }

            if (_tokens.CurrentKind == SyntaxKind.DefaultKeyword)
            {
                var defaultKeyword = _tokens.EatToken();
                var semi = MatchToken(SyntaxKind.SemicolonToken);
                return new GotoStatementSyntax(SyntaxKind.GotoDefaultStatement, gotoKeyword, defaultKeyword, expression: null, semi);
            }

            // goto label;
            ExpressionSyntax labelExpr;
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken)
                labelExpr = new IdentifierNameSyntax(_tokens.EatToken());
            else
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected label identifier after 'goto'."));
                labelExpr = CreateMissingIdentifierName(_tokens.Current.Span.Start);
            }

            var semi2 = MatchToken(SyntaxKind.SemicolonToken);
            return new GotoStatementSyntax(SyntaxKind.GotoStatement, gotoKeyword, caseOrDefaultKeyword: default, labelExpr, semi2);
        }
        private WhileStatementSyntax ParseWhileStatement()
        {
            var kw = MatchToken(SyntaxKind.WhileKeyword);
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var close = MatchToken(SyntaxKind.CloseParenToken);
            var stmt = ParseStatement();
            return new WhileStatementSyntax(kw, open, condition, close, stmt);
        }
        private static SeparatedSyntaxList<ExpressionSyntax> EmptySeparatedExpressions()
            => new SeparatedSyntaxList<ExpressionSyntax>(Array.Empty<SyntaxNodeOrToken>());
        private SeparatedSyntaxList<ExpressionSyntax> ParseSeparatedExpressions(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            while (_tokens.CurrentKind != closeKind &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var e = ParseExpression();
                list.Add(new SyntaxNodeOrToken(e));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in expression list parsing.");

                break;
            }

            return new SeparatedSyntaxList<ExpressionSyntax>(list.ToArray());
        }
        private DoStatementSyntax ParseDoStatement()
        {
            var doKeyword = MatchToken(SyntaxKind.DoKeyword);
            var stmt = ParseStatement();
            var whileKeyword = MatchToken(SyntaxKind.WhileKeyword);
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var close = MatchToken(SyntaxKind.CloseParenToken);
            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new DoStatementSyntax(doKeyword, stmt, whileKeyword, open, condition, close, semi);
        }
        private ForStatementSyntax ParseForStatement()
        {
            var forKeyword = MatchToken(SyntaxKind.ForKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);

            VariableDeclarationSyntax? decl = null;
            SeparatedSyntaxList<ExpressionSyntax> inits = EmptySeparatedExpressions();

            if (_tokens.CurrentKind != SyntaxKind.SemicolonToken)
            {
                if (TryParseForVariableDeclaration(out decl))
                {
                    inits = EmptySeparatedExpressions();
                }
                else
                {
                    decl = null;
                    inits = ParseSeparatedExpressions(SyntaxKind.SemicolonToken);
                }
            }

            var firstSemi = MatchToken(SyntaxKind.SemicolonToken);
            ExpressionSyntax? condition = null;
            if (_tokens.CurrentKind != SyntaxKind.SemicolonToken &&
                _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                condition = ParseExpression();
            }

            var secondSemi = MatchToken(SyntaxKind.SemicolonToken);

            var incrementors = EmptySeparatedExpressions();
            if (_tokens.CurrentKind != SyntaxKind.CloseParenToken &&
                _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                incrementors = ParseSeparatedExpressions(SyntaxKind.CloseParenToken);
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var stmt = ParseStatement();

            return new ForStatementSyntax(
                forKeyword,
                openParen,
                decl,
                inits,
                firstSemi,
                condition,
                secondSemi,
                incrementors,
                closeParen,
                stmt);
        }
        private StatementSyntax ParseForEachStatement(SyntaxToken awaitKeyword)
        {
            var forEachKeyword = MatchToken(SyntaxKind.ForEachKeyword);
            var open = MatchToken(SyntaxKind.OpenParenToken);

            if (Probe(scan: ScanTypedForEachHeader, tempContext: ParseContext.Type))
            {
                var type = ParseType();
                var id = MatchToken(SyntaxKind.IdentifierToken);
                var inKeyword = MatchToken(SyntaxKind.InKeyword);
                var expr = ParseExpression();
                var close = MatchToken(SyntaxKind.CloseParenToken);
                var stmt = ParseStatement();

                return new ForEachStatementSyntax(awaitKeyword, forEachKeyword, open, type, id, inKeyword, expr, close, stmt);
            }
            {

                var variable = ParseExpression();
                var inKeyword = MatchToken(SyntaxKind.InKeyword);
                var expr = ParseExpression();

                var close = MatchToken(SyntaxKind.CloseParenToken);
                var stmt = ParseStatement();

                return new ForEachVariableStatementSyntax(awaitKeyword, forEachKeyword, open, variable, inKeyword, expr, close, stmt);
            }
        }
        private bool ScanTypedForEachHeader()
        {
            if (!ScanType())
                return false;
            if (_tokens.Current.Kind != SyntaxKind.IdentifierToken)
                return false;
            _tokens.EatToken(); // identifier
            return _tokens.Current.Kind == SyntaxKind.InKeyword;
        }
        private ThrowStatementSyntax ParseThrowStatement()
        {
            var kw = MatchToken(SyntaxKind.ThrowKeyword);

            ExpressionSyntax? expr = null;
            if (_tokens.CurrentKind != SyntaxKind.SemicolonToken && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                expr = ParseExpression();

            var semi = MatchToken(SyntaxKind.SemicolonToken);
            return new ThrowStatementSyntax(kw, expr, semi);
        }

        private UnsafeStatementSyntax ParseUnsafeStatement()
        {
            var unsafeKeyword = MatchToken(SyntaxKind.UnsafeKeyword);

            using var __ = _ctx.Push(ParseContext.Unsafe);
            var block = ParseBlock();
            return new UnsafeStatementSyntax(unsafeKeyword, block);
        }

        private CheckedStatementSyntax ParseCheckedStatement(bool isChecked)
        {
            var keyword = MatchToken(isChecked ? SyntaxKind.CheckedKeyword : SyntaxKind.UncheckedKeyword);
            var block = ParseBlock();
            return new CheckedStatementSyntax(isChecked ? SyntaxKind.CheckedStatement : SyntaxKind.UncheckedStatement, keyword, block);
        }

        private FixedStatementSyntax ParseFixedStatement()
        {
            var fixedKeyword = MatchToken(SyntaxKind.FixedKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);

            var type = ParseType();
            var firstId = MatchToken(SyntaxKind.IdentifierToken);
            var variables = ParseVariableDeclarators(firstId, closeKind: SyntaxKind.CloseParenToken);
            var declaration = new VariableDeclarationSyntax(type, variables);

            ReportMissingInitializers(declaration, message: "Fixed statement requires an initializer.");

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var statement = ParseStatement();

            return new FixedStatementSyntax(fixedKeyword, openParen, declaration, closeParen, statement);
        }
        private StatementSyntax ParseUsingLikeStatement(SyntaxToken awaitKeyword)
        {
            var usingKeyword = MatchToken(SyntaxKind.UsingKeyword);

            // using (...) statement
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                return ParseUsingStatementAfterUsing(awaitKeyword, usingKeyword);

            // using var x = ...;
            return ParseUsingDeclarationAfterUsing(awaitKeyword, usingKeyword);
        }
        private UsingStatementSyntax ParseUsingStatementAfterUsing(SyntaxToken awaitKeyword, SyntaxToken usingKeyword)
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);

            VariableDeclarationSyntax? decl = null;
            ExpressionSyntax? expr = null;

            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected resource in using statement."));
                expr = CreateMissingIdentifierName(_tokens.Current.Span.Start);
            }
            else if (TryParseUsingVariableDeclaration(out decl))
            {
                // ok
            }
            else
            {
                expr = ParseExpression();
            }

            var close = MatchToken(SyntaxKind.CloseParenToken);
            var stmt = ParseStatement();

            if (decl != null)
                ReportMissingInitializers(decl, message: "Using statement variable declaration should have initializer.");

            return new UsingStatementSyntax(awaitKeyword, usingKeyword, open, decl, expr, close, stmt);
        }
        private LocalDeclarationStatementSyntax ParseUsingDeclarationAfterUsing(SyntaxToken awaitKeyword, SyntaxToken usingKeyword)
        {
            var modifiers = ParseModifiers(ModifierContext.Local);

            var type = ParseType();
            var firstId = MatchToken(SyntaxKind.IdentifierToken);

            var vars = ParseVariableDeclarators(firstId, closeKind: SyntaxKind.SemicolonToken);
            var decl = new VariableDeclarationSyntax(type, vars);

            ReportMissingInitializers(decl, message: "Using declaration requires an initializer.");

            var semi = MatchToken(SyntaxKind.SemicolonToken);

            return new LocalDeclarationStatementSyntax(
                awaitKeyword: awaitKeyword,
                usingKeyword: usingKeyword,
                modifiers: modifiers,
                declaration: decl,
                semicolonToken: semi);

        }
        private bool TryParseUsingVariableDeclaration(out VariableDeclarationSyntax decl)
        {
            return TryParse(
                parse: () =>
                {
                    var type = ParseType();
                    var firstId = MatchToken(SyntaxKind.IdentifierToken);
                    var vars = ParseVariableDeclarators(firstId, closeKind: SyntaxKind.CloseParenToken);
                    return new VariableDeclarationSyntax(type, vars);
                },
                out decl,
                tempContext: ParseContext.Type,
                requireProgress: true,
                requireNoNewDiagnostics: true,
                validateAfter: () => _tokens.CurrentKind == SyntaxKind.CloseParenToken);
        }
        private YieldStatementSyntax ParseYieldStatement()
        {
            var yieldKeyword = MatchContextualKeyword(SyntaxKind.YieldKeyword);
            SyntaxToken returnOrBreak;
            SyntaxKind kind;

            if (_tokens.CurrentKind == SyntaxKind.ReturnKeyword)
            {
                returnOrBreak = _tokens.EatToken();
                kind = SyntaxKind.YieldReturnStatement;

                ExpressionSyntax expr;
                if (_tokens.CurrentKind == SyntaxKind.SemicolonToken)
                {
                    _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected expression after 'yield return'."));
                    expr = CreateMissingIdentifierName(_tokens.Current.Span.Start);
                }
                else
                {
                    expr = ParseExpression();
                }

                var semi = MatchToken(SyntaxKind.SemicolonToken);
                return new YieldStatementSyntax(kind, yieldKeyword, returnOrBreak, expr, semi);
            }

            // yield break;
            returnOrBreak = MatchToken(SyntaxKind.BreakKeyword);
            kind = SyntaxKind.YieldBreakStatement;

            var semi2 = MatchToken(SyntaxKind.SemicolonToken);
            return new YieldStatementSyntax(kind, yieldKeyword, returnOrBreak, expression: null, semicolonToken: semi2);
        }
        private void ReportMissingInitializers(VariableDeclarationSyntax decl, string message)
        {
            for (int i = 0; i < decl.Variables.Count; i++)
            {
                var v = decl.Variables[i];
                if (v.Initializer is null)
                    _diagnostics.Add(new SyntaxDiagnostic(v.Identifier.Span.Start, message));
            }
        }
        private bool TryParseForVariableDeclaration(out VariableDeclarationSyntax? decl)
        {
            if (TryParse(
                parse: () =>
                {
                    var type = ParseType();
                    var firstId = MatchToken(SyntaxKind.IdentifierToken);
                    var vars = ParseVariableDeclarators(firstId, closeKind: SyntaxKind.SemicolonToken);
                    return new VariableDeclarationSyntax(type, vars);
                },
                out VariableDeclarationSyntax parsed,
                tempContext: ParseContext.Type,
                requireProgress: true,
                requireNoNewDiagnostics: true,
                validateAfter: () => _tokens.CurrentKind == SyntaxKind.SemicolonToken))
            {
                decl = parsed;
                return true;
            }

            decl = null;
            return false;
        }
        private IfStatementSyntax ParseIfStatement()
        {
            var ifKeyword = MatchToken(SyntaxKind.IfKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            var statement = ParseStatement();

            ElseClauseSyntax? elseClause = null;
            if (_tokens.CurrentKind == SyntaxKind.ElseKeyword)
            {
                var elseKeyword = _tokens.EatToken();
                var elseStmt = ParseStatement();
                elseClause = new ElseClauseSyntax(elseKeyword, elseStmt);
            }

            return new IfStatementSyntax(ifKeyword, openParen, condition, closeParen, statement, elseClause);
        }
        private SwitchStatementSyntax ParseSwitchStatement()
        {
            var switchKeyword = MatchToken(SyntaxKind.SwitchKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);

            ExpressionSyntax expr;
            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(openParen.Span.End, "Expected expression in switch statement."));
                expr = CreateMissingIdentifierName(openParen.Span.End);
            }
            else
            {
                expr = ParseExpression();
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var openBrace = MatchToken(SyntaxKind.OpenBraceToken);

            var sections = new List<SwitchSectionSyntax>();

            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                sections.Add(ParseSwitchSection());

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in switch section parsing.");
                    break;
                }
            }

            var closeBrace = MatchToken(SyntaxKind.CloseBraceToken);

            return new SwitchStatementSyntax(
                switchKeyword, openParen, expr, closeParen,
                openBrace,
                new SyntaxList<SwitchSectionSyntax>(sections.ToArray()),
                closeBrace);
        }
        private SwitchSectionSyntax ParseSwitchSection()
        {
            var labels = new List<SwitchLabelSyntax>();

            // At least one label
            if (_tokens.CurrentKind != SyntaxKind.CaseKeyword &&
                _tokens.CurrentKind != SyntaxKind.DefaultKeyword)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected 'case' or 'default' label."));
                // recovery
                EatAsSkippedToken("Skipping token while recovering to a switch label.");
            }

            while (_tokens.CurrentKind == SyntaxKind.CaseKeyword ||
                   _tokens.CurrentKind == SyntaxKind.DefaultKeyword)
            {
                labels.Add(ParseSwitchLabel());
            }

            var statements = new List<StatementSyntax>();

            while (_tokens.CurrentKind != SyntaxKind.CaseKeyword &&
                   _tokens.CurrentKind != SyntaxKind.DefaultKeyword &&
                   _tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;
                statements.Add(ParseStatement());

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in switch section statements parsing.");
                    break;
                }
            }

            return new SwitchSectionSyntax(
                new SyntaxList<SwitchLabelSyntax>(labels.ToArray()),
                new SyntaxList<StatementSyntax>(statements.ToArray()));
        }
        private SwitchLabelSyntax ParseSwitchLabel()
        {
            if (_tokens.CurrentKind == SyntaxKind.DefaultKeyword)
            {
                var defaultKeyword = _tokens.EatToken();
                var colon = MatchToken(SyntaxKind.ColonToken);
                return new DefaultSwitchLabelSyntax(defaultKeyword, colon);
            }

            var caseKeyword = MatchToken(SyntaxKind.CaseKeyword);
            var pattern = ParsePatternCore();

            WhenClauseSyntax? whenClause = null;
            if (_tokens.CurrentKind == SyntaxKind.WhenKeyword)
                whenClause = ParseWhenClause();

            if (whenClause is null &&
                pattern is ConstantPatternSyntax cp &&
                _tokens.CurrentKind == SyntaxKind.ColonToken)
            {
                var colon = _tokens.EatToken();
                return new CaseSwitchLabelSyntax(caseKeyword, cp.Expression, colon);
            }

            var colon2 = MatchToken(SyntaxKind.ColonToken);
            return new CasePatternSwitchLabelSyntax(caseKeyword, pattern, whenClause, colon2);
        }
        private SeparatedSyntaxList<VariableDeclaratorSyntax> ParseVariableDeclarators(SyntaxToken firstIdentifier, SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            var first = ParseVariableDeclaratorAfterIdentifier(firstIdentifier);
            list.Add(new SyntaxNodeOrToken(first));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));

                var idStart = _tokens.Position;
                var id = MatchToken(SyntaxKind.IdentifierToken);
                if (_tokens.Position == idStart && _tokens.CurrentKind != closeKind && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                    EatAsSkippedToken("Expected identifier after comma in variable declarators.");

                var v = ParseVariableDeclaratorAfterIdentifier(id);
                list.Add(new SyntaxNodeOrToken(v));
            }

            return new SeparatedSyntaxList<VariableDeclaratorSyntax>(list.ToArray());
        }
        private VariableDeclaratorSyntax ParseVariableDeclaratorAfterIdentifier(SyntaxToken id)
        {
            BracketedArgumentListSyntax? args = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
            {
                args = ParseBracketedArgumentList();
            }

            EqualsValueClauseSyntax? init = null;
            if (_tokens.CurrentKind == SyntaxKind.EqualsToken)
                init = ParseEqualsValueClause();

            return new VariableDeclaratorSyntax(id, args, init);
        }

        private EqualsValueClauseSyntax ParseEqualsValueClause()
        {
            var eq = MatchToken(SyntaxKind.EqualsToken);
            var value = ParseExpression();
            return new EqualsValueClauseSyntax(eq, value);
        }

        // names/types
        private NameSyntax ParseName()
        {
            NameSyntax left = ParseSimpleName();

            while (_tokens.CurrentKind == SyntaxKind.DotToken)
            {
                var dot = _tokens.EatToken();
                var right = ParseSimpleName();
                left = new QualifiedNameSyntax(left, dot, right);
            }

            return left;
        }
        private SimpleNameSyntax ParseSimpleName()
        {
            var id = MatchToken(SyntaxKind.IdentifierToken);

            if (_tokens.CurrentKind == SyntaxKind.LessThanToken)
            {
                var tal = ParseTypeArgumentList();
                return new GenericNameSyntax(id, tal);
            }

            return new IdentifierNameSyntax(id);
        }
        private SimpleNameSyntax ParseSimpleNameInExpressionContext()
        {
            var id = MatchToken(SyntaxKind.IdentifierToken);

            if (_tokens.Current.Kind == SyntaxKind.LessThanToken && IsTypeArgumentListInExpressionContext())
            {
                var tal = ParseTypeArgumentList();
                return new GenericNameSyntax(id, tal);
            }

            return new IdentifierNameSyntax(id);
        }
        private TypeArgumentListSyntax ParseTypeArgumentList()
        {
            var lt = MatchToken(SyntaxKind.LessThanToken);

            var args = new List<SyntaxNodeOrToken>();

            // At least one type
            args.Add(new SyntaxNodeOrToken(ParseType()));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                args.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                args.Add(new SyntaxNodeOrToken(ParseType()));
            }
            var gt = EatGreaterThanTokenForTypeArgs(); // handles >, >>, >>>
            return new TypeArgumentListSyntax(lt, new SeparatedSyntaxList<TypeSyntax>(args.ToArray()), gt);
        }
        private bool TryEatGreaterThanTokenForTypeArgs()
        {
            if (_tokens.Current.Kind == SyntaxKind.GreaterThanToken)
            {
                _tokens.EatToken();
                return true;
            }

            // split >> or >>>
            var t = _tokens.Current;

            if (t.Kind == SyntaxKind.GreaterThanGreaterThanToken)
            {
                _tokens.EatToken();
                var (first, second) = SplitShiftTokenIntoGreaterThans(t, count: 2);
                _tokens.PrependInjected(second);
                return true;
            }

            if (t.Kind == SyntaxKind.GreaterThanGreaterThanGreaterThanToken)
            {
                _tokens.EatToken();
                var parts = SplitShiftTokenIntoGreaterThans3(t);
                _tokens.PrependInjected(parts[1], parts[2]);
                return true;
            }

            return false;
        }
        private SyntaxToken EatGreaterThanTokenForTypeArgs()
        {
            if (_tokens.CurrentKind == SyntaxKind.GreaterThanToken)
                return _tokens.EatToken();

            // split >> or >>>
            var t = _tokens.Current;
            if (t.Kind == SyntaxKind.GreaterThanGreaterThanToken)
            {
                _tokens.EatToken();
                var (first, second) = SplitShiftTokenIntoGreaterThans(t, count: 2);
                _tokens.PrependInjected(second);
                return first;
            }

            if (t.Kind == SyntaxKind.GreaterThanGreaterThanGreaterThanToken)
            {
                _tokens.EatToken();
                var parts = SplitShiftTokenIntoGreaterThans3(t);
                _tokens.PrependInjected(parts[1], parts[2]);
                return parts[0];
            }

            // recovery
            _diagnostics.Add(new SyntaxDiagnostic(t.Span.Start, $"Expected '>' in type argument list, found '{t.Kind}'."));
            return CreateMissingToken(SyntaxKind.GreaterThanToken, t.Span.Start);
        }
        private static (SyntaxToken first, SyntaxToken second) SplitShiftTokenIntoGreaterThans(SyntaxToken t, int count)
        {
            // count==2 for >>
            int start = t.Span.Start;

            var first = new SyntaxToken(
                SyntaxKind.GreaterThanToken,
                new TextSpan(start, 1),
                valueText: null,
                value: null,
                leadingTrivia: t.LeadingTrivia,
                trailingTrivia: Array.Empty<SyntaxTrivia>());

            var second = new SyntaxToken(
                SyntaxKind.GreaterThanToken,
                new TextSpan(start + 1, 1),
                valueText: null,
                value: null,
                leadingTrivia: Array.Empty<SyntaxTrivia>(),
                trailingTrivia: t.TrailingTrivia);

            return (first, second);
        }
        private static SyntaxToken[] SplitShiftTokenIntoGreaterThans3(SyntaxToken t)
        {
            int start = t.Span.Start;

            var first = new SyntaxToken(SyntaxKind.GreaterThanToken, new TextSpan(start, 1), null, null, t.LeadingTrivia, Array.Empty<SyntaxTrivia>());
            var second = new SyntaxToken(SyntaxKind.GreaterThanToken, new TextSpan(start + 1, 1), null, null, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
            var third = new SyntaxToken(SyntaxKind.GreaterThanToken, new TextSpan(start + 2, 1), null, null, Array.Empty<SyntaxTrivia>(), t.TrailingTrivia);

            return new[] { first, second, third };
        }
        private TypeSyntax ParseType()
        {
            using var __ = _ctx.Push(ParseContext.Type);
            TypeSyntax t = ParseTypeCore();
            while (true)
            {
                if (_tokens.CurrentKind == SyntaxKind.QuestionToken)
                {
                    var q = _tokens.EatToken();
                    t = new NullableTypeSyntax(t, q);
                    continue;
                }

                if (_tokens.CurrentKind == SyntaxKind.AsteriskToken)
                {
                    var star = _tokens.EatToken();
                    t = new PointerTypeSyntax(t, star);
                    continue;
                }

                if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                {
                    var ranks = new List<ArrayRankSpecifierSyntax>();

                    while (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                        ranks.Add(ParseArrayRankSpecifier());

                    t = new ArrayTypeSyntax(t, new SyntaxList<ArrayRankSpecifierSyntax>(ranks.ToArray()));
                    continue;
                }

                break;
            }

            return t;
        }
        private bool ScanTypeArgumentList()
        {
            if (_tokens.Current.Kind != SyntaxKind.LessThanToken)
                return false;

            _tokens.EatToken(); // '<'

            if (!ScanType())
                return false;

            while (_tokens.Current.Kind == SyntaxKind.CommaToken)
            {
                _tokens.EatToken();
                if (!ScanType())
                    return false;
            }

            return TryEatGreaterThanTokenForTypeArgs();
        }
        private bool ScanSimpleName()
        {
            if (_tokens.Current.Kind != SyntaxKind.IdentifierToken)
                return false;

            _tokens.EatToken();

            if (_tokens.Current.Kind == SyntaxKind.LessThanToken)
            {
                if (!ScanTypeArgumentList())
                    return false;
            }

            return true;
        }

        private bool ScanName()
        {
            if (!ScanSimpleName())
                return false;

            while (_tokens.Current.Kind == SyntaxKind.DotToken)
            {
                _tokens.EatToken();
                if (!ScanSimpleName())
                    return false;
            }

            return true;
        }
        private bool ScanType()
        {
            // ref type
            if (_tokens.Current.Kind == SyntaxKind.RefKeyword)
            {
                _tokens.EatToken(); // ref

                if (_tokens.Current.Kind == SyntaxKind.ReadOnlyKeyword)
                    _tokens.EatToken(); // readonly

                return ScanType();
            }
            // tuple type
            if (_tokens.Current.Kind == SyntaxKind.OpenParenToken)
            {
                if (!ScanTupleTypeCore())
                    return false;
            }
            // predefined
            else if (IsPredefinedTypeKeyword(_tokens.Current.Kind)
                || (_tokens.Current.Kind == SyntaxKind.IdentifierToken
                && (_tokens.Current.ValueText == "nint" || _tokens.Current.ValueText == "nuint")))
            {
                _tokens.EatToken();
            }
            else
            {
                if (!ScanName())
                    return false;
            }

            // suffixes
            while (true)
            {
                if (_tokens.Current.Kind == SyntaxKind.QuestionToken)
                {
                    _tokens.EatToken();
                    continue;
                }
                if (_tokens.Current.Kind == SyntaxKind.AsteriskToken)
                {
                    _tokens.EatToken();
                    continue;
                }
                if (_tokens.Current.Kind == SyntaxKind.OpenBracketToken)
                {
                    do
                    {
                        _tokens.EatToken(); // '['
                        while (_tokens.Current.Kind == SyntaxKind.CommaToken)
                            _tokens.EatToken();
                        if (_tokens.Current.Kind != SyntaxKind.CloseBracketToken)
                            return false;
                        _tokens.EatToken(); // ']'
                    }
                    while (_tokens.Current.Kind == SyntaxKind.OpenBracketToken);

                    continue;
                }

                break;
            }

            return true;
        }
        private bool ScanTupleTypeCore()
        {
            if (_tokens.Current.Kind != SyntaxKind.OpenParenToken)
                return false;

            _tokens.EatToken(); // '('

            // First element
            if (!ScanType())
                return false;

            // Optional element name
            if (_tokens.Current.Kind == SyntaxKind.IdentifierToken)
            {
                var next = _tokens.Peek(1).Kind;
                if (next == SyntaxKind.CommaToken || next == SyntaxKind.CloseParenToken)
                    _tokens.EatToken();
            }

            if (_tokens.Current.Kind != SyntaxKind.CommaToken)
                return false;

            while (_tokens.Current.Kind == SyntaxKind.CommaToken)
            {
                _tokens.EatToken();

                if (!ScanType())
                    return false;

                if (_tokens.Current.Kind == SyntaxKind.IdentifierToken)
                {
                    var next = _tokens.Peek(1).Kind;
                    if (next == SyntaxKind.CommaToken || next == SyntaxKind.CloseParenToken)
                        _tokens.EatToken();
                }
            }
            if (_tokens.Current.Kind != SyntaxKind.CloseParenToken)
                return false;

            _tokens.EatToken(); // ')'
            return true;
        }
        private bool IsTypeArgumentListInExpressionContext()
        {
            return Probe(
                scan: () =>
                {
                    if (!ScanTypeArgumentList())
                        return false;

                    var k = _tokens.Current.Kind;
                    return k == SyntaxKind.OpenParenToken || k == SyntaxKind.DotToken;
                },
                tempContext: ParseContext.Type);
        }
        private ArrayRankSpecifierSyntax ParseArrayRankSpecifier()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);

            var list = new List<SyntaxNodeOrToken>();

            // First omitted
            list.Add(new SyntaxNodeOrToken(new OmittedArraySizeExpressionSyntax(
                CreateMissingToken(SyntaxKind.OmittedArraySizeExpressionToken, open.Span.End))));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));

                list.Add(new SyntaxNodeOrToken(new OmittedArraySizeExpressionSyntax(
                    CreateMissingToken(SyntaxKind.OmittedArraySizeExpressionToken, comma.Span.End))));
            }

            var close = MatchToken(SyntaxKind.CloseBracketToken);
            return new ArrayRankSpecifierSyntax(open, new SeparatedSyntaxList<ExpressionSyntax>(list.ToArray()), close);
        }
        private TypeSyntax ParseTypeCore()
        {
            if (_tokens.CurrentKind == SyntaxKind.RefKeyword)
            {
                var refKeyword = _tokens.EatToken();

                SyntaxToken readOnlyKeyword = default;
                if (_tokens.CurrentKind == SyntaxKind.ReadOnlyKeyword)
                    readOnlyKeyword = _tokens.EatToken();

                var type = ParseType();
                return new RefTypeSyntax(refKeyword, readOnlyKeyword, type);
            }

            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                return ParseTupleType();

            if (IsPredefinedTypeKeyword(_tokens.CurrentKind)
                || (_tokens.Current.Kind == SyntaxKind.IdentifierToken
                && (_tokens.Current.ValueText == "nint" || _tokens.Current.ValueText == "nuint")))
            {
                var kw = _tokens.EatToken();
                return new PredefinedTypeSyntax(kw);
            }

            return ParseName();
        }
        private TupleTypeSyntax ParseTupleType()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);

            var list = new List<SyntaxNodeOrToken>();

            // First element
            list.Add(new SyntaxNodeOrToken(ParseTupleTypeElement()));

            bool hasComma = false;
            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                hasComma = true;
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));

                if (_tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                    _tokens.CurrentKind == SyntaxKind.EndOfFileToken)
                {
                    _diagnostics.Add(new SyntaxDiagnostic(comma.Span.End, "Expected tuple element after ','."));
                    var missingType = (TypeSyntax)CreateMissingIdentifierName(comma.Span.End);
                    list.Add(new SyntaxNodeOrToken(new TupleElementSyntax(missingType, default)));
                    break;
                }

                list.Add(new SyntaxNodeOrToken(ParseTupleTypeElement()));
            }

            var close = MatchToken(SyntaxKind.CloseParenToken);

            if (!hasComma)
                _diagnostics.Add(new SyntaxDiagnostic(close.Span.Start, "Tuple type must contain at least two elements."));

            return new TupleTypeSyntax(open, new SeparatedSyntaxList<TupleElementSyntax>(list.ToArray()), close);
        }
        private TupleElementSyntax ParseTupleTypeElement()
        {
            // Element type
            var type = ParseType();

            // Optional element name
            SyntaxToken identifier = default;
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken)
            {
                var next = _tokens.Peek(1).Kind;
                if (next == SyntaxKind.CommaToken || next == SyntaxKind.CloseParenToken)
                    identifier = _tokens.EatToken();
            }

            return new TupleElementSyntax(type, identifier);
        }
        // expressions
        private ExpressionSyntax ParseExpression()
        {
            using var __ = _ctx.Push(ParseContext.Expression);

            if (IsAnonymousMethodExpressionStart())
                return ParseAnonymousMethodExpression();

            if (IsLambdaExpressionStart())
                return ParseLambdaExpression();

            return ParseAssignmentExpression();
        }
        private ExpressionSyntax ParseAssignmentExpression()
        {
            var left = ParseConditionalExpression();

            if (IsAssignmentOperator(_tokens.CurrentKind))
            {
                var op = _tokens.EatToken();
                var right = ParseAssignmentExpression(); // right associative
                var kind = GetAssignmentExpressionKind(op.Kind);
                return new AssignmentExpressionSyntax(kind, left, op, right);
            }

            return left;
        }
        private ExpressionSyntax ParseConditionalExpression()
        {
            var condition = ParseNullCoalescingExpression();

            if (_tokens.CurrentKind == SyntaxKind.QuestionToken)
            {
                var question = _tokens.EatToken();
                var whenTrue = ParseExpression();
                var colon = MatchToken(SyntaxKind.ColonToken);
                var whenFalse = ParseExpression();
                return new ConditionalExpressionSyntax(condition, question, whenTrue, colon, whenFalse);
            }

            return condition;
        }
        private ExpressionSyntax ParseNullCoalescingExpression()
        {
            var left = ParseBinaryExpression();

            if (_tokens.CurrentKind == SyntaxKind.QuestionQuestionToken)
            {
                var op = _tokens.EatToken();
                var right = ParseNullCoalescingExpression(); // right associative
                return new BinaryExpressionSyntax(SyntaxKind.CoalesceExpression, left, op, right);
            }

            return left;
        }
        private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
        {
            var left = ParseRangeExpression();

            while (true)
            {
                int precedence = SyntaxFacts.GetBinaryOperatorPrecedence(_tokens.CurrentKind);
                if (precedence == 0 || precedence <= parentPrecedence)
                    break;

                var op = _tokens.EatToken();

                ExpressionSyntax right;
                if (op.Kind == SyntaxKind.IsKeyword || op.Kind == SyntaxKind.AsKeyword)
                {
                    right = ParseType();
                }
                else
                {
                    right = ParseBinaryExpression(precedence);
                }

                var kind = GetBinaryExpressionKind(op.Kind);
                left = new BinaryExpressionSyntax(kind, left, op, right);
            }

            return left;
        }
        private ExpressionSyntax ParseRangeExpression()
        {
            // prefix range: ..x / ..
            if (_tokens.CurrentKind == SyntaxKind.DotDotToken)
            {
                var op = _tokens.EatToken();
                ExpressionSyntax? right = IsStartOfUnaryExpression(_tokens.Current)
                    ? ParseUnaryExpression()
                    : null;

                return new RangeExpressionSyntax(null, op, right);
            }

            var left = ParseUnaryExpression();

            if (_tokens.CurrentKind == SyntaxKind.DotDotToken)
            {
                var op = _tokens.EatToken();
                ExpressionSyntax? right = IsStartOfUnaryExpression(_tokens.Current)
                    ? ParseUnaryExpression()
                    : null;

                return new RangeExpressionSyntax(left, op, right);
            }

            return left;
        }

        private ExpressionSyntax ParseUnaryExpression()
        {
            if (_tokens.CurrentKind == SyntaxKind.ThrowKeyword)
                return ParseThrowExpression();

            if (IsAwaitKeyword())
            {
                var awaitKeyword = _tokens.EatToken();
                var operand = ParseUnaryExpression();
                return new AwaitExpressionSyntax(awaitKeyword, operand);
            }

            if (TryParseCastExpression(out var castExpr))
                return castExpr;

            // prefix unary
            int prec = SyntaxFacts.GetUnaryOperatorPrecedence(_tokens.CurrentKind);
            if (prec != 0)
            {
                var op = _tokens.EatToken();
                var operand = ParseUnaryExpression();
                var kind = GetPrefixUnaryExpressionKind(op.Kind);
                return new PrefixUnaryExpressionSyntax(kind, op, operand);
            }
            // postfix unary
            return ParsePostfixExpression();

        }
        private ThrowExpressionSyntax ParseThrowExpression()
        {
            var throwKeyword = MatchToken(SyntaxKind.ThrowKeyword);

            if (_tokens.CurrentKind is SyntaxKind.SemicolonToken
                or SyntaxKind.CommaToken
                or SyntaxKind.CloseParenToken
                or SyntaxKind.CloseBracketToken
                or SyntaxKind.CloseBraceToken
                or SyntaxKind.EndOfFileToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(throwKeyword.Span.End, "Expected expression after 'throw'."));
                var missing = CreateMissingIdentifierName(throwKeyword.Span.End);
                return new ThrowExpressionSyntax(throwKeyword, missing);
            }
            var expr = ParseExpression();
            return new ThrowExpressionSyntax(throwKeyword, expr);
        }
        private bool TryParseCastExpression(out ExpressionSyntax expr)
        {
            expr = default!;

            if (_tokens.CurrentKind != SyntaxKind.OpenParenToken)
                return false;

            return TryParse(
                parse: () =>
                {
                    var open = _tokens.EatToken();
                    var type = ParseType();
                    var close = MatchToken(SyntaxKind.CloseParenToken);

                    if (!TypeDefinitelyNotExpression(type) && !IsCastFollowerToken(_tokens.Current))
                    {
                        return (ExpressionSyntax)new ParenthesizedExpressionSyntax(
                            open, CreateMissingIdentifierName(close.Span.End), close);
                    }

                    var operand = ParseUnaryExpression();
                    return (ExpressionSyntax)new CastExpressionSyntax(open, type, close, operand);
                },
                out expr,
                tempContext: ParseContext.Type,
                requireProgress: true,
                requireNoNewDiagnostics: true,
                validateNode: e => e is CastExpressionSyntax);
        }
        private ExpressionSyntax ParsePostfixExpression()
        {
            var expr = ParsePrimaryExpression();

            while (true)
            {
                switch (_tokens.CurrentKind)
                {
                    case SyntaxKind.OpenParenToken:
                        expr = new InvocationExpressionSyntax(expr, ParseArgumentList());
                        continue;

                    case SyntaxKind.OpenBracketToken:
                        expr = new ElementAccessExpressionSyntax(expr, ParseBracketedArgumentList());
                        continue;

                    case SyntaxKind.DotToken:
                    case SyntaxKind.MinusGreaterThanToken:
                        expr = ParseMemberAccess(expr);
                        continue;

                    case SyntaxKind.PlusPlusToken:
                    case SyntaxKind.MinusMinusToken:
                    case SyntaxKind.ExclamationToken:
                        expr = ParsePostfixUnary(expr);
                        continue;

                    case SyntaxKind.SwitchKeyword:
                        expr = ParseSwitchExpressionAfterGoverningExpression(expr);
                        continue;

                    default:
                        return expr;
                }
            }
        }
        private ExpressionSyntax ParsePostfixUnary(ExpressionSyntax operand)
        {
            var op = _tokens.EatToken();
            var kind = GetPostfixUnaryExpressionKind(op.Kind);
            return new PostfixUnaryExpressionSyntax(kind, operand, op);
        }
        private ExpressionSyntax ParseParenthesizedOrTupleExpression()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected expression."));
                var missing = CreateMissingIdentifierName(_tokens.Current.Span.Start);
                var close0 = _tokens.EatToken();
                return new ParenthesizedExpressionSyntax(open, missing, close0);
            }
            bool forceTuple = _tokens.CurrentKind == SyntaxKind.IdentifierToken &&
                      _tokens.Peek(1).Kind == SyntaxKind.ColonToken;
            if (!forceTuple)
            {
                var expr = ParseExpression();

                // Parenthesized expression: (expr)
                if (_tokens.CurrentKind != SyntaxKind.CommaToken)
                {
                    var close0 = MatchToken(SyntaxKind.CloseParenToken);
                    return new ParenthesizedExpressionSyntax(open, expr, close0);
                }

                // Tuple expression: (expr, ...)
                var list = new List<SyntaxNodeOrToken>
                {
                    new SyntaxNodeOrToken(new ArgumentSyntax(null, null, expr))
                };

                while (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    var comma = _tokens.EatToken();
                    list.Add(new SyntaxNodeOrToken(comma));

                    if (_tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                        _tokens.CurrentKind == SyntaxKind.EndOfFileToken)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(comma.Span.End, "Expected expression after ',' in tuple expression."));
                        var missing = CreateMissingIdentifierName(comma.Span.End);
                        list.Add(new SyntaxNodeOrToken(new ArgumentSyntax(null, null, missing)));
                        break;
                    }

                    list.Add(new SyntaxNodeOrToken(ParseArgument()));
                }

                var close = MatchToken(SyntaxKind.CloseParenToken);
                return new TupleExpressionSyntax(open, new SeparatedSyntaxList<ArgumentSyntax>(list.ToArray()), close);
            }
            {
                var list = new List<SyntaxNodeOrToken>
                {
                    new SyntaxNodeOrToken(ParseArgument())
                };

                bool hasComma = false;
                while (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    hasComma = true;
                    var comma = _tokens.EatToken();
                    list.Add(new SyntaxNodeOrToken(comma));

                    if (_tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                        _tokens.CurrentKind == SyntaxKind.EndOfFileToken)
                    {
                        _diagnostics.Add(new SyntaxDiagnostic(comma.Span.End, "Expected expression after ',' in tuple expression."));
                        var missing = CreateMissingIdentifierName(comma.Span.End);
                        list.Add(new SyntaxNodeOrToken(new ArgumentSyntax(null, null, missing)));
                        break;
                    }

                    list.Add(new SyntaxNodeOrToken(ParseArgument()));
                }

                var close = MatchToken(SyntaxKind.CloseParenToken);

                if (!hasComma)
                    _diagnostics.Add(new SyntaxDiagnostic(close.Span.Start, "Tuple expression must contain at least two elements."));

                return new TupleExpressionSyntax(open, new SeparatedSyntaxList<ArgumentSyntax>(list.ToArray()), close);
            }
        }
        private TypeOfExpressionSyntax ParseTypeOfExpression()
        {
            var typeOfKeyword = MatchToken(SyntaxKind.TypeOfKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var type = ParseType();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new TypeOfExpressionSyntax(typeOfKeyword, openParen, type, closeParen);
        }

        private SizeOfExpressionSyntax ParseSizeOfExpression()
        {
            var sizeOfKeyword = MatchToken(SyntaxKind.SizeOfKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var type = ParseType();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new SizeOfExpressionSyntax(sizeOfKeyword, openParen, type, closeParen);
        }
        private CheckedExpressionSyntax ParseCheckedExpression(bool isChecked)
        {
            var keyword = MatchToken(isChecked ? SyntaxKind.CheckedKeyword : SyntaxKind.UncheckedKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var expr = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new CheckedExpressionSyntax(
                isChecked ? SyntaxKind.CheckedExpression : SyntaxKind.UncheckedExpression,
                keyword,
                openParen,
                expr,
                closeParen);
        }

        private ExpressionSyntax ParsePrimaryExpression()
        {
            var t = _tokens.Current;

            if (IsDeclarationExpressionStart())
                return ParseDeclarationExpression();

            if (IsPredefinedTypeKeyword(t.Kind)
                || (t.Kind == SyntaxKind.IdentifierToken && (t.ValueText == "nint" || t.ValueText == "nuint")))
                return new PredefinedTypeSyntax(_tokens.EatToken());

            switch (t.Kind)
            {
                case SyntaxKind.TypeOfKeyword:
                    return ParseTypeOfExpression();

                case SyntaxKind.SizeOfKeyword:
                    return ParseSizeOfExpression();
                case SyntaxKind.InterpolatedStringStartToken:
                case SyntaxKind.InterpolatedVerbatimStringStartToken:
                case SyntaxKind.InterpolatedSingleLineRawStringStartToken:
                case SyntaxKind.InterpolatedMultiLineRawStringStartToken:
                    return ParseInterpolatedStringExpression();

                case SyntaxKind.SingleLineRawStringLiteralToken:
                case SyntaxKind.MultiLineRawStringLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.StringLiteralExpression, _tokens.EatToken());

                case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
                case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.Utf8StringLiteralExpression, _tokens.EatToken());

                case SyntaxKind.ThisKeyword:
                    return new ThisExpressionSyntax(_tokens.EatToken());

                case SyntaxKind.BaseKeyword:
                    return new BaseExpressionSyntax(_tokens.EatToken());

                case SyntaxKind.NewKeyword:
                    return ParseNewExpression();

                case SyntaxKind.StackAllocKeyword:
                    return ParseStackAllocExpression();

                case SyntaxKind.CheckedKeyword:
                    return ParseCheckedExpression(isChecked: true);

                case SyntaxKind.UncheckedKeyword:
                    return ParseCheckedExpression(isChecked: false);

                case SyntaxKind.IdentifierToken:
                    return ParseSimpleNameInExpressionContext();

                case SyntaxKind.NumericLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.NumericLiteralExpression, _tokens.EatToken());

                case SyntaxKind.StringLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.StringLiteralExpression, _tokens.EatToken());

                case SyntaxKind.Utf8StringLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.Utf8StringLiteralExpression, _tokens.EatToken());

                case SyntaxKind.CharacterLiteralToken:
                    return new LiteralExpressionSyntax(SyntaxKind.CharacterLiteralExpression, _tokens.EatToken());

                case SyntaxKind.TrueKeyword:
                    return new LiteralExpressionSyntax(SyntaxKind.TrueLiteralExpression, _tokens.EatToken());

                case SyntaxKind.FalseKeyword:
                    return new LiteralExpressionSyntax(SyntaxKind.FalseLiteralExpression, _tokens.EatToken());

                case SyntaxKind.NullKeyword:
                    return new LiteralExpressionSyntax(SyntaxKind.NullLiteralExpression, _tokens.EatToken());

                case SyntaxKind.OpenParenToken:
                    return ParseParenthesizedOrTupleExpression();

                case SyntaxKind.OpenBracketToken:
                    return ParseCollectionExpression();

                case SyntaxKind.DelegateKeyword:
                    return ParseAnonymousMethodExpression();

                default:
                    if (IsExpressionTerminator(t.Kind) || t.Kind == SyntaxKind.EndOfFileToken)
                        return CreateMissingIdentifierName(t.Span.Start);

                    EatAsSkippedToken($"Unexpected token in expression: '{t.Kind}'.");
                    return CreateMissingIdentifierName(t.Span.Start);
            }
        }
        private InterpolatedStringExpressionSyntax ParseInterpolatedStringExpression()
        {
            var start = _tokens.EatToken();

            var isRaw =
                start.Kind == SyntaxKind.InterpolatedSingleLineRawStringStartToken ||
                start.Kind == SyntaxKind.InterpolatedMultiLineRawStringStartToken;

            var endKind = isRaw
                ? SyntaxKind.InterpolatedRawStringEndToken
                : SyntaxKind.InterpolatedStringEndToken;

            var contents = new List<InterpolatedStringContentSyntax>();

            while (_tokens.CurrentKind != endKind && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                if (_tokens.CurrentKind == SyntaxKind.InterpolatedStringTextToken)
                {
                    contents.Add(new InterpolatedStringTextSyntax(_tokens.EatToken()));
                    continue;
                }

                if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
                {
                    contents.Add(ParseInterpolation());
                    continue;
                }

                EatAsSkippedToken($"Unexpected token in interpolated string: '{_tokens.CurrentKind}'.");
            }
            var end = MatchToken(endKind);

            return new InterpolatedStringExpressionSyntax(
                start,
                new SyntaxList<InterpolatedStringContentSyntax>(contents.ToArray()),
                end);
        }
        private InterpolationSyntax ParseInterpolation()
        {
            var openBrace = MatchToken(SyntaxKind.OpenBraceToken);
            var expr = ParseExpression();

            InterpolationAlignmentClauseSyntax? alignment = null;
            if (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                var value = ParseExpression();
                alignment = new InterpolationAlignmentClauseSyntax(comma, value);
            }

            InterpolationFormatClauseSyntax? format = null;
            if (_tokens.CurrentKind == SyntaxKind.ColonToken)
            {
                var colon = _tokens.EatToken();
                _tokens.EnterInterpolationFormat();

                var formatToken = MatchToken(SyntaxKind.InterpolatedStringTextToken);
                format = new InterpolationFormatClauseSyntax(colon, formatToken);
            }
            var closeBrace = MatchToken(SyntaxKind.CloseBraceToken);

            return new InterpolationSyntax(openBrace, expr, alignment, format, closeBrace);
        }
        private CollectionExpressionSyntax ParseCollectionExpression()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);
            var elements = ParseSeparatedCollectionElements(closeKind: SyntaxKind.CloseBracketToken);
            var close = MatchToken(SyntaxKind.CloseBracketToken);
            return new CollectionExpressionSyntax(open, elements, close);
        }
        private SeparatedSyntaxList<CollectionElementSyntax> ParseSeparatedCollectionElements(SyntaxKind closeKind)
        {
            var list = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == closeKind)
                return new SeparatedSyntaxList<CollectionElementSyntax>(list.ToArray());

            while (_tokens.CurrentKind != closeKind && _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                var start = _tokens.Position;

                var element = ParseCollectionElement();
                list.Add(new SyntaxNodeOrToken(element));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    if (_tokens.CurrentKind == closeKind) // trailing comma allowed
                        break;
                    continue;
                }

                if (_tokens.Position == start)
                    EatAsSkippedToken("Parser made no progress in collection expression element parsing.");
                break;
            }

            return new SeparatedSyntaxList<CollectionElementSyntax>(list.ToArray());
        }

        private CollectionElementSyntax ParseCollectionElement()
        {
            if (_tokens.CurrentKind == SyntaxKind.DotDotToken)
            {
                var dotdot = _tokens.EatToken();

                if (IsExpressionTerminator(_tokens.CurrentKind) || _tokens.CurrentKind == SyntaxKind.CommaToken || _tokens.CurrentKind == SyntaxKind.CloseBracketToken)
                {
                    _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Spread element requires an expression."));
                    var missing = CreateMissingIdentifierName(_tokens.Current.Span.Start);
                    return new SpreadElementSyntax(dotdot, missing);
                }

                var expr = ParseExpression();
                return new SpreadElementSyntax(dotdot, expr);
            }

            var e = ParseExpression();
            return new ExpressionElementSyntax(e);
        }
        private ExpressionSyntax ParseNewExpression()
        {
            var newKeyword = MatchToken(SyntaxKind.NewKeyword);

            // Implicit array creation
            if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                return ParseImplicitArrayCreationExpression(newKeyword);

            // Target typed
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                return ParseImplicitObjectCreationExpression(newKeyword);

            // Anonymous object creation
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Anonymous object creation 'new { ... }' is not implemented."));

                var missingType = (TypeSyntax)CreateMissingIdentifierName(_tokens.Current.Span.Start);
                var init = ParseInitializerExpression(SyntaxKind.ObjectInitializerExpression);
                return new ObjectCreationExpressionSyntax(newKeyword, missingType, argumentList: null, initializer: init);
            }

            var type = ParseTypeWithoutArrayCreationRankSpecifiers();

            // Array creation
            if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                return ParseArrayCreationExpression(newKeyword, type);

            return ParseObjectCreationExpressionAfterNew(newKeyword, type);
        }
        private ExpressionSyntax ParseImplicitObjectCreationExpression(SyntaxToken newKeyword)
        {
            var argList = ParseArgumentList();
            InitializerExpressionSyntax? init = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
                init = ParseInitializerExpression(SyntaxKind.ObjectInitializerExpression);
            return new ImplicitObjectCreationExpressionSyntax(newKeyword, argList, init);
        }
        private ObjectCreationExpressionSyntax ParseObjectCreationExpressionAfterNew(SyntaxToken newKeyword, TypeSyntax type)
        {
            ArgumentListSyntax? argList = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                argList = ParseArgumentList();

            InitializerExpressionSyntax? init = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
                init = ParseInitializerExpression(SyntaxKind.ObjectInitializerExpression);

            if (argList == null && init == null)
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Object creation requires argument list '()' or initializer '{...}' (recovery continues)."));

            return new ObjectCreationExpressionSyntax(newKeyword, type, argList, init);
        }
        private ExpressionSyntax ParseArrayCreationExpression(SyntaxToken newKeyword, TypeSyntax elementType)
        {
            var ranks = new List<ArrayRankSpecifierSyntax>();
            while (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                ranks.Add(ParseArrayRankSpecifierWithSizes());

            if (ranks.Count == 0)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Array creation requires at least one rank specifier '[...]' (recovery continues)."));
                ranks.Add(CreateMissingArrayRankSpecifier(_tokens.Current.Span.Start));
            }

            var arrayType = new ArrayTypeSyntax(elementType, new SyntaxList<ArrayRankSpecifierSyntax>(ranks.ToArray()));

            InitializerExpressionSyntax? init = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
                init = ParseInitializerExpression(SyntaxKind.ArrayInitializerExpression);

            if (init == null && AllRankSpecifiersOmitted(ranks))
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Array creation with omitted sizes requires an initializer '{...}' (recovery continues)."));

            return new ArrayCreationExpressionSyntax(newKeyword, arrayType, init);
        }

        private ImplicitArrayCreationExpressionSyntax ParseImplicitArrayCreationExpression(SyntaxToken newKeyword)
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);

            var commas = new List<SyntaxToken>();
            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
                commas.Add(_tokens.EatToken());

            var close = MatchToken(SyntaxKind.CloseBracketToken);

            InitializerExpressionSyntax init;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                init = ParseInitializerExpression(SyntaxKind.ArrayInitializerExpression);
            }
            else
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Implicit array creation requires an initializer '{...}' (recovery continues)."));
                init = CreateMissingInitializerExpression(SyntaxKind.ArrayInitializerExpression, _tokens.Current.Span.Start);
            }

            return new ImplicitArrayCreationExpressionSyntax(newKeyword, open, new SyntaxTokenList(commas.ToArray()), close, init);
        }
        private ExpressionSyntax ParseStackAllocExpression()
        {
            var stackAllocKeyword = MatchToken(SyntaxKind.StackAllocKeyword);

            // Implicit stackalloc array creation
            if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                return ParseImplicitStackAllocArrayCreationExpression(stackAllocKeyword);

            var elementType = ParseTypeWithoutArrayCreationRankSpecifiers();
            var ranks = new List<ArrayRankSpecifierSyntax>();

            if (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
            {
                while (_tokens.CurrentKind == SyntaxKind.OpenBracketToken)
                    ranks.Add(ParseArrayRankSpecifierWithSizes());
            }
            else
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "stackalloc requires an array rank specifier '[...]' (recovery continues)."));
                ranks.Add(CreateMissingArrayRankSpecifier(_tokens.Current.Span.Start));
            }

            var arrayType = new ArrayTypeSyntax(elementType, new SyntaxList<ArrayRankSpecifierSyntax>(ranks.ToArray()));

            InitializerExpressionSyntax? init = null;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
                init = ParseInitializerExpression(SyntaxKind.ArrayInitializerExpression);

            return new StackAllocArrayCreationExpressionSyntax(stackAllocKeyword, arrayType, init);
        }
        private ImplicitStackAllocArrayCreationExpressionSyntax ParseImplicitStackAllocArrayCreationExpression(SyntaxToken stackAllocKeyword)
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);

            // stackalloc[] is always 1D; if commas exist, consume them as skipped tokens.
            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
                EatAsSkippedToken("Unexpected ',' in implicit stackalloc rank specifier.");

            var close = MatchToken(SyntaxKind.CloseBracketToken);

            InitializerExpressionSyntax init;
            if (_tokens.CurrentKind == SyntaxKind.OpenBraceToken)
            {
                init = ParseInitializerExpression(SyntaxKind.ArrayInitializerExpression);
            }
            else
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start,
                    "Implicit stackalloc array creation requires an initializer '{...}' (recovery continues)."));
                init = CreateMissingInitializerExpression(SyntaxKind.ArrayInitializerExpression, _tokens.Current.Span.Start);
            }

            return new ImplicitStackAllocArrayCreationExpressionSyntax(stackAllocKeyword, open, close, init);
        }

        private TypeSyntax ParseTypeWithoutArrayCreationRankSpecifiers()
        {
            using var __ = _ctx.Push(ParseContext.Type);

            TypeSyntax t = ParseTypeCore();

            while (true)
            {
                if (_tokens.CurrentKind == SyntaxKind.AsteriskToken)
                {
                    var star = _tokens.EatToken();
                    t = new PointerTypeSyntax(t, star);
                    continue;
                }

                if (_tokens.CurrentKind == SyntaxKind.QuestionToken)
                {
                    var q = _tokens.EatToken();
                    t = new NullableTypeSyntax(t, q);
                    continue;
                }

                break;
            }

            return t;
        }
        private ArrayRankSpecifierSyntax ParseArrayRankSpecifierWithSizes()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);
            var list = new List<SyntaxNodeOrToken>();

            // First size or omitted
            if (_tokens.CurrentKind == SyntaxKind.CloseBracketToken || _tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                list.Add(new SyntaxNodeOrToken(new OmittedArraySizeExpressionSyntax(
                    CreateMissingToken(SyntaxKind.OmittedArraySizeExpressionToken, open.Span.End))));
            }
            else
            {
                list.Add(new SyntaxNodeOrToken(ParseExpression()));
            }

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));

                if (_tokens.CurrentKind == SyntaxKind.CloseBracketToken || _tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(new OmittedArraySizeExpressionSyntax(
                        CreateMissingToken(SyntaxKind.OmittedArraySizeExpressionToken, comma.Span.End))));
                }
                else
                {
                    list.Add(new SyntaxNodeOrToken(ParseExpression()));
                }
            }

            var close = MatchToken(SyntaxKind.CloseBracketToken);
            return new ArrayRankSpecifierSyntax(open, new SeparatedSyntaxList<ExpressionSyntax>(list.ToArray()), close);
        }

        private ArrayRankSpecifierSyntax CreateMissingArrayRankSpecifier(int position)
        {
            var open = CreateMissingToken(SyntaxKind.OpenBracketToken, position);
            var omitted = new OmittedArraySizeExpressionSyntax(CreateMissingToken(SyntaxKind.OmittedArraySizeExpressionToken, position));
            var sizes = new SeparatedSyntaxList<ExpressionSyntax>(new SyntaxNodeOrToken[] { new SyntaxNodeOrToken(omitted) });
            var close = CreateMissingToken(SyntaxKind.CloseBracketToken, position);
            return new ArrayRankSpecifierSyntax(open, sizes, close);
        }
        private InitializerExpressionSyntax CreateMissingInitializerExpression(SyntaxKind kind, int position)
        {
            var open = CreateMissingToken(SyntaxKind.OpenBraceToken, position);
            var close = CreateMissingToken(SyntaxKind.CloseBraceToken, position);
            return new InitializerExpressionSyntax(kind, open, new SeparatedSyntaxList<ExpressionSyntax>(Array.Empty<SyntaxNodeOrToken>()), close);
        }
        private InitializerExpressionSyntax ParseInitializerExpression(SyntaxKind kind)
        {
            var open = MatchToken(SyntaxKind.OpenBraceToken);

            var list = new List<SyntaxNodeOrToken>();
            while (_tokens.CurrentKind != SyntaxKind.CloseBraceToken &&
           _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                list.Add(new SyntaxNodeOrToken(ParseExpression()));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    continue;
                }

                break;
            }

            var close = MatchToken(SyntaxKind.CloseBraceToken);
            return new InitializerExpressionSyntax(kind, open, new SeparatedSyntaxList<ExpressionSyntax>(list.ToArray()), close);
        }
        private MemberAccessExpressionSyntax ParseMemberAccess(ExpressionSyntax receiver)
        {
            var op = _tokens.EatToken(); // '.' or '->'
            var name = ParseSimpleNameInExpressionContext();

            var kind = op.Kind switch
            {
                SyntaxKind.DotToken => SyntaxKind.SimpleMemberAccessExpression,
                SyntaxKind.MinusGreaterThanToken => SyntaxKind.PointerMemberAccessExpression,
                _ => SyntaxKind.SimpleMemberAccessExpression
            };

            return new MemberAccessExpressionSyntax(kind, receiver, op, name);
        }
        private ArgumentListSyntax ParseArgumentList()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var args = ParseSeparatedArguments(closeKind: SyntaxKind.CloseParenToken, requireAtLeastOne: false);
            var close = MatchToken(SyntaxKind.CloseParenToken);
            return new ArgumentListSyntax(open, args, close);
        }
        private BracketedArgumentListSyntax ParseBracketedArgumentList()
        {
            var open = MatchToken(SyntaxKind.OpenBracketToken);
            var args = ParseSeparatedArguments(closeKind: SyntaxKind.CloseBracketToken, requireAtLeastOne: true);
            var close = MatchToken(SyntaxKind.CloseBracketToken);
            return new BracketedArgumentListSyntax(open, args, close);
        }

        private SeparatedSyntaxList<ArgumentSyntax> ParseSeparatedArguments(SyntaxKind closeKind, bool requireAtLeastOne)
        {
            var list = new List<SyntaxNodeOrToken>();

            if (requireAtLeastOne && _tokens.CurrentKind == closeKind)
            {
                _diagnostics.Add(new SyntaxDiagnostic(_tokens.Current.Span.Start, "Expected expression in bracketed argument list."));
                var missingExpr = CreateMissingIdentifierName(_tokens.Current.Span.Start);
                list.Add(new SyntaxNodeOrToken(new ArgumentSyntax(null, null, missingExpr)));
                return new SeparatedSyntaxList<ArgumentSyntax>(list.ToArray());
            }

            while (_tokens.CurrentKind != closeKind &&
                   _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
            {
                int start = _tokens.Position;
                var arg = ParseArgument();
                list.Add(new SyntaxNodeOrToken(arg));

                if (_tokens.CurrentKind == SyntaxKind.CommaToken)
                {
                    list.Add(new SyntaxNodeOrToken(_tokens.EatToken()));
                    continue;
                }
                if (_tokens.CurrentKind != closeKind &&
                    _tokens.CurrentKind != SyntaxKind.EndOfFileToken)
                {
                    EatAsSkippedToken("Expected ',' or closing token in argument list.");
                    continue;
                }

                if (_tokens.Position == start)
                {
                    EatAsSkippedToken("Parser made no progress in argument parsing.");
                    continue;
                }
                break;
            }

            return new SeparatedSyntaxList<ArgumentSyntax>(list.ToArray());
        }
        private ArgumentSyntax ParseArgument()
        {
            NameColonSyntax? nameColon = null;

            // Named argument
            if (_tokens.CurrentKind == SyntaxKind.IdentifierToken &&
                _tokens.Peek(1).Kind == SyntaxKind.ColonToken)
            {
                var id = new IdentifierNameSyntax(_tokens.EatToken());
                var colon = _tokens.EatToken();
                nameColon = new NameColonSyntax(id, colon);
            }

            // Argument modifier ref/out/in
            SyntaxToken? refKindKeyword = null;
            if (_tokens.CurrentKind == SyntaxKind.RefKeyword ||
                _tokens.CurrentKind == SyntaxKind.InKeyword ||
                _tokens.CurrentKind == SyntaxKind.OutKeyword)
            {
                refKindKeyword = _tokens.EatToken();
            }

            ExpressionSyntax expr;

            if (refKindKeyword is { Kind: SyntaxKind.OutKeyword } && IsOutDeclarationExpressionStart())
            {
                expr = ParseDeclarationExpression();
            }
            else
            {
                expr = ParseExpression();
            }

            return new ArgumentSyntax(nameColon, refKindKeyword, expr);
        }
        private DeclarationExpressionSyntax ParseDeclarationExpression()
        {
            var type = ParseType();
            var designation = ParseVariableDesignation();
            return new DeclarationExpressionSyntax(type, designation);

        }
        private VariableDesignationSyntax ParseVariableDesignation()
        {
            if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                return ParseParenthesizedVariableDesignation();
            var id = MatchToken(SyntaxKind.IdentifierToken);
            if (id.Kind == SyntaxKind.IdentifierToken &&
                string.Equals(id.ValueText, "_", StringComparison.Ordinal))
            {
                return new DiscardDesignationSyntax(id);
            }
            return new SingleVariableDesignationSyntax(id);
        }
        private ParenthesizedVariableDesignationSyntax ParseParenthesizedVariableDesignation()
        {
            var open = MatchToken(SyntaxKind.OpenParenToken);
            var list = new List<SyntaxNodeOrToken>();

            if (_tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                _tokens.CurrentKind == SyntaxKind.EndOfFileToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(open.Span.End, "Expected variable designation."));
                var missing = CreateMissingToken(SyntaxKind.IdentifierToken, open.Span.End);
                list.Add(new SyntaxNodeOrToken(new SingleVariableDesignationSyntax(missing)));
                var close0 = MatchToken(SyntaxKind.CloseParenToken);
                return new ParenthesizedVariableDesignationSyntax(open, new SeparatedSyntaxList<VariableDesignationSyntax>(list.ToArray()), close0);
            }

            list.Add(new SyntaxNodeOrToken(ParseVariableDesignation()));

            while (_tokens.CurrentKind == SyntaxKind.CommaToken)
            {
                var comma = _tokens.EatToken();
                list.Add(new SyntaxNodeOrToken(comma));

                if (_tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                    _tokens.CurrentKind == SyntaxKind.EndOfFileToken)
                {
                    _diagnostics.Add(new SyntaxDiagnostic(comma.Span.End, "Expected variable designation after ','."));
                    var missing = CreateMissingToken(SyntaxKind.IdentifierToken, comma.Span.End);
                    list.Add(new SyntaxNodeOrToken(new SingleVariableDesignationSyntax(missing)));
                    break;
                }

                list.Add(new SyntaxNodeOrToken(ParseVariableDesignation()));
            }

            var close = MatchToken(SyntaxKind.CloseParenToken);
            return new ParenthesizedVariableDesignationSyntax(open, new SeparatedSyntaxList<VariableDesignationSyntax>(list.ToArray()), close);
        }
        // =patterns=
        private PatternSyntax ParsePatternCore()
        {
            if (_tokens.Current.Kind == SyntaxKind.IdentifierToken &&
                string.Equals(_tokens.Current.ValueText, "_", StringComparison.Ordinal))
            {
                var underscore = _tokens.EatToken();
                return new DiscardPatternSyntax(underscore);
            }

            // constant pattern
            var expr = ParseExpression();
            return new ConstantPatternSyntax(expr);
        }
        private WhenClauseSyntax ParseWhenClause()
        {
            var whenKeyword = MatchToken(SyntaxKind.WhenKeyword);

            ExpressionSyntax condition;
            if (_tokens.CurrentKind == SyntaxKind.EqualsGreaterThanToken ||
                _tokens.CurrentKind == SyntaxKind.ColonToken)
            {
                _diagnostics.Add(new SyntaxDiagnostic(whenKeyword.Span.End, "Expected expression after 'when'."));
                condition = CreateMissingIdentifierName(whenKeyword.Span.End);
            }
            else
            {
                condition = ParseExpression();
            }

            return new WhenClauseSyntax(whenKeyword, condition);
        }

        // heuristics
        private bool IsLambdaExpressionStart()
        {
            return Probe(scan: ScanLambdaExpressionStart, tempContext: ParseContext.None, requireProgress: true);
        }
        private bool IsAnonymousMethodExpressionStart()
        {
            if (_tokens.CurrentKind == SyntaxKind.DelegateKeyword)
                return true;

            if (IsCurrentContextual(SyntaxKind.AsyncKeyword) && _tokens.Peek(1).Kind == SyntaxKind.DelegateKeyword)
                return true;

            return false;
        }
        private bool ScanLambdaExpressionStart()
        {
            // optional static
            if (_tokens.Current.Kind == SyntaxKind.StaticKeyword)
                _tokens.EatToken();

            // optional async
            if (IsCurrentContextual(SyntaxKind.AsyncKeyword) && _tokens.Peek(1).Kind != SyntaxKind.EqualsGreaterThanToken)
                _tokens.EatToken();

            // x =>
            if (_tokens.Current.Kind == SyntaxKind.IdentifierToken &&
                _tokens.Peek(1).Kind == SyntaxKind.EqualsGreaterThanToken)
                return true;

            // ( ... ) =>
            if (_tokens.Current.Kind != SyntaxKind.OpenParenToken)
                return false;

            _tokens.EatToken(); // '('

            if (_tokens.Current.Kind == SyntaxKind.CloseParenToken)
            {
                _tokens.EatToken(); // ')'
                return _tokens.Current.Kind == SyntaxKind.EqualsGreaterThanToken;
            }

            if (!ScanLambdaParameter())
                return false;

            while (_tokens.Current.Kind == SyntaxKind.CommaToken)
            {
                _tokens.EatToken();
                if (!ScanLambdaParameter())
                    return false;
            }

            if (_tokens.Current.Kind != SyntaxKind.CloseParenToken)
                return false;

            _tokens.EatToken(); // ')'
            return _tokens.Current.Kind == SyntaxKind.EqualsGreaterThanToken;
        }
        private bool ScanLambdaParameter()
        {
            // modifiers
            while (IsModifierToken(_tokens.Current, ModifierContext.Parameter))
                _tokens.EatToken();

            // Try typed
            var mark = _tokens.MarkState();

            if (ScanType() && _tokens.Current.Kind == SyntaxKind.IdentifierToken)
            {
                _tokens.EatToken();
                return true;
            }

            _tokens.Reset(mark);

            // Untyped
            if (_tokens.Current.Kind != SyntaxKind.IdentifierToken)
                return false;

            _tokens.EatToken();
            return true;
        }
        private bool IsOutDeclarationExpressionStart()
        {
            return Probe(
            scan: () =>
            {
                if (!ScanType())
                    return false;

                if (!ScanVariableDesignation())
                    return false;

                return _tokens.CurrentKind == SyntaxKind.CommaToken ||
                    _tokens.CurrentKind == SyntaxKind.CloseParenToken ||
                    _tokens.CurrentKind == SyntaxKind.CloseBracketToken;
            },
            tempContext: ParseContext.Type,
            requireProgress: true);
        }
        private bool ScanVariableDesignation()
        {
            if (_tokens.Current.Kind == SyntaxKind.OpenParenToken)
                return ScanParenthesizedVariableDesignation();
            if (_tokens.Current.Kind != SyntaxKind.IdentifierToken)
                return false;
            _tokens.EatToken();
            return true;
        }
        private bool ScanParenthesizedVariableDesignation()
        {
            if (_tokens.Current.Kind != SyntaxKind.OpenParenToken)
                return false;
            _tokens.EatToken();
            if (!ScanVariableDesignation())
                return false;
            while (_tokens.Current.Kind == SyntaxKind.CommaToken)
            {
                _tokens.EatToken();
                if (!ScanVariableDesignation())
                    return false;
            }
            if (_tokens.Current.Kind != SyntaxKind.CloseParenToken)
                return false;
            _tokens.EatToken();
            return true;
        }
        private bool IsDeclarationExpressionStart()
        {
            return Probe(
                scan: () =>
                {
                    var start = _tokens.Current;

                    if (!ScanTypeNoPointerSuffix())
                        return false;

                    if (_tokens.CurrentKind == SyntaxKind.OpenParenToken)
                    {
                        if (!(start.Kind == SyntaxKind.IdentifierToken && start.ContextualKind == SyntaxKind.VarKeyword) &&
                            start.Kind != SyntaxKind.RefKeyword)
                        {
                            return false;
                        }

                        if (!ScanParenthesizedVariableDesignation())
                            return false;
                    }
                    else
                    {
                        if (_tokens.CurrentKind != SyntaxKind.IdentifierToken)
                            return false;

                        _tokens.EatToken(); // single designation identifier
                    }

                    var k = _tokens.CurrentKind;
                    return k == SyntaxKind.CommaToken
                        || k == SyntaxKind.CloseParenToken
                        || k == SyntaxKind.CloseBracketToken
                        || k == SyntaxKind.EqualsToken
                        || k == SyntaxKind.InKeyword
                        || k == SyntaxKind.SemicolonToken;
                },
                tempContext: ParseContext.Type,
                requireProgress: true);
        }
        private bool ScanTypeNoPointerSuffix()
        {
            if (_tokens.Current.Kind == SyntaxKind.RefKeyword)
            {
                _tokens.EatToken(); // ref
                if (_tokens.Current.Kind == SyntaxKind.ReadOnlyKeyword)
                    _tokens.EatToken(); // readonly
                return ScanTypeNoPointerSuffix();
            }

            if (_tokens.Current.Kind == SyntaxKind.OpenParenToken)
            {
                if (!ScanTupleTypeCore())
                    return false;
            }
            else if (IsPredefinedTypeKeyword(_tokens.Current.Kind)
                || (_tokens.Current.Kind == SyntaxKind.IdentifierToken
                    && (_tokens.Current.ValueText == "nint" || _tokens.Current.ValueText == "nuint")))
            {
                _tokens.EatToken();
            }
            else
            {
                if (!ScanName())
                    return false;
            }

            while (true)
            {
                if (_tokens.Current.Kind == SyntaxKind.QuestionToken)
                {
                    _tokens.EatToken();
                    continue;
                }

                if (_tokens.Current.Kind == SyntaxKind.OpenBracketToken)
                {
                    do
                    {
                        _tokens.EatToken(); // '['
                        while (_tokens.Current.Kind == SyntaxKind.CommaToken)
                            _tokens.EatToken();
                        if (_tokens.Current.Kind != SyntaxKind.CloseBracketToken)
                            return false;
                        _tokens.EatToken(); // ']'
                    }
                    while (_tokens.Current.Kind == SyntaxKind.OpenBracketToken);

                    continue;
                }

                break;
            }

            return true;
        }
        private static bool IsStartOfUnaryExpression(SyntaxToken t)
        {
            if (t.Kind == SyntaxKind.EndOfFileToken)
                return false;

            if (t.Kind == SyntaxKind.ThrowKeyword)
                return true;

            if (t.Kind == SyntaxKind.TypeOfKeyword || t.Kind == SyntaxKind.SizeOfKeyword)
                return true;

            if (t.Kind == SyntaxKind.InterpolatedStringStartToken ||
                t.Kind == SyntaxKind.InterpolatedVerbatimStringStartToken ||
                t.Kind == SyntaxKind.InterpolatedSingleLineRawStringStartToken ||
                t.Kind == SyntaxKind.InterpolatedMultiLineRawStringStartToken)
                return true;

            if (t.Kind == SyntaxKind.NewKeyword || t.Kind == SyntaxKind.StackAllocKeyword)
                return true;

            if (t.Kind == SyntaxKind.OpenParenToken)
                return true;

            if (t.Kind == SyntaxKind.DotDotToken)
                return true;

            if (t.Kind == SyntaxKind.IdentifierToken)
                return true;

            if (t.Kind == SyntaxKind.ThisKeyword || t.Kind == SyntaxKind.BaseKeyword)
                return true;

            if (t.Kind == SyntaxKind.NumericLiteralToken ||
                t.Kind == SyntaxKind.StringLiteralToken ||
                t.Kind == SyntaxKind.Utf8StringLiteralToken ||
                t.Kind == SyntaxKind.CharacterLiteralToken ||
                t.Kind == SyntaxKind.TrueKeyword ||
                t.Kind == SyntaxKind.FalseKeyword ||
                t.Kind == SyntaxKind.NullKeyword)
                return true;

            if (t.Kind == SyntaxKind.IdentifierToken && t.ContextualKind == SyntaxKind.AwaitKeyword)
                return true;

            return SyntaxFacts.GetUnaryOperatorPrecedence(t.Kind) != 0;
        }
        private bool IsConstructorDeclarationStart(SyntaxToken classNameToken)
        {
            var t0 = _tokens.Current;
            if (t0.Kind != SyntaxKind.IdentifierToken)
                return false;

            var name0 = t0.ValueText;
            var className = classNameToken.ValueText;

            if (!string.Equals(name0, className, StringComparison.Ordinal))
                return false;

            return _tokens.Peek(1).Kind == SyntaxKind.OpenParenToken;
        }

        private static bool ContainsGenericName(SyntaxNode node)
        {
            if (node is GenericNameSyntax) return true;

            return node switch
            {
                QualifiedNameSyntax q => ContainsGenericName(q.Left) || ContainsGenericName(q.Right),
                NullableTypeSyntax n => ContainsGenericName(n.ElementType),
                ArrayTypeSyntax a => ContainsGenericName(a.ElementType),
                PointerTypeSyntax p => ContainsGenericName(p.ElementType),
                RefTypeSyntax r => ContainsGenericName(r.Type),
                TupleTypeSyntax t => ContainsGenericNameInTupleType(t),
                _ => false
            };
        }
        private static bool ContainsGenericNameInTupleType(TupleTypeSyntax t)
        {
            for (int i = 0; i < t.Elements.Count; i++)
            {
                if (ContainsGenericName(t.Elements[i].Type))
                    return true;
            }
            return false;
        }
        private static bool TypeDefinitelyNotExpression(TypeSyntax t)
        {
            if (t is PredefinedTypeSyntax) return true;
            if (t is NullableTypeSyntax) return true;
            if (t is ArrayTypeSyntax) return true;
            if (t is RefTypeSyntax) return true;
            if (t is PointerTypeSyntax) return true;

            if (ContainsGenericName(t)) return true;

            return false;
        }
        private static bool IsCastFollowerToken(SyntaxToken t)
        {
            var k = t.Kind;

            if (k == SyntaxKind.TildeToken || k == SyntaxKind.ExclamationToken || k == SyntaxKind.OpenParenToken)
                return true;

            if (k == SyntaxKind.IdentifierToken)
                return true;

            if (IsLiteralToken(k))
                return true;

            if (t.IsKeyword() && k != SyntaxKind.AsKeyword && k != SyntaxKind.IsKeyword)
                return true;

            return false;
        }
        private static bool AllRankSpecifiersOmitted(List<ArrayRankSpecifierSyntax> ranks)
        {
            for (int i = 0; i < ranks.Count; i++)
            {
                var sizes = ranks[i].Sizes;
                for (int j = 0; j < sizes.Count; j++)
                {
                    if (sizes[j] is not OmittedArraySizeExpressionSyntax)
                        return false;
                }
            }

            return true;
        }
        // tables
        private static bool IsExpressionTerminator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.SemicolonToken => true,
            SyntaxKind.CommaToken => true,
            SyntaxKind.CloseParenToken => true,
            SyntaxKind.CloseBracketToken => true,
            SyntaxKind.CloseBraceToken => true,
            SyntaxKind.ColonToken => true,
            _ => false
        };
        private static bool IsOverloadableOperatorToken(SyntaxKind kind) => kind switch
        {
            SyntaxKind.PlusToken => true,
            SyntaxKind.MinusToken => true,
            SyntaxKind.ExclamationToken => true,
            SyntaxKind.TildeToken => true,
            SyntaxKind.PlusPlusToken => true,
            SyntaxKind.MinusMinusToken => true,
            SyntaxKind.TrueKeyword => true,
            SyntaxKind.FalseKeyword => true,
            SyntaxKind.AsteriskToken => true,
            SyntaxKind.SlashToken => true,
            SyntaxKind.PercentToken => true,
            SyntaxKind.AmpersandToken => true,
            SyntaxKind.BarToken => true,
            SyntaxKind.CaretToken => true,
            SyntaxKind.LessThanLessThanToken => true,
            SyntaxKind.GreaterThanGreaterThanToken => true,
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => true,
            SyntaxKind.EqualsEqualsToken => true,
            SyntaxKind.ExclamationEqualsToken => true,
            SyntaxKind.LessThanToken => true,
            SyntaxKind.GreaterThanToken => true,
            SyntaxKind.LessThanEqualsToken => true,
            SyntaxKind.GreaterThanEqualsToken => true,

            SyntaxKind.PlusEqualsToken => true,
            SyntaxKind.MinusEqualsToken => true,
            SyntaxKind.AsteriskEqualsToken => true,
            SyntaxKind.SlashEqualsToken => true,
            SyntaxKind.PercentEqualsToken => true,
            SyntaxKind.AmpersandEqualsToken => true,
            SyntaxKind.BarEqualsToken => true,
            SyntaxKind.CaretEqualsToken => true,
            SyntaxKind.LessThanLessThanEqualsToken => true,
            SyntaxKind.GreaterThanGreaterThanEqualsToken => true,
            SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken => true,

            _ => false
        };
        private static bool IsAssignmentOperator(SyntaxKind kind) => kind switch
        {
            SyntaxKind.EqualsToken => true,
            SyntaxKind.PlusEqualsToken => true,
            SyntaxKind.MinusEqualsToken => true,
            SyntaxKind.AsteriskEqualsToken => true,
            SyntaxKind.SlashEqualsToken => true,
            SyntaxKind.PercentEqualsToken => true,
            SyntaxKind.AmpersandEqualsToken => true,
            SyntaxKind.BarEqualsToken => true,
            SyntaxKind.CaretEqualsToken => true,
            SyntaxKind.LessThanLessThanEqualsToken => true,
            SyntaxKind.GreaterThanGreaterThanEqualsToken => true,
            SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken => true,
            SyntaxKind.QuestionQuestionEqualsToken => true,
            _ => false
        };
        private static bool IsPredefinedTypeKeyword(SyntaxKind kind) => kind switch
        {
            SyntaxKind.BoolKeyword => true,
            SyntaxKind.ByteKeyword => true,
            SyntaxKind.SByteKeyword => true,
            SyntaxKind.ShortKeyword => true,
            SyntaxKind.UShortKeyword => true,
            SyntaxKind.IntKeyword => true,
            SyntaxKind.UIntKeyword => true,
            SyntaxKind.LongKeyword => true,
            SyntaxKind.ULongKeyword => true,
            SyntaxKind.CharKeyword => true,
            SyntaxKind.FloatKeyword => true,
            SyntaxKind.DoubleKeyword => true,
            SyntaxKind.DecimalKeyword => true,
            SyntaxKind.StringKeyword => true,
            SyntaxKind.ObjectKeyword => true,
            SyntaxKind.VoidKeyword => true,
            _ => false
        };
        private static bool IsModifierToken(SyntaxToken t, ModifierContext ctx)
        {
            // Contextual modifiers
            if (t.Kind == SyntaxKind.IdentifierToken)
            {
                return t.ContextualKind switch
                {
                    SyntaxKind.PartialKeyword => ctx == ModifierContext.Type || ctx == ModifierContext.Member,
                    SyntaxKind.AsyncKeyword => ctx == ModifierContext.Member || ctx == ModifierContext.LocalFunction,
                    SyntaxKind.ScopedKeyword => ctx == ModifierContext.Parameter,
                    _ => false
                };
            }
            // Reserved keywords
            return ctx switch
            {
                ModifierContext.Type => t.Kind switch
                {
                    SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword
                    or SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword or SyntaxKind.StaticKeyword
                    or SyntaxKind.UnsafeKeyword
                    or SyntaxKind.ReadOnlyKeyword // readonly struct
                    or SyntaxKind.RefKeyword      // ref struct
                    or SyntaxKind.NewKeyword      // nested type
                        => true,
                    _ => false
                },

                ModifierContext.Member => t.Kind switch
                {
                    SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword
                    or SyntaxKind.StaticKeyword or SyntaxKind.ReadOnlyKeyword or SyntaxKind.VolatileKeyword or SyntaxKind.ConstKeyword
                    or SyntaxKind.AbstractKeyword or SyntaxKind.SealedKeyword or SyntaxKind.VirtualKeyword or SyntaxKind.OverrideKeyword
                    or SyntaxKind.ExternKeyword or SyntaxKind.UnsafeKeyword
                    or SyntaxKind.NewKeyword
                        => true,
                    _ => false
                },

                ModifierContext.Local => t.Kind switch
                {
                    SyntaxKind.ConstKeyword
                    or SyntaxKind.RefKeyword
                    or SyntaxKind.ReadOnlyKeyword // ref readonly
                        => true,
                    _ => false
                },

                ModifierContext.LocalFunction => t.Kind switch
                {
                    SyntaxKind.StaticKeyword
                    or SyntaxKind.UnsafeKeyword
                        => true,
                    _ => false
                },

                ModifierContext.Parameter => t.Kind switch
                {
                    SyntaxKind.RefKeyword or SyntaxKind.ReadOnlyKeyword
                    or SyntaxKind.OutKeyword or SyntaxKind.InKeyword
                    or SyntaxKind.ParamsKeyword or SyntaxKind.ThisKeyword
                    or SyntaxKind.ScopedKeyword
                        => true,
                    _ => false
                },

                ModifierContext.Accessor => t.Kind switch
                {
                    SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword
                    or SyntaxKind.InterfaceKeyword or SyntaxKind.ReadOnlyKeyword
                    => true,
                    _ => false
                },


                _ => false
            };
        }
        private static bool IsLiteralToken(SyntaxKind kind) => kind switch
        {
            SyntaxKind.NumericLiteralToken => true,
            SyntaxKind.StringLiteralToken => true,
            SyntaxKind.Utf8StringLiteralToken => true,
            SyntaxKind.CharacterLiteralToken => true,
            _ => false
        };
        private static bool IsTypeDeclarationKeyword(SyntaxKind kind) => kind switch
        {
            SyntaxKind.ClassKeyword => true,
            SyntaxKind.StructKeyword => true,
            SyntaxKind.InterfaceKeyword => true,
            SyntaxKind.EnumKeyword => true,
            _ => false
        };

        private static SyntaxKind GetAssignmentExpressionKind(SyntaxKind tokenKind) => tokenKind switch
        {
            SyntaxKind.EqualsToken => SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.PlusEqualsToken => SyntaxKind.AddAssignmentExpression,
            SyntaxKind.MinusEqualsToken => SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.AsteriskEqualsToken => SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.SlashEqualsToken => SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.PercentEqualsToken => SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AmpersandEqualsToken => SyntaxKind.AndAssignmentExpression,
            SyntaxKind.BarEqualsToken => SyntaxKind.OrAssignmentExpression,
            SyntaxKind.CaretEqualsToken => SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.LessThanLessThanEqualsToken => SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.GreaterThanGreaterThanEqualsToken => SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken => SyntaxKind.UnsignedRightShiftAssignmentExpression,
            SyntaxKind.QuestionQuestionEqualsToken => SyntaxKind.CoalesceAssignmentExpression,
            _ => SyntaxKind.SimpleAssignmentExpression
        };

        private static SyntaxKind GetBinaryExpressionKind(SyntaxKind tokenKind) => tokenKind switch
        {
            SyntaxKind.AsteriskToken => SyntaxKind.MultiplyExpression,
            SyntaxKind.SlashToken => SyntaxKind.DivideExpression,
            SyntaxKind.PercentToken => SyntaxKind.ModuloExpression,

            SyntaxKind.PlusToken => SyntaxKind.AddExpression,
            SyntaxKind.MinusToken => SyntaxKind.SubtractExpression,

            SyntaxKind.LessThanLessThanToken => SyntaxKind.LeftShiftExpression,
            SyntaxKind.GreaterThanGreaterThanToken => SyntaxKind.RightShiftExpression,
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => SyntaxKind.UnsignedRightShiftExpression,

            SyntaxKind.LessThanToken => SyntaxKind.LessThanExpression,
            SyntaxKind.LessThanEqualsToken => SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanToken => SyntaxKind.GreaterThanExpression,
            SyntaxKind.GreaterThanEqualsToken => SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.IsKeyword => SyntaxKind.IsExpression,
            SyntaxKind.AsKeyword => SyntaxKind.AsExpression,

            SyntaxKind.EqualsEqualsToken => SyntaxKind.EqualsExpression,
            SyntaxKind.ExclamationEqualsToken => SyntaxKind.NotEqualsExpression,

            SyntaxKind.AmpersandToken => SyntaxKind.BitwiseAndExpression,
            SyntaxKind.CaretToken => SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.BarToken => SyntaxKind.BitwiseOrExpression,

            SyntaxKind.AmpersandAmpersandToken => SyntaxKind.LogicalAndExpression,
            SyntaxKind.BarBarToken => SyntaxKind.LogicalOrExpression,

            _ => SyntaxKind.AddExpression
        };

        private static SyntaxKind GetPrefixUnaryExpressionKind(SyntaxKind tokenKind) => tokenKind switch
        {
            SyntaxKind.PlusToken => SyntaxKind.UnaryPlusExpression,
            SyntaxKind.MinusToken => SyntaxKind.UnaryMinusExpression,
            SyntaxKind.ExclamationToken => SyntaxKind.LogicalNotExpression,
            SyntaxKind.TildeToken => SyntaxKind.BitwiseNotExpression,
            SyntaxKind.PlusPlusToken => SyntaxKind.PreIncrementExpression,
            SyntaxKind.MinusMinusToken => SyntaxKind.PreDecrementExpression,
            SyntaxKind.CaretToken => SyntaxKind.IndexExpression,
            SyntaxKind.AmpersandToken => SyntaxKind.AddressOfExpression,
            SyntaxKind.AsteriskToken => SyntaxKind.PointerIndirectionExpression,
            SyntaxKind.RefKeyword => SyntaxKind.RefExpression,
            _ => SyntaxKind.UnaryPlusExpression
        };

        private static SyntaxKind GetPostfixUnaryExpressionKind(SyntaxKind tokenKind) => tokenKind switch
        {
            SyntaxKind.PlusPlusToken => SyntaxKind.PostIncrementExpression,
            SyntaxKind.MinusMinusToken => SyntaxKind.PostDecrementExpression,
            SyntaxKind.ExclamationToken => SyntaxKind.SuppressNullableWarningExpression,
            _ => SyntaxKind.PostIncrementExpression
        };

    }
}
