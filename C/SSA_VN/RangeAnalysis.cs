using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C;

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
    public bool IsEmpty => Min > Max;

    public ValueRange Intersect(ValueRange other) => new(Int128.Max(Min, other.Min), Int128.Min(Max, other.Max));
    public ValueRange Union(ValueRange other) => new(Int128.Min(Min, other.Min), Int128.Max(Max, other.Max));
    public bool Contains(ValueRange other) => Min <= other.Min && other.Max <= Max;
    public bool SameAs(ValueRange other) => Min == other.Min && Max == other.Max;
}

/// <summary>Holds the range each SSA value keeps from its definition on, narrowed where dominating assertions apply</summary>
internal sealed class ValueRangeTable
{
    private const int MaxScopeWalk = 64;
    private const int MaxRefineDepth = 1;

    private readonly Dictionary<GimpleName, ValueRange> _ranges = new();
    private readonly HashSet<GimpleName> _unresolved = new();
    private readonly AssertionTable? _assertions;
    private readonly TargetInfo _target;

    public ValueRangeTable(TargetInfo target, AssertionTable? assertions)
    {
        _target = target;
        _assertions = assertions;
    }

    public bool Proves(ControlFlowBlock block, GimpleValue left, GimpleTreeCode code, GimpleValue right)
        => Evaluate(_assertions?.ScopeOf(block), left, code, right) == true;

    public bool? Evaluate(AssertionScope? scope, GimpleValue left, GimpleTreeCode code, GimpleValue right)
    {
        if (AssertionTable.Implication(scope, left, code, right) is bool truth)
            return truth;

        return RangeAt(left, scope) is ValueRange leftRange && RangeAt(right, scope) is ValueRange rightRange
            ? Compare(leftRange, code, rightRange)
            : null;
    }

    public ValueRange? RangeAt(GimpleValue value, AssertionScope? scope) => RangeAt(value, scope, MaxRefineDepth);

    internal bool IsUnresolved(GimpleValue value) => value is GimpleName name && _unresolved.Contains(name);

    internal void MarkUnresolved(GimpleName name) => _unresolved.Add(name);

    internal void ResolveAll() => _unresolved.Clear();

    internal bool TryGet(GimpleName name, out ValueRange range) => _ranges.TryGetValue(name, out range);

    internal void Set(GimpleName name, ValueRange range)
    {
        _ranges[name] = range;
        _unresolved.Remove(name);
    }

    internal bool TryGetTypeRange(QualifiedType type, out ValueRange range)
    {
        range = default;
        if (type.Type.Kind == TypeKind.Pointer)
        {
            range = new ValueRange(0, (Int128.One << (_target.PointerSize * 8)) - 1);
            return true;
        }
        if (!GimpleTypes.IsIntegerLike(type))
            return false;
        if (IsBool(type))
        {
            range = new ValueRange(0, 1);
            return true;
        }

        int bits = _target.SizeOf(type) * 8;
        if (bits is <= 0 or > 64)
            return false;
        range = GimpleTypes.IsSigned(type, _target)
            ? new ValueRange(-(Int128.One << (bits - 1)), (Int128.One << (bits - 1)) - 1)
            : new ValueRange(0, (Int128.One << bits) - 1);
        return true;
    }

    internal static bool IsBool(QualifiedType type) => type.Type is BuiltinType builtin && builtin.BuiltinKind == BuiltinTypeKind.Bool;

    private static bool? Compare(ValueRange left, GimpleTreeCode code, ValueRange right)
    {
        if (left.IsEmpty || right.IsEmpty)
            return null;
        return code switch
        {
            GimpleTreeCode.LtExpr => left.Max < right.Min ? true : left.Min >= right.Max ? false : null,
            GimpleTreeCode.LeExpr => left.Max <= right.Min ? true : left.Min > right.Max ? false : null,
            GimpleTreeCode.GtExpr => left.Min > right.Max ? true : left.Max <= right.Min ? false : null,
            GimpleTreeCode.GeExpr => left.Min >= right.Max ? true : left.Max < right.Min ? false : null,
            GimpleTreeCode.EqExpr => left.IsPoint && right.IsPoint && left.Min == right.Min ? true : left.Max < right.Min || right.Max < left.Min ? false : null,
            GimpleTreeCode.NeExpr => left.IsPoint && right.IsPoint && left.Min == right.Min ? false : left.Max < right.Min || right.Max < left.Min ? true : null,
            _ => null,
        };
    }

