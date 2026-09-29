using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C;

internal readonly struct Assertion
{
    public Assertion(GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        Left = left;
        Code = code;
        Right = right;
    }

    public GimpleValue Left { get; }
    public GimpleTreeCode Code { get; }
    public GimpleValue Right { get; }
}

internal sealed class AssertionScope
{
    public AssertionScope(Assertion assertion, AssertionScope? parent)
    {
        Assertion = assertion;
        Parent = parent;
    }

    public Assertion Assertion { get; }
    public AssertionScope? Parent { get; }
}

internal readonly struct ValueRange
{
    public ValueRange(Int128 min, Int128 max)
    {
        Min = min;
        Max = max;
    }

    public Int128 Min { get; }
    public Int128 Max { get; }
    public bool IsPoint => Min == Max;

    public ValueRange Intersect(ValueRange other) => new(Int128.Max(Min, other.Min), Int128.Min(Max, other.Max));
    public ValueRange Union(ValueRange other) => new(Int128.Min(Min, other.Min), Int128.Max(Max, other.Max));
    public bool Contains(ValueRange other) => Min <= other.Min && other.Max <= Max;
}

/// <summary>Holds the relations known to be true on entry to each block, and the ranges SSA values keep everywhere</summary>
internal sealed class AssertionTable
{
    private const int MaxScopeWalk = 64;
    private const int MaxRefineDepth = 1;

    private readonly Dictionary<ControlFlowBlock, AssertionScope?> _scopes = new();
    private readonly Dictionary<GimpleName, ValueRange?> _ranges = new();
    private readonly HashSet<GimpleName> _pending = new();
    private readonly IReadOnlyDictionary<GimpleName, GimpleStatementAnnotations> _definitions;
    private readonly TargetInfo _target;

    public AssertionTable(TargetInfo target, IReadOnlyDictionary<GimpleName, GimpleStatementAnnotations> definitions)
    {
        _target = target;
        _definitions = definitions;
    }

    internal void Record(ControlFlowBlock block, AssertionScope? scope) => _scopes[block] = scope;

    public bool Proves(ControlFlowBlock block, GimpleValue left, GimpleTreeCode code, GimpleValue right)
        => _scopes.TryGetValue(block, out var scope) && Evaluate(scope, left, code, right) == true;

    public bool? Evaluate(AssertionScope? scope, GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        var steps = 0;
        for (var current = scope; current is not null && steps++ < MaxScopeWalk; current = current.Parent)
        {
            if (Implied(current.Assertion, left, code, right) is { } truth)
                return truth;
        }

        return RangeAt(left, scope, MaxRefineDepth) is { } leftRange && RangeAt(right, scope, MaxRefineDepth) is { } rightRange
            ? Compare(leftRange, code, rightRange)
            : null;
    }

    public static bool IsTracked(QualifiedType type)
        => type.Type.Kind == TypeKind.Pointer || GimpleTypes.IsIntegerLike(type);

    private static bool? Implied(Assertion known, GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        if (Same(known.Left, left) && Same(known.Right, right))
            return Implies(known.Code, code);
        if (Same(known.Left, right) && Same(known.Right, left))
            return Implies(Swap(known.Code), code);
        return null;
    }

    private static bool? Implies(GimpleTreeCode known, GimpleTreeCode query)
    {
        if (known == query)
            return true;
        return (known, query) switch
        {
            (GimpleTreeCode.LtExpr, GimpleTreeCode.LeExpr or GimpleTreeCode.NeExpr) => true,
            (GimpleTreeCode.LtExpr, GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr or GimpleTreeCode.EqExpr) => false,
            (GimpleTreeCode.GtExpr, GimpleTreeCode.GeExpr or GimpleTreeCode.NeExpr) => true,
            (GimpleTreeCode.GtExpr, GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or GimpleTreeCode.EqExpr) => false,
            (GimpleTreeCode.LeExpr, GimpleTreeCode.GtExpr) => false,
            (GimpleTreeCode.GeExpr, GimpleTreeCode.LtExpr) => false,
            (GimpleTreeCode.EqExpr, GimpleTreeCode.LeExpr or GimpleTreeCode.GeExpr) => true,
            (GimpleTreeCode.EqExpr, GimpleTreeCode.NeExpr or GimpleTreeCode.LtExpr or GimpleTreeCode.GtExpr) => false,
            (GimpleTreeCode.NeExpr, GimpleTreeCode.EqExpr) => false,
            _ => null,
        };
    }

