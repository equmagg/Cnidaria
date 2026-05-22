using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    internal sealed class Gimplifier
    {
        private readonly TypeCatalog _types = TypeCatalog.Instance;
        private readonly Dictionary<LabelSymbol, GimpleLabel> _labels = new();
        private readonly Stack<GimpleLabel> _breakTargets = new();
        private readonly Stack<GimpleLabel> _continueTargets = new();
        private readonly Stack<SwitchContext> _switches = new();
        private readonly List<BlockBuilder> _blocks = new();
        private readonly List<GimpleTemporaryValue> _temporaries = new();

        private FunctionSymbol? _currentFunction;
        private BlockBuilder? _currentBlock;
        private int _labelOrdinal;
        private int _temporaryOrdinal;

        private Gimplifier()
        {
        }

        internal static GimpleTree Lower(SemanticModel semanticModel)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Lower(semanticModel.GetBoundTree());
        }

        private static GimpleTree Lower(BoundTree boundTree)
        {
            if (boundTree is null)
                throw new ArgumentNullException(nameof(boundTree));

            var lowerer = new Gimplifier();
            var members = ImmutableArray.CreateBuilder<GimpleNode>();

            foreach (var member in boundTree.Root.Members)
                members.Add(lowerer.LowerTopLevelMember(member));

            return new GimpleTree(boundTree.SemanticModel, members.ToImmutable(), boundTree.Diagnostics);
        }

        private GimpleNode LowerTopLevelMember(BoundNode node)
        {
            switch (node)
            {
                case BoundFunctionDefinition function:
                    return LowerFunction(function);

                case BoundDeclaration declaration:
                    return LowerGlobalDeclaration(declaration);

                case BoundStaticAssertDeclaration staticAssert:
                    return new GimpleStaticAssertDeclaration(
                        staticAssert.Syntax,
                        LowerStaticExpression(staticAssert.Condition),
                        staticAssert.Message is null ? null : LowerStaticExpression(staticAssert.Message));

                default:
                    return new GimpleSkippedDeclaration(node.Syntax);
            }
        }

        private GimpleGlobalDeclaration LowerGlobalDeclaration(BoundDeclaration declaration)
        {
            var declarators = ImmutableArray.CreateBuilder<GimpleVariableDeclaration>();
            foreach (var declarator in declaration.Declarators)
                declarators.Add(LowerVariableDeclaration(declarator, declaration.StorageClass, includeInitializer: true));

            return new GimpleGlobalDeclaration(
                declaration.Syntax,
                declaration.StorageClass,
                declarators.ToImmutable());
        }

        private GimpleVariableDeclaration LowerVariableDeclaration(
            BoundDeclarator declarator,
            StorageClass storageClass,
            bool includeInitializer)
        {
            var initializer = includeInitializer && declarator.Initializer is not null
                ? LowerInitializerForDeclaration(declarator.Initializer)
                : null;

            return new GimpleVariableDeclaration(
                declarator.Symbol,
                declarator.Type,
                storageClass,
                initializer,
                declarator.Syntax);
        }

        private GimpleInitializer LowerInitializerForDeclaration(BoundInitializer initializer)
        {
            switch (initializer)
            {
                case BoundExpressionInitializer expressionInitializer:
                    return new GimpleExpressionInitializer(
                        expressionInitializer.Syntax,
                        expressionInitializer.TargetType,
                        LowerStaticExpression(expressionInitializer.Expression));

                case BoundInitializerList initializerList:
                    {
                        var items = ImmutableArray.CreateBuilder<GimpleInitializerListItem>();
                        foreach (var item in initializerList.Items)
                        {
                            items.Add(new GimpleInitializerListItem(
                                item.Syntax,
                                item.Designators,
                                LowerInitializerForDeclaration(item.Initializer)));
                        }

                        return new GimpleInitializerList(
                            initializerList.Syntax,
                            initializerList.TargetType,
                            items.ToImmutable());
                    }

                default:
                    return new GimpleExpressionInitializer(
                        initializer.Syntax,
                        initializer.TargetType,
                        CreateZeroValue(initializer.TargetType, initializer.Syntax));
            }
        }

        private GimpleFunctionDefinition LowerFunction(BoundFunctionDefinition function)
        {
            ResetFunctionState(function.Symbol);

            var entry = CreateGeneratedLabel("entry");
            StartBlock(entry);
            LowerCompoundStatement(function.Body);

            if (!IsCurrentBlockTerminated())
                Emit(new GimpleReturnStatement(function.Symbol, expression: null, function.Syntax));

            var blocks = _blocks.Select(static block => block.ToImmutable()).ToImmutableArray();
            return new GimpleFunctionDefinition(
                function.Syntax,
                function.Symbol,
                _temporaries.ToImmutableArray(),
                blocks,
                entry);
        }

        private void ResetFunctionState(FunctionSymbol? function)
        {
            _currentFunction = function;
            _labels.Clear();
            _breakTargets.Clear();
            _continueTargets.Clear();
            _switches.Clear();
            _blocks.Clear();
            _temporaries.Clear();
            _currentBlock = null;
            _labelOrdinal = 0;
            _temporaryOrdinal = 0;
        }

        private void LowerNode(BoundNode node)
        {
            switch (node)
            {
                case BoundDeclaration declaration:
                    LowerLocalDeclaration(declaration);
                    break;

                case BoundStaticAssertDeclaration:
                case BoundSkippedDeclaration:
                    break;

                case BoundStatement statement:
                    LowerStatement(statement);
                    break;

                default:
                    Emit(new GimpleNopStatement(node.Syntax));
                    break;
            }
        }

        private void LowerStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundCompoundStatement compound:
                    LowerCompoundStatement(compound);
                    break;

                case BoundIfStatement ifStatement:
                    LowerIfStatement(ifStatement);
                    break;

                case BoundSwitchStatement switchStatement:
                    LowerSwitchStatement(switchStatement);
                    break;

                case BoundWhileStatement whileStatement:
                    LowerWhileStatement(whileStatement);
                    break;

                case BoundDoStatement doStatement:
                    LowerDoStatement(doStatement);
                    break;

                case BoundForStatement forStatement:
                    LowerForStatement(forStatement);
                    break;

                case BoundBreakStatement breakStatement:
                    Emit(new GimpleGotoStatement(GetBreakTarget(), breakStatement.Syntax));
                    break;

                case BoundContinueStatement continueStatement:
                    Emit(new GimpleGotoStatement(GetContinueTarget(), continueStatement.Syntax));
                    break;

                case BoundGotoStatement gotoStatement:
                    Emit(new GimpleGotoStatement(GetLabel(gotoStatement.Label), gotoStatement.Syntax));
                    break;

                case BoundLabelStatement labelStatement:
                    StartBlock(GetLabel(labelStatement.Label, labelStatement.Syntax));
                    LowerStatement(labelStatement.Statement);
                    break;

                case BoundCaseStatement caseStatement:
                    StartBlock(GetCaseLabel(caseStatement));
                    LowerStatement(caseStatement.Statement);
                    break;

                case BoundDefaultStatement defaultStatement:
                    StartBlock(GetDefaultLabel(defaultStatement));
                    LowerStatement(defaultStatement.Statement);
                    break;

                case BoundReturnStatement returnStatement:
                    LowerReturnStatement(returnStatement);
                    break;

                case BoundExpressionStatement expressionStatement:
                    LowerExpressionForSideEffects(expressionStatement.Expression);
                    break;

                case BoundEmptyStatement emptyStatement:
                    Emit(new GimpleNopStatement(emptyStatement.Syntax));
                    break;

                case BoundErrorStatement errorStatement:
                    Emit(new GimpleNopStatement(errorStatement.Syntax));
                    break;

                default:
                    Emit(new GimpleNopStatement(statement.Syntax));
                    break;
            }
        }

        private void LowerCompoundStatement(BoundCompoundStatement statement)
        {
            foreach (var member in statement.Members)
                LowerNode(member);
        }

        private void LowerLocalDeclaration(BoundDeclaration declaration)
        {
            foreach (var declarator in declaration.Declarators)
            {
                Emit(new GimpleDeclarationStatement(
                    LowerVariableDeclaration(declarator, declaration.StorageClass, includeInitializer: false)));

                if (declarator.Symbol is not TypedSymbol typedSymbol || declarator.Initializer is null)
                    continue;

                var target = new GimpleSymbolValue(typedSymbol, typedSymbol.Type, declarator.Syntax);
                LowerInitializer(target, declarator.Initializer);
            }
        }

        private void LowerInitializer(GimplePlace target, BoundInitializer initializer)
        {
            switch (initializer)
            {
                case BoundExpressionInitializer expressionInitializer:
                    Emit(new GimpleAssignmentStatement(
                        target,
                        LowerRValue(expressionInitializer.Expression),
                        expressionInitializer.Syntax));
                    break;

                case BoundInitializerList initializerList:
                    LowerInitializerList(target, initializerList);
                    break;

                default:
                    EmitZeroInitialize(target, initializer.Syntax);
                    break;
            }
        }

        private void LowerInitializerList(GimplePlace target, BoundInitializerList initializer)
        {
            EmitZeroInitialize(target, initializer.Syntax);

            switch (target.Type.Type)
            {
                case ArrayType arrayType:
                    LowerArrayInitializerList(target, arrayType, initializer);
                    break;

                case TagType tagType when tagType.Symbol.TagKind == TagKind.Struct:
                    LowerStructInitializerList(target, tagType.Symbol, initializer);
                    break;

                case TagType tagType when tagType.Symbol.TagKind == TagKind.Union:
                    LowerUnionInitializerList(target, tagType.Symbol, initializer);
                    break;

                default:
                    LowerScalarInitializerList(target, initializer);
                    break;
            }
        }

        private void LowerScalarInitializerList(GimplePlace target, BoundInitializerList initializer)
        {
            if (initializer.Items.Length == 0)
                return;

            var first = initializer.Items[0];
            if (first.Designators.Length != 0)
            {
                if (TryApplyDesignators(target, target.Type, first.Designators, out var designatedTarget, out _))
                    LowerInitializer(designatedTarget, first.Initializer);

                return;
            }

            LowerInitializer(target, first.Initializer);
        }

        private void LowerArrayInitializerList(GimplePlace target, ArrayType arrayType, BoundInitializerList initializer)
        {
            var nextIndex = 0L;

            foreach (var item in initializer.Items)
            {
                if (item.Designators.Length != 0)
                {
                    if (TryApplyDesignators(target, target.Type, item.Designators, out var designatedTarget, out _))
                    {
                        LowerInitializer(designatedTarget, item.Initializer);

                        if (TryGetFirstArrayDesignatorIndex(item.Designators, out var index))
                            nextIndex = index + 1;
                    }

                    continue;
                }

                if (arrayType.Length.HasValue && nextIndex >= arrayType.Length.Value)
                    continue;

                var elementTarget = CreateElementAccess(target, nextIndex, arrayType.ElementType, item.Syntax);
                LowerInitializer(elementTarget, item.Initializer);
                nextIndex++;
            }
        }

        private void LowerStructInitializerList(GimplePlace target, TagSymbol tag, BoundInitializerList initializer)
        {
            var fields = tag.Fields;
            var nextField = 0;

            foreach (var item in initializer.Items)
            {
                if (item.Designators.Length != 0)
                {
                    if (TryApplyDesignators(target, target.Type, item.Designators, out var designatedTarget, out _))
                    {
                        LowerInitializer(designatedTarget, item.Initializer);

                        if (TryGetFirstFieldDesignator(tag, item.Designators, out var firstField))
                            nextField = Math.Min(firstField.Ordinal + 1, fields.Length);
                    }

                    continue;
                }

                if (nextField >= fields.Length)
                    continue;

                var field = fields[nextField++];
                var fieldTarget = CreateMemberAccess(target, field, item.Syntax);
                LowerInitializer(fieldTarget, item.Initializer);
            }
        }

        private void LowerUnionInitializerList(GimplePlace target, TagSymbol tag, BoundInitializerList initializer)
        {
            if (initializer.Items.Length == 0)
                return;

            foreach (var item in initializer.Items)
            {
                if (item.Designators.Length != 0)
                {
                    if (TryApplyDesignators(target, target.Type, item.Designators, out var designatedTarget, out _))
                        LowerInitializer(designatedTarget, item.Initializer);

                    continue;
                }

                if (tag.Fields.Length == 0)
                    continue;

                var fieldTarget = CreateMemberAccess(target, tag.Fields[0], item.Syntax);
                LowerInitializer(fieldTarget, item.Initializer);
            }
        }

        private void EmitZeroInitialize(GimplePlace target, SyntaxNode? syntax)
        {
            if (IsAggregateType(target.Type))
            {
                Emit(new GimpleZeroInitializeStatement(target, syntax));
                return;
            }

            Emit(new GimpleAssignmentStatement(target, CreateZeroValue(target.Type, syntax), syntax));
        }

        private GimpleConstantValue CreateZeroValue(QualifiedType type, SyntaxNode? syntax)
        {
            object value = 0;

            if (type.Type is BuiltinType builtin &&
                builtin.BuiltinKind is BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble)
            {
                value = 0.0;
            }

            return new GimpleConstantValue(value, type.IsError ? _types.Builtin(BuiltinTypeKind.Int) : type, syntax);
        }

        private static bool IsAggregateType(QualifiedType type)
        {
            if (type.Type is ArrayType)
                return true;

            return type.Type is TagType tagType &&
                   tagType.Symbol.TagKind is TagKind.Struct or TagKind.Union;
        }

        private bool TryApplyDesignators(
            GimplePlace root,
            QualifiedType rootType,
            ImmutableArray<DesignatorSyntax> designators,
            out GimplePlace target,
            out QualifiedType targetType)
        {
            target = root;
            targetType = rootType;

            foreach (var designator in designators)
            {
                switch (designator)
                {
                    case FieldDesignatorSyntax fieldDesignator:
                        {
                            if (!TryFindFieldPath(targetType, fieldDesignator.NameToken.Text, out var path))
                                return false;

                            foreach (var field in path)
                                target = CreateMemberAccess(target, field, fieldDesignator);

                            targetType = path[^1].Type;
                            break;
                        }

                    case ArrayDesignatorSyntax arrayDesignator:
                        {
                            if (targetType.Type is not ArrayType arrayType ||
                                !TryEvaluateIntegerConstantExpression(arrayDesignator.Expression, out var index))
                            {
                                return false;
                            }

                            target = CreateElementAccess(target, index, arrayType.ElementType, arrayDesignator);
                            targetType = arrayType.ElementType;
                            break;
                        }

                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool TryFindFieldPath(QualifiedType aggregateType, string name, out ImmutableArray<FieldSymbol> path)
        {
            path = ImmutableArray<FieldSymbol>.Empty;

            if (aggregateType.Type is not TagType tagType ||
                tagType.Symbol.TagKind is not TagKind.Struct and not TagKind.Union)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<FieldSymbol>();
            if (!TryFindFieldPath(tagType.Symbol, name, new HashSet<TagSymbol>(), builder))
                return false;

            path = builder.ToImmutable();
            return path.Length != 0;
        }

        private static bool TryFindFieldPath(
            TagSymbol tag,
            string name,
            HashSet<TagSymbol> visited,
            ImmutableArray<FieldSymbol>.Builder path)
        {
            if (!visited.Add(tag))
                return false;

            foreach (var field in tag.Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    path.Add(field);
                    return true;
                }
            }

            foreach (var field in tag.Fields)
            {
                if (field.Name.Length != 0 ||
                    field.Type.Type is not TagType anonymousTag ||
                    anonymousTag.Symbol.TagKind is not TagKind.Struct and not TagKind.Union)
                {
                    continue;
                }

                var startLength = path.Count;
                path.Add(field);

                if (TryFindFieldPath(anonymousTag.Symbol, name, visited, path))
                    return true;

                while (path.Count > startLength)
                    path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        private GimplePlace CreateMemberAccess(GimpleValue expression, FieldSymbol field, SyntaxNode? syntax)
        {
            var operatorToken = syntax is FieldDesignatorSyntax fieldDesignator
                ? fieldDesignator.DotToken
                : CreateSyntheticToken(SyntaxKind.DotToken, ".");

            var nameToken = syntax is FieldDesignatorSyntax namedFieldDesignator
                ? namedFieldDesignator.NameToken
                : CreateSyntheticToken(SyntaxKind.IdentifierToken, field.Name);

            return new GimpleMemberAccessExpression(
                expression,
                operatorToken,
                nameToken,
                field,
                field.Type,
                syntax);
        }

        private GimplePlace CreateElementAccess(GimpleValue expression, long index, QualifiedType elementType, SyntaxNode? syntax)
        {
            return new GimpleElementAccessExpression(
                expression,
                new GimpleConstantValue(index, _types.Builtin(BuiltinTypeKind.Long), syntax),
                elementType,
                syntax);
        }

        private static bool TryGetFirstFieldDesignator(
            TagSymbol tag,
            ImmutableArray<DesignatorSyntax> designators,
            out FieldSymbol field)
        {
            field = null!;

            if (designators.Length == 0 ||
                designators[0] is not FieldDesignatorSyntax fieldDesignator)
            {
                return false;
            }

            return tag.TryGetField(fieldDesignator.NameToken.Text, out field!) && field is not null;
        }

        private static bool TryGetFirstArrayDesignatorIndex(
            ImmutableArray<DesignatorSyntax> designators,
            out long index)
        {
            index = 0;

            return designators.Length != 0 &&
                   designators[0] is ArrayDesignatorSyntax arrayDesignator &&
                   TryEvaluateIntegerConstantExpression(arrayDesignator.Expression, out index);
        }

        private void LowerIfStatement(BoundIfStatement statement)
        {
            var thenLabel = CreateGeneratedLabel("if_then");
            var elseLabel = statement.ElseStatement is null ? null : CreateGeneratedLabel("if_else");
            var endLabel = CreateGeneratedLabel("if_end");

            EmitConditional(statement.Condition, thenLabel, elseLabel ?? endLabel);

            StartBlock(thenLabel);
            LowerStatement(statement.ThenStatement);
            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(endLabel));

            if (statement.ElseStatement is not null && elseLabel is not null)
            {
                StartBlock(elseLabel);
                LowerStatement(statement.ElseStatement);
                if (!IsCurrentBlockTerminated())
                    Emit(new GimpleGotoStatement(endLabel));
            }

            StartBlock(endLabel);
        }

        private void LowerWhileStatement(BoundWhileStatement statement)
        {
            var testLabel = CreateGeneratedLabel("while_test");
            var bodyLabel = CreateGeneratedLabel("while_body");
            var endLabel = CreateGeneratedLabel("while_end");

            Emit(new GimpleGotoStatement(testLabel));
            StartBlock(testLabel);
            EmitConditional(statement.Condition, bodyLabel, endLabel);

            StartBlock(bodyLabel);
            _breakTargets.Push(endLabel);
            _continueTargets.Push(testLabel);
            LowerStatement(statement.Statement);
            _continueTargets.Pop();
            _breakTargets.Pop();

            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(testLabel));

            StartBlock(endLabel);
        }

        private void LowerDoStatement(BoundDoStatement statement)
        {
            var bodyLabel = CreateGeneratedLabel("do_body");
            var testLabel = CreateGeneratedLabel("do_test");
            var endLabel = CreateGeneratedLabel("do_end");

            StartBlock(bodyLabel);
            _breakTargets.Push(endLabel);
            _continueTargets.Push(testLabel);
            LowerStatement(statement.Statement);
            _continueTargets.Pop();
            _breakTargets.Pop();

            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(testLabel));

            StartBlock(testLabel);
            EmitConditional(statement.Condition, bodyLabel, endLabel);
            StartBlock(endLabel);
        }

        private void LowerForStatement(BoundForStatement statement)
        {
            var testLabel = CreateGeneratedLabel("for_test");
            var bodyLabel = CreateGeneratedLabel("for_body");
            var incrementLabel = CreateGeneratedLabel("for_step");
            var endLabel = CreateGeneratedLabel("for_end");

            if (statement.Initializer is not null)
                LowerNode(statement.Initializer);

            Emit(new GimpleGotoStatement(testLabel));
            StartBlock(testLabel);

            if (statement.Condition is null)
                Emit(new GimpleGotoStatement(bodyLabel));
            else
                EmitConditional(statement.Condition, bodyLabel, endLabel);

            StartBlock(bodyLabel);
            _breakTargets.Push(endLabel);
            _continueTargets.Push(incrementLabel);
            LowerStatement(statement.Statement);
            _continueTargets.Pop();
            _breakTargets.Pop();

            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(incrementLabel));

            StartBlock(incrementLabel);
            if (statement.Increment is not null)
                LowerExpressionForSideEffects(statement.Increment);
            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(testLabel));

            StartBlock(endLabel);
        }

        private void LowerSwitchStatement(BoundSwitchStatement statement)
        {
            var endLabel = CreateGeneratedLabel("switch_end");
            var labels = CollectSwitchLabels(statement.Statement);
            var defaultLabel = labels.DefaultLabel ?? endLabel;
            var value = LowerExpression(statement.Expression);

            Emit(new GimpleSwitchStatement(value, labels.Cases, defaultLabel, statement.Syntax));

            _breakTargets.Push(endLabel);
            _switches.Push(new SwitchContext(endLabel, labels.CaseLabels, defaultLabel));
            LowerStatement(statement.Statement);
            _switches.Pop();
            _breakTargets.Pop();

            if (!IsCurrentBlockTerminated())
                Emit(new GimpleGotoStatement(endLabel));

            StartBlock(endLabel);
        }

        private void LowerReturnStatement(BoundReturnStatement statement)
        {
            var expression = statement.Expression is null
                ? null
                : LowerExpression(statement.Expression);

            Emit(new GimpleReturnStatement(statement.Function ?? _currentFunction, expression, statement.Syntax));
        }

        private GimpleValue LowerStaticExpression(BoundExpression expression)
        {
            if (TryEvaluateIntegerConstantValue(expression, out var integerValue))
                return new GimpleConstantValue(integerValue, expression.Type, expression.Syntax);

            if (expression.ConstantValue is not null)
                return new GimpleConstantValue(expression.ConstantValue, expression.Type, expression.Syntax);

            return LowerExpressionNoEmit(expression);
        }

        private GimpleValue LowerExpressionNoEmit(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundLiteralExpression literal:
                    return new GimpleConstantValue(literal.ConstantValue, literal.Type, literal.Syntax);

                case BoundNameExpression name:
                    return LowerNameExpression(name);

                case BoundParenthesizedExpression parenthesized:
                    return LowerExpressionNoEmit(parenthesized.Expression);

                case BoundConversionExpression conversion:
                    if (conversion.ConversionKind is BoundConversionKind.Identity or BoundConversionKind.LValueToRValue)
                        return LowerExpressionNoEmit(conversion.Expression);

                    return new GimpleConversionExpression(
                        LowerExpressionNoEmit(conversion.Expression),
                        conversion.Type,
                        ToGimpleConversionKind(conversion.ConversionKind),
                        conversion.Syntax);

                case BoundCastExpression cast:
                    return new GimpleCastExpression(
                        LowerExpressionNoEmit(cast.Expression),
                        cast.Type,
                        cast.Syntax);

                case BoundUnaryExpression unary when unary.OperatorToken.Kind == SyntaxKind.AmpersandToken:
                    return new GimpleAddressOfExpression(
                        LowerPlaceNoEmit(unary.Operand),
                        unary.Type,
                        unary.Syntax);

                case BoundUnaryExpression unary when unary.OperatorToken.Kind == SyntaxKind.StarToken:
                    return new GimpleIndirectExpression(
                        LowerExpressionNoEmit(unary.Operand),
                        unary.Type,
                        unary.Syntax);

                case BoundUnaryExpression unary:
                    return new GimpleUnaryExpression(
                        unary.OperatorToken,
                        LowerExpressionNoEmit(unary.Operand),
                        unary.Type,
                        unary.Syntax);

                case BoundBinaryExpression binary:
                    return new GimpleBinaryExpression(
                        LowerExpressionNoEmit(binary.Left),
                        binary.OperatorToken,
                        LowerExpressionNoEmit(binary.Right),
                        binary.Type,
                        binary.Syntax);

                case BoundSizeofExpression sizeofExpression:
                    return new GimpleConstantValue(sizeofExpression.ConstantValue, sizeofExpression.Type, sizeofExpression.Syntax);

                case BoundGenericSelectionExpression generic:
                    return generic.SelectedExpression is null
                        ? new GimpleErrorValue(generic.Syntax)
                        : LowerExpressionNoEmit(generic.SelectedExpression);

                case BoundElementAccessExpression elementAccess:
                    return new GimpleElementAccessExpression(
                        LowerExpressionNoEmit(elementAccess.Expression),
                        elementAccess.Index is null ? null : LowerExpressionNoEmit(elementAccess.Index),
                        elementAccess.Type,
                        elementAccess.Syntax);

                case BoundMemberAccessExpression memberAccess:
                    return new GimpleMemberAccessExpression(
                        LowerExpressionNoEmit(memberAccess.Expression),
                        memberAccess.OperatorToken,
                        memberAccess.NameToken,
                        memberAccess.Field,
                        memberAccess.Type,
                        memberAccess.Syntax);

                default:
                    if (TryEvaluateIntegerConstantValue(expression, out var integerValue))
                        return new GimpleConstantValue(integerValue, expression.Type, expression.Syntax);

                    return new GimpleErrorValue(expression.Syntax);
            }
        }

        private GimplePlace LowerPlaceNoEmit(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundNameExpression name when name.Symbol is not null:
                    return new GimpleSymbolValue(name.Symbol, name.Type, name.Syntax);

                case BoundParenthesizedExpression parenthesized:
                    return LowerPlaceNoEmit(parenthesized.Expression);

                case BoundUnaryExpression unary when unary.OperatorToken.Kind == SyntaxKind.StarToken:
                    return new GimpleIndirectExpression(LowerExpressionNoEmit(unary.Operand), unary.Type, unary.Syntax);

                case BoundElementAccessExpression elementAccess:
                    return new GimpleElementAccessExpression(
                        LowerExpressionNoEmit(elementAccess.Expression),
                        elementAccess.Index is null ? null : LowerExpressionNoEmit(elementAccess.Index),
                        elementAccess.Type,
                        elementAccess.Syntax);

                case BoundMemberAccessExpression memberAccess:
                    return new GimpleMemberAccessExpression(
                        LowerExpressionNoEmit(memberAccess.Expression),
                        memberAccess.OperatorToken,
                        memberAccess.NameToken,
                        memberAccess.Field,
                        memberAccess.Type,
                        memberAccess.Syntax);

                case BoundConversionExpression conversion when conversion.ConversionKind == BoundConversionKind.Identity:
                    return LowerPlaceNoEmit(conversion.Expression);

                default:
                    return new GimpleIndirectExpression(new GimpleErrorValue(expression.Syntax), expression.Type, expression.Syntax);
            }
        }

        private void LowerExpressionForSideEffects(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundCallExpression call when call.Type.Type.Kind == TypeKind.Builtin &&
                                               call.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void }:
                    Emit(new GimpleExpressionStatement(LowerCallExpression(call), expression.Syntax));
                    break;

                default:
                    _ = LowerExpression(expression);
                    break;
            }
        }

        private GimpleValue LowerExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundLiteralExpression literal:
                    return new GimpleConstantValue(literal.ConstantValue, literal.Type, literal.Syntax);

                case BoundNameExpression name:
                    return LowerNameExpression(name);

                case BoundParenthesizedExpression parenthesized:
                    return LowerExpression(parenthesized.Expression);

                case BoundConversionExpression conversion:
                    return LowerConversionExpression(conversion);

                case BoundCastExpression cast:
                    return Materialize(new GimpleCastExpression(
                        LowerExpression(cast.Expression),
                        cast.Type,
                        cast.Syntax));

                case BoundUnaryExpression unary:
                    return LowerUnaryExpression(unary);

                case BoundPostfixUnaryExpression postfix:
                    return LowerPostfixUnaryExpression(postfix);

                case BoundBinaryExpression binary:
                    return LowerBinaryExpression(binary);

                case BoundAssignmentExpression assignment:
                    return LowerAssignmentExpression(assignment);

                case BoundConditionalExpression conditional:
                    return LowerConditionalExpressionToValue(conditional);

                case BoundSizeofExpression sizeofExpression:
                    return new GimpleConstantValue(sizeofExpression.ConstantValue, sizeofExpression.Type, sizeofExpression.Syntax);

                case BoundCompoundLiteralExpression compoundLiteral:
                    return LowerCompoundLiteralExpression(compoundLiteral);

                case BoundGenericSelectionExpression generic:
                    return generic.SelectedExpression is null
                        ? new GimpleErrorValue(generic.Syntax)
                        : LowerExpression(generic.SelectedExpression);

                case BoundStatementExpression statementExpression:
                    return LowerStatementExpression(statementExpression);

                case BoundCallExpression call:
                    return Materialize(LowerCallExpression(call));

                case BoundElementAccessExpression elementAccess:
                    return LowerElementAccessExpression(elementAccess);

                case BoundMemberAccessExpression memberAccess:
                    return LowerMemberAccessExpression(memberAccess);

                case BoundErrorExpression error:
                    return new GimpleErrorValue(error.Syntax);

                default:
                    return new GimpleErrorValue(expression.Syntax);
            }
        }

        private GimpleValue LowerRValue(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundBinaryExpression binary when binary.OperatorToken.Kind == SyntaxKind.CommaToken:
                    LowerExpressionForSideEffects(binary.Left);
                    return LowerRValue(binary.Right);

                case BoundBinaryExpression binary when IsLogicalOperator(binary.OperatorToken.Kind):
                    return LowerConditionalExpressionToValue(binary);

                case BoundConditionalExpression conditional:
                    return LowerConditionalExpressionToValue(conditional);

                case BoundAssignmentExpression assignment:
                    return LowerAssignmentExpression(assignment);

                case BoundUnaryExpression unary when unary.OperatorToken.Kind is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken:
                    return LowerUnaryExpression(unary);

                case BoundPostfixUnaryExpression postfix:
                    return LowerPostfixUnaryExpression(postfix);

                case BoundCallExpression call:
                    return LowerCallExpression(call);

                default:
                    return LowerExpression(expression);
            }
        }

        private GimpleValue LowerNameExpression(BoundNameExpression expression)
        {
            if (expression.Symbol is null)
                return new GimpleErrorValue(expression.Syntax);

            return new GimpleSymbolValue(expression.Symbol, expression.Type, expression.Syntax);
        }

        private GimpleValue LowerConversionExpression(BoundConversionExpression expression)
        {
            if (expression.ConversionKind is BoundConversionKind.Identity or BoundConversionKind.LValueToRValue)
                return LowerExpression(expression.Expression);

            return Materialize(new GimpleConversionExpression(
                LowerExpression(expression.Expression),
                expression.Type,
                ToGimpleConversionKind(expression.ConversionKind),
                expression.Syntax));
        }

        private GimpleValue LowerUnaryExpression(BoundUnaryExpression expression)
        {
            switch (expression.OperatorToken.Kind)
            {
                case SyntaxKind.AmpersandToken:
                    return Materialize(new GimpleAddressOfExpression(
                        LowerPlace(expression.Operand),
                        expression.Type,
                        expression.Syntax));

                case SyntaxKind.StarToken:
                    return new GimpleIndirectExpression(
                        LowerExpression(expression.Operand),
                        expression.Type,
                        expression.Syntax);

                case SyntaxKind.PlusPlusToken:
                case SyntaxKind.MinusMinusToken:
                    return LowerPrefixIncrement(expression);

                default:
                    return Materialize(new GimpleUnaryExpression(
                        expression.OperatorToken,
                        LowerExpression(expression.Operand),
                        expression.Type,
                        expression.Syntax));
            }
        }

        private GimpleValue LowerPrefixIncrement(BoundUnaryExpression expression)
        {
            var target = LowerPlace(expression.Operand);
            var op = expression.OperatorToken.Kind == SyntaxKind.PlusPlusToken
                ? CreateSyntheticToken(SyntaxKind.PlusToken, "+", expression.OperatorToken)
                : CreateSyntheticToken(SyntaxKind.MinusToken, "-", expression.OperatorToken);

            var updated = new GimpleBinaryExpression(
                target,
                op,
                CreateIntegerOne(target.Type, expression.Syntax),
                target.Type,
                expression.Syntax);

            Emit(new GimpleAssignmentStatement(target, updated, expression.Syntax));
            return target;
        }

        private GimpleValue LowerPostfixUnaryExpression(BoundPostfixUnaryExpression expression)
        {
            var target = LowerPlace(expression.Operand);
            var oldValue = Materialize(target);
            var op = expression.OperatorToken.Kind == SyntaxKind.PlusPlusToken
                ? CreateSyntheticToken(SyntaxKind.PlusToken, "+", expression.OperatorToken)
                : CreateSyntheticToken(SyntaxKind.MinusToken, "-", expression.OperatorToken);

            var updated = new GimpleBinaryExpression(
                target,
                op,
                CreateIntegerOne(target.Type, expression.Syntax),
                target.Type,
                expression.Syntax);

            Emit(new GimpleAssignmentStatement(target, updated, expression.Syntax));
            return oldValue;
        }

        private GimpleValue LowerBinaryExpression(BoundBinaryExpression expression)
        {
            if (expression.OperatorToken.Kind == SyntaxKind.CommaToken)
            {
                LowerExpressionForSideEffects(expression.Left);
                return LowerExpression(expression.Right);
            }

            if (IsLogicalOperator(expression.OperatorToken.Kind))
                return LowerConditionalExpressionToValue(expression);

            return Materialize(new GimpleBinaryExpression(
                LowerExpression(expression.Left),
                expression.OperatorToken,
                LowerExpression(expression.Right),
                expression.Type,
                expression.Syntax));
        }

        private GimpleValue LowerAssignmentExpression(BoundAssignmentExpression expression)
        {
            var target = LowerPlace(expression.Left);
            GimpleValue value;

            if (expression.OperatorToken.Kind == SyntaxKind.EqualsToken)
            {
                value = LowerRValue(expression.Right);
            }
            else
            {
                var binaryOperator = GetCompoundAssignmentOperator(expression.OperatorToken);
                value = new GimpleBinaryExpression(
                    target,
                    binaryOperator,
                    LowerExpression(expression.Right),
                    target.Type,
                    expression.Syntax);
            }

            Emit(new GimpleAssignmentStatement(target, value, expression.Syntax));
            return target;
        }

        private GimpleValue LowerConditionalExpressionToValue(BoundExpression expression)
        {
            var result = CreateTemporary(expression.Type, expression.Syntax);
            var trueLabel = CreateGeneratedLabel("cond_true");
            var falseLabel = CreateGeneratedLabel("cond_false");
            var endLabel = CreateGeneratedLabel("cond_end");

            switch (expression)
            {
                case BoundConditionalExpression conditional:
                    EmitConditional(conditional.Condition, trueLabel, falseLabel);

                    StartBlock(trueLabel);
                    Emit(new GimpleAssignmentStatement(result, LowerRValue(conditional.WhenTrue), conditional.WhenTrue.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));

                    StartBlock(falseLabel);
                    Emit(new GimpleAssignmentStatement(result, LowerRValue(conditional.WhenFalse), conditional.WhenFalse.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));
                    break;

                case BoundBinaryExpression logical when logical.OperatorToken.Kind == SyntaxKind.AmpersandAmpersandToken:
                    EmitConditional(logical.Left, trueLabel, falseLabel);

                    StartBlock(trueLabel);
                    EmitConditional(logical.Right, CreateGeneratedLabel("logical_true"), falseLabel);
                    var rightTrue = CurrentTerminatorTrueTarget();

                    StartBlock(rightTrue);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerOne(result.Type, logical.Syntax), logical.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));

                    StartBlock(falseLabel);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerZero(result.Type, logical.Syntax), logical.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));
                    break;

                case BoundBinaryExpression logical when logical.OperatorToken.Kind == SyntaxKind.PipePipeToken:
                    EmitConditional(logical.Left, trueLabel, falseLabel);

                    StartBlock(falseLabel);
                    EmitConditional(logical.Right, trueLabel, CreateGeneratedLabel("logical_false"));
                    var rightFalse = CurrentTerminatorFalseTarget();

                    StartBlock(trueLabel);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerOne(result.Type, logical.Syntax), logical.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));

                    StartBlock(rightFalse);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerZero(result.Type, logical.Syntax), logical.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));
                    break;

                default:
                    EmitConditional(expression, trueLabel, falseLabel);

                    StartBlock(trueLabel);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerOne(result.Type, expression.Syntax), expression.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));

                    StartBlock(falseLabel);
                    Emit(new GimpleAssignmentStatement(result, CreateIntegerZero(result.Type, expression.Syntax), expression.Syntax));
                    Emit(new GimpleGotoStatement(endLabel));
                    break;
            }

            StartBlock(endLabel);
            return result;
        }

        private GimpleValue LowerCompoundLiteralExpression(BoundCompoundLiteralExpression expression)
        {
            var target = CreateTemporary(expression.Type, expression.Syntax);
            if (expression.InitializerList is not null)
                LowerInitializer(target, expression.InitializerList);
            else
                EmitZeroInitialize(target, expression.Syntax);

            return target;
        }

        private GimpleValue LowerStatementExpression(BoundStatementExpression expression)
        {
            var members = expression.Statement.Members;
            if (members.Length == 0)
                return new GimpleConstantValue(null, expression.Type, expression.Syntax);

            for (var i = 0; i < members.Length - 1; i++)
                LowerNode(members[i]);

            if (members[^1] is BoundExpressionStatement expressionStatement)
                return LowerExpression(expressionStatement.Expression);

            LowerNode(members[^1]);
            return new GimpleConstantValue(null, expression.Type, expression.Syntax);
        }

        private GimpleCallExpression LowerCallExpression(BoundCallExpression expression)
        {
            var arguments = ImmutableArray.CreateBuilder<GimpleValue>();
            foreach (var argument in expression.Arguments)
                arguments.Add(LowerExpression(argument));

            return new GimpleCallExpression(
                LowerCallCalleeExpression(expression.Expression),
                arguments.ToImmutable(),
                expression.FunctionType,
                expression.Type,
                expression.Syntax);
        }
        private GimpleValue LowerCallCalleeExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundParenthesizedExpression parenthesized:
                    return LowerCallCalleeExpression(parenthesized.Expression);

                case BoundConversionExpression conversion when conversion.ConversionKind == BoundConversionKind.Identity:
                    return LowerCallCalleeExpression(conversion.Expression);

                case BoundConversionExpression conversion when conversion.ConversionKind == BoundConversionKind.FunctionToPointer:
                    return new GimpleConversionExpression(
                        LowerCallCalleeExpression(conversion.Expression),
                        conversion.Type,
                        ToGimpleConversionKind(conversion.ConversionKind),
                        conversion.Syntax);

                case BoundNameExpression name when name.Symbol is FunctionSymbol:
                    return LowerNameExpression(name);

                default:
                    return LowerExpression(expression);
            }
        }

        private GimplePlace LowerElementAccessExpression(BoundElementAccessExpression expression)
        {
            return new GimpleElementAccessExpression(
                LowerExpression(expression.Expression),
                expression.Index is null ? null : LowerExpression(expression.Index),
                expression.Type,
                expression.Syntax);
        }

        private GimplePlace LowerMemberAccessExpression(BoundMemberAccessExpression expression)
        {
            return new GimpleMemberAccessExpression(
                LowerExpression(expression.Expression),
                expression.OperatorToken,
                expression.NameToken,
                expression.Field,
                expression.Type,
                expression.Syntax);
        }

        private GimplePlace LowerPlace(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundNameExpression name when name.Symbol is not null:
                    return new GimpleSymbolValue(name.Symbol, name.Type, name.Syntax);

                case BoundParenthesizedExpression parenthesized:
                    return LowerPlace(parenthesized.Expression);

                case BoundUnaryExpression unary when unary.OperatorToken.Kind == SyntaxKind.StarToken:
                    return new GimpleIndirectExpression(LowerExpression(unary.Operand), unary.Type, unary.Syntax);

                case BoundElementAccessExpression elementAccess:
                    return LowerElementAccessExpression(elementAccess);

                case BoundMemberAccessExpression memberAccess:
                    return LowerMemberAccessExpression(memberAccess);

                case BoundCompoundLiteralExpression compoundLiteral:
                    return (GimplePlace)LowerCompoundLiteralExpression(compoundLiteral);

                case BoundConversionExpression conversion when conversion.ConversionKind == BoundConversionKind.Identity:
                    return LowerPlace(conversion.Expression);

                default:
                    var lowered = LowerExpression(expression);
                    return lowered as GimplePlace ?? CreateTemporary(expression.Type, expression.Syntax);
            }
        }

        private void EmitConditional(BoundExpression condition, GimpleLabel whenTrue, GimpleLabel whenFalse)
        {
            switch (condition)
            {
                case BoundParenthesizedExpression parenthesized:
                    EmitConditional(parenthesized.Expression, whenTrue, whenFalse);
                    return;

                case BoundConversionExpression conversion when conversion.ConversionKind == BoundConversionKind.LValueToRValue ||
                                                            conversion.ConversionKind == BoundConversionKind.Identity:
                    EmitConditional(conversion.Expression, whenTrue, whenFalse);
                    return;

                case BoundBinaryExpression binary when binary.OperatorToken.Kind == SyntaxKind.AmpersandAmpersandToken:
                    {
                        var rightLabel = CreateGeneratedLabel("logical_rhs");
                        EmitConditional(binary.Left, rightLabel, whenFalse);
                        StartBlock(rightLabel);
                        EmitConditional(binary.Right, whenTrue, whenFalse);
                        return;
                    }

                case BoundBinaryExpression binary when binary.OperatorToken.Kind == SyntaxKind.PipePipeToken:
                    {
                        var rightLabel = CreateGeneratedLabel("logical_rhs");
                        EmitConditional(binary.Left, whenTrue, rightLabel);
                        StartBlock(rightLabel);
                        EmitConditional(binary.Right, whenTrue, whenFalse);
                        return;
                    }

                case BoundConditionalExpression conditional:
                    {
                        var trueArm = CreateGeneratedLabel("cond_branch_true");
                        var falseArm = CreateGeneratedLabel("cond_branch_false");
                        EmitConditional(conditional.Condition, trueArm, falseArm);
                        StartBlock(trueArm);
                        EmitConditional(conditional.WhenTrue, whenTrue, whenFalse);
                        StartBlock(falseArm);
                        EmitConditional(conditional.WhenFalse, whenTrue, whenFalse);
                        return;
                    }
            }

            Emit(new GimpleConditionalGotoStatement(
                LowerExpression(condition),
                whenTrue,
                whenFalse,
                condition.Syntax));
        }

        private GimpleTemporaryValue Materialize(GimpleValue value)
        {
            if (value is GimpleTemporaryValue temporary)
                return temporary;

            var target = CreateTemporary(value.Type, value.Syntax);
            Emit(new GimpleAssignmentStatement(target, value, value.Syntax));
            return target;
        }

        private GimpleTemporaryValue CreateTemporary(QualifiedType type, SyntaxNode? syntax)
        {
            var temporary = new GimpleTemporaryValue(_temporaryOrdinal++, type, syntax);
            _temporaries.Add(temporary);
            return temporary;
        }

        private GimpleConstantValue CreateIntegerZero(QualifiedType type, SyntaxNode? syntax)
            => new GimpleConstantValue(0, type.IsError ? _types.Builtin(BuiltinTypeKind.Int) : type, syntax);

        private GimpleConstantValue CreateIntegerOne(QualifiedType type, SyntaxNode? syntax)
            => new GimpleConstantValue(1, type.IsError ? _types.Builtin(BuiltinTypeKind.Int) : type, syntax);

        private void Emit(GimpleStatement statement)
        {
            if (_currentBlock is null || _currentBlock.HasTerminator)
                StartBlock(CreateGeneratedLabel("unreachable"));

            _currentBlock!.Statements.Add(statement);
        }

        private void StartBlock(GimpleLabel label)
        {
            if (label is null)
                throw new ArgumentNullException(nameof(label));

            if (_currentBlock is not null && !_currentBlock.HasTerminator)
                _currentBlock.Statements.Add(new GimpleGotoStatement(label));

            _currentBlock = new BlockBuilder(label);
            _blocks.Add(_currentBlock);
        }

        private bool IsCurrentBlockTerminated() => _currentBlock?.HasTerminator == true;

        private GimpleLabel CreateGeneratedLabel(string prefix)
            => new GimpleLabel($"{prefix}_{_labelOrdinal++.ToString(CultureInfo.InvariantCulture)}");

        private GimpleLabel GetLabel(LabelSymbol? symbol, SyntaxNode? syntax = null)
        {
            if (symbol is null)
                return CreateGeneratedLabel("missing_label");

            if (!_labels.TryGetValue(symbol, out var label))
            {
                label = new GimpleLabel(symbol.Name, symbol, syntax ?? symbol.DeclaringSyntax);
                _labels.Add(symbol, label);
            }

            return label;
        }

        private GimpleLabel GetBreakTarget()
            => _breakTargets.Count != 0 ? _breakTargets.Peek() : CreateGeneratedLabel("invalid_break");

        private GimpleLabel GetContinueTarget()
            => _continueTargets.Count != 0 ? _continueTargets.Peek() : CreateGeneratedLabel("invalid_continue");

        private GimpleLabel GetCaseLabel(BoundCaseStatement statement)
        {
            if (_switches.Count != 0 && _switches.Peek().CaseLabels.TryGetValue(statement, out var label))
                return label;

            return CreateGeneratedLabel("case");
        }

        private GimpleLabel GetDefaultLabel(BoundDefaultStatement statement)
        {
            if (_switches.Count != 0)
                return _switches.Peek().DefaultLabel;

            return CreateGeneratedLabel("default");
        }

        private SwitchLabels CollectSwitchLabels(BoundStatement statement)
        {
            var cases = ImmutableArray.CreateBuilder<GimpleSwitchCase>();
            var caseLabels = new Dictionary<BoundCaseStatement, GimpleLabel>();
            GimpleLabel? defaultLabel = null;

            void Visit(BoundStatement current)
            {
                switch (current)
                {
                    case BoundCaseStatement caseStatement:
                        {
                            var label = CreateGeneratedLabel("case");
                            caseLabels.Add(caseStatement, label);
                            var value = TryEvaluateIntegerConstantValue(caseStatement.Expression, out var constantValue)
                                ? new GimpleConstantValue(constantValue, caseStatement.Expression.Type, caseStatement.Expression.Syntax)
                                : new GimpleConstantValue(null, caseStatement.Expression.Type, caseStatement.Expression.Syntax);

                            cases.Add(new GimpleSwitchCase(value, label));
                            Visit(caseStatement.Statement);
                            break;
                        }

                    case BoundDefaultStatement defaultStatement:
                        defaultLabel ??= CreateGeneratedLabel("default");
                        Visit(defaultStatement.Statement);
                        break;

                    case BoundCompoundStatement compound:
                        foreach (var member in compound.Members)
                        {
                            if (member is BoundStatement nested)
                                Visit(nested);
                        }
                        break;

                    case BoundLabelStatement labelStatement:
                        Visit(labelStatement.Statement);
                        break;

                    case BoundSwitchStatement:
                        break;
                }
            }

            Visit(statement);
            return new SwitchLabels(cases.ToImmutable(), caseLabels, defaultLabel);
        }

        private GimpleLabel CurrentTerminatorTrueTarget()
        {
            if (_currentBlock?.Statements.LastOrDefault() is GimpleConditionalGotoStatement branch)
                return branch.WhenTrue;

            return CreateGeneratedLabel("logical_true");
        }

        private GimpleLabel CurrentTerminatorFalseTarget()
        {
            if (_currentBlock?.Statements.LastOrDefault() is GimpleConditionalGotoStatement branch)
                return branch.WhenFalse;

            return CreateGeneratedLabel("logical_false");
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
                    return TryEvaluateUnaryIntegerConstant(unary.OperatorToken.Kind, unary.Operand, out value);

                case BoundBinaryExpression binary:
                    return TryEvaluateBinaryIntegerConstant(binary.Left, binary.OperatorToken.Kind, binary.Right, out value);

                case BoundConditionalExpression conditional:
                    return TryEvaluateConditionalIntegerConstant(conditional.Condition, conditional.WhenTrue, conditional.WhenFalse, out value);

                default:
                    return TryConvertConstantToLong(expression.ConstantValue, out value);
            }
        }

        private static bool TryEvaluateIntegerConstantExpression(ExpressionSyntax expression, out long value)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal:
                    return TryConvertConstantToLong(literal.LiteralToken.Value, out value) ||
                           TryParseIntegerLiteral(literal.LiteralToken.Text, out value);

                case ParenthesizedExpressionSyntax parenthesized:
                    return TryEvaluateIntegerConstantExpression(parenthesized.Expression, out value);

                case CastExpressionSyntax cast:
                    return TryEvaluateIntegerConstantExpression(cast.Expression, out value);

                case UnaryExpressionSyntax unary:
                    return TryEvaluateUnaryIntegerConstant(unary.OperatorToken.Kind, unary.Operand, out value);

                case BinaryExpressionSyntax binary:
                    return TryEvaluateBinaryIntegerConstant(binary.Left, binary.OperatorToken.Kind, binary.Right, out value);

                case ConditionalExpressionSyntax conditional:
                    return TryEvaluateConditionalIntegerConstant(
                        conditional.Condition,
                        conditional.WhenTrue,
                        conditional.WhenFalse,
                        out value);

                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryEvaluateUnaryIntegerConstant(
            SyntaxKind operatorKind,
            BoundExpression operandExpression,
            out long value)
        {
            value = 0;
            if (!TryEvaluateIntegerConstantValue(operandExpression, out var operand))
                return false;

            return TryEvaluateUnaryIntegerConstant(operatorKind, operand, out value);
        }

        private static bool TryEvaluateUnaryIntegerConstant(
            SyntaxKind operatorKind,
            ExpressionSyntax operandExpression,
            out long value)
        {
            value = 0;
            if (!TryEvaluateIntegerConstantExpression(operandExpression, out var operand))
                return false;

            return TryEvaluateUnaryIntegerConstant(operatorKind, operand, out value);
        }

        private static bool TryEvaluateUnaryIntegerConstant(SyntaxKind operatorKind, long operand, out long value)
        {
            value = 0;

            try
            {
                switch (operatorKind)
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

        private static bool TryEvaluateBinaryIntegerConstant(
            BoundExpression leftExpression,
            SyntaxKind operatorKind,
            BoundExpression rightExpression,
            out long value)
        {
            value = 0;

            if (!TryEvaluateIntegerConstantValue(leftExpression, out var left) ||
                !TryEvaluateIntegerConstantValue(rightExpression, out var right))
            {
                return false;
            }

            return TryEvaluateBinaryIntegerConstant(left, operatorKind, right, out value);
        }

        private static bool TryEvaluateBinaryIntegerConstant(
            ExpressionSyntax leftExpression,
            SyntaxKind operatorKind,
            ExpressionSyntax rightExpression,
            out long value)
        {
            value = 0;

            if (!TryEvaluateIntegerConstantExpression(leftExpression, out var left) ||
                !TryEvaluateIntegerConstantExpression(rightExpression, out var right))
            {
                return false;
            }

            return TryEvaluateBinaryIntegerConstant(left, operatorKind, right, out value);
        }

        private static bool TryEvaluateBinaryIntegerConstant(
            long left,
            SyntaxKind operatorKind,
            long right,
            out long value)
        {
            value = 0;

            try
            {
                switch (operatorKind)
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

        private static bool TryEvaluateConditionalIntegerConstant(
            BoundExpression condition,
            BoundExpression whenTrue,
            BoundExpression whenFalse,
            out long value)
        {
            value = 0;

            if (!TryEvaluateIntegerConstantValue(condition, out var conditionValue))
                return false;

            return conditionValue != 0
                ? TryEvaluateIntegerConstantValue(whenTrue, out value)
                : TryEvaluateIntegerConstantValue(whenFalse, out value);
        }

        private static bool TryEvaluateConditionalIntegerConstant(
            ExpressionSyntax condition,
            ExpressionSyntax whenTrue,
            ExpressionSyntax whenFalse,
            out long value)
        {
            value = 0;

            if (!TryEvaluateIntegerConstantExpression(condition, out var conditionValue))
                return false;

            return conditionValue != 0
                ? TryEvaluateIntegerConstantExpression(whenTrue, out value)
                : TryEvaluateIntegerConstantExpression(whenFalse, out value);
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

        private static bool TryParseIntegerLiteral(string text, out long value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim().Replace("'", string.Empty);
            trimmed = trimmed.TrimEnd('u', 'U', 'l', 'L');

            try
            {
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return long.TryParse(
                        trimmed.Substring(2),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out value);
                }

                if (trimmed.Length > 1 && trimmed[0] == '0')
                    return TryParseOctalIntegerLiteral(trimmed, out value);

                return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        private static bool TryParseOctalIntegerLiteral(string text, out long value)
        {
            value = 0;

            foreach (var ch in text)
            {
                if (ch < '0' || ch > '7')
                    return false;

                checked
                {
                    value = value * 8 + (ch - '0');
                }
            }

            return true;
        }

        private static GimpleConversionKind ToGimpleConversionKind(BoundConversionKind kind)
        {
            return kind switch
            {
                BoundConversionKind.Identity => GimpleConversionKind.Identity,
                BoundConversionKind.LValueToRValue => GimpleConversionKind.LValueToRValue,
                BoundConversionKind.ArrayToPointer => GimpleConversionKind.ArrayToPointer,
                BoundConversionKind.FunctionToPointer => GimpleConversionKind.FunctionToPointer,
                BoundConversionKind.Implicit => GimpleConversionKind.Implicit,
                BoundConversionKind.Explicit => GimpleConversionKind.Explicit,
                BoundConversionKind.Error => GimpleConversionKind.Error,
                _ => GimpleConversionKind.Error,
            };
        }

        private static bool IsLogicalOperator(SyntaxKind kind)
            => kind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken;

        private static SyntaxToken GetCompoundAssignmentOperator(SyntaxToken token)
        {
            return token.Kind switch
            {
                SyntaxKind.PlusEqualsToken => CreateSyntheticToken(SyntaxKind.PlusToken, "+", token),
                SyntaxKind.MinusEqualsToken => CreateSyntheticToken(SyntaxKind.MinusToken, "-", token),
                SyntaxKind.StarEqualsToken => CreateSyntheticToken(SyntaxKind.StarToken, "*", token),
                SyntaxKind.SlashEqualsToken => CreateSyntheticToken(SyntaxKind.SlashToken, "/", token),
                SyntaxKind.PercentEqualsToken => CreateSyntheticToken(SyntaxKind.PercentToken, "%", token),
                SyntaxKind.AmpersandEqualsToken => CreateSyntheticToken(SyntaxKind.AmpersandToken, "&", token),
                SyntaxKind.PipeEqualsToken => CreateSyntheticToken(SyntaxKind.PipeToken, "|", token),
                SyntaxKind.HatEqualsToken => CreateSyntheticToken(SyntaxKind.HatToken, "^", token),
                SyntaxKind.LessThanLessThanEqualsToken => CreateSyntheticToken(SyntaxKind.LessThanLessThanToken, "<<", token),
                SyntaxKind.GreaterThanGreaterThanEqualsToken => CreateSyntheticToken(SyntaxKind.GreaterThanGreaterThanToken, ">>", token),
                _ => token,
            };
        }

        private static SyntaxToken CreateSyntheticToken(SyntaxKind kind, string text)
        {
            return new SyntaxToken(
                kind,
                0,
                text,
                value: null,
                ImmutableArray<SyntaxTrivia>.Empty,
                ImmutableArray<SyntaxTrivia>.Empty);
        }

        private static SyntaxToken CreateSyntheticToken(SyntaxKind kind, string text, SyntaxToken source)
        {
            return new SyntaxToken(
                kind,
                source.Position,
                text,
                value: null,
                ImmutableArray<SyntaxTrivia>.Empty,
                ImmutableArray<SyntaxTrivia>.Empty);
        }

        private sealed class SwitchContext
        {
            public GimpleLabel BreakLabel { get; }
            internal Dictionary<BoundCaseStatement, GimpleLabel> CaseLabels { get; }
            public GimpleLabel DefaultLabel { get; }

            public SwitchContext(
                GimpleLabel breakLabel,
                Dictionary<BoundCaseStatement, GimpleLabel> caseLabels,
                GimpleLabel defaultLabel)
            {
                BreakLabel = breakLabel;
                CaseLabels = caseLabels ?? throw new ArgumentNullException(nameof(caseLabels));
                DefaultLabel = defaultLabel ?? throw new ArgumentNullException(nameof(defaultLabel));
            }
        }

        private readonly struct SwitchLabels
        {
            public ImmutableArray<GimpleSwitchCase> Cases { get; }
            internal Dictionary<BoundCaseStatement, GimpleLabel> CaseLabels { get; }
            public GimpleLabel? DefaultLabel { get; }

            public SwitchLabels(
                ImmutableArray<GimpleSwitchCase> cases,
                Dictionary<BoundCaseStatement, GimpleLabel> caseLabels,
                GimpleLabel? defaultLabel)
            {
                Cases = cases.IsDefault ? ImmutableArray<GimpleSwitchCase>.Empty : cases;
                CaseLabels = caseLabels ?? throw new ArgumentNullException(nameof(caseLabels));
                DefaultLabel = defaultLabel;
            }
        }

        private sealed class BlockBuilder
        {
            public GimpleLabel Label { get; }
            public List<GimpleStatement> Statements { get; } = new();
            public bool HasTerminator => Statements.Count != 0 && Statements[^1].IsTerminator;

            public BlockBuilder(GimpleLabel label)
            {
                Label = label ?? throw new ArgumentNullException(nameof(label));
            }

            public GimpleBasicBlock ToImmutable()
                => new GimpleBasicBlock(Label, Statements.ToImmutableArray());
        }
    }
}