    private ValueRange? Defined(GimpleName name)
    {
        if (_ranges.TryGetValue(name, out ValueRange range))
            return range;
        return !_unresolved.Contains(name) && TryGetTypeRange(name.Type, out range) ? range : null;
    }

    private ValueRange? RangeAt(GimpleValue value, AssertionScope? scope, int depth)
    {
        if (value is GimpleConstantValue constant)
            return TryGetTypedConstant(constant, out Int128 number) ? new ValueRange(number, number) : null;
        if (value is not GimpleName name || Defined(name) is not ValueRange range)
            return null;

        var steps = 0;
        for (AssertionScope? current = scope; current is not null && steps++ < MaxScopeWalk; current = current.Parent)
        {
            Assertion known = current.Assertion;
            if (ReferenceEquals(known.Left, value))
                range = Refine(range, known.Code, known.Right, scope, depth);
            else if (ReferenceEquals(known.Right, value))
                range = Refine(range, AssertionTable.Swap(known.Code), known.Left, scope, depth);
        }

        if (depth > 0 && GimpleTypes.HasUndefinedOverflow(name.Type, _target) && name.Definition?.Statement is GimpleAssignStatement assign &&
            assign.Subcode is GimpleTreeCode.PlusExpr or GimpleTreeCode.MinusExpr && assign.Operands.Length == 2 &&
            assign.Operands[0] is GimpleName source && assign.Operands[1] is GimpleConstantValue offset &&
            TryGetTypedConstant(offset, out Int128 amount) && RangeAt(source, scope, depth - 1) is ValueRange sourceRange)
        {
            Int128 shift = assign.Subcode == GimpleTreeCode.PlusExpr ? amount : -amount;
            range = range.Intersect(new ValueRange(sourceRange.Min + shift, sourceRange.Max + shift));
        }
        return range;
    }

    private ValueRange Refine(ValueRange range, GimpleTreeCode code, GimpleValue other, AssertionScope? scope, int depth)
    {
        if (other is not GimpleConstantValue && depth == 0 || RangeAt(other, scope, depth - 1) is not ValueRange bound)
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

    // A constant is its bits read in its own type, whichever integer happens to box them
    private bool TryGetTypedConstant(GimpleConstantValue constant, out Int128 value)
    {
        if (!AssertionTable.TryGetConstant(constant, out value))
            return false;
        if (TryGetTypeRange(constant.Type, out ValueRange range))
        {
            Int128 span = range.Max - range.Min + 1;
            value = range.Min + ((value - range.Min) % span + span) % span;
        }
        return true;
    }
}

/// <summary>Propagates value ranges through the SSA graph and simplifies what the ranges decide</summary>
internal static class RangeAnalysis
{
    private const int MaxAscendingPasses = 12;
    private const int DescendingPasses = 2;
    private const int WideningDelay = 2;

    public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, TargetInfo target, SsaOptimizationOptions options,
        ValueNumberingOptions valueNumberingOptions, AssertionTable? assertions, out ValueRangeTable? ranges)
    {
        ranges = null;
        if (!options.EnableRangeAnalysis || function.Problems.Length != 0)
            return function;
        return new Analyzer(function, target, valueNumberingOptions, assertions).Run(out ranges);
    }

    private sealed class Analyzer
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly TargetInfo _target;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly AssertionTable? _assertions;
        private readonly ValueRangeTable _table;
        private readonly List<GimpleBlockAnnotations> _order = new();
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocks = new();
        private readonly Dictionary<ControlFlowEdge, AssertionScope?> _edgeScopes = new();
        private readonly Dictionary<ControlFlowBlock, bool> _decisions = new();
        private readonly Dictionary<GimpleName, int> _growth = new();
        private readonly Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> _replacements = new();
        private readonly Dictionary<GimpleDefinition, GimpleDefinition> _redefinitions = new();