    private static bool? Compare(ValueRange left, GimpleTreeCode code, ValueRange right)
        => code switch
        {
            GimpleTreeCode.LtExpr => left.Max < right.Min ? true : left.Min >= right.Max ? false : null,
            GimpleTreeCode.LeExpr => left.Max <= right.Min ? true : left.Min > right.Max ? false : null,
            GimpleTreeCode.GtExpr => left.Min > right.Max ? true : left.Max <= right.Min ? false : null,
            GimpleTreeCode.GeExpr => left.Min >= right.Max ? true : left.Max < right.Min ? false : null,
            GimpleTreeCode.EqExpr => left.IsPoint && right.IsPoint && left.Min == right.Min ? true : left.Max < right.Min || right.Max < left.Min ? false : null,
            GimpleTreeCode.NeExpr => left.IsPoint && right.IsPoint && left.Min == right.Min ? false : left.Max < right.Min || right.Max < left.Min ? true : null,
            _ => null,
        };

    public static GimpleTreeCode Swap(GimpleTreeCode code) => code switch
    {
        GimpleTreeCode.LtExpr => GimpleTreeCode.GtExpr,
        GimpleTreeCode.LeExpr => GimpleTreeCode.GeExpr,
        GimpleTreeCode.GtExpr => GimpleTreeCode.LtExpr,
        GimpleTreeCode.GeExpr => GimpleTreeCode.LeExpr,
        _ => code,
    };

    public static bool Same(GimpleValue left, GimpleValue right)
        => ReferenceEquals(left, right) ||
           left is GimpleConstantValue a && right is GimpleConstantValue b &&
           TryGetConstant(a, out var x) && TryGetConstant(b, out var y) && x == y;

    private static bool TryGetConstant(GimpleConstantValue constant, out Int128 value)
    {
        value = 0;
        if (constant.Value is null)
            return true;
        if (!ScalarEvolution.TryGetInteger(constant.Value, out var number))
            return false;
        value = constant.Value is ulong unsignedNumber ? unsignedNumber : number;
        return true;
    }

    private ValueRange? RangeAt(GimpleValue value, AssertionScope? scope, int depth)
    {
        ValueRange range;
        switch (value)
        {
            case GimpleConstantValue constant when TryGetConstant(constant, out var number):
                return new ValueRange(number, number);
            case GimpleName name when GlobalRange(name) is { } global:
                range = global;
                break;
            default:
                return null;
        }

        var steps = 0;
        for (var current = scope; current is not null && steps++ < MaxScopeWalk; current = current.Parent)
        {
            var known = current.Assertion;
            if (ReferenceEquals(known.Left, value))
                range = Refine(range, known.Code, known.Right, scope, depth);
            else if (ReferenceEquals(known.Right, value))
                range = Refine(range, Swap(known.Code), known.Left, scope, depth);
        }

        if (depth > 0 && value is GimpleName derived && GimpleTypes.IsSigned(derived.Type) &&
            derived.Definition?.Statement is GimpleAssignStatement { Subcode: GimpleTreeCode.PlusExpr or GimpleTreeCode.MinusExpr, Operands: [GimpleName source, GimpleConstantValue offset] } assign &&
            TryGetConstant(offset, out var amount) && RangeAt(source, scope, depth - 1) is { } sourceRange)
        {
            var shift = assign.Subcode == GimpleTreeCode.PlusExpr ? amount : -amount;
            range = range.Intersect(new ValueRange(sourceRange.Min + shift, sourceRange.Max + shift));
        }
        return range;
    }

