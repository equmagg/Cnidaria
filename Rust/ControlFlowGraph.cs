using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Rust;

internal enum ProjectionKind : byte { Deref, Field, Index, ConstantIndex, Downcast }
internal readonly record struct Projection(ProjectionKind Kind, int Value, bool FromEnd = false);

internal readonly struct FlowPlace : IEquatable<FlowPlace>
{
    internal readonly int Local;
    internal readonly ImmutableArray<Projection> Projections;
    internal FlowPlace(int local, ImmutableArray<Projection> projections = default) { Local = local; Projections = projections.IsDefault ? [] : projections; }
    internal FlowPlace Project(ProjectionKind kind, int value = 0, bool fromEnd = false) => new(Local, Projections.Add(new Projection(kind, value, fromEnd)));
    internal FlowPlace Deref() => Project(ProjectionKind.Deref);
    internal FlowPlace Prefix(int length) => new(Local, Projections.RemoveRange(length, Projections.Length - length));
    internal bool IsIndirect => Projections.Any(p => p.Kind == ProjectionKind.Deref);
    public bool Equals(FlowPlace other) => Local == other.Local && Projections.SequenceEqual(other.Projections);
    public override bool Equals(object? obj) => obj is FlowPlace other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Local);
        foreach (Projection projection in Projections) hash.Add(projection);
        return hash.ToHashCode();
    }
}

internal enum OperandKind : byte { Copy, Move, Constant }
internal readonly record struct FlowOperand(OperandKind Kind, FlowPlace Place, TypeSymbol Type);

internal enum RvalueKind : byte { Use, Ref, AddressOf, Aggregate, Unary, Binary, Cast, Call, Discriminant, Length, Iterate }
internal sealed class FlowRvalue
{
    internal RvalueKind Kind { get; init; }
    internal TypeSymbol Type { get; init; } = null!;
    internal ImmutableArray<FlowOperand> Operands { get; init; } = [];
    internal FlowPlace Place { get; init; }
    internal bool IsMutable { get; init; }
    internal bool IsTwoPhase { get; init; }
    internal bool IsPromoted { get; init; }
    internal ImmutableArray<int> Fields { get; init; } = [];
    internal VariantSymbol? Variant { get; init; }
    internal ConversionKind Conversion { get; init; }
    internal FunctionSymbol? Callee { get; init; }
    internal ImmutableArray<TypeSymbol> TypeArguments { get; init; } = [];
}

internal enum StatementKind : byte { Assign, StorageDead, FakeRead }
internal sealed class FlowStatement(StatementKind kind, SyntaxNode syntax, FlowPlace place, FlowRvalue? value = null)
{
    internal StatementKind Kind { get; } = kind;
    internal SyntaxNode Syntax { get; } = syntax;
    internal FlowPlace Place { get; } = place;
    internal FlowRvalue? Value { get; } = value;
}

internal enum TerminatorKind : byte { Goto, Branch, Return, Unreachable }
internal sealed class FlowTerminator(TerminatorKind kind, SyntaxNode syntax, int target = -1, int otherwise = -1, FlowOperand condition = default)
{
    internal TerminatorKind Kind { get; } = kind;
    internal SyntaxNode Syntax { get; } = syntax;
    internal int Target { get; } = target;
    internal int Otherwise { get; } = otherwise;
    internal FlowOperand Condition { get; } = condition;
    internal IEnumerable<int> Successors => Kind switch
    {
        TerminatorKind.Goto => [Target],
        TerminatorKind.Branch => [Target, Otherwise],
        _ => []
    };
}

internal sealed class FlowBlock
{
    internal readonly List<FlowStatement> Statements = new();
    internal FlowTerminator Terminator = null!;
}

internal enum LocalKind : byte { Return, Parameter, User, Temporary, Static }
internal sealed class FlowLocal(LocalKind kind, TypeSymbol type, SyntaxNode syntax, Symbol? symbol)
{
    internal LocalKind Kind { get; } = kind;
    internal TypeSymbol Type { get; } = type;
    internal SyntaxNode Syntax { get; } = syntax;
    internal Symbol? Symbol { get; } = symbol;
    internal bool IsAnnotated { get; set; }
}

///<summary>Control flow graph of places, operands and storage scopes of one function body for borrow checking</summary>
internal sealed class FlowGraph(FunctionSymbol function)
{
    internal FunctionSymbol Function { get; } = function;
    internal readonly List<FlowLocal> Locals = new();
    internal readonly List<FlowBlock> Blocks = new();
}

internal sealed class FlowGraphBuilder
{
    private sealed class Scope { internal readonly List<int> Locals = new(); }
    private sealed record Breakable(LabelSymbol Label, int Break, int Continue, FlowPlace? Destination, int Depth);