        public Analyzer(GimpleFunctionAnnotations function, TargetInfo target, ValueNumberingOptions valueNumberingOptions, AssertionTable? assertions)
        {
            _function = function;
            _target = target;
            _valueNumberingOptions = valueNumberingOptions;
            _assertions = assertions;
            _table = new ValueRangeTable(target, assertions);
            foreach (GimpleBlockAnnotations block in function.Blocks)
                _blocks[block.ControlFlowBlock] = block;
            foreach (ControlFlowBlock block in function.ControlFlowFunction.ReversePostOrder)
            {
                if (_blocks.TryGetValue(block, out GimpleBlockAnnotations? annotations))
                    _order.Add(annotations);
            }
        }

        public GimpleFunctionAnnotations Run(out ValueRangeTable ranges)
        {
            foreach (GimpleBlockAnnotations block in _order)
            {
                foreach (GimplePhi phi in block.Phis)
                {
                    if (Tracks(phi.Result))
                        _table.MarkUnresolved(phi.Result);
                }
                foreach (GimpleStatementAnnotations instruction in block.Statements)
                {
                    if (Defines(instruction) is GimpleName name)
                        _table.MarkUnresolved(name);
                }
            }

            var passes = 0;
            while (Sweep(ascending: true))
            {
                if (++passes == MaxAscendingPasses)
                {
                    ranges = new ValueRangeTable(_target, _assertions);
                    return _function;
                }
            }
            for (var i = 0; i < DescendingPasses; i++)
                Sweep(ascending: false);
            _table.ResolveAll();

            foreach (GimpleBlockAnnotations block in _order)
                Simplify(block);
            ranges = _table;
            return _replacements.Count == 0 ? _function : Rebuild();
        }

        private bool Sweep(bool ascending)
        {
            var changed = false;
            foreach (GimpleBlockAnnotations block in _order)
            {
                foreach (GimplePhi phi in block.Phis)
                {
                    if (!Tracks(phi.Result))
                        continue;
                    changed |= Update(phi.Result, PhiRange(phi), ascending, widen: true);
                }

                AssertionScope? scope = _assertions?.ScopeOf(block.ControlFlowBlock);
                foreach (GimpleStatementAnnotations instruction in block.Statements)
                {
                    if (Defines(instruction) is not GimpleName name)
                        continue;
                    changed |= Update(name, StatementRange(instruction, (GimpleAssignStatement)instruction.Statement, name, scope), ascending, widen: false);
                }

                if (block.Statements.Length != 0 && block.Statements[^1].Statement is GimpleCondStatement condition)
                    changed |= Decide(block.ControlFlowBlock, condition, scope);
            }
            return changed;
        }

        private bool Update(GimpleName name, ValueRange? computed, bool ascending, bool widen)
        {
            if (computed is not ValueRange range || range.IsEmpty)
                return false;
            if (!_table.TryGet(name, out ValueRange old))
            {
                _table.Set(name, range);
                return true;
            }

            ValueRange next = ascending ? old.Union(range) : old.Intersect(range);
            if (next.IsEmpty || next.SameAs(old))
                return false;
            if (ascending && widen)
            {
                int growth = _growth.TryGetValue(name, out int previous) ? previous + 1 : 1;
                _growth[name] = growth;
                // A value still growing after a few rounds jumps to its type's bound, which narrowing then pulls back
                if (growth > WideningDelay && _table.TryGetTypeRange(name.Type, out ValueRange type))
                    next = new ValueRange(next.Min < old.Min ? type.Min : old.Min, next.Max > old.Max ? type.Max : old.Max);
            }
            _table.Set(name, next);
            return true;
        }

        private bool Decide(ControlFlowBlock block, GimpleCondStatement condition, AssertionScope? scope)
        {
            bool? truth = SplitsTargets(condition) ? Evaluate(condition, scope) : null;
            bool known = _decisions.TryGetValue(block, out bool previous);
            if (truth is not bool taken)
                return known && _decisions.Remove(block);
            if (known && previous == taken)
                return false;
            _decisions[block] = taken;
            return true;
        }

