using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Python
{
    internal sealed class Parser
    {
        private const int LookaheadCompactionThreshold = 64;

        private readonly Lexer _lexer;
        private readonly List<GreenToken> _lookahead = [];

        private int _lookaheadStart;
        private int _consumedTokenCount;
        private GreenToken? _endOfFile;

        internal Parser(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _lexer = new Lexer(source);
        }

        private GreenToken Current => Peek(0);

        private GreenToken Peek(int offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);

            var index = checked(_lookaheadStart + offset);
            while (index >= _lookahead.Count)
            {
                if (_endOfFile is not null)
                    return _endOfFile;

                var token = _lexer.NextToken();
                _lookahead.Add(token);
                if (token.Kind == SyntaxKind.EndOfFileToken)
                    _endOfFile = token;
            }

            return _lookahead[index];
        }

        internal GreenNode ParseCompilationUnit()
        {
            var statements = new List<GreenNode?>();

            while (Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;

                if (Current.Kind is SyntaxKind.IndentToken or SyntaxKind.DedentToken)
                {
                    statements.Add(ParseSkippedTokens(
                        static kind => kind is not (
                            SyntaxKind.IndentToken or SyntaxKind.DedentToken)));
                }
                else if (Current.Kind == SyntaxKind.NewLineToken)
                {
                    statements.Add(EatToken());
                }
                else
                {
                    statements.Add(ParseStatement());
                }

                EnsureProgress(start, statements);
            }

            var endOfFile = EatToken();
            return GreenFactory.Node(
                SyntaxKind.CompilationUnit,
                GreenFactory.List(statements),
                endOfFile);
        }

        private GreenNode ParseStatement()
        {
            return Current.Kind switch
            {
                SyntaxKind.AtToken => ParseDecoratedStatement(),
                SyntaxKind.DefKeyword => ParseFunctionDefinition(asyncKeyword: null, decorators: null),
                SyntaxKind.ClassKeyword => ParseClassDefinition(decorators: null),
                SyntaxKind.IfKeyword => ParseIfStatement(),
                SyntaxKind.WhileKeyword => ParseWhileStatement(),
                SyntaxKind.ForKeyword => ParseForStatement(asyncKeyword: null),
                SyntaxKind.WithKeyword => ParseWithStatement(asyncKeyword: null),
                SyntaxKind.TryKeyword => ParseTryStatement(),
                SyntaxKind.AsyncKeyword when Peek(1).Kind == SyntaxKind.DefKeyword =>
                    ParseFunctionDefinition(EatToken(), decorators: null),
                SyntaxKind.AsyncKeyword when Peek(1).Kind == SyntaxKind.ForKeyword =>
                    ParseForStatement(EatToken()),
                SyntaxKind.AsyncKeyword when Peek(1).Kind == SyntaxKind.WithKeyword =>
                    ParseWithStatement(EatToken()),
                _ when IsMatchStatementStart() => ParseMatchStatement(),
                _ => ParseSimpleStatementList(),
            };
        }

        private GreenNode ParseSimpleStatementList()
        {
            var children = new List<GreenNode?>
        {
            ParseSimpleStatement(),
        };

            while (Current.Kind == SyntaxKind.SemicolonToken)
            {
                children.Add(EatToken());
                if (Current.Kind is SyntaxKind.NewLineToken or SyntaxKind.EndOfFileToken)
                    break;

                var start = _consumedTokenCount;
                children.Add(ParseSimpleStatement());
                EnsureProgress(start, children);
            }

            if (Current.Kind is not (SyntaxKind.NewLineToken or SyntaxKind.EndOfFileToken))
            {
                children.Add(ParseSkippedTokens(
                    static kind => kind is not (
                        SyntaxKind.SemicolonToken or
                        SyntaxKind.NewLineToken or
                        SyntaxKind.DedentToken or
                        SyntaxKind.EndOfFileToken)));

                while (Current.Kind == SyntaxKind.SemicolonToken)
                {
                    children.Add(EatToken());
                    if (Current.Kind is SyntaxKind.NewLineToken or SyntaxKind.EndOfFileToken)
                        break;

                    var start = _consumedTokenCount;
                    children.Add(ParseSimpleStatement());
                    EnsureProgress(start, children);
                }
            }

            children.Add(MatchToken(SyntaxKind.NewLineToken));
            return GreenFactory.Node(SyntaxKind.SimpleStatementList, [.. children]);
        }

        private GreenNode ParseSimpleStatement()
        {
            return Current.Kind switch
            {
                SyntaxKind.ReturnKeyword => ParseReturnStatement(),
                SyntaxKind.RaiseKeyword => ParseRaiseStatement(),
                SyntaxKind.PassKeyword => GreenFactory.Node(
                    SyntaxKind.PassStatement,
                    EatToken()),
                SyntaxKind.BreakKeyword => GreenFactory.Node(
                    SyntaxKind.BreakStatement,
                    EatToken()),
                SyntaxKind.ContinueKeyword => GreenFactory.Node(
                    SyntaxKind.ContinueStatement,
                    EatToken()),
                SyntaxKind.AssertKeyword => ParseAssertStatement(),
                SyntaxKind.DelKeyword => ParseDeleteStatement(),
                SyntaxKind.GlobalKeyword => ParseNameDeclaration(SyntaxKind.GlobalStatement),
                SyntaxKind.NonlocalKeyword => ParseNameDeclaration(SyntaxKind.NonlocalStatement),
                SyntaxKind.ImportKeyword => ParseImportStatement(),
                SyntaxKind.FromKeyword => ParseFromImportStatement(),
                SyntaxKind.YieldKeyword => GreenFactory.Node(
                    SyntaxKind.YieldStatement,
                    ParseYieldExpression()),
                _ when IsSoftKeyword(Current, "type") &&
                       Peek(1).Kind == SyntaxKind.IdentifierToken => ParseTypeAliasStatement(),
                _ => ParseExpressionOrAssignmentStatement(),
            };
        }

        private GreenNode ParseReturnStatement()
        {
            var keyword = EatToken();
            GreenNode? expression = null;
            if (!IsSimpleStatementTerminator(Current.Kind))
                expression = ParseStarExpressions();

            return GreenFactory.Node(SyntaxKind.ReturnStatement, keyword, expression);
        }

        private GreenNode ParseRaiseStatement()
        {
            var keyword = EatToken();
            GreenNode? expression = null;
            GreenToken? fromKeyword = null;
            GreenNode? cause = null;

            if (!IsSimpleStatementTerminator(Current.Kind))
            {
                expression = ParseExpression();
                if (Current.Kind == SyntaxKind.FromKeyword)
                {
                    fromKeyword = EatToken();
                    cause = ParseExpression();
                }
            }

            return GreenFactory.Node(
                SyntaxKind.RaiseStatement,
                keyword,
                expression,
                fromKeyword,
                cause);
        }

        private GreenNode ParseAssertStatement()
        {
            var keyword = EatToken();
            var condition = ParseExpression();
            GreenToken? comma = null;
            GreenNode? message = null;

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                comma = EatToken();
                message = ParseExpression();
            }

            return GreenFactory.Node(
                SyntaxKind.AssertStatement,
                keyword,
                condition,
                comma,
                message);
        }

        private GreenNode ParseDeleteStatement()
        {
            var keyword = EatToken();
            var targets = ParseTargetList(stopAtInKeyword: false);
            if (!IsAssignmentTarget(
                    targets,
                    allowSequence: true,
                    allowStarredRoot: false,
                    allowStarredInSequence: false))
            {
                targets = WithDiagnostic(
                    targets,
                    SyntaxDiagnosticCode.InvalidAssignmentTarget,
                    "invalid deletion target");
            }

            return GreenFactory.Node(SyntaxKind.DeleteStatement, keyword, targets);
        }

        private GreenNode ParseNameDeclaration(SyntaxKind statementKind)
        {
            var keyword = EatToken();
            var names = new List<GreenNode?>();

            names.Add(MatchName());
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                names.Add(EatToken());
                names.Add(MatchName());
            }

            return GreenFactory.Node(
                statementKind,
                keyword,
                GreenFactory.SeparatedList(names));
        }

        private GreenNode ParseImportStatement()
        {
            var keyword = EatToken();
            var aliases = ParseDottedAliases();
            return GreenFactory.Node(SyntaxKind.ImportStatement, keyword, aliases);
        }

        private GreenNode ParseFromImportStatement()
        {
            var fromKeyword = EatToken();
            var moduleParts = new List<GreenNode?>();

            while (Current.Kind is SyntaxKind.DotToken or SyntaxKind.EllipsisToken)
                moduleParts.Add(EatToken());

            if (Current.Kind == SyntaxKind.IdentifierToken)
                moduleParts.Add(ParseDottedName());

            var importKeyword = MatchToken(SyntaxKind.ImportKeyword);
            GreenNode targets;

            if (Current.Kind == SyntaxKind.StarToken)
            {
                targets = EatToken();
            }
            else if (Current.Kind == SyntaxKind.LeftParenthesisToken)
            {
                var left = EatToken();
                var aliases = ParseImportAliases();
                GreenToken? trailingComma = null;
                if (Current.Kind == SyntaxKind.CommaToken)
                    trailingComma = EatToken();
                var right = MatchToken(SyntaxKind.RightParenthesisToken);
                targets = GreenFactory.Node(
                    SyntaxKind.SyntaxList,
                    left,
                    aliases,
                    trailingComma,
                    right);
            }
            else
            {
                targets = ParseImportAliases();
            }

            return GreenFactory.Node(
                SyntaxKind.FromImportStatement,
                fromKeyword,
                GreenFactory.List(moduleParts),
                importKeyword,
                targets);
        }

        private GreenNode ParseDottedAliases()
        {
            var items = new List<GreenNode?> { ParseDottedAlias() };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                items.Add(ParseDottedAlias());
            }

            return GreenFactory.SeparatedList(items);
        }

        private GreenNode ParseDottedAlias()
        {
            var name = ParseDottedName();
            GreenToken? asKeyword = null;
            GreenToken? alias = null;
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                asKeyword = EatToken();
                alias = MatchName();
            }

            return GreenFactory.Node(SyntaxKind.ImportAlias, name, asKeyword, alias);
        }

        private GreenNode ParseImportAliases()
        {
            var items = new List<GreenNode?> { ParseImportAlias() };
            while (Current.Kind == SyntaxKind.CommaToken &&
                   Peek(1).Kind != SyntaxKind.RightParenthesisToken)
            {
                items.Add(EatToken());
                items.Add(ParseImportAlias());
            }

            return GreenFactory.SeparatedList(items);
        }

        private GreenNode ParseImportAlias()
        {
            var name = MatchName();
            GreenToken? asKeyword = null;
            GreenToken? alias = null;
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                asKeyword = EatToken();
                alias = MatchName();
            }

            return GreenFactory.Node(SyntaxKind.ImportAlias, name, asKeyword, alias);
        }

        private GreenNode ParseDottedName()
        {
            var parts = new List<GreenNode?> { MatchName() };
            while (Current.Kind == SyntaxKind.DotToken)
            {
                parts.Add(EatToken());
                parts.Add(MatchName());
            }

            return GreenFactory.Node(SyntaxKind.DottedName, [.. parts]);
        }

        private GreenNode ParseTypeAliasStatement()
        {
            var typeKeyword = EatToken();
            var name = MatchName();
            GreenNode? typeParameters = null;
            if (Current.Kind == SyntaxKind.LeftBracketToken)
                typeParameters = ParseTypeParameterList();

            var equal = MatchToken(SyntaxKind.EqualToken);
            var value = ParseExpression();
            return GreenFactory.Node(
                SyntaxKind.TypeAliasStatement,
                typeKeyword,
                name,
                typeParameters,
                equal,
                value);
        }

        private GreenNode ParseTypeParameterList()
        {
            var left = EatToken();
            var items = new List<GreenNode?>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sawDefault = false;
            var previousWasTypeVarTuple = false;

            while (Current.Kind is not (
                SyntaxKind.RightBracketToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                var parameter = ParseTypeParameter();
                var prefix = parameter.GetSlot(0) as GreenToken;
                var name = parameter.GetSlot(1) as GreenToken;
                var hasDefault = parameter.GetSlot(4) is GreenToken;

                if (name is { IsMissing: false } &&
                    !string.IsNullOrEmpty(name.Text) &&
                    !names.Add(name.Text))
                {
                    parameter = WithDiagnostic(
                        parameter,
                        SyntaxDiagnosticCode.InvalidParameter,
                        $"duplicate type parameter '{name.Text}'");
                }

                if (sawDefault && !hasDefault)
                {
                    parameter = WithDiagnostic(
                        parameter,
                        SyntaxDiagnosticCode.InvalidParameter,
                        name is { IsMissing: false }
                            ? $"non-default type parameter '{name.Text}' follows default type parameter"
                            : "non-default type parameter follows default type parameter");
                }

                if (previousWasTypeVarTuple &&
                    prefix is null &&
                    hasDefault)
                {
                    parameter = WithDiagnostic(
                        parameter,
                        SyntaxDiagnosticCode.InvalidParameter,
                        "TypeVar with a default cannot immediately follow TypeVarTuple");
                }

                sawDefault |= hasDefault;
                previousWasTypeVarTuple = prefix?.Kind == SyntaxKind.StarToken;
                items.Add(parameter);

                if (Current.Kind != SyntaxKind.CommaToken)
                {
                    if (CanStartTypeParameter(Current.Kind))
                        items.Add(MissingToken(SyntaxKind.CommaToken));
                    else
                        break;
                }
                else
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightBracketToken)
                        break;
                }

                EnsureProgress(start, items);
            }

            var right = MatchToken(SyntaxKind.RightBracketToken);
            GreenNode node = GreenFactory.Node(
                SyntaxKind.TypeParameterList,
                left,
                GreenFactory.SeparatedList(items),
                right);
            return items.Count == 0
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidParameter,
                    "type parameter list cannot be empty")
                : node;
        }

        private GreenNode ParseTypeParameter()
        {
            GreenToken? prefix = null;
            if (Current.Kind is SyntaxKind.StarToken or SyntaxKind.DoubleStarToken)
                prefix = EatToken();

            var name = MatchName();
            GreenToken? colon = null;
            GreenNode? bound = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                colon = EatToken();
                bound = ParseExpression();
            }

            GreenToken? equal = null;
            GreenNode? defaultValue = null;
            if (Current.Kind == SyntaxKind.EqualToken)
            {
                equal = EatToken();
                defaultValue = prefix?.Kind == SyntaxKind.StarToken
                    ? ParseStarExpression()
                    : ParseExpression();
            }

            GreenNode node = GreenFactory.Node(
                SyntaxKind.TypeParameter,
                prefix,
                name,
                colon,
                bound,
                equal,
                defaultValue);
            return prefix is not null && colon is not null
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidParameter,
                    prefix.Kind == SyntaxKind.StarToken
                        ? "TypeVarTuple cannot have a bound or constraints"
                        : "ParamSpec cannot have a bound or constraints")
                : node;
        }

        private GreenNode ParseExpressionOrAssignmentStatement()
        {
            var left = ParseStarExpressions();

            if (Current.Kind == SyntaxKind.ColonToken)
            {
                var colon = EatToken();
                var annotation = ParseExpression();
                GreenToken? equal = null;
                GreenNode? value = null;
                if (Current.Kind == SyntaxKind.EqualToken)
                {
                    equal = EatToken();
                    value = ParseAnnotatedRightHandSide();
                }

                return CreateAssignmentNode(
                    SyntaxKind.AnnotatedAssignmentStatement,
                    left,
                    colon,
                    annotation,
                    equal,
                    value);
            }

            if (IsAugmentedAssignment(Current.Kind))
            {
                var op = EatToken();
                var value = ParseAnnotatedRightHandSide();
                return CreateAssignmentNode(
                    SyntaxKind.AugmentedAssignmentStatement,
                    left,
                    op,
                    value);
            }

            if (Current.Kind == SyntaxKind.EqualToken)
            {
                var children = new List<GreenNode?> { left };
                do
                {
                    children.Add(EatToken());
                    children.Add(ParseAnnotatedRightHandSide());
                }
                while (Current.Kind == SyntaxKind.EqualToken);

                return CreateAssignmentNode(
                    SyntaxKind.AssignmentStatement,
                    [.. children]);
            }

            return GreenFactory.Node(SyntaxKind.ExpressionStatement, left);
        }

        private GreenNode ParseAnnotatedRightHandSide() =>
            Current.Kind == SyntaxKind.YieldKeyword
                ? ParseYieldExpression()
                : ParseStarExpressions();

        private GreenNode CreateAssignmentNode(
            SyntaxKind kind,
            params GreenNode?[] children)
        {
            if (children.Length == 0)
                return GreenFactory.Node(kind, children);

            if (kind is SyntaxKind.AnnotatedAssignmentStatement or
                SyntaxKind.AugmentedAssignmentStatement)
            {
                if (children[0] is { } target &&
                    !IsAssignmentTarget(target, allowSequence: false, allowStarredRoot: false))
                {
                    children[0] = WithDiagnostic(
                        target,
                        kind == SyntaxKind.AugmentedAssignmentStatement
                            ? SyntaxDiagnosticCode.InvalidAugmentedAssignmentTarget
                            : SyntaxDiagnosticCode.InvalidAssignmentTarget,
                        kind == SyntaxKind.AugmentedAssignmentStatement
                            ? "invalid augmented assignment target"
                            : "invalid annotated assignment target");
                }

                return GreenFactory.Node(kind, children);
            }

            for (var i = 0; i + 2 < children.Length; i += 2)
            {
                if (children[i] is not { } target ||
                    IsAssignmentTarget(target, allowSequence: true, allowStarredRoot: false))
                {
                    continue;
                }

                children[i] = WithDiagnostic(
                    target,
                    SyntaxDiagnosticCode.InvalidAssignmentTarget,
                    "invalid assignment target");
            }

            return GreenFactory.Node(kind, children);
        }

        private GreenNode ParseDecoratedStatement()
        {
            var decorators = new List<GreenNode?>();
            while (Current.Kind == SyntaxKind.AtToken)
            {
                var atToken = EatToken();
                var expression = ParseNamedExpression();
                var newLine = MatchToken(SyntaxKind.NewLineToken);
                decorators.Add(GreenFactory.Node(
                    SyntaxKind.Decorator,
                    atToken,
                    expression,
                    newLine));
            }

            var decoratorList = GreenFactory.Node(
                SyntaxKind.DecoratorList,
                GreenFactory.List(decorators));

            if (Current.Kind == SyntaxKind.DefKeyword)
                return ParseFunctionDefinition(null, decoratorList);

            if (Current.Kind == SyntaxKind.ClassKeyword)
                return ParseClassDefinition(decoratorList);

            if (Current.Kind == SyntaxKind.AsyncKeyword &&
                Peek(1).Kind == SyntaxKind.DefKeyword)
            {
                return ParseFunctionDefinition(EatToken(), decoratorList);
            }

            GreenNode? recovery = null;
            if (Current.Kind != SyntaxKind.EndOfFileToken)
                recovery = ParseStatement();

            return GreenFactory.NodeWithDiagnostics(
                SyntaxKind.ErrorStatement,
                [decoratorList, recovery],
                [
                    new GreenDiagnostic(
                        SyntaxDiagnosticCode.ExpectedStatement,
                        decoratorList.FullWidth,
                        recovery?.FullWidth ?? 0,
                        "expected function or class definition after decorator")
                ]);
        }

        private GreenNode ParseFunctionDefinition(
            GreenToken? asyncKeyword,
            GreenNode? decorators)
        {
            var defKeyword = MatchToken(SyntaxKind.DefKeyword);
            var name = MatchName();
            GreenNode? typeParameters = null;
            if (Current.Kind == SyntaxKind.LeftBracketToken)
                typeParameters = ParseTypeParameterList();

            var leftParenthesis = MatchToken(SyntaxKind.LeftParenthesisToken);
            var parameters = ParseParameterList(SyntaxKind.RightParenthesisToken);
            var rightParenthesis = MatchToken(SyntaxKind.RightParenthesisToken);

            GreenToken? arrow = null;
            GreenNode? returnAnnotation = null;
            if (Current.Kind == SyntaxKind.ArrowToken)
            {
                arrow = EatToken();
                returnAnnotation = ParseExpression();
            }

            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            return GreenFactory.Node(
                SyntaxKind.FunctionDefinition,
                decorators,
                asyncKeyword,
                defKeyword,
                name,
                typeParameters,
                leftParenthesis,
                parameters,
                rightParenthesis,
                arrow,
                returnAnnotation,
                colon,
                body);
        }

        private GreenNode ParseClassDefinition(GreenNode? decorators)
        {
            var classKeyword = MatchToken(SyntaxKind.ClassKeyword);
            var name = MatchName();
            GreenNode? typeParameters = null;
            if (Current.Kind == SyntaxKind.LeftBracketToken)
                typeParameters = ParseTypeParameterList();

            GreenNode? arguments = null;
            if (Current.Kind == SyntaxKind.LeftParenthesisToken)
                arguments = ParseArgumentList();

            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            return GreenFactory.Node(
                SyntaxKind.ClassDefinition,
                decorators,
                classKeyword,
                name,
                typeParameters,
                arguments,
                colon,
                body);
        }

        private GreenNode ParseParameterList(SyntaxKind terminator)
        {
            var children = new List<GreenNode?>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sawParameter = false;
            var sawSlash = false;
            var sawStar = false;
            var sawDoubleStar = false;
            var sawPositionalDefault = false;
            var bareStarNeedsFollowingParameter = false;

            while (Current.Kind is not SyntaxKind.EndOfFileToken &&
                   Current.Kind != terminator)
            {
                var start = _consumedTokenCount;
                GreenNode item;

                if (Current.Kind == SyntaxKind.SlashToken)
                {
                    var slash = EatToken();
                    item = slash;
                    if (sawSlash)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidParameter,
                            "duplicate positional-only marker");
                    }
                    else if (!sawParameter)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidParameter,
                            "positional-only marker must follow a parameter");
                    }
                    else if (sawStar || sawDoubleStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidParameter,
                            "positional-only marker must precede * and ** parameters");
                    }

                    sawSlash = true;
                }
                else
                {
                    GreenToken? prefix = null;
                    if (Current.Kind is SyntaxKind.StarToken or SyntaxKind.DoubleStarToken)
                        prefix = EatToken();

                    var isBareStar = prefix?.Kind == SyntaxKind.StarToken &&
                        (Current.Kind == SyntaxKind.CommaToken || Current.Kind == terminator);

                    if (isBareStar)
                    {
                        item = prefix!;
                        if (sawStar || sawDoubleStar)
                        {
                            item = WithDiagnostic(
                                item,
                                SyntaxDiagnosticCode.InvalidParameter,
                                "duplicate * parameter marker");
                        }

                        sawStar = true;
                        bareStarNeedsFollowingParameter = true;
                    }
                    else
                    {
                        var name = MatchName();
                        GreenToken? colon = null;
                        GreenNode? annotation = null;
                        if (Current.Kind == SyntaxKind.ColonToken)
                        {
                            colon = EatToken();
                            annotation = ParseExpression();
                        }

                        GreenToken? equal = null;
                        GreenNode? defaultValue = null;
                        if (Current.Kind == SyntaxKind.EqualToken)
                        {
                            equal = EatToken();
                            defaultValue = ParseExpression();
                        }

                        item = GreenFactory.Node(
                            SyntaxKind.Parameter,
                            prefix,
                            name,
                            colon,
                            annotation,
                            equal,
                            defaultValue);

                        if (sawDoubleStar)
                        {
                            item = WithDiagnostic(
                                item,
                                SyntaxDiagnosticCode.InvalidParameter,
                                "no parameter may follow a ** parameter");
                        }

                        if (prefix?.Kind == SyntaxKind.StarToken)
                        {
                            if (sawStar || sawDoubleStar)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "duplicate * parameter");
                            }
                            if (equal is not null)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "* parameter cannot have a default value");
                            }

                            sawStar = true;
                            bareStarNeedsFollowingParameter = false;
                        }
                        else if (prefix?.Kind == SyntaxKind.DoubleStarToken)
                        {
                            if (sawDoubleStar)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "duplicate ** parameter");
                            }
                            if (equal is not null)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "** parameter cannot have a default value");
                            }

                            sawDoubleStar = true;
                        }
                        else
                        {
                            sawParameter = true;
                            if (!sawStar && !sawDoubleStar)
                            {
                                if (equal is not null)
                                {
                                    sawPositionalDefault = true;
                                }
                                else if (sawPositionalDefault)
                                {
                                    item = WithDiagnostic(
                                        item,
                                        SyntaxDiagnosticCode.InvalidParameter,
                                        "non-default parameter follows default parameter");
                                }
                            }

                            if (bareStarNeedsFollowingParameter)
                                bareStarNeedsFollowingParameter = false;
                        }
                    }
                }

                children.Add(ValidateParameterName(item, names));

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    children.Add(EatToken());
                    if (Current.Kind == terminator)
                        break;
                }
                else if (CanStartParameter(Current.Kind))
                {
                    children.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, children);
            }

            var node = GreenFactory.Node(
                SyntaxKind.ParameterList,
                GreenFactory.SeparatedList(children));

            return bareStarNeedsFollowingParameter
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidParameter,
                    "named arguments must follow bare *")
                : node;
        }

        private GreenNode ParseWithStatement(GreenToken? asyncKeyword)
        {
            var withKeyword = MatchToken(SyntaxKind.WithKeyword);
            GreenToken? leftParenthesis = null;
            GreenToken? rightParenthesis = null;
            if (Current.Kind == SyntaxKind.LeftParenthesisToken)
                leftParenthesis = EatToken();

            var items = new List<GreenNode?>();
            var terminator = leftParenthesis is null
                ? SyntaxKind.ColonToken
                : SyntaxKind.RightParenthesisToken;

            if (Current.Kind == terminator)
                items.Add(GreenFactory.MissingExpression("expected with item"));

            while (Current.Kind is not SyntaxKind.EndOfFileToken &&
                   Current.Kind != terminator)
            {
                var start = _consumedTokenCount;
                items.Add(ParseWithItem());

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == terminator)
                        break;
                }
                else if (CanStartExpression(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            if (leftParenthesis is not null)
                rightParenthesis = MatchToken(SyntaxKind.RightParenthesisToken);

            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            return GreenFactory.Node(
                SyntaxKind.WithStatement,
                asyncKeyword,
                withKeyword,
                leftParenthesis,
                GreenFactory.SeparatedList(items),
                rightParenthesis,
                colon,
                body);
        }

        private GreenNode ParseWithItem()
        {
            var expression = ParseExpression();
            GreenToken? asKeyword = null;
            GreenNode? target = null;
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                asKeyword = EatToken();
                target = ParseTarget();
                if (!IsAssignmentTarget(target, allowSequence: true, allowStarredRoot: false))
                {
                    target = WithDiagnostic(
                        target,
                        SyntaxDiagnosticCode.InvalidAssignmentTarget,
                        "invalid with-item target");
                }
            }

            return GreenFactory.Node(
                SyntaxKind.WithItem,
                expression,
                asKeyword,
                target);
        }

        private GreenNode ParseTryStatement()
        {
            var tryKeyword = EatToken();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            var clauses = new List<GreenNode?>();
            var sawNormalExcept = false;
            var sawStarExcept = false;
            var sawBareExcept = false;
            var exceptCount = 0;

            while (Current.Kind == SyntaxKind.ExceptKeyword)
            {
                var isStar = Peek(1).Kind == SyntaxKind.StarToken;
                var isBare = !isStar && Peek(1).Kind == SyntaxKind.ColonToken;
                sawStarExcept |= isStar;
                sawNormalExcept |= !isStar;

                var clause = ParseExceptClause(isStar);
                if (sawBareExcept)
                {
                    clause = WithDiagnostic(
                        clause,
                        SyntaxDiagnosticCode.InvalidExceptClause,
                        "default except clause must be last");
                }

                sawBareExcept |= isBare;
                exceptCount++;
                clauses.Add(clause);
            }

            if (Current.Kind == SyntaxKind.ElseKeyword)
            {
                var elseKeyword = EatToken();
                var elseColon = MatchToken(SyntaxKind.ColonToken);
                var elseBody = ParseSuite();
                GreenNode elseClause = GreenFactory.Node(
                    SyntaxKind.ElseClause,
                    elseKeyword,
                    elseColon,
                    elseBody);
                if (exceptCount == 0)
                {
                    elseClause = WithDiagnostic(
                        elseClause,
                        SyntaxDiagnosticCode.InvalidExceptClause,
                        "else clause requires at least one except clause");
                }
                clauses.Add(elseClause);
            }

            if (Current.Kind == SyntaxKind.FinallyKeyword)
            {
                var finallyKeyword = EatToken();
                var finallyColon = MatchToken(SyntaxKind.ColonToken);
                var finallyBody = ParseSuite();
                clauses.Add(GreenFactory.Node(
                    SyntaxKind.FinallyClause,
                    finallyKeyword,
                    finallyColon,
                    finallyBody));
            }

            var node = GreenFactory.Node(
                SyntaxKind.TryStatement,
                tryKeyword,
                colon,
                body,
                GreenFactory.List(clauses));

            if (clauses.Count == 0)
            {
                return WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidExceptClause,
                    "expected except or finally clause");
            }

            if (sawNormalExcept && sawStarExcept)
            {
                return WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidExceptClause,
                    "cannot mix except and except* clauses");
            }

            return node;
        }

        private GreenNode ParseExceptClause(bool isStar)
        {
            var exceptKeyword = EatToken();
            GreenToken? star = null;
            if (isStar)
                star = MatchToken(SyntaxKind.StarToken);

            GreenNode? exceptionType = null;
            if (Current.Kind != SyntaxKind.ColonToken)
                exceptionType = ParseExpressionSequenceUntilAsOrColon();
            else if (isStar)
                exceptionType = GreenFactory.MissingExpression("expected exception type after except*");

            GreenToken? asKeyword = null;
            GreenToken? name = null;
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                asKeyword = EatToken();
                name = MatchName();
            }

            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            GreenNode node = GreenFactory.Node(
                isStar ? SyntaxKind.ExceptStarClause : SyntaxKind.ExceptClause,
                exceptKeyword,
                star,
                exceptionType,
                asKeyword,
                name,
                colon,
                body);

            if (asKeyword is not null && exceptionType?.Kind == SyntaxKind.ExceptionTypeList)
            {
                node = WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidExceptClause,
                    "multiple unparenthesized exception types cannot use as");
            }

            return node;
        }

        private GreenNode ParseExpressionSequenceUntilAsOrColon()
        {
            var first = ParseExpression();
            if (Current.Kind != SyntaxKind.CommaToken)
                return first;

            var items = new List<GreenNode?> { first };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                if (Current.Kind is SyntaxKind.AsKeyword or SyntaxKind.ColonToken)
                    break;
                items.Add(ParseExpression());
            }

            return GreenFactory.Node(
                SyntaxKind.ExceptionTypeList,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseMatchStatement()
        {
            var matchKeyword = EatToken();
            var subject = ParseMatchSubjectExpression();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var newLine = MatchToken(SyntaxKind.NewLineToken);
            var indent = MatchToken(SyntaxKind.IndentToken);
            var cases = new List<GreenNode?>();

            while (Current.Kind is not SyntaxKind.DedentToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.NewLineToken)
                {
                    cases.Add(EatToken());
                }
                else if (IsSoftKeyword(Current, "case"))
                {
                    cases.Add(ParseCaseClause());
                }
                else
                {
                    cases.Add(ParseSkippedUntilCaseOrDedent());
                }

                EnsureProgress(start, cases);
            }

            var dedent = MatchToken(SyntaxKind.DedentToken);
            var node = GreenFactory.Node(
                SyntaxKind.MatchStatement,
                matchKeyword,
                subject,
                colon,
                newLine,
                indent,
                GreenFactory.List(cases),
                dedent);

            var hasCase = false;
            foreach (var item in cases)
            {
                if (item?.Kind == SyntaxKind.CaseClause)
                {
                    hasCase = true;
                    break;
                }
            }

            return hasCase
                ? node
                : WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.ExpectedCaseClause,
                    "expected at least one case clause");
        }

        private GreenNode ParseMatchSubjectExpression()
        {
            var first = ParseStarNamedExpression();
            if (Current.Kind != SyntaxKind.CommaToken &&
                !CanStartExpression(Current.Kind))
            {
                return first.Kind == SyntaxKind.StarredExpression
                    ? WithDiagnostic(
                        first,
                        SyntaxDiagnosticCode.InvalidMatchSubject,
                        "starred match subject must be part of a tuple")
                    : first;
            }

            var items = new List<GreenNode?> { first };
            while (Current.Kind != SyntaxKind.ColonToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.ColonToken)
                        break;
                }
                else if (CanStartExpression(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                items.Add(ParseStarNamedExpression());
                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.TupleExpression,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseCaseClause()
        {
            var caseKeyword = EatToken();
            var pattern = ParsePatterns();
            GreenToken? ifKeyword = null;
            GreenNode? guard = null;
            if (Current.Kind == SyntaxKind.IfKeyword)
            {
                ifKeyword = EatToken();
                guard = ParseNamedExpression();
            }

            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            return GreenFactory.Node(
                SyntaxKind.CaseClause,
                caseKeyword,
                pattern,
                ifKeyword,
                guard,
                colon,
                body);
        }

        private GreenNode ParseSkippedUntilCaseOrDedent()
        {
            var tokens = new List<GreenNode?>();
            while (Current.Kind is not SyntaxKind.EndOfFileToken &&
                   Current.Kind != SyntaxKind.DedentToken &&
                   !IsSoftKeyword(Current, "case"))
            {
                tokens.Add(EatToken());
            }

            if (tokens.Count == 0 && Current.Kind != SyntaxKind.EndOfFileToken)
                tokens.Add(EatToken());

            return GreenFactory.NodeWithDiagnostics(
                SyntaxKind.SkippedTokens,
                ImmutableArray.CreateRange(tokens),
                [
                    new GreenDiagnostic(
                        SyntaxDiagnosticCode.ExpectedCaseClause,
                        0,
                        SumWidth(tokens),
                        "expected case clause")
                ]);
        }

        private GreenNode ParseIfStatement()
        {
            var ifKeyword = EatToken();
            var condition = ParseNamedExpression();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            var clauses = new List<GreenNode?>();

            while (Current.Kind == SyntaxKind.ElifKeyword)
            {
                var elifKeyword = EatToken();
                var elifCondition = ParseNamedExpression();
                var elifColon = MatchToken(SyntaxKind.ColonToken);
                var elifBody = ParseSuite();
                clauses.Add(GreenFactory.Node(
                    SyntaxKind.ElifClause,
                    elifKeyword,
                    elifCondition,
                    elifColon,
                    elifBody));
            }

            if (Current.Kind == SyntaxKind.ElseKeyword)
            {
                var elseKeyword = EatToken();
                var elseColon = MatchToken(SyntaxKind.ColonToken);
                var elseBody = ParseSuite();
                clauses.Add(GreenFactory.Node(
                    SyntaxKind.ElseClause,
                    elseKeyword,
                    elseColon,
                    elseBody));
            }

            return GreenFactory.Node(
                SyntaxKind.IfStatement,
                ifKeyword,
                condition,
                colon,
                body,
                GreenFactory.List(clauses));
        }

        private GreenNode ParseWhileStatement()
        {
            var keyword = EatToken();
            var condition = ParseNamedExpression();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            GreenNode? elseClause = null;

            if (Current.Kind == SyntaxKind.ElseKeyword)
            {
                var elseKeyword = EatToken();
                var elseColon = MatchToken(SyntaxKind.ColonToken);
                var elseBody = ParseSuite();
                elseClause = GreenFactory.Node(
                    SyntaxKind.ElseClause,
                    elseKeyword,
                    elseColon,
                    elseBody);
            }

            return GreenFactory.Node(
                SyntaxKind.WhileStatement,
                keyword,
                condition,
                colon,
                body,
                elseClause);
        }

        private GreenNode ParseForStatement(GreenToken? asyncKeyword)
        {
            var forKeyword = MatchToken(SyntaxKind.ForKeyword);
            var targets = ParseTargetList(stopAtInKeyword: true);
            if (!IsAssignmentTarget(targets, allowSequence: true, allowStarredRoot: false))
            {
                targets = WithDiagnostic(
                    targets,
                    SyntaxDiagnosticCode.InvalidAssignmentTarget,
                    "invalid for-loop target");
            }
            var inKeyword = MatchToken(SyntaxKind.InKeyword);
            var source = ParseStarExpressions();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseSuite();
            GreenNode? elseClause = null;

            if (Current.Kind == SyntaxKind.ElseKeyword)
            {
                var elseKeyword = EatToken();
                var elseColon = MatchToken(SyntaxKind.ColonToken);
                var elseBody = ParseSuite();
                elseClause = GreenFactory.Node(
                    SyntaxKind.ElseClause,
                    elseKeyword,
                    elseColon,
                    elseBody);
            }

            return GreenFactory.Node(
                SyntaxKind.ForStatement,
                asyncKeyword,
                forKeyword,
                targets,
                inKeyword,
                source,
                colon,
                body,
                elseClause);
        }

        private GreenNode ParseSuite()
        {
            if (Current.Kind != SyntaxKind.NewLineToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.Suite,
                    ParseSimpleStatementList());
            }

            var newLine = EatToken();
            if (Current.Kind != SyntaxKind.IndentToken)
            {
                var node = GreenFactory.Node(
                    SyntaxKind.Suite,
                    newLine,
                    MissingToken(SyntaxKind.IndentToken),
                    GreenFactory.List(Array.Empty<GreenNode?>()),
                    MissingToken(SyntaxKind.DedentToken));
                return WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.ExpectedIndentedBlock,
                    "expected an indented block");
            }

            var indent = EatToken();
            var statements = new List<GreenNode?>();

            while (Current.Kind is not (
                SyntaxKind.DedentToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.NewLineToken)
                    statements.Add(EatToken());
                else
                    statements.Add(ParseStatement());

                EnsureProgress(start, statements);
            }

            var dedent = MatchToken(SyntaxKind.DedentToken);
            return GreenFactory.Node(
                SyntaxKind.Suite,
                newLine,
                indent,
                GreenFactory.List(statements),
                dedent);
        }

        private GreenNode ParsePatterns()
        {
            var first = Current.Kind == SyntaxKind.StarToken
                ? ParseStarPattern()
                : ParsePattern();

            if (Current.Kind != SyntaxKind.CommaToken)
                return first;

            var sawStar = first.Kind == SyntaxKind.StarPattern;
            var items = new List<GreenNode?> { first };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                if (Current.Kind is SyntaxKind.ColonToken or SyntaxKind.IfKeyword)
                    break;

                var item = Current.Kind == SyntaxKind.StarToken
                    ? ParseStarPattern()
                    : ParsePattern();
                if (item.Kind == SyntaxKind.StarPattern)
                {
                    if (sawStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "multiple starred names in sequence pattern");
                    }
                    sawStar = true;
                }
                items.Add(item);
            }

            return GreenFactory.Node(
                SyntaxKind.SequencePattern,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParsePattern()
        {
            var pattern = ParseOrPattern();
            if (Current.Kind != SyntaxKind.AsKeyword)
                return pattern;

            var asKeyword = EatToken();
            var target = ParsePatternCaptureTarget();
            return GreenFactory.Node(
                SyntaxKind.AsPattern,
                pattern,
                asKeyword,
                target);
        }

        private GreenNode ParseOrPattern()
        {
            var first = ParseClosedPattern();
            if (Current.Kind != SyntaxKind.PipeToken)
                return first;

            var items = new List<GreenNode?> { first };
            while (Current.Kind == SyntaxKind.PipeToken)
            {
                items.Add(EatToken());
                items.Add(ParseClosedPattern());
            }

            return GreenFactory.Node(
                SyntaxKind.OrPattern,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseClosedPattern()
        {
            if (Current.Kind is SyntaxKind.NumberToken or
                SyntaxKind.TrueKeyword or
                SyntaxKind.FalseKeyword or
                SyntaxKind.NoneKeyword)
            {
                return ParseLiteralPattern();
            }

            if (Current.Kind == SyntaxKind.MinusToken &&
                Peek(1).Kind == SyntaxKind.NumberToken)
            {
                return ParseLiteralPattern();
            }

            if (IsStringStart(Current.Kind))
            {
                return GreenFactory.Node(
                    SyntaxKind.LiteralPattern,
                    ParseStrings());
            }

            if (Current.Kind == SyntaxKind.IdentifierToken)
            {
                if (IsSoftKeyword(Current, "_"))
                {
                    return GreenFactory.Node(
                        SyntaxKind.WildcardPattern,
                        EatToken());
                }

                var name = EatToken();
                GreenNode value = GreenFactory.Node(
                    SyntaxKind.NameExpression,
                    name);

                var dotted = false;
                while (Current.Kind == SyntaxKind.DotToken)
                {
                    dotted = true;
                    value = GreenFactory.Node(
                        SyntaxKind.AttributeExpression,
                        value,
                        EatToken(),
                        MatchName());
                }

                if (Current.Kind == SyntaxKind.LeftParenthesisToken)
                    return ParseClassPattern(value);

                if (dotted)
                    return GreenFactory.Node(SyntaxKind.ValuePattern, value);

                return GreenFactory.Node(
                    SyntaxKind.CapturePattern,
                    name);
            }

            if (Current.Kind == SyntaxKind.LeftParenthesisToken)
                return ParseParenthesizedPattern();

            if (Current.Kind == SyntaxKind.LeftBracketToken)
                return ParseSequencePattern(SyntaxKind.LeftBracketToken, SyntaxKind.RightBracketToken);

            if (Current.Kind == SyntaxKind.LeftBraceToken)
                return ParseMappingPattern();

            if (IsPatternTerminator(Current.Kind))
                return GreenFactory.MissingPattern("expected pattern");

            return GreenFactory.ErrorPattern(
                EatToken(),
                "unexpected token in pattern");
        }

        private GreenNode ParseLiteralPattern()
        {
            var children = new List<GreenNode?>();
            if (Current.Kind == SyntaxKind.MinusToken)
                children.Add(EatToken());

            var first = EatToken();
            children.Add(first);
            var invalidComplex = false;

            if (first.Kind == SyntaxKind.NumberToken &&
                (Current.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken) &&
                Peek(1).Kind == SyntaxKind.NumberToken)
            {
                children.Add(EatToken());
                var imaginary = EatToken();
                children.Add(imaginary);
                invalidComplex = IsImaginaryNumber(first) ||
                    !IsImaginaryNumber(imaginary);
            }

            GreenNode node = GreenFactory.Node(
                SyntaxKind.LiteralPattern,
                [.. children]);
            return invalidComplex
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidPattern,
                    "complex pattern must combine a real and an imaginary number")
                : node;
        }

        private GreenNode ParseParenthesizedPattern()
        {
            var left = EatToken();
            if (Current.Kind == SyntaxKind.RightParenthesisToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.SequencePattern,
                    left,
                    GreenFactory.SeparatedList(Array.Empty<GreenNode?>()),
                    EatToken());
            }

            var first = Current.Kind == SyntaxKind.StarToken
                ? ParseStarPattern()
                : ParsePattern();

            if (Current.Kind != SyntaxKind.CommaToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.GroupPattern,
                    left,
                    first,
                    MatchToken(SyntaxKind.RightParenthesisToken));
            }

            var sawStar = first.Kind == SyntaxKind.StarPattern;
            var items = new List<GreenNode?> { first };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                if (Current.Kind == SyntaxKind.RightParenthesisToken)
                    break;

                var item = Current.Kind == SyntaxKind.StarToken
                    ? ParseStarPattern()
                    : ParsePattern();
                if (item.Kind == SyntaxKind.StarPattern)
                {
                    if (sawStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "multiple starred names in sequence pattern");
                    }
                    sawStar = true;
                }
                items.Add(item);
            }

            return GreenFactory.Node(
                SyntaxKind.SequencePattern,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightParenthesisToken));
        }

        private GreenNode ParseSequencePattern(
            SyntaxKind leftKind,
            SyntaxKind rightKind)
        {
            var left = MatchToken(leftKind);
            var items = new List<GreenNode?>();
            var sawStar = false;

            while (Current.Kind != rightKind &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;
                var item = Current.Kind == SyntaxKind.StarToken
                    ? ParseStarPattern()
                    : ParsePattern();
                if (item.Kind == SyntaxKind.StarPattern)
                {
                    if (sawStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "multiple starred names in sequence pattern");
                    }
                    sawStar = true;
                }
                items.Add(item);

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == rightKind)
                        break;
                }
                else if (CanStartPattern(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.SequencePattern,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(rightKind));
        }

        private GreenNode ParseStarPattern()
        {
            var star = EatToken();
            GreenNode target;
            if (Current.Kind == SyntaxKind.IdentifierToken &&
                IsSoftKeyword(Current, "_"))
            {
                target = GreenFactory.Node(
                    SyntaxKind.WildcardPattern,
                    EatToken());
            }
            else
            {
                target = ParsePatternCaptureTarget();
            }

            return GreenFactory.Node(
                SyntaxKind.StarPattern,
                star,
                target);
        }

        private GreenNode ParseMappingPattern()
        {
            var left = EatToken();
            var items = new List<GreenNode?>();
            var sawDoubleStar = false;

            while (Current.Kind is not SyntaxKind.RightBraceToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;
                GreenNode item;
                if (Current.Kind == SyntaxKind.DoubleStarToken)
                {
                    var doubleStar = EatToken();
                    var target = ParsePatternCaptureTarget();
                    item = GreenFactory.Node(
                        SyntaxKind.DoubleStarPattern,
                        doubleStar,
                        target);
                    if (sawDoubleStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "multiple ** rest patterns in mapping pattern");
                    }
                    sawDoubleStar = true;
                }
                else
                {
                    var key = ParsePatternKey();
                    var colon = MatchToken(SyntaxKind.ColonToken);
                    var value = ParsePattern();
                    item = GreenFactory.Node(
                        SyntaxKind.MappingPatternItem,
                        key,
                        colon,
                        value);
                    if (sawDoubleStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "mapping entries cannot follow ** rest pattern");
                    }
                }
                items.Add(item);

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightBraceToken)
                        break;
                }
                else if (CanStartPattern(Current.Kind) ||
                         Current.Kind == SyntaxKind.DoubleStarToken)
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.MappingPattern,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightBraceToken));
        }

        private GreenNode ParsePatternKey()
        {
            if (Current.Kind is SyntaxKind.NumberToken or
                SyntaxKind.TrueKeyword or
                SyntaxKind.FalseKeyword or
                SyntaxKind.NoneKeyword ||
                Current.Kind == SyntaxKind.MinusToken ||
                IsStringStart(Current.Kind))
            {
                var expression = ParseExpression();
                return IsValidPatternLiteralExpression(expression)
                    ? expression
                    : WithDiagnostic(
                        expression,
                        SyntaxDiagnosticCode.InvalidPattern,
                        "mapping pattern key must be a literal or dotted value");
            }

            if (Current.Kind == SyntaxKind.IdentifierToken)
            {
                GreenNode value = GreenFactory.Node(
                    SyntaxKind.NameExpression,
                    EatToken());
                var dotted = false;
                while (Current.Kind == SyntaxKind.DotToken)
                {
                    dotted = true;
                    value = GreenFactory.Node(
                        SyntaxKind.AttributeExpression,
                        value,
                        EatToken(),
                        MatchName());
                }

                return dotted
                    ? value
                    : WithDiagnostic(
                        value,
                        SyntaxDiagnosticCode.InvalidPattern,
                        "mapping pattern key must be a literal or dotted value");
            }

            if (IsPatternTerminator(Current.Kind))
                return GreenFactory.MissingExpression("expected mapping pattern key");

            return GreenFactory.ErrorExpression(
                EatToken(),
                "invalid mapping pattern key");
        }

        private GreenNode ParseClassPattern(GreenNode className)
        {
            var left = EatToken();
            var items = new List<GreenNode?>();
            var sawKeyword = false;
            var keywordNames = new HashSet<string>(StringComparer.Ordinal);

            while (Current.Kind is not SyntaxKind.RightParenthesisToken &&
                   Current.Kind != SyntaxKind.EndOfFileToken)
            {
                var start = _consumedTokenCount;
                GreenNode item;
                if (Current.Kind == SyntaxKind.IdentifierToken &&
                    Peek(1).Kind == SyntaxKind.EqualToken)
                {
                    sawKeyword = true;
                    var name = EatToken();
                    item = GreenFactory.Node(
                        SyntaxKind.KeywordPattern,
                        name,
                        EatToken(),
                        ParsePattern());
                    if (!keywordNames.Add(name.Text))
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "duplicate keyword pattern");
                    }
                }
                else
                {
                    item = ParsePattern();
                    if (sawKeyword)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidPattern,
                            "positional pattern follows keyword pattern");
                    }
                }

                items.Add(item);
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightParenthesisToken)
                        break;
                }
                else if (CanStartPattern(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.ClassPattern,
                className,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightParenthesisToken));
        }

        private GreenNode ParsePatternCaptureTarget()
        {
            var name = MatchName();
            GreenNode node = GreenFactory.Node(
                SyntaxKind.CapturePattern,
                name);

            if (string.Equals(name.Text, "_", StringComparison.Ordinal))
            {
                node = WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidPattern,
                    "wildcard cannot be used as a capture target");
            }

            if (Current.Kind == SyntaxKind.EqualToken)
            {
                node = WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidPattern,
                    "capture target cannot be followed by =");
            }

            return node;
        }

        private GreenNode ParseTargetList(bool stopAtInKeyword)
        {
            var items = new List<GreenNode?> { ParseTarget() };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                if (stopAtInKeyword && Current.Kind == SyntaxKind.InKeyword)
                    break;
                if (IsSimpleStatementTerminator(Current.Kind))
                    break;
                items.Add(ParseTarget());
            }

            return items.Count == 1
                ? items[0]!
                : GreenFactory.Node(
                    SyntaxKind.TupleExpression,
                    GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseTarget()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.StarredExpression,
                    EatToken(),
                    ParseTarget());
            }

            GreenNode expression;
            switch (Current.Kind)
            {
                case SyntaxKind.IdentifierToken:
                    expression = GreenFactory.Node(
                        SyntaxKind.NameExpression,
                        EatToken());
                    break;

                case SyntaxKind.LeftParenthesisToken:
                    {
                        var left = EatToken();
                        var inner = ParseTargetList(stopAtInKeyword: false);
                        var right = MatchToken(SyntaxKind.RightParenthesisToken);
                        expression = GreenFactory.Node(
                            SyntaxKind.ParenthesizedExpression,
                            left,
                            inner,
                            right);
                        break;
                    }

                case SyntaxKind.LeftBracketToken:
                    {
                        var left = EatToken();
                        var inner = Current.Kind == SyntaxKind.RightBracketToken
                            ? null
                            : ParseTargetList(stopAtInKeyword: false);
                        var right = MatchToken(SyntaxKind.RightBracketToken);
                        expression = GreenFactory.Node(
                            SyntaxKind.ListExpression,
                            left,
                            inner,
                            right);
                        break;
                    }

                default:
                    return ParseAtom();
            }

            while (true)
            {
                if (Current.Kind == SyntaxKind.DotToken)
                {
                    expression = GreenFactory.Node(
                        SyntaxKind.AttributeExpression,
                        expression,
                        EatToken(),
                        MatchName());
                    continue;
                }

                if (Current.Kind == SyntaxKind.LeftParenthesisToken)
                {
                    expression = GreenFactory.Node(
                        SyntaxKind.CallExpression,
                        expression,
                        ParseArgumentList());
                    continue;
                }

                if (Current.Kind == SyntaxKind.LeftBracketToken)
                {
                    var left = EatToken();
                    var slices = ParseSliceList();
                    var right = MatchToken(SyntaxKind.RightBracketToken);
                    expression = GreenFactory.Node(
                        SyntaxKind.SubscriptExpression,
                        expression,
                        left,
                        slices,
                        right);
                    continue;
                }

                break;
            }

            return expression;
        }

        private GreenNode ParseStarExpressions()
        {
            var first = ParseStarExpression();
            if (Current.Kind != SyntaxKind.CommaToken)
                return first;

            var items = new List<GreenNode?> { first };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                items.Add(EatToken());
                if (!CanStartExpression(Current.Kind))
                    break;

                items.Add(ParseStarExpression());
            }

            return GreenFactory.Node(
                SyntaxKind.TupleExpression,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseStarExpression()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.StarredExpression,
                    EatToken(),
                    ParseBitwiseOr());
            }

            return ParseExpression();
        }

        private GreenNode ParseStarNamedExpression()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.StarredExpression,
                    EatToken(),
                    ParseBitwiseOr());
            }

            return ParseNamedExpression();
        }

        private GreenNode ParseNamedExpression()
        {
            if (Current.Kind == SyntaxKind.IdentifierToken &&
                Peek(1).Kind == SyntaxKind.ColonEqualToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.NamedExpression,
                    EatToken(),
                    EatToken(),
                    ParseExpression());
            }

            return ParseExpression();
        }

        private GreenNode ParseExpression()
        {
            if (Current.Kind == SyntaxKind.LambdaKeyword)
                return ParseLambdaExpression();

            var condition = ParseDisjunction();
            if (Current.Kind != SyntaxKind.IfKeyword)
                return condition;

            var ifKeyword = EatToken();
            var test = ParseDisjunction();
            var elseKeyword = MatchToken(SyntaxKind.ElseKeyword);
            var alternative = ParseExpression();
            return GreenFactory.Node(
                SyntaxKind.ConditionalExpression,
                condition,
                ifKeyword,
                test,
                elseKeyword,
                alternative);
        }

        private GreenNode ParseLambdaExpression()
        {
            var lambdaKeyword = EatToken();
            var parameters = ParseLambdaParameterList();
            var colon = MatchToken(SyntaxKind.ColonToken);
            var body = ParseExpression();
            return GreenFactory.Node(
                SyntaxKind.LambdaExpression,
                lambdaKeyword,
                parameters,
                colon,
                body);
        }

        private GreenNode ParseLambdaParameterList()
        {
            var children = new List<GreenNode?>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sawParameter = false;
            var sawSlash = false;
            var sawStar = false;
            var sawDoubleStar = false;
            var sawPositionalDefault = false;
            var bareStarNeedsFollowingParameter = false;

            while (Current.Kind is not (
                SyntaxKind.ColonToken or
                SyntaxKind.NewLineToken or
                SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                GreenNode item;

                if (Current.Kind == SyntaxKind.SlashToken)
                {
                    item = EatToken();
                    if (sawSlash || !sawParameter || sawStar || sawDoubleStar)
                    {
                        item = WithDiagnostic(
                            item,
                            SyntaxDiagnosticCode.InvalidParameter,
                            "invalid positional-only marker in lambda parameter list");
                    }
                    sawSlash = true;
                }
                else
                {
                    GreenToken? prefix = null;
                    if (Current.Kind is SyntaxKind.StarToken or SyntaxKind.DoubleStarToken)
                        prefix = EatToken();

                    var isBareStar = prefix?.Kind == SyntaxKind.StarToken &&
                        (Current.Kind is SyntaxKind.CommaToken or SyntaxKind.ColonToken);
                    if (isBareStar)
                    {
                        item = prefix!;
                        if (sawStar || sawDoubleStar)
                        {
                            item = WithDiagnostic(
                                item,
                                SyntaxDiagnosticCode.InvalidParameter,
                                "duplicate * parameter marker");
                        }
                        sawStar = true;
                        bareStarNeedsFollowingParameter = true;
                    }
                    else
                    {
                        var name = MatchName();
                        GreenToken? equal = null;
                        GreenNode? defaultValue = null;
                        if (Current.Kind == SyntaxKind.EqualToken)
                        {
                            equal = EatToken();
                            defaultValue = ParseExpression();
                        }

                        item = GreenFactory.Node(
                            SyntaxKind.Parameter,
                            prefix,
                            name,
                            equal,
                            defaultValue);

                        if (sawDoubleStar)
                        {
                            item = WithDiagnostic(
                                item,
                                SyntaxDiagnosticCode.InvalidParameter,
                                "no parameter may follow a ** parameter");
                        }

                        if (prefix?.Kind == SyntaxKind.StarToken)
                        {
                            if (sawStar || sawDoubleStar)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "duplicate * parameter");
                            }
                            if (equal is not null)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "* parameter cannot have a default value");
                            }
                            sawStar = true;
                            bareStarNeedsFollowingParameter = false;
                        }
                        else if (prefix?.Kind == SyntaxKind.DoubleStarToken)
                        {
                            if (sawDoubleStar)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "duplicate ** parameter");
                            }
                            if (equal is not null)
                            {
                                item = WithDiagnostic(
                                    item,
                                    SyntaxDiagnosticCode.InvalidParameter,
                                    "** parameter cannot have a default value");
                            }
                            sawDoubleStar = true;
                        }
                        else
                        {
                            sawParameter = true;
                            if (!sawStar && !sawDoubleStar)
                            {
                                if (equal is not null)
                                    sawPositionalDefault = true;
                                else if (sawPositionalDefault)
                                {
                                    item = WithDiagnostic(
                                        item,
                                        SyntaxDiagnosticCode.InvalidParameter,
                                        "non-default parameter follows default parameter");
                                }
                            }
                            if (bareStarNeedsFollowingParameter)
                                bareStarNeedsFollowingParameter = false;
                        }
                    }
                }

                children.Add(ValidateParameterName(item, names));
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    children.Add(EatToken());
                    if (Current.Kind == SyntaxKind.ColonToken)
                        break;
                }
                else if (CanStartParameter(Current.Kind))
                {
                    children.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, children);
            }

            GreenNode node = GreenFactory.Node(
                SyntaxKind.ParameterList,
                GreenFactory.SeparatedList(children));
            return bareStarNeedsFollowingParameter
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidParameter,
                    "named arguments must follow bare *")
                : node;
        }

        private GreenNode ParseDisjunction() =>
            ParseLeftAssociative(
                ParseConjunction,
                static kind => kind == SyntaxKind.OrKeyword,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseConjunction() =>
            ParseLeftAssociative(
                ParseInversion,
                static kind => kind == SyntaxKind.AndKeyword,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseInversion()
        {
            if (Current.Kind == SyntaxKind.NotKeyword)
            {
                return GreenFactory.Node(
                    SyntaxKind.UnaryExpression,
                    EatToken(),
                    ParseInversion());
            }

            return ParseComparison();
        }

        private GreenNode ParseComparison()
        {
            var left = ParseBitwiseOr();
            if (!IsComparisonStart())
                return left;

            var children = new List<GreenNode?> { left };
            while (IsComparisonStart())
            {
                if (Current.Kind == SyntaxKind.NotKeyword &&
                    Peek(1).Kind == SyntaxKind.InKeyword)
                {
                    children.Add(EatToken());
                    children.Add(EatToken());
                }
                else if (Current.Kind == SyntaxKind.IsKeyword &&
                         Peek(1).Kind == SyntaxKind.NotKeyword)
                {
                    children.Add(EatToken());
                    children.Add(EatToken());
                }
                else
                {
                    children.Add(EatToken());
                }

                children.Add(ParseBitwiseOr());
            }

            return GreenFactory.Node(SyntaxKind.ComparisonExpression, [.. children]);
        }

        private GreenNode ParseBitwiseOr() =>
            ParseLeftAssociative(
                ParseBitwiseXor,
                static kind => kind == SyntaxKind.PipeToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseBitwiseXor() =>
            ParseLeftAssociative(
                ParseBitwiseAnd,
                static kind => kind == SyntaxKind.CaretToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseBitwiseAnd() =>
            ParseLeftAssociative(
                ParseShiftExpression,
                static kind => kind == SyntaxKind.AmpersandToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseShiftExpression() =>
            ParseLeftAssociative(
                ParseSum,
                static kind => kind is SyntaxKind.LeftShiftToken or SyntaxKind.RightShiftToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseSum() =>
            ParseLeftAssociative(
                ParseTerm,
                static kind => kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseTerm() =>
            ParseLeftAssociative(
                ParseFactor,
                static kind => kind is
                    SyntaxKind.StarToken or
                    SyntaxKind.SlashToken or
                    SyntaxKind.DoubleSlashToken or
                    SyntaxKind.PercentToken or
                    SyntaxKind.AtToken,
                SyntaxKind.BinaryExpression);

        private GreenNode ParseFactor()
        {
            if (Current.Kind is
                SyntaxKind.PlusToken or
                SyntaxKind.MinusToken or
                SyntaxKind.TildeToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.UnaryExpression,
                    EatToken(),
                    ParseFactor());
            }

            return ParsePower();
        }

        private GreenNode ParsePower()
        {
            var left = ParseAwaitPrimary();
            if (Current.Kind != SyntaxKind.DoubleStarToken)
                return left;

            return GreenFactory.Node(
                SyntaxKind.BinaryExpression,
                left,
                EatToken(),
                ParseFactor());
        }

        private GreenNode ParseAwaitPrimary()
        {
            if (Current.Kind == SyntaxKind.AwaitKeyword)
            {
                return GreenFactory.Node(
                    SyntaxKind.AwaitExpression,
                    EatToken(),
                    ParsePrimary());
            }

            return ParsePrimary();
        }

        private GreenNode ParsePrimary()
        {
            var expression = ParseAtom();

            while (true)
            {
                switch (Current.Kind)
                {
                    case SyntaxKind.DotToken:
                        expression = GreenFactory.Node(
                            SyntaxKind.AttributeExpression,
                            expression,
                            EatToken(),
                            MatchName());
                        continue;

                    case SyntaxKind.LeftParenthesisToken:
                        expression = GreenFactory.Node(
                            SyntaxKind.CallExpression,
                            expression,
                            ParseArgumentList());
                        continue;

                    case SyntaxKind.LeftBracketToken:
                        {
                            var left = EatToken();
                            var slices = ParseSliceList();
                            var right = MatchToken(SyntaxKind.RightBracketToken);
                            expression = GreenFactory.Node(
                                SyntaxKind.SubscriptExpression,
                                expression,
                                left,
                                slices,
                                right);
                            continue;
                        }
                }

                return expression;
            }
        }

        private GreenNode ParseAtom()
        {
            switch (Current.Kind)
            {
                case SyntaxKind.IdentifierToken:
                    return GreenFactory.Node(SyntaxKind.NameExpression, EatToken());

                case SyntaxKind.NumberToken:
                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                case SyntaxKind.NoneKeyword:
                case SyntaxKind.EllipsisToken:
                    return GreenFactory.Node(SyntaxKind.LiteralExpression, EatToken());

                case SyntaxKind.StringToken:
                case SyntaxKind.FStringStartToken:
                case SyntaxKind.TStringStartToken:
                    return ParseStrings();

                case SyntaxKind.LeftParenthesisToken:
                    return ParseParenthesizedOrTupleExpression();

                case SyntaxKind.LeftBracketToken:
                    return ParseListExpression();

                case SyntaxKind.LeftBraceToken:
                    return ParseBraceExpression();

            }

            if (IsExpressionTerminator(Current.Kind))
                return GreenFactory.MissingExpression("expected expression");

            var unexpected = EatToken();
            return GreenFactory.ErrorExpression(
                unexpected,
                $"unexpected token '{Display(unexpected.Kind)}' in expression");
        }

        private GreenNode ParseStrings()
        {
            var items = new List<GreenNode?>();
            var sawTemplate = false;
            var sawNonTemplate = false;

            while (IsStringStart(Current.Kind))
            {
                if (Current.Kind == SyntaxKind.StringToken)
                {
                    sawNonTemplate = true;
                    items.Add(GreenFactory.Node(
                        SyntaxKind.LiteralExpression,
                        EatToken()));
                }
                else
                {
                    var isTemplate = Current.Kind == SyntaxKind.TStringStartToken;
                    sawTemplate |= isTemplate;
                    sawNonTemplate |= !isTemplate;
                    items.Add(ParseFormattedString());
                }
            }

            GreenNode node = items.Count == 1
                ? items[0]!
                : GreenFactory.Node(
                    sawTemplate
                        ? SyntaxKind.TStringExpression
                        : SyntaxKind.StringConcatenationExpression,
                    GreenFactory.List(items));

            return sawTemplate && sawNonTemplate
                ? WithDiagnostic(
                    node,
                    SyntaxDiagnosticCode.InvalidStringConcatenation,
                    "template strings cannot be concatenated with string or formatted string literals")
                : node;
        }

        private GreenNode ParseFormattedString()
        {
            var isTemplate = Current.Kind == SyntaxKind.TStringStartToken;
            var startKind = isTemplate
                ? SyntaxKind.TStringStartToken
                : SyntaxKind.FStringStartToken;
            var middleKind = isTemplate
                ? SyntaxKind.TStringMiddleToken
                : SyntaxKind.FStringMiddleToken;
            var endKind = isTemplate
                ? SyntaxKind.TStringEndToken
                : SyntaxKind.FStringEndToken;

            var start = MatchToken(startKind);
            var parts = new List<GreenNode?>();

            while (Current.Kind is not SyntaxKind.EndOfFileToken &&
                   Current.Kind != endKind)
            {
                var before = _consumedTokenCount;
                if (Current.Kind == middleKind)
                {
                    parts.Add(EatToken());
                }
                else if (Current.Kind == SyntaxKind.LeftBraceToken)
                {
                    parts.Add(ParseInterpolation(isTemplate, inFormatSpec: false));
                }
                else
                {
                    parts.Add(GreenFactory.ErrorExpression(
                        EatToken(),
                        "unexpected token in formatted string"));
                }

                EnsureProgress(before, parts);
            }

            var end = MatchToken(endKind);
            return GreenFactory.Node(
                isTemplate ? SyntaxKind.TStringExpression : SyntaxKind.FStringExpression,
                start,
                GreenFactory.List(parts),
                end);
        }

        private GreenNode ParseInterpolation(bool isTemplate, bool inFormatSpec)
        {
            var leftBrace = EatToken();
            var expression = Current.Kind == SyntaxKind.YieldKeyword
                ? ParseYieldExpression()
                : ParseStarExpressions();

            GreenToken? debugEqual = null;
            if (Current.Kind == SyntaxKind.EqualToken)
                debugEqual = EatToken();

            GreenNode? conversion = null;
            if (Current.Kind == SyntaxKind.ExclamationToken)
            {
                var exclamation = EatToken();
                var conversionName = MatchName();
                conversion = GreenFactory.Node(
                    SyntaxKind.ConversionClause,
                    exclamation,
                    conversionName);

                if (!conversionName.IsMissing &&
                    conversionName.Text is not ("s" or "r" or "a"))
                {
                    conversion = WithDiagnostic(
                        conversion,
                        SyntaxDiagnosticCode.InvalidInterpolation,
                        "conversion must be one of !s, !r, or !a");
                }
            }

            GreenNode? formatSpec = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                var colon = EatToken();
                var parts = new List<GreenNode?>();
                var middleKind = isTemplate
                    ? SyntaxKind.TStringMiddleToken
                    : SyntaxKind.FStringMiddleToken;

                while (Current.Kind is not (
                    SyntaxKind.RightBraceToken or SyntaxKind.EndOfFileToken))
                {
                    var before = _consumedTokenCount;
                    if (Current.Kind == middleKind)
                    {
                        parts.Add(EatToken());
                    }
                    else if (Current.Kind == SyntaxKind.LeftBraceToken)
                    {
                        parts.Add(ParseInterpolation(isTemplate, inFormatSpec: true));
                    }
                    else
                    {
                        parts.Add(GreenFactory.ErrorExpression(
                            EatToken(),
                            "unexpected token in formatted string format specifier"));
                    }

                    EnsureProgress(before, parts);
                }

                formatSpec = GreenFactory.Node(
                    SyntaxKind.FormatSpecClause,
                    colon,
                    GreenFactory.List(parts));
            }

            var rightBrace = MatchToken(SyntaxKind.RightBraceToken);
            return GreenFactory.Node(
                SyntaxKind.Interpolation,
                leftBrace,
                expression,
                debugEqual,
                conversion,
                formatSpec,
                rightBrace);
        }

        private GreenNode ParseParenthesizedOrTupleExpression()
        {
            var left = EatToken();
            if (Current.Kind == SyntaxKind.RightParenthesisToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.TupleExpression,
                    left,
                    MatchToken(SyntaxKind.RightParenthesisToken));
            }

            GreenNode expression = Current.Kind == SyntaxKind.YieldKeyword
                ? ParseYieldExpression()
                : ParseStarNamedExpression();

            if (IsComprehensionStart())
            {
                expression = ValidateComprehensionElement(expression);
                var clauses = ParseComprehensionClauses();
                return GreenFactory.Node(
                    SyntaxKind.GeneratorExpression,
                    left,
                    expression,
                    clauses,
                    MatchToken(SyntaxKind.RightParenthesisToken));
            }

            if (Current.Kind != SyntaxKind.CommaToken &&
                !CanStartExpression(Current.Kind))
            {
                if (expression.Kind == SyntaxKind.StarredExpression)
                {
                    expression = WithDiagnostic(
                        expression,
                        SyntaxDiagnosticCode.InvalidExpression,
                        "cannot use starred expression here");
                }

                var right = MatchToken(SyntaxKind.RightParenthesisToken);
                return GreenFactory.Node(
                    SyntaxKind.ParenthesizedExpression,
                    left,
                    expression,
                    right);
            }

            var items = new List<GreenNode?> { expression };
            while (Current.Kind is not (
                SyntaxKind.RightParenthesisToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightParenthesisToken)
                        break;
                }
                else if (CanStartExpression(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                items.Add(ParseStarNamedExpression());
                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.TupleExpression,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightParenthesisToken));
        }

        private GreenNode ParseListExpression()
        {
            var left = EatToken();
            if (Current.Kind == SyntaxKind.RightBracketToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.ListExpression,
                    left,
                    GreenFactory.SeparatedList(Array.Empty<GreenNode?>()),
                    EatToken());
            }

            var first = ParseStarNamedExpression();
            if (IsComprehensionStart())
            {
                first = ValidateComprehensionElement(first);
                return GreenFactory.Node(
                    SyntaxKind.ListComprehensionExpression,
                    left,
                    first,
                    ParseComprehensionClauses(),
                    MatchToken(SyntaxKind.RightBracketToken));
            }

            var items = new List<GreenNode?> { first };
            while (Current.Kind is not (
                SyntaxKind.RightBracketToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightBracketToken)
                        break;
                    items.Add(ParseStarNamedExpression());
                }
                else if (CanStartExpression(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                    items.Add(ParseStarNamedExpression());
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.ListExpression,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightBracketToken));
        }

        private GreenNode ParseBraceExpression()
        {
            var left = EatToken();
            if (Current.Kind == SyntaxKind.RightBraceToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.DictionaryExpression,
                    left,
                    EatToken());
            }

            if (Current.Kind == SyntaxKind.DoubleStarToken)
                return ParseDictionaryExpression(left, firstItem: null);

            var first = ParseStarNamedExpression();
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                var pair = ParseKeyValuePair(first);
                if (IsComprehensionStart())
                {
                    return GreenFactory.Node(
                        SyntaxKind.DictionaryComprehensionExpression,
                        left,
                        pair,
                        ParseComprehensionClauses(),
                        MatchToken(SyntaxKind.RightBraceToken));
                }

                return ParseDictionaryExpression(left, pair);
            }

            if (IsComprehensionStart())
            {
                first = ValidateComprehensionElement(first);
                return GreenFactory.Node(
                    SyntaxKind.SetComprehensionExpression,
                    left,
                    first,
                    ParseComprehensionClauses(),
                    MatchToken(SyntaxKind.RightBraceToken));
            }

            var items = new List<GreenNode?> { first };
            while (Current.Kind is not (
                SyntaxKind.RightBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightBraceToken)
                        break;
                    items.Add(ParseStarNamedExpression());
                }
                else if (CanStartExpression(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                    items.Add(ParseStarNamedExpression());
                }
                else
                {
                    break;
                }
                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.SetExpression,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightBraceToken));
        }

        private GreenNode ParseDictionaryExpression(
            GreenToken left,
            GreenNode? firstItem)
        {
            var items = new List<GreenNode?>();
            if (firstItem is not null)
                items.Add(firstItem);

            var needsSeparator = firstItem is not null;
            while (Current.Kind is not (
                SyntaxKind.RightBraceToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                if (needsSeparator)
                {
                    if (Current.Kind == SyntaxKind.CommaToken)
                    {
                        items.Add(EatToken());
                        if (Current.Kind == SyntaxKind.RightBraceToken)
                            break;
                    }
                    else if (Current.Kind == SyntaxKind.DoubleStarToken ||
                             CanStartExpression(Current.Kind))
                    {
                        items.Add(MissingToken(SyntaxKind.CommaToken));
                    }
                    else
                    {
                        break;
                    }
                }

                if (Current.Kind == SyntaxKind.DoubleStarToken)
                {
                    items.Add(GreenFactory.Node(
                        SyntaxKind.DoubleStarredExpression,
                        EatToken(),
                        ParseBitwiseOr()));
                }
                else
                {
                    var key = ParseExpression();
                    items.Add(ParseKeyValuePair(key));
                }

                needsSeparator = true;
                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.DictionaryExpression,
                left,
                GreenFactory.SeparatedList(items),
                MatchToken(SyntaxKind.RightBraceToken));
        }

        private GreenNode ParseComprehensionClauses()
        {
            var clauses = new List<GreenNode?>();
            while (IsComprehensionStart())
            {
                GreenToken? asyncKeyword = null;
                if (Current.Kind == SyntaxKind.AsyncKeyword)
                    asyncKeyword = EatToken();

                var forKeyword = MatchToken(SyntaxKind.ForKeyword);
                var target = ParseTargetList(stopAtInKeyword: true);
                if (!IsAssignmentTarget(target, allowSequence: true, allowStarredRoot: false))
                {
                    target = WithDiagnostic(
                        target,
                        SyntaxDiagnosticCode.InvalidAssignmentTarget,
                        "invalid comprehension target");
                }
                var inKeyword = MatchToken(SyntaxKind.InKeyword);
                var source = ParseDisjunction();
                clauses.Add(GreenFactory.Node(
                    SyntaxKind.ForComprehensionClause,
                    asyncKeyword,
                    forKeyword,
                    target,
                    inKeyword,
                    source));

                while (Current.Kind == SyntaxKind.IfKeyword)
                {
                    clauses.Add(GreenFactory.Node(
                        SyntaxKind.IfComprehensionClause,
                        EatToken(),
                        ParseDisjunction()));
                }
            }

            return GreenFactory.Node(
                SyntaxKind.ComprehensionClauseList,
                GreenFactory.List(clauses));
        }

        private GreenNode ParseKeyValuePair(GreenNode key)
        {
            if (key.Kind is
                SyntaxKind.NamedExpression or
                SyntaxKind.StarredExpression or
                SyntaxKind.YieldExpression)
            {
                key = WithDiagnostic(
                    key,
                    SyntaxDiagnosticCode.InvalidDictionaryItem,
                    key.Kind switch
                    {
                        SyntaxKind.NamedExpression =>
                            "assignment expression in a dictionary key must be parenthesized",
                        SyntaxKind.StarredExpression =>
                            "iterable unpacking cannot be used as a dictionary key",
                        _ => "yield expression in a dictionary key must be parenthesized",
                    });
            }

            var colon = MatchToken(SyntaxKind.ColonToken);
            var value = ParseExpression();
            return GreenFactory.Node(SyntaxKind.KeyValuePair, key, colon, value);
        }

        private GreenNode ParseArgumentList()
        {
            var left = EatToken();
            var arguments = new List<GreenNode?>();
            var argumentCount = 0;
            var sawKeywordArgument = false;
            var sawKeywordUnpacking = false;
            var keywordNames = new HashSet<string>(StringComparer.Ordinal);

            while (Current.Kind is not (
                SyntaxKind.RightParenthesisToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                var argument = ParseArgument();

                if (IsUnparenthesizedGeneratorArgument(argument) &&
                    (argumentCount != 0 || Current.Kind == SyntaxKind.CommaToken))
                {
                    argument = WithDiagnostic(
                        argument,
                        SyntaxDiagnosticCode.InvalidArgument,
                        "generator expression must be parenthesized");
                }

                switch (argument.Kind)
                {
                    case SyntaxKind.KeywordArgument:
                        sawKeywordArgument = true;
                        if (argument.GetSlot(0) is GreenToken { IsMissing: false } keywordName &&
                            !keywordNames.Add(keywordName.Text))
                        {
                            argument = WithDiagnostic(
                                argument,
                                SyntaxDiagnosticCode.InvalidArgument,
                                $"keyword argument repeated: {keywordName.Text}");
                        }
                        break;

                    case SyntaxKind.DoubleStarredExpression:
                        sawKeywordUnpacking = true;
                        break;

                    case SyntaxKind.StarredExpression:
                        if (sawKeywordUnpacking)
                        {
                            argument = WithDiagnostic(
                                argument,
                                SyntaxDiagnosticCode.InvalidArgument,
                                "iterable argument unpacking follows keyword argument unpacking");
                        }
                        break;

                    default:
                        if (sawKeywordUnpacking)
                        {
                            argument = WithDiagnostic(
                                argument,
                                SyntaxDiagnosticCode.InvalidArgument,
                                "positional argument follows keyword argument unpacking");
                        }
                        else if (sawKeywordArgument)
                        {
                            argument = WithDiagnostic(
                                argument,
                                SyntaxDiagnosticCode.InvalidArgument,
                                "positional argument follows keyword argument");
                        }
                        break;
                }

                arguments.Add(argument);
                argumentCount++;

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    arguments.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightParenthesisToken)
                        break;
                }
                else if (CanStartArgument(Current.Kind))
                {
                    arguments.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, arguments);
            }

            return GreenFactory.Node(
                SyntaxKind.ArgumentList,
                left,
                GreenFactory.SeparatedList(arguments),
                MatchToken(SyntaxKind.RightParenthesisToken));
        }

        private GreenNode ParseArgument()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                GreenNode argument = GreenFactory.Node(
                    SyntaxKind.StarredExpression,
                    EatToken(),
                    ParseExpression());
                argument = RecoverInvalidArgumentAssignment(argument);
                return RecoverInvalidComprehensionArgument(
                    argument,
                    "iterable unpacking cannot be used in a comprehension");
            }

            if (Current.Kind == SyntaxKind.DoubleStarToken)
            {
                GreenNode argument = GreenFactory.Node(
                    SyntaxKind.DoubleStarredExpression,
                    EatToken(),
                    ParseExpression());
                argument = RecoverInvalidArgumentAssignment(argument);
                return RecoverInvalidComprehensionArgument(
                    argument,
                    "keyword unpacking cannot be used in a comprehension");
            }

            if (Current.Kind == SyntaxKind.IdentifierToken &&
                Peek(1).Kind == SyntaxKind.EqualToken)
            {
                GreenNode argument = GreenFactory.Node(
                    SyntaxKind.KeywordArgument,
                    EatToken(),
                    EatToken(),
                    ParseExpression());
                argument = RecoverInvalidArgumentAssignment(argument);
                return RecoverInvalidComprehensionArgument(
                    argument,
                    "keyword argument cannot be used in a comprehension");
            }

            var expression = RecoverInvalidArgumentAssignment(ParseNamedExpression());
            if (!IsComprehensionStart())
                return expression;

            return GreenFactory.Node(
                SyntaxKind.GeneratorExpression,
                expression,
                ParseComprehensionClauses());
        }

        private GreenNode RecoverInvalidArgumentAssignment(GreenNode argument)
        {
            if (Current.Kind != SyntaxKind.EqualToken)
                return argument;

            var children = new List<GreenNode?> { argument };
            do
            {
                children.Add(EatToken());
                children.Add(ParseExpression());
            }
            while (Current.Kind == SyntaxKind.EqualToken);

            var message = argument.Kind switch
            {
                SyntaxKind.DoubleStarredExpression =>
                    "cannot assign to keyword argument unpacking",
                SyntaxKind.StarredExpression =>
                    "cannot assign to iterable argument unpacking",
                _ => "expression cannot contain assignment; perhaps you meant '=='?",
            };

            return WithDiagnostic(
                GreenFactory.Node(SyntaxKind.ErrorExpression, [.. children]),
                SyntaxDiagnosticCode.InvalidArgument,
                message);
        }

        private GreenNode RecoverInvalidComprehensionArgument(
            GreenNode argument,
            string message)
        {
            if (!IsComprehensionStart())
                return argument;

            return WithDiagnostic(
                GreenFactory.Node(
                    SyntaxKind.GeneratorExpression,
                    argument,
                    ParseComprehensionClauses()),
                SyntaxDiagnosticCode.InvalidComprehension,
                message);
        }

        private GreenNode ParseSliceList()
        {
            var items = new List<GreenNode?>();
            if (Current.Kind == SyntaxKind.RightBracketToken)
            {
                return WithDiagnostic(
                    GreenFactory.Node(
                        SyntaxKind.SliceList,
                        GreenFactory.SeparatedList(items)),
                    SyntaxDiagnosticCode.ExpectedExpression,
                    "expected subscript expression");
            }

            while (Current.Kind is not (
                SyntaxKind.RightBracketToken or SyntaxKind.EndOfFileToken))
            {
                var start = _consumedTokenCount;
                items.Add(ParseSlice());

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    items.Add(EatToken());
                    if (Current.Kind == SyntaxKind.RightBracketToken)
                        break;
                }
                else if (CanStartSlice(Current.Kind))
                {
                    items.Add(MissingToken(SyntaxKind.CommaToken));
                }
                else
                {
                    break;
                }

                EnsureProgress(start, items);
            }

            return GreenFactory.Node(
                SyntaxKind.SliceList,
                GreenFactory.SeparatedList(items));
        }

        private GreenNode ParseSlice()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                return GreenFactory.Node(
                    SyntaxKind.StarredExpression,
                    EatToken(),
                    ParseExpression());
            }

            if (Current.Kind == SyntaxKind.IdentifierToken &&
                Peek(1).Kind == SyntaxKind.ColonEqualToken)
            {
                return ParseNamedExpression();
            }

            GreenNode? lower = null;
            if (Current.Kind != SyntaxKind.ColonToken)
                lower = ParseExpression();

            if (Current.Kind != SyntaxKind.ColonToken)
                return lower ?? GreenFactory.MissingExpression("expected subscript expression");

            var firstColon = EatToken();
            GreenNode? upper = null;
            if (Current.Kind is not (
                SyntaxKind.ColonToken or
                SyntaxKind.CommaToken or
                SyntaxKind.RightBracketToken))
            {
                upper = ParseExpression();
            }

            GreenToken? secondColon = null;
            GreenNode? step = null;
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                secondColon = EatToken();
                if (Current.Kind is not (
                    SyntaxKind.CommaToken or SyntaxKind.RightBracketToken))
                {
                    step = ParseExpression();
                }
            }

            return GreenFactory.Node(
                SyntaxKind.SliceExpression,
                lower,
                firstColon,
                upper,
                secondColon,
                step);
        }

        private GreenNode ParseYieldExpression()
        {
            var yieldKeyword = MatchToken(SyntaxKind.YieldKeyword);
            GreenToken? fromKeyword = null;
            GreenNode? value = null;

            if (Current.Kind == SyntaxKind.FromKeyword)
            {
                fromKeyword = EatToken();
                value = ParseExpression();
            }
            else if (!IsExpressionTerminator(Current.Kind))
            {
                value = ParseStarExpressions();
            }

            return GreenFactory.Node(
                SyntaxKind.YieldExpression,
                yieldKeyword,
                fromKeyword,
                value);
        }

        private GreenNode ParseLeftAssociative(
            Func<GreenNode> parseOperand,
            Func<SyntaxKind, bool> isOperator,
            SyntaxKind nodeKind)
        {
            var left = parseOperand();
            while (isOperator(Current.Kind))
            {
                left = GreenFactory.Node(
                    nodeKind,
                    left,
                    EatToken(),
                    parseOperand());
            }

            return left;
        }

        private GreenToken EatToken()
        {
            var token = Current;
            _consumedTokenCount = checked(_consumedTokenCount + 1);

            if (_lookaheadStart < _lookahead.Count)
                _lookaheadStart++;

            CompactLookahead();
            return token;
        }

        private void CompactLookahead()
        {
            if (_lookaheadStart < LookaheadCompactionThreshold ||
                _lookaheadStart < (_lookahead.Count + 1) / 2)
            {
                return;
            }

            _lookahead.RemoveRange(0, _lookaheadStart);
            _lookaheadStart = 0;
        }

        private GreenToken MatchToken(SyntaxKind kind) =>
            Current.Kind == kind
                ? EatToken()
                : MissingToken(kind);

        private GreenToken MatchName() =>
            Current.Kind == SyntaxKind.IdentifierToken
                ? EatToken()
                : GreenToken.Missing(SyntaxKind.IdentifierToken, "expected name");

        private static GreenToken MissingToken(SyntaxKind kind) =>
            GreenToken.Missing(kind, $"expected '{Display(kind)}'");

        private GreenNode ParseSkippedTokens(Func<SyntaxKind, bool> shouldConsume)
        {
            var tokens = new List<GreenNode?>();
            while (Current.Kind != SyntaxKind.EndOfFileToken &&
                   shouldConsume(Current.Kind))
            {
                tokens.Add(EatToken());
            }

            if (tokens.Count == 0 && Current.Kind != SyntaxKind.EndOfFileToken)
                tokens.Add(EatToken());

            return GreenFactory.NodeWithDiagnostics(
                SyntaxKind.SkippedTokens,
                ImmutableArray.CreateRange(tokens),
                [
                    new GreenDiagnostic(
                    SyntaxDiagnosticCode.UnexpectedToken,
                    0,
                    tokens.Count == 0 ? 0 : SumWidth(tokens),
                    "unexpected token sequence")
                ]);
        }

        private void EnsureProgress(int start, List<GreenNode?> destination)
        {
            if (_consumedTokenCount != start || Current.Kind == SyntaxKind.EndOfFileToken)
                return;

            destination.Add(GreenFactory.Node(
                SyntaxKind.SkippedTokens,
                EatToken()));
        }

        private static int SumWidth(List<GreenNode?> nodes)
        {
            var result = 0;
            foreach (var node in nodes)
            {
                if (node is not null)
                    result = checked(result + node.FullWidth);
            }
            return result;
        }

        private bool IsMatchStatementStart()
        {
            if (!IsSoftKeyword(Current, "match"))
                return false;

            if (Peek(1).Kind is
                SyntaxKind.ColonToken or
                SyntaxKind.DotToken or
                SyntaxKind.LeftBracketToken or
                SyntaxKind.EqualToken or
                SyntaxKind.ColonEqualToken)
            {
                return false;
            }

            var depth = 0;
            for (var offset = 1; ; offset++)
            {
                var kind = Peek(offset).Kind;
                switch (kind)
                {
                    case SyntaxKind.LeftParenthesisToken:
                    case SyntaxKind.LeftBracketToken:
                    case SyntaxKind.LeftBraceToken:
                        depth++;
                        break;

                    case SyntaxKind.RightParenthesisToken:
                    case SyntaxKind.RightBracketToken:
                    case SyntaxKind.RightBraceToken:
                        if (depth > 0)
                            depth--;
                        break;

                    case SyntaxKind.ColonToken when depth == 0:
                        return true;

                    case SyntaxKind.EqualToken when depth == 0:
                    case SyntaxKind.SemicolonToken when depth == 0:
                    case SyntaxKind.NewLineToken when depth == 0:
                    case SyntaxKind.EndOfFileToken:
                        return false;
                }
            }
        }

        private bool IsComprehensionStart() =>
            Current.Kind == SyntaxKind.ForKeyword ||
            (Current.Kind == SyntaxKind.AsyncKeyword &&
             Peek(1).Kind == SyntaxKind.ForKeyword);

        private static bool IsPatternTerminator(SyntaxKind kind) => kind is
            SyntaxKind.CommaToken or
            SyntaxKind.ColonToken or
            SyntaxKind.PipeToken or
            SyntaxKind.RightParenthesisToken or
            SyntaxKind.RightBracketToken or
            SyntaxKind.RightBraceToken or
            SyntaxKind.IfKeyword or
            SyntaxKind.NewLineToken or
            SyntaxKind.DedentToken or
            SyntaxKind.EndOfFileToken;

        private static bool CanStartPattern(SyntaxKind kind) => kind is
            SyntaxKind.IdentifierToken or
            SyntaxKind.NumberToken or
            SyntaxKind.StringToken or
            SyntaxKind.FStringStartToken or
            SyntaxKind.TStringStartToken or
            SyntaxKind.TrueKeyword or
            SyntaxKind.FalseKeyword or
            SyntaxKind.NoneKeyword or
            SyntaxKind.MinusToken or
            SyntaxKind.LeftParenthesisToken or
            SyntaxKind.LeftBracketToken or
            SyntaxKind.LeftBraceToken or
            SyntaxKind.StarToken;

        private static bool CanStartParameter(SyntaxKind kind) => kind is
            SyntaxKind.IdentifierToken or
            SyntaxKind.SlashToken or
            SyntaxKind.StarToken or
            SyntaxKind.DoubleStarToken;

        private static bool CanStartSlice(SyntaxKind kind) =>
            kind == SyntaxKind.ColonToken || CanStartExpression(kind);

        private static GreenNode ValidateParameterName(
            GreenNode item,
            HashSet<string> names)
        {
            if (item.Kind != SyntaxKind.Parameter ||
                item.GetSlot(1) is not GreenToken { IsMissing: false } name ||
                string.IsNullOrEmpty(name.Text) ||
                names.Add(name.Text))
            {
                return item;
            }

            return WithDiagnostic(
                item,
                SyntaxDiagnosticCode.InvalidParameter,
                $"duplicate parameter '{name.Text}'");
        }

        private static bool IsUnparenthesizedGeneratorArgument(GreenNode argument) =>
            argument.Kind == SyntaxKind.GeneratorExpression &&
            argument.SlotCount == 2;

        private static GreenNode ValidateComprehensionElement(GreenNode element)
        {
            return element.Kind switch
            {
                SyntaxKind.StarredExpression => WithDiagnostic(
                    element,
                    SyntaxDiagnosticCode.InvalidComprehension,
                    "iterable unpacking cannot be used in a comprehension"),
                SyntaxKind.YieldExpression => WithDiagnostic(
                    element,
                    SyntaxDiagnosticCode.InvalidComprehension,
                    "yield expression cannot be used as a comprehension element"),
                _ => element,
            };
        }

        private static bool IsValidPatternLiteralExpression(GreenNode expression)
        {
            if (expression.Kind is
                SyntaxKind.StringConcatenationExpression or
                SyntaxKind.FStringExpression or
                SyntaxKind.TStringExpression)
            {
                return true;
            }

            if (expression.Kind == SyntaxKind.LiteralExpression)
            {
                return expression.GetSlot(0) is GreenToken token &&
                    token.Kind is
                        SyntaxKind.NumberToken or
                        SyntaxKind.StringToken or
                        SyntaxKind.TrueKeyword or
                        SyntaxKind.FalseKeyword or
                        SyntaxKind.NoneKeyword;
            }

            if (expression.Kind == SyntaxKind.UnaryExpression)
            {
                return expression.GetSlot(0) is GreenToken
                {
                    Kind: SyntaxKind.MinusToken
                } &&
                    expression.GetSlot(1) is { } operand &&
                    IsNumberLiteral(operand, requireImaginary: null);
            }

            if (expression.Kind != SyntaxKind.BinaryExpression ||
                expression.GetSlot(0) is not { } left ||
                expression.GetSlot(1) is not GreenToken
                {
                    Kind: SyntaxKind.PlusToken or SyntaxKind.MinusToken
                } ||
                expression.GetSlot(2) is not { } right)
            {
                return false;
            }

            return IsSignedRealNumber(left) &&
                IsNumberLiteral(right, requireImaginary: true);
        }

        private static bool IsSignedRealNumber(GreenNode expression)
        {
            if (IsNumberLiteral(expression, requireImaginary: false))
                return true;

            return expression.Kind == SyntaxKind.UnaryExpression &&
                expression.GetSlot(0) is GreenToken
                {
                    Kind: SyntaxKind.MinusToken
                } &&
                expression.GetSlot(1) is { } operand &&
                IsNumberLiteral(operand, requireImaginary: false);
        }

        private static bool IsNumberLiteral(
            GreenNode expression,
            bool? requireImaginary)
        {
            if (expression.Kind != SyntaxKind.LiteralExpression ||
                expression.GetSlot(0) is not GreenToken
                {
                    Kind: SyntaxKind.NumberToken
                } token)
            {
                return false;
            }

            return requireImaginary is null ||
                IsImaginaryNumber(token) == requireImaginary.Value;
        }

        private static bool IsImaginaryNumber(GreenToken token) =>
            token.Kind == SyntaxKind.NumberToken &&
            token.Text.Length != 0 &&
            token.Text[^1] is 'j' or 'J';

        private static GreenNode WithDiagnostic(
            GreenNode node,
            SyntaxDiagnosticCode code,
            string message)
        {
            var diagnostics = node.Diagnostics.Add(new GreenDiagnostic(
                code,
                0,
                node.FullWidth,
                message));

            if (node is GreenToken token)
            {
                return new GreenToken(
                    token.Kind,
                    token.Text,
                    token.LeadingTrivia,
                    token.TrailingTrivia,
                    diagnostics,
                    token.IsMissing);
            }

            var children = ImmutableArray.CreateBuilder<GreenNode?>(node.SlotCount);
            for (var i = 0; i < node.SlotCount; i++)
                children.Add(node.GetSlot(i));

            return GreenFactory.NodeWithDiagnostics(
                node.Kind,
                children.MoveToImmutable(),
                diagnostics);
        }

        private static bool IsSoftKeyword(GreenToken token, string text) =>
            token.Kind == SyntaxKind.IdentifierToken &&
            string.Equals(token.Text, text, StringComparison.Ordinal);

        private static bool IsAssignmentTarget(
            GreenNode node,
            bool allowSequence,
            bool allowStarredRoot,
            bool allowStarredInSequence = true)
        {
            switch (node.Kind)
            {
                case SyntaxKind.NameExpression:
                case SyntaxKind.AttributeExpression:
                case SyntaxKind.SubscriptExpression:
                    return true;

                case SyntaxKind.ParenthesizedExpression:
                    {
                        var inner = GetOnlySemanticChild(node);
                        return inner is not null &&
                            inner.Kind != SyntaxKind.StarredExpression &&
                            IsAssignmentTarget(
                                inner,
                                allowSequence,
                                allowStarredRoot: false,
                                allowStarredInSequence: allowStarredInSequence);
                    }

                case SyntaxKind.StarredExpression:
                    return allowStarredRoot &&
                        node.GetSlot(1) is { } starredTarget &&
                        IsAssignmentTarget(
                            starredTarget,
                            allowSequence: true,
                            allowStarredRoot: false,
                            allowStarredInSequence: allowStarredInSequence);

                case SyntaxKind.ListExpression:
                case SyntaxKind.TupleExpression:
                    return allowSequence && IsAssignmentTargetContainer(
                        node,
                        allowStarredInSequence);

                default:
                    return false;
            }
        }

        private static bool IsAssignmentTargetContainer(
            GreenNode node,
            bool allowStarredInSequence)
        {
            var starredCount = 0;
            return IsAssignmentTargetContainer(
                node,
                allowStarredInSequence,
                ref starredCount);
        }

        private static bool IsAssignmentTargetContainer(
            GreenNode node,
            bool allowStarredInSequence,
            ref int starredCount)
        {
            for (var i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetSlot(i);
                if (child is null || child is GreenToken)
                    continue;

                if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SeparatedSyntaxList)
                {
                    if (!IsAssignmentTargetContainer(
                        child,
                        allowStarredInSequence,
                        ref starredCount))
                        return false;
                    continue;
                }

                if (child.Kind == SyntaxKind.StarredExpression)
                {
                    if (!allowStarredInSequence || ++starredCount > 1)
                        return false;
                }

                if (!IsAssignmentTarget(
                    child,
                    allowSequence: true,
                    allowStarredRoot: allowStarredInSequence,
                    allowStarredInSequence: allowStarredInSequence))
                {
                    return false;
                }
            }

            return true;
        }

        private static GreenNode? GetOnlySemanticChild(GreenNode node)
        {
            GreenNode? result = null;
            for (var i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetSlot(i);
                if (child is null || child is GreenToken)
                    continue;

                if (result is not null)
                    return null;
                result = child;
            }

            return result;
        }

        private static bool IsAugmentedAssignment(SyntaxKind kind) => kind is
            SyntaxKind.PlusEqualToken or
            SyntaxKind.MinusEqualToken or
            SyntaxKind.StarEqualToken or
            SyntaxKind.AtEqualToken or
            SyntaxKind.SlashEqualToken or
            SyntaxKind.PercentEqualToken or
            SyntaxKind.AmpersandEqualToken or
            SyntaxKind.PipeEqualToken or
            SyntaxKind.CaretEqualToken or
            SyntaxKind.LeftShiftEqualToken or
            SyntaxKind.RightShiftEqualToken or
            SyntaxKind.DoubleStarEqualToken or
            SyntaxKind.DoubleSlashEqualToken;

        private bool IsComparisonStart() =>
            Current.Kind is
                SyntaxKind.EqualEqualToken or
                SyntaxKind.NotEqualToken or
                SyntaxKind.LessEqualToken or
                SyntaxKind.LessToken or
                SyntaxKind.GreaterEqualToken or
                SyntaxKind.GreaterToken or
                SyntaxKind.InKeyword or
                SyntaxKind.IsKeyword ||
            (Current.Kind == SyntaxKind.NotKeyword &&
             Peek(1).Kind == SyntaxKind.InKeyword);

        private static bool IsStringStart(SyntaxKind kind) => kind is
            SyntaxKind.StringToken or
            SyntaxKind.FStringStartToken or
            SyntaxKind.TStringStartToken;

        private static bool IsSimpleStatementTerminator(SyntaxKind kind) => kind is
            SyntaxKind.SemicolonToken or
            SyntaxKind.NewLineToken or
            SyntaxKind.DedentToken or
            SyntaxKind.EndOfFileToken;

        private static bool IsExpressionTerminator(SyntaxKind kind) => kind is
            SyntaxKind.CommaToken or
            SyntaxKind.SemicolonToken or
            SyntaxKind.ColonToken or
            SyntaxKind.EqualToken or
            SyntaxKind.RightParenthesisToken or
            SyntaxKind.RightBracketToken or
            SyntaxKind.RightBraceToken or
            SyntaxKind.NewLineToken or
            SyntaxKind.IndentToken or
            SyntaxKind.DedentToken or
            SyntaxKind.EndOfFileToken or
            SyntaxKind.ElseKeyword or
            SyntaxKind.ElifKeyword or
            SyntaxKind.FinallyKeyword or
            SyntaxKind.ExceptKeyword;

        private static bool CanStartExpression(SyntaxKind kind) => kind is
            SyntaxKind.IdentifierToken or
            SyntaxKind.NumberToken or
            SyntaxKind.StringToken or
            SyntaxKind.FStringStartToken or
            SyntaxKind.TStringStartToken or
            SyntaxKind.TrueKeyword or
            SyntaxKind.FalseKeyword or
            SyntaxKind.NoneKeyword or
            SyntaxKind.EllipsisToken or
            SyntaxKind.LeftParenthesisToken or
            SyntaxKind.LeftBracketToken or
            SyntaxKind.LeftBraceToken or
            SyntaxKind.LambdaKeyword or
            SyntaxKind.AwaitKeyword or
            SyntaxKind.NotKeyword or
            SyntaxKind.PlusToken or
            SyntaxKind.MinusToken or
            SyntaxKind.TildeToken or
            SyntaxKind.StarToken;

        private static bool CanStartArgument(SyntaxKind kind) =>
            kind == SyntaxKind.DoubleStarToken || CanStartExpression(kind);

        private static bool CanStartTypeParameter(SyntaxKind kind) => kind is
            SyntaxKind.IdentifierToken or
            SyntaxKind.StarToken or
            SyntaxKind.DoubleStarToken;

        private static string Display(SyntaxKind kind) => kind switch
        {
            SyntaxKind.EndOfFileToken => "end of file",
            SyntaxKind.IdentifierToken => "name",
            SyntaxKind.NewLineToken => "newline",
            SyntaxKind.IndentToken => "indent",
            SyntaxKind.DedentToken => "dedent",
            SyntaxKind.LeftParenthesisToken => "(",
            SyntaxKind.RightParenthesisToken => ")",
            SyntaxKind.LeftBracketToken => "[",
            SyntaxKind.RightBracketToken => "]",
            SyntaxKind.LeftBraceToken => "{",
            SyntaxKind.RightBraceToken => "}",
            SyntaxKind.ColonToken => ":",
            SyntaxKind.CommaToken => ",",
            SyntaxKind.SemicolonToken => ";",
            SyntaxKind.EqualToken => "=",
            SyntaxKind.InKeyword => "in",
            SyntaxKind.ElseKeyword => "else",
            SyntaxKind.ImportKeyword => "import",
            SyntaxKind.ForKeyword => "for",
            SyntaxKind.FStringEndToken => "formatted string end",
            SyntaxKind.TStringEndToken => "template string end",
            _ => kind.ToString(),
        };
    }


}
