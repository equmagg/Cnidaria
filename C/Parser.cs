using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C
{
    public sealed class Parser
    {
        private readonly Lexer _lexer;
        private readonly TypeNameTable _typeNames;
        private readonly List<SyntaxToken> _buffer = new();
        private readonly List<SyntaxDiagnostic> _diagnostics = new();
        private int _position;

        private Parser(Lexer lexer, TypeNameTable typeNames)
        {
            _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
            _typeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
        }

        public IReadOnlyList<SyntaxDiagnostic> Diagnostics => _diagnostics;
        private SyntaxToken Current => Peek(0);
        public static ParseResult Parse(
            string text,
            PreprocessorOptions? options = null)
        {
            TypeNameTable typeNames = new TypeNameTable();

            var lexer = new Lexer(
                text,
                typeNames,
                options ?? PreprocessorOptions.CreateDefault());

            var parser = new Parser(lexer, typeNames);
            var root = parser.ParseTranslationUnit();

            var diagnostics = lexer.Diagnostics
                .Concat(parser.Diagnostics)
                .ToImmutableArray();

            return new ParseResult(root, diagnostics, typeNames);
        }

        private TranslationUnitSyntax ParseTranslationUnit()
        {
            var members = ImmutableArray.CreateBuilder<SyntaxNode>();

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _position;

                members.Add(ParseExternalDeclaration());

                if (_position == start)
                    members.Add(ParseSkippedExternalDeclaration());
            }

            var eof = MatchToken(SyntaxKind.EndOfFileToken);
            return new TranslationUnitSyntax(members.ToImmutable(), eof);
        }

        private SyntaxNode ParseExternalDeclaration()
        {
            if (IsStaticAssertDeclarationStart(Current.Kind))
                return ParseStaticAssertDeclaration();

            if (!IsDeclarationSpecifierStart(Current.Kind))
            {
                Report(Current, $"Expected external declaration, got '{Current.Kind}'.");
                return ParseSkippedExternalDeclaration();
            }

            return ParseDeclarationOrFunctionDefinition(allowFunctionDefinition: true);
        }

        private StaticAssertDeclarationSyntax ParseStaticAssertDeclaration()
        {
            var keyword = NextToken();
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseAssignmentExpression();

            SyntaxToken? comma = null;
            ExpressionSyntax? message = null;
            if (Current.Kind == SyntaxKind.CommaToken)
            {
                comma = NextToken();

                if (Current.Kind != SyntaxKind.CloseParenToken &&
                    Current.Kind != SyntaxKind.SemicolonToken &&
                    Current.Kind != SyntaxKind.EndOfFileToken)
                {
                    message = ParseAssignmentExpression();
                }
                else
                {
                    Report(Current, "Expected static assertion message.");
                    message = new InvalidExpressionSyntax(MissingTokenAt(Current.Position));
                }
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var semicolon = MatchToken(SyntaxKind.SemicolonToken);

            return new StaticAssertDeclarationSyntax(
                keyword,
                openParen,
                condition,
                comma,
                message,
                closeParen,
                semicolon);
        }

        private SyntaxNode ParseDeclarationOrFunctionDefinition(bool allowFunctionDefinition)
        {
            var specifiers = ParseDeclarationSpecifiers();

            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                var semicolon = MatchToken(SyntaxKind.SemicolonToken);
                return new DeclarationSyntax(
                    specifiers,
                    ImmutableArray<InitDeclaratorSyntax>.Empty,
                    semicolon);
            }

            var firstDeclarator = ParseDeclarator();
            var isTypedef = specifiers.Any(static token => token.Kind == SyntaxKind.TypedefKeyword);

            if (allowFunctionDefinition && Current.Kind == SyntaxKind.OpenBraceToken)
            {
                DeclareDeclaratorName(firstDeclarator, isTypedefName: false);
                var body = ParseCompoundStatement();
                return new FunctionDefinitionSyntax(specifiers, firstDeclarator, body);
            }

            var declarators = ImmutableArray.CreateBuilder<InitDeclaratorSyntax>();
            declarators.Add(ParseInitDeclaratorContinuation(firstDeclarator, isTypedef));

            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                var declarator = ParseDeclarator();
                declarators.Add(ParseInitDeclaratorContinuation(declarator, isTypedef));
            }

            var semicolonToken = MatchToken(SyntaxKind.SemicolonToken);

            var declaration = new DeclarationSyntax(
                specifiers,
                declarators.ToImmutable(),
                semicolonToken);

            return declaration;
        }

        private DeclarationSyntax ParseBlockDeclaration()
        {
            var node = ParseDeclarationOrFunctionDefinition(allowFunctionDefinition: false);

            if (node is DeclarationSyntax declaration)
                return declaration;

            throw new InvalidOperationException("Block declaration parser produced a non-declaration node.");
        }

        private ImmutableArray<SyntaxToken> ParseDeclarationSpecifiers()
        {
            var specifiers = ImmutableArray.CreateBuilder<SyntaxToken>();

            while (IsDeclarationSpecifierStart(Current.Kind))
            {
                if (Current.Kind is SyntaxKind.StructKeyword or SyntaxKind.UnionKeyword or SyntaxKind.EnumKeyword)
                {
                    ParseTagSpecifier(specifiers);
                    continue;
                }

                if (IsParenthesizedDeclarationSpecifier(Current.Kind))
                {
                    specifiers.Add(NextToken());

                    if (Current.Kind == SyntaxKind.OpenParenToken)
                        ReadBalancedTokenSequence(specifiers);

                    continue;
                }

                specifiers.Add(NextToken());
            }

            return specifiers.ToImmutable();
        }

        private void ParseTagSpecifier(ImmutableArray<SyntaxToken>.Builder tokens)
        {
            tokens.Add(NextToken());

            if (Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                tokens.Add(NextToken());

            if (Current.Kind == SyntaxKind.OpenBraceToken)
                ReadBalancedTokenSequence(tokens);
        }

        private InitDeclaratorSyntax ParseInitDeclaratorContinuation(
            DeclaratorSyntax declarator,
            bool isTypedefName)
        {
            DeclareDeclaratorName(declarator, isTypedefName);

            SyntaxToken? equalsToken = null;
            InitializerSyntax? initializer = null;

            if (Current.Kind == SyntaxKind.EqualsToken)
            {
                equalsToken = NextToken();
                initializer = ParseInitializer();
            }

            return new InitDeclaratorSyntax(
                declarator,
                equalsToken,
                initializer);
        }

        private DeclaratorSyntax ParseDeclarator()
        {
            var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
            SyntaxToken? identifier = null;

            while (Current.Kind == SyntaxKind.StarToken)
            {
                tokens.Add(NextToken());

                while (IsTypeQualifier(Current.Kind))
                    tokens.Add(NextToken());
            }

            ParseDirectDeclarator(tokens, ref identifier);

            while (Current.Kind is SyntaxKind.OpenBracketToken or SyntaxKind.OpenParenToken)
                ReadBalancedTokenSequence(tokens);

            return new DeclaratorSyntax(tokens.ToImmutable(), identifier);
        }

        private void ParseDirectDeclarator(
            ImmutableArray<SyntaxToken>.Builder tokens,
            ref SyntaxToken? identifier)
        {
            if (Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
            {
                var token = NextToken();
                tokens.Add(token);
                identifier = token;
                return;
            }

            if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                tokens.Add(NextToken());

                var inner = ParseDeclarator();
                tokens.AddRange(inner.Tokens);

                if (identifier is null)
                    identifier = inner.Identifier;

                tokens.Add(MatchToken(SyntaxKind.CloseParenToken));
                return;
            }

            Report(Current, $"Expected declarator, got '{Current.Kind}'.");

            if (!IsDeclaratorRecoveryPoint(Current.Kind))
                tokens.Add(NextToken());
            else
                tokens.Add(MissingTokenAt(Current.Position));
        }

        private InitializerSyntax ParseInitializer()
        {
            if (Current.Kind == SyntaxKind.OpenBraceToken)
                return ParseInitializerList();

            return new ExpressionInitializerSyntax(ParseAssignmentExpression());
        }

        private InitializerListSyntax ParseInitializerList()
        {
            var openBrace = MatchToken(SyntaxKind.OpenBraceToken);
            var items = ImmutableArray.CreateBuilder<InitializerListItemSyntax>();

            while (Current.Kind != SyntaxKind.CloseBraceToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _position;
                var item = ParseInitializerListItem();
                items.Add(item);

                if (item.CommaToken.HasValue)
                    continue;

                if (Current.Kind == SyntaxKind.CloseBraceToken ||
                    Current.Kind == SyntaxKind.EndOfFileToken)
                    break;

                Report(Current, $"Expected ',' or '}}' in initializer list, got '{Current.Kind}'.");
                SkipInitializerListRecovery();

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    continue;
                }

                if (_position == start && Current.Kind != SyntaxKind.CloseBraceToken)
                    NextToken();
            }

            var closeBrace = MatchToken(SyntaxKind.CloseBraceToken);
            return new InitializerListSyntax(openBrace, items.ToImmutable(), closeBrace);
        }

        private InitializerListItemSyntax ParseInitializerListItem()
        {
            var designators = ImmutableArray.CreateBuilder<DesignatorSyntax>();

            while (IsDesignatorStart(Current.Kind))
                designators.Add(ParseDesignator());

            SyntaxToken? equalsToken = null;
            if (designators.Count != 0)
                equalsToken = MatchToken(SyntaxKind.EqualsToken);

            var initializer = ParseInitializer();

            SyntaxToken? commaToken = null;
            if (Current.Kind == SyntaxKind.CommaToken)
                commaToken = NextToken();

            return new InitializerListItemSyntax(
                designators.ToImmutable(),
                equalsToken,
                initializer,
                commaToken);
        }

        private DesignatorSyntax ParseDesignator()
        {
            if (Current.Kind == SyntaxKind.DotToken)
            {
                var dot = NextToken();
                var name = Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken
                    ? NextToken()
                    : MatchToken(SyntaxKind.IdentifierToken);

                return new FieldDesignatorSyntax(dot, name);
            }

            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                var openBracket = MatchToken(SyntaxKind.OpenBracketToken);

                ExpressionSyntax expression;
                if (Current.Kind != SyntaxKind.CloseBracketToken &&
                    Current.Kind != SyntaxKind.EndOfFileToken)
                {
                    expression = ParseExpression();
                }
                else
                {
                    Report(Current, "Expected array designator expression.");
                    expression = new InvalidExpressionSyntax(MissingTokenAt(Current.Position));
                }

                var closeBracket = MatchToken(SyntaxKind.CloseBracketToken);
                return new ArrayDesignatorSyntax(openBracket, expression, closeBracket);
            }

            Report(Current, $"Expected initializer designator, got '{Current.Kind}'.");
            return new FieldDesignatorSyntax(
                MissingTokenAt(Current.Position),
                MissingTokenAt(Current.Position));
        }

        private void SkipInitializerListRecovery()
        {
            while (Current.Kind != SyntaxKind.EndOfFileToken &&
                   Current.Kind != SyntaxKind.CloseBraceToken &&
                   Current.Kind != SyntaxKind.CommaToken)
            {
                NextToken();
            }
        }

        private CompoundStatementSyntax ParseCompoundStatement()
        {
            _typeNames.BeginScope();

            var openBrace = MatchToken(SyntaxKind.OpenBraceToken);
            var members = ImmutableArray.CreateBuilder<SyntaxNode>();

            try
            {
                while (Current.Kind != SyntaxKind.CloseBraceToken &&
                       Current.Kind != SyntaxKind.EndOfFileToken)
                {
                    var start = _position;

                    if (IsStaticAssertDeclarationStart(Current.Kind))
                        members.Add(ParseStaticAssertDeclaration());
                    else if (IsDeclarationSpecifierStart(Current.Kind))
                        members.Add(ParseBlockDeclaration());
                    else
                        members.Add(ParseStatement());

                    if (_position == start)
                        members.Add(ParseSkippedStatement());
                }

                var closeBrace = MatchToken(SyntaxKind.CloseBraceToken);
                return new CompoundStatementSyntax(openBrace, members.ToImmutable(), closeBrace);
            }
            finally
            {
                _typeNames.EndScope();
            }
        }

        private StatementSyntax ParseStatement()
        {
            return Current.Kind switch
            {
                SyntaxKind.OpenBraceToken => ParseCompoundStatement(),
                SyntaxKind.IfKeyword => ParseIfStatement(),
                SyntaxKind.SwitchKeyword => ParseSwitchStatement(),
                SyntaxKind.WhileKeyword => ParseWhileStatement(),
                SyntaxKind.DoKeyword => ParseDoStatement(),
                SyntaxKind.ForKeyword => ParseForStatement(),
                SyntaxKind.BreakKeyword => ParseBreakStatement(),
                SyntaxKind.ContinueKeyword => ParseContinueStatement(),
                SyntaxKind.GotoKeyword => ParseGotoStatement(),
                SyntaxKind.CaseKeyword => ParseCaseStatement(),
                SyntaxKind.DefaultKeyword => ParseDefaultStatement(),
                SyntaxKind.ReturnKeyword => ParseReturnStatement(),
                SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken when Peek(1).Kind == SyntaxKind.ColonToken => ParseLabelStatement(),
                _ => ParseExpressionStatement(),
            };
        }

        private IfStatementSyntax ParseIfStatement()
        {
            var ifKeyword = MatchToken(SyntaxKind.IfKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var thenStatement = ParseStatement();

            SyntaxToken? elseKeyword = null;
            StatementSyntax? elseStatement = null;
            if (Current.Kind == SyntaxKind.ElseKeyword)
            {
                elseKeyword = NextToken();
                elseStatement = ParseStatement();
            }

            return new IfStatementSyntax(
                ifKeyword,
                openParen,
                condition,
                closeParen,
                thenStatement,
                elseKeyword,
                elseStatement);
        }

        private SwitchStatementSyntax ParseSwitchStatement()
        {
            var switchKeyword = MatchToken(SyntaxKind.SwitchKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var expression = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var statement = ParseStatement();

            return new SwitchStatementSyntax(
                switchKeyword,
                openParen,
                expression,
                closeParen,
                statement);
        }

        private WhileStatementSyntax ParseWhileStatement()
        {
            var whileKeyword = MatchToken(SyntaxKind.WhileKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var statement = ParseStatement();

            return new WhileStatementSyntax(
                whileKeyword,
                openParen,
                condition,
                closeParen,
                statement);
        }

        private DoStatementSyntax ParseDoStatement()
        {
            var doKeyword = MatchToken(SyntaxKind.DoKeyword);
            var statement = ParseStatement();
            var whileKeyword = MatchToken(SyntaxKind.WhileKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var condition = ParseExpression();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var semicolon = MatchToken(SyntaxKind.SemicolonToken);

            return new DoStatementSyntax(
                doKeyword,
                statement,
                whileKeyword,
                openParen,
                condition,
                closeParen,
                semicolon);
        }

        private ForStatementSyntax ParseForStatement()
        {
            _typeNames.BeginScope();

            try
            {
                var forKeyword = MatchToken(SyntaxKind.ForKeyword);
                var openParen = MatchToken(SyntaxKind.OpenParenToken);

                SyntaxNode? initializer = null;
                SyntaxToken firstSemicolon;

                if (IsDeclarationSpecifierStart(Current.Kind))
                {
                    var declaration = ParseBlockDeclaration();
                    initializer = declaration;
                    firstSemicolon = declaration.SemicolonToken;
                }
                else
                {
                    if (Current.Kind != SyntaxKind.SemicolonToken &&
                        Current.Kind != SyntaxKind.EndOfFileToken)
                    {
                        initializer = ParseExpression();
                    }

                    firstSemicolon = MatchToken(SyntaxKind.SemicolonToken);
                }

                ExpressionSyntax? condition = null;
                if (Current.Kind != SyntaxKind.SemicolonToken &&
                    Current.Kind != SyntaxKind.EndOfFileToken)
                {
                    condition = ParseExpression();
                }

                var secondSemicolon = MatchToken(SyntaxKind.SemicolonToken);

                ExpressionSyntax? increment = null;
                if (Current.Kind != SyntaxKind.CloseParenToken &&
                    Current.Kind != SyntaxKind.EndOfFileToken)
                {
                    increment = ParseExpression();
                }

                var closeParen = MatchToken(SyntaxKind.CloseParenToken);
                var statement = ParseStatement();

                return new ForStatementSyntax(
                    forKeyword,
                    openParen,
                    initializer,
                    firstSemicolon,
                    condition,
                    secondSemicolon,
                    increment,
                    closeParen,
                    statement);
            }
            finally
            {
                _typeNames.EndScope();
            }
        }

        private BreakStatementSyntax ParseBreakStatement()
        {
            var breakKeyword = MatchToken(SyntaxKind.BreakKeyword);
            var semicolon = MatchToken(SyntaxKind.SemicolonToken);
            return new BreakStatementSyntax(breakKeyword, semicolon);
        }

        private ContinueStatementSyntax ParseContinueStatement()
        {
            var continueKeyword = MatchToken(SyntaxKind.ContinueKeyword);
            var semicolon = MatchToken(SyntaxKind.SemicolonToken);
            return new ContinueStatementSyntax(continueKeyword, semicolon);
        }

        private GotoStatementSyntax ParseGotoStatement()
        {
            var gotoKeyword = MatchToken(SyntaxKind.GotoKeyword);
            var identifier = Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken
                ? NextToken()
                : MatchToken(SyntaxKind.IdentifierToken);
            var semicolon = MatchToken(SyntaxKind.SemicolonToken);

            return new GotoStatementSyntax(gotoKeyword, identifier, semicolon);
        }

        private LabelStatementSyntax ParseLabelStatement()
        {
            var identifier = NextToken();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var statement = ParseStatement();

            return new LabelStatementSyntax(identifier, colon, statement);
        }

        private CaseStatementSyntax ParseCaseStatement()
        {
            var caseKeyword = MatchToken(SyntaxKind.CaseKeyword);
            var expression = ParseExpression();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var statement = ParseStatement();

            return new CaseStatementSyntax(caseKeyword, expression, colon, statement);
        }

        private DefaultStatementSyntax ParseDefaultStatement()
        {
            var defaultKeyword = MatchToken(SyntaxKind.DefaultKeyword);
            var colon = MatchToken(SyntaxKind.ColonToken);
            var statement = ParseStatement();

            return new DefaultStatementSyntax(defaultKeyword, colon, statement);
        }

        private ReturnStatementSyntax ParseReturnStatement()
        {
            var returnKeyword = MatchToken(SyntaxKind.ReturnKeyword);

            ExpressionSyntax? expression = null;
            if (!IsStatementExpressionTerminator(Current.Kind))
                expression = ParseExpression();

            var semicolon = MatchToken(SyntaxKind.SemicolonToken);
            return new ReturnStatementSyntax(returnKeyword, expression, semicolon);
        }

        private ExpressionStatementSyntax ParseExpressionStatement()
        {
            ExpressionSyntax? expression = null;

            if (Current.Kind != SyntaxKind.SemicolonToken &&
                Current.Kind != SyntaxKind.CloseBraceToken &&
                Current.Kind != SyntaxKind.EndOfFileToken)
            {
                expression = ParseExpression();
            }

            var semicolon = MatchToken(SyntaxKind.SemicolonToken);
            return new ExpressionStatementSyntax(expression, semicolon);
        }

        private ExpressionSyntax ParseExpression()
        {
            var expression = ParseAssignmentExpression();

            while (Current.Kind == SyntaxKind.CommaToken)
            {
                var comma = NextToken();
                var right = ParseAssignmentExpression();
                expression = new BinaryExpressionSyntax(expression, comma, right);
            }

            return expression;
        }

        private ExpressionSyntax ParseAssignmentExpression()
        {
            var left = ParseConditionalExpression();

            if (!IsAssignmentOperator(Current.Kind))
                return left;

            var operatorToken = NextToken();
            var right = ParseAssignmentExpression();
            return new AssignmentExpressionSyntax(left, operatorToken, right);
        }

        private ExpressionSyntax ParseConditionalExpression()
        {
            var condition = ParseBinaryExpression();

            if (Current.Kind != SyntaxKind.QuestionToken)
                return condition;

            var question = NextToken();
            var whenTrue = ParseExpression();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var whenFalse = ParseConditionalExpression();

            return new ConditionalExpressionSyntax(
                condition,
                question,
                whenTrue,
                colon,
                whenFalse);
        }

        private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
        {
            var left = ParseUnaryExpression();

            while (true)
            {
                var precedence = GetBinaryOperatorPrecedence(Current.Kind);
                if (precedence == 0 || precedence <= parentPrecedence)
                    break;

                var operatorToken = NextToken();
                var right = ParseBinaryExpression(precedence);
                left = new BinaryExpressionSyntax(left, operatorToken, right);
            }

            return left;
        }

        private ExpressionSyntax ParseUnaryExpression()
        {
            if (IsCastExpressionStart())
                return ParseCastExpression();

            if (IsSizeofLikeOperator(Current.Kind))
                return ParseSizeofExpression();

            if (GetUnaryOperatorPrecedence(Current.Kind) != 0)
            {
                var operatorToken = NextToken();
                var operand = ParseUnaryExpression();
                return new UnaryExpressionSyntax(operatorToken, operand);
            }

            return ParsePostfixExpression();
        }

        private CompoundLiteralExpressionSyntax ParseCompoundLiteralExpression()
        {
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var typeNameTokens = ReadTypeNameTokens();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var initializerList = ParseInitializerList();

            return new CompoundLiteralExpressionSyntax(
                openParen,
                typeNameTokens,
                closeParen,
                initializerList);
        }

        private CastExpressionSyntax ParseCastExpression()
        {
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var typeNameTokens = ReadTypeNameTokens();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);
            var expression = ParseUnaryExpression();

            return new CastExpressionSyntax(
                openParen,
                typeNameTokens,
                closeParen,
                expression);
        }

        private SizeofExpressionSyntax ParseSizeofExpression()
        {
            var keyword = NextToken();

            if (Current.Kind == SyntaxKind.OpenParenToken && IsTypeNameStart(Peek(1).Kind))
            {
                var openParen = MatchToken(SyntaxKind.OpenParenToken);
                var typeNameTokens = ReadTypeNameTokens();
                var closeParen = MatchToken(SyntaxKind.CloseParenToken);

                return new SizeofExpressionSyntax(
                    keyword,
                    openParen,
                    typeNameTokens,
                    closeParen,
                    expression: null);
            }

            var expression = ParseUnaryExpression();
            return new SizeofExpressionSyntax(
                keyword,
                null,
                ImmutableArray<SyntaxToken>.Empty,
                null,
                expression);
        }

        private ExpressionSyntax ParsePostfixExpression()
        {
            var expression = IsCompoundLiteralExpressionStart()
                ? ParseCompoundLiteralExpression()
                : ParsePrimaryExpression();

            while (true)
            {
                switch (Current.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        expression = ParseCallExpression(expression);
                        break;

                    case SyntaxKind.OpenBracketToken:
                        expression = ParseElementAccessExpression(expression);
                        break;

                    case SyntaxKind.DotToken:
                    case SyntaxKind.ArrowToken:
                        expression = ParseMemberAccessExpression(expression);
                        break;

                    case SyntaxKind.PlusPlusToken:
                    case SyntaxKind.MinusMinusToken:
                        expression = new PostfixUnaryExpressionSyntax(expression, NextToken());
                        break;

                    default:
                        return expression;
                }
            }
        }

        private CallExpressionSyntax ParseCallExpression(ExpressionSyntax expression)
        {
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var arguments = ImmutableArray.CreateBuilder<ExpressionSyntax>();

            if (Current.Kind != SyntaxKind.CloseParenToken &&
                Current.Kind != SyntaxKind.EndOfFileToken)
            {
                while (true)
                {
                    arguments.Add(ParseAssignmentExpression());

                    if (Current.Kind != SyntaxKind.CommaToken)
                        break;

                    NextToken();
                }
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new CallExpressionSyntax(
                expression,
                openParen,
                arguments.ToImmutable(),
                closeParen);
        }

        private ElementAccessExpressionSyntax ParseElementAccessExpression(ExpressionSyntax expression)
        {
            var openBracket = MatchToken(SyntaxKind.OpenBracketToken);

            ExpressionSyntax? index = null;
            if (Current.Kind != SyntaxKind.CloseBracketToken &&
                Current.Kind != SyntaxKind.EndOfFileToken)
            {
                index = ParseExpression();
            }

            var closeBracket = MatchToken(SyntaxKind.CloseBracketToken);
            return new ElementAccessExpressionSyntax(expression, openBracket, index, closeBracket);
        }

        private MemberAccessExpressionSyntax ParseMemberAccessExpression(ExpressionSyntax expression)
        {
            var operatorToken = NextToken();
            var name = Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken
                ? NextToken()
                : MatchToken(SyntaxKind.IdentifierToken);

            return new MemberAccessExpressionSyntax(expression, operatorToken, name);
        }

        private ExpressionSyntax ParsePrimaryExpression()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.UnderscoreGenericKeyword:
                    return ParseGenericSelectionExpression();

                case SyntaxKind.IdentifierToken:
                case SyntaxKind.TypedefNameToken:
                    return new NameExpressionSyntax(NextToken());

                case SyntaxKind.IntegerLiteralToken:
                case SyntaxKind.FloatingLiteralToken:
                case SyntaxKind.CharacterLiteralToken:
                case SyntaxKind.WideCharacterLiteralToken:
                case SyntaxKind.Utf8CharacterLiteralToken:
                case SyntaxKind.Utf16CharacterLiteralToken:
                case SyntaxKind.Utf32CharacterLiteralToken:
                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.WideStringLiteralToken:
                case SyntaxKind.Utf8StringLiteralToken:
                case SyntaxKind.Utf16StringLiteralToken:
                case SyntaxKind.Utf32StringLiteralToken:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.NullptrKeyword:
                    return new LiteralExpressionSyntax(NextToken());

                case SyntaxKind.OpenParenToken:
                    {
                        if (Peek(1).Kind == SyntaxKind.OpenBraceToken)
                            return ParseStatementExpression();

                        var openParen = MatchToken(SyntaxKind.OpenParenToken);
                        var expression = ParseExpression();
                        var closeParen = MatchToken(SyntaxKind.CloseParenToken);

                        return new ParenthesizedExpressionSyntax(
                            openParen,
                            expression,
                            closeParen);
                    }

                default:
                    {
                        Report(Current, $"Expected expression, got '{Current.Kind}'.");

                        if (IsExpressionRecoveryPoint(Current.Kind))
                            return new InvalidExpressionSyntax(MissingTokenAt(Current.Position));

                        return new InvalidExpressionSyntax(NextToken());
                    }
            }
        }

        private GenericSelectionExpressionSyntax ParseGenericSelectionExpression()
        {
            var genericKeyword = MatchToken(SyntaxKind.UnderscoreGenericKeyword);
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var controlExpression = ParseAssignmentExpression();
            var comma = MatchToken(SyntaxKind.CommaToken);

            var associations = ImmutableArray.CreateBuilder<GenericAssociationSyntax>();
            while (Current.Kind != SyntaxKind.CloseParenToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                associations.Add(ParseGenericAssociation());

                if (Current.Kind != SyntaxKind.CommaToken)
                    break;

                NextToken();

                if (Current.Kind == SyntaxKind.CloseParenToken)
                    break;
            }

            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new GenericSelectionExpressionSyntax(
                genericKeyword,
                openParen,
                controlExpression,
                comma,
                associations.ToImmutable(),
                closeParen);
        }

        private GenericAssociationSyntax ParseGenericAssociation()
        {
            SyntaxToken? defaultKeyword = null;
            var typeNameTokens = ImmutableArray.CreateBuilder<SyntaxToken>();

            if (Current.Kind == SyntaxKind.DefaultKeyword)
                defaultKeyword = NextToken();
            else
                ReadTokensUntilTopLevel(typeNameTokens, SyntaxKind.ColonToken);

            var colon = MatchToken(SyntaxKind.ColonToken);
            var expression = ParseAssignmentExpression();

            return new GenericAssociationSyntax(
                defaultKeyword,
                typeNameTokens.ToImmutable(),
                colon,
                expression);
        }

        private StatementExpressionSyntax ParseStatementExpression()
        {
            var openParen = MatchToken(SyntaxKind.OpenParenToken);
            var statement = ParseCompoundStatement();
            var closeParen = MatchToken(SyntaxKind.CloseParenToken);

            return new StatementExpressionSyntax(openParen, statement, closeParen);
        }

        private ImmutableArray<SyntaxToken> ReadTypeNameTokens()
        {
            var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
            var parenDepth = 0;
            var bracketDepth = 0;
            var braceDepth = 0;

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                if (parenDepth == 0 &&
                    bracketDepth == 0 &&
                    braceDepth == 0 &&
                    Current.Kind == SyntaxKind.CloseParenToken)
                {
                    break;
                }

                var token = NextToken();
                tokens.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;
                    case SyntaxKind.CloseParenToken:
                        if (parenDepth > 0)
                            parenDepth--;
                        break;
                    case SyntaxKind.OpenBracketToken:
                        bracketDepth++;
                        break;
                    case SyntaxKind.CloseBracketToken:
                        if (bracketDepth > 0)
                            bracketDepth--;
                        break;
                    case SyntaxKind.OpenBraceToken:
                        braceDepth++;
                        break;
                    case SyntaxKind.CloseBraceToken:
                        if (braceDepth > 0)
                            braceDepth--;
                        break;
                }
            }

            return tokens.ToImmutable();
        }

        private SkippedExternalDeclarationSyntax ParseSkippedExternalDeclaration()
        {
            var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var token = NextToken();
                tokens.Add(token);

                if (token.Kind == SyntaxKind.SemicolonToken)
                    break;

                if (token.Kind == SyntaxKind.CloseBraceToken)
                    break;
            }

            return new SkippedExternalDeclarationSyntax(tokens.ToImmutable());
        }

        private SkippedStatementSyntax ParseSkippedStatement()
        {
            var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();

            while (Current.Kind != SyntaxKind.EndOfFileToken &&
                   Current.Kind != SyntaxKind.CloseBraceToken)
            {
                var token = NextToken();
                tokens.Add(token);

                if (token.Kind == SyntaxKind.SemicolonToken)
                    break;
            }

            return new SkippedStatementSyntax(tokens.ToImmutable());
        }

        private void ReadInitializerListTokens(ImmutableArray<SyntaxToken>.Builder tokens)
        {
            var parenDepth = 0;
            var braceDepth = 0;
            var bracketDepth = 0;

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                if (parenDepth == 0 &&
                    braceDepth == 0 &&
                    bracketDepth == 0 &&
                    Current.Kind == SyntaxKind.CloseBraceToken)
                    break;

                var token = NextToken();
                tokens.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;

                    case SyntaxKind.CloseParenToken:
                        if (parenDepth > 0)
                            parenDepth--;
                        break;

                    case SyntaxKind.OpenBraceToken:
                        braceDepth++;
                        break;

                    case SyntaxKind.CloseBraceToken:
                        if (braceDepth > 0)
                            braceDepth--;
                        break;

                    case SyntaxKind.OpenBracketToken:
                        bracketDepth++;
                        break;

                    case SyntaxKind.CloseBracketToken:
                        if (bracketDepth > 0)
                            bracketDepth--;
                        break;
                }
            }
        }

        private void ReadTokensUntilTopLevel(
            ImmutableArray<SyntaxToken>.Builder tokens,
            SyntaxKind terminator)
        {
            var parenDepth = 0;
            var braceDepth = 0;
            var bracketDepth = 0;

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                if (parenDepth == 0 &&
                    braceDepth == 0 &&
                    bracketDepth == 0 &&
                    Current.Kind == terminator)
                    break;

                var token = NextToken();
                tokens.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;

                    case SyntaxKind.CloseParenToken:
                        if (parenDepth > 0)
                            parenDepth--;
                        break;

                    case SyntaxKind.OpenBraceToken:
                        braceDepth++;
                        break;

                    case SyntaxKind.CloseBraceToken:
                        if (braceDepth > 0)
                            braceDepth--;
                        break;

                    case SyntaxKind.OpenBracketToken:
                        bracketDepth++;
                        break;

                    case SyntaxKind.CloseBracketToken:
                        if (bracketDepth > 0)
                            bracketDepth--;
                        break;
                }
            }
        }

        private void ReadBalancedTokenSequence(ImmutableArray<SyntaxToken>.Builder tokens)
        {
            if (!IsOpenDelimiter(Current.Kind))
                return;

            var depth = 0;

            do
            {
                var token = NextToken();
                tokens.Add(token);

                if (IsOpenDelimiter(token.Kind))
                    depth++;
                else if (IsCloseDelimiter(token.Kind))
                    depth--;

            } while (depth > 0 && Current.Kind != SyntaxKind.EndOfFileToken);
        }

        private void DeclareDeclaratorName(DeclaratorSyntax declarator, bool isTypedefName)
        {
            var identifier = declarator.Identifier;
            if (identifier is null)
                return;

            var text = identifier.Value.Text;
            if (string.IsNullOrEmpty(text))
                return;

            if (isTypedefName)
                _typeNames.DeclareTypedef(text);
            else
                _typeNames.DeclareOrdinaryIdentifier(text);
        }

        private SyntaxToken Peek(int offset)
        {
            var index = _position + offset;

            while (index >= _buffer.Count)
                _buffer.Add(_lexer.NextToken());

            return _buffer[index];
        }

        private SyntaxToken NextToken()
        {
            var current = Current;
            _position++;
            return current;
        }

        private SyntaxToken MatchToken(SyntaxKind kind)
        {
            if (Current.Kind == kind)
                return NextToken();

            Report(Current, $"Expected '{kind}', got '{Current.Kind}'.");
            return MissingTokenAt(Current.Position);
        }

        private SyntaxToken MissingTokenAt(int position)
        {
            return new SyntaxToken(
                SyntaxKind.MissingToken,
                position,
                string.Empty,
                null,
                ImmutableArray<SyntaxTrivia>.Empty,
                ImmutableArray<SyntaxTrivia>.Empty);
        }

        private void Report(SyntaxToken token, string message)
        {
            _diagnostics.Add(SyntaxDiagnostic.Error(message, token.Span));
        }

        private static bool IsStaticAssertDeclarationStart(SyntaxKind kind)
        {
            return kind is SyntaxKind.UnderscoreStaticAssertKeyword or
                SyntaxKind.StaticAssertKeyword;
        }

        private static bool IsDeclarationSpecifierStart(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.TypedefKeyword or
                SyntaxKind.ExternKeyword or
                SyntaxKind.StaticKeyword or
                SyntaxKind.AutoKeyword or
                SyntaxKind.RegisterKeyword or
                SyntaxKind.ThreadLocalKeyword or
                SyntaxKind.UnderscoreThreadLocalKeyword or

                SyntaxKind.VoidKeyword or
                SyntaxKind.CharKeyword or
                SyntaxKind.ShortKeyword or
                SyntaxKind.IntKeyword or
                SyntaxKind.LongKeyword or
                SyntaxKind.FloatKeyword or
                SyntaxKind.DoubleKeyword or
                SyntaxKind.SignedKeyword or
                SyntaxKind.UnsignedKeyword or
                SyntaxKind.BoolKeyword or
                SyntaxKind.UnderscoreBoolKeyword or
                SyntaxKind.UnderscoreComplexKeyword or
                SyntaxKind.UnderscoreImaginaryKeyword or
                SyntaxKind.UnderscoreBitIntKeyword or
                SyntaxKind.UnderscoreDecimal32Keyword or
                SyntaxKind.UnderscoreDecimal64Keyword or
                SyntaxKind.UnderscoreDecimal128Keyword or

                SyntaxKind.StructKeyword or
                SyntaxKind.UnionKeyword or
                SyntaxKind.EnumKeyword or
                SyntaxKind.TypeofKeyword or
                SyntaxKind.TypeofUnqualKeyword or
                SyntaxKind.TypeofExtensionKeyword or

                SyntaxKind.ConstKeyword or
                SyntaxKind.VolatileKeyword or
                SyntaxKind.RestrictKeyword or
                SyntaxKind.InlineKeyword or
                SyntaxKind.UnderscoreNoreturnKeyword or
                SyntaxKind.ConstexprKeyword or

                SyntaxKind.AlignasKeyword or
                SyntaxKind.UnderscoreAlignasKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or

                SyntaxKind.ConstExtensionKeyword or
                SyntaxKind.VolatileExtensionKeyword or
                SyntaxKind.RestrictExtensionKeyword or
                SyntaxKind.InlineExtensionKeyword or
                SyntaxKind.ExtensionKeyword or
                SyntaxKind.AttributeKeyword or
                SyntaxKind.DeclspecKeyword or

                SyntaxKind.TypedefNameToken => true,

                _ => false,
            };
        }

        private static bool IsParenthesizedDeclarationSpecifier(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.AlignasKeyword or
                SyntaxKind.UnderscoreAlignasKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or
                SyntaxKind.TypeofKeyword or
                SyntaxKind.TypeofUnqualKeyword or
                SyntaxKind.TypeofExtensionKeyword or
                SyntaxKind.UnderscoreBitIntKeyword or
                SyntaxKind.AttributeKeyword or
                SyntaxKind.DeclspecKeyword => true,

                _ => false,
            };
        }

        private static bool IsTypeNameStart(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.VoidKeyword or
                SyntaxKind.CharKeyword or
                SyntaxKind.ShortKeyword or
                SyntaxKind.IntKeyword or
                SyntaxKind.LongKeyword or
                SyntaxKind.FloatKeyword or
                SyntaxKind.DoubleKeyword or
                SyntaxKind.SignedKeyword or
                SyntaxKind.UnsignedKeyword or
                SyntaxKind.BoolKeyword or
                SyntaxKind.UnderscoreBoolKeyword or
                SyntaxKind.UnderscoreComplexKeyword or
                SyntaxKind.UnderscoreImaginaryKeyword or
                SyntaxKind.UnderscoreBitIntKeyword or
                SyntaxKind.UnderscoreDecimal32Keyword or
                SyntaxKind.UnderscoreDecimal64Keyword or
                SyntaxKind.UnderscoreDecimal128Keyword or

                SyntaxKind.StructKeyword or
                SyntaxKind.UnionKeyword or
                SyntaxKind.EnumKeyword or
                SyntaxKind.TypeofKeyword or
                SyntaxKind.TypeofUnqualKeyword or
                SyntaxKind.TypeofExtensionKeyword or

                SyntaxKind.ConstKeyword or
                SyntaxKind.VolatileKeyword or
                SyntaxKind.RestrictKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or

                SyntaxKind.ConstExtensionKeyword or
                SyntaxKind.VolatileExtensionKeyword or
                SyntaxKind.RestrictExtensionKeyword or

                SyntaxKind.TypedefNameToken => true,

                _ => false,
            };
        }

        private static bool IsDesignatorStart(SyntaxKind kind)
        {
            return kind is SyntaxKind.DotToken or SyntaxKind.OpenBracketToken;
        }

        private bool IsCastExpressionStart()
        {
            return Current.Kind == SyntaxKind.OpenParenToken &&
                   IsTypeNameStart(Peek(1).Kind);
        }

        private bool IsCompoundLiteralExpressionStart()
        {
            if (Current.Kind != SyntaxKind.OpenParenToken ||
                !IsTypeNameStart(Peek(1).Kind))
                return false;

            var depth = 0;
            var offset = 0;

            while (true)
            {
                var kind = Peek(offset).Kind;

                if (kind == SyntaxKind.EndOfFileToken)
                    return false;

                if (kind == SyntaxKind.OpenParenToken)
                    depth++;
                else if (kind == SyntaxKind.CloseParenToken)
                {
                    depth--;

                    if (depth == 0)
                        return Peek(offset + 1).Kind == SyntaxKind.OpenBraceToken;
                }

                offset++;
            }
        }

        private static bool IsSizeofLikeOperator(SyntaxKind kind)
        {
            return kind is SyntaxKind.SizeofKeyword or
                SyntaxKind.AlignofKeyword or
                SyntaxKind.UnderscoreAlignofKeyword;
        }

        private static bool IsTypeQualifier(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.ConstKeyword or
                SyntaxKind.VolatileKeyword or
                SyntaxKind.RestrictKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or
                SyntaxKind.ConstExtensionKeyword or
                SyntaxKind.VolatileExtensionKeyword or
                SyntaxKind.RestrictExtensionKeyword => true,

                _ => false,
            };
        }

        private static bool IsDeclaratorRecoveryPoint(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.CommaToken or
                SyntaxKind.SemicolonToken or
                SyntaxKind.EqualsToken or
                SyntaxKind.OpenBraceToken or
                SyntaxKind.CloseParenToken or
                SyntaxKind.CloseBracketToken or
                SyntaxKind.EndOfFileToken => true,

                _ => false,
            };
        }

        private static bool IsStatementExpressionTerminator(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.SemicolonToken or
                SyntaxKind.CloseParenToken or
                SyntaxKind.CloseBracketToken or
                SyntaxKind.CloseBraceToken or
                SyntaxKind.EndOfFileToken => true,

                _ => false,
            };
        }

        private static bool IsExpressionRecoveryPoint(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.SemicolonToken or
                SyntaxKind.CommaToken or
                SyntaxKind.ColonToken or
                SyntaxKind.CloseParenToken or
                SyntaxKind.CloseBracketToken or
                SyntaxKind.CloseBraceToken or
                SyntaxKind.EndOfFileToken => true,

                _ => false,
            };
        }

        private static bool IsOpenDelimiter(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.OpenParenToken or
                SyntaxKind.OpenBracketToken or
                SyntaxKind.OpenBraceToken => true,

                _ => false,
            };
        }

        private static bool IsCloseDelimiter(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.CloseParenToken or
                SyntaxKind.CloseBracketToken or
                SyntaxKind.CloseBraceToken => true,

                _ => false,
            };
        }

        private static int GetUnaryOperatorPrecedence(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.PlusToken or
                SyntaxKind.MinusToken or
                SyntaxKind.BangToken or
                SyntaxKind.TildeToken or
                SyntaxKind.StarToken or
                SyntaxKind.AmpersandToken or
                SyntaxKind.PlusPlusToken or
                SyntaxKind.MinusMinusToken => 12,

                _ => 0,
            };
        }

        private static int GetBinaryOperatorPrecedence(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.PipePipeToken => 2,
                SyntaxKind.AmpersandAmpersandToken => 3,
                SyntaxKind.PipeToken => 4,
                SyntaxKind.HatToken => 5,
                SyntaxKind.AmpersandToken => 6,

                SyntaxKind.EqualsEqualsToken or
                SyntaxKind.BangEqualsToken => 7,

                SyntaxKind.LessThanToken or
                SyntaxKind.LessThanEqualsToken or
                SyntaxKind.GreaterThanToken or
                SyntaxKind.GreaterThanEqualsToken => 8,

                SyntaxKind.LessThanLessThanToken or
                SyntaxKind.GreaterThanGreaterThanToken => 9,

                SyntaxKind.PlusToken or
                SyntaxKind.MinusToken => 10,

                SyntaxKind.StarToken or
                SyntaxKind.SlashToken or
                SyntaxKind.PercentToken => 11,

                _ => 0,
            };
        }

        private static bool IsAssignmentOperator(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.EqualsToken or
                SyntaxKind.PlusEqualsToken or
                SyntaxKind.MinusEqualsToken or
                SyntaxKind.StarEqualsToken or
                SyntaxKind.SlashEqualsToken or
                SyntaxKind.PercentEqualsToken or
                SyntaxKind.AmpersandEqualsToken or
                SyntaxKind.PipeEqualsToken or
                SyntaxKind.HatEqualsToken or
                SyntaxKind.LessThanLessThanEqualsToken or
                SyntaxKind.GreaterThanGreaterThanEqualsToken => true,

                _ => false,
            };
        }
    }
}
