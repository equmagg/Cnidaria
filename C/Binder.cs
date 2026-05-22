using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    public sealed class Binder
    {
        private readonly SemanticModel _semanticModel;
        private readonly Compilation _compilation;
        private readonly TypeCatalog _types = TypeCatalog.Instance;
        private readonly List<SemanticDiagnostic> _diagnostics = new();

        private FunctionSymbol? _currentFunction;
        private Dictionary<string, LabelSymbol>? _currentLabels;
        private int _loopDepth;
        private int _switchDepth;
        private readonly Stack<SwitchContext> _switchContexts = new();

        private Binder(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _compilation = semanticModel.Compilation;
        }

        public static BoundTree BindTree(SemanticModel semanticModel)
        {
            var binder = new Binder(semanticModel);
            var root = binder.BindTranslationUnit(semanticModel.Root);

            var diagnostics = ImmutableArray.CreateBuilder<SemanticDiagnostic>();
            diagnostics.AddRange(semanticModel.Compilation.SemanticDiagnostics);
            diagnostics.AddRange(binder._diagnostics);

            return new BoundTree(semanticModel, root, diagnostics.ToImmutable());
        }

        private BoundTranslationUnit BindTranslationUnit(TranslationUnitSyntax syntax)
        {
            var members = ImmutableArray.CreateBuilder<BoundNode>();

            foreach (var member in syntax.Members)
                members.Add(BindExternalMember(member));

            return new BoundTranslationUnit(syntax, members.ToImmutable());
        }

        private BoundNode BindExternalMember(SyntaxNode syntax)
        {
            switch (syntax)
            {
                case DeclarationSyntax declaration:
                    return BindDeclaration(declaration);

                case FunctionDefinitionSyntax functionDefinition:
                    return BindFunctionDefinition(functionDefinition);

                case StaticAssertDeclarationSyntax staticAssert:
                    return BindStaticAssertDeclaration(staticAssert);

                default:
                    return new BoundSkippedDeclaration(syntax);
            }
        }

        private BoundFunctionDefinition BindFunctionDefinition(FunctionDefinitionSyntax syntax)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(syntax) as FunctionSymbol;
            var previousFunction = _currentFunction;
            var previousLabels = _currentLabels;

            _currentFunction = symbol;
            _currentLabels = BuildLabelMap(syntax.Body);

            var body = BindCompoundStatement(syntax.Body);
            AnalyzeFunctionControlFlow(symbol, body);

            _currentFunction = previousFunction;
            _currentLabels = previousLabels;

            return new BoundFunctionDefinition(syntax, symbol, body);
        }

        private Dictionary<string, LabelSymbol> BuildLabelMap(CompoundStatementSyntax body)
        {
            var labels = new Dictionary<string, LabelSymbol>(StringComparer.Ordinal);
            CollectLabels(body, labels);
            return labels;
        }

        private void CollectLabels(SyntaxNode node, Dictionary<string, LabelSymbol> labels)
        {
            switch (node)
            {
                case LabelStatementSyntax label:
                    {
                        var symbol = _semanticModel.GetDeclaredSymbol(label) as LabelSymbol
                            ?? new LabelSymbol(label.IdentifierToken.Text, label);

                        if (!labels.ContainsKey(symbol.Name))
                            labels.Add(symbol.Name, symbol);

                        CollectLabels(label.Statement, labels);
                        break;
                    }

                case CompoundStatementSyntax compound:
                    foreach (var member in compound.Members)
                        CollectLabels(member, labels);
                    break;

                case IfStatementSyntax ifStatement:
                    CollectLabels(ifStatement.ThenStatement, labels);
                    if (ifStatement.ElseStatement is not null)
                        CollectLabels(ifStatement.ElseStatement, labels);
                    break;

                case SwitchStatementSyntax switchStatement:
                    CollectLabels(switchStatement.Statement, labels);
                    break;

                case WhileStatementSyntax whileStatement:
                    CollectLabels(whileStatement.Statement, labels);
                    break;

                case DoStatementSyntax doStatement:
                    CollectLabels(doStatement.Statement, labels);
                    break;

                case ForStatementSyntax forStatement:
                    CollectLabels(forStatement.Statement, labels);
                    break;

                case CaseStatementSyntax caseStatement:
                    CollectLabels(caseStatement.Statement, labels);
                    break;

                case DefaultStatementSyntax defaultStatement:
                    CollectLabels(defaultStatement.Statement, labels);
                    break;

                case StatementExpressionSyntax statementExpression:
                    CollectLabels(statementExpression.Statement, labels);
                    break;
            }
        }

        private BoundDeclaration BindDeclaration(DeclarationSyntax syntax)
        {
            var scope = _semanticModel.GetScope(syntax) ?? _compilation.GlobalScope;
            var specifiers = DeclarationTypeParser.ParseSpecifiers(syntax.Specifiers, scope, _types);
            var declarators = ImmutableArray.CreateBuilder<BoundDeclarator>();

            foreach (var declarator in syntax.Declarators)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(declarator);
                var type = symbol is TypedSymbol typed
                    ? typed.Type
                    : DeclaratorTypeBuilder.Build(declarator.Declarator, specifiers.BaseType, _types, scope);

                var initializer = declarator.Initializer is not null
                    ? BindInitializer(declarator.Initializer, type)
                    : null;

                declarators.Add(new BoundDeclarator(
                    declarator,
                    symbol,
                    type,
                    initializer));
            }

            return new BoundDeclaration(
                syntax,
                specifiers.StorageClass,
                declarators.ToImmutable());
        }

        private BoundInitializer BindInitializer(InitializerSyntax syntax, QualifiedType targetType)
        {
            switch (syntax)
            {
                case ExpressionInitializerSyntax expressionInitializer:
                    {
                        var expression = ApplyDefaultConversions(BindExpression(expressionInitializer.Expression));

                        if (!targetType.IsError && !expression.Type.IsError && !CanConvert(expression.Type, targetType))
                        {
                            Report(
                                $"Cannot initialize object of type '{targetType.ToDisplayString()}' with expression of type '{expression.Type.ToDisplayString()}'.",
                                SpanOf(expressionInitializer.Expression));
                        }

                        return new BoundExpressionInitializer(expressionInitializer, targetType, expression);
                    }

                case InitializerListSyntax initializerList:
                    {
                        var items = ImmutableArray.CreateBuilder<BoundInitializerListItem>();

                        foreach (var item in initializerList.Items)
                        {
                            var boundItemInitializer = BindInitializer(item.Initializer, targetType);
                            items.Add(new BoundInitializerListItem(item, boundItemInitializer));
                        }

                        return new BoundInitializerList(initializerList, targetType, items.ToImmutable());
                    }

                default:
                    throw new InvalidOperationException($"Unexpected initializer syntax: {syntax.GetType().Name}");
            }
        }

        private BoundStaticAssertDeclaration BindStaticAssertDeclaration(StaticAssertDeclarationSyntax syntax)
        {
            var condition = ApplyDefaultConversions(BindExpression(syntax.Condition));
            if (!IsIntegerType(condition.Type))
            {
                Report(
                    "Static assertion expression must have integer type.",
                    SpanOf(syntax.Condition));
            }

            BoundExpression? message = null;
            if (syntax.Message is not null)
                message = ApplyDefaultConversions(BindExpression(syntax.Message));

            return new BoundStaticAssertDeclaration(syntax, condition, message);
        }

        private BoundStatement BindStatement(StatementSyntax syntax)
        {
            switch (syntax)
            {
                case CompoundStatementSyntax compound:
                    return BindCompoundStatement(compound);

                case IfStatementSyntax ifStatement:
                    return BindIfStatement(ifStatement);

                case SwitchStatementSyntax switchStatement:
                    return BindSwitchStatement(switchStatement);

                case WhileStatementSyntax whileStatement:
                    return BindWhileStatement(whileStatement);

                case DoStatementSyntax doStatement:
                    return BindDoStatement(doStatement);

                case ForStatementSyntax forStatement:
                    return BindForStatement(forStatement);

                case BreakStatementSyntax breakStatement:
                    return BindBreakStatement(breakStatement);

                case ContinueStatementSyntax continueStatement:
                    return BindContinueStatement(continueStatement);

                case GotoStatementSyntax gotoStatement:
                    return BindGotoStatement(gotoStatement);

                case LabelStatementSyntax labelStatement:
                    return BindLabelStatement(labelStatement);

                case CaseStatementSyntax caseStatement:
                    return BindCaseStatement(caseStatement);

                case DefaultStatementSyntax defaultStatement:
                    return BindDefaultStatement(defaultStatement);

                case ReturnStatementSyntax returnStatement:
                    return BindReturnStatement(returnStatement);

                case ExpressionStatementSyntax expressionStatement:
                    return BindExpressionStatement(expressionStatement);

                default:
                    Report($"Unsupported statement syntax '{syntax.Kind}'.", SpanOf(syntax));
                    return new BoundErrorStatement(syntax);
            }
        }

        private BoundCompoundStatement BindCompoundStatement(CompoundStatementSyntax syntax)
        {
            var scope = _semanticModel.GetScope(syntax);
            var members = ImmutableArray.CreateBuilder<BoundNode>();

            foreach (var member in syntax.Members)
            {
                switch (member)
                {
                    case DeclarationSyntax declaration:
                        members.Add(BindDeclaration(declaration));
                        break;

                    case StaticAssertDeclarationSyntax staticAssert:
                        members.Add(BindStaticAssertDeclaration(staticAssert));
                        break;

                    case StatementSyntax statement:
                        members.Add(BindStatement(statement));
                        break;

                    default:
                        members.Add(new BoundSkippedDeclaration(member));
                        break;
                }
            }

            return new BoundCompoundStatement(syntax, scope, members.ToImmutable());
        }

        private BoundIfStatement BindIfStatement(IfStatementSyntax syntax)
        {
            var condition = BindScalarCondition(syntax.Condition, "if");
            var thenStatement = BindStatement(syntax.ThenStatement);
            var elseStatement = syntax.ElseStatement is null
                ? null
                : BindStatement(syntax.ElseStatement);

            return new BoundIfStatement(syntax, condition, thenStatement, elseStatement);
        }

        private BoundSwitchStatement BindSwitchStatement(SwitchStatementSyntax syntax)
        {
            var expression = ApplyDefaultConversions(BindExpression(syntax.Expression));
            if (!IsIntegerType(expression.Type) && !expression.Type.IsError)
                Report("Switch expression must have integer type.", SpanOf(syntax.Expression));

            BoundStatement statement;
            _switchDepth++;
            _switchContexts.Push(new SwitchContext());
            try
            {
                statement = BindStatement(syntax.Statement);
            }
            finally
            {
                _switchContexts.Pop();
                _switchDepth--;
            }

            return new BoundSwitchStatement(syntax, expression, statement);
        }

        private BoundWhileStatement BindWhileStatement(WhileStatementSyntax syntax)
        {
            var condition = BindScalarCondition(syntax.Condition, "while");

            _loopDepth++;
            var statement = BindStatement(syntax.Statement);
            _loopDepth--;

            return new BoundWhileStatement(syntax, condition, statement);
        }

        private BoundDoStatement BindDoStatement(DoStatementSyntax syntax)
        {
            _loopDepth++;
            var statement = BindStatement(syntax.Statement);
            _loopDepth--;

            var condition = BindScalarCondition(syntax.Condition, "do");

            return new BoundDoStatement(syntax, statement, condition);
        }

        private BoundForStatement BindForStatement(ForStatementSyntax syntax)
        {
            BoundNode? initializer = null;
            if (syntax.Initializer is DeclarationSyntax declaration)
                initializer = BindDeclaration(declaration);
            else if (syntax.Initializer is ExpressionSyntax initializerExpression)
                initializer = ApplyDefaultConversions(BindExpression(initializerExpression));

            var condition = syntax.Condition is null
                ? null
                : BindScalarCondition(syntax.Condition, "for");

            var increment = syntax.Increment is null
                ? null
                : ApplyDefaultConversions(BindExpression(syntax.Increment));

            _loopDepth++;
            var statement = BindStatement(syntax.Statement);
            _loopDepth--;

            return new BoundForStatement(
                syntax,
                _semanticModel.GetScope(syntax),
                initializer,
                condition,
                increment,
                statement);
        }

        private BoundBreakStatement BindBreakStatement(BreakStatementSyntax syntax)
        {
            if (_loopDepth == 0 && _switchDepth == 0)
                Report("A break statement may only appear inside a loop or switch statement.", syntax.BreakKeyword.Span);

            return new BoundBreakStatement(syntax);
        }

        private BoundContinueStatement BindContinueStatement(ContinueStatementSyntax syntax)
        {
            if (_loopDepth == 0)
                Report("A continue statement may only appear inside a loop statement.", syntax.ContinueKeyword.Span);

            return new BoundContinueStatement(syntax);
        }

        private BoundGotoStatement BindGotoStatement(GotoStatementSyntax syntax)
        {
            LabelSymbol? label = null;

            if (_currentLabels is not null)
                _currentLabels.TryGetValue(syntax.IdentifierToken.Text, out label);

            if (label is null)
                Report($"Unknown label '{syntax.IdentifierToken.Text}'.", syntax.IdentifierToken.Span);

            return new BoundGotoStatement(syntax, label);
        }

        private BoundLabelStatement BindLabelStatement(LabelStatementSyntax syntax)
        {
            LabelSymbol? label = null;

            if (_currentLabels is not null)
                _currentLabels.TryGetValue(syntax.IdentifierToken.Text, out label);

            label ??= _semanticModel.GetDeclaredSymbol(syntax) as LabelSymbol;

            return new BoundLabelStatement(
                syntax,
                label,
                BindStatement(syntax.Statement));
        }

        private BoundCaseStatement BindCaseStatement(CaseStatementSyntax syntax)
        {
            var switchContext = _switchContexts.Count == 0 ? null : _switchContexts.Peek();

            if (_switchDepth == 0)
                Report("A case label may only appear inside a switch statement.", syntax.CaseKeyword.Span);

            var expression = ApplyDefaultConversions(BindExpression(syntax.Expression));
            if (!IsIntegerType(expression.Type) && !expression.Type.IsError)
            {
                Report("Case label expression must have integer type.", SpanOf(syntax.Expression));
            }
            else if (!expression.Type.IsError)
            {
                if (!TryEvaluateIntegerConstantValue(expression, out var value))
                {
                    Report("Case label expression must be an integer constant expression.", SpanOf(syntax.Expression));
                }
                else if (switchContext is not null &&
                         !switchContext.TryDeclareCase(value, syntax, out var existingCase))
                {
                    Report(
                        $"Duplicate case label value '{value.ToString(CultureInfo.InvariantCulture)}'.",
                        SpanOf(syntax.Expression));
                }
            }

            return new BoundCaseStatement(
                syntax,
                expression,
                BindStatement(syntax.Statement));
        }

        private BoundDefaultStatement BindDefaultStatement(DefaultStatementSyntax syntax)
        {
            var switchContext = _switchContexts.Count == 0 ? null : _switchContexts.Peek();

            if (_switchDepth == 0)
            {
                Report("A default label may only appear inside a switch statement.", syntax.DefaultKeyword.Span);
            }
            else if (switchContext is not null &&
                     !switchContext.TryDeclareDefault(syntax, out var existingDefault))
            {
                Report("Duplicate default label in switch statement.", syntax.DefaultKeyword.Span);
            }

            return new BoundDefaultStatement(
                syntax,
                BindStatement(syntax.Statement));
        }

        private BoundReturnStatement BindReturnStatement(ReturnStatementSyntax syntax)
        {
            var function = _currentFunction;
            var returnType = function?.FunctionType?.ReturnType;

            BoundExpression? expression = null;
            if (syntax.Expression is not null)
                expression = ApplyDefaultConversions(BindExpression(syntax.Expression));

            if (function is null)
            {
                Report("A return statement may only appear inside a function definition.", syntax.ReturnKeyword.Span);
            }
            else if (returnType.HasValue)
            {
                var returnsVoid = returnType.Value.Type is BuiltinType builtin &&
                                  builtin.BuiltinKind == BuiltinTypeKind.Void;

                if (returnsVoid && expression is not null)
                {
                    Report("A void function should not return a value.", SpanOf(syntax.Expression!));
                }
                else if (!returnsVoid && expression is null)
                {
                    Report("A non-void function should return a value.", syntax.ReturnKeyword.Span);
                }
                else if (!returnsVoid && expression is not null &&
                         !CanConvert(expression.Type, returnType.Value))
                {
                    Report(
                        $"Cannot convert return expression of type '{expression.Type.ToDisplayString()}' to '{returnType.Value.ToDisplayString()}'.",
                        SpanOf(syntax.Expression!));
                }
            }

            return new BoundReturnStatement(syntax, function, expression);
        }

        private BoundStatement BindExpressionStatement(ExpressionStatementSyntax syntax)
        {
            if (syntax.Expression is null)
                return new BoundEmptyStatement(syntax);

            return new BoundExpressionStatement(
                syntax,
                ApplyDefaultConversions(BindExpression(syntax.Expression)));
        }

        private BoundExpression BindScalarCondition(ExpressionSyntax syntax, string constructName)
        {
            var condition = ApplyDefaultConversions(BindExpression(syntax));
            if (!IsScalarType(condition.Type) && !condition.Type.IsError)
            {
                Report(
                    $"The controlling expression of '{constructName}' must have scalar type.",
                    SpanOf(syntax));
            }

            return condition;
        }

        private BoundExpression BindExpression(ExpressionSyntax syntax)
        {
            switch (syntax)
            {
                case LiteralExpressionSyntax literal:
                    return BindLiteralExpression(literal);

                case NameExpressionSyntax name:
                    return BindNameExpression(name);

                case UnaryExpressionSyntax unary:
                    return BindUnaryExpression(unary);

                case BinaryExpressionSyntax binary:
                    return BindBinaryExpression(binary);

                case AssignmentExpressionSyntax assignment:
                    return BindAssignmentExpression(assignment);

                case ConditionalExpressionSyntax conditional:
                    return BindConditionalExpression(conditional);

                case CastExpressionSyntax cast:
                    return BindCastExpression(cast);

                case SizeofExpressionSyntax sizeofExpression:
                    return BindSizeofExpression(sizeofExpression);

                case ParenthesizedExpressionSyntax parenthesized:
                    return new BoundParenthesizedExpression(
                        parenthesized,
                        BindExpression(parenthesized.Expression));

                case CompoundLiteralExpressionSyntax compoundLiteral:
                    return BindCompoundLiteralExpression(compoundLiteral);

                case GenericSelectionExpressionSyntax generic:
                    return BindGenericSelectionExpression(generic);

                case StatementExpressionSyntax statementExpression:
                    return BindStatementExpression(statementExpression);

                case CallExpressionSyntax call:
                    return BindCallExpression(call);

                case ElementAccessExpressionSyntax elementAccess:
                    return BindElementAccessExpression(elementAccess);

                case MemberAccessExpressionSyntax memberAccess:
                    return BindMemberAccessExpression(memberAccess);

                case PostfixUnaryExpressionSyntax postfix:
                    return BindPostfixUnaryExpression(postfix);

                case InvalidExpressionSyntax invalid:
                    return new BoundErrorExpression(invalid);

                default:
                    Report($"Unsupported expression syntax '{syntax.Kind}'.", SpanOf(syntax));
                    return new BoundErrorExpression(syntax);
            }
        }

        private BoundExpression BindLiteralExpression(LiteralExpressionSyntax syntax)
        {
            var token = syntax.LiteralToken;
            QualifiedType type;
            object? constantValue = token.Value;

            switch (token.Kind)
            {
                case SyntaxKind.IntegerLiteralToken:
                    type = InferIntegerLiteralType(token.Text, out constantValue);
                    break;

                case SyntaxKind.FloatingLiteralToken:
                    type = InferFloatingLiteralType(token.Text);
                    constantValue = TryParseFloatingLiteral(token.Text);
                    break;

                case SyntaxKind.CharacterLiteralToken:
                case SyntaxKind.WideCharacterLiteralToken:
                case SyntaxKind.Utf8CharacterLiteralToken:
                case SyntaxKind.Utf16CharacterLiteralToken:
                case SyntaxKind.Utf32CharacterLiteralToken:
                    type = _types.Builtin(BuiltinTypeKind.Int);
                    constantValue = token.Value;
                    break;

                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.Utf8StringLiteralToken:
                    type = new QualifiedType(_types.ArrayOf(_types.Builtin(BuiltinTypeKind.Char), null));
                    constantValue = token.Value;
                    break;

                case SyntaxKind.WideStringLiteralToken:
                case SyntaxKind.Utf16StringLiteralToken:
                case SyntaxKind.Utf32StringLiteralToken:
                    type = new QualifiedType(_types.ArrayOf(_types.Builtin(BuiltinTypeKind.Int), null));
                    break;

                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                    type = _types.Builtin(BuiltinTypeKind.Bool);
                    constantValue = token.Kind == SyntaxKind.TrueKeyword;
                    break;

                case SyntaxKind.NullptrKeyword:
                    type = new QualifiedType(_types.PointerTo(_types.Builtin(BuiltinTypeKind.Void)));
                    constantValue = null;
                    break;

                default:
                    type = ErrorType;
                    break;
            }

            return new BoundLiteralExpression(syntax, token, type, constantValue);
        }

        private BoundExpression BindNameExpression(NameExpressionSyntax syntax)
        {
            var symbol = _semanticModel.GetSymbolInfo(syntax);
            symbol ??= _semanticModel.GetScope(syntax)?.LookupOrdinary(syntax.IdentifierToken.Text);

            if (symbol is null)
            {
                Report($"Undefined identifier '{syntax.IdentifierToken.Text}'.", syntax.IdentifierToken.Span);
                return new BoundNameExpression(syntax, ErrorSymbol.Instance, ErrorType, BoundValueKind.Error);
            }

            if (symbol is TypeAliasSymbol)
            {
                Report($"'{symbol.Name}' names a type, not an expression.", syntax.IdentifierToken.Span);
                return new BoundNameExpression(syntax, symbol, ErrorType, BoundValueKind.Error);
            }

            if (symbol is FunctionSymbol function)
            {
                return new BoundNameExpression(
                    syntax,
                    function,
                    function.Type,
                    BoundValueKind.Function);
            }

            if (symbol is TypedSymbol typed)
            {
                return new BoundNameExpression(
                    syntax,
                    symbol,
                    typed.Type,
                    BoundValueKind.LValue);
            }

            Report($"'{symbol.Name}' is not an expression symbol.", syntax.IdentifierToken.Span);
            return new BoundNameExpression(syntax, symbol, ErrorType, BoundValueKind.Error);
        }

        private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
        {
            var operand = BindExpression(syntax.Operand);

            switch (syntax.OperatorToken.Kind)
            {
                case SyntaxKind.AmpersandToken:
                    if (operand.Type.IsError)
                        return new BoundUnaryExpression(syntax, syntax.OperatorToken, operand, ErrorType, BoundValueKind.Error);

                    return new BoundUnaryExpression(
                        syntax,
                        syntax.OperatorToken,
                        operand,
                        new QualifiedType(_types.PointerTo(operand.Type)),
                        BoundValueKind.RValue);

                case SyntaxKind.StarToken:
                    {
                        var converted = ApplyDefaultConversions(operand);
                        if (TryGetPointeeType(converted.Type, out var pointee))
                        {
                            return new BoundUnaryExpression(
                                syntax,
                                syntax.OperatorToken,
                                converted,
                                pointee,
                                BoundValueKind.LValue);
                        }

                        Report($"Cannot dereference expression of type '{converted.Type.ToDisplayString()}'.", SpanOf(syntax.Operand));
                        return new BoundUnaryExpression(syntax, syntax.OperatorToken, converted, ErrorType, BoundValueKind.Error);
                    }

                case SyntaxKind.PlusToken:
                case SyntaxKind.MinusToken:
                    {
                        var converted = ApplyDefaultConversions(operand);
                        if (!IsArithmeticType(converted.Type) && !converted.Type.IsError)
                            Report($"Unary operator '{syntax.OperatorToken.Text}' requires an arithmetic operand.", syntax.OperatorToken.Span);

                        return new BoundUnaryExpression(
                            syntax,
                            syntax.OperatorToken,
                            converted,
                            IntegerPromote(converted.Type),
                            BoundValueKind.RValue);
                    }

                case SyntaxKind.TildeToken:
                    {
                        var converted = ApplyDefaultConversions(operand);
                        if (!IsIntegerType(converted.Type) && !converted.Type.IsError)
                            Report("Unary operator '~' requires an integer operand.", syntax.OperatorToken.Span);

                        return new BoundUnaryExpression(
                            syntax,
                            syntax.OperatorToken,
                            converted,
                            IntegerPromote(converted.Type),
                            BoundValueKind.RValue);
                    }

                case SyntaxKind.BangToken:
                    {
                        var converted = ApplyDefaultConversions(operand);
                        if (!IsScalarType(converted.Type) && !converted.Type.IsError)
                            Report("Unary operator '!' requires a scalar operand.", syntax.OperatorToken.Span);

                        return new BoundUnaryExpression(
                            syntax,
                            syntax.OperatorToken,
                            converted,
                            _types.Builtin(BuiltinTypeKind.Int),
                            BoundValueKind.RValue);
                    }

                case SyntaxKind.PlusPlusToken:
                case SyntaxKind.MinusMinusToken:
                    if (!IsModifiableLValue(operand))
                        Report("Increment and decrement require a modifiable lvalue.", syntax.OperatorToken.Span);

                    return new BoundUnaryExpression(
                        syntax,
                        syntax.OperatorToken,
                        operand,
                        operand.Type,
                        BoundValueKind.RValue);

                default:
                    Report($"Unsupported unary operator '{syntax.OperatorToken.Text}'.", syntax.OperatorToken.Span);
                    return new BoundUnaryExpression(syntax, syntax.OperatorToken, operand, ErrorType, BoundValueKind.Error);
            }
        }

        private BoundExpression BindPostfixUnaryExpression(PostfixUnaryExpressionSyntax syntax)
        {
            var operand = BindExpression(syntax.Expression);

            if (!IsModifiableLValue(operand))
                Report("Increment and decrement require a modifiable lvalue.", syntax.OperatorToken.Span);

            return new BoundPostfixUnaryExpression(
                syntax,
                operand,
                syntax.OperatorToken,
                operand.Type);
        }

        private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
        {
            var left = ApplyDefaultConversions(BindExpression(syntax.Left));
            var right = ApplyDefaultConversions(BindExpression(syntax.Right));

            var resultType = BindBinaryResultType(syntax.OperatorToken, left, right);
            return new BoundBinaryExpression(
                syntax,
                left,
                syntax.OperatorToken,
                right,
                resultType);
        }

        private QualifiedType BindBinaryResultType(
            SyntaxToken operatorToken,
            BoundExpression left,
            BoundExpression right)
        {
            if (left.Type.IsError || right.Type.IsError)
                return ErrorType;

            switch (operatorToken.Kind)
            {
                case SyntaxKind.StarToken:
                case SyntaxKind.SlashToken:
                    if (!IsArithmeticType(left.Type) || !IsArithmeticType(right.Type))
                        Report($"Binary operator '{operatorToken.Text}' requires arithmetic operands.", operatorToken.Span);
                    return UsualArithmeticConversion(left.Type, right.Type);

                case SyntaxKind.PercentToken:
                    if (!IsIntegerType(left.Type) || !IsIntegerType(right.Type))
                        Report("Binary operator '%' requires integer operands.", operatorToken.Span);
                    return UsualArithmeticConversion(left.Type, right.Type);

                case SyntaxKind.PlusToken:
                    if (IsPointerType(left.Type) && IsIntegerType(right.Type))
                        return left.Type;
                    if (IsIntegerType(left.Type) && IsPointerType(right.Type))
                        return right.Type;
                    if (!IsArithmeticType(left.Type) || !IsArithmeticType(right.Type))
                        Report("Binary operator '+' requires arithmetic operands or pointer/integer operands.", operatorToken.Span);
                    return UsualArithmeticConversion(left.Type, right.Type);

                case SyntaxKind.MinusToken:
                    if (IsPointerType(left.Type) && IsIntegerType(right.Type))
                        return left.Type;
                    if (IsPointerType(left.Type) && IsPointerType(right.Type))
                        return _types.Builtin(BuiltinTypeKind.Long);
                    if (!IsArithmeticType(left.Type) || !IsArithmeticType(right.Type))
                        Report("Binary operator '-' requires arithmetic operands or pointer operands.", operatorToken.Span);
                    return UsualArithmeticConversion(left.Type, right.Type);

                case SyntaxKind.LessThanToken:
                case SyntaxKind.LessThanEqualsToken:
                case SyntaxKind.GreaterThanToken:
                case SyntaxKind.GreaterThanEqualsToken:
                    if (!CanCompare(left.Type, right.Type))
                        Report($"Relational operator '{operatorToken.Text}' cannot compare '{left.Type}' and '{right.Type}'.", operatorToken.Span);
                    return _types.Builtin(BuiltinTypeKind.Int);

                case SyntaxKind.EqualsEqualsToken:
                case SyntaxKind.BangEqualsToken:
                    if (!CanCompare(left.Type, right.Type))
                        Report($"Equality operator '{operatorToken.Text}' cannot compare '{left.Type}' and '{right.Type}'.", operatorToken.Span);
                    return _types.Builtin(BuiltinTypeKind.Int);

                case SyntaxKind.AmpersandAmpersandToken:
                case SyntaxKind.PipePipeToken:
                    if (!IsScalarType(left.Type) || !IsScalarType(right.Type))
                        Report($"Logical operator '{operatorToken.Text}' requires scalar operands.", operatorToken.Span);
                    return _types.Builtin(BuiltinTypeKind.Int);

                case SyntaxKind.AmpersandToken:
                case SyntaxKind.PipeToken:
                case SyntaxKind.HatToken:
                case SyntaxKind.LessThanLessThanToken:
                case SyntaxKind.GreaterThanGreaterThanToken:
                    if (!IsIntegerType(left.Type) || !IsIntegerType(right.Type))
                        Report($"Bitwise operator '{operatorToken.Text}' requires integer operands.", operatorToken.Span);
                    return UsualArithmeticConversion(left.Type, right.Type);

                case SyntaxKind.CommaToken:
                    return right.Type;

                default:
                    Report($"Unsupported binary operator '{operatorToken.Text}'.", operatorToken.Span);
                    return ErrorType;
            }
        }

        private BoundExpression BindAssignmentExpression(AssignmentExpressionSyntax syntax)
        {
            var left = BindExpression(syntax.Left);
            var right = ApplyDefaultConversions(BindExpression(syntax.Right));

            if (!IsModifiableLValue(left))
            {
                Report("Left side of assignment must be a modifiable lvalue.", SpanOf(syntax.Left));
            }

            if (!left.Type.IsError && !right.Type.IsError && !CanConvert(right.Type, left.Type))
            {
                Report(
                    $"Cannot assign expression of type '{right.Type.ToDisplayString()}' to object of type '{left.Type.ToDisplayString()}'.",
                    SpanOf(syntax.Right));
            }

            return new BoundAssignmentExpression(
                syntax,
                left,
                syntax.OperatorToken,
                right,
                left.Type.IsError ? ErrorType : left.Type);
        }

        private BoundExpression BindConditionalExpression(ConditionalExpressionSyntax syntax)
        {
            var condition = ApplyDefaultConversions(BindExpression(syntax.Condition));
            if (!IsScalarType(condition.Type) && !condition.Type.IsError)
                Report("Conditional expression condition must have scalar type.", SpanOf(syntax.Condition));

            var whenTrue = ApplyDefaultConversions(BindExpression(syntax.WhenTrue));
            var whenFalse = ApplyDefaultConversions(BindExpression(syntax.WhenFalse));

            var resultType = CommonConditionalType(whenTrue.Type, whenFalse.Type);

            return new BoundConditionalExpression(
                syntax,
                condition,
                whenTrue,
                whenFalse,
                resultType);
        }

        private BoundExpression BindCastExpression(CastExpressionSyntax syntax)
        {
            var scope = _semanticModel.GetScope(syntax) ?? _compilation.GlobalScope;
            var targetType = BindTypeName(syntax.TypeNameTokens, scope);
            var expression = ApplyDefaultConversions(BindExpression(syntax.Expression));

            return new BoundCastExpression(syntax, expression, targetType);
        }

        private BoundExpression BindSizeofExpression(SizeofExpressionSyntax syntax)
        {
            QualifiedType operandType;
            BoundExpression? expression = null;
            var scope = _semanticModel.GetScope(syntax) ?? _compilation.GlobalScope;

            if (syntax.Expression is not null)
            {
                expression = BindExpression(syntax.Expression);
                operandType = expression.Type;
            }
            else
            {
                operandType = BindTypeName(syntax.TypeNameTokens, scope);
            }

            var resultType = _types.Builtin(BuiltinTypeKind.UnsignedLong);
            object? constantValue = null;
            if (!operandType.IsError)
            {
                try
                {
                    constantValue = _compilation.Options.Target.SizeOf(operandType);
                }
                catch (OverflowException)
                {
                    Report("The size of the operand cannot be represented by the target size type.", SpanOf(syntax));
                }
            }

            return new BoundSizeofExpression(
                syntax,
                expression,
                operandType,
                resultType,
                constantValue);
        }

        private BoundExpression BindCompoundLiteralExpression(CompoundLiteralExpressionSyntax syntax)
        {
            var scope = _semanticModel.GetScope(syntax) ?? _compilation.GlobalScope;
            var type = BindTypeName(syntax.TypeNameTokens, scope);

            var initializerList = syntax.InitializerList is not null
                ? (BoundInitializerList)BindInitializer(syntax.InitializerList, type)
                : null;

            return new BoundCompoundLiteralExpression(
                syntax,
                type,
                initializerList);
        }

        private BoundExpression BindGenericSelectionExpression(GenericSelectionExpressionSyntax syntax)
        {
            var control = ApplyDefaultConversions(BindExpression(syntax.ControlExpression));
            var associationExpressions = ImmutableArray.CreateBuilder<BoundExpression>();
            BoundExpression? selected = null;

            foreach (var association in syntax.Associations)
            {
                var expression = ApplyDefaultConversions(BindExpression(association.Expression));
                associationExpressions.Add(expression);

                if (selected is null && association.DefaultKeyword.HasValue)
                    selected = expression;
            }

            selected ??= associationExpressions.Count == 0 ? null : associationExpressions[0];

            return new BoundGenericSelectionExpression(
                syntax,
                control,
                associationExpressions.ToImmutable(),
                selected,
                selected?.Type ?? ErrorType);
        }

        private BoundExpression BindStatementExpression(StatementExpressionSyntax syntax)
        {
            var statement = BindCompoundStatement(syntax.Statement);
            var lastExpression = FindLastExpression(statement);

            return new BoundStatementExpression(
                syntax,
                statement,
                lastExpression?.Type ?? _types.Builtin(BuiltinTypeKind.Void),
                lastExpression?.ValueKind ?? BoundValueKind.RValue);
        }

        private BoundExpression? FindLastExpression(BoundCompoundStatement statement)
        {
            if (statement.Members.Length == 0)
                return null;

            var last = statement.Members[statement.Members.Length - 1];
            if (last is BoundExpressionStatement expressionStatement)
                return expressionStatement.Expression;

            return null;
        }

        private BoundExpression BindCallExpression(CallExpressionSyntax syntax)
        {
            var expression = ApplyDefaultConversions(BindExpression(syntax.Expression));
            var arguments = ImmutableArray.CreateBuilder<BoundExpression>();

            foreach (var argument in syntax.Arguments)
                arguments.Add(ApplyDefaultConversions(BindExpression(argument)));

            var functionType = GetFunctionType(expression.Type);
            QualifiedType resultType;

            if (functionType is null)
            {
                Report($"Expression of type '{expression.Type.ToDisplayString()}' is not callable.", SpanOf(syntax.Expression));
                resultType = ErrorType;
            }
            else
            {
                resultType = functionType.ReturnType;
                if (functionType.HasPrototype)
                    CheckCallArguments(syntax, functionType, arguments);
                else
                    ApplyDefaultArgumentPromotions(arguments, startIndex: 0);
            }

            return new BoundCallExpression(
                syntax,
                expression,
                arguments.ToImmutable(),
                functionType,
                resultType);
        }

        private void CheckCallArguments(
            CallExpressionSyntax syntax,
            FunctionType functionType,
            ImmutableArray<BoundExpression>.Builder arguments)
        {
            var fixedCount = functionType.Parameters.Length;

            if (!functionType.IsVariadic && arguments.Count != fixedCount)
            {
                Report(
                    $"Function expects {fixedCount.ToString(CultureInfo.InvariantCulture)} argument(s), but {arguments.Count.ToString(CultureInfo.InvariantCulture)} were provided.",
                    SpanOf(syntax));
                return;
            }

            if (functionType.IsVariadic && arguments.Count < fixedCount)
            {
                Report(
                    $"Function expects at least {fixedCount.ToString(CultureInfo.InvariantCulture)} argument(s), but {arguments.Count.ToString(CultureInfo.InvariantCulture)} were provided.",
                    SpanOf(syntax));
                return;
            }

            for (var i = 0; i < fixedCount && i < arguments.Count; i++)
            {
                var parameterType = functionType.Parameters[i].Type;
                var argument = arguments[i];

                if (!argument.Type.IsError && !CanConvert(argument.Type, parameterType))
                {
                    Report(
                        $"Cannot convert argument {(i + 1).ToString(CultureInfo.InvariantCulture)} from '{argument.Type.ToDisplayString()}' to '{parameterType.ToDisplayString()}'.",
                        SpanOf(syntax.Arguments[i]));
                }
                else
                {
                    arguments[i] = ConvertCallArgument(argument, parameterType);
                }
            }
            if (functionType.IsVariadic)
                ApplyDefaultArgumentPromotions(arguments, fixedCount);
        }
        private void ApplyDefaultArgumentPromotions(ImmutableArray<BoundExpression>.Builder arguments, int startIndex)
        {
            for (var i = startIndex; i < arguments.Count; i++)
                arguments[i] = ApplyDefaultArgumentPromotion(arguments[i]);
        }
        private BoundExpression ApplyDefaultArgumentPromotion(BoundExpression argument)
        {
            if (argument.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float })
                return ConvertCallArgument(argument, _types.Builtin(BuiltinTypeKind.Double));
            return ConvertCallArgument(argument, IntegerPromote(argument.Type));
        }
        private BoundExpression ConvertCallArgument(BoundExpression argument, QualifiedType targetType)
        {
            if (argument.Type.IsError || targetType.IsError || SameType(argument.Type, targetType))
                return argument;

            return new BoundConversionExpression(
                argument.Syntax as ExpressionSyntax,
                argument,
                targetType,
                BoundValueKind.RValue,
                BoundConversionKind.Implicit);
        }
        private BoundExpression BindElementAccessExpression(ElementAccessExpressionSyntax syntax)
        {
            var expression = ApplyDefaultConversions(BindExpression(syntax.Expression));
            var index = syntax.Index is null
                ? null
                : ApplyDefaultConversions(BindExpression(syntax.Index));

            if (index is not null && !IsIntegerType(index.Type) && !index.Type.IsError)
                Report("Array subscript must have integer type.", SpanOf(syntax.Index!));

            if (TryGetPointeeType(expression.Type, out var elementType))
            {
                return new BoundElementAccessExpression(
                    syntax,
                    expression,
                    index,
                    elementType);
            }

            Report($"Expression of type '{expression.Type.ToDisplayString()}' is not subscriptable.", SpanOf(syntax.Expression));
            return new BoundElementAccessExpression(syntax, expression, index, ErrorType);
        }

        private BoundExpression BindMemberAccessExpression(MemberAccessExpressionSyntax syntax)
        {
            var expression = BindExpression(syntax.Expression);
            var accessTarget = syntax.OperatorToken.Kind == SyntaxKind.ArrowToken
                ? ApplyDefaultConversions(expression)
                : expression;

            QualifiedType aggregateType = ErrorType;
            var hasAggregateType = false;

            if (syntax.OperatorToken.Kind == SyntaxKind.DotToken)
            {
                if (accessTarget.Type.Type.Kind is TypeKind.Struct or TypeKind.Union)
                {
                    aggregateType = accessTarget.Type;
                    hasAggregateType = true;
                }
                else if (!accessTarget.Type.IsError)
                {
                    Report("Member access '.' requires a struct or union object.", syntax.OperatorToken.Span);
                }
            }
            else if (syntax.OperatorToken.Kind == SyntaxKind.ArrowToken)
            {
                if (TryGetPointeeType(accessTarget.Type, out var pointee) &&
                    pointee.Type.Kind is TypeKind.Struct or TypeKind.Union)
                {
                    aggregateType = pointee;
                    hasAggregateType = true;
                }
                else if (!accessTarget.Type.IsError)
                {
                    Report("Member access '->' requires a pointer to struct or union.", syntax.OperatorToken.Span);
                }
            }

            if (hasAggregateType && TryGetTagSymbol(aggregateType, out var tag))
            {
                if (!tag.IsComplete)
                {
                    Report(
                        $"Cannot access member '{syntax.NameToken.Text}' of incomplete {tag.TagKind.ToString().ToLowerInvariant()} type '{tag.Name}'.",
                        syntax.NameToken.Span);
                }
                else if (TryLookupField(tag, syntax.NameToken.Text, out var field))
                {
                    var fieldType = WithAddedQualifiers(field.Type, aggregateType.Qualifiers);

                    return new BoundMemberAccessExpression(
                        syntax,
                        accessTarget,
                        syntax.OperatorToken,
                        syntax.NameToken,
                        field,
                        fieldType,
                        BoundValueKind.LValue);
                }
                else
                {
                    Report(
                        $"'{tag.TagKind.ToString().ToLowerInvariant()} {tag.Name}' has no member named '{syntax.NameToken.Text}'.",
                        syntax.NameToken.Span);
                }
            }

            return new BoundMemberAccessExpression(
                syntax,
                accessTarget,
                syntax.OperatorToken,
                syntax.NameToken,
                field: null,
                ErrorType,
                BoundValueKind.Error);
        }

        private static QualifiedType WithAddedQualifiers(QualifiedType type, TypeQualifiers qualifiers)
            => qualifiers == TypeQualifiers.None
                ? type
                : new QualifiedType(type.Type, type.Qualifiers | qualifiers);

        private static bool TryGetTagSymbol(QualifiedType type, out TagSymbol tag)
        {
            if (type.Type is TagType tagType)
            {
                tag = tagType.Symbol;
                return true;
            }

            tag = null!;
            return false;
        }

        private static bool TryLookupField(TagSymbol tag, string name, out FieldSymbol field)
        {
            return TryLookupField(tag, name, new HashSet<TagSymbol>(), out field);
        }

        private static bool TryLookupField(
            TagSymbol tag,
            string name,
            HashSet<TagSymbol> visited,
            out FieldSymbol field)
        {
            if (!visited.Add(tag))
            {
                field = null!;
                return false;
            }

            if (tag.TryGetField(name, out var directField) && directField is not null)
            {
                field = directField;
                return true;
            }

            foreach (var anonymousField in tag.Fields)
            {
                if (anonymousField.Name.Length != 0 ||
                    anonymousField.Type.Type is not TagType anonymousTagType)
                {
                    continue;
                }

                if (anonymousTagType.Symbol.TagKind is not TagKind.Struct and not TagKind.Union)
                    continue;

                if (TryLookupField(anonymousTagType.Symbol, name, visited, out field))
                    return true;
            }

            field = null!;
            return false;
        }

        private BoundExpression ApplyDefaultConversions(BoundExpression expression)
        {
            if (expression.Type.IsError)
                return expression;

            if (expression.Type.Type is ArrayType array)
            {
                return new BoundConversionExpression(
                    expression.Syntax as ExpressionSyntax,
                    expression,
                    new QualifiedType(_types.PointerTo(array.ElementType)),
                    BoundValueKind.RValue,
                    BoundConversionKind.ArrayToPointer);
            }

            if (expression.Type.Type is FunctionType)
            {
                return new BoundConversionExpression(
                    expression.Syntax as ExpressionSyntax,
                    expression,
                    new QualifiedType(_types.PointerTo(expression.Type)),
                    BoundValueKind.RValue,
                    BoundConversionKind.FunctionToPointer);
            }

            if (expression.ValueKind == BoundValueKind.LValue)
            {
                return new BoundConversionExpression(
                    expression.Syntax as ExpressionSyntax,
                    expression,
                    expression.Type,
                    BoundValueKind.RValue,
                    BoundConversionKind.LValueToRValue);
            }

            return expression;
        }

        private QualifiedType BindTypeName(ImmutableArray<SyntaxToken> tokens, Scope scope)
        {
            if (tokens.IsDefaultOrEmpty)
                return ErrorType;

            SplitTypeNameTokens(tokens, out var specifierTokens, out var declaratorTokens);

            if (specifierTokens.Length == 0)
                return ErrorType;

            var specifiers = DeclarationTypeParser.ParseSpecifiers(specifierTokens, scope, _types);
            if (declaratorTokens.Length == 0)
                return specifiers.BaseType;

            return DeclaratorTypeBuilder.Build(
                new DeclaratorSyntax(declaratorTokens, identifier: null),
                specifiers.BaseType,
                _types,
                scope);
        }

        private static void SplitTypeNameTokens(
            ImmutableArray<SyntaxToken> tokens,
            out ImmutableArray<SyntaxToken> specifierTokens,
            out ImmutableArray<SyntaxToken> declaratorTokens)
        {
            var specifiers = ImmutableArray.CreateBuilder<SyntaxToken>();
            var index = 0;

            while (index < tokens.Length)
            {
                var token = tokens[index];

                if (!IsTypeNameSpecifierToken(token.Kind))
                    break;

                specifiers.Add(token);
                index++;

                if (token.Kind is SyntaxKind.StructKeyword or SyntaxKind.UnionKeyword or SyntaxKind.EnumKeyword)
                {
                    if (index < tokens.Length &&
                        tokens[index].Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    {
                        specifiers.Add(tokens[index]);
                        index++;
                    }

                    if (index < tokens.Length && tokens[index].Kind == SyntaxKind.OpenBraceToken)
                        ReadBalancedTokenSequence(tokens, specifiers, ref index);

                    continue;
                }

                if (IsParenthesizedTypeSpecifier(token.Kind) &&
                    index < tokens.Length &&
                    tokens[index].Kind == SyntaxKind.OpenParenToken)
                {
                    ReadBalancedTokenSequence(tokens, specifiers, ref index);
                    continue;
                }
            }

            specifierTokens = specifiers.ToImmutable();
            declaratorTokens = tokens.Skip(index).ToImmutableArray();
        }

        private static void ReadBalancedTokenSequence(
            ImmutableArray<SyntaxToken> tokens,
            ImmutableArray<SyntaxToken>.Builder destination,
            ref int index)
        {
            if (index >= tokens.Length)
                return;

            var openKind = tokens[index].Kind;
            var closeKind = openKind switch
            {
                SyntaxKind.OpenParenToken => SyntaxKind.CloseParenToken,
                SyntaxKind.OpenBraceToken => SyntaxKind.CloseBraceToken,
                SyntaxKind.OpenBracketToken => SyntaxKind.CloseBracketToken,
                _ => SyntaxKind.None,
            };

            if (closeKind == SyntaxKind.None)
                return;

            var depth = 0;
            while (index < tokens.Length)
            {
                var token = tokens[index++];
                destination.Add(token);

                if (token.Kind == openKind)
                {
                    depth++;
                    continue;
                }

                if (token.Kind == closeKind)
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
            }
        }

        private static bool IsParenthesizedTypeSpecifier(SyntaxKind kind)
        {
            return kind is SyntaxKind.AtomicKeyword
                or SyntaxKind.UnderscoreAtomicKeyword
                or SyntaxKind.TypeofKeyword
                or SyntaxKind.TypeofUnqualKeyword
                or SyntaxKind.TypeofExtensionKeyword;
        }

        private static bool IsTypeNameSpecifierToken(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.BoolKeyword:
                case SyntaxKind.UnderscoreBoolKeyword:
                case SyntaxKind.CharKeyword:
                case SyntaxKind.ShortKeyword:
                case SyntaxKind.IntKeyword:
                case SyntaxKind.LongKeyword:
                case SyntaxKind.SignedKeyword:
                case SyntaxKind.UnsignedKeyword:
                case SyntaxKind.FloatKeyword:
                case SyntaxKind.DoubleKeyword:
                case SyntaxKind.StructKeyword:
                case SyntaxKind.UnionKeyword:
                case SyntaxKind.EnumKeyword:
                case SyntaxKind.TypedefNameToken:
                case SyntaxKind.TypeofKeyword:
                case SyntaxKind.TypeofUnqualKeyword:
                case SyntaxKind.TypeofExtensionKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.ConstExtensionKeyword:
                case SyntaxKind.VolatileKeyword:
                case SyntaxKind.VolatileExtensionKeyword:
                case SyntaxKind.RestrictKeyword:
                case SyntaxKind.RestrictExtensionKeyword:
                case SyntaxKind.AtomicKeyword:
                case SyntaxKind.UnderscoreAtomicKeyword:
                    return true;

                default:
                    return false;
            }
        }

        private FunctionType? GetFunctionType(QualifiedType type)
        {
            if (type.Type is FunctionType functionType)
                return functionType;

            if (type.Type is PointerType pointer && pointer.PointeeType.Type is FunctionType pointedFunctionType)
                return pointedFunctionType;

            return null;
        }

        private bool CanConvert(QualifiedType from, QualifiedType to)
        {
            if (from.IsError || to.IsError)
                return true;

            if (SameType(from, to))
                return true;

            if (IsArithmeticType(from) && IsArithmeticType(to))
                return true;

            if (IsPointerType(from) && IsPointerType(to))
                return true;

            if (IsIntegerType(from) && IsPointerType(to))
                return true;

            if (IsPointerType(from) && IsIntegerType(to))
                return true;

            return false;
        }

        private bool CanCompare(QualifiedType left, QualifiedType right)
        {
            if (left.IsError || right.IsError)
                return true;

            if (IsArithmeticType(left) && IsArithmeticType(right))
                return true;

            if (IsPointerType(left) && IsPointerType(right))
                return true;

            if (IsPointerType(left) && IsIntegerType(right))
                return true;

            if (IsIntegerType(left) && IsPointerType(right))
                return true;

            return false;
        }

        private QualifiedType CommonConditionalType(QualifiedType left, QualifiedType right)
        {
            if (left.IsError || right.IsError)
                return ErrorType;

            if (SameType(left, right))
                return left;

            if (IsArithmeticType(left) && IsArithmeticType(right))
                return UsualArithmeticConversion(left, right);

            if (IsPointerType(left) && IsPointerType(right))
                return left;

            return ErrorType;
        }

        private QualifiedType UsualArithmeticConversion(QualifiedType left, QualifiedType right)
        {
            if (left.IsError || right.IsError)
                return ErrorType;

            if (!IsArithmeticType(left) || !IsArithmeticType(right))
                return ErrorType;

            var leftRank = ArithmeticRank(left);
            var rightRank = ArithmeticRank(right);
            return leftRank >= rightRank ? IntegerPromote(left) : IntegerPromote(right);
        }

        private QualifiedType IntegerPromote(QualifiedType type)
        {
            if (!IsIntegerType(type))
                return type;

            if (type.Type is BuiltinType builtin)
            {
                switch (builtin.BuiltinKind)
                {
                    case BuiltinTypeKind.Bool:
                    case BuiltinTypeKind.Char:
                    case BuiltinTypeKind.SignedChar:
                    case BuiltinTypeKind.UnsignedChar:
                    case BuiltinTypeKind.Short:
                    case BuiltinTypeKind.UnsignedShort:
                        return _types.Builtin(BuiltinTypeKind.Int);
                }
            }

            return type;
        }

        private int ArithmeticRank(QualifiedType type)
        {
            if (type.Type is not BuiltinType builtin)
                return 0;

            switch (builtin.BuiltinKind)
            {
                case BuiltinTypeKind.Bool:
                    return 1;
                case BuiltinTypeKind.Char:
                case BuiltinTypeKind.SignedChar:
                case BuiltinTypeKind.UnsignedChar:
                    return 2;
                case BuiltinTypeKind.Short:
                case BuiltinTypeKind.UnsignedShort:
                    return 3;
                case BuiltinTypeKind.Int:
                case BuiltinTypeKind.UnsignedInt:
                    return 4;
                case BuiltinTypeKind.Long:
                case BuiltinTypeKind.UnsignedLong:
                    return 5;
                case BuiltinTypeKind.LongLong:
                case BuiltinTypeKind.UnsignedLongLong:
                    return 6;
                case BuiltinTypeKind.Float:
                    return 7;
                case BuiltinTypeKind.Double:
                    return 8;
                case BuiltinTypeKind.LongDouble:
                    return 9;
                default:
                    return 0;
            }
        }

        private bool IsModifiableLValue(BoundExpression expression)
        {
            if (expression.ValueKind != BoundValueKind.LValue)
                return false;

            if (expression.Type.Qualifiers.HasFlag(TypeQualifiers.Const))
                return false;

            return !expression.Type.IsError;
        }

        private bool IsScalarType(QualifiedType type)
            => IsArithmeticType(type) || IsPointerType(type);
        private static bool IsAggregateType(QualifiedType type)
            => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array;
        private static bool IsPointerType(QualifiedType type)
            => type.Type is PointerType;

        private bool TryGetPointeeType(QualifiedType type, out QualifiedType pointeeType)
        {
            if (type.Type is PointerType pointer)
            {
                pointeeType = pointer.PointeeType;
                return true;
            }

            if (type.Type is ArrayType array)
            {
                pointeeType = array.ElementType;
                return true;
            }

            pointeeType = ErrorType;
            return false;
        }

        private bool IsArithmeticType(QualifiedType type)
            => IsIntegerType(type) || IsFloatingType(type);

        private static bool IsFloatingType(QualifiedType type)
        {
            if (type.Type is not BuiltinType builtin)
                return false;

            return builtin.BuiltinKind is BuiltinTypeKind.Float
                or BuiltinTypeKind.Double
                or BuiltinTypeKind.LongDouble;
        }

        private static bool IsIntegerType(QualifiedType type)
        {
            if (type.Type is BuiltinType builtin)
            {
                return builtin.BuiltinKind is BuiltinTypeKind.Bool
                    or BuiltinTypeKind.Char
                    or BuiltinTypeKind.SignedChar
                    or BuiltinTypeKind.UnsignedChar
                    or BuiltinTypeKind.Short
                    or BuiltinTypeKind.UnsignedShort
                    or BuiltinTypeKind.Int
                    or BuiltinTypeKind.UnsignedInt
                    or BuiltinTypeKind.Long
                    or BuiltinTypeKind.UnsignedLong
                    or BuiltinTypeKind.LongLong
                    or BuiltinTypeKind.UnsignedLongLong;
            }

            return type.Type is EnumType;
        }

        private bool SameType(QualifiedType left, QualifiedType right)
        {
            if (left.Qualifiers != right.Qualifiers)
                return false;

            if (ReferenceEquals(left.Type, right.Type))
                return true;

            return left.ToDisplayString() == right.ToDisplayString();
        }

        private QualifiedType InferIntegerLiteralType(string text, out object? value)
        {
            value = TryParseIntegerLiteral(text, out var parsed) ? parsed : null;

            if (text.EndsWith("ULL", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("LLU", StringComparison.OrdinalIgnoreCase))
            {
                return _types.Builtin(BuiltinTypeKind.UnsignedLongLong);
            }

            if (text.EndsWith("LL", StringComparison.OrdinalIgnoreCase))
                return _types.Builtin(BuiltinTypeKind.LongLong);

            if (text.EndsWith("UL", StringComparison.OrdinalIgnoreCase) ||
                text.EndsWith("LU", StringComparison.OrdinalIgnoreCase))
            {
                return _types.Builtin(BuiltinTypeKind.UnsignedLong);
            }

            if (text.EndsWith("U", StringComparison.OrdinalIgnoreCase))
                return _types.Builtin(BuiltinTypeKind.UnsignedInt);

            if (text.EndsWith("L", StringComparison.OrdinalIgnoreCase))
                return _types.Builtin(BuiltinTypeKind.Long);

            return _types.Builtin(BuiltinTypeKind.Int);
        }

        private QualifiedType InferFloatingLiteralType(string text)
        {
            if (text.EndsWith("F", StringComparison.OrdinalIgnoreCase))
                return _types.Builtin(BuiltinTypeKind.Float);

            if (text.EndsWith("L", StringComparison.OrdinalIgnoreCase))
                return _types.Builtin(BuiltinTypeKind.LongDouble);

            return _types.Builtin(BuiltinTypeKind.Double);
        }

        private static bool TryParseIntegerLiteral(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = StripIntegerSuffix(text.Replace("'", string.Empty));
            var numberStyles = NumberStyles.Integer;
            var numberBase = 10;

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                numberBase = 16;
                trimmed = trimmed.Substring(2);
                numberStyles = NumberStyles.HexNumber;
            }
            else if (trimmed.Length > 1 && trimmed[0] == '0')
            {
                numberBase = 8;
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.Length == 0)
            {
                value = 0;
                return true;
            }

            if (numberBase == 8)
            {
                try
                {
                    long result = 0;
                    foreach (var ch in trimmed)
                    {
                        if (ch < '0' || ch > '7')
                            return false;

                        result = checked(result * 8 + (ch - '0'));
                    }

                    value = result;
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            if (long.TryParse(trimmed, numberStyles, CultureInfo.InvariantCulture, out value))
                return true;

            return false;
        }

        private static string StripIntegerSuffix(string text)
        {
            var end = text.Length;
            while (end > 0)
            {
                var ch = text[end - 1];
                if (ch is 'u' or 'U' or 'l' or 'L')
                {
                    end--;
                    continue;
                }

                break;
            }

            return text.Substring(0, end);
        }

        private void AnalyzeFunctionControlFlow(FunctionSymbol? function, BoundCompoundStatement body)
        {
            ControlFlowAnalyzer.Analyze(function, body, Report, ReportWarning);
        }

        private sealed class SwitchContext
        {
            private readonly Dictionary<long, CaseStatementSyntax> _caseLabels = new();
            private DefaultStatementSyntax? _defaultLabel;

            public bool TryDeclareCase(long value, CaseStatementSyntax syntax, out CaseStatementSyntax? existing)
            {
                if (_caseLabels.TryGetValue(value, out existing))
                    return false;

                _caseLabels.Add(value, syntax);
                return true;
            }

            public bool TryDeclareDefault(DefaultStatementSyntax syntax, out DefaultStatementSyntax? existing)
            {
                existing = _defaultLabel;
                if (_defaultLabel is not null)
                    return false;

                _defaultLabel = syntax;
                return true;
            }
        }

        private readonly struct FlowResult
        {
            public bool CanFallThrough { get; }
            public bool HasBreak { get; }
            public bool HasContinue { get; }

            public FlowResult(bool canFallThrough, bool hasBreak, bool hasContinue)
            {
                CanFallThrough = canFallThrough;
                HasBreak = hasBreak;
                HasContinue = hasContinue;
            }

            public static FlowResult FallThrough => new FlowResult(true, false, false);
            public static FlowResult NoFallThrough => new FlowResult(false, false, false);
            public static FlowResult Break => new FlowResult(false, true, false);
            public static FlowResult Continue => new FlowResult(false, false, true);

            public static FlowResult Merge(FlowResult left, FlowResult right)
                => new FlowResult(
                    left.CanFallThrough || right.CanFallThrough,
                    left.HasBreak || right.HasBreak,
                    left.HasContinue || right.HasContinue);
        }

        private sealed class ControlFlowAnalyzer
        {
            private readonly Action<string, TextSpan> _reportError;
            private readonly Action<string, TextSpan> _reportWarning;
            private readonly HashSet<LabelSymbol> _reachableGotoTargets = new();
            private readonly Stack<bool> _switchEntryReachable = new();
            private bool _collectGotoTargets;
            private bool _reportUnreachable;
            private bool _gotoTargetSetChanged;

            private ControlFlowAnalyzer(
                Action<string, TextSpan> reportError,
                Action<string, TextSpan> reportWarning)
            {
                _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
                _reportWarning = reportWarning ?? throw new ArgumentNullException(nameof(reportWarning));
            }

            public static void Analyze(
                FunctionSymbol? function,
                BoundCompoundStatement body,
                Action<string, TextSpan> reportError,
                Action<string, TextSpan> reportWarning)
            {
                new ControlFlowAnalyzer(reportError, reportWarning).AnalyzeFunction(function, body);
            }

            private void AnalyzeFunction(FunctionSymbol? function, BoundCompoundStatement body)
            {
                do
                {
                    _gotoTargetSetChanged = false;
                    _collectGotoTargets = true;
                    _reportUnreachable = false;
                    AnalyzeCompound(body, isReachable: true);
                }
                while (_gotoTargetSetChanged);

                _collectGotoTargets = false;
                _reportUnreachable = true;
                var result = AnalyzeCompound(body, isReachable: true);

                if (RequiresReturnValue(function) && result.CanFallThrough)
                {
                    _reportError(
                        "Not all control paths return a value.",
                        SpanOf(function?.DeclaringSyntax ?? body.Syntax));
                }
            }

            private static bool RequiresReturnValue(FunctionSymbol? function)
            {
                var returnType = function?.FunctionType?.ReturnType;
                if (!returnType.HasValue || returnType.Value.IsError)
                    return false;

                return returnType.Value.Type is not BuiltinType builtin ||
                       builtin.BuiltinKind != BuiltinTypeKind.Void;
            }

            private FlowResult AnalyzeNode(BoundNode node, bool isReachable)
            {
                switch (node)
                {
                    case BoundStatement statement:
                        return AnalyzeStatement(statement, isReachable);

                    case BoundDeclaration:
                    case BoundStaticAssertDeclaration:
                        if (!isReachable)
                            ReportUnreachable(node);
                        return isReachable ? FlowResult.FallThrough : FlowResult.NoFallThrough;

                    default:
                        return isReachable ? FlowResult.FallThrough : FlowResult.NoFallThrough;
                }
            }

            private FlowResult AnalyzeStatement(BoundStatement statement, bool isReachable)
            {
                var targetReachable = IsReachableBranchTarget(statement);
                var effectiveReachable = isReachable || targetReachable;

                if (!effectiveReachable &&
                    statement is not BoundCompoundStatement &&
                    !ContainsReachableBranchTarget(statement))
                {
                    ReportUnreachable(statement);
                    return FlowResult.NoFallThrough;
                }

                switch (statement)
                {
                    case BoundCompoundStatement compound:
                        return AnalyzeCompound(compound, effectiveReachable);

                    case BoundIfStatement ifStatement:
                        return AnalyzeIfStatement(ifStatement, effectiveReachable);

                    case BoundSwitchStatement switchStatement:
                        return AnalyzeSwitchStatement(switchStatement, effectiveReachable);

                    case BoundWhileStatement whileStatement:
                        return AnalyzeWhileStatement(whileStatement, effectiveReachable);

                    case BoundDoStatement doStatement:
                        return AnalyzeDoStatement(doStatement, effectiveReachable);

                    case BoundForStatement forStatement:
                        return AnalyzeForStatement(forStatement, effectiveReachable);

                    case BoundLabelStatement labelStatement:
                        return AnalyzeStatement(labelStatement.Statement, effectiveReachable);

                    case BoundCaseStatement caseStatement:
                        return AnalyzeStatement(caseStatement.Statement, effectiveReachable);

                    case BoundDefaultStatement defaultStatement:
                        return AnalyzeStatement(defaultStatement.Statement, effectiveReachable);

                    case BoundReturnStatement:
                        return FlowResult.NoFallThrough;

                    case BoundGotoStatement gotoStatement:
                        if (effectiveReachable && _collectGotoTargets && gotoStatement.Label is not null)
                        {
                            if (_reachableGotoTargets.Add(gotoStatement.Label))
                                _gotoTargetSetChanged = true;
                        }
                        return FlowResult.NoFallThrough;

                    case BoundBreakStatement:
                        return effectiveReachable ? FlowResult.Break : FlowResult.NoFallThrough;

                    case BoundContinueStatement:
                        return effectiveReachable ? FlowResult.Continue : FlowResult.NoFallThrough;

                    case BoundExpressionStatement:
                    case BoundEmptyStatement:
                    case BoundErrorStatement:
                    default:
                        return effectiveReachable ? FlowResult.FallThrough : FlowResult.NoFallThrough;
                }
            }

            private FlowResult AnalyzeCompound(BoundCompoundStatement compound, bool isReachable)
            {
                var reachable = isReachable;
                var hasBreak = false;
                var hasContinue = false;

                foreach (var member in compound.Members)
                {
                    var memberIsTarget = member is BoundStatement statement && IsReachableBranchTarget(statement);
                    var memberContainsTarget = member is BoundStatement statementWithTarget &&
                                               ContainsReachableBranchTarget(statementWithTarget);
                    var memberReachable = reachable || memberIsTarget || memberContainsTarget;
                    var result = AnalyzeNode(member, memberReachable);

                    hasBreak |= result.HasBreak;
                    hasContinue |= result.HasContinue;
                    reachable = result.CanFallThrough;
                }

                return new FlowResult(reachable, hasBreak, hasContinue);
            }

            private FlowResult AnalyzeIfStatement(BoundIfStatement ifStatement, bool isReachable)
            {
                var condition = TryGetKnownBool(ifStatement.Condition);

                var thenReachable = isReachable && condition != false;
                var thenResult = AnalyzeStatement(ifStatement.ThenStatement, thenReachable);

                FlowResult elseResult;
                if (ifStatement.ElseStatement is not null)
                {
                    var elseReachable = isReachable && condition != true;
                    elseResult = AnalyzeStatement(ifStatement.ElseStatement, elseReachable);
                }
                else
                {
                    elseResult = isReachable && condition != true
                        ? FlowResult.FallThrough
                        : FlowResult.NoFallThrough;
                }

                return FlowResult.Merge(thenResult, elseResult);
            }

            private FlowResult AnalyzeSwitchStatement(BoundSwitchStatement switchStatement, bool isReachable)
            {
                var hasDefault = ContainsDefaultLabel(switchStatement.Statement);

                _switchEntryReachable.Push(isReachable);
                FlowResult bodyResult;
                try
                {
                    bodyResult = AnalyzeStatement(switchStatement.Statement, isReachable: false);
                }
                finally
                {
                    _switchEntryReachable.Pop();
                }

                var canSkipSwitchBody = isReachable && !hasDefault;
                return new FlowResult(
                    canSkipSwitchBody || bodyResult.CanFallThrough || bodyResult.HasBreak,
                    hasBreak: false,
                    hasContinue: bodyResult.HasContinue);
            }

            private FlowResult AnalyzeWhileStatement(BoundWhileStatement whileStatement, bool isReachable)
            {
                var condition = TryGetKnownBool(whileStatement.Condition);
                var bodyReachable = isReachable && condition != false;
                var bodyResult = AnalyzeStatement(whileStatement.Statement, bodyReachable);
                var canExitByCondition = isReachable && condition != true;

                return new FlowResult(
                    canExitByCondition || bodyResult.HasBreak,
                    hasBreak: false,
                    hasContinue: false);
            }

            private FlowResult AnalyzeDoStatement(BoundDoStatement doStatement, bool isReachable)
            {
                var bodyResult = AnalyzeStatement(doStatement.Statement, isReachable);
                var condition = TryGetKnownBool(doStatement.Condition);
                var canReachCondition = bodyResult.CanFallThrough || bodyResult.HasContinue;
                var canExitByCondition = canReachCondition && condition != true;

                return new FlowResult(
                    bodyResult.HasBreak || canExitByCondition,
                    hasBreak: false,
                    hasContinue: false);
            }

            private FlowResult AnalyzeForStatement(BoundForStatement forStatement, bool isReachable)
            {
                if (forStatement.Initializer is BoundNode initializer)
                    AnalyzeNode(initializer, isReachable);

                var condition = forStatement.Condition is null
                    ? true
                    : TryGetKnownBool(forStatement.Condition);

                var bodyReachable = isReachable && condition != false;
                var bodyResult = AnalyzeStatement(forStatement.Statement, bodyReachable);
                var canExitByCondition = isReachable && condition != true;

                return new FlowResult(
                    canExitByCondition || bodyResult.HasBreak,
                    hasBreak: false,
                    hasContinue: false);
            }

            private bool IsReachableBranchTarget(BoundStatement statement)
            {
                switch (statement)
                {
                    case BoundLabelStatement labelStatement:
                        return labelStatement.Label is not null &&
                               _reachableGotoTargets.Contains(labelStatement.Label);

                    case BoundCaseStatement:
                    case BoundDefaultStatement:
                        return _switchEntryReachable.Count != 0 && _switchEntryReachable.Peek();

                    default:
                        return false;
                }
            }

            private bool ContainsReachableBranchTarget(BoundStatement statement)
            {
                if (IsReachableBranchTarget(statement))
                    return true;

                switch (statement)
                {
                    case BoundCompoundStatement compound:
                        foreach (var member in compound.Members)
                        {
                            if (member is BoundStatement child && ContainsReachableBranchTarget(child))
                                return true;
                        }
                        return false;

                    case BoundIfStatement ifStatement:
                        return ContainsReachableBranchTarget(ifStatement.ThenStatement) ||
                               (ifStatement.ElseStatement is not null &&
                                ContainsReachableBranchTarget(ifStatement.ElseStatement));

                    case BoundSwitchStatement switchStatement:
                        return ContainsReachableBranchTarget(switchStatement.Statement);

                    case BoundWhileStatement whileStatement:
                        return ContainsReachableBranchTarget(whileStatement.Statement);

                    case BoundDoStatement doStatement:
                        return ContainsReachableBranchTarget(doStatement.Statement);

                    case BoundForStatement forStatement:
                        return ContainsReachableBranchTarget(forStatement.Statement);

                    case BoundLabelStatement labelStatement:
                        return ContainsReachableBranchTarget(labelStatement.Statement);

                    case BoundCaseStatement caseStatement:
                        return ContainsReachableBranchTarget(caseStatement.Statement);

                    case BoundDefaultStatement defaultStatement:
                        return ContainsReachableBranchTarget(defaultStatement.Statement);

                    default:
                        return false;
                }
            }

            private static bool ContainsDefaultLabel(BoundStatement statement)
            {
                switch (statement)
                {
                    case BoundDefaultStatement:
                        return true;

                    case BoundCompoundStatement compound:
                        foreach (var member in compound.Members)
                        {
                            if (member is BoundStatement child && ContainsDefaultLabel(child))
                                return true;
                        }
                        return false;

                    case BoundIfStatement ifStatement:
                        return ContainsDefaultLabel(ifStatement.ThenStatement) ||
                               (ifStatement.ElseStatement is not null &&
                                ContainsDefaultLabel(ifStatement.ElseStatement));

                    case BoundWhileStatement whileStatement:
                        return ContainsDefaultLabel(whileStatement.Statement);

                    case BoundDoStatement doStatement:
                        return ContainsDefaultLabel(doStatement.Statement);

                    case BoundForStatement forStatement:
                        return ContainsDefaultLabel(forStatement.Statement);

                    case BoundLabelStatement labelStatement:
                        return ContainsDefaultLabel(labelStatement.Statement);

                    case BoundCaseStatement caseStatement:
                        return ContainsDefaultLabel(caseStatement.Statement);

                    case BoundSwitchStatement:
                        return false;

                    default:
                        return false;
                }
            }

            private void ReportUnreachable(BoundNode node)
            {
                if (_reportUnreachable)
                    _reportWarning("Unreachable code.", SpanOf(node.Syntax));
            }
        }

        private static bool? TryGetKnownBool(BoundExpression expression)
        {
            if (TryEvaluateIntegerConstantValue(expression, out var integerValue))
                return integerValue != 0;

            if (TryGetConstantValue(expression, out var value))
            {
                switch (value)
                {
                    case bool boolean:
                        return boolean;
                    case float single:
                        return single != 0;
                    case double dbl:
                        return dbl != 0;
                    case decimal dec:
                        return dec != 0;
                }
            }

            return null;
        }

        private static bool TryEvaluateIntegerConstantValue(BoundExpression expression, out long value)
        {
            switch (expression)
            {
                case BoundConversionExpression conversion:
                    return TryEvaluateIntegerConstantValue(conversion.Expression, out value);

                case BoundParenthesizedExpression parenthesized:
                    return TryEvaluateIntegerConstantValue(parenthesized.Expression, out value);

                case BoundCastExpression cast:
                    return TryEvaluateIntegerConstantValue(cast.Expression, out value);

                case BoundSizeofExpression sizeofExpression:
                    return TryConvertConstantToLong(sizeofExpression.ConstantValue, out value);

                case BoundLiteralExpression literal:
                    return TryConvertConstantToLong(literal.ConstantValue, out value);

                case BoundUnaryExpression unary:
                    return TryEvaluateUnaryIntegerConstant(unary, out value);

                case BoundBinaryExpression binary:
                    return TryEvaluateBinaryIntegerConstant(binary, out value);

                case BoundConditionalExpression conditional:
                    return TryEvaluateConditionalIntegerConstant(conditional, out value);

                default:
                    return TryConvertConstantToLong(expression.ConstantValue, out value);
            }
        }

        private static bool TryEvaluateUnaryIntegerConstant(BoundUnaryExpression expression, out long value)
        {
            value = 0;
            if (!TryEvaluateIntegerConstantValue(expression.Operand, out var operand))
                return false;

            try
            {
                switch (expression.OperatorToken.Kind)
                {
                    case SyntaxKind.PlusToken:
                        value = operand;
                        return true;
                    case SyntaxKind.MinusToken:
                        value = checked(-operand);
                        return true;
                    case SyntaxKind.TildeToken:
                        value = ~operand;
                        return true;
                    case SyntaxKind.BangToken:
                        value = operand == 0 ? 1 : 0;
                        return true;
                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        private static bool TryEvaluateBinaryIntegerConstant(BoundBinaryExpression expression, out long value)
        {
            value = 0;

            if (!TryEvaluateIntegerConstantValue(expression.Left, out var left))
                return false;

            if (!TryEvaluateIntegerConstantValue(expression.Right, out var right))
                return false;

            try
            {
                switch (expression.OperatorToken.Kind)
                {
                    case SyntaxKind.StarToken:
                        value = checked(left * right);
                        return true;
                    case SyntaxKind.SlashToken:
                        if (right == 0)
                            return false;
                        value = left / right;
                        return true;
                    case SyntaxKind.PercentToken:
                        if (right == 0)
                            return false;
                        value = left % right;
                        return true;
                    case SyntaxKind.PlusToken:
                        value = checked(left + right);
                        return true;
                    case SyntaxKind.MinusToken:
                        value = checked(left - right);
                        return true;
                    case SyntaxKind.LessThanLessThanToken:
                        if (right < 0 || right >= 64)
                            return false;
                        value = checked(left << (int)right);
                        return true;
                    case SyntaxKind.GreaterThanGreaterThanToken:
                        if (right < 0 || right >= 64)
                            return false;
                        value = left >> (int)right;
                        return true;
                    case SyntaxKind.LessThanToken:
                        value = left < right ? 1 : 0;
                        return true;
                    case SyntaxKind.LessThanEqualsToken:
                        value = left <= right ? 1 : 0;
                        return true;
                    case SyntaxKind.GreaterThanToken:
                        value = left > right ? 1 : 0;
                        return true;
                    case SyntaxKind.GreaterThanEqualsToken:
                        value = left >= right ? 1 : 0;
                        return true;
                    case SyntaxKind.EqualsEqualsToken:
                        value = left == right ? 1 : 0;
                        return true;
                    case SyntaxKind.BangEqualsToken:
                        value = left != right ? 1 : 0;
                        return true;
                    case SyntaxKind.AmpersandToken:
                        value = left & right;
                        return true;
                    case SyntaxKind.PipeToken:
                        value = left | right;
                        return true;
                    case SyntaxKind.HatToken:
                        value = left ^ right;
                        return true;
                    case SyntaxKind.AmpersandAmpersandToken:
                        value = left != 0 && right != 0 ? 1 : 0;
                        return true;
                    case SyntaxKind.PipePipeToken:
                        value = left != 0 || right != 0 ? 1 : 0;
                        return true;
                    case SyntaxKind.CommaToken:
                        value = right;
                        return true;
                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        private static bool TryEvaluateConditionalIntegerConstant(BoundConditionalExpression expression, out long value)
        {
            value = 0;

            var condition = TryGetKnownBool(expression.Condition);
            if (!condition.HasValue)
                return false;

            return condition.Value
                ? TryEvaluateIntegerConstantValue(expression.WhenTrue, out value)
                : TryEvaluateIntegerConstantValue(expression.WhenFalse, out value);
        }

        private static bool TryGetConstantValue(BoundExpression expression, out object? value)
        {
            switch (expression)
            {
                case BoundConversionExpression conversion:
                    return TryGetConstantValue(conversion.Expression, out value);

                case BoundParenthesizedExpression parenthesized:
                    return TryGetConstantValue(parenthesized.Expression, out value);

                case BoundCastExpression cast:
                    return TryGetConstantValue(cast.Expression, out value);

                default:
                    value = expression.ConstantValue;
                    return value is not null;
            }
        }

        private static bool TryConvertConstantToLong(object? constantValue, out long value)
        {
            switch (constantValue)
            {
                case byte byteValue:
                    value = byteValue;
                    return true;
                case sbyte signedByteValue:
                    value = signedByteValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case ushort unsignedShortValue:
                    value = unsignedShortValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case uint unsignedIntValue:
                    value = unsignedIntValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case ulong unsignedLongValue when unsignedLongValue <= long.MaxValue:
                    value = (long)unsignedLongValue;
                    return true;
                case char charValue:
                    value = charValue;
                    return true;
                case bool boolValue:
                    value = boolValue ? 1 : 0;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static double? TryParseFloatingLiteral(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.TrimEnd('f', 'F', 'l', 'L').Replace("'", string.Empty);
            return double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : null;
        }

        private void Report(string message, TextSpan span)
            => _diagnostics.Add(SemanticDiagnostic.Error(message, span));

        private void ReportWarning(string message, TextSpan span)
            => _diagnostics.Add(SemanticDiagnostic.Warning(message, span));

        private QualifiedType ErrorType => new QualifiedType(CErrorType.Instance);

        private static TextSpan SpanOf(SyntaxNode? node)
        {
            switch (node)
            {
                case TranslationUnitSyntax translationUnit:
                    return translationUnit.EndOfFileToken.Span;

                case DeclarationSyntax declaration:
                    if (declaration.Declarators.Length > 0)
                        return SpanOf(declaration.Declarators[0]);
                    if (declaration.Specifiers.Length > 0)
                        return declaration.Specifiers[0].Span;
                    return declaration.SemicolonToken.Span;

                case FunctionDefinitionSyntax functionDefinition:
                    return functionDefinition.Declarator.Identifier?.Span ?? SpanOf(functionDefinition.Declarator);

                case StaticAssertDeclarationSyntax staticAssert:
                    return staticAssert.StaticAssertKeyword.Span;

                case InitDeclaratorSyntax initDeclarator:
                    return initDeclarator.Declarator.Identifier?.Span ?? SpanOf(initDeclarator.Declarator);

                case DeclaratorSyntax declarator:
                    if (declarator.Identifier.HasValue)
                        return declarator.Identifier.Value.Span;
                    if (declarator.Tokens.Length > 0)
                        return declarator.Tokens[0].Span;
                    return new TextSpan(0, 0);

                case CompoundStatementSyntax compound:
                    return compound.OpenBraceToken.Span;

                case IfStatementSyntax ifStatement:
                    return ifStatement.IfKeyword.Span;

                case SwitchStatementSyntax switchStatement:
                    return switchStatement.SwitchKeyword.Span;

                case WhileStatementSyntax whileStatement:
                    return whileStatement.WhileKeyword.Span;

                case DoStatementSyntax doStatement:
                    return doStatement.DoKeyword.Span;

                case ForStatementSyntax forStatement:
                    return forStatement.ForKeyword.Span;

                case BreakStatementSyntax breakStatement:
                    return breakStatement.BreakKeyword.Span;

                case ContinueStatementSyntax continueStatement:
                    return continueStatement.ContinueKeyword.Span;

                case GotoStatementSyntax gotoStatement:
                    return gotoStatement.GotoKeyword.Span;

                case LabelStatementSyntax labelStatement:
                    return labelStatement.IdentifierToken.Span;

                case CaseStatementSyntax caseStatement:
                    return caseStatement.CaseKeyword.Span;

                case DefaultStatementSyntax defaultStatement:
                    return defaultStatement.DefaultKeyword.Span;

                case ReturnStatementSyntax returnStatement:
                    return returnStatement.ReturnKeyword.Span;

                case ExpressionStatementSyntax expressionStatement:
                    return expressionStatement.Expression is null
                        ? expressionStatement.SemicolonToken.Span
                        : SpanOf(expressionStatement.Expression);

                case LiteralExpressionSyntax literal:
                    return literal.LiteralToken.Span;

                case NameExpressionSyntax name:
                    return name.IdentifierToken.Span;

                case UnaryExpressionSyntax unary:
                    return unary.OperatorToken.Span;

                case BinaryExpressionSyntax binary:
                    return SpanOf(binary.Left);

                case AssignmentExpressionSyntax assignment:
                    return SpanOf(assignment.Left);

                case ConditionalExpressionSyntax conditional:
                    return SpanOf(conditional.Condition);

                case CastExpressionSyntax cast:
                    return cast.OpenParenToken.Span;

                case SizeofExpressionSyntax sizeofExpression:
                    return sizeofExpression.Keyword.Span;

                case ParenthesizedExpressionSyntax parenthesized:
                    return parenthesized.OpenParenToken.Span;

                case CompoundLiteralExpressionSyntax compoundLiteral:
                    return compoundLiteral.OpenParenToken.Span;

                case GenericSelectionExpressionSyntax generic:
                    return generic.GenericKeyword.Span;

                case GenericAssociationSyntax genericAssociation:
                    return genericAssociation.DefaultKeyword?.Span ??
                           (genericAssociation.TypeNameTokens.Length > 0
                               ? genericAssociation.TypeNameTokens[0].Span
                               : genericAssociation.ColonToken.Span);

                case StatementExpressionSyntax statementExpression:
                    return statementExpression.OpenParenToken.Span;

                case CallExpressionSyntax call:
                    return SpanOf(call.Expression);

                case ElementAccessExpressionSyntax elementAccess:
                    return SpanOf(elementAccess.Expression);

                case MemberAccessExpressionSyntax memberAccess:
                    return SpanOf(memberAccess.Expression);

                case PostfixUnaryExpressionSyntax postfix:
                    return SpanOf(postfix.Expression);

                case InvalidExpressionSyntax invalid:
                    return invalid.Token.Span;

                default:
                    return new TextSpan(0, 0);
            }
        }
    }
}