    private readonly TypeSystem _types;
    private readonly Func<TypeSymbol, bool> _isCopy;
    private readonly FlowGraph _graph;
    private readonly Dictionary<Symbol, int> _variables = new();
    private readonly List<Scope> _scopes = new();
    private readonly List<Breakable> _breakables = new();
    private Scope _block = null!;
    private int _current = -1;

    private FlowGraphBuilder(TypeSystem types, FunctionSymbol function, Func<TypeSymbol, bool> isCopy) { _types = types; _graph = new FlowGraph(function); _isCopy = isCopy; }

    internal static FlowGraph Build(TypeSystem types, BoundFunction function, Func<TypeSymbol, bool> isCopy)
    {
        var builder = new FlowGraphBuilder(types, function.Symbol, isCopy);
        builder.Function(function);
        return builder._graph;
    }

    private void Function(BoundFunction function)
    {
        FunctionSymbol symbol = function.Symbol;
        _graph.Locals.Add(new FlowLocal(LocalKind.Return, symbol.ReturnType, function.Syntax, null));
        foreach (ParameterSymbol parameter in symbol.Parameters)
        {
            _variables.Add(parameter, _graph.Locals.Count);
            _graph.Locals.Add(new FlowLocal(LocalKind.Parameter, parameter.Type, parameter.Declaration, parameter));
        }
        _current = NewBlock();
        _block = Push();
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            BoundPattern pattern = function.Parameters[i];
            if (pattern.Variable == symbol.Parameters[i] && pattern.Subpatterns.IsEmpty) continue;
            Bind(pattern, new FlowPlace(_variables[symbol.Parameters[i]]), _block, false);
        }
        Into(new FlowPlace(0), function.Body!);
        Exit(_block);
        Pop();
        Terminate(new FlowTerminator(TerminatorKind.Return, function.Syntax));
        foreach (FlowBlock block in _graph.Blocks) block.Terminator ??= new FlowTerminator(TerminatorKind.Unreachable, function.Syntax);
    }

    private int NewBlock() { _graph.Blocks.Add(new FlowBlock()); return _graph.Blocks.Count - 1; }
    private void Start(int block) => _current = block;
    private void Emit(FlowStatement statement)
    {
        if (_current < 0) _current = NewBlock();
        _graph.Blocks[_current].Statements.Add(statement);
    }
    private void Terminate(FlowTerminator terminator)
    {
        if (_current < 0) _current = NewBlock();
        _graph.Blocks[_current].Terminator = terminator;
        _current = -1;
    }
    private void Goto(int target, SyntaxNode syntax) => Terminate(new FlowTerminator(TerminatorKind.Goto, syntax, target));
    private void Branch(FlowOperand condition, int then, int otherwise, SyntaxNode syntax) => Terminate(new FlowTerminator(TerminatorKind.Branch, syntax, then, otherwise, condition));
    private void Test(FlowOperand condition, int fail, SyntaxNode syntax)
    {
        int next = NewBlock();
        Branch(condition, next, fail, syntax);
        Start(next);
    }
    private void Assign(FlowPlace place, FlowRvalue value, SyntaxNode syntax) => Emit(new FlowStatement(StatementKind.Assign, syntax, place, value));

    private Scope Push() { var scope = new Scope(); _scopes.Add(scope); return scope; }
    private void Pop() => _scopes.RemoveAt(_scopes.Count - 1);
    private void Exit(Scope scope)
    {
        if (_current < 0) return;
        for (int i = scope.Locals.Count - 1; i >= 0; i--)
        {
            int local = scope.Locals[i];
            Emit(new FlowStatement(StatementKind.StorageDead, _graph.Locals[local].Syntax, new FlowPlace(local)));
        }
    }
    private void Unwind(int depth) { for (int i = _scopes.Count - 1; i >= depth; i--) Exit(_scopes[i]); }

    private int NewLocal(LocalKind kind, TypeSymbol type, SyntaxNode syntax, Symbol? symbol = null)
    {
        _graph.Locals.Add(new FlowLocal(kind, type, syntax, symbol));
        return _graph.Locals.Count - 1;
    }
    private int Temp(TypeSymbol type, SyntaxNode syntax) => NewLocal(LocalKind.Temporary, type, syntax);
    private int Declare(VariableSymbol variable, Scope scope)
    {
        if (_variables.TryGetValue(variable, out int existing)) return existing;
        int local = NewLocal(LocalKind.User, variable.Type, variable.Declaration ?? _graph.Function.Declaration, variable);
        _variables.Add(variable, local);
        scope.Locals.Add(local);
        return local;
    }
    private int Static(GlobalSymbol global)
    {
        if (_variables.TryGetValue(global, out int local)) return local;
        local = NewLocal(LocalKind.Static, global.Type, global.Declaration, global);
        _variables.Add(global, local);
        return local;
    }

    private static FlowOperand Constant(TypeSymbol type) => new(OperandKind.Constant, default, type);
    private FlowOperand Consume(FlowPlace place, TypeSymbol type) => new(_isCopy(type) ? OperandKind.Copy : OperandKind.Move, place, type);
    private static FlowRvalue Use(FlowOperand operand) => new() { Kind = RvalueKind.Use, Type = operand.Type, Operands = [operand] };
    private static FlowRvalue Ref(FlowPlace place, TypeSymbol type, bool mutable, bool twoPhase = false) => new() { Kind = RvalueKind.Ref, Type = type, Place = place, IsMutable = mutable, IsTwoPhase = twoPhase };

    private static bool IsPlace(BoundExpression expression) => expression switch
    {
        BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } } => false,
        BoundVariableExpression or BoundFieldExpression or BoundIndexExpression => true,
        BoundUnaryExpression { Operator: SyntaxKind.StarToken } => true,
        _ => false
    };
    private static bool IsConstant(BoundExpression expression)
        => expression is BoundLiteralExpression or BoundFunctionReference or BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } };
    private static bool IsUniqueReference(TypeSymbol type) => type.Resolve() is IndirectTypeSymbol { Kind: TypeKind.Reference, IsMutable: true };

    private FlowOperand AsOperand(BoundExpression expression, Scope? extend = null)
    {
        if (IsConstant(expression)) return Constant(expression.Type);
        int temp = Temp(expression.Type, expression.Syntax);
        Into(new FlowPlace(temp), expression, extend);
        return new FlowOperand(OperandKind.Move, new FlowPlace(temp), expression.Type);
    }

    private FlowPlace AsPlace(BoundExpression expression, Scope? extend = null)
    {
        switch (expression)
        {
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Static } global }: return new FlowPlace(Static(global));
            case BoundVariableExpression { Variable: not GlobalSymbol } variable: return new FlowPlace(_variables[variable.Variable]);
            case BoundUnaryExpression { Operator: SyntaxKind.StarToken } dereference: return AsPlace(dereference.Operand, extend).Deref();
            case BoundFieldExpression field: return AsPlace(field.Receiver, extend).Project(ProjectionKind.Field, field.TupleIndex);
            case BoundIndexExpression index:
            {
                FlowPlace receiver = AsPlace(index.Receiver, extend);
                int subscript = Temp(index.Index.Type, index.Index.Syntax);
                Into(new FlowPlace(subscript), index.Index);
                return receiver.Project(ProjectionKind.Index, subscript);
            }
        }
        int temp = Temp(expression.Type, expression.Syntax);
        (extend ?? _scopes[^1]).Locals.Add(temp);
        Into(new FlowPlace(temp), expression, extend);
        return new FlowPlace(temp);
    }

    private void Into(FlowPlace destination, BoundExpression expression, Scope? extend = null)
    {
        switch (expression)
        {
            case BoundBlockExpression block: Block(destination, block, extend); return;
            case BoundIfExpression conditional: If(destination, conditional, extend); return;
            case BoundMatchExpression match: Match(destination, match, extend); return;
            case BoundLoopExpression loop: Loop(destination, loop); return;
            case BoundForExpression loop: For(loop); return;
            case BoundControlFlowExpression control: Control(control); return;
            case BoundAssignmentExpression assignment: Assignment(assignment); return;
            case BoundBinaryExpression { Operator: SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken } logical: ShortCircuit(destination, logical); return;
            case BoundPatternTestExpression test: PatternTest(destination, test); return;
            case BoundConversionExpression { Conversion: ConversionKind.Never } never: Discard(never.Operand); return;
            case BoundCallExpression call:
                Assign(destination, Call(call), call.Syntax);
                if (call.Type.Kind == TypeKind.Never) Terminate(new FlowTerminator(TerminatorKind.Unreachable, call.Syntax));
                return;
        }
        if (IsPlace(expression)) Assign(destination, Use(Consume(AsPlace(expression, extend), expression.Type)), expression.Syntax);
        else Assign(destination, AsRvalue(expression, extend), expression.Syntax);
    }

    private FlowRvalue AsRvalue(BoundExpression expression, Scope? extend = null)
    {
        switch (expression)
        {
            case BoundTupleExpression tuple:
                return new FlowRvalue { Kind = RvalueKind.Aggregate, Type = tuple.Type, Operands = tuple.Elements.Select(e => AsOperand(e, extend)).ToImmutableArray() };
            case BoundArrayExpression array:
                return new FlowRvalue { Kind = RvalueKind.Aggregate, Type = array.Type, Operands = array.Elements.Select(e => AsOperand(e, extend)).ToImmutableArray() };
            case BoundRangeExpression range:
                return new FlowRvalue { Kind = RvalueKind.Aggregate, Type = range.Type, Operands = new[] { range.Start, range.End }.OfType<BoundExpression>().Select(e => AsOperand(e)).ToImmutableArray() };
            case BoundConstructionExpression construction: return Construct(construction, extend);
            case BoundUnaryExpression { Operator: SyntaxKind.AmpersandToken or SyntaxKind.AmpersandAmpersandToken } borrow: return Borrow(borrow, extend);
            case BoundUnaryExpression { Operator: not SyntaxKind.StarToken } unary:
                return new FlowRvalue { Kind = RvalueKind.Unary, Type = unary.Type, Operands = [AsOperand(unary.Operand)] };
            case BoundBinaryExpression { Operator: not (SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken) } binary:
                return new FlowRvalue { Kind = RvalueKind.Binary, Type = binary.Type, Operands = [AsOperand(binary.Left), AsOperand(binary.Right)] };
            case BoundConversionExpression conversion: return Convert(conversion, extend);
            case BoundCallExpression call: return Call(call);
        }
        return Use(AsOperand(expression, extend));
    }

    private FlowRvalue Construct(BoundConstructionExpression construction, Scope? extend)
    {
        var operands = construction.Fields.Select(f => IsUniqueReference(f.Field.Type) && IsPlace(f.Value) && IsUniqueReference(f.Value.Type)
            ? Reborrow(f.Value, false) : AsOperand(f.Value, extend)).ToList();
        var fields = construction.Fields.Select(f => f.Field.Ordinal).ToList();
        if (construction.Base is not null)
        {
            FlowPlace source = AsPlace(construction.Base);
            var type = (NamedTypeSymbol)construction.Type;
            foreach (FieldSymbol field in construction.Variant?.Fields ?? type.Definition.Fields)
            {
                if (fields.Contains(field.Ordinal)) continue;
                fields.Add(field.Ordinal);
                operands.Add(Consume(source.Project(ProjectionKind.Field, field.Ordinal), _types.FieldType(field, type)));
            }
        }
        return new FlowRvalue { Kind = RvalueKind.Aggregate, Type = construction.Type, Operands = operands.ToImmutableArray(), Fields = fields.ToImmutableArray(), Variant = construction.Variant };
    }

    private FlowRvalue Borrow(BoundUnaryExpression borrow, Scope? extend)
    {
        var type = (IndirectTypeSymbol)borrow.Type;
        if (borrow.IsRawBorrow)
            return new FlowRvalue { Kind = RvalueKind.AddressOf, Type = type, Place = AsPlace(borrow.Operand, extend), IsMutable = type.IsMutable };
        if (borrow.Operator == SyntaxKind.AmpersandAmpersandToken)
        {
            var inner = (IndirectTypeSymbol)type.ElementType;
            int temp = Temp(inner, borrow.Syntax);
            (extend ?? _scopes[^1]).Locals.Add(temp);
            Assign(new FlowPlace(temp), Ref(AsPlace(borrow.Operand, extend), inner, inner.IsMutable), borrow.Syntax);
            return Ref(new FlowPlace(temp), type, false);
        }
        if (!type.IsMutable && Binder.IsConstant(borrow.Operand))
        {
            int promoted = NewLocal(LocalKind.Static, borrow.Operand.Type, borrow.Operand.Syntax);
            return new FlowRvalue { Kind = RvalueKind.Ref, Type = type, Place = new FlowPlace(promoted), IsPromoted = true };
        }
        // Method receiver autorefs carry the receiver's syntax and are activated only by the call
        bool autoref = borrow.Syntax is not UnaryExpressionSyntax;
        return Ref(AsPlace(borrow.Operand, extend), type, type.IsMutable, autoref && type.IsMutable);
    }

    private FlowRvalue Convert(BoundConversionExpression conversion, Scope? extend)
    {
        switch (conversion.Conversion)
        {
            case ConversionKind.Identity: return AsRvalue(conversion.Operand, extend);
            case ConversionKind.Never:
                Discard(conversion.Operand);
                return Use(Constant(conversion.Type));
            case ConversionKind.MutableToSharedReference:
                return Ref(AsPlace(conversion.Operand, extend).Deref(), conversion.Type, false);
            default:
                return new FlowRvalue { Kind = RvalueKind.Cast, Type = conversion.Type, Operands = [AsOperand(conversion.Operand, extend)], Conversion = conversion.Conversion };
        }
    }

    private FlowRvalue Call(BoundCallExpression call)
    {
        var reference = call.Target as BoundFunctionReference;
        FlowOperand target = AsOperand(call.Target);
        ImmutableArray<TypeSymbol> declared = reference is not null ? reference.Function.Parameters.Select(p => p.Type).ToImmutableArray()
            : call.Target.Type is FunctionTypeSymbol signature ? signature.ParameterTypes : [];
        var operands = ImmutableArray.CreateBuilder<FlowOperand>();
        operands.Add(target);
        for (int i = 0; i < call.Arguments.Length; i++)
        {
            BoundExpression argument = call.Arguments[i];
            if (i < declared.Length && IsUniqueReference(declared[i]) && IsPlace(argument) && IsUniqueReference(argument.Type))
                operands.Add(Reborrow(argument, true));
            else operands.Add(AsOperand(argument));
        }
        return new FlowRvalue { Kind = RvalueKind.Call, Type = call.Type, Operands = operands.ToImmutable(), Callee = reference?.Function, TypeArguments = reference?.TypeArguments ?? [] };
    }
    private FlowOperand Reborrow(BoundExpression reference, bool twoPhase)
    {
        int temp = Temp(reference.Type, reference.Syntax);
        Assign(new FlowPlace(temp), Ref(AsPlace(reference).Deref(), reference.Type, true, twoPhase), reference.Syntax);
        return new FlowOperand(OperandKind.Move, new FlowPlace(temp), reference.Type);
    }

    private void Discard(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundAssignmentExpression assignment: Assignment(assignment); return;
            case BoundControlFlowExpression control: Control(control); return;
        }
        Into(new FlowPlace(Temp(expression.Type, expression.Syntax)), expression);
    }

    private void Assignment(BoundAssignmentExpression assignment)
    {
        if (assignment.Operator == SyntaxKind.EqualsToken)
        {
            FlowRvalue value = IsUniqueReference(assignment.Target.Type) && IsPlace(assignment.Value) && IsUniqueReference(assignment.Value.Type)
                ? Ref(AsPlace(assignment.Value).Deref(), assignment.Value.Type, true) : AsRvalue(assignment.Value);
            Assign(AsPlace(assignment.Target), value, assignment.Syntax);
            return;
        }
        FlowOperand operand = AsOperand(assignment.Value);
        FlowPlace target = AsPlace(assignment.Target);
        Assign(target, new FlowRvalue { Kind = RvalueKind.Binary, Type = assignment.Target.Type,
            Operands = [new FlowOperand(OperandKind.Copy, target, assignment.Target.Type), operand] }, assignment.Syntax);
    }

    private void Block(FlowPlace destination, BoundBlockExpression block, Scope? extend)
    {
        Scope scope = Push(), outer = _block;
        _block = scope;
        int end = -1;
        if (block.Label is not null)
        {
            end = NewBlock();
            _breakables.Add(new Breakable(block.Label, end, -1, destination, _scopes.Count - 1));
        }
        foreach (BoundStatement statement in block.Statements)
        {
            switch (statement)
            {
                case BoundExpressionStatement { Expression: BoundExpression expression }:
                {
                    Scope temporaries = Push();
                    Discard(expression);
                    Exit(temporaries); Pop();
                    break;
                }
                case BoundLetStatement let: Let(let); break;
                case BoundExpressionStatement: break;
                default: throw new InvalidOperationException($"Unexpected statement '{statement.Kind}' in borrow checking.");
            }
        }
        if (block.Tail is not null)
        {
            Scope temporaries = Push();
            Into(destination, block.Tail, extend);
            Exit(temporaries); Pop();
        }
        Exit(scope); Pop();
        _block = outer;
        if (block.Label is null) return;
        _breakables.RemoveAt(_breakables.Count - 1);
        Goto(end, block.Syntax);
        Start(end);
    }

    private void Let(BoundLetStatement let)
    {
        Scope temporaries = Push();
        BoundPattern pattern = let.Pattern;
        if (let.Initializer is null)
            foreach (VariableSymbol variable in pattern.Descendants().Prepend(pattern).OfType<BoundPattern>().Select(p => p.Variable).OfType<VariableSymbol>()) Declare(variable, _block);
        else if (pattern is { PatternKind: BoundPatternKind.Binding, BindingMode: BindingMode.Value, Subpatterns.IsEmpty: true, Variable: VariableSymbol variable } && let.Else is null)
        {
            int local = Declare(variable, _block);
            bool annotated = let.Syntax is LetStatementSyntax { Type: not null };
            _graph.Locals[local].IsAnnotated = annotated;
            if (annotated && IsUniqueReference(variable.Type) && IsPlace(let.Initializer) && IsUniqueReference(let.Initializer.Type))
                Assign(new FlowPlace(local), Ref(AsPlace(let.Initializer).Deref(), variable.Type, true), let.Initializer.Syntax);
            else Into(new FlowPlace(local), let.Initializer, _block);
        }
        else
        {
            FlowPlace place = AsPlace(let.Initializer, _block);
            Emit(new FlowStatement(StatementKind.FakeRead, let.Initializer.Syntax, place));
            if (let.Else is not null)
            {
                int fail = NewBlock(), success = NewBlock();
                TestPattern(pattern, place, fail);
                Goto(success, let.Syntax);
                Start(fail);
                Discard(let.Else);
                Start(success);
            }
            Bind(pattern, place, _block, false);
        }
        Exit(temporaries); Pop();
    }

    private void If(FlowPlace destination, BoundIfExpression conditional, Scope? extend)
    {
        Scope scope = Push();
        int then = NewBlock(), otherwise = NewBlock(), end = NewBlock();
        Condition(conditional.Condition, then, otherwise, scope);
        Start(then);
        Into(destination, conditional.Then, extend);
        Exit(scope); Goto(end, conditional.Syntax);
        Start(otherwise);
        Exit(scope);
        if (conditional.Else is not null) Into(destination, conditional.Else, extend);
        Goto(end, conditional.Syntax);
        Pop();
        Start(end);
    }

    private void Condition(BoundExpression condition, int then, int otherwise, Scope scope)
    {
        switch (condition)
        {
            case BoundPatternTestExpression test:
            {
                FlowPlace place = AsPlace(test.Value, scope);
                Emit(new FlowStatement(StatementKind.FakeRead, test.Value.Syntax, place));
                TestPattern(test.Pattern, place, otherwise);
                Bind(test.Pattern, place, scope, false);
                Goto(then, test.Syntax);
                return;
            }
            case BoundBinaryExpression { Operator: SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken } logical:
            {
                int middle = NewBlock();
                bool and = logical.Operator == SyntaxKind.AmpersandAmpersandToken;
                Condition(logical.Left, and ? middle : then, and ? otherwise : middle, scope);
                Start(middle);
                Condition(logical.Right, then, otherwise, scope);
                return;
            }
        }
        Scope temporaries = Push();
        FlowOperand value = AsOperand(condition);
        Exit(temporaries); Pop();
        Branch(value, then, otherwise, condition.Syntax);
    }

    private void ShortCircuit(FlowPlace destination, BoundBinaryExpression logical)
    {
        bool and = logical.Operator == SyntaxKind.AmpersandAmpersandToken;
        FlowOperand left = AsOperand(logical.Left);
        int right = NewBlock(), shortcut = NewBlock(), end = NewBlock();
        Branch(left, and ? right : shortcut, and ? shortcut : right, logical.Syntax);
        Start(right);
        Into(destination, logical.Right);
        Goto(end, logical.Syntax);
        Start(shortcut);
        Assign(destination, Use(Constant(logical.Type)), logical.Syntax);
        Goto(end, logical.Syntax);
        Start(end);
    }

    private void PatternTest(FlowPlace destination, BoundPatternTestExpression test)
    {
        FlowPlace place = AsPlace(test.Value);
        int fail = NewBlock(), end = NewBlock();
        TestPattern(test.Pattern, place, fail);
        Bind(test.Pattern, place, _scopes[^1], false);
        Assign(destination, Use(Constant(test.Type)), test.Syntax);
        Goto(end, test.Syntax);
        Start(fail);
        Assign(destination, Use(Constant(test.Type)), test.Syntax);
        Goto(end, test.Syntax);
        Start(end);
    }

    private void Loop(FlowPlace destination, BoundLoopExpression loop)
    {
        int header = NewBlock(), exit = NewBlock();
        Goto(header, loop.Syntax);
        Start(header);
        _breakables.Add(new Breakable(loop.Label, exit, header, destination, _scopes.Count));
        if (loop.Condition is null)
        {
            Discard(loop.Body);
            Goto(header, loop.Syntax);
        }
        else
        {
            Scope scope = Push();
            int body = NewBlock(), done = NewBlock();
            Condition(loop.Condition, body, done, scope);
            Start(body);
            Discard(loop.Body);
            Exit(scope); Goto(header, loop.Syntax);
            Start(done);
            Exit(scope); Goto(exit, loop.Syntax);
            Pop();
        }
        _breakables.RemoveAt(_breakables.Count - 1);
        Start(exit);
    }

    private void For(BoundForExpression loop)
    {
        Scope scope = Push();
        int iterator = Temp(loop.Iterator.Type, loop.Iterator.Syntax);
        Into(new FlowPlace(iterator), loop.Iterator, scope);
        int header = NewBlock(), body = NewBlock(), exit = NewBlock();
        Goto(header, loop.Syntax);
        Start(header);
        Branch(Constant(_types.Bool), body, exit, loop.Syntax);
        Start(body);
        _breakables.Add(new Breakable(loop.Label, exit, header, null, _scopes.Count));
        Scope iteration = Push();
        int item = Temp(loop.Pattern.InputType, loop.Pattern.Syntax);
        Assign(new FlowPlace(item), new FlowRvalue { Kind = RvalueKind.Iterate, Type = loop.Pattern.InputType, Place = new FlowPlace(iterator) }, loop.Iterator.Syntax);
        Bind(loop.Pattern, new FlowPlace(item), iteration, false);
        Discard(loop.Body);
        Exit(iteration); Pop();
        Goto(header, loop.Syntax);
        _breakables.RemoveAt(_breakables.Count - 1);
        Start(exit);
        Exit(scope); Pop();
    }

    private void Match(FlowPlace destination, BoundMatchExpression match, Scope? extend)
    {
        Scope scope = Push();
        FlowPlace place = AsPlace(match.Scrutinee, scope);
        Emit(new FlowStatement(StatementKind.FakeRead, match.Scrutinee.Syntax, place));
        int end = NewBlock();
        foreach (BoundMatchArm arm in match.Arms)
        {
            int next = NewBlock();
            Scope bindings = Push();
            TestPattern(arm.Pattern, place, next);
            if (arm.Guard is not null)
            {
                Bind(arm.Pattern, place, bindings, true);
                Scope temporaries = Push();
                FlowOperand guard = AsOperand(arm.Guard);
                Exit(temporaries); Pop();
                int accepted = NewBlock(), rejected = NewBlock();
                Branch(guard, accepted, rejected, arm.Guard.Syntax);
                Start(rejected);
                Exit(bindings); Goto(next, arm.Syntax);
                Start(accepted);
            }
            Bind(arm.Pattern, place, bindings, false);
            Scope armTemporaries = Push();
            Into(destination, arm.Value, extend);
            Exit(armTemporaries); Pop();
            Exit(bindings); Pop();
            Goto(end, arm.Syntax);
            Start(next);
        }
        Terminate(new FlowTerminator(TerminatorKind.Unreachable, match.Syntax));
        Start(end);
        Exit(scope); Pop();
    }

    private void Control(BoundControlFlowExpression control)
    {
        if (control.Kind == BoundNodeKind.Return)
        {
            if (control.Value is not null) Into(new FlowPlace(0), control.Value);
            Unwind(0);
            Terminate(new FlowTerminator(TerminatorKind.Return, control.Syntax));
            return;
        }
        Breakable target = _breakables.Last(b => ReferenceEquals(b.Label, control.Target));
        if (control.Value is not null)
        {
            if (target.Destination is FlowPlace destination) Into(destination, control.Value);
            else Discard(control.Value);
        }
        Unwind(target.Depth);
        Goto(control.Kind == BoundNodeKind.Break ? target.Break : target.Continue, control.Syntax);
    }

    private static TypeSymbol Dereferenced(TypeSymbol type) => ((IndirectTypeSymbol)type.Resolve()).ElementType;

    private void TestPattern(BoundPattern pattern, FlowPlace place, int fail)
    {
        switch (pattern.PatternKind)
        {
            case BoundPatternKind.Binding:
                foreach (BoundPattern subpattern in pattern.Subpatterns) TestPattern(subpattern, place, fail);
                return;
            case BoundPatternKind.Literal:
                Compare(place, pattern, pattern.Value!, fail);
                return;
            case BoundPatternKind.Range:
            {
                var range = (BoundRangeExpression)pattern.Value!;
                if (range.Start is not null) Compare(place, pattern, range.Start, fail);
                if (range.End is not null) Compare(place, pattern, range.End, fail);
                return;
            }
            case BoundPatternKind.Reference:
            {
                (FlowPlace inner, _) = Referent(pattern, place);
                TestPattern(pattern.Subpatterns[0], inner, fail);
                return;
            }
            case BoundPatternKind.Tuple:
                for (int i = 0; i < pattern.Subpatterns.Length; i++)
                {
                    BoundPattern element = pattern.Subpatterns[i];
                    TestPattern(element, place.Project(ProjectionKind.Field, element.InputIndex >= 0 ? element.InputIndex : i), fail);
                }
                return;
            case BoundPatternKind.Aggregate:
            {
                FlowPlace receiver = place;
                if (pattern.MatchedSymbol is VariantSymbol variant)
                {
                    if (variant.Enum.Variants.Length > 1)
                    {
                        int discriminant = Temp(_types.Bool, pattern.Syntax);
                        Assign(new FlowPlace(discriminant), new FlowRvalue { Kind = RvalueKind.Discriminant, Type = _types.Bool, Place = place }, pattern.Syntax);
                        Test(new FlowOperand(OperandKind.Move, new FlowPlace(discriminant), _types.Bool), fail, pattern.Syntax);
                    }
                    receiver = place.Project(ProjectionKind.Downcast, variant.Enum.Variants.IndexOf(variant));
                }
                for (int i = 0; i < pattern.Subpatterns.Length; i++)
                    TestPattern(pattern.Subpatterns[i], receiver.Project(ProjectionKind.Field, pattern.Fields[i].Ordinal), fail);
                return;
            }
            case BoundPatternKind.Slice:
            {
                if (pattern.InputType is SequenceTypeSymbol { Kind: TypeKind.Slice })
                {
                    int length = Temp(_types.Bool, pattern.Syntax);
                    Assign(new FlowPlace(length), new FlowRvalue { Kind = RvalueKind.Length, Type = _types.Bool, Place = place }, pattern.Syntax);
                    Test(new FlowOperand(OperandKind.Move, new FlowPlace(length), _types.Bool), fail, pattern.Syntax);
                }
                foreach (var (element, elementPlace) in SliceElements(pattern, place)) TestPattern(element, elementPlace, fail);
                return;
            }
            case BoundPatternKind.Or:
            {
                int done = NewBlock();
                foreach (BoundPattern alternative in pattern.Subpatterns)
                {
                    int next = NewBlock();
                    TestPattern(alternative, place, next);
                    Goto(done, alternative.Syntax);
                    Start(next);
                }
                Goto(fail, pattern.Syntax);
                Start(done);
                return;
            }
        }
    }
    private void Compare(FlowPlace place, BoundPattern pattern, BoundExpression value, int fail)
    {
        int result = Temp(_types.Bool, pattern.Syntax);
        Assign(new FlowPlace(result), new FlowRvalue { Kind = RvalueKind.Binary, Type = _types.Bool,
            Operands = [new FlowOperand(OperandKind.Copy, place, pattern.InputType), AsOperand(value)] }, pattern.Syntax);
        Test(new FlowOperand(OperandKind.Move, new FlowPlace(result), _types.Bool), fail, pattern.Syntax);
    }
    private static (FlowPlace Place, TypeSymbol Type) Referent(BoundPattern pattern, FlowPlace place)
    {
        TypeSymbol type = pattern.InputType;
        BoundPattern referenced = pattern.Subpatterns[0];
        do { place = place.Deref(); type = Dereferenced(type); } while (type is IndirectTypeSymbol && !TypeSystem.Same(type, referenced.InputType));
        return (place, type);
    }
    private static IEnumerable<(BoundPattern Pattern, FlowPlace Place)> SliceElements(BoundPattern pattern, FlowPlace place)
    {
        ulong? length = (pattern.InputType as SequenceTypeSymbol)?.Length;
        int count = pattern.Subpatterns.Length, rest = -1;
        for (int i = 0; i < count; i++) if (pattern.Subpatterns[i].PatternKind == BoundPatternKind.Rest) rest = i;
        for (int i = 0; i < count; i++)
        {
            BoundPattern element = pattern.Subpatterns[i];
            if (element.PatternKind == BoundPatternKind.Rest) continue;
            if (rest < 0 || i < rest) yield return (element, place.Project(ProjectionKind.ConstantIndex, i));
            else if (length is ulong fixedLength) yield return (element, place.Project(ProjectionKind.ConstantIndex, (int)fixedLength - (count - i)));
            else yield return (element, place.Project(ProjectionKind.ConstantIndex, count - i, true));
        }
    }

    private void Bind(BoundPattern pattern, FlowPlace place, Scope scope, bool guard)
    {
        switch (pattern.PatternKind)
        {
            case BoundPatternKind.Binding:
            {
                VariableSymbol variable = pattern.Variable!;
                int local = Declare(variable, scope);
                TypeSymbol input = pattern.InputType;
                FlowRvalue value = pattern.BindingMode switch
                {
                    BindingMode.Value => Use(guard ? new FlowOperand(OperandKind.Copy, place, input) : Consume(place, input)),
                    BindingMode.Reference => Ref(place, variable.Type, false),
                    _ => Ref(place, variable.Type, true)
                };
                Assign(new FlowPlace(local), value, pattern.Syntax);
                foreach (BoundPattern subpattern in pattern.Subpatterns) Bind(subpattern, place, scope, guard);
                return;
            }
            case BoundPatternKind.Reference:
                Bind(pattern.Subpatterns[0], Referent(pattern, place).Place, scope, guard);
                return;
            case BoundPatternKind.Tuple:
                for (int i = 0; i < pattern.Subpatterns.Length; i++)
                {
                    BoundPattern element = pattern.Subpatterns[i];
                    Bind(element, place.Project(ProjectionKind.Field, element.InputIndex >= 0 ? element.InputIndex : i), scope, guard);
                }
                return;
            case BoundPatternKind.Aggregate:
            {
                FlowPlace receiver = pattern.MatchedSymbol is VariantSymbol variant ? place.Project(ProjectionKind.Downcast, variant.Enum.Variants.IndexOf(variant)) : place;
                for (int i = 0; i < pattern.Subpatterns.Length; i++)
                    Bind(pattern.Subpatterns[i], receiver.Project(ProjectionKind.Field, pattern.Fields[i].Ordinal), scope, guard);
                return;
            }
            case BoundPatternKind.Slice:
                foreach (var (element, elementPlace) in SliceElements(pattern, place)) Bind(element, elementPlace, scope, guard);
                return;
        }
    }
}
