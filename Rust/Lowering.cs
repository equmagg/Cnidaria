using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Cnidaria.Rust;

internal sealed class LoweringException(SyntaxNode syntax, string message) : Exception(message)
{
    internal SyntaxNode Syntax { get; } = syntax;
}

internal sealed class Lowerer
{
    private readonly TypeSystem _types;
    private readonly Dictionary<LabelSymbol, (LabelSymbol Break, LabelSymbol Continue, LocalSymbol? Result)> _targets = new();
    private FunctionSymbol _function = null!;
    private int _ordinal;

    private Lowerer(Compilation compilation) => _types = compilation.Binding.Types;

    internal static BoundCompilationUnit Lower(Compilation compilation)
    {
        var lowerer = new Lowerer(compilation);
        BoundCompilationUnit[] units = compilation.SyntaxTrees.Select(t => compilation.GetSemanticModel(t).GetBoundTree()).ToArray();
        // Generic functions are lowered only when code generation instantiates them
        return new BoundCompilationUnit(compilation.RootSyntaxTree.Root, compilation.Crate,
            units.SelectMany(u => u.Functions).Where(f => f.Symbol.AllTypeParameters.IsEmpty).Select(lowerer.Function).ToImmutableArray(),
            units.SelectMany(u => u.Globals).ToImmutableArray(), false);
    }
    internal static BoundFunction Lower(Compilation compilation, BoundFunction function) => new Lowerer(compilation).Function(function);