    private ValueRange Refine(ValueRange range, GimpleTreeCode code, GimpleValue other, AssertionScope? scope, int depth)
    {
        if (other is not GimpleConstantValue && depth == 0 || RangeAt(other, scope, depth - 1) is not { } bound)
            return range;
        return code switch
        {
            GimpleTreeCode.LtExpr => new ValueRange(range.Min, Int128.Min(range.Max, bound.Max - 1)),
            GimpleTreeCode.LeExpr => new ValueRange(range.Min, Int128.Min(range.Max, bound.Max)),
            GimpleTreeCode.GtExpr => new ValueRange(Int128.Max(range.Min, bound.Min + 1), range.Max),
            GimpleTreeCode.GeExpr => new ValueRange(Int128.Max(range.Min, bound.Min), range.Max),
            GimpleTreeCode.EqExpr => range.Intersect(bound),
            GimpleTreeCode.NeExpr when bound.IsPoint && range.Min == bound.Min => new ValueRange(range.Min + 1, range.Max),
            GimpleTreeCode.NeExpr when bound.IsPoint && range.Max == bound.Min => new ValueRange(range.Min, range.Max - 1),
            _ => range,
        };
    }

    public ValueRange? GlobalRange(GimpleName name)
    {
        if (_ranges.TryGetValue(name, out var cached))
            return cached;
        if (!TryGetTypeRange(name.Type, out var typeRange))
            return null;
        if (!_pending.Add(name))
            return typeRange;

        var computed = ComputeRange(name, typeRange);
        _pending.Remove(name);
        var result = computed is { } narrowed ? narrowed.Intersect(typeRange) : typeRange;
        _ranges[name] = result;
        return result;
    }

    private ValueRange? ComputeRange(GimpleName name, ValueRange typeRange)
    {
        if (name.IsUndefined || name.Definition is not { } definition)
            return null;
        if (definition.Kind == GimpleDefinitionKind.Phi && definition.Statement is GimplePhi phi)
            return PhiRange(phi, typeRange);
        if (definition.Kind != GimpleDefinitionKind.Statement || definition.Statement is not GimpleAssignStatement assign ||
            !_definitions.TryGetValue(name, out var instruction) || (instruction.Flags & GimpleStatementFlags.ReadsMemory) != 0)
            return null;

        var operands = assign.Operands;
        switch (assign.Subcode)
        {
            case GimpleTreeCode.IntegerCst:
            case GimpleTreeCode.SsaName:
                return operands.Length == 1 ? Operand(operands[0]) : null;

            case GimpleTreeCode.NopExpr:
            case GimpleTreeCode.ConvertExpr:
                return operands.Length == 1 && Operand(operands[0]) is { } source && typeRange.Contains(source) ? source : null;

            case GimpleTreeCode.BitAndExpr when operands.Length == 2:
                {
                    var left = Operand(operands[0]);
                    var right = Operand(operands[1]);
                    var leftBound = left is { } a && a.Min >= 0 ? a.Max : (Int128?)null;
                    var rightBound = right is { } b && b.Min >= 0 ? b.Max : (Int128?)null;
                    if (leftBound is { } l && rightBound is { } r)
                        return new ValueRange(0, Int128.Min(l, r));
                    return (leftBound ?? rightBound) is { } bound ? new ValueRange(0, bound) : null;
                }

            case GimpleTreeCode.TruncModExpr when operands.Length == 2 && Operand(operands[1]) is { IsPoint: true } divisor && divisor.Min > 0:
                return Operand(operands[0]) is { } dividend && dividend.Min >= 0
                    ? new ValueRange(0, divisor.Min - 1)
                    : new ValueRange(1 - divisor.Min, divisor.Min - 1);

            case GimpleTreeCode.RshiftExpr when operands.Length == 2 && Operand(operands[1]) is { IsPoint: true } shift && shift.Min >= 0 && shift.Min < 64 &&
                                                Operand(operands[0]) is { } shifted && shifted.Min >= 0:
                return new ValueRange(shifted.Min >> (int)shift.Min, shifted.Max >> (int)shift.Min);

            case GimpleTreeCode.PlusExpr:
            case GimpleTreeCode.MinusExpr:
            case GimpleTreeCode.MultExpr:
                {
                    if (operands.Length != 2 || Operand(operands[0]) is not { } a || Operand(operands[1]) is not { } b)
                        return null;
                    var result = assign.Subcode switch
                    {
                        GimpleTreeCode.PlusExpr => new ValueRange(a.Min + b.Min, a.Max + b.Max),
                        GimpleTreeCode.MinusExpr => new ValueRange(a.Min - b.Max, a.Max - b.Min),
                        _ => Multiply(a, b),
                    };
                    // An unsigned result that leaves its type wrapped around; a signed one would have been undefined
                    return typeRange.Contains(result) || GimpleTypes.IsSigned(name.Type) ? result : null;
                }

            case GimpleTreeCode.LtExpr:
            case GimpleTreeCode.LeExpr:
            case GimpleTreeCode.GtExpr:
            case GimpleTreeCode.GeExpr:
            case GimpleTreeCode.EqExpr:
            case GimpleTreeCode.NeExpr:
            case GimpleTreeCode.TruthNotExpr:
                return new ValueRange(0, 1);

            default:
                return null;
        }
    }

