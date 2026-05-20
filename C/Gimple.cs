using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
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

    public sealed class GimpleTree
    {
        public SemanticModel SemanticModel { get; }
        public ImmutableArray<GimpleNode> Members { get; }
        public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }

        public GimpleTree(
            SemanticModel semanticModel,
            ImmutableArray<GimpleNode> members,
            ImmutableArray<SemanticDiagnostic> diagnostics)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Members = NormalizeMembers(members);
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<SemanticDiagnostic>.Empty : diagnostics;
        }

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

        public static GimpleTree Lower(SemanticModel semanticModel)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Gimplifier.Lower(semanticModel);
        }
    }

    public abstract class GimpleNode
    {
        public SyntaxNode? Syntax { get; }
        public abstract GimpleNodeKind Kind { get; }

        protected GimpleNode(SyntaxNode? syntax)
        {
            Syntax = syntax;
        }
    }

    public sealed class GimpleFunctionDefinition : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.FunctionDefinition;

        public FunctionSymbol? Symbol { get; }
        public ImmutableArray<GimpleTemporaryValue> Temporaries { get; }
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

    public sealed class GimpleExpressionInitializer : GimpleInitializer
    {
        public GimpleValue Expression { get; }

        public GimpleExpressionInitializer(SyntaxNode? syntax, QualifiedType targetType, GimpleValue expression)
            : base(syntax, targetType)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

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

    public sealed class GimpleSkippedDeclaration : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.SkippedDeclaration;

        public GimpleSkippedDeclaration(SyntaxNode? syntax)
            : base(syntax)
        {
        }
    }

    public sealed class GimpleBasicBlock : GimpleNode
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.BasicBlock;

        public GimpleLabel Label { get; }
        public ImmutableArray<GimpleStatement> Statements { get; }

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

    public abstract class GimpleStatement : GimpleNode
    {
        public virtual bool IsTerminator => false;

        protected GimpleStatement(SyntaxNode? syntax)
            : base(syntax)
        {
        }
    }

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

    public sealed class GimpleNopStatement : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.NopStatement;

        public GimpleNopStatement(SyntaxNode? syntax = null)
            : base(syntax)
        {
        }
    }

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

    internal static class GimpleTypeHelpers
    {
        public static QualifiedType Normalize(QualifiedType type)
            => type.Type is null ? new QualifiedType(CErrorType.Instance) : type;
    }

    public abstract class GimpleValue : GimpleNode
    {
        public QualifiedType Type { get; }

        protected GimpleValue(SyntaxNode? syntax, QualifiedType type)
            : base(syntax)
        {
            Type = GimpleTypeHelpers.Normalize(type);
        }
    }

    public abstract class GimplePlace : GimpleValue
    {
        protected GimplePlace(SyntaxNode? syntax, QualifiedType type)
            : base(syntax, type)
        {
        }
    }

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

    public sealed class GimpleMemberAccessExpression : GimplePlace
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.MemberAccessExpression;

        public GimpleValue Expression { get; }
        public SyntaxToken OperatorToken { get; }
        public SyntaxToken NameToken { get; }
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

    public sealed class GimpleCallExpression : GimpleValue
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.CallExpression;

        public GimpleValue Callee { get; }
        public ImmutableArray<GimpleValue> Arguments { get; }
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