        private bool? Evaluate(GimpleCondStatement condition, AssertionScope? scope)
        {
            if (AssertionTable.Describe(condition) is Assertion described &&
                _table.Evaluate(scope, described.Left, described.Code, described.Right) is bool truth)
            {
                return truth;
            }
            return _table.Evaluate(scope, condition.Lhs, condition.Code, condition.Rhs);
        }

        private ValueRange? PhiRange(GimplePhi phi)
        {
            ValueRange? union = null;
            foreach (GimplePhiOperand operand in phi.Operands)
            {
                if (!IsLive(operand.Edge) || _table.RangeAt(operand.Value, EdgeScope(operand.Edge)) is not ValueRange range || range.IsEmpty)
                    continue;
                union = union is ValueRange accumulated ? accumulated.Union(range) : range;
            }
            return union;
        }

        private bool IsLive(ControlFlowEdge edge)
        {
            if (!edge.Source.IsReachable)
                return false;
            if (edge.Kind is not (ControlFlowEdgeKind.ConditionalTrue or ControlFlowEdgeKind.ConditionalFalse))
                return true;

            GimpleStatement? terminator = Terminator(edge.Source);
            if (terminator is GimpleGotoStatement jump)
                return Targets(jump.Target, edge.Target);
            if (terminator is not GimpleCondStatement condition || !SplitsTargets(condition))
                return true;
            return !_decisions.TryGetValue(edge.Source, out bool taken) || taken == (edge.Kind == ControlFlowEdgeKind.ConditionalTrue);
        }

        private AssertionScope? EdgeScope(ControlFlowEdge edge)
        {
            if (_edgeScopes.TryGetValue(edge, out AssertionScope? cached))
                return cached;

            AssertionScope? scope = _assertions?.ScopeOf(edge.Source);
            if (edge.Kind is ControlFlowEdgeKind.ConditionalTrue or ControlFlowEdgeKind.ConditionalFalse &&
                Terminator(edge.Source) is GimpleCondStatement condition && SplitsTargets(condition))
            {
                scope = AssertionTable.Enter(scope, condition, edge.Kind == ControlFlowEdgeKind.ConditionalTrue);
            }
            _edgeScopes[edge] = scope;
            return scope;
        }

        private GimpleStatement? Terminator(ControlFlowBlock block)
            => _blocks.TryGetValue(block, out GimpleBlockAnnotations? annotations) && annotations.Statements.Length != 0
                ? annotations.Statements[^1].Statement
                : null;

        private bool SplitsTargets(GimpleCondStatement condition)
            => _function.ControlFlowFunction.TryGetBlock(condition.WhenTrue, out ControlFlowBlock? whenTrue) &&
               _function.ControlFlowFunction.TryGetBlock(condition.WhenFalse, out ControlFlowBlock? whenFalse) &&
               !ReferenceEquals(whenTrue, whenFalse);

        private bool Targets(GimpleLabel label, ControlFlowBlock block)
            => _function.ControlFlowFunction.TryGetBlock(label, out ControlFlowBlock? target) && ReferenceEquals(target, block);

        private bool Tracks(GimpleName name) => _table.TryGetTypeRange(name.Type, out _);

        private GimpleName? Defines(GimpleStatementAnnotations instruction)
            => instruction.Statement is GimpleAssignStatement assign && assign.Lhs is GimpleName name && Tracks(name) ? name : null;

        private ValueRange? StatementRange(GimpleStatementAnnotations instruction, GimpleAssignStatement assign, GimpleName name, AssertionScope? scope)
        {
            foreach (GimpleValue operand in assign.Operands)
            {
                if (_table.IsUnresolved(operand))
                    return null;
            }

            _table.TryGetTypeRange(name.Type, out ValueRange type);
            if ((instruction.Flags & GimpleStatementFlags.ReadsMemory) != 0 || Compute(assign, name, type, scope) is not ValueRange computed)
                return type;
            ValueRange fitted = computed.Intersect(type);
            return fitted.IsEmpty ? type : fitted;
        }