    private BoundFunction Function(BoundFunction function)
    {
        _function = function.Symbol;
        _ordinal = 0;
        _targets.Clear();
        if (function.Body is null) return function;
        var prologue = new List<BoundStatement>();
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            BoundPattern pattern = function.Parameters[i];
            if (pattern.Variable == function.Symbol.Parameters[i] && pattern.Subpatterns.IsEmpty) continue;
            BindPattern(pattern, Read(function.Symbol.Parameters[i]), null, prologue);
        }
        BoundBlockExpression body = Block(function.Body);
        return new BoundFunction(function.Symbol.Declaration, function.Symbol, [],
            new BoundBlockExpression(body.Syntax, body.Type, prologue.Concat(body.Statements).ToImmutableArray(), body.Tail, body.Locals));
    }

    private LocalSymbol Temp(SyntaxNode syntax, TypeSymbol type) => new("$temp" + _ordinal++, _function, syntax, _function.SyntaxTree!, type, true);
    private LabelSymbol Label(SyntaxNode syntax) => new("$label" + _ordinal++, _function, syntax, _function.SyntaxTree!, false);
    private BoundVariableExpression Read(VariableSymbol variable) => new(variable.Declaration!, variable);
    private BoundExpression Unit(SyntaxNode syntax) => new BoundTupleExpression(syntax, _types.Unit, []);
    private BoundLiteralExpression Literal(SyntaxNode syntax, TypeSymbol type, object value) => new(syntax, type, value);
    private BoundExpressionStatement Statement(BoundExpression expression) => new(expression.Syntax, expression);
    private BoundLetStatement Declare(LocalSymbol variable, BoundExpression? value) => new(variable.Declaration!,
        new BoundPattern(variable.Declaration!, variable.Type, BoundPatternKind.Binding, variable: variable, irrefutable: true), value, null);
    private BoundExpression Assign(VariableSymbol variable, BoundExpression value) => new BoundAssignmentExpression(value.Syntax, _types.Unit, SyntaxKind.EqualsToken, Read(variable), value);
    private LocalSymbol? Result(SyntaxNode syntax, TypeSymbol type, List<BoundStatement> statements)
    {
        if (type.Kind is TypeKind.Unit or TypeKind.Never) return null;
        LocalSymbol result = Temp(syntax, type);
        statements.Add(Declare(result, null));
        return result;
    }
    private BoundBlockExpression Sequence(SyntaxNode syntax, TypeSymbol type, List<BoundStatement> statements, BoundExpression? tail = null)
        => new(syntax, type, statements.ToImmutableArray(), tail ?? Unit(syntax), []);
    private BoundExpression Capture(BoundExpression expression, List<BoundStatement> statements, bool preservePlace = false)
    {
        if (preservePlace && expression.ValueCategory != ValueCategory.Value)
        {
            var reference = new IndirectTypeSymbol(TypeKind.Reference, expression.Type, expression.ValueCategory == ValueCategory.MutablePlace);
            LocalSymbol address = Temp(expression.Syntax, reference);
            statements.Add(Declare(address, new BoundUnaryExpression(expression.Syntax, reference, SyntaxKind.AmpersandToken, Rewrite(expression))));
            return new BoundUnaryExpression(expression.Syntax, expression.Type, SyntaxKind.StarToken, Read(address), expression.ValueCategory);
        }
        LocalSymbol local = Temp(expression.Syntax, expression.Type);
        statements.Add(Declare(local, Rewrite(expression)));
        return Read(local);
    }
    private void StoreResult(BoundExpression value, LocalSymbol? result, List<BoundStatement> statements)
        => statements.Add(Statement(result is null ? Rewrite(value) : Assign(result, Rewrite(value))));

    private BoundExpression Rewrite(BoundExpression expression) => expression switch
    {
        BoundLiteralExpression or BoundVariableExpression or BoundFunctionReference or BoundPanicExpression => expression,
        // Folded expressions become literals, so constant expressions keep their shape for promotion after lowering
        _ when expression.ConstantValue.HasValue => new BoundLiteralExpression(expression.Syntax, expression.Type, expression.ConstantValue.Value),
        BoundBlockExpression block => Block(block),
        BoundIfExpression conditional => Conditional(conditional),
        BoundLoopExpression loop => Loop(loop),
        BoundForExpression loop => For(loop),
        BoundMatchExpression match => Match(match),
        BoundPatternTestExpression test => PatternTest(test),
        BoundControlFlowExpression control => Control(control),
        BoundBinaryExpression binary when binary.Operator is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken => ShortCircuit(binary),
        BoundBinaryExpression binary => new BoundBinaryExpression(binary.Syntax, binary.Type, binary.Operator, Rewrite(binary.Left), Rewrite(binary.Right)),
        BoundUnaryExpression unary => new BoundUnaryExpression(unary.Syntax, unary.Type, unary.Operator, Rewrite(unary.Operand), unary.ValueCategory, unary.IsRawBorrow),
        BoundConversionExpression conversion => new BoundConversionExpression(conversion.Syntax, conversion.Type, conversion.Conversion, Rewrite(conversion.Operand)),
        BoundAssignmentExpression assignment => Assignment(assignment),
        BoundCallExpression call => new BoundCallExpression(call.Syntax, call.Type, Rewrite(call.Target), call.Arguments.Select(Rewrite).ToImmutableArray()),
        BoundFieldExpression field => new BoundFieldExpression(field.Syntax, field.Type, Rewrite(field.Receiver), field.Field, field.TupleIndex, field.ValueCategory),
        BoundIndexExpression index => new BoundIndexExpression(index.Syntax, index.Type, Rewrite(index.Receiver), Rewrite(index.Index), index.ValueCategory),
        BoundTupleExpression tuple => new BoundTupleExpression(tuple.Syntax, tuple.Type, tuple.Elements.Select(Rewrite).ToImmutableArray()),
        BoundArrayExpression array => new BoundArrayExpression(array.Syntax, array.Type, array.Elements.Select(Rewrite).ToImmutableArray(), array.RepeatCount),
        BoundConstructionExpression construction => new BoundConstructionExpression(construction.Syntax, (NamedTypeSymbol)construction.Type,
            construction.Variant, construction.Fields.Select(f => new BoundFieldInitializer(f.Field, Rewrite(f.Value))).ToImmutableArray(), construction.Base is null ? null : Rewrite(construction.Base)),
        _ => throw new LoweringException(expression.Syntax, $"Lowering is not implemented for '{expression.Kind}'.")
    };

    private BoundBlockExpression Block(BoundBlockExpression block)
    {
        var statements = new List<BoundStatement>();
        LocalSymbol? result = block.Label is null ? null : Result(block.Syntax, block.Type, statements);
        LabelSymbol? end = block.Label is null ? null : Label(block.Syntax);
        if (block.Label is not null) _targets.Add(block.Label, (end!, end!, result));
        foreach (BoundStatement statement in block.Statements)
        {
            switch (statement)
            {
                case BoundExpressionStatement expression:
                    if (expression.Expression is not null) statements.Add(Statement(Rewrite(expression.Expression)));
                    break;
                case BoundLetStatement let:
                    if (let.Initializer is null)
                    {
                        foreach (VariableSymbol variable in let.Pattern.Descendants().Prepend(let.Pattern).OfType<BoundPattern>().Select(p => p.Variable).OfType<VariableSymbol>())
                            statements.Add(new BoundLetStatement(let.Syntax, new BoundPattern(let.Syntax, variable.Type, BoundPatternKind.Binding, variable: variable, irrefutable: true), null, null));
                        break;
                    }
                    BoundExpression value = Capture(let.Initializer, statements, preservePlace: true);
                    LabelSymbol? fail = let.Else is null ? null : Label(let.Syntax);
                    BindPattern(let.Pattern, value, fail, statements);
                    if (fail is not null)
                    {
                        LabelSymbol done = Label(let.Syntax);
                        statements.Add(new BoundGotoStatement(let.Syntax, done));
                        statements.Add(new BoundLabelStatement(let.Syntax, fail));
                        statements.Add(Statement(Rewrite(let.Else!)));
                        statements.Add(new BoundLabelStatement(let.Syntax, done));
                    }
                    break;
                default: throw new LoweringException(statement.Syntax, $"Lowering is not implemented for '{statement.Kind}'.");
            }
        }
        if (end is null)
            return new BoundBlockExpression(block.Syntax, block.Type, statements.ToImmutableArray(), block.Tail is null ? null : Rewrite(block.Tail), block.Locals);
        if (block.Tail is not null) StoreResult(block.Tail, result, statements);
        statements.Add(new BoundLabelStatement(block.Syntax, end));
        _targets.Remove(block.Label!);
        return Sequence(block.Syntax, block.Type, statements, result is null ? null : Read(result));
    }

    private BoundExpression Conditional(BoundIfExpression conditional)
    {
        var statements = new List<BoundStatement>();
        LocalSymbol? result = Result(conditional.Syntax, conditional.Type, statements);
        LabelSymbol otherwise = Label(conditional.Syntax), end = Label(conditional.Syntax);
        statements.Add(new BoundGotoStatement(conditional.Syntax, otherwise, Rewrite(conditional.Condition), false));
        StoreResult(conditional.Then, result, statements);
        statements.Add(new BoundGotoStatement(conditional.Syntax, end));
        statements.Add(new BoundLabelStatement(conditional.Syntax, otherwise));
        if (conditional.Else is not null) StoreResult(conditional.Else, result, statements);
        statements.Add(new BoundLabelStatement(conditional.Syntax, end));
        return Sequence(conditional.Syntax, conditional.Type, statements, result is null ? null : Read(result));
    }
    private BoundExpression ShortCircuit(BoundBinaryExpression binary)
    {
        bool and = binary.Operator == SyntaxKind.AmpersandAmpersandToken;
        var then = new BoundBlockExpression(binary.Syntax, _types.Bool, [], and ? binary.Right : Literal(binary.Syntax, _types.Bool, true), []);
        return Conditional(new BoundIfExpression(binary.Syntax, _types.Bool, binary.Left, then, and ? Literal(binary.Syntax, _types.Bool, false) : binary.Right));
    }
    private BoundExpression Loop(BoundLoopExpression loop)
    {
        var statements = new List<BoundStatement>();
        LocalSymbol? result = Result(loop.Syntax, loop.Type, statements);
        LabelSymbol test = Label(loop.Syntax), end = Label(loop.Syntax);
        _targets.Add(loop.Label, (end, test, result));
        statements.Add(new BoundLabelStatement(loop.Syntax, test));
        if (loop.Condition is not null) statements.Add(new BoundGotoStatement(loop.Syntax, end, Rewrite(loop.Condition), false));
        statements.Add(Statement(Block(loop.Body)));
        statements.Add(new BoundGotoStatement(loop.Syntax, test));
        statements.Add(new BoundLabelStatement(loop.Syntax, end));
        _targets.Remove(loop.Label);
        return Sequence(loop.Syntax, loop.Type, statements, result is null ? null : Read(result));
    }
    private BoundExpression For(BoundForExpression loop)
    {
        var statements = new List<BoundStatement>();
        LabelSymbol test = Label(loop.Syntax), next = Label(loop.Syntax), end = Label(loop.Syntax);
        _targets.Add(loop.Label, (end, next, null));
        BoundExpression limit, item;
        LocalSymbol index;
        bool inclusive = false;
        if (loop.Iterator is BoundRangeExpression { Start: not null, End: not null } range)
        {
            if (range.Start.Type.Kind != TypeKind.Integer) throw new LoweringException(range.Syntax, "Character range iteration is not implemented yet.");
            index = Temp(range.Syntax, range.Start.Type);
            statements.Add(Declare(index, Rewrite(range.Start)));
            limit = Capture(range.End, statements);
            inclusive = range.Inclusive;
            item = Read(index);
        }
        else
        {
            BoundExpression array = Capture(loop.Iterator, statements);
            bool reference = array.Type is IndirectTypeSymbol;
            TypeSymbol sequenceType = reference ? ((IndirectTypeSymbol)array.Type).ElementType : array.Type;
            if (sequenceType is not SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } sequence)
                throw new LoweringException(loop.Iterator.Syntax, "Iteration currently requires an integer range or fixed-size array.");
            if (reference) array = new BoundUnaryExpression(array.Syntax, sequence, SyntaxKind.StarToken, array, ValueCategory.MutablePlace);
            index = Temp(loop.Syntax, _types.Usize);
            statements.Add(Declare(index, Literal(loop.Syntax, _types.Usize, BigInteger.Zero)));
            limit = Literal(loop.Syntax, _types.Usize, new BigInteger(length));
            item = new BoundIndexExpression(loop.Syntax, sequence.ElementType, array, Read(index), ValueCategory.MutablePlace);
            if (reference) item = new BoundUnaryExpression(loop.Syntax, loop.Pattern.InputType, SyntaxKind.AmpersandToken, item);
        }
        statements.Add(new BoundLabelStatement(loop.Syntax, test));
        statements.Add(new BoundGotoStatement(loop.Syntax, end, new BoundBinaryExpression(loop.Syntax, _types.Bool,
            inclusive ? SyntaxKind.LessThanEqualsToken : SyntaxKind.LessThanToken, Read(index), limit), false));
        BindPattern(loop.Pattern, item, null, statements);
        statements.Add(Statement(Block(loop.Body)));
        statements.Add(new BoundLabelStatement(loop.Syntax, next));
        // Test the inclusive endpoint before incrementing to avoid wrapping the largest value
        if (inclusive) statements.Add(new BoundGotoStatement(loop.Syntax, end,
            new BoundBinaryExpression(loop.Syntax, _types.Bool, SyntaxKind.EqualsEqualsToken, Read(index), limit)));
        statements.Add(Statement(Assign(index, new BoundBinaryExpression(loop.Syntax, index.Type, SyntaxKind.PlusToken, Read(index), Literal(loop.Syntax, index.Type, BigInteger.One)))));
        statements.Add(new BoundGotoStatement(loop.Syntax, test));
        statements.Add(new BoundLabelStatement(loop.Syntax, end));
        _targets.Remove(loop.Label);
        return Sequence(loop.Syntax, _types.Unit, statements);
    }
    private BoundExpression Control(BoundControlFlowExpression control)
    {
        if (control.Kind == BoundNodeKind.Return)
            return new BoundControlFlowExpression(control.Kind, control.Syntax, control.Type, null, control.Value is null ? null : Rewrite(control.Value));
        var target = _targets[control.Target!];
        var statements = new List<BoundStatement>();
        if (control.Value is not null) StoreResult(control.Value, target.Result, statements);
        statements.Add(new BoundGotoStatement(control.Syntax, control.Kind == BoundNodeKind.Break ? target.Break : target.Continue));
        return Sequence(control.Syntax, _types.Never, statements);
    }
    private BoundExpression Assignment(BoundAssignmentExpression assignment)
    {
        if (assignment.Operator == SyntaxKind.EqualsToken)
            return new BoundAssignmentExpression(assignment.Syntax, assignment.Type, assignment.Operator, Rewrite(assignment.Target), Rewrite(assignment.Value));
        var statements = new List<BoundStatement>();
        BoundExpression value = Capture(assignment.Value, statements);
        BoundExpression target = Capture(assignment.Target, statements, preservePlace: true);
        SyntaxKind op = assignment.Operator switch
        {
            SyntaxKind.PlusEqualsToken => SyntaxKind.PlusToken, SyntaxKind.MinusEqualsToken => SyntaxKind.MinusToken,
            SyntaxKind.StarEqualsToken => SyntaxKind.StarToken, SyntaxKind.SlashEqualsToken => SyntaxKind.SlashToken,
            SyntaxKind.PercentEqualsToken => SyntaxKind.PercentToken, SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandToken,
            SyntaxKind.PipeEqualsToken => SyntaxKind.PipeToken, SyntaxKind.CaretEqualsToken => SyntaxKind.CaretToken,
            SyntaxKind.LessThanLessThanEqualsToken => SyntaxKind.LessThanLessThanToken, SyntaxKind.GreaterThanGreaterThanEqualsToken => SyntaxKind.GreaterThanGreaterThanToken,
            _ => throw new LoweringException(assignment.Syntax, "Unsupported compound assignment.")
        };
        statements.Add(Statement(new BoundAssignmentExpression(assignment.Syntax, assignment.Type, SyntaxKind.EqualsToken, target,
            new BoundBinaryExpression(assignment.Syntax, assignment.Target.Type, op, target, value))));
        return Sequence(assignment.Syntax, _types.Unit, statements);
    }
    private BoundExpression PatternTest(BoundPatternTestExpression test)
    {
        var statements = new List<BoundStatement>();
        BoundExpression value = Capture(test.Value, statements, preservePlace: true);
        LocalSymbol result = Temp(test.Syntax, _types.Bool);
        statements.Add(Declare(result, Literal(test.Syntax, _types.Bool, false)));
        LabelSymbol end = Label(test.Syntax);
        BindPattern(test.Pattern, value, end, statements);
        statements.Add(Statement(Assign(result, Literal(test.Syntax, _types.Bool, true))));
        statements.Add(new BoundLabelStatement(test.Syntax, end));
        return Sequence(test.Syntax, _types.Bool, statements, Read(result));
    }
    private BoundExpression Match(BoundMatchExpression match)
    {
        var statements = new List<BoundStatement>();
        BoundExpression value = Capture(match.Scrutinee, statements, preservePlace: true);
        LocalSymbol? result = Result(match.Syntax, match.Type, statements);
        LabelSymbol end = Label(match.Syntax);
        foreach (BoundMatchArm arm in match.Arms)
        {
            LabelSymbol next = Label(arm.Syntax);
            BindPattern(arm.Pattern, value, next, statements);
            if (arm.Guard is not null) statements.Add(new BoundGotoStatement(arm.Syntax, next, Rewrite(arm.Guard), false));
            StoreResult(arm.Value, result, statements);
            statements.Add(new BoundGotoStatement(arm.Syntax, end));
            statements.Add(new BoundLabelStatement(arm.Syntax, next));
        }
        statements.Add(Statement(new BoundPanicExpression(match.Syntax, _types.Never)));
        statements.Add(new BoundLabelStatement(match.Syntax, end));
        return Sequence(match.Syntax, match.Type, statements, result is null ? null : Read(result));
    }

    private void BindPattern(BoundPattern pattern, BoundExpression value, LabelSymbol? fail, List<BoundStatement> statements)
    {
        void Check(BoundExpression condition)
        {
            if (fail is null) throw new LoweringException(pattern.Syntax, "Refutable pattern requires a failure target.");
            statements.Add(new BoundGotoStatement(pattern.Syntax, fail, condition, false));
        }
        switch (pattern.PatternKind)
        {
            case BoundPatternKind.Wildcard: case BoundPatternKind.Rest: return;
            case BoundPatternKind.Binding:
                BoundExpression bound = pattern.BindingMode == BindingMode.Value ? value
                    : new BoundUnaryExpression(pattern.Syntax, pattern.Variable!.Type, SyntaxKind.AmpersandToken, value);
                statements.Add(new BoundLetStatement(pattern.Syntax, new BoundPattern(pattern.Syntax, pattern.Variable!.Type,
                    BoundPatternKind.Binding, variable: pattern.Variable, irrefutable: true), bound, null));
                foreach (BoundPattern child in pattern.Subpatterns) BindPattern(child, value, fail, statements);
                return;
            case BoundPatternKind.Literal:
                Check(new BoundBinaryExpression(pattern.Syntax, _types.Bool, SyntaxKind.EqualsEqualsToken, value, Rewrite(pattern.Value!)));
                return;
            case BoundPatternKind.Range:
                var range = (BoundRangeExpression)pattern.Value!;
                if (range.Start is not null) Check(new BoundBinaryExpression(pattern.Syntax, _types.Bool, SyntaxKind.GreaterThanEqualsToken, value, Rewrite(range.Start)));
                if (range.End is not null) Check(new BoundBinaryExpression(pattern.Syntax, _types.Bool, range.Inclusive ? SyntaxKind.LessThanEqualsToken : SyntaxKind.LessThanToken, value, Rewrite(range.End)));
                return;
            case BoundPatternKind.Reference:
                BoundPattern referenced = pattern.Subpatterns[0];
                do
                {
                    value = new BoundUnaryExpression(pattern.Syntax, ((IndirectTypeSymbol)value.Type).ElementType,
                        SyntaxKind.StarToken, value, ValueCategory.MutablePlace);
                } while (!TypeSystem.Same(value.Type, referenced.InputType));
                BindPattern(referenced, value, fail, statements);
                return;
            case BoundPatternKind.Tuple: case BoundPatternKind.Aggregate:
                if (pattern.MatchedSymbol is VariantSymbol { Enum.Variants.Length: > 1 } variant)
                {
                    TypeSymbol isize = _types.Primitives["isize"];
                    Check(new BoundBinaryExpression(pattern.Syntax, _types.Bool, SyntaxKind.EqualsEqualsToken,
                        new BoundDiscriminantExpression(pattern.Syntax, isize, value), Literal(pattern.Syntax, isize, variant.Discriminant)));
                }
                for (int i = 0; i < pattern.Subpatterns.Length; i++)
                {
                    BoundPattern child = pattern.Subpatterns[i];
                    FieldSymbol? field = pattern.PatternKind == BoundPatternKind.Aggregate ? pattern.Fields[i] : null;
                    int index = child.InputIndex >= 0 ? child.InputIndex : i;
                    BindPattern(child, new BoundFieldExpression(child.Syntax, child.InputType, value, field, index, value.ValueCategory), fail, statements);
                }
                return;
            case BoundPatternKind.Or:
                LabelSymbol done = Label(pattern.Syntax);
                foreach (BoundPattern alternative in pattern.Subpatterns)
                {
                    LabelSymbol next = Label(pattern.Syntax);
                    BindPattern(alternative, value, next, statements);
                    statements.Add(new BoundGotoStatement(pattern.Syntax, done));
                    statements.Add(new BoundLabelStatement(pattern.Syntax, next));
                }
                if (fail is null) statements.Add(Statement(new BoundPanicExpression(pattern.Syntax, _types.Never)));
                else statements.Add(new BoundGotoStatement(pattern.Syntax, fail));
                statements.Add(new BoundLabelStatement(pattern.Syntax, done));
                return;
            default: throw new LoweringException(pattern.Syntax, $"Lowering is not implemented for '{pattern.PatternKind}' patterns.");
        }
    }
}