    private ValueRange? PhiRange(GimplePhi phi, ValueRange typeRange)
    {
        if (phi.Operands.Length == 2 && GimpleTypes.IsSigned(phi.Result.Type))
        {
            for (var i = 0; i < 2; i++)
            {
                if (!TryGetStep(phi.Operands[i].Value, phi.Result, out var step) || GlobalRange(phi.Operands[1 - i].Value) is not { } start)
                    continue;
                if (step > 0)
                    return new ValueRange(start.Min, typeRange.Max);
                if (step < 0)
                    return new ValueRange(typeRange.Min, start.Max);
            }
        }

        ValueRange? union = null;
        foreach (var operand in phi.Operands)
        {
            if (GlobalRange(operand.Value) is not { } range)
                return null;
            union = union is { } accumulated ? accumulated.Union(range) : range;
        }
        return union;
    }

    private bool TryGetStep(GimpleName value, GimpleName phi, out Int128 step)
    {
        step = 0;
        if (value.Definition?.Statement is not GimpleAssignStatement { Operands.Length: 2 } assign ||
            assign.Subcode is not (GimpleTreeCode.PlusExpr or GimpleTreeCode.MinusExpr) ||
            !ReferenceEquals(assign.Operands[0], phi) || assign.Operands[1] is not GimpleConstantValue constant ||
            !TryGetConstant(constant, out var amount))
            return false;
        step = assign.Subcode == GimpleTreeCode.PlusExpr ? amount : -amount;
        return true;
    }

    private ValueRange? Operand(GimpleValue value)
        => value switch
        {
            GimpleConstantValue constant when TryGetConstant(constant, out var number) => new ValueRange(number, number),
            GimpleName name => GlobalRange(name),
            _ => null,
        };

    private static ValueRange Multiply(ValueRange a, ValueRange b)
    {
        var p1 = a.Min * b.Min;
        var p2 = a.Min * b.Max;
        var p3 = a.Max * b.Min;
        var p4 = a.Max * b.Max;
        return new ValueRange(Int128.Min(Int128.Min(p1, p2), Int128.Min(p3, p4)), Int128.Max(Int128.Max(p1, p2), Int128.Max(p3, p4)));
    }

    private bool TryGetTypeRange(QualifiedType type, out ValueRange range)
    {
        range = default;
        if (type.Type.Kind == TypeKind.Pointer)
        {
            range = new ValueRange(0, (Int128.One << (_target.PointerSize * 8)) - 1);
            return true;
        }
        if (!GimpleTypes.IsIntegerLike(type))
            return false;
        if (type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
        {
            range = new ValueRange(0, 1);
            return true;
        }

        var bits = _target.SizeOf(type) * 8;
        if (bits is <= 0 or > 64)
            return false;
        var signed = type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Char }
            ? _target.CharSignedness != CharSignedness.Unsigned
            : GimpleTypes.IsSigned(type);
        range = signed
            ? new ValueRange(-(Int128.One << (bits - 1)), (Int128.One << (bits - 1)) - 1)
            : new ValueRange(0, (Int128.One << bits) - 1);
        return true;
    }
}