        private ValueRange? Compute(GimpleAssignStatement assign, GimpleName name, ValueRange type, AssertionScope? scope)
        {
            ImmutableArray<GimpleValue> operands = assign.Operands;
            ValueRange? left = operands.Length > 0 ? _table.RangeAt(operands[0], scope) : null;
            ValueRange? right = operands.Length > 1 ? _table.RangeAt(operands[1], scope) : null;
            bool wraps = !GimpleTypes.HasUndefinedOverflow(name.Type, _target);
            int bits = _target.SizeOf(name.Type) * 8;
            switch (assign.Subcode)
            {
                case GimpleTreeCode.IntegerCst:
                case GimpleTreeCode.SsaName:
                    return operands.Length == 1 ? left : null;

                case GimpleTreeCode.NopExpr:
                case GimpleTreeCode.ConvertExpr:
                    if (operands.Length != 1 || left is not ValueRange source)
                        return null;
                    return ValueRangeTable.IsBool(name.Type) ? Truth(source) : Fit(source, type, wrapsAround: true);

                case GimpleTreeCode.NegateExpr when operands.Length == 1 && left is ValueRange negated:
                    return Fit(new ValueRange(-negated.Max, -negated.Min), type, wraps);

                case GimpleTreeCode.BitNotExpr when operands.Length == 1 && left is ValueRange inverted:
                    return Fit(new ValueRange(-inverted.Max - 1, -inverted.Min - 1), type, wrapsAround: true);

                case GimpleTreeCode.BitAndExpr when operands.Length == 2:
                    return BitAnd(left, right, type);

                case GimpleTreeCode.BitIorExpr when operands.Length == 2:
                case GimpleTreeCode.BitXorExpr when operands.Length == 2:
                    {
                        if (left is not ValueRange a || right is not ValueRange b || a.Min < 0 || b.Min < 0)
                            return null;
                        Int128 low = assign.Subcode == GimpleTreeCode.BitIorExpr ? Int128.Max(a.Min, b.Min) : 0;
                        return new ValueRange(low, AllOnes(Int128.Max(a.Max, b.Max)));
                    }

                case GimpleTreeCode.TruncModExpr when operands.Length == 2:
                    return Remainder(left, right);

                case GimpleTreeCode.TruncDivExpr when operands.Length == 2:
                case GimpleTreeCode.ExactDivExpr when operands.Length == 2:
                    return Quotient(left, right);

                case GimpleTreeCode.RshiftExpr when operands.Length == 2 && left is ValueRange shifted && right is ValueRange count &&
                                                    count.Min >= 0 && count.Max < bits:
                    return new ValueRange(Int128.Min(shifted.Min >> (int)count.Min, shifted.Min >> (int)count.Max),
                        Int128.Max(shifted.Max >> (int)count.Min, shifted.Max >> (int)count.Max));

                case GimpleTreeCode.LshiftExpr when operands.Length == 2 && left is ValueRange shifted && right is ValueRange count &&
                                                    count.IsPoint && count.Min >= 0 && count.Min < bits:
                    {
                        Int128 factor = Int128.One << (int)count.Min;
                        return Multiply(shifted, new ValueRange(factor, factor)) is ValueRange product ? Fit(product, type, wraps) : null;
                    }

                case GimpleTreeCode.PlusExpr when operands.Length == 2 && left is ValueRange a && right is ValueRange b:
                    return Fit(new ValueRange(a.Min + b.Min, a.Max + b.Max), type, wraps);

                case GimpleTreeCode.MinusExpr when operands.Length == 2 && left is ValueRange a && right is ValueRange b:
                    return Fit(new ValueRange(a.Min - b.Max, a.Max - b.Min), type, wraps);

                case GimpleTreeCode.MultExpr when operands.Length == 2 && left is ValueRange a && right is ValueRange b:
                    return Multiply(a, b) is ValueRange multiplied ? Fit(multiplied, type, wraps) : null;

                case GimpleTreeCode.LtExpr:
                case GimpleTreeCode.LeExpr:
                case GimpleTreeCode.GtExpr:
                case GimpleTreeCode.GeExpr:
                case GimpleTreeCode.EqExpr:
                case GimpleTreeCode.NeExpr:
                    return operands.Length == 2 && _table.Evaluate(scope, operands[0], assign.Subcode, operands[1]) is bool truth
                        ? Point(truth ? 1 : 0)
                        : new ValueRange(0, 1);

                case GimpleTreeCode.TruthNotExpr:
                    return operands.Length == 1 && left is ValueRange operand && operand.IsPoint ? Point(operand.Min == 0 ? 1 : 0) : new ValueRange(0, 1);

                case GimpleTreeCode.TruthAndExpr:
                case GimpleTreeCode.TruthOrExpr:
                case GimpleTreeCode.TruthXorExpr:
                    return new ValueRange(0, 1);

                default:
                    return null;
            }
        }

