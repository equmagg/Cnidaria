using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    /// <summary>Identifies the concrete shape of a lowered node</summary>
    public enum GimpleNodeKind : ushort
    {
        Tree,
        FunctionDefinition,
        GlobalDeclaration,
        StaticAssertDeclaration,
        SkippedDeclaration,
        BasicBlock,
        Label,

        DeclarationStatement,
        AssignmentStatement,
        ZeroInitializeStatement,
        ExpressionStatement,
        GotoStatement,
        ConditionalGotoStatement,
        SwitchStatement,
        ReturnStatement,
        AsmStatement,
        NopStatement,

        SymbolValue,
        TemporaryValue,
        ConstantValue,
        UnaryExpression,
        BinaryExpression,
        ConversionExpression,
        CastExpression,
        AddressOfExpression,
        IndirectExpression,
        ElementAccessExpression,
        MemberAccessExpression,
        CallExpression,
        ErrorValue,
    }

    /// <summary>Identifies the semantic conversion preserved by a lowered expression</summary>
    public enum GimpleConversionKind : byte
    {
        Identity,
        LValueToRValue,
        ArrayToPointer,
        FunctionToPointer,
        Implicit,
        Explicit,
        Error,
    }

    /// <summary>Contains lowered top-level members for one semantic model</summary>
    /// <remarks>The tree is immutable and preserves top-level source order</remarks>
    public sealed class GimpleTree
    {
        public SemanticModel SemanticModel { get; }
        public ImmutableArray<GimpleNode> Members { get; }
        public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }
        /// <summary>Gets whether the configured inlining pass has already run</summary>
        internal bool HasInliningApplied { get; }

        public GimpleTree(
            SemanticModel semanticModel,
            ImmutableArray<GimpleNode> members,
            ImmutableArray<SemanticDiagnostic> diagnostics,
            bool hasInliningApplied = false)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Members = NormalizeMembers(members);
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<SemanticDiagnostic>.Empty : diagnostics;
            HasInliningApplied = hasInliningApplied;
        }

        internal GimpleTree WithInliningApplied()
            => HasInliningApplied
                ? this
                : new GimpleTree(SemanticModel, Members, Diagnostics, hasInliningApplied: true);

        private static ImmutableArray<GimpleNode> NormalizeMembers(ImmutableArray<GimpleNode> members)
        {
            var normalized = members.IsDefault ? ImmutableArray<GimpleNode>.Empty : members;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE tree cannot contain a null top-level member.", nameof(members));
            }

            return normalized;
        }

        /// <summary>Lowers a semantic model into explicit statements and basic blocks</summary>
        public static GimpleTree Lower(SemanticModel semanticModel)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Gimplifier.Lower(semanticModel);
        }
    }

    /// <summary>Base class for all lowered nodes</summary>
    public abstract class GimpleNode
    {
        /// <summary>Gets the source syntax associated with this node when available</summary>
        public SyntaxNode? Syntax { get; }
        public abstract GimpleNodeKind Kind { get; }

        protected GimpleNode(SyntaxNode? syntax)
        {
            Syntax = syntax;
        }
    }

    /// <summary>Represents a function as temporaries and labeled basic blocks</summary>
    /// <remarks>The first block owns EntryLabel and every non-final block has an explicit terminator</remarks>
    public sealed class GimpleFunctionDefinition : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.FunctionDefinition;

        public FunctionSymbol? Symbol { get; }
        /// <summary>Gets function-local temporaries in allocation order</summary>
        public ImmutableArray<GimpleTemporaryValue> Temporaries { get; }
        /// <summary>Gets basic blocks in emitted order</summary>
        public ImmutableArray<GimpleBasicBlock> Blocks { get; }
        public GimpleLabel EntryLabel { get; }

        public GimpleFunctionDefinition(
            SyntaxNode? syntax,
            FunctionSymbol? symbol,
            ImmutableArray<GimpleTemporaryValue> temporaries,
            ImmutableArray<GimpleBasicBlock> blocks,
            GimpleLabel entryLabel)
            : base(syntax)
        {
            Symbol = symbol;
            Temporaries = NormalizeTemporaries(temporaries);
            EntryLabel = entryLabel ?? throw new ArgumentNullException(nameof(entryLabel));
            Blocks = NormalizeBlocks(blocks, EntryLabel);
        }

        private static ImmutableArray<GimpleTemporaryValue> NormalizeTemporaries(ImmutableArray<GimpleTemporaryValue> temporaries)
        {
            var normalized = temporaries.IsDefault ? ImmutableArray<GimpleTemporaryValue>.Empty : temporaries;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE function cannot contain a null temporary.", nameof(temporaries));
            }

            return normalized;
        }

        private static ImmutableArray<GimpleBasicBlock> NormalizeBlocks(
            ImmutableArray<GimpleBasicBlock> blocks,
            GimpleLabel entryLabel)
        {
            var normalized = blocks.IsDefault ? ImmutableArray<GimpleBasicBlock>.Empty : blocks;
            if (normalized.Length == 0)
                throw new ArgumentException("A GIMPLE function must contain at least one basic block.", nameof(blocks));

            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE function cannot contain a null basic block.", nameof(blocks));

                if (i < normalized.Length - 1 && !normalized[i].HasTerminator)
                    throw new ArgumentException("Every non-final GIMPLE basic block must end with an explicit terminator.", nameof(blocks));
            }

            if (!ReferenceEquals(normalized[0].Label, entryLabel))
                throw new ArgumentException("The entry label must be the label of the first basic block.", nameof(entryLabel));

            return normalized;
        }
    }

    /// <summary>Describes one lowered variable declarator and its optional initializer</summary>
    public sealed class GimpleVariableDeclaration
    {
        public SyntaxNode? Syntax { get; }
        public Symbol? Symbol { get; }
        public QualifiedType Type { get; }
        public StorageClass StorageClass { get; }
        public GimpleInitializer? Initializer { get; }

        public GimpleVariableDeclaration(
            Symbol? symbol,
            QualifiedType type,
            StorageClass storageClass,
            GimpleInitializer? initializer = null,
            SyntaxNode? syntax = null)
        {
            Syntax = syntax ?? (symbol as TypedSymbol)?.DeclaringSyntax;
            Symbol = symbol;
            Type = GimpleTypeHelpers.Normalize(type);
            StorageClass = storageClass;
            Initializer = initializer;
        }
    }

    /// <summary>Base class for lowered static and aggregate initializers</summary>
    public abstract class GimpleInitializer
    {
        public SyntaxNode? Syntax { get; }
        public QualifiedType TargetType { get; }

        protected GimpleInitializer(SyntaxNode? syntax, QualifiedType targetType)
        {
            Syntax = syntax;
            TargetType = GimpleTypeHelpers.Normalize(targetType);
        }
    }

    /// <summary>Initializes an object from one lowered value</summary>
    public sealed class GimpleExpressionInitializer : GimpleInitializer
    {
        public GimpleValue Expression { get; }

        public GimpleExpressionInitializer(SyntaxNode? syntax, QualifiedType targetType, GimpleValue expression)
            : base(syntax, targetType)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    /// <summary>Initializes an aggregate or scalar from ordered initializer items</summary>
    public sealed class GimpleInitializerList : GimpleInitializer
    {
        public ImmutableArray<GimpleInitializerListItem> Items { get; }

        public GimpleInitializerList(
            SyntaxNode? syntax,
            QualifiedType targetType,
            ImmutableArray<GimpleInitializerListItem> items)
            : base(syntax, targetType)
        {
            Items = NormalizeItems(items);
        }

        private static ImmutableArray<GimpleInitializerListItem> NormalizeItems(ImmutableArray<GimpleInitializerListItem> items)
        {
            var normalized = items.IsDefault ? ImmutableArray<GimpleInitializerListItem>.Empty : items;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i].Initializer is null)
                    throw new ArgumentException("A GIMPLE initializer list cannot contain an empty item.", nameof(items));
            }

            return normalized;
        }
    }

    /// <summary>Pairs an initializer with the designator path selecting its target</summary>
    public readonly struct GimpleInitializerListItem
    {
        public SyntaxNode? Syntax { get; }
        public ImmutableArray<DesignatorSyntax> Designators { get; }
        public GimpleInitializer Initializer { get; }

        public GimpleInitializerListItem(
            SyntaxNode? syntax,
            ImmutableArray<DesignatorSyntax> designators,
            GimpleInitializer initializer)
        {
            Syntax = syntax;
            Designators = designators.IsDefault ? ImmutableArray<DesignatorSyntax>.Empty : designators;
            Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        }
    }

    /// <summary>Represents a top-level declaration containing one or more variables</summary>
    public sealed class GimpleGlobalDeclaration : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.GlobalDeclaration;

        public StorageClass StorageClass { get; }
        public ImmutableArray<GimpleVariableDeclaration> Declarators { get; }

        public GimpleGlobalDeclaration(
            SyntaxNode? syntax,
            StorageClass storageClass,
            ImmutableArray<GimpleVariableDeclaration> declarators)
            : base(syntax)
        {
            StorageClass = storageClass;
            Declarators = NormalizeDeclarators(declarators);
        }

        private static ImmutableArray<GimpleVariableDeclaration> NormalizeDeclarators(
            ImmutableArray<GimpleVariableDeclaration> declarators)
        {
            var normalized = declarators.IsDefault ? ImmutableArray<GimpleVariableDeclaration>.Empty : declarators;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE global declaration cannot contain a null declarator.", nameof(declarators));
            }

            return normalized;
        }
    }

    /// <summary>Preserves a translated static assertion and its optional message</summary>
    public sealed class GimpleStaticAssertDeclaration : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.StaticAssertDeclaration;

        public GimpleValue Condition { get; }
        public GimpleValue? Message { get; }

        public GimpleStaticAssertDeclaration(
            SyntaxNode? syntax,
            GimpleValue condition,
            GimpleValue? message = null)
            : base(syntax)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Message = message;
        }
    }

    /// <summary>Preserves source association for a top-level member with no lowered form</summary>
    public sealed class GimpleSkippedDeclaration : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.SkippedDeclaration;

        public GimpleSkippedDeclaration(SyntaxNode? syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Contains a label followed by an ordered statement sequence</summary>
    /// <remarks>A terminator may appear only as the final statement</remarks>
    public sealed class GimpleBasicBlock : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.BasicBlock;

        public GimpleLabel Label { get; }
        public ImmutableArray<GimpleStatement> Statements { get; }

        /// <summary>Gets whether the final statement terminates control flow</summary>
        public bool HasTerminator => Statements.Length != 0 && Statements[^1].IsTerminator;

        public GimpleBasicBlock(GimpleLabel label, ImmutableArray<GimpleStatement> statements)
            : base(label?.Syntax)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Statements = NormalizeStatements(statements);
        }

        private static ImmutableArray<GimpleStatement> NormalizeStatements(ImmutableArray<GimpleStatement> statements)
        {
            var normalized = statements.IsDefault ? ImmutableArray<GimpleStatement>.Empty : statements;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE basic block cannot contain a null statement.", nameof(statements));

                if (i < normalized.Length - 1 && normalized[i].IsTerminator)
                    throw new ArgumentException("A GIMPLE basic block cannot contain statements after a terminator.", nameof(statements));
            }

            return normalized;
        }
    }

    /// <summary>Identifies a basic block and optionally retains its source label symbol</summary>
    public sealed class GimpleLabel : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.Label;

        public string Name { get; }
        public LabelSymbol? Symbol { get; }

        public GimpleLabel(string name, LabelSymbol? symbol = null, SyntaxNode? syntax = null)
            : base(syntax ?? symbol?.DeclaringSyntax)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<label>" : name;
            Symbol = symbol;
        }

        public override string ToString() => Name;
    }

    /// <summary>Base class for lowered statements</summary>
    public abstract class GimpleStatement : GimpleNode
    {
        /// <summary>Gets whether the statement ends its basic block</summary>
        public virtual bool IsTerminator => false;

        protected GimpleStatement(SyntaxNode? syntax)
            : base(syntax)
        {
        }
    }

    /// <summary>Introduces a local variable without executing its initializer</summary>
    public sealed class GimpleDeclarationStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.DeclarationStatement;

        public GimpleVariableDeclaration Declaration { get; }
        public Symbol? Symbol => Declaration.Symbol;
        public QualifiedType Type => Declaration.Type;
        public StorageClass StorageClass => Declaration.StorageClass;

        public GimpleDeclarationStatement(GimpleVariableDeclaration declaration)
            : base(declaration?.Syntax)
        {
            Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        }
    }

    /// <summary>Stores a value into an assignable place</summary>
    public sealed class GimpleAssignmentStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.AssignmentStatement;

        public GimplePlace Target { get; }
        public GimpleValue Value { get; }

        public GimpleAssignmentStatement(GimplePlace target, GimpleValue value, SyntaxNode? syntax = null)
            : base(syntax ?? target?.Syntax ?? value?.Syntax)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>Zero-initializes an aggregate place</summary>
    public sealed class GimpleZeroInitializeStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ZeroInitializeStatement;

        public GimplePlace Target { get; }

        public GimpleZeroInitializeStatement(GimplePlace target, SyntaxNode? syntax = null)
            : base(syntax ?? target?.Syntax)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }

    /// <summary>Evaluates a value only for its side effects</summary>
    public sealed class GimpleExpressionStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ExpressionStatement;

        public GimpleValue Expression { get; }

        public GimpleExpressionStatement(GimpleValue expression, SyntaxNode? syntax = null)
            : base(syntax ?? expression?.Syntax)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    /// <summary>Transfers control unconditionally to a label</summary>
    public sealed class GimpleGotoStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.GotoStatement;
        public override bool IsTerminator => true;

        public GimpleLabel Target { get; }

        public GimpleGotoStatement(GimpleLabel target, SyntaxNode? syntax = null)
            : base(syntax ?? target?.Syntax)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }

    /// <summary>Transfers control to one of two labels based on a value</summary>
    public sealed class GimpleConditionalGotoStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ConditionalGotoStatement;
        public override bool IsTerminator => true;

        public GimpleValue Condition { get; }
        public GimpleLabel WhenTrue { get; }
        public GimpleLabel WhenFalse { get; }

        public GimpleConditionalGotoStatement(
            GimpleValue condition,
            GimpleLabel whenTrue,
            GimpleLabel whenFalse,
            SyntaxNode? syntax = null)
            : base(syntax ?? condition?.Syntax)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
            WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
        }
    }

    /// <summary>Dispatches an integral value to case or default labels</summary>
    public sealed class GimpleSwitchStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.SwitchStatement;
        public override bool IsTerminator => true;

        public GimpleValue Expression { get; }
        public ImmutableArray<GimpleSwitchCase> Cases { get; }
        public GimpleLabel DefaultLabel { get; }

        public GimpleSwitchStatement(
            GimpleValue expression,
            ImmutableArray<GimpleSwitchCase> cases,
            GimpleLabel defaultLabel,
            SyntaxNode? syntax = null)
            : base(syntax ?? expression?.Syntax)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Cases = NormalizeCases(cases);
            DefaultLabel = defaultLabel ?? throw new ArgumentNullException(nameof(defaultLabel));
        }

        private static ImmutableArray<GimpleSwitchCase> NormalizeCases(ImmutableArray<GimpleSwitchCase> cases)
        {
            var normalized = cases.IsDefault ? ImmutableArray<GimpleSwitchCase>.Empty : cases;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i].Value is null || normalized[i].Target is null)
                    throw new ArgumentException("A GIMPLE switch cannot contain an empty case.", nameof(cases));
            }

            return normalized;
        }
    }

    /// <summary>Returns from the current function with an optional value</summary>
    public sealed class GimpleReturnStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ReturnStatement;
        public override bool IsTerminator => true;

        public GimpleValue? Expression { get; }
        public FunctionSymbol? Function { get; }

        public GimpleReturnStatement(FunctionSymbol? function, GimpleValue? expression, SyntaxNode? syntax = null)
            : base(syntax ?? expression?.Syntax)
        {
            Function = function;
            Expression = expression;
        }
    }

    /// <summary>Describes one input or output operand of an inline assembly statement</summary>
    public sealed class GimpleAsmOperand
    {
        public string? Name { get; }
        public string Constraint { get; }
        /// <summary>Gets the destination place for an output operand</summary>
        public GimplePlace? Target { get; }
        /// <summary>Gets the input value or the initial value of a read-write output</summary>
        public GimpleValue? Value { get; }
        public bool IsOutput { get; }
        public bool IsReadWrite { get; }
        public SyntaxNode? Syntax { get; }

        public GimpleAsmOperand(
            string? name,
            string constraint,
            GimplePlace? target,
            GimpleValue? value,
            bool isOutput,
            bool isReadWrite,
            SyntaxNode? syntax)
        {
            Name = string.IsNullOrEmpty(name) ? null : name;
            Constraint = constraint ?? string.Empty;
            Target = target;
            Value = value;
            IsOutput = isOutput;
            IsReadWrite = isReadWrite;
            Syntax = syntax;
        }
    }

    /// <summary>Represents inline assembly with normalized operands, clobbers, and targets</summary>
    /// <remarks>Goto assembly terminates the block but also permits an explicit fallthrough edge</remarks>
    public sealed class GimpleAsmStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.AsmStatement;
        public override bool IsTerminator => IsGoto;

        public string Text { get; }
        public bool IsVolatile { get; }
        public bool IsInline { get; }
        public bool IsGoto { get; }
        public ImmutableArray<GimpleAsmOperand> Outputs { get; }
        public ImmutableArray<GimpleAsmOperand> Inputs { get; }
        public ImmutableArray<string> Clobbers { get; }
        public ImmutableArray<GimpleLabel> GotoLabels { get; }
        public bool HasMemoryClobber => InlineAsmConstraints.HasMemoryClobber(Clobbers);

        public GimpleAsmStatement(
            string text,
            bool isVolatile,
            bool isInline,
            bool isGoto,
            ImmutableArray<GimpleAsmOperand> outputs,
            ImmutableArray<GimpleAsmOperand> inputs,
            ImmutableArray<string> clobbers,
            ImmutableArray<GimpleLabel> gotoLabels,
            SyntaxNode? syntax = null)
            : base(syntax)
        {
            Text = text ?? string.Empty;
            IsVolatile = isVolatile;
            IsInline = isInline;
            IsGoto = isGoto;
            Outputs = outputs.IsDefault ? ImmutableArray<GimpleAsmOperand>.Empty : outputs;
            Inputs = inputs.IsDefault ? ImmutableArray<GimpleAsmOperand>.Empty : inputs;
            Clobbers = clobbers.IsDefault ? ImmutableArray<string>.Empty : clobbers;
            GotoLabels = gotoLabels.IsDefault ? ImmutableArray<GimpleLabel>.Empty : gotoLabels;
        }
    }

    /// <summary>Preserves a statement position without semantic work</summary>
    public sealed class GimpleNopStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.NopStatement;

        public GimpleNopStatement(SyntaxNode? syntax = null)
            : base(syntax)
        {
        }
    }

    /// <summary>Maps one constant switch value to a target label</summary>
    public readonly struct GimpleSwitchCase
    {
        public GimpleConstantValue Value { get; }
        public GimpleLabel Target { get; }

        public GimpleSwitchCase(GimpleConstantValue value, GimpleLabel target)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }

    /// <summary>Normalizes missing semantic types into the error type</summary>
    internal static class GimpleTypeHelpers
    {
        public static QualifiedType Normalize(QualifiedType type)
            => type.Type is null ? new QualifiedType(CErrorType.Instance) : type;
    }

    /// <summary>Base class for typed values</summary>
    public abstract class GimpleValue : GimpleNode
    {
        public QualifiedType Type { get; }

        protected GimpleValue(SyntaxNode? syntax, QualifiedType type)
            : base(syntax)
        {
            Type = GimpleTypeHelpers.Normalize(type);
        }
    }

    /// <summary>Base class for values that identify assignable storage</summary>
    public abstract class GimplePlace : GimpleValue
    {
        protected GimplePlace(SyntaxNode? syntax, QualifiedType type)
            : base(syntax, type)
        {
        }
    }

    /// <summary>References storage or a function through a semantic symbol</summary>
    public sealed class GimpleSymbolValue : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.SymbolValue;

        public Symbol Symbol { get; }

        public GimpleSymbolValue(Symbol symbol, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? (symbol as TypedSymbol)?.DeclaringSyntax, type)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public override string ToString() => Symbol.Name;
    }

    /// <summary>Identifies a function-local temporary by allocation ordinal</summary>
    public sealed class GimpleTemporaryValue : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.TemporaryValue;

        public int Ordinal { get; }
        public string Name { get; }

        public GimpleTemporaryValue(int ordinal, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax, type)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Name = $"_t{Ordinal.ToString(CultureInfo.InvariantCulture)}";
        }

        public override string ToString() => Name;
    }

    /// <summary>Represents a typed compile-time value</summary>
    public sealed class GimpleConstantValue : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ConstantValue;

        public object? Value { get; }

        public GimpleConstantValue(object? value, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax, type)
        {
            Value = value;
        }

        public override string ToString() => Value?.ToString() ?? "null";
    }

    /// <summary>Applies a unary operator to one lowered operand</summary>
    public sealed class GimpleUnaryExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.UnaryExpression;

        public SyntaxToken OperatorToken { get; }
        public GimpleValue Operand { get; }

        public GimpleUnaryExpression(SyntaxToken operatorToken, GimpleValue operand, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? operand?.Syntax, type)
        {
            OperatorToken = operatorToken;
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }
    }

    /// <summary>Applies a binary operator to two lowered operands</summary>
    public sealed class GimpleBinaryExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.BinaryExpression;

        public GimpleValue Left { get; }
        public SyntaxToken OperatorToken { get; }
        public GimpleValue Right { get; }

        public GimpleBinaryExpression(GimpleValue left, SyntaxToken operatorToken, GimpleValue right, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? left?.Syntax ?? right?.Syntax, type)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            OperatorToken = operatorToken;
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }
    }

    /// <summary>Preserves a semantic conversion around a lowered operand</summary>
    public sealed class GimpleConversionExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ConversionExpression;

        public GimpleValue Operand { get; }
        public GimpleConversionKind ConversionKind { get; }

        public GimpleConversionExpression(GimpleValue operand, QualifiedType type, GimpleConversionKind conversionKind, SyntaxNode? syntax = null)
            : base(syntax ?? operand?.Syntax, type)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
            ConversionKind = conversionKind;
        }
    }

    /// <summary>Represents an explicit cast to the node type</summary>
    public sealed class GimpleCastExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.CastExpression;

        public GimpleValue Operand { get; }

        public GimpleCastExpression(GimpleValue operand, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? operand?.Syntax, type)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }
    }

    /// <summary>Produces the address of an assignable place</summary>
    public sealed class GimpleAddressOfExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.AddressOfExpression;

        public GimplePlace Target { get; }

        public GimpleAddressOfExpression(GimplePlace target, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? target?.Syntax, type)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }

    /// <summary>Identifies storage reached through a pointer value</summary>
    public sealed class GimpleIndirectExpression : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.IndirectExpression;

        public GimpleValue Address { get; }

        public GimpleIndirectExpression(GimpleValue address, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? address?.Syntax, type)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
        }
    }

    /// <summary>Identifies an indexed element of an aggregate or pointer value</summary>
    /// <remarks>Index may be null when source recovery omitted the index expression</remarks>
    public sealed class GimpleElementAccessExpression : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ElementAccessExpression;

        public GimpleValue Expression { get; }
        public GimpleValue? Index { get; }

        public GimpleElementAccessExpression(GimpleValue expression, GimpleValue? index, QualifiedType type, SyntaxNode? syntax = null)
            : base(syntax ?? expression?.Syntax ?? index?.Syntax, type)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Index = index;
        }
    }

    /// <summary>Identifies a named field reached through member access</summary>
    public sealed class GimpleMemberAccessExpression : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.MemberAccessExpression;

        public GimpleValue Expression { get; }
        public SyntaxToken OperatorToken { get; }
        public SyntaxToken NameToken { get; }
        /// <summary>Gets the resolved field or null when binding failed</summary>
        public FieldSymbol? Field { get; }

        public GimpleMemberAccessExpression(
            GimpleValue expression,
            SyntaxToken operatorToken,
            SyntaxToken nameToken,
            FieldSymbol? field,
            QualifiedType type,
            SyntaxNode? syntax = null)
            : base(syntax ?? expression?.Syntax, type)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            OperatorToken = operatorToken;
            NameToken = nameToken;
            Field = field;
        }
    }

    /// <summary>Invokes a lowered callee with ordered argument values</summary>
    public sealed class GimpleCallExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.CallExpression;

        public GimpleValue Callee { get; }
        public ImmutableArray<GimpleValue> Arguments { get; }
        /// <summary>Gets the resolved call signature when available</summary>
        public FunctionType? FunctionType { get; }

        public GimpleCallExpression(
            GimpleValue callee,
            ImmutableArray<GimpleValue> arguments,
            FunctionType? functionType,
            QualifiedType type,
            SyntaxNode? syntax = null)
            : base(syntax ?? callee?.Syntax, type)
        {
            Callee = callee ?? throw new ArgumentNullException(nameof(callee));
            Arguments = NormalizeArguments(arguments);
            FunctionType = functionType;
        }

        private static ImmutableArray<GimpleValue> NormalizeArguments(ImmutableArray<GimpleValue> arguments)
        {
            var normalized = arguments.IsDefault ? ImmutableArray<GimpleValue>.Empty : arguments;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] is null)
                    throw new ArgumentException("A GIMPLE call cannot contain a null argument.", nameof(arguments));
            }

            return normalized;
        }
    }

    /// <summary>Represents an invalid value while preserving source and type shape</summary>
    public sealed class GimpleErrorValue : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.ErrorValue;

        public static GimpleErrorValue Instance { get; } = new GimpleErrorValue(null);

        public GimpleErrorValue(SyntaxNode? syntax)
            : base(syntax, new QualifiedType(CErrorType.Instance))
        {
        }
    }

}