internal static class AssertionPropagation
{
    public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, TargetInfo target, SsaOptimizationOptions options,
        ValueNumberingOptions valueNumberingOptions, out AssertionTable? assertions)
    {
        assertions = null;
        if (!options.EnableAssertionPropagation || function.Problems.Length != 0)
            return function;
        return new Propagator(function, target, valueNumberingOptions, options.MaxLoopAnalysisWork).Run(out assertions);
    }

    private sealed class Propagator
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocks = new();
        private readonly Dictionary<GimpleName, GimpleStatementAnnotations> _definitions = new();
        private readonly Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> _replacements = new();
        private readonly Dictionary<GimpleDefinition, GimpleDefinition> _redefinitions = new();
        private readonly AssertionTable _table;
        private int _budget;

        public Propagator(GimpleFunctionAnnotations function, TargetInfo target, ValueNumberingOptions valueNumberingOptions, int budget)
        {
            _function = function;
            _valueNumberingOptions = valueNumberingOptions;
            _budget = budget;
            foreach (var block in function.Blocks)
            {
                _blocks[block.ControlFlowBlock] = block;
                foreach (var instruction in block.Statements)
                {
                    foreach (var definition in instruction.Definitions)
                        _definitions[definition.Name] = instruction;
                }
            }
            _table = new AssertionTable(target, _definitions);
        }

        public GimpleFunctionAnnotations Run(out AssertionTable assertions)
        {
            assertions = _table;
            var entry = _function.ControlFlowFunction.Entry;
            var pending = new Stack<(ControlFlowBlock Block, AssertionScope? Scope)>();
            pending.Push((entry, null));
            while (pending.Count != 0 && _budget-- > 0)
            {
                var (block, scope) = pending.Pop();
                _table.Record(block, scope);
                if (!_blocks.TryGetValue(block, out var annotations))
                    continue;

                PropagateConstants(annotations, scope);
                var test = annotations.Statements.Length != 0 ? annotations.Statements[^1] : null;
                var condition = test?.Statement as GimpleCondStatement;
                if (condition is not null && Describe(condition) is { } described &&
                    _table.Evaluate(scope, described.Left, described.Code, described.Right) is { } truth)
                {
                    var target = truth ? condition.WhenTrue : condition.WhenFalse;
                    var jump = new GimpleGotoStatement(target, condition.Syntax);
                    _replacements[test!] = new GimpleStatementAnnotations(test!.Ordinal, test.Block, jump, test.InputStatement,
                        ImmutableArray<GimpleOperandInfo>.Empty, ImmutableArray<GimpleUse>.Empty, ImmutableArray<GimpleDefinition>.Empty,
                        null, null, GimpleStatementFlags.None);
                }

                foreach (var child in block.DominatorChildren)
                    pending.Push((child, Enter(block, child, condition, scope)));
            }

            return _replacements.Count == 0 ? _function : Rebuild();
        }

        private AssertionScope? Enter(ControlFlowBlock from, ControlFlowBlock to, GimpleCondStatement? condition, AssertionScope? scope)
        {
            if (condition is null || to.Predecessors.Length != 1 || !ReferenceEquals(to.Predecessors[0].Source, from))
                return scope;
            var onTrue = Targets(condition.WhenTrue, to);
            if (onTrue == Targets(condition.WhenFalse, to))
                return scope;

            foreach (var assertion in Assertions(condition))
            {
                var code = onTrue ? assertion.Code : ScalarEvolution.Invert(assertion.Code);
                scope = new AssertionScope(new Assertion(assertion.Left, code, assertion.Right), scope);
            }
            return scope;
        }

        private IEnumerable<Assertion> Assertions(GimpleCondStatement condition)
        {
            if (!IsComparison(condition.Code) || !AssertionTable.IsTracked(condition.Lhs.Type) || !AssertionTable.IsTracked(condition.Rhs.Type))
                yield break;
            yield return new Assertion(condition.Lhs, condition.Code, condition.Rhs);
            if (Describe(condition) is { } described && !ReferenceEquals(described.Left, condition.Lhs))
                yield return described;
        }

        // A test of a flag against zero stands for the comparison that computed the flag
        private Assertion? Describe(GimpleCondStatement condition)
        {
            if (!IsComparison(condition.Code) || !AssertionTable.IsTracked(condition.Lhs.Type) || !AssertionTable.IsTracked(condition.Rhs.Type))
                return null;
            if (condition.Code is GimpleTreeCode.NeExpr or GimpleTreeCode.EqExpr &&
                condition.Lhs is GimpleName flag && condition.Rhs is GimpleConstantValue zero && AssertionTable.Same(zero, new GimpleConstantValue(0, zero.Type)) &&
                flag.Definition?.Statement is GimpleAssignStatement { Operands.Length: 2 } comparison && IsComparison(comparison.Subcode) &&
                AssertionTable.IsTracked(comparison.Operands[0].Type) && AssertionTable.IsTracked(comparison.Operands[1].Type))
            {
                var code = condition.Code == GimpleTreeCode.NeExpr ? comparison.Subcode : ScalarEvolution.Invert(comparison.Subcode);
                return new Assertion(comparison.Operands[0], code, comparison.Operands[1]);
            }
            return new Assertion(condition.Lhs, condition.Code, condition.Rhs);
        }

        private static bool IsComparison(GimpleTreeCode code)
            => code is GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr
                or GimpleTreeCode.EqExpr or GimpleTreeCode.NeExpr;

        private bool Targets(GimpleLabel label, ControlFlowBlock block)
            => _function.ControlFlowFunction.TryGetBlock(label, out var target) && ReferenceEquals(target, block);

        private void PropagateConstants(GimpleBlockAnnotations block, AssertionScope? scope)
        {
            Dictionary<GimpleName, GimpleConstantValue>? constants = null;
            for (var current = scope; current is not null; current = current.Parent)
            {
                var known = current.Assertion;
                if (known.Code != GimpleTreeCode.EqExpr)
                    continue;
                if (known.Left is GimpleName name && known.Right is GimpleConstantValue constant && GimpleTypes.IsIntegerLike(name.Type))
                    (constants ??= new()).TryAdd(name, constant);
                else if (known.Right is GimpleName swapped && known.Left is GimpleConstantValue swappedConstant && GimpleTypes.IsIntegerLike(swapped.Type))
                    (constants ??= new()).TryAdd(swapped, swappedConstant);
            }
            if (constants is null)
                return;

            foreach (var instruction in block.Statements)
            {
                if (instruction.Statement is GimpleAsmStatement or GimpleDeclarationStatement)
                    continue;
                var touches = false;
                foreach (var use in instruction.Uses)
                    touches |= use.Kind == GimpleUseKind.Value && constants.ContainsKey(use.Name);
                if (!touches)
                    continue;

                var current = _replacements.TryGetValue(instruction, out var replaced) ? replaced : instruction;
                _replacements[instruction] = SsaRewriting.Rewrite(current, node =>
                    node.Name is { } name && !node.IsAddress && constants.TryGetValue(name, out var constant)
                        ? new GimpleOperandInfo(new GimpleConstantValue(constant.Value, name.Type, constant.Syntax), null,
                            ImmutableArray<GimpleOperandInfo>.Empty, false, false, false)
                        : null, _redefinitions);
            }
        }

        private GimpleFunctionAnnotations Rebuild()
        {
            var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
            var uses = ImmutableArray.CreateBuilder<GimpleUse>();
            foreach (var block in _function.Blocks)
            {
                var statements = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                foreach (var instruction in block.Statements)
                {
                    var rewritten = _replacements.TryGetValue(instruction, out var replacement) ? replacement : instruction;
                    statements.Add(rewritten);
                    uses.AddRange(rewritten.Uses);
                }
                blocks.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, block.Phis, statements.MoveToImmutable()));
            }

            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
            foreach (var definition in _function.Definitions)
                definitions.Add(_redefinitions.TryGetValue(definition, out var redefined) ? redefined : definition);

            var undefined = new Dictionary<GimpleVariable, GimpleName>();
            foreach (var variable in _function.Variables)
                undefined.Add(variable, _function.GetUndefinedName(variable));

            return new GimpleFunctionAnnotations(_function.ControlFlowFunction, _function.MemoryVariable, _function.Variables,
                blocks.ToImmutable(), definitions.ToImmutable(), uses.ToImmutable(), _function.Problems, undefined,
                _valueNumberingOptions, _function.Target);
        }
    }
}