        private static ValueRange? BitAnd(ValueRange? left, ValueRange? right, ValueRange type)
        {
            if (left is not ValueRange a || right is not ValueRange b)
                return null;
            if (b.IsPoint && a.Min >= 0)
                return Masked(a, b.Min, type);
            if (a.IsPoint && b.Min >= 0)
                return Masked(b, a.Min, type);
            if (a.Min >= 0 && b.Min >= 0)
                return new ValueRange(0, Int128.Min(a.Max, b.Max));
            if (a.Min >= 0 || b.Min >= 0)
                return new ValueRange(0, a.Min >= 0 ? a.Max : b.Max);
            return null;
        }

        private static ValueRange Masked(ValueRange value, Int128 mask, ValueRange type)
        {
            Int128 bits = MaskBits(mask, type);
            Int128 span = AllOnes(value.Max);
            Int128 cleared = ~bits & span;
            // Clearing only low bits keeps values in order
            if ((cleared & (cleared + 1)) == 0)
                return new ValueRange(value.Min & bits, value.Max & bits);
            return new ValueRange(0, Int128.Min(value.Max, bits & span));
        }

        private static bool Preserves(ValueRange value, Int128 mask, ValueRange type)
            => (~MaskBits(mask, type) & AllOnes(value.Max)) == 0;

        private static ValueRange? Remainder(ValueRange? left, ValueRange? right)
        {
            if (left is not ValueRange dividend || right is not ValueRange divisor)
                return null;
            Int128 magnitude = Int128.Max(Int128.Abs(divisor.Min), Int128.Abs(divisor.Max));
            if (magnitude == 0)
                return null;
            Int128 bound = magnitude - 1;
            return new ValueRange(dividend.Min >= 0 ? 0 : Int128.Max(dividend.Min, -bound), dividend.Max <= 0 ? 0 : Int128.Min(dividend.Max, bound));
        }

        // A division by zero is undefined, so only the nonzero part of the divisor counts
        private static ValueRange? Quotient(ValueRange? left, ValueRange? right)
        {
            if (left is not ValueRange dividend || right is not ValueRange divisor)
                return null;
            ValueRange? quotient = null;
            if (divisor.Max > 0)
                quotient = Divide(dividend, Int128.Max(divisor.Min, 1), divisor.Max);
            if (divisor.Min < 0)
            {
                ValueRange negative = Divide(dividend, divisor.Min, Int128.Min(divisor.Max, -1));
                quotient = quotient is ValueRange positive ? positive.Union(negative) : negative;
            }
            return quotient;
        }

        private static ValueRange Divide(ValueRange dividend, Int128 low, Int128 high)
        {
            Int128 a = dividend.Min / low;
            Int128 b = dividend.Min / high;
            Int128 c = dividend.Max / low;
            Int128 d = dividend.Max / high;
            return new ValueRange(Int128.Min(Int128.Min(a, b), Int128.Min(c, d)), Int128.Max(Int128.Max(a, b), Int128.Max(c, d)));
        }

        private static ValueRange? Multiply(ValueRange a, ValueRange b)
        {
            if (Magnitude(a) + Magnitude(b) > 126)
                return null;
            Int128 p1 = a.Min * b.Min;
            Int128 p2 = a.Min * b.Max;
            Int128 p3 = a.Max * b.Min;
            Int128 p4 = a.Max * b.Max;
            return new ValueRange(Int128.Min(Int128.Min(p1, p2), Int128.Min(p3, p4)), Int128.Max(Int128.Max(p1, p2), Int128.Max(p3, p4)));
        }

        private static int Magnitude(ValueRange range)
            => 128 - (int)Int128.LeadingZeroCount(Int128.Max(Int128.Abs(range.Min), Int128.Abs(range.Max)));

