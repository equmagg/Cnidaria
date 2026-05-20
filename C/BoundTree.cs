using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    public enum BoundNodeKind : ushort
    {
        TranslationUnit,

        FunctionDefinition,
        Declaration,
        Declarator,
        ExpressionInitializer,
        InitializerList,
        InitializerListItem,
        StaticAssertDeclaration,
        SkippedDeclaration,

        CompoundStatement,
        IfStatement,
        SwitchStatement,
        WhileStatement,
        DoStatement,
        ForStatement,
        BreakStatement,
        ContinueStatement,
        GotoStatement,
        LabelStatement,
        CaseStatement,
        DefaultStatement,
        ReturnStatement,
        ExpressionStatement,
        EmptyStatement,
        ErrorStatement,

        LiteralExpression,
        NameExpression,
        UnaryExpression,
        BinaryExpression,
        AssignmentExpression,
        ConditionalExpression,
        ConversionExpression,
        CastExpression,
        SizeofExpression,
        ParenthesizedExpression,
        CompoundLiteralExpression,
        GenericSelectionExpression,
        StatementExpression,
        CallExpression,
        ElementAccessExpression,
        MemberAccessExpression,
        PostfixUnaryExpression,
        ErrorExpression,
    }

    public enum BoundValueKind : byte
    {
        None,
        RValue,
        LValue,
        Function,
        Error,
    }

    public enum BoundConversionKind : byte
    {
        Identity,
        LValueToRValue,
        ArrayToPointer,
        FunctionToPointer,
        Implicit,
        Explicit,
        Error,
    }

    public sealed class BoundTree
    {
        public SemanticModel SemanticModel { get; }
        public BoundTranslationUnit Root { get; }
        public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }

        public BoundTree(
            SemanticModel semanticModel,
            BoundTranslationUnit root,
            ImmutableArray<SemanticDiagnostic> diagnostics)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<SemanticDiagnostic>.Empty
                : diagnostics;
        }

        public static BoundTree Bind(SemanticModel semanticModel)
            => Binder.BindTree(semanticModel);
    }

    public abstract class BoundNode
    {
        public SyntaxNode? Syntax { get; }

        protected BoundNode(SyntaxNode? syntax)
        {
            Syntax = syntax;
        }

        public abstract BoundNodeKind Kind { get; }
    }

    public sealed class BoundTranslationUnit : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.TranslationUnit;

        public ImmutableArray<BoundNode> Members { get; }

        public BoundTranslationUnit(
            TranslationUnitSyntax syntax,
            ImmutableArray<BoundNode> members)
            : base(syntax)
        {
            Members = members.IsDefault ? ImmutableArray<BoundNode>.Empty : members;
        }
    }

    public sealed class BoundFunctionDefinition : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.FunctionDefinition;

        public FunctionSymbol? Symbol { get; }
        public BoundCompoundStatement Body { get; }

        public BoundFunctionDefinition(
            FunctionDefinitionSyntax syntax,
            FunctionSymbol? symbol,
            BoundCompoundStatement body)
            : base(syntax)
        {
            Symbol = symbol;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }
    }

    public sealed class BoundDeclaration : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.Declaration;

        public StorageClass StorageClass { get; }
        public ImmutableArray<BoundDeclarator> Declarators { get; }

        public BoundDeclaration(
            DeclarationSyntax syntax,
            StorageClass storageClass,
            ImmutableArray<BoundDeclarator> declarators)
            : base(syntax)
        {
            StorageClass = storageClass;
            Declarators = declarators.IsDefault
                ? ImmutableArray<BoundDeclarator>.Empty
                : declarators;
        }
    }

    public sealed class BoundDeclarator : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.Declarator;

        public Symbol? Symbol { get; }
        public QualifiedType Type { get; }
        public BoundInitializer? Initializer { get; }

        public BoundDeclarator(
            InitDeclaratorSyntax syntax,
            Symbol? symbol,
            QualifiedType type,
            BoundInitializer? initializer)
            : base(syntax)
        {
            Symbol = symbol;
            Type = type;
            Initializer = initializer;
        }
    }

    public abstract class BoundInitializer : BoundNode
    {
        public QualifiedType TargetType { get; }

        protected BoundInitializer(InitializerSyntax syntax, QualifiedType targetType)
            : base(syntax)
        {
            TargetType = targetType;
        }
    }

    public sealed class BoundExpressionInitializer : BoundInitializer
    {
        public override BoundNodeKind Kind => BoundNodeKind.ExpressionInitializer;

        public BoundExpression Expression { get; }

        public BoundExpressionInitializer(
            ExpressionInitializerSyntax syntax,
            QualifiedType targetType,
            BoundExpression expression)
            : base(syntax, targetType)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    public sealed class BoundInitializerList : BoundInitializer
    {
        public override BoundNodeKind Kind => BoundNodeKind.InitializerList;

        public ImmutableArray<BoundInitializerListItem> Items { get; }

        public BoundInitializerList(
            InitializerListSyntax syntax,
            QualifiedType targetType,
            ImmutableArray<BoundInitializerListItem> items)
            : base(syntax, targetType)
        {
            Items = items.IsDefault
                ? ImmutableArray<BoundInitializerListItem>.Empty
                : items;
        }
    }

    public sealed class BoundInitializerListItem : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.InitializerListItem;

        public ImmutableArray<DesignatorSyntax> Designators { get; }
        public BoundInitializer Initializer { get; }

        public BoundInitializerListItem(InitializerListItemSyntax syntax, BoundInitializer initializer)
            : base(syntax)
        {
            Designators = syntax.Designators.IsDefault
                ? ImmutableArray<DesignatorSyntax>.Empty
                : syntax.Designators;
            Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        }
    }

    public sealed class BoundStaticAssertDeclaration : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.StaticAssertDeclaration;

        public BoundExpression Condition { get; }
        public BoundExpression? Message { get; }

        public BoundStaticAssertDeclaration(
            StaticAssertDeclarationSyntax syntax,
            BoundExpression condition,
            BoundExpression? message)
            : base(syntax)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Message = message;
        }
    }

    public sealed class BoundSkippedDeclaration : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.SkippedDeclaration;

        public BoundSkippedDeclaration(SyntaxNode syntax)
            : base(syntax)
        {
        }
    }

    public abstract class BoundStatement : BoundNode
    {
        protected BoundStatement(StatementSyntax? syntax)
            : base(syntax)
        {
        }
    }

    public sealed class BoundCompoundStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.CompoundStatement;

        public Scope? Scope { get; }
        public ImmutableArray<BoundNode> Members { get; }

        public BoundCompoundStatement(
            CompoundStatementSyntax syntax,
            Scope? scope,
            ImmutableArray<BoundNode> members)
            : base(syntax)
        {
            Scope = scope;
            Members = members.IsDefault ? ImmutableArray<BoundNode>.Empty : members;
        }
    }

    public sealed class BoundIfStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.IfStatement;

        public BoundExpression Condition { get; }
        public BoundStatement ThenStatement { get; }
        public BoundStatement? ElseStatement { get; }

        public BoundIfStatement(
            IfStatementSyntax syntax,
            BoundExpression condition,
            BoundStatement thenStatement,
            BoundStatement? elseStatement)
            : base(syntax)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            ThenStatement = thenStatement ?? throw new ArgumentNullException(nameof(thenStatement));
            ElseStatement = elseStatement;
        }
    }

    public sealed class BoundSwitchStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.SwitchStatement;

        public BoundExpression Expression { get; }
        public BoundStatement Statement { get; }

        public BoundSwitchStatement(
            SwitchStatementSyntax syntax,
            BoundExpression expression,
            BoundStatement statement)
            : base(syntax)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundWhileStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.WhileStatement;

        public BoundExpression Condition { get; }
        public BoundStatement Statement { get; }

        public BoundWhileStatement(
            WhileStatementSyntax syntax,
            BoundExpression condition,
            BoundStatement statement)
            : base(syntax)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundDoStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.DoStatement;

        public BoundStatement Statement { get; }
        public BoundExpression Condition { get; }

        public BoundDoStatement(
            DoStatementSyntax syntax,
            BoundStatement statement,
            BoundExpression condition)
            : base(syntax)
        {
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }
    }

    public sealed class BoundForStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ForStatement;

        public BoundNode? Initializer { get; }
        public BoundExpression? Condition { get; }
        public BoundExpression? Increment { get; }
        public BoundStatement Statement { get; }
        public Scope? Scope { get; }

        public BoundForStatement(
            ForStatementSyntax syntax,
            Scope? scope,
            BoundNode? initializer,
            BoundExpression? condition,
            BoundExpression? increment,
            BoundStatement statement)
            : base(syntax)
        {
            Scope = scope;
            Initializer = initializer;
            Condition = condition;
            Increment = increment;
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundBreakStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.BreakStatement;

        public BoundBreakStatement(BreakStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    public sealed class BoundContinueStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ContinueStatement;

        public BoundContinueStatement(ContinueStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    public sealed class BoundGotoStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.GotoStatement;

        public LabelSymbol? Label { get; }

        public BoundGotoStatement(GotoStatementSyntax syntax, LabelSymbol? label)
            : base(syntax)
        {
            Label = label;
        }
    }

    public sealed class BoundLabelStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.LabelStatement;

        public LabelSymbol? Label { get; }
        public BoundStatement Statement { get; }

        public BoundLabelStatement(
            LabelStatementSyntax syntax,
            LabelSymbol? label,
            BoundStatement statement)
            : base(syntax)
        {
            Label = label;
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundCaseStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.CaseStatement;

        public BoundExpression Expression { get; }
        public BoundStatement Statement { get; }

        public BoundCaseStatement(
            CaseStatementSyntax syntax,
            BoundExpression expression,
            BoundStatement statement)
            : base(syntax)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundDefaultStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.DefaultStatement;

        public BoundStatement Statement { get; }

        public BoundDefaultStatement(
            DefaultStatementSyntax syntax,
            BoundStatement statement)
            : base(syntax)
        {
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundReturnStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ReturnStatement;

        public BoundExpression? Expression { get; }
        public FunctionSymbol? Function { get; }

        public BoundReturnStatement(
            ReturnStatementSyntax syntax,
            FunctionSymbol? function,
            BoundExpression? expression)
            : base(syntax)
        {
            Function = function;
            Expression = expression;
        }
    }

    public sealed class BoundExpressionStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ExpressionStatement;

        public BoundExpression Expression { get; }

        public BoundExpressionStatement(
            ExpressionStatementSyntax syntax,
            BoundExpression expression)
            : base(syntax)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    public sealed class BoundEmptyStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.EmptyStatement;

        public BoundEmptyStatement(ExpressionStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    public sealed class BoundErrorStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ErrorStatement;

        public BoundErrorStatement(StatementSyntax? syntax)
            : base(syntax)
        {
        }
    }

    public abstract class BoundExpression : BoundNode
    {
        public QualifiedType Type { get; }
        public BoundValueKind ValueKind { get; }
        public object? ConstantValue { get; }

        public bool HasErrors => Type.IsError || ValueKind == BoundValueKind.Error;

        protected BoundExpression(
            ExpressionSyntax? syntax,
            QualifiedType type,
            BoundValueKind valueKind,
            object? constantValue = null)
            : base(syntax)
        {
            Type = type;
            ValueKind = valueKind;
            ConstantValue = constantValue;
        }
    }

    public sealed class BoundLiteralExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.LiteralExpression;

        public SyntaxToken LiteralToken { get; }

        public BoundLiteralExpression(
            LiteralExpressionSyntax syntax,
            SyntaxToken literalToken,
            QualifiedType type,
            object? constantValue)
            : base(syntax, type, BoundValueKind.RValue, constantValue)
        {
            LiteralToken = literalToken;
        }
    }

    public sealed class BoundNameExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.NameExpression;

        public Symbol? Symbol { get; }

        public BoundNameExpression(
            NameExpressionSyntax syntax,
            Symbol? symbol,
            QualifiedType type,
            BoundValueKind valueKind)
            : base(syntax, type, valueKind)
        {
            Symbol = symbol;
        }
    }

    public sealed class BoundUnaryExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.UnaryExpression;

        public SyntaxToken OperatorToken { get; }
        public BoundExpression Operand { get; }

        public BoundUnaryExpression(
            UnaryExpressionSyntax syntax,
            SyntaxToken operatorToken,
            BoundExpression operand,
            QualifiedType type,
            BoundValueKind valueKind,
            object? constantValue = null)
            : base(syntax, type, valueKind, constantValue)
        {
            OperatorToken = operatorToken;
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }
    }

    public sealed class BoundPostfixUnaryExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.PostfixUnaryExpression;

        public BoundExpression Operand { get; }
        public SyntaxToken OperatorToken { get; }

        public BoundPostfixUnaryExpression(
            PostfixUnaryExpressionSyntax syntax,
            BoundExpression operand,
            SyntaxToken operatorToken,
            QualifiedType type,
            object? constantValue = null)
            : base(syntax, type, BoundValueKind.RValue, constantValue)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
            OperatorToken = operatorToken;
        }
    }

    public sealed class BoundBinaryExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.BinaryExpression;

        public BoundExpression Left { get; }
        public SyntaxToken OperatorToken { get; }
        public BoundExpression Right { get; }

        public BoundBinaryExpression(
            BinaryExpressionSyntax syntax,
            BoundExpression left,
            SyntaxToken operatorToken,
            BoundExpression right,
            QualifiedType type,
            object? constantValue = null)
            : base(syntax, type, BoundValueKind.RValue, constantValue)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            OperatorToken = operatorToken;
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }
    }

    public sealed class BoundAssignmentExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.AssignmentExpression;

        public BoundExpression Left { get; }
        public SyntaxToken OperatorToken { get; }
        public BoundExpression Right { get; }

        public BoundAssignmentExpression(
            AssignmentExpressionSyntax syntax,
            BoundExpression left,
            SyntaxToken operatorToken,
            BoundExpression right,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.RValue)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            OperatorToken = operatorToken;
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }
    }

    public sealed class BoundConditionalExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ConditionalExpression;

        public BoundExpression Condition { get; }
        public BoundExpression WhenTrue { get; }
        public BoundExpression WhenFalse { get; }

        public BoundConditionalExpression(
            ConditionalExpressionSyntax syntax,
            BoundExpression condition,
            BoundExpression whenTrue,
            BoundExpression whenFalse,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.RValue)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
            WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
        }
    }

    public sealed class BoundConversionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ConversionExpression;

        public BoundExpression Expression { get; }
        public BoundConversionKind ConversionKind { get; }

        public BoundConversionExpression(
            ExpressionSyntax? syntax,
            BoundExpression expression,
            QualifiedType type,
            BoundValueKind valueKind,
            BoundConversionKind conversionKind)
            : base(syntax, type, valueKind)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            ConversionKind = conversionKind;
        }
    }

    public sealed class BoundCastExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CastExpression;

        public BoundExpression Expression { get; }

        public BoundCastExpression(
            CastExpressionSyntax syntax,
            BoundExpression expression,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.RValue)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    public sealed class BoundSizeofExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.SizeofExpression;

        public BoundExpression? Expression { get; }
        public QualifiedType? OperandType { get; }

        public BoundSizeofExpression(
            SizeofExpressionSyntax syntax,
            BoundExpression? expression,
            QualifiedType? operandType,
            QualifiedType resultType,
            object? constantValue)
            : base(syntax, resultType, BoundValueKind.RValue, constantValue)
        {
            Expression = expression;
            OperandType = operandType;
        }
    }

    public sealed class BoundParenthesizedExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ParenthesizedExpression;

        public BoundExpression Expression { get; }

        public BoundParenthesizedExpression(
            ParenthesizedExpressionSyntax syntax,
            BoundExpression expression)
            : base(syntax, expression.Type, expression.ValueKind, expression.ConstantValue)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    public sealed class BoundCompoundLiteralExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CompoundLiteralExpression;

        public BoundInitializerList? InitializerList { get; }

        public BoundCompoundLiteralExpression(
            CompoundLiteralExpressionSyntax syntax,
            QualifiedType type,
            BoundInitializerList? initializerList)
            : base(syntax, type, BoundValueKind.LValue)
        {
            InitializerList = initializerList;
        }
    }

    public sealed class BoundGenericSelectionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.GenericSelectionExpression;

        public BoundExpression ControlExpression { get; }
        public ImmutableArray<BoundExpression> AssociationExpressions { get; }
        public BoundExpression? SelectedExpression { get; }

        public BoundGenericSelectionExpression(
            GenericSelectionExpressionSyntax syntax,
            BoundExpression controlExpression,
            ImmutableArray<BoundExpression> associationExpressions,
            BoundExpression? selectedExpression,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.RValue)
        {
            ControlExpression = controlExpression ?? throw new ArgumentNullException(nameof(controlExpression));
            AssociationExpressions = associationExpressions.IsDefault
                ? ImmutableArray<BoundExpression>.Empty
                : associationExpressions;
            SelectedExpression = selectedExpression;
        }
    }

    public sealed class BoundStatementExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.StatementExpression;

        public BoundCompoundStatement Statement { get; }

        public BoundStatementExpression(
            StatementExpressionSyntax syntax,
            BoundCompoundStatement statement,
            QualifiedType type,
            BoundValueKind valueKind)
            : base(syntax, type, valueKind)
        {
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
        }
    }

    public sealed class BoundCallExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CallExpression;

        public BoundExpression Expression { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        public FunctionType? FunctionType { get; }

        public BoundCallExpression(
            CallExpressionSyntax syntax,
            BoundExpression expression,
            ImmutableArray<BoundExpression> arguments,
            FunctionType? functionType,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.RValue)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Arguments = arguments.IsDefault ? ImmutableArray<BoundExpression>.Empty : arguments;
            FunctionType = functionType;
        }
    }

    public sealed class BoundElementAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ElementAccessExpression;

        public BoundExpression Expression { get; }
        public BoundExpression? Index { get; }

        public BoundElementAccessExpression(
            ElementAccessExpressionSyntax syntax,
            BoundExpression expression,
            BoundExpression? index,
            QualifiedType type)
            : base(syntax, type, BoundValueKind.LValue)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Index = index;
        }
    }

    public sealed class BoundMemberAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.MemberAccessExpression;

        public BoundExpression Expression { get; }
        public SyntaxToken OperatorToken { get; }
        public SyntaxToken NameToken { get; }
        public FieldSymbol? Field { get; }

        public BoundMemberAccessExpression(
            MemberAccessExpressionSyntax syntax,
            BoundExpression expression,
            SyntaxToken operatorToken,
            SyntaxToken nameToken,
            FieldSymbol? field,
            QualifiedType type,
            BoundValueKind valueKind)
            : base(syntax, type, valueKind)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            OperatorToken = operatorToken;
            NameToken = nameToken;
            Field = field;
        }
    }

    public sealed class BoundErrorExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ErrorExpression;

        public BoundErrorExpression(ExpressionSyntax? syntax)
            : base(syntax, new QualifiedType(CErrorType.Instance), BoundValueKind.Error)
        {
        }
    }


}
