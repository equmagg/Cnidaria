using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    /// <summary>Identifies the semantic shape of a bound node</summary>
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
        AsmStatement,
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

    /// <summary>Describes how an expression value may be used</summary>
    public enum BoundValueKind : byte
    {
        None,
        RValue,
        LValue,
        Function,
        Error,
    }

    /// <summary>Identifies the conversion represented by a bound conversion node</summary>
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

    /// <summary>CSharp bound tree root</summary>
    /// <remarks>Contains a root translation unit, semantic model and its diagnostics</remarks>
    public sealed class BoundTree
    {
        public SemanticModel SemanticModel { get; }
        public BoundTranslationUnit Root { get; }
        /// <summary>Diagnostics produced before and during binding</summary>
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

        /// <summary>Creates a bound tree for the semantic model</summary>
        public static BoundTree Bind(SemanticModel semanticModel)
            => Binder.BindTree(semanticModel);
    }

    /// <summary>Base class for typed semantic nodes</summary>
    public abstract class BoundNode
    {
        /// <summary>Source syntax or null for synthesized recovery nodes</summary>
        public SyntaxNode? Syntax { get; }

        protected BoundNode(SyntaxNode? syntax)
        {
            Syntax = syntax;
        }

        public abstract BoundNodeKind Kind { get; }
    }

    /// <summary>Contains the bound top-level members of a source file</summary>
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

    /// <summary>Associates a function symbol with its bound body</summary>
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

    /// <summary>Contains declarators that share declaration specifiers</summary>
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

    /// <summary>Associates a declared symbol and type with an optional initializer</summary>
    public sealed class BoundDeclarator : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.Declarator;

        public Symbol? Symbol { get; }
        /// <summary>Declared type after applying all declarator layers</summary>
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

    /// <summary>Base class for initializers bound against a target type</summary>
    public abstract class BoundInitializer : BoundNode
    {
        /// <summary>Type being initialized</summary>
        public QualifiedType TargetType { get; }

        protected BoundInitializer(InitializerSyntax syntax, QualifiedType targetType)
            : base(syntax)
        {
            TargetType = targetType;
        }
    }

    /// <summary>Represents initialization from a single expression</summary>
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

    /// <summary>Represents aggregate initialization from an ordered item list</summary>
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

    /// <summary>Pairs an initializer with its optional designator path</summary>
    public sealed class BoundInitializerListItem : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.InitializerListItem;

        /// <summary>Field and element path relative to the enclosing initializer</summary>
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

    /// <summary>Represents a bound compile-time assertion</summary>
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

    /// <summary>Preserves unsupported syntax in the bound tree</summary>
    public sealed class BoundSkippedDeclaration : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.SkippedDeclaration;

        public BoundSkippedDeclaration(SyntaxNode syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Base class for bound statements</summary>
    public abstract class BoundStatement : BoundNode
    {
        protected BoundStatement(StatementSyntax? syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Contains declarations and statements in lexical order</summary>
    public sealed class BoundCompoundStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.CompoundStatement;

        /// <summary>Lexical scope associated with the statement</summary>
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

    /// <summary>Represents conditional control flow</summary>
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

    /// <summary>Represents multiway control flow over an integer expression</summary>
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

    /// <summary>Represents a pre-test loop</summary>
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

    /// <summary>Represents a post-test loop</summary>
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

    /// <summary>Represents a loop with optional initializer condition and increment</summary>
    public sealed class BoundForStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ForStatement;

        public BoundNode? Initializer { get; }
        public BoundExpression? Condition { get; }
        public BoundExpression? Increment { get; }
        public BoundStatement Statement { get; }
        /// <summary>Lexical scope associated with the statement</summary>
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

    /// <summary>Represents control transfer out of the nearest loop or switch</summary>
    public sealed class BoundBreakStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.BreakStatement;

        public BoundBreakStatement(BreakStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Represents control transfer to the next loop iteration</summary>
    public sealed class BoundContinueStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ContinueStatement;

        public BoundContinueStatement(ContinueStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Represents control transfer to a function-scoped label</summary>
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

    /// <summary>Associates a label symbol with its following statement</summary>
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

    /// <summary>Associates a case expression with its following statement</summary>
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

    /// <summary>Represents the fallback label of a switch statement</summary>
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

    /// <summary>Represents control transfer from the current function</summary>
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

    /// <summary>Represents an expression evaluated for its effects</summary>
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

    /// <summary>Stores a bound inline assembly operand and its constraint</summary>
    public sealed class BoundAsmOperand
    {
        public AsmOperandSyntax Syntax { get; }
        public string? Name { get; }
        /// <summary>Constraint text used during operand selection</summary>
        public string Constraint { get; }
        public BoundExpression Expression { get; }
        /// <summary>Whether the operand is both an input and an output</summary>
        public bool IsReadWrite { get; }

        public BoundAsmOperand(AsmOperandSyntax syntax, string? name, string constraint, BoundExpression expression, bool isReadWrite)
        {
            Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
            Name = string.IsNullOrEmpty(name) ? null : name;
            Constraint = constraint ?? string.Empty;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            IsReadWrite = isReadWrite;
        }
    }

    /// <summary>Stores inline assembly data after binding</summary>
    public sealed class BoundAsmStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.AsmStatement;

        public string Text { get; }
        /// <summary>Whether the statement must be retained and ordered as volatile</summary>
        public bool IsVolatile { get; }
        public bool IsInline { get; }
        public bool IsGoto { get; }
        public ImmutableArray<BoundAsmOperand> Outputs { get; }
        public ImmutableArray<BoundAsmOperand> Inputs { get; }
        public ImmutableArray<string> Clobbers { get; }
        /// <summary>Resolved branch targets for an assembly goto statement</summary>
        public ImmutableArray<LabelSymbol> GotoLabels { get; }

        public BoundAsmStatement(
            AsmStatementSyntax syntax,
            string text,
            bool isVolatile,
            bool isInline,
            bool isGoto,
            ImmutableArray<BoundAsmOperand> outputs,
            ImmutableArray<BoundAsmOperand> inputs,
            ImmutableArray<string> clobbers,
            ImmutableArray<LabelSymbol> gotoLabels)
            : base(syntax)
        {
            Text = text ?? string.Empty;
            IsVolatile = isVolatile;
            IsInline = isInline;
            IsGoto = isGoto;
            Outputs = outputs.IsDefault ? ImmutableArray<BoundAsmOperand>.Empty : outputs;
            Inputs = inputs.IsDefault ? ImmutableArray<BoundAsmOperand>.Empty : inputs;
            Clobbers = clobbers.IsDefault ? ImmutableArray<string>.Empty : clobbers;
            GotoLabels = gotoLabels.IsDefault ? ImmutableArray<LabelSymbol>.Empty : gotoLabels;
        }
    }

    /// <summary>Represents an empty expression statement</summary>
    public sealed class BoundEmptyStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.EmptyStatement;

        public BoundEmptyStatement(ExpressionStatementSyntax syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Represents a statement that failed to bind</summary>
    public sealed class BoundErrorStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ErrorStatement;

        public BoundErrorStatement(StatementSyntax? syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Base class for typed expressions</summary>
    public abstract class BoundExpression : BoundNode
    {
        /// <summary>Result type after binding and conversions</summary>
        public QualifiedType Type { get; }
        /// <summary>Value category exposed to parent expressions</summary>
        public BoundValueKind ValueKind { get; }
        /// <summary>Folded value when known during binding</summary>
        public object? ConstantValue { get; }

        /// <summary>Whether the expression contains an error type or value category</summary>
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

    /// <summary>Represents a literal and its decoded constant value</summary>
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

    /// <summary>Represents a reference to a resolved symbol</summary>
    public sealed class BoundNameExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.NameExpression;

        public Symbol? Symbol { get; }

        public BoundNameExpression(
            NameExpressionSyntax syntax,
            Symbol? symbol,
            QualifiedType type,
            BoundValueKind valueKind,
            object? constantValue = null)
            : base(syntax, type, valueKind, constantValue)
        {
            Symbol = symbol;
        }
    }

    /// <summary>Represents a prefix unary operation</summary>
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

    /// <summary>Represents a postfix unary operation</summary>
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

    /// <summary>Represents a binary operation after operand conversion</summary>
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

    /// <summary>Represents an assignment expression</summary>
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

    /// <summary>Represents selection between two expression values</summary>
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

    /// <summary>Makes an implicit or explicit value conversion visible in the tree</summary>
    public sealed class BoundConversionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ConversionExpression;

        public BoundExpression Expression { get; }
        /// <summary>Semantic conversion performed by this node</summary>
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

    /// <summary>Represents a source-level cast</summary>
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

    /// <summary>Represents a size query over an expression or type</summary>
    public sealed class BoundSizeofExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.SizeofExpression;

        /// <summary>Bound operand when the query uses an expression</summary>
        public BoundExpression? Expression { get; }
        /// <summary>Resolved operand type when the query uses a type name</summary>
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

    /// <summary>Preserves source parentheses while forwarding value semantics</summary>
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

    /// <summary>Represents an initialized unnamed object value</summary>
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

    /// <summary>Stores a generic selection and its chosen association</summary>
    public sealed class BoundGenericSelectionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.GenericSelectionExpression;

        public BoundExpression ControlExpression { get; }
        public ImmutableArray<BoundExpression> AssociationExpressions { get; }
        /// <summary>Chosen association or null when none were bound</summary>
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

    /// <summary>Represents a compound statement used as an expression</summary>
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

    /// <summary>Represents invocation of a callable expression</summary>
    public sealed class BoundCallExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CallExpression;

        public BoundExpression Expression { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        /// <summary>Resolved callable signature or null after recovery</summary>
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

    /// <summary>Represents indexed access through an array or pointer</summary>
    public sealed class BoundElementAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ElementAccessExpression;

        public BoundExpression Expression { get; }
        /// <summary>Bound index or null when the source omitted an index</summary>
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

    /// <summary>Represents direct or indirect field access</summary>
    public sealed class BoundMemberAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.MemberAccessExpression;

        public BoundExpression Expression { get; }
        public SyntaxToken OperatorToken { get; }
        public SyntaxToken NameToken { get; }
        /// <summary>Resolved field or null when lookup fails</summary>
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

    /// <summary>Represents an expression that failed to bind</summary>
    public sealed class BoundErrorExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ErrorExpression;

        public BoundErrorExpression(ExpressionSyntax? syntax)
            : base(syntax, new QualifiedType(CErrorType.Instance), BoundValueKind.Error)
        {
        }
    }


}