        private static ValueRange Fit(ValueRange value, ValueRange type, bool wrapsAround)
        {
            if (type.Contains(value))
                return value;
            if (!wrapsAround)
            {
                ValueRange clamped = value.Intersect(type);
                return clamped.IsEmpty ? type : clamped;
            }

            Int128 span = type.Max - type.Min + 1;
            if (value.Max - value.Min >= span)
                return type;
            Int128 low = type.Min + ((value.Min - type.Min) % span + span) % span;
            Int128 high = type.Min + ((value.Max - type.Min) % span + span) % span;
            return low <= high ? new ValueRange(low, high) : type;
        }

        private static ValueRange Truth(ValueRange source)
            => source.Min > 0 || source.Max < 0 ? Point(1) : source.IsPoint ? Point(0) : new ValueRange(0, 1);

        private static ValueRange Point(Int128 value) => new(value, value);

        private static Int128 AllOnes(Int128 value)
            => value <= 0 ? 0 : (Int128.One << (128 - (int)Int128.LeadingZeroCount(value))) - 1;

        private static Int128 MaskBits(Int128 value, ValueRange type)
        {
            Int128 span = type.Max - type.Min + 1;
            return (value % span + span) % span;
        }

        private void Simplify(GimpleBlockAnnotations block)
        {
            AssertionScope? scope = _assertions?.ScopeOf(block.ControlFlowBlock);
            foreach (GimpleStatementAnnotations instruction in block.Statements)
            {
                if (instruction.Statement is GimpleCondStatement condition)
                {
                    if (_decisions.TryGetValue(block.ControlFlowBlock, out bool taken))
                        Replace(instruction, new GimpleGotoStatement(taken ? condition.WhenTrue : condition.WhenFalse, condition.Syntax));
                }
                else if (Defines(instruction) is GimpleName name && (instruction.Flags & GimpleStatementFlags.ReadsMemory) == 0 &&
                         SimplifiedAssign((GimpleAssignStatement)instruction.Statement, name, scope) is GimpleAssignStatement simplified)
                {
                    Replace(instruction, simplified);
                }
            }
        }

        private GimpleAssignStatement? SimplifiedAssign(GimpleAssignStatement assign, GimpleName name, AssertionScope? scope)
        {
            if (!_table.TryGet(name, out ValueRange range))
                return null;
            if (range.IsPoint && assign.Subcode != GimpleTreeCode.IntegerCst && GimpleTypes.IsIntegerLike(name.Type))
                return GimpleAssignStatement.Single(name, Constant(range.Min, name.Type, assign.Syntax), assign.Syntax);
            if (assign.Operands.Length != 2)
                return null;

            GimpleValue left = assign.Operands[0];
            GimpleValue right = assign.Operands[1];
            switch (assign.Subcode)
            {
                case GimpleTreeCode.TruncDivExpr:
                case GimpleTreeCode.ExactDivExpr:
                case GimpleTreeCode.TruncModExpr:
                    {
                        if (_table.RangeAt(left, scope) is not ValueRange dividend || _table.RangeAt(right, scope) is not ValueRange divisor)
                            return null;
                        bool remainder = assign.Subcode == GimpleTreeCode.TruncModExpr;
                        Int128 smallest = divisor.Min > 0 ? divisor.Min : divisor.Max < 0 ? -divisor.Max : 0;
                        if (remainder && smallest > 0 && dividend.Min > -smallest && dividend.Max < smallest)
                            return GimpleAssignStatement.Single(name, left, assign.Syntax);

                        // A dividend that is never negative divides by a power of two the way an unsigned one does
                        if (!GimpleTypes.IsSigned(name.Type, _target) || dividend.Min < 0 || !divisor.IsPoint || divisor.Min < 2 ||
                            (divisor.Min & (divisor.Min - 1)) != 0)
                        {
                            return null;
                        }
                        if (remainder)
                            return GimpleAssignStatement.Binary(name, GimpleTreeCode.BitAndExpr, left, Constant(divisor.Min - 1, name.Type, assign.Syntax), assign.Syntax);
                        var count = new GimpleConstantValue(127 - (int)Int128.LeadingZeroCount(divisor.Min), new QualifiedType(TypeCatalog.Instance.Int), assign.Syntax);
                        return GimpleAssignStatement.Binary(name, GimpleTreeCode.RshiftExpr, left, count, assign.Syntax);
                    }

                case GimpleTreeCode.BitAndExpr:
                    {
                        if (!_table.TryGetTypeRange(name.Type, out ValueRange type) ||
                            _table.RangeAt(left, scope) is not ValueRange a || _table.RangeAt(right, scope) is not ValueRange b)
                        {
                            return null;
                        }
                        if (b.IsPoint && a.Min >= 0 && Preserves(a, b.Min, type))
                            return GimpleAssignStatement.Single(name, left, assign.Syntax);
                        if (a.IsPoint && b.Min >= 0 && Preserves(b, a.Min, type))
                            return GimpleAssignStatement.Single(name, right, assign.Syntax);
                        return null;
                    }

                default:
                    return null;
            }
        }

