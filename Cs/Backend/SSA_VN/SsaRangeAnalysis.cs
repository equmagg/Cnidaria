using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum SsaRangeLimitKind : byte
    {
        Undefined,
        ArrayBound,
        Constant,
        Dependent,
        Unknown,
    }

    internal readonly struct SsaRangeLimit : IEquatable<SsaRangeLimit>
    {
        public readonly SsaRangeLimitKind Kind;
        public readonly ValueNumber Bound;
        public readonly int Value;

        private SsaRangeLimit(SsaRangeLimitKind kind, ValueNumber bound, int value)
        {
            Kind = kind;
            Bound = bound;
            Value = value;
        }

        public static SsaRangeLimit Undefined => new SsaRangeLimit(SsaRangeLimitKind.Undefined, default, 0);
        public static SsaRangeLimit Dependent => new SsaRangeLimit(SsaRangeLimitKind.Dependent, default, 0);
        public static SsaRangeLimit Unknown => new SsaRangeLimit(SsaRangeLimitKind.Unknown, default, 0);
        public static SsaRangeLimit Constant(int value) => new SsaRangeLimit(SsaRangeLimitKind.Constant, default, value);

        public static SsaRangeLimit ArrayBound(ValueNumber bound, int offset)
        {
            if (!bound.IsValid)
                throw new ArgumentOutOfRangeException(nameof(bound));
            return new SsaRangeLimit(SsaRangeLimitKind.ArrayBound, bound, offset);
        }

        public bool IsConcrete => Kind is SsaRangeLimitKind.Constant or SsaRangeLimitKind.ArrayBound;

        public bool Equals(SsaRangeLimit other)
            => Kind == other.Kind && Bound == other.Bound && Value == other.Value;

        public override bool Equals(object? obj)
            => obj is SsaRangeLimit other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((int)Kind, Bound.Id, Value);
    }

    internal readonly struct SsaRange : IEquatable<SsaRange>
    {
        public readonly SsaRangeLimit Lower;
        public readonly SsaRangeLimit Upper;

        public SsaRange(SsaRangeLimit lower, SsaRangeLimit upper)
        {
            Lower = lower;
            Upper = upper;
        }

        public static SsaRange Unknown => new SsaRange(SsaRangeLimit.Unknown, SsaRangeLimit.Unknown);
        public static SsaRange FullInt32 => new SsaRange(SsaRangeLimit.Constant(int.MinValue), SsaRangeLimit.Constant(int.MaxValue));
        public static SsaRange NonNegativeInt32 => new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(int.MaxValue));
        public static SsaRange Exact(int value) => new SsaRange(SsaRangeLimit.Constant(value), SsaRangeLimit.Constant(value));

        public bool IsUnknown => Lower.Kind == SsaRangeLimitKind.Unknown || Upper.Kind == SsaRangeLimitKind.Unknown;

        public bool IsValid
        {
            get
            {
                if (Lower.Kind == SsaRangeLimitKind.Constant && Upper.Kind == SsaRangeLimitKind.Constant)
                    return Lower.Value <= Upper.Value;
                if (Lower.Kind == SsaRangeLimitKind.ArrayBound && Upper.Kind == SsaRangeLimitKind.ArrayBound && Lower.Bound == Upper.Bound)
                    return Lower.Value <= Upper.Value;
                if (Lower.Kind == SsaRangeLimitKind.ArrayBound && Upper.Kind == SsaRangeLimitKind.Constant)
                    return Lower.Value <= Upper.Value;
                return true;
            }
        }

        public bool IsExactConstant(out int value)
        {
            if (Lower.Kind == SsaRangeLimitKind.Constant &&
                Upper.Kind == SsaRangeLimitKind.Constant &&
                Lower.Value == Upper.Value)
            {
                value = Lower.Value;
                return true;
            }

            value = 0;
            return false;
        }

        public bool Equals(SsaRange other)
            => Lower.Equals(other.Lower) && Upper.Equals(other.Upper);

        public override bool Equals(object? obj)
            => obj is SsaRange other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Lower, Upper);
    }

    internal static class SsaRangeOperations
    {
        public static SsaRange Merge(SsaRange left, SsaRange right, bool monotonicIncreasing)
            => new SsaRange(
                MergeLower(left.Lower, right.Lower, monotonicIncreasing),
                MergeUpper(left.Upper, right.Upper));

        public static SsaRange Add(SsaRange left, SsaRange right)
        {
            SsaRangeLimit lower = Add(left.Lower, right.Lower);
            SsaRangeLimit upper = Add(left.Upper, right.Upper);
            return new SsaRange(lower, upper);
        }

        public static SsaRange Subtract(SsaRange left, SsaRange right)
            => Add(left, Negate(right));

        public static SsaRange Negate(SsaRange range)
        {
            if (!TryGetConstants(range, out int lower, out int upper) || lower == int.MinValue || upper == int.MinValue)
                return SsaRange.Unknown;
            return new SsaRange(SsaRangeLimit.Constant(-upper), SsaRangeLimit.Constant(-lower));
        }

        public static SsaRange Multiply(SsaRange left, SsaRange right)
        {
            if (!TryGetConstants(left, out int ll, out int lu) || !TryGetConstants(right, out int rl, out int ru))
            {
                return new SsaRange(
                    left.Lower.Kind == SsaRangeLimitKind.Dependent || right.Lower.Kind == SsaRangeLimitKind.Dependent
                        ? SsaRangeLimit.Dependent
                        : SsaRangeLimit.Unknown,
                    left.Upper.Kind == SsaRangeLimitKind.Dependent || right.Upper.Kind == SsaRangeLimitKind.Dependent
                        ? SsaRangeLimit.Dependent
                        : SsaRangeLimit.Unknown);
            }

            long a = (long)ll * rl;
            long b = (long)ll * ru;
            long c = (long)lu * rl;
            long d = (long)lu * ru;
            long lower = Math.Min(Math.Min(a, b), Math.Min(c, d));
            long upper = Math.Max(Math.Max(a, b), Math.Max(c, d));
            return FitsInt32(lower) && FitsInt32(upper)
                ? new SsaRange(SsaRangeLimit.Constant((int)lower), SsaRangeLimit.Constant((int)upper))
                : SsaRange.Unknown;
        }

        public static SsaRange ShiftLeft(SsaRange value, SsaRange shift)
        {
            if (!TryGetConstants(shift, out int lower, out int upper) ||
                lower <= 0 || lower > 31 || upper <= 0 || upper > 31)
            {
                return SsaRange.Unknown;
            }

            var multiplier = new SsaRange(
                SsaRangeLimit.Constant(unchecked(1 << lower)),
                SsaRangeLimit.Constant(unchecked(1 << upper)));
            return Multiply(value, multiplier);
        }

        public static SsaRange ShiftRight(SsaRange value, SsaRange shift, bool unsigned)
        {
            if (!TryGetConstants(shift, out int shiftLower, out int shiftUpper) ||
                (uint)shiftLower > 31 || (uint)shiftUpper > 31)
            {
                return SsaRange.Unknown;
            }

            SsaRangeLimit lower = SsaRangeLimit.Unknown;
            SsaRangeLimit upper = SsaRangeLimit.Unknown;
            if (value.Lower.Kind == SsaRangeLimitKind.Constant && value.Lower.Value >= 0)
                lower = SsaRangeLimit.Constant(value.Lower.Value >> shiftUpper);
            if (value.Upper.Kind == SsaRangeLimitKind.Constant && value.Upper.Value >= 0)
                upper = SsaRangeLimit.Constant(value.Upper.Value >> shiftLower);

            if (unsigned && shiftLower >= 1 &&
                !(value.Lower.Kind == SsaRangeLimitKind.Constant && value.Lower.Value >= 0))
            {
                lower = SsaRangeLimit.Constant(0);
                upper = SsaRangeLimit.Constant((int)(uint.MaxValue >> shiftLower));
            }

            return new SsaRange(lower, upper);
        }

        public static SsaRange And(SsaRange left, SsaRange right)
        {
            bool leftExact = left.IsExactConstant(out int leftConstant);
            bool rightExact = right.IsExactConstant(out int rightConstant);
            if (leftExact && rightExact)
                return SsaRange.Exact(leftConstant & rightConstant);
            if (leftExact && leftConstant >= 0)
                return new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(leftConstant));
            if (rightExact && rightConstant >= 0)
                return new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(rightConstant));
            return SsaRange.Unknown;
        }

        public static SsaRange Or(SsaRange left, SsaRange right)
        {
            if (!TryGetConstants(left, out int ll, out int lu) || !TryGetConstants(right, out int rl, out int ru))
                return SsaRange.Unknown;
            if (ll < 0 || rl < 0)
                return SsaRange.Unknown;
            int maximum = Math.Max(lu, ru);
            int upper = maximum == 0 ? 0 : (int)((1L << (32 - System.Numerics.BitOperations.LeadingZeroCount((uint)maximum))) - 1);
            return new SsaRange(SsaRangeLimit.Constant(Math.Max(ll, rl)), SsaRangeLimit.Constant(upper));
        }

        public static SsaRange UnsignedMod(SsaRange dividend, SsaRange divisor)
        {
            if (!divisor.IsExactConstant(out int constant) || constant <= 0)
                return SsaRange.Unknown;
            return new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(constant - 1));
        }

        public static SsaRange UnsignedDivide(SsaRange dividend, SsaRange divisor)
        {
            if (!TryGetConstants(dividend, out int numeratorLower, out int numeratorUpper) ||
                !TryGetConstants(divisor, out int divisorLower, out int divisorUpper) ||
                numeratorLower < 0 || divisorLower <= 0)
            {
                return SsaRange.Unknown;
            }

            return new SsaRange(
                SsaRangeLimit.Constant(numeratorLower / divisorUpper),
                SsaRangeLimit.Constant(numeratorUpper / divisorLower));
        }

        private static SsaRangeLimit MergeLower(SsaRangeLimit left, SsaRangeLimit right, bool monotonicIncreasing)
        {
            if (left.Equals(right))
                return left;
            if (left.Kind == SsaRangeLimitKind.Undefined)
                return right;
            if (right.Kind == SsaRangeLimitKind.Undefined)
                return left;
            if (left.Kind == SsaRangeLimitKind.Unknown || right.Kind == SsaRangeLimitKind.Unknown)
                return SsaRangeLimit.Unknown;
            if (left.Kind == SsaRangeLimitKind.Dependent || right.Kind == SsaRangeLimitKind.Dependent)
                return monotonicIncreasing
                    ? (left.Kind == SsaRangeLimitKind.Dependent ? right : left)
                    : SsaRangeLimit.Dependent;
            if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.Constant)
                return SsaRangeLimit.Constant(Math.Min(left.Value, right.Value));
            if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.Constant && left.Value <= 0)
                return SsaRangeLimit.Constant(Math.Min(left.Value, right.Value));
            if (right.Kind == SsaRangeLimitKind.ArrayBound && left.Kind == SsaRangeLimitKind.Constant && right.Value <= 0)
                return SsaRangeLimit.Constant(Math.Min(right.Value, left.Value));
            if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.ArrayBound && left.Bound == right.Bound)
                return SsaRangeLimit.ArrayBound(left.Bound, Math.Min(left.Value, right.Value));
            return SsaRangeLimit.Unknown;
        }

        private static SsaRangeLimit MergeUpper(SsaRangeLimit left, SsaRangeLimit right)
        {
            if (left.Equals(right))
                return left;
            if (left.Kind == SsaRangeLimitKind.Undefined)
                return right;
            if (right.Kind == SsaRangeLimitKind.Undefined)
                return left;
            if (left.Kind == SsaRangeLimitKind.Unknown || right.Kind == SsaRangeLimitKind.Unknown)
                return SsaRangeLimit.Unknown;
            if (left.Kind == SsaRangeLimitKind.Dependent || right.Kind == SsaRangeLimitKind.Dependent)
                return SsaRangeLimit.Dependent;
            if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.Constant)
                return SsaRangeLimit.Constant(Math.Max(left.Value, right.Value));
            if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.ArrayBound && right.Value >= left.Value)
                return right;
            if (right.Kind == SsaRangeLimitKind.Constant && left.Kind == SsaRangeLimitKind.ArrayBound && left.Value >= right.Value)
                return left;
            if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.ArrayBound && left.Bound == right.Bound)
                return SsaRangeLimit.ArrayBound(left.Bound, Math.Max(left.Value, right.Value));
            return SsaRangeLimit.Unknown;
        }

        private static SsaRangeLimit Add(SsaRangeLimit left, SsaRangeLimit right)
        {
            if (left.Kind == SsaRangeLimitKind.Dependent || right.Kind == SsaRangeLimitKind.Dependent)
                return SsaRangeLimit.Dependent;
            if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.Constant)
            {
                long value = (long)left.Value + right.Value;
                return FitsInt32(value) ? SsaRangeLimit.Constant((int)value) : SsaRangeLimit.Unknown;
            }
            if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.Constant)
                return AddArrayBound(left, right.Value);
            if (right.Kind == SsaRangeLimitKind.ArrayBound && left.Kind == SsaRangeLimitKind.Constant)
                return AddArrayBound(right, left.Value);
            return SsaRangeLimit.Unknown;
        }

        private static SsaRangeLimit AddArrayBound(SsaRangeLimit bound, int constant)
        {
            long offset = (long)bound.Value + constant;
            return FitsInt32(offset) ? SsaRangeLimit.ArrayBound(bound.Bound, (int)offset) : SsaRangeLimit.Unknown;
        }

        private static bool TryGetConstants(SsaRange range, out int lower, out int upper)
        {
            if (range.IsValid &&
                range.Lower.Kind == SsaRangeLimitKind.Constant &&
                range.Upper.Kind == SsaRangeLimitKind.Constant)
            {
                lower = range.Lower.Value;
                upper = range.Upper.Value;
                return true;
            }
            lower = 0;
            upper = 0;
            return false;
        }

        private static bool FitsInt32(long value)
            => value >= int.MinValue && value <= int.MaxValue;
    }

    internal sealed class SsaRangeCheckAnalysisResult
    {
        public ImmutableArray<SsaBlock> Blocks { get; }
        public bool Changed { get; }

        public SsaRangeCheckAnalysisResult(ImmutableArray<SsaBlock> blocks, bool changed)
        {
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
            Changed = changed;
        }
    }

    internal static class SsaRangeCheckAnalyzer
    {
        public static SsaRangeCheckAnalysisResult OptimizeMethod(
            SsaMethod method,
            SsaAssertionFacts facts,
            int operationBudget = 8192,
            int maximumSearchDepth = 100)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (facts is null)
                throw new ArgumentNullException(nameof(facts));
            if (method.ValueNumbers is null || method.Blocks.IsDefaultOrEmpty)
                return new SsaRangeCheckAnalysisResult(method.Blocks, changed: false);

            return new Analyzer(method, facts, operationBudget, maximumSearchDepth).Run();
        }

        private sealed class Analyzer
        {
            private readonly SsaMethod _method;
            private readonly SsaAssertionFacts _facts;
            private readonly SsaValueNumberingResult _valueNumbers;
            private readonly ValueNumberStore _store;
            private readonly Dictionary<ValueNumber, SsaPhi> _phiByValueNumber = new();
            private readonly int _maximumSearchDepth;
            private int _remainingBudget;
            private ValueNumber _preferredBound;
            private IReadOnlyCollection<int> _activeAssertions = Array.Empty<int>();
            private bool _monotonicIncreasing;
            private readonly Dictionary<ValueNumber, SsaRange> _cache = new();
            private readonly HashSet<ValueNumber> _searchPath = new();
            private readonly HashSet<ValueNumber> _assertionSearchPath = new();

            public Analyzer(SsaMethod method, SsaAssertionFacts facts, int operationBudget, int maximumSearchDepth)
            {
                _method = method;
                _facts = facts;
                _valueNumbers = method.ValueNumbers ?? throw new InvalidOperationException("Range analysis requires value numbering.");
                _store = _valueNumbers.Store;
                _maximumSearchDepth = maximumSearchDepth > 0 ? maximumSearchDepth : 100;
                _remainingBudget = operationBudget > 0 ? operationBudget : 8192;

                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                {
                    var definition = method.ValueDefinitions[i];
                    if (!definition.IsPhi || definition.Phi is null ||
                        !_valueNumbers.TryGetSsaValue(definition.Name, out var pair))
                    {
                        continue;
                    }

                    ValueNumber value = _store.VNNormalValue(pair.Conservative);
                    if (value.IsValid && _store.TryGetEntry(value, out var entry) && entry.Function == ValueNumberFunction.PhiDef)
                        _phiByValueNumber[value] = definition.Phi;
                }
            }

            public SsaRangeCheckAnalysisResult Run()
            {
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(_method.Blocks.Length);
                bool changed = false;

                for (int blockIndex = 0; blockIndex < _method.Blocks.Length; blockIndex++)
                {
                    var block = _method.Blocks[blockIndex];
                    var active = new HashSet<int>(_facts.GetBlockIn(block.Id));
                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    var treeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(block.StatementTreeLists.Length);
                    bool blockChanged = false;

                    for (int statementIndex = 0; statementIndex < block.Statements.Length; statementIndex++)
                    {
                        var originalRoot = block.Statements[statementIndex];
                        var originalList = block.StatementTreeLists[statementIndex];
                        var rewritten = new Dictionary<SsaTree, SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                        var rewrittenList = ImmutableArray.CreateBuilder<SsaTree>(originalList.Length);

                        for (int treeIndex = 0; treeIndex < originalList.Length; treeIndex++)
                        {
                            SsaTree original = originalList[treeIndex];
                            SsaTree candidate = Rebuild(original, rewritten);

                            if ((original.Source.Flags & GenTreeFlags.BoundsCheckEliminated) == 0 &&
                                original.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement &&
                                IsBoundsCheckRedundant(original, active))
                            {
                                candidate = WithFlags(candidate, candidate.Source.Flags | GenTreeFlags.BoundsCheckEliminated | GenTreeFlags.Ordered);
                                blockChanged = true;
                            }

                            if (TryApplyDivRemProperties(original, candidate, active, out var divRemCandidate))
                            {
                                candidate = divRemCandidate;
                                blockChanged = true;
                            }

                            rewritten.Add(original, candidate);
                            rewrittenList.Add(candidate);

                            var generated = _facts.GetGeneratedAfter(original.Source);
                            for (int i = 0; i < generated.Length; i++)
                                active.Add(generated[i]);
                        }

                        statements.Add(rewritten[originalRoot]);
                        treeLists.Add(rewrittenList.ToImmutable());
                    }

                    if (blockChanged)
                    {
                        changed = true;
                        blocks.Add(new SsaBlock(
                            block.CfgBlock,
                            block.Phis,
                            statements.ToImmutable(),
                            block.MemoryPhis,
                            block.MemoryIn,
                            block.MemoryOut,
                            treeLists.ToImmutable()));
                    }
                    else
                    {
                        blocks.Add(block);
                    }
                }

                return new SsaRangeCheckAnalysisResult(blocks.ToImmutable(), changed);
            }

            private bool IsBoundsCheckRedundant(SsaTree tree, IReadOnlyCollection<int> active)
            {
                if (tree.Operands.Length < 2 ||
                    !TryGetNormalValueNumber(tree.Operands[0].Source, out ValueNumber arrayValue))
                {
                    return false;
                }

                ValueNumber indexValue;
                if (tree.Source.HasBoundsCheckIndexOverride)
                {
                    indexValue = _store.VNForInt32(tree.Source.BoundsCheckIndexOverride);
                }
                else if (!TryGetNormalValueNumber(tree.Operands[1].Source, out indexValue))
                {
                    return false;
                }

                ValueNumber lengthValue = _store.VNForFunc(
                    GenStackKind.I4,
                    null,
                    ValueNumberFunction.ArrayLength,
                    arrayValue);

                if (!TryGetProvenRange(indexValue, active, lengthValue, out SsaRange indexRange))
                    return false;

                SsaRange lengthRange = TryGetInt32Constant(lengthValue, out int knownLength)
                    ? SsaRange.Exact(knownLength)
                    : GetAssertionRange(lengthValue, active, new SsaRange(
                        SsaRangeLimit.Constant(0),
                        SsaRangeLimit.Constant(Array.MaxLength)));

                return BetweenBounds(indexRange, lengthValue, lengthRange);
            }

            private bool TryApplyDivRemProperties(
                SsaTree original,
                SsaTree candidate,
                IReadOnlyCollection<int> active,
                out SsaTree rewritten)
            {
                rewritten = candidate;
                if (original.Kind != GenTreeKind.Binary ||
                    original.Operands.Length != 2 ||
                    original.Source.StackKind != GenStackKind.I4 ||
                    original.Source.SourceOp is not (BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un) ||
                    !GenTreeArithmeticSemantics.IsIntegralArithmeticType(original.Source.Type, original.Source.StackKind) ||
                    !TryGetNormalValueNumber(original.Operands[1].Source, out ValueNumber divisorValue))
                {
                    return false;
                }

                GenTreeFlags flags = candidate.Source.Flags;
                bool changed = false;
                bool divisorNonNegative = false;
                if (TryGetProvenRange(divisorValue, active, default, out SsaRange divisorRange))
                {
                    if ((flags & GenTreeFlags.DivModNoByZero) == 0 && RangeExcludes(divisorRange, 0))
                    {
                        flags |= GenTreeFlags.DivModNoByZero | GenTreeFlags.Ordered;
                        changed = true;
                    }

                    divisorNonNegative = IsRangeNonNegative(divisorRange);
                }

                if ((flags & GenTreeFlags.DivModNoOverflow) == 0)
                {
                    bool noOverflow = original.Source.SourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un || divisorNonNegative;
                    if (!noOverflow &&
                        TryGetNormalValueNumber(original.Operands[0].Source, out ValueNumber dividendValue) &&
                        TryGetProvenRange(dividendValue, active, default, out SsaRange dividendRange))
                    {
                        noOverflow = IsRangeNonNegative(dividendRange);
                    }

                    if (noOverflow)
                    {
                        flags |= GenTreeFlags.DivModNoOverflow | GenTreeFlags.Ordered;
                        changed = true;
                    }
                }

                if (!changed)
                    return false;

                rewritten = WithFlags(candidate, flags);
                return true;
            }

            private bool TryGetProvenRange(
                ValueNumber value,
                IReadOnlyCollection<int> active,
                ValueNumber preferredBound,
                out SsaRange range)
            {
                range = SsaRange.Unknown;
                if (_remainingBudget <= 0)
                    return false;

                _preferredBound = NormalizeRangeValue(preferredBound);
                _activeAssertions = active;
                _monotonicIncreasing = false;
                _cache.Clear();
                _searchPath.Clear();
                _assertionSearchPath.Clear();

                range = GetRange(value, depth: 0);
                if (range.IsUnknown || !range.IsValid)
                    return false;

                _searchPath.Clear();
                if (MayOverflow(value, range, active, new HashSet<ValueNumber>(), depth: 0))
                    return false;

                if ((range.Lower.Kind == SsaRangeLimitKind.Dependent || range.Upper.Kind == SsaRangeLimitKind.Dependent) &&
                    IsMonotonicIncreasing(value, new HashSet<ValueNumber>(), rejectNegative: false, depth: 0))
                {
                    _monotonicIncreasing = true;
                    _cache.Clear();
                    _searchPath.Clear();
                    _assertionSearchPath.Clear();
                    range = GetRange(value, depth: 0);
                    if (range.IsUnknown || !range.IsValid)
                        return false;
                }

                return true;
            }

            private static bool IsRangeNonNegative(SsaRange range)
                => TryGetLimitMinimum(range.Lower, out int minimum) && minimum >= 0;

            private static bool RangeExcludes(SsaRange range, int value)
            {
                if (TryGetLimitMinimum(range.Lower, out int minimum) && minimum > value)
                    return true;
                return TryGetLimitMaximum(range.Upper, out int maximum) && maximum < value;
            }

            private SsaRange GetRange(ValueNumber value, int depth)
            {
                value = NormalizeRangeValue(value);
                if (!value.IsValid || depth > _maximumSearchDepth || --_remainingBudget < 0)
                    return SsaRange.Unknown;
                if (TryGetInt32Constant(value, out int constant))
                    return SsaRange.Exact(constant);
                if (value == _preferredBound)
                    return new SsaRange(SsaRangeLimit.ArrayBound(value, 0), SsaRangeLimit.ArrayBound(value, 0));
                if (_cache.TryGetValue(value, out SsaRange cached))
                    return cached;
                if (!_searchPath.Add(value))
                    return new SsaRange(SsaRangeLimit.Dependent, SsaRangeLimit.Dependent);

                SsaRange range = GetRangeWorker(value, depth);
                _searchPath.Remove(value);
                range = GetAssertionRange(value, _activeAssertions, range);
                if (!range.IsValid)
                    range = SsaRange.Unknown;

                _cache[value] = range;
                return range;
            }

            private SsaRange GetRangeWorker(ValueNumber value, int depth)
            {
                if (_store.TryGetConstant(value, out var constant))
                {
                    if (constant.Kind == ValueNumberConstantKind.Int32)
                        return SsaRange.Exact(unchecked((int)constant.A));
                    if (constant.Kind == ValueNumberConstantKind.Int64 && constant.A >= int.MinValue && constant.A <= int.MaxValue)
                        return SsaRange.Exact((int)constant.A);
                    return SsaRange.Unknown;
                }

                if (!_store.TryGetEntry(value, out var entry))
                    return SsaRange.Unknown;

                if (entry.Function == ValueNumberFunction.PhiDef && _phiByValueNumber.TryGetValue(value, out var phi))
                    return GetPhiRange(phi, depth + 1);

                if (entry.Function == ValueNumberFunction.ArrayLength)
                    return new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(Array.MaxLength));

                if (entry.Function is ValueNumberFunction.Ceq or ValueNumberFunction.Clt or ValueNumberFunction.CltUn or ValueNumberFunction.Cgt or ValueNumberFunction.CgtUn)
                    return new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(1));

                if (entry.Function == ValueNumberFunction.SsaNormalize && entry.Args.Length != 0)
                {
                    ValueNumber normalized = NormalizeRangeValue(value);
                    if (normalized != value)
                        return GetRange(normalized, depth + 1);
                    return entry.StackKind == GenStackKind.I4 ? SsaRange.FullInt32 : SsaRange.Unknown;
                }

                if (entry.Function == ValueNumberFunction.Neg && entry.Args.Length == 1)
                    return SsaRangeOperations.Negate(GetRange(entry.Args[0], depth + 1));

                if (entry.Args.Length != 2)
                    return entry.StackKind == GenStackKind.I4 ? SsaRange.FullInt32 : SsaRange.Unknown;

                SsaRange left = GetRange(entry.Args[0], depth + 1);
                SsaRange right = GetRange(entry.Args[1], depth + 1);
                return entry.Function switch
                {
                    ValueNumberFunction.Add => SsaRangeOperations.Add(left, right),
                    ValueNumberFunction.Sub => SsaRangeOperations.Subtract(left, right),
                    ValueNumberFunction.Mul => SsaRangeOperations.Multiply(left, right),
                    ValueNumberFunction.And => SsaRangeOperations.And(left, right),
                    ValueNumberFunction.Or => SsaRangeOperations.Or(left, right),
                    ValueNumberFunction.Shl => SsaRangeOperations.ShiftLeft(left, right),
                    ValueNumberFunction.Shr => SsaRangeOperations.ShiftRight(left, right, unsigned: false),
                    ValueNumberFunction.ShrUn => SsaRangeOperations.ShiftRight(left, right, unsigned: true),
                    ValueNumberFunction.RemUn => SsaRangeOperations.UnsignedMod(left, right),
                    ValueNumberFunction.DivUn => SsaRangeOperations.UnsignedDivide(left, right),
                    _ => entry.StackKind == GenStackKind.I4 ? SsaRange.FullInt32 : SsaRange.Unknown,
                };
            }

            private SsaRange GetPhiRange(SsaPhi phi, int depth)
            {
                SsaRange result = new SsaRange(SsaRangeLimit.Undefined, SsaRangeLimit.Undefined);
                bool hasRange = false;

                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    var input = phi.Inputs[i];
                    if (!_valueNumbers.TryGetSsaValue(input.Value, out var pair))
                        return SsaRange.Unknown;

                    ValueNumber inputValue = _store.VNNormalValue(pair.Conservative);
                    SsaRange inputRange = GetRange(inputValue, depth + 1);
                    var edgeAssertions = GetPhiInputAssertions(phi, input.PredecessorBlockId);
                    inputRange = GetAssertionRange(inputValue, edgeAssertions, inputRange);
                    result = hasRange ? SsaRangeOperations.Merge(result, inputRange, _monotonicIncreasing) : inputRange;
                    hasRange = true;
                }

                return hasRange ? result : SsaRange.Unknown;
            }

            private bool MayOverflow(
                ValueNumber value,
                SsaRange believedRange,
                IReadOnlyCollection<int> assertions,
                HashSet<ValueNumber> path,
                int depth)
            {
                value = NormalizeRangeValue(value);
                if (!value.IsValid || depth > _maximumSearchDepth || --_remainingBudget < 0)
                    return true;

                SsaRange assertionRange = GetAssertionRange(value, assertions, SsaRange.Unknown);
                if (IsRangeSubset(assertionRange, believedRange))
                    return false;

                if (_store.TryGetConstant(value, out _))
                    return false;
                if (!path.Add(value))
                    return false;
                if (!_store.TryGetEntry(value, out var entry))
                {
                    path.Remove(value);
                    return false;
                }

                bool overflows;
                if (entry.Function == ValueNumberFunction.PhiDef && _phiByValueNumber.TryGetValue(value, out var phi))
                {
                    overflows = false;
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        if (!_valueNumbers.TryGetSsaValue(phi.Inputs[i].Value, out var pair))
                        {
                            overflows = true;
                            break;
                        }

                        ValueNumber input = _store.VNNormalValue(pair.Conservative);
                        if (path.Contains(input))
                            continue;
                        ImmutableArray<int> edgeAssertions = GetPhiInputAssertions(phi, phi.Inputs[i].PredecessorBlockId);
                        SsaRange inputRange = GetAssertionRange(input, edgeAssertions, GetRange(input, depth + 1));
                        if (MayOverflow(input, inputRange, edgeAssertions, path, depth + 1))
                        {
                            overflows = true;
                            break;
                        }
                    }
                }
                else if (entry.Function == ValueNumberFunction.SsaNormalize && entry.Args.Length != 0)
                {
                    overflows = MayOverflow(entry.Args[0], believedRange, assertions, path, depth + 1);
                }
                else if (entry.Function is ValueNumberFunction.And or ValueNumberFunction.Shr or ValueNumberFunction.ShrUn or
                         ValueNumberFunction.RemUn or ValueNumberFunction.DivUn or ValueNumberFunction.Neg or
                         ValueNumberFunction.ArrayLength or ValueNumberFunction.Ceq or ValueNumberFunction.Clt or
                         ValueNumberFunction.CltUn or ValueNumberFunction.Cgt or ValueNumberFunction.CgtUn or
                         ValueNumberFunction.InitVal)
                {
                    overflows = false;
                }
                else if (entry.Args.Length == 2 &&
                         (entry.Function is ValueNumberFunction.Add or ValueNumberFunction.Sub or
                          ValueNumberFunction.Mul or ValueNumberFunction.Shl or ValueNumberFunction.Or))
                {
                    overflows = DoesBinaryOperationOverflow(entry, assertions, path, depth + 1);
                }
                else
                {
                    overflows = entry.Kind == ValueNumberKind.Function;
                }

                path.Remove(value);
                return overflows;
            }

            private bool DoesBinaryOperationOverflow(
                ValueNumberEntry entry,
                IReadOnlyCollection<int> assertions,
                HashSet<ValueNumber> path,
                int depth)
            {
                ValueNumber leftValue = _store.VNNormalValue(entry.Args[0]);
                ValueNumber rightValue = _store.VNNormalValue(entry.Args[1]);

                if (!path.Contains(leftValue))
                {
                    SsaRange leftBelieved = GetAssertionRange(leftValue, assertions, GetRange(leftValue, depth));
                    if (MayOverflow(leftValue, leftBelieved, assertions, path, depth))
                        return true;
                }

                if (!path.Contains(rightValue))
                {
                    SsaRange rightBelieved = GetAssertionRange(rightValue, assertions, GetRange(rightValue, depth));
                    if (MayOverflow(rightValue, rightBelieved, assertions, path, depth))
                        return true;
                }

                SsaRange left = GetAssertionRange(leftValue, assertions, GetRange(leftValue, depth));
                SsaRange right = GetAssertionRange(rightValue, assertions, GetRange(rightValue, depth));

                if (entry.Function == ValueNumberFunction.Or)
                {
                    return left.Lower.Kind != SsaRangeLimitKind.Constant ||
                           right.Lower.Kind != SsaRangeLimitKind.Constant ||
                           left.Lower.Value < 0 || right.Lower.Value < 0;
                }

                if (entry.Function == ValueNumberFunction.Shl)
                    return SsaRangeOperations.ShiftLeft(left, right).IsUnknown;

                if (entry.Function == ValueNumberFunction.Add)
                    return AddMayOverflow(left, right);

                if (entry.Function == ValueNumberFunction.Mul)
                    return SsaRangeOperations.Multiply(left, right).IsUnknown;

                if (entry.Function == ValueNumberFunction.Sub)
                    return SubtractMayOverflow(left, right);

                return true;
            }

            private static bool AddMayOverflow(SsaRange left, SsaRange right)
            {
                bool hasLeftUpper = TryGetLimitMaximum(left.Upper, out int leftUpper);
                bool hasRightUpper = TryGetLimitMaximum(right.Upper, out int rightUpper);
                bool upperSafe = (hasLeftUpper && leftUpper <= 0) || (hasRightUpper && rightUpper <= 0);
                if (!upperSafe && hasLeftUpper && hasRightUpper)
                    upperSafe = (long)leftUpper + rightUpper <= int.MaxValue;

                bool hasLeftLower = TryGetLimitMinimum(left.Lower, out int leftLower);
                bool hasRightLower = TryGetLimitMinimum(right.Lower, out int rightLower);
                bool lowerSafe = (hasLeftLower && leftLower >= 0) || (hasRightLower && rightLower >= 0);
                if (!lowerSafe && hasLeftLower && hasRightLower)
                    lowerSafe = (long)leftLower + rightLower >= int.MinValue;

                return !upperSafe || !lowerSafe;
            }

            private static bool SubtractMayOverflow(SsaRange left, SsaRange right)
            {
                bool hasLeftUpper = TryGetLimitMaximum(left.Upper, out int leftUpper);
                bool hasRightLower = TryGetLimitMinimum(right.Lower, out int rightLower);
                bool upperSafe = (hasLeftUpper && leftUpper < 0) || (hasRightLower && rightLower >= 0);
                if (!upperSafe && hasLeftUpper && hasRightLower)
                    upperSafe = (long)leftUpper - rightLower <= int.MaxValue;

                bool hasLeftLower = TryGetLimitMinimum(left.Lower, out int leftLower);
                bool hasRightUpper = TryGetLimitMaximum(right.Upper, out int rightUpper);
                bool lowerSafe = (hasLeftLower && leftLower >= 0) || (hasRightUpper && rightUpper <= 0);
                if (!lowerSafe && hasLeftLower && hasRightUpper)
                    lowerSafe = (long)leftLower - rightUpper >= int.MinValue;

                return !upperSafe || !lowerSafe;
            }

            private static bool IsRangeSubset(SsaRange candidate, SsaRange container)
            {
                if (candidate.IsUnknown || !candidate.IsValid || container.IsUnknown || !container.IsValid)
                    return false;
                return IsLowerAtLeast(candidate.Lower, container.Lower) &&
                       IsUpperAtMost(candidate.Upper, container.Upper);
            }

            private static bool IsLowerAtLeast(SsaRangeLimit candidate, SsaRangeLimit container)
            {
                if (candidate.Equals(container))
                    return true;
                if (candidate.Kind == SsaRangeLimitKind.Constant && container.Kind == SsaRangeLimitKind.Constant)
                    return candidate.Value >= container.Value;
                if (candidate.Kind == SsaRangeLimitKind.ArrayBound && container.Kind == SsaRangeLimitKind.ArrayBound && candidate.Bound == container.Bound)
                    return candidate.Value >= container.Value;
                if (TryGetLimitMinimum(candidate, out int candidateMinimum) &&
                    TryGetLimitMaximum(container, out int containerMaximum))
                {
                    return candidateMinimum >= containerMaximum;
                }
                return false;
            }

            private static bool IsUpperAtMost(SsaRangeLimit candidate, SsaRangeLimit container)
            {
                if (candidate.Equals(container))
                    return true;
                if (candidate.Kind == SsaRangeLimitKind.Constant && container.Kind == SsaRangeLimitKind.Constant)
                    return candidate.Value <= container.Value;
                if (candidate.Kind == SsaRangeLimitKind.ArrayBound && container.Kind == SsaRangeLimitKind.ArrayBound && candidate.Bound == container.Bound)
                    return candidate.Value <= container.Value;
                if (TryGetLimitMaximum(candidate, out int candidateMaximum) &&
                    TryGetLimitMinimum(container, out int containerMinimum))
                {
                    return candidateMaximum <= containerMinimum;
                }
                return false;
            }

            private static bool TryGetLimitMinimum(SsaRangeLimit limit, out int minimum)
            {
                if (limit.Kind == SsaRangeLimitKind.Constant)
                {
                    minimum = limit.Value;
                    return true;
                }
                if (limit.Kind == SsaRangeLimitKind.ArrayBound)
                {
                    minimum = limit.Value;
                    return true;
                }
                minimum = 0;
                return false;
            }

            private static bool TryGetLimitMaximum(SsaRangeLimit limit, out int maximum)
            {
                if (limit.Kind == SsaRangeLimitKind.Constant)
                {
                    maximum = limit.Value;
                    return true;
                }
                if (limit.Kind == SsaRangeLimitKind.ArrayBound)
                {
                    long value = (long)Array.MaxLength + limit.Value;
                    if (value >= int.MinValue && value <= int.MaxValue)
                    {
                        maximum = (int)value;
                        return true;
                    }
                }
                maximum = 0;
                return false;
            }

            private bool IsMonotonicIncreasing(ValueNumber value, HashSet<ValueNumber> path, bool rejectNegative, int depth)
            {
                value = NormalizeRangeValue(value);
                if (depth > _maximumSearchDepth || --_remainingBudget < 0)
                    return false;
                if (_store.TryGetConstant(value, out var constant))
                {
                    if (constant.Kind == ValueNumberConstantKind.Int32)
                        return !rejectNegative || unchecked((int)constant.A) >= 0;
                    return false;
                }
                if (!path.Add(value))
                    return true;
                if (!_store.TryGetEntry(value, out var entry))
                {
                    path.Remove(value);
                    return false;
                }

                bool result;
                if (entry.Function == ValueNumberFunction.PhiDef && _phiByValueNumber.TryGetValue(value, out var phi))
                {
                    result = true;
                    for (int i = 0; i < phi.Inputs.Length && result; i++)
                    {
                        if (!_valueNumbers.TryGetSsaValue(phi.Inputs[i].Value, out var pair))
                        {
                            result = false;
                            break;
                        }
                        ValueNumber input = _store.VNNormalValue(pair.Conservative);
                        if (path.Contains(input))
                            continue;
                        result = IsMonotonicIncreasing(input, path, rejectNegative, depth + 1);
                    }
                }
                else if (entry.Function == ValueNumberFunction.SsaNormalize && entry.Args.Length != 0)
                {
                    result = false;
                }
                else if (entry.Function == ValueNumberFunction.Add && entry.Args.Length == 2)
                {
                    if (TryGetInt32Constant(entry.Args[0], out int leftConstant))
                    {
                        result = leftConstant >= 0 &&
                                 IsMonotonicIncreasing(entry.Args[1], path, rejectNegative, depth + 1);
                    }
                    else if (TryGetInt32Constant(entry.Args[1], out int rightConstant))
                    {
                        result = rightConstant >= 0 &&
                                 IsMonotonicIncreasing(entry.Args[0], path, rejectNegative, depth + 1);
                    }
                    else
                    {
                        result = IsMonotonicIncreasing(entry.Args[0], path, rejectNegative, depth + 1) &&
                                 IsMonotonicIncreasing(entry.Args[1], path, rejectNegative: true, depth: depth + 1);
                    }
                }
                else
                {
                    result = false;
                }

                path.Remove(value);
                return result;
            }

            private ValueNumber NormalizeRangeValue(ValueNumber value)
            {
                if (!value.IsValid)
                    return value;

                value = _store.VNNormalValue(value);
                for (int depth = 0; depth < 32; depth++)
                {
                    if (!_store.TryGetEntry(value, out var entry) ||
                        entry.Function != ValueNumberFunction.SsaNormalize ||
                        entry.Args.Length == 0 ||
                        entry.StackKind != GenStackKind.I4)
                    {
                        break;
                    }

                    ValueNumber unwrapped = _store.VNNormalValue(entry.Args[0]);
                    if (!unwrapped.IsValid ||
                        unwrapped == value ||
                        !_store.TryGetEntry(unwrapped, out var unwrappedEntry) ||
                        unwrappedEntry.StackKind != GenStackKind.I4)
                    {
                        break;
                    }
                    value = unwrapped;
                }
                return value;
            }

            private bool TryGetInt32Constant(ValueNumber value, out int constant)
            {
                value = NormalizeRangeValue(value);
                if (_store.TryGetConstant(value, out var key) && key.Kind == ValueNumberConstantKind.Int32)
                {
                    constant = unchecked((int)key.A);
                    return true;
                }
                constant = 0;
                return false;
            }

            private SsaRange GetAssertionRange(
                ValueNumber value,
                IReadOnlyCollection<int> assertionIndices,
                SsaRange initial)
            {
                value = NormalizeRangeValue(value);
                if (!value.IsValid || !_assertionSearchPath.Add(value))
                    return initial;

                try
                {
                    var aliases = CollectAliases(value, assertionIndices);
                    SsaRange range = initial;
                    foreach (int index in assertionIndices)
                    {
                        SsaAssertionDescriptor assertion = _facts.GetAssertion(index);
                        for (int i = 0; i < aliases.Count; i++)
                            ApplyAssertion(aliases[i], assertion, assertionIndices, ref range);
                    }
                    return range;
                }
                finally
                {
                    _assertionSearchPath.Remove(value);
                }
            }

            private List<ValueNumber> CollectAliases(ValueNumber value, IReadOnlyCollection<int> assertionIndices)
            {
                value = NormalizeRangeValue(value);
                var aliases = new List<ValueNumber> { value };
                var seen = new HashSet<ValueNumber> { value };
                bool changed;
                do
                {
                    changed = false;
                    foreach (int index in assertionIndices)
                    {
                        SsaAssertionDescriptor assertion = _facts.GetAssertion(index);
                        if (assertion.Kind != SsaAssertionKind.Equal ||
                            assertion.Operand1Kind != SsaAssertionOperand1Kind.ValueNumber ||
                            assertion.Operand2Kind != SsaAssertionOperand2Kind.ValueNumberPlusConstant ||
                            assertion.Operand2Constant != 0)
                        {
                            continue;
                        }

                        ValueNumber left = NormalizeRangeValue(assertion.Operand1Value);
                        ValueNumber right = NormalizeRangeValue(assertion.Operand2Value);
                        if (seen.Contains(left) && seen.Add(right))
                        {
                            aliases.Add(right);
                            changed = true;
                        }
                        if (seen.Contains(right) && seen.Add(left))
                        {
                            aliases.Add(left);
                            changed = true;
                        }
                    }
                }
                while (changed);
                return aliases;
            }

            private void ApplyAssertion(
                ValueNumber target,
                SsaAssertionDescriptor assertion,
                IReadOnlyCollection<int> assertionIndices,
                ref SsaRange range)
            {
                if (assertion.Operand1Kind != SsaAssertionOperand1Kind.ValueNumber)
                    return;

                target = NormalizeRangeValue(target);
                if (!_store.TryGetEntry(target, out var targetEntry) || targetEntry.StackKind != GenStackKind.I4)
                    return;

                ValueNumber operand1 = NormalizeRangeValue(assertion.Operand1Value);
                if (assertion.Operand2Kind == SsaAssertionOperand2Kind.Subrange && operand1 == target)
                {
                    range = Intersect(range, new SsaRange(
                        SsaRangeLimit.Constant(assertion.RangeLower),
                        SsaRangeLimit.Constant(assertion.RangeUpper)));
                    return;
                }

                if (assertion.Operand2Kind is SsaAssertionOperand2Kind.ConstantInt32 or SsaAssertionOperand2Kind.ConstantInt64)
                {
                    if (operand1 != target || assertion.Operand2Constant < int.MinValue || assertion.Operand2Constant > int.MaxValue)
                        return;
                    ApplyConstantAssertion(assertion.Kind, (int)assertion.Operand2Constant, ref range);
                    return;
                }

                if (assertion.Operand2Kind != SsaAssertionOperand2Kind.ValueNumberPlusConstant)
                    return;

                ValueNumber operand2 = NormalizeRangeValue(assertion.Operand2Value);
                bool operand1Matches = operand1 == target;
                bool operand2Matches = operand2 == target;
                if (operand1Matches == operand2Matches)
                    return;

                SsaAssertionKind kind;
                ValueNumber other;
                long offset;
                if (operand1Matches)
                {
                    kind = assertion.Kind;
                    other = operand2;
                    offset = assertion.Operand2Constant;
                }
                else
                {
                    if (assertion.Operand2Constant != 0)
                        return;
                    kind = SsaAssertionDescriptor.ReverseKind(assertion.Kind);
                    other = operand1;
                    offset = 0;
                }

                if (!_store.TryGetEntry(other, out var otherEntry) || otherEntry.StackKind != GenStackKind.I4)
                    return;

                SsaRange boundRange;
                if (_preferredBound.IsValid && other == _preferredBound)
                {
                    boundRange = new SsaRange(
                        SsaRangeLimit.ArrayBound(other, 0),
                        SsaRangeLimit.ArrayBound(other, 0));
                }
                else if (TryGetInt32Constant(other, out int constant))
                {
                    boundRange = SsaRange.Exact(constant);
                }
                else
                {
                    if (_assertionSearchPath.Count > _maximumSearchDepth || --_remainingBudget < 0)
                        return;
                    boundRange = GetRangeFromAssertions(other, assertionIndices);
                    if (boundRange.IsUnknown || !boundRange.IsValid)
                        return;
                }

                if (!TryOffsetRange(boundRange, offset, out SsaRange offsetBoundRange))
                    return;

                ApplyRelationalRange(kind, offsetBoundRange, ref range);
            }

            private SsaRange GetRangeFromAssertions(ValueNumber value, IReadOnlyCollection<int> assertionIndices)
            {
                value = NormalizeRangeValue(value);
                if (!value.IsValid || !_store.TryGetEntry(value, out var entry) || entry.StackKind != GenStackKind.I4)
                    return SsaRange.Unknown;

                SsaRange range;
                if (TryGetInt32Constant(value, out int constant))
                {
                    range = SsaRange.Exact(constant);
                }
                else if (entry.Function == ValueNumberFunction.ArrayLength)
                {
                    range = new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(Array.MaxLength));
                }
                else if (entry.Function is ValueNumberFunction.Ceq or ValueNumberFunction.Clt or ValueNumberFunction.CltUn or ValueNumberFunction.Cgt or ValueNumberFunction.CgtUn)
                {
                    range = new SsaRange(SsaRangeLimit.Constant(0), SsaRangeLimit.Constant(1));
                }
                else
                {
                    range = SsaRange.FullInt32;
                }

                return GetAssertionRange(value, assertionIndices, range);
            }

            private static void ApplyConstantAssertion(SsaAssertionKind kind, int constant, ref SsaRange range)
            {
                if (constant < 0 &&
                    (kind is SsaAssertionKind.LessThanUnsigned or SsaAssertionKind.LessThanOrEqualUnsigned or
                     SsaAssertionKind.GreaterThanUnsigned or SsaAssertionKind.GreaterThanOrEqualUnsigned))
                {
                    return;
                }
                ApplyRelationalRange(kind, SsaRange.Exact(constant), ref range);
            }

            private static void ApplyRelationalRange(SsaAssertionKind kind, SsaRange bound, ref SsaRange range)
            {
                switch (kind)
                {
                    case SsaAssertionKind.Equal:
                        range = Intersect(range, bound);
                        break;
                    case SsaAssertionKind.NotEqual:
                        if (bound.IsExactConstant(out int excluded))
                            ExcludeConstant(excluded, ref range);
                        break;
                    case SsaAssertionKind.LessThan:
                        if (TryOffset(bound.Upper, -1, out var signedUpper))
                            range = Intersect(range, new SsaRange(SsaRangeLimit.Undefined, signedUpper));
                        break;
                    case SsaAssertionKind.LessThanOrEqual:
                        range = Intersect(range, new SsaRange(SsaRangeLimit.Undefined, bound.Upper));
                        break;
                    case SsaAssertionKind.GreaterThan:
                        if (TryOffset(bound.Lower, 1, out var signedLower))
                            range = Intersect(range, new SsaRange(signedLower, SsaRangeLimit.Undefined));
                        break;
                    case SsaAssertionKind.GreaterThanOrEqual:
                        range = Intersect(range, new SsaRange(bound.Lower, SsaRangeLimit.Undefined));
                        break;
                    case SsaAssertionKind.LessThanUnsigned:
                        if (TryGetLimitMinimum(bound.Lower, out int unsignedMinimum) && unsignedMinimum >= 0 &&
                            TryOffset(bound.Upper, -1, out var unsignedUpper))
                        {
                            range = Intersect(range, new SsaRange(SsaRangeLimit.Constant(0), unsignedUpper));
                        }
                        break;
                    case SsaAssertionKind.LessThanOrEqualUnsigned:
                        if (TryGetLimitMinimum(bound.Lower, out int unsignedOrEqualMinimum) && unsignedOrEqualMinimum >= 0)
                            range = Intersect(range, new SsaRange(SsaRangeLimit.Constant(0), bound.Upper));
                        break;
                    case SsaAssertionKind.GreaterThanUnsigned:
                    case SsaAssertionKind.GreaterThanOrEqualUnsigned:
                        break;
                }
            }

            private static void ExcludeConstant(int value, ref SsaRange range)
            {
                if (range.Lower.Kind == SsaRangeLimitKind.Constant && range.Lower.Value == value && value != int.MaxValue)
                {
                    range = new SsaRange(SsaRangeLimit.Constant(value + 1), range.Upper);
                }
                else if (range.Upper.Kind == SsaRangeLimitKind.Constant && range.Upper.Value == value && value != int.MinValue)
                {
                    range = new SsaRange(range.Lower, SsaRangeLimit.Constant(value - 1));
                }
            }

            private static bool TryOffsetRange(SsaRange range, long offset, out SsaRange result)
            {
                if (offset < int.MinValue || offset > int.MaxValue ||
                    !TryOffsetRangeLimit(range.Lower, (int)offset, out SsaRangeLimit lower) ||
                    !TryOffsetRangeLimit(range.Upper, (int)offset, out SsaRangeLimit upper))
                {
                    result = SsaRange.Unknown;
                    return false;
                }

                result = new SsaRange(lower, upper);
                return result.IsValid;
            }

            private static bool TryOffsetRangeLimit(SsaRangeLimit limit, int delta, out SsaRangeLimit result)
            {
                if (limit.Kind == SsaRangeLimitKind.Dependent)
                {
                    result = SsaRangeLimit.Dependent;
                    return true;
                }
                if (limit.Kind == SsaRangeLimitKind.Undefined)
                {
                    result = SsaRangeLimit.Undefined;
                    return true;
                }
                return TryOffset(limit, delta, out result);
            }

            private static SsaRange Intersect(SsaRange left, SsaRange right)
            {
                SsaRangeLimit lower = IntersectLower(left.Lower, right.Lower);
                SsaRangeLimit upper = IntersectUpper(left.Upper, right.Upper);
                return new SsaRange(lower, upper);
            }

            private static SsaRangeLimit IntersectLower(SsaRangeLimit left, SsaRangeLimit right)
            {
                if (right.Kind == SsaRangeLimitKind.Undefined)
                    return left;
                if (left.Kind is SsaRangeLimitKind.Undefined or SsaRangeLimitKind.Unknown or SsaRangeLimitKind.Dependent)
                    return right;
                if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.Constant)
                    return SsaRangeLimit.Constant(Math.Max(left.Value, right.Value));
                if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.ArrayBound && left.Bound == right.Bound)
                    return SsaRangeLimit.ArrayBound(left.Bound, Math.Max(left.Value, right.Value));
                return left;
            }

            private static SsaRangeLimit IntersectUpper(SsaRangeLimit left, SsaRangeLimit right)
            {
                if (right.Kind == SsaRangeLimitKind.Undefined)
                    return left;
                if (left.Kind is SsaRangeLimitKind.Undefined or SsaRangeLimitKind.Unknown or SsaRangeLimitKind.Dependent)
                    return right;
                if (left.Kind == SsaRangeLimitKind.Constant && right.Kind == SsaRangeLimitKind.Constant)
                    return SsaRangeLimit.Constant(Math.Min(left.Value, right.Value));
                if (left.Kind == SsaRangeLimitKind.ArrayBound && right.Kind == SsaRangeLimitKind.ArrayBound && left.Bound == right.Bound)
                    return SsaRangeLimit.ArrayBound(left.Bound, Math.Min(left.Value, right.Value));
                if (left.Kind == SsaRangeLimitKind.Constant && left.Value == int.MaxValue &&
                    right.Kind == SsaRangeLimitKind.ArrayBound && right.Value <= 0)
                {
                    return right;
                }
                return left;
            }

            private static bool TryOffset(SsaRangeLimit value, int delta, out SsaRangeLimit result)
            {
                if (value.Kind == SsaRangeLimitKind.Constant)
                {
                    long sum = (long)value.Value + delta;
                    if (sum >= int.MinValue && sum <= int.MaxValue)
                    {
                        result = SsaRangeLimit.Constant((int)sum);
                        return true;
                    }
                }
                else if (value.Kind == SsaRangeLimitKind.ArrayBound)
                {
                    long sum = (long)value.Value + delta;
                    if (sum >= int.MinValue && sum <= int.MaxValue)
                    {
                        result = SsaRangeLimit.ArrayBound(value.Bound, (int)sum);
                        return true;
                    }
                }

                result = SsaRangeLimit.Unknown;
                return false;
            }

            private ImmutableArray<int> GetPhiInputAssertions(SsaPhi phi, int predecessorBlockId)
            {
                HashSet<int>? intersection = null;
                if ((uint)phi.BlockId >= (uint)_method.Blocks.Length)
                    return ImmutableArray<int>.Empty;

                var predecessors = _method.Blocks[phi.BlockId].CfgBlock.Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    CfgEdge edge = predecessors[i];
                    if (edge.Kind == CfgEdgeKind.Exception || edge.FromBlockId != predecessorBlockId)
                        continue;
                    var assertions = _facts.GetEdgeOut(edge);
                    if (intersection is null)
                        intersection = new HashSet<int>(assertions);
                    else
                        intersection.IntersectWith(assertions);
                }

                return intersection is null ? ImmutableArray<int>.Empty : ImmutableArray.CreateRange(intersection);
            }

            private static bool BetweenBounds(SsaRange indexRange, ValueNumber lengthValue, SsaRange lengthRange)
            {
                if (indexRange.Lower.Kind != SsaRangeLimitKind.Constant || indexRange.Lower.Value < 0)
                    return false;

                if (indexRange.Upper.Kind == SsaRangeLimitKind.ArrayBound &&
                    indexRange.Upper.Bound == lengthValue &&
                    indexRange.Upper.Value < 0)
                {
                    return true;
                }

                if (lengthRange.Lower.Kind == SsaRangeLimitKind.Constant &&
                    indexRange.Upper.Kind == SsaRangeLimitKind.Constant)
                {
                    return indexRange.Upper.Value < lengthRange.Lower.Value;
                }

                return false;
            }

            private bool TryGetNormalValueNumber(GenTree tree, out ValueNumber value)
            {
                if (_valueNumbers.TryGetTreeValue(tree, out var pair))
                {
                    value = _store.VNNormalValue(pair.Conservative.IsValid ? pair.Conservative : pair.Liberal);
                    return value.IsValid;
                }

                value = default;
                return false;
            }

            private static SsaTree Rebuild(SsaTree tree, Dictionary<SsaTree, SsaTree> rewritten)
            {
                if (tree.Operands.IsDefaultOrEmpty)
                    return tree;

                bool changed = false;
                var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    SsaTree operand = rewritten.TryGetValue(tree.Operands[i], out var replacement) ? replacement : tree.Operands[i];
                    changed |= !ReferenceEquals(operand, tree.Operands[i]);
                    operands.Add(operand);
                }

                if (!changed)
                    return tree;

                ImmutableArray<SsaTree> rewrittenOperands = operands.ToImmutable();
                GenTree source = CloneSource(tree.Source, rewrittenOperands, tree.Source.Flags);
                return new SsaTree(
                    source,
                    rewrittenOperands,
                    tree.Value,
                    tree.StoreTarget,
                    tree.LocalFieldBaseValue,
                    tree.LocalField,
                    tree.MemoryUses,
                    tree.MemoryDefinitions);
            }

            private static SsaTree WithFlags(SsaTree tree, GenTreeFlags flags)
            {
                GenTree source = CloneSource(tree.Source, tree.Operands, flags);
                return new SsaTree(
                    source,
                    tree.Operands,
                    tree.Value,
                    tree.StoreTarget,
                    tree.LocalFieldBaseValue,
                    tree.LocalField,
                    tree.MemoryUses,
                    tree.MemoryDefinitions);
            }

            private static GenTree CloneSource(GenTree source, ImmutableArray<SsaTree> operands, GenTreeFlags flags)
            {
                var genOperands = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    genOperands.Add(operands[i].Source);

                var clone = new GenTree(
                    source.Id,
                    source.Kind,
                    source.Pc,
                    source.SourceOp,
                    source.Type,
                    source.StackKind,
                    flags,
                    genOperands.ToImmutable(),
                    source.Int32,
                    source.Int64,
                    source.Text,
                    source.RuntimeType,
                    source.Field,
                    source.Method,
                    source.ConvKind,
                    source.ConvFlags,
                    source.TargetPc,
                    source.TargetBlockId,
                    source.BoundsCheckIndexOverride);
                clone.LocalDescriptor = source.LocalDescriptor;
                clone.CseNumber = source.CseNumber;
                return clone;
            }
        }
    }
}