        private GimpleConstantValue Constant(Int128 value, QualifiedType type, SyntaxNode? syntax)
        {
            object boxed;
            if (ValueRangeTable.IsBool(type))
                boxed = value == 0 ? 0 : 1;
            else if (GimpleTypes.IsSigned(type, _target))
                boxed = _target.SizeOf(type) <= 4 ? (object)(int)value : (long)value;
            else
                boxed = _target.SizeOf(type) <= 4 ? (object)(uint)value : (ulong)value;
            return new GimpleConstantValue(boxed, type, syntax);
        }

        private void Replace(GimpleStatementAnnotations instruction, GimpleStatement statement)
        {
            ImmutableArray<GimpleOperandInfo> expressions = statement is GimpleAssignStatement assign
                ? assign.Operands.Select(static operand => SsaRewriting.Build(operand, GimpleOperandRole.Value, readsMemory: false)).ToImmutableArray()
                : ImmutableArray<GimpleOperandInfo>.Empty;
            ImmutableArray<GimpleDefinition>.Builder definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(instruction.Definitions.Length);
            foreach (GimpleDefinition definition in instruction.Definitions)
            {
                var redefined = new GimpleDefinition(definition.Name, definition.Kind, definition.Block, statement,
                    definition.Target is GimpleName ? definition.Target : null, definition.Parameter);
                _redefinitions[definition] = redefined;
                definitions.Add(redefined);
            }
            _replacements[instruction] = new GimpleStatementAnnotations(instruction.Ordinal, instruction.Block, statement, instruction.InputStatement,
                expressions, SsaRewriting.CollectUses(expressions, instruction.Block, statement), definitions.MoveToImmutable(), null, null,
                GimpleStatementFlags.None);
        }

        private GimpleFunctionAnnotations Rebuild()
        {
            ImmutableArray<GimpleBlockAnnotations>.Builder blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
            ImmutableArray<GimpleUse>.Builder uses = ImmutableArray.CreateBuilder<GimpleUse>();
            foreach (GimpleBlockAnnotations block in _function.Blocks)
            {
                ImmutableArray<GimpleStatementAnnotations>.Builder statements = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                foreach (GimpleStatementAnnotations instruction in block.Statements)
                {
                    GimpleStatementAnnotations rewritten = _replacements.TryGetValue(instruction, out GimpleStatementAnnotations? replacement) ? replacement : instruction;
                    statements.Add(rewritten);
                    uses.AddRange(rewritten.Uses);
                }
                blocks.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, block.Phis, statements.MoveToImmutable()));
            }

            ImmutableArray<GimpleDefinition>.Builder definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
            foreach (GimpleDefinition definition in _function.Definitions)
                definitions.Add(_redefinitions.TryGetValue(definition, out GimpleDefinition? redefined) ? redefined : definition);

            var undefined = new Dictionary<GimpleVariable, GimpleName>();
            foreach (GimpleVariable variable in _function.Variables)
                undefined.Add(variable, _function.GetUndefinedName(variable));

            return new GimpleFunctionAnnotations(_function.ControlFlowFunction, _function.MemoryVariable, _function.Variables,
                blocks.ToImmutable(), definitions.ToImmutable(), uses.ToImmutable(), _function.Problems, undefined,
                _valueNumberingOptions, _function.Target);
        }
    }
}
