using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum SsaAssertionKind : byte
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanUnsigned,
        LessThanOrEqual,
        LessThanOrEqualUnsigned,
        GreaterThan,
        GreaterThanUnsigned,
        GreaterThanOrEqual,
        GreaterThanOrEqualUnsigned,
        Subrange,
    }

    internal enum SsaAssertionOperand1Kind : byte
    {
        ValueNumber,
        ExactType,
        Subtype,
    }

    internal enum SsaAssertionOperand2Kind : byte
    {
        ConstantInt32,
        ConstantInt64,
        ConstantDouble,
        ConstantVector,
        Null,
        ZeroObject,
        Subrange,
        ValueNumberPlusConstant,
    }

    internal readonly struct SsaAssertionDescriptor : IEquatable<SsaAssertionDescriptor>
    {
        public readonly SsaAssertionKind Kind;
        public readonly SsaAssertionOperand1Kind Operand1Kind;
        public readonly ValueNumber Operand1Value;
        public readonly SsaAssertionOperand2Kind Operand2Kind;
        public readonly ValueNumber Operand2Value;
        public readonly long Operand2Constant;
        public readonly int RangeLower;
        public readonly int RangeUpper;

        public SsaAssertionDescriptor(
            SsaAssertionKind kind,
            SsaAssertionOperand1Kind operand1Kind,
            ValueNumber operand1Value,
            SsaAssertionOperand2Kind operand2Kind,
            ValueNumber operand2Value = default,
            long operand2Constant = 0,
            int rangeLower = 0,
            int rangeUpper = 0)
        {
            if (!operand1Value.IsValid)
                throw new ArgumentOutOfRangeException(nameof(operand1Value));

            Kind = kind;
            Operand1Kind = operand1Kind;
            Operand1Value = operand1Value;
            Operand2Kind = operand2Kind;
            Operand2Value = operand2Value;
            Operand2Constant = operand2Constant;
            RangeLower = rangeLower;
            RangeUpper = rangeUpper;
        }

        public bool Equals(SsaAssertionDescriptor other)
            => Kind == other.Kind &&
               Operand1Kind == other.Operand1Kind &&
               Operand1Value == other.Operand1Value &&
               Operand2Kind == other.Operand2Kind &&
               Operand2Value == other.Operand2Value &&
               Operand2Constant == other.Operand2Constant &&
               RangeLower == other.RangeLower &&
               RangeUpper == other.RangeUpper;

        public override bool Equals(object? obj)
            => obj is SsaAssertionDescriptor other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                (int)Kind,
                (int)Operand1Kind,
                Operand1Value.Id,
                (int)Operand2Kind,
                Operand2Value.Id,
                Operand2Constant,
                RangeLower,
                RangeUpper);

        public SsaAssertionDescriptor Complement()
            => new SsaAssertionDescriptor(
                ComplementKind(Kind),
                Operand1Kind,
                Operand1Value,
                Operand2Kind,
                Operand2Value,
                Operand2Constant,
                RangeLower,
                RangeUpper);

        public static SsaAssertionKind ComplementKind(SsaAssertionKind kind)
            => kind switch
            {
                SsaAssertionKind.Equal => SsaAssertionKind.NotEqual,
                SsaAssertionKind.NotEqual => SsaAssertionKind.Equal,
                SsaAssertionKind.LessThan => SsaAssertionKind.GreaterThanOrEqual,
                SsaAssertionKind.LessThanUnsigned => SsaAssertionKind.GreaterThanOrEqualUnsigned,
                SsaAssertionKind.LessThanOrEqual => SsaAssertionKind.GreaterThan,
                SsaAssertionKind.LessThanOrEqualUnsigned => SsaAssertionKind.GreaterThanUnsigned,
                SsaAssertionKind.GreaterThan => SsaAssertionKind.LessThanOrEqual,
                SsaAssertionKind.GreaterThanUnsigned => SsaAssertionKind.LessThanOrEqualUnsigned,
                SsaAssertionKind.GreaterThanOrEqual => SsaAssertionKind.LessThan,
                SsaAssertionKind.GreaterThanOrEqualUnsigned => SsaAssertionKind.LessThanUnsigned,
                _ => throw new InvalidOperationException("Assertion kind has no complement."),
            };

        public static SsaAssertionKind ReverseKind(SsaAssertionKind kind)
            => kind switch
            {
                SsaAssertionKind.Equal => SsaAssertionKind.Equal,
                SsaAssertionKind.NotEqual => SsaAssertionKind.NotEqual,
                SsaAssertionKind.LessThan => SsaAssertionKind.GreaterThan,
                SsaAssertionKind.LessThanUnsigned => SsaAssertionKind.GreaterThanUnsigned,
                SsaAssertionKind.LessThanOrEqual => SsaAssertionKind.GreaterThanOrEqual,
                SsaAssertionKind.LessThanOrEqualUnsigned => SsaAssertionKind.GreaterThanOrEqualUnsigned,
                SsaAssertionKind.GreaterThan => SsaAssertionKind.LessThan,
                SsaAssertionKind.GreaterThanUnsigned => SsaAssertionKind.LessThanUnsigned,
                SsaAssertionKind.GreaterThanOrEqual => SsaAssertionKind.LessThanOrEqual,
                SsaAssertionKind.GreaterThanOrEqualUnsigned => SsaAssertionKind.LessThanOrEqualUnsigned,
                _ => throw new InvalidOperationException("Assertion kind cannot be reversed."),
            };
    }

    internal sealed class SsaAssertionPropagationResult
    {
        public ImmutableArray<SsaBlock> Blocks { get; }
        public bool Changed { get; }
        public int NextSyntheticTreeId { get; }

        public SsaAssertionPropagationResult(ImmutableArray<SsaBlock> blocks, bool changed, int nextSyntheticTreeId)
        {
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
            Changed = changed;
            NextSyntheticTreeId = nextSyntheticTreeId;
        }
    }

    internal static class SsaAssertionPropagator
    {
        public static SsaAssertionPropagationResult OptimizeMethod(
            SsaMethod method,
            int nextSyntheticTreeId)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.ValueNumbers is null || method.Blocks.IsDefaultOrEmpty)
                return new SsaAssertionPropagationResult(method.Blocks, changed: false, nextSyntheticTreeId);

            return new Propagator(method, nextSyntheticTreeId).Run();
        }

        private enum AssertionValueKind : byte
        {
            ValueNumber,
            ConstantInt32,
            ConstantInt64,
            Null,
        }

        private readonly struct AssertionValue
        {
            public readonly AssertionValueKind Kind;
            public readonly ValueNumber ValueNumber;
            public readonly long Constant;

            private AssertionValue(AssertionValueKind kind, ValueNumber valueNumber, long constant)
            {
                Kind = kind;
                ValueNumber = valueNumber;
                Constant = constant;
            }

            public static AssertionValue ForValueNumber(ValueNumber value)
                => new AssertionValue(AssertionValueKind.ValueNumber, value, 0);

            public static AssertionValue ForInt32(int value)
                => new AssertionValue(AssertionValueKind.ConstantInt32, default, value);

            public static AssertionValue ForInt64(long value)
                => value == 0
                    ? ForInt32(0)
                    : new AssertionValue(AssertionValueKind.ConstantInt64, default, value);

            public static AssertionValue Null
                => new AssertionValue(AssertionValueKind.Null, default, 0);
        }

        private readonly struct AssertionRange
        {
            public readonly long Lower;
            public readonly long Upper;

            public AssertionRange(long lower, long upper)
            {
                Lower = lower;
                Upper = upper;
            }

            public AssertionRange Intersect(AssertionRange other)
                => new AssertionRange(Math.Max(Lower, other.Lower), Math.Min(Upper, other.Upper));

            public AssertionRange Union(AssertionRange other)
                => new AssertionRange(Math.Min(Lower, other.Lower), Math.Max(Upper, other.Upper));

            public bool IsValid => Lower <= Upper;
            public bool IsExact => Lower == Upper;
        }

        private sealed class AssertionTable
        {
            private readonly int _maximumCount;
            private readonly List<SsaAssertionDescriptor> _entries = new();
            private readonly Dictionary<SsaAssertionDescriptor, int> _indexByDescriptor = new();

            public AssertionTable(int maximumCount)
            {
                _maximumCount = maximumCount;
            }

            public int Count => _entries.Count;

            public int Add(SsaAssertionDescriptor descriptor)
            {
                if (_indexByDescriptor.TryGetValue(descriptor, out int existing))
                    return existing;
                if (_entries.Count >= _maximumCount)
                    return 0;
                return AddUnchecked(descriptor);
            }

            private int AddUnchecked(SsaAssertionDescriptor descriptor)
            {
                int index = _entries.Count + 1;
                _entries.Add(descriptor);
                _indexByDescriptor.Add(descriptor, index);
                return index;
            }

            public int Find(SsaAssertionDescriptor descriptor)
                => _indexByDescriptor.TryGetValue(descriptor, out int index) ? index : 0;

            public SsaAssertionDescriptor Get(int index)
            {
                if ((uint)(index - 1) >= (uint)_entries.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _entries[index - 1];
            }
        }

        private sealed class AssertionSet : IEquatable<AssertionSet>
        {
            private readonly ulong[] _words;

            private AssertionSet(int assertionCount, bool universal)
            {
                _words = new ulong[(assertionCount + 63) >> 6];
                if (!universal)
                    return;

                Array.Fill(_words, ulong.MaxValue);
                int excessBits = (_words.Length << 6) - assertionCount;
                if (excessBits > 0 && _words.Length != 0)
                    _words[_words.Length - 1] >>= excessBits;
            }

            private AssertionSet(ulong[] words)
            {
                _words = words;
            }

            public static AssertionSet Empty(int assertionCount)
                => new AssertionSet(assertionCount, universal: false);

            public static AssertionSet Universal(int assertionCount)
                => new AssertionSet(assertionCount, universal: true);

            public AssertionSet Clone()
                => new AssertionSet((ulong[])_words.Clone());

            public void Add(int index)
            {
                if (index <= 0)
                    return;
                int bit = index - 1;
                int word = bit >> 6;
                if ((uint)word >= (uint)_words.Length)
                    throw new ArgumentOutOfRangeException(nameof(index));
                _words[word] |= 1UL << (bit & 63);
            }

            public bool Contains(int index)
            {
                if (index <= 0)
                    return false;
                int bit = index - 1;
                int word = bit >> 6;
                return (uint)word < (uint)_words.Length && (_words[word] & (1UL << (bit & 63))) != 0;
            }

            public void IntersectWith(AssertionSet other)
            {
                if (_words.Length != other._words.Length)
                    throw new InvalidOperationException("Assertion set sizes do not match.");
                for (int i = 0; i < _words.Length; i++)
                    _words[i] &= other._words[i];
            }

            public void UnionWith(AssertionSet other)
            {
                if (_words.Length != other._words.Length)
                    throw new InvalidOperationException("Assertion set sizes do not match.");
                for (int i = 0; i < _words.Length; i++)
                    _words[i] |= other._words[i];
            }

            public bool Equals(AssertionSet? other)
            {
                if (other is null || _words.Length != other._words.Length)
                    return false;
                for (int i = 0; i < _words.Length; i++)
                {
                    if (_words[i] != other._words[i])
                        return false;
                }
                return true;
            }

            public override bool Equals(object? obj)
                => obj is AssertionSet other && Equals(other);

            public override int GetHashCode()
            {
                int hash = 17;
                for (int i = 0; i < _words.Length; i++)
                    hash = unchecked(hash * 31 + _words[i].GetHashCode());
                return hash;
            }

            public IEnumerable<int> Enumerate()
            {
                for (int wordIndex = 0; wordIndex < _words.Length; wordIndex++)
                {
                    ulong word = _words[wordIndex];
                    while (word != 0)
                    {
                        int bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                        yield return (wordIndex << 6) + bit + 1;
                        word &= word - 1;
                    }
                }
            }
        }

        private sealed class Propagator
        {
            private readonly SsaMethod _method;
            private readonly SsaValueNumberingResult _valueNumbers;
            private readonly ValueNumberStore _store;
            private readonly AssertionTable _table;
            private readonly Dictionary<GenTree, List<int>> _generatedAfter = new(ReferenceEqualityComparer<GenTree>.Instance);
            private readonly Dictionary<CfgEdge, List<int>> _generatedOnEdge = new();
            private readonly Dictionary<ValueNumber, SsaPhi> _phiByValueNumber = new();
            private AssertionSet[] _blockIn = Array.Empty<AssertionSet>();
            private Dictionary<CfgEdge, AssertionSet> _edgeOut = new();
            private int _nextSyntheticTreeId;

            public Propagator(SsaMethod method, int nextSyntheticTreeId)
            {
                _method = method;
                _valueNumbers = method.ValueNumbers ?? throw new InvalidOperationException("Assertion propagation requires value numbering.");
                _store = _valueNumbers.Store;
                int trackedLocalCount = 0;
                for (int i = 0; i < method.Slots.Length; i++)
                {
                    if (method.Slots[i].Tracked)
                        trackedLocalCount++;
                }
                int maximumAssertions = Math.Max(64, Math.Min(256, (trackedLocalCount + 3 * method.Blocks.Length + 48) >> 2));
                _table = new AssertionTable(maximumAssertions);
                _nextSyntheticTreeId = nextSyntheticTreeId;

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

            public SsaAssertionPropagationResult Run()
            {
                GenerateAssertions();
                ComputeDataflow();
                return Rewrite();
            }

            private void GenerateAssertions()
            {
                for (int blockIndex = 0; blockIndex < _method.Blocks.Length; blockIndex++)
                {
                    var block = _method.Blocks[blockIndex];
                    for (int i = 0; i < block.TreeList.Length; i++)
                        GenerateAfterTree(block.TreeList[i].Tree);

                    GenerateConditionalEdgeAssertions(block);
                }
            }

            private void GenerateAfterTree(SsaTree tree)
            {
                if ((tree.Source.Flags & GenTreeFlags.NullCheckEliminated) == 0)
                {
                    switch (tree.Kind)
                    {
                        case GenTreeKind.NullCheck:
                        case GenTreeKind.Field:
                        case GenTreeKind.FieldAddr:
                        case GenTreeKind.StoreField:
                        case GenTreeKind.VirtualCall:
                        case GenTreeKind.DelegateInvoke:
                        case GenTreeKind.ArrayLength:
                        case GenTreeKind.ArrayElement:
                        case GenTreeKind.ArrayElementAddr:
                        case GenTreeKind.StoreArrayElement:
                        case GenTreeKind.ArrayDataRef:
                            AddNonNullAfter(tree, 0);
                            break;
                        case GenTreeKind.LoadIndirect:
                        case GenTreeKind.StoreIndirect:
                        case GenTreeKind.IndirectCall:
                            AddNonZeroAfter(tree, 0);
                            break;
                    }
                }

                if ((tree.Source.Flags & GenTreeFlags.BoundsCheckEliminated) == 0 &&
                    tree.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement)
                {
                    AddBoundsAssertionAfter(tree);
                }

                if (tree.Kind == GenTreeKind.Binary &&
                    tree.Source.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un &&
                    tree.Operands.Length == 2 &&
                    GenTreeArithmeticSemantics.IsIntegralArithmeticType(tree.Source.Type, tree.Source.StackKind) &&
                    TryGetAssertionValue(tree.Operands[1].Source, out var divisor) &&
                    TryCreateRelation(SsaAssertionKind.NotEqual, divisor, ZeroFor(tree.Operands[1].Source), out var nonZero))
                {
                    AddGeneratedAfter(tree.Source, nonZero);
                }

                if (tree.Kind is GenTreeKind.NewObject or GenTreeKind.NewArray or GenTreeKind.NewDelegate)
                {
                    if (TryGetAssertionValue(tree.Source, out var result) &&
                        TryCreateRelation(SsaAssertionKind.NotEqual, result, AssertionValue.Null, out var nonNull))
                    {
                        AddGeneratedAfter(tree.Source, nonNull);
                    }
                }

                if (tree.Kind == GenTreeKind.NewArray && tree.Operands.Length != 0 &&
                    TryGetAssertionValue(tree.Operands[0].Source, out var length) &&
                    TryCreateRelation(SsaAssertionKind.GreaterThanOrEqual, length, AssertionValue.ForInt32(0), out var nonNegative))
                {
                    AddGeneratedAfter(tree.Source, nonNegative);
                }
            }

            private void AddNonNullAfter(SsaTree tree, int operandIndex)
            {
                if ((uint)operandIndex >= (uint)tree.Operands.Length)
                    return;

                GenTree operand = tree.Operands[operandIndex].Source;
                if (!TryGetAssertionValue(operand, out var value))
                    return;

                AssertionValue zero = operand.StackKind is GenStackKind.Ref or GenStackKind.Null
                    ? AssertionValue.Null
                    : ZeroFor(operand);
                if (TryCreateRelation(SsaAssertionKind.NotEqual, value, zero, out var assertion))
                    AddGeneratedAfter(tree.Source, assertion);
            }

            private void AddNonZeroAfter(SsaTree tree, int operandIndex)
            {
                if ((uint)operandIndex >= (uint)tree.Operands.Length)
                    return;
                if (!TryGetAssertionValue(tree.Operands[operandIndex].Source, out var value))
                    return;
                if (TryCreateRelation(SsaAssertionKind.NotEqual, value, ZeroFor(tree.Operands[operandIndex].Source), out var assertion))
                    AddGeneratedAfter(tree.Source, assertion);
            }

            private void AddBoundsAssertionAfter(SsaTree tree)
            {
                if (tree.Operands.Length < 2)
                    return;
                if (!TryGetNormalValueNumber(tree.Operands[0].Source, out var arrayValue) ||
                    !TryGetAssertionValue(tree.Operands[1].Source, out var indexValue))
                {
                    return;
                }

                ValueNumber lengthValue = _store.VNForFunc(
                    GenStackKind.I4,
                    type: null,
                    ValueNumberFunction.ArrayLength,
                    arrayValue);

                if (TryCreateRelation(
                    SsaAssertionKind.LessThanUnsigned,
                    indexValue,
                    AssertionValue.ForValueNumber(lengthValue),
                    out var inRange))
                {
                    AddGeneratedAfter(tree.Source, inRange);
                }
            }

            private void GenerateConditionalEdgeAssertions(SsaBlock block)
            {
                if (!TryGetConditionalTerminator(block.Statements, out var terminator) || terminator.Operands.Length != 1)
                    return;

                CfgEdge? trueEdge = null;
                CfgEdge? falseEdge = null;
                for (int i = 0; i < block.CfgBlock.Successors.Length; i++)
                {
                    var edge = block.CfgBlock.Successors[i];
                    bool? conditionTruth = EdgeConditionTruth(terminator.Kind, edge.Kind);
                    if (!conditionTruth.HasValue)
                        continue;

                    if (conditionTruth.Value)
                    {
                        if (trueEdge.HasValue)
                            return;
                        trueEdge = edge;
                    }
                    else
                    {
                        if (falseEdge.HasValue)
                            return;
                        falseEdge = edge;
                    }
                }

                if (!trueEdge.HasValue || !falseEdge.HasValue || trueEdge.Value.ToBlockId == falseEdge.Value.ToBlockId)
                    return;

                if (!TryCreatePredicateAssertion(terminator.Operands[0], truth: true, depth: 0, out var trueAssertion))
                    return;

                int trueIndex = _table.Add(trueAssertion);
                if (trueIndex == 0)
                    return;

                int falseIndex = 0;
                if (ShouldCreateComplement(trueAssertion))
                    falseIndex = _table.Add(trueAssertion.Complement());

                AddGeneratedOnEdge(trueEdge.Value, trueIndex);
                if (falseIndex != 0)
                    AddGeneratedOnEdge(falseEdge.Value, falseIndex);
            }


            private static bool TryGetConditionalTerminator(ImmutableArray<SsaTree> statements, out SsaTree terminator)
            {
                if (!statements.IsDefaultOrEmpty)
                {
                    var last = statements[statements.Length - 1];
                    if (last.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                    {
                        terminator = last;
                        return true;
                    }

                    if (last.Kind == GenTreeKind.Branch && statements.Length >= 2)
                    {
                        var previous = statements[statements.Length - 2];
                        if (previous.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                        {
                            terminator = previous;
                            return true;
                        }
                    }
                }

                terminator = null!;
                return false;
            }

            private static bool? EdgeConditionTruth(GenTreeKind branchKind, CfgEdgeKind edgeKind)
            {
                if (edgeKind == CfgEdgeKind.Exception)
                    return null;

                if (branchKind == GenTreeKind.BranchTrue)
                {
                    if (edgeKind == CfgEdgeKind.BranchTrue)
                        return true;
                    if (edgeKind == CfgEdgeKind.FallThrough)
                        return false;
                }
                else
                {
                    if (edgeKind == CfgEdgeKind.BranchFalse)
                        return false;
                    if (edgeKind == CfgEdgeKind.FallThrough)
                        return true;
                }

                return null;
            }

            private bool TryCreatePredicateAssertion(SsaTree condition, bool truth, int depth, out SsaAssertionDescriptor assertion)
            {
                if (depth > 8)
                {
                    assertion = default;
                    return false;
                }

                if (condition.Kind == GenTreeKind.Binary && condition.Operands.Length == 2)
                {
                    BytecodeOp op = condition.Source.SourceOp;
                    if (op == BytecodeOp.Ceq && TryGetBooleanConstant(condition.Operands[0].Source, out bool leftBoolean) && IsPredicateTree(condition.Operands[1]))
                        return TryCreatePredicateAssertion(condition.Operands[1], truth == leftBoolean, depth + 1, out assertion);
                    if (op == BytecodeOp.Ceq && TryGetBooleanConstant(condition.Operands[1].Source, out bool rightBoolean) && IsPredicateTree(condition.Operands[0]))
                        return TryCreatePredicateAssertion(condition.Operands[0], truth == rightBoolean, depth + 1, out assertion);

                    SsaAssertionKind? relation = op switch
                    {
                        BytecodeOp.Ceq => SsaAssertionKind.Equal,
                        BytecodeOp.Clt => SsaAssertionKind.LessThan,
                        BytecodeOp.Clt_Un => SsaAssertionKind.LessThanUnsigned,
                        BytecodeOp.Cgt => SsaAssertionKind.GreaterThan,
                        BytecodeOp.Cgt_Un => SsaAssertionKind.GreaterThanUnsigned,
                        _ => null,
                    };

                    if (relation.HasValue &&
                        TryGetAssertionValue(condition.Operands[0].Source, out var left) &&
                        TryGetAssertionValue(condition.Operands[1].Source, out var right) &&
                        (relation.Value == SsaAssertionKind.Equal ||
                         SupportsOrderedAssertion(relation.Value, condition.Operands[0].Source, condition.Operands[1].Source, left, right)) &&
                        TryCreateRelation(relation.Value, left, right, out assertion))
                    {
                        if (!truth)
                            assertion = assertion.Complement();
                        return true;
                    }
                }

                if (TryGetAssertionValue(condition.Source, out var value) &&
                    TryCreateRelation(
                        truth ? SsaAssertionKind.NotEqual : SsaAssertionKind.Equal,
                        value,
                        condition.Source.StackKind is GenStackKind.Ref or GenStackKind.Null ? AssertionValue.Null : ZeroFor(condition.Source),
                        out assertion))
                {
                    return true;
                }

                assertion = default;
                return false;
            }

            private static bool IsPredicateTree(SsaTree tree)
                => tree.Kind == GenTreeKind.Binary && tree.Source.SourceOp is
                    BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un;

            private static bool SupportsOrderedAssertion(
                SsaAssertionKind kind,
                GenTree leftTree,
                GenTree rightTree,
                AssertionValue left,
                AssertionValue right)
            {
                if (IsStableOrderedAssertionStackKind(leftTree.StackKind) &&
                    IsStableOrderedAssertionStackKind(rightTree.StackKind))
                {
                    return true;
                }

                if (kind is not (SsaAssertionKind.LessThanUnsigned or SsaAssertionKind.GreaterThanUnsigned))
                    return false;

                return (IsZero(left) && IsZeroComparableReferenceStackKind(rightTree.StackKind)) ||
                       (IsZero(right) && IsZeroComparableReferenceStackKind(leftTree.StackKind));
            }

            private static bool IsStableOrderedAssertionStackKind(GenStackKind kind)
                => kind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

            private static bool IsObjectReferenceStackKind(GenStackKind kind)
                => kind is GenStackKind.Ref or GenStackKind.Null;

            private static bool IsZeroComparableReferenceStackKind(GenStackKind kind)
                => kind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;

            private bool TryGetBooleanConstant(GenTree tree, out bool value)
            {
                if (TryGetAssertionValue(tree, out var assertionValue))
                {
                    if (assertionValue.Kind == AssertionValueKind.ConstantInt32 && assertionValue.Constant is 0 or 1)
                    {
                        value = assertionValue.Constant != 0;
                        return true;
                    }
                    if (assertionValue.Kind == AssertionValueKind.ConstantInt64 && assertionValue.Constant is 0 or 1)
                    {
                        value = assertionValue.Constant != 0;
                        return true;
                    }
                }

                value = false;
                return false;
            }

            private static bool ShouldCreateComplement(SsaAssertionDescriptor assertion)
            {
                if (assertion.Kind == SsaAssertionKind.Equal)
                {
                    if (assertion.Operand1Kind is SsaAssertionOperand1Kind.ExactType or SsaAssertionOperand1Kind.Subtype)
                        return false;
                    if (assertion.Operand2Kind is SsaAssertionOperand2Kind.ConstantInt32 or SsaAssertionOperand2Kind.ConstantInt64)
                        return assertion.Operand2Constant is 0 or 1;
                }

                if ((assertion.Kind is SsaAssertionKind.LessThanUnsigned or SsaAssertionKind.LessThanOrEqualUnsigned) &&
                    assertion.Operand2Kind == SsaAssertionOperand2Kind.ValueNumberPlusConstant)
                {
                    return false;
                }

                return assertion.Kind != SsaAssertionKind.Subrange;
            }

            private void AddGeneratedAfter(GenTree tree, SsaAssertionDescriptor assertion)
            {
                int index = _table.Add(assertion);
                if (index == 0)
                    return;

                if (!_generatedAfter.TryGetValue(tree, out var assertions))
                {
                    assertions = new List<int>();
                    _generatedAfter.Add(tree, assertions);
                }
                if (!assertions.Contains(index))
                    assertions.Add(index);
            }

            private void AddGeneratedOnEdge(CfgEdge edge, int index)
            {
                if (!_generatedOnEdge.TryGetValue(edge, out var assertions))
                {
                    assertions = new List<int>();
                    _generatedOnEdge.Add(edge, assertions);
                }
                if (!assertions.Contains(index))
                    assertions.Add(index);
            }

            private void ComputeDataflow()
            {
                int blockCount = _method.Blocks.Length;
                int assertionCount = _table.Count;
                var blockGen = new AssertionSet[blockCount];
                _blockIn = new AssertionSet[blockCount];
                _edgeOut = new Dictionary<CfgEdge, AssertionSet>();

                for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    blockGen[blockIndex] = AssertionSet.Empty(assertionCount);
                    _blockIn[blockIndex] = blockIndex == 0
                        ? AssertionSet.Empty(assertionCount)
                        : AssertionSet.Universal(assertionCount);

                    var block = _method.Blocks[blockIndex];
                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        if (_generatedAfter.TryGetValue(block.TreeList[i].Tree.Source, out var generated))
                        {
                            for (int j = 0; j < generated.Count; j++)
                                blockGen[blockIndex].Add(generated[j]);
                        }
                    }

                    for (int edgeIndex = 0; edgeIndex < block.CfgBlock.Successors.Length; edgeIndex++)
                    {
                        var edge = block.CfgBlock.Successors[edgeIndex];
                        _edgeOut[edge] = edge.Kind == CfgEdgeKind.Exception
                            ? AssertionSet.Empty(assertionCount)
                            : AssertionSet.Universal(assertionCount);
                    }
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int orderIndex = 0; orderIndex < _method.Cfg.ReversePostOrder.Length; orderIndex++)
                    {
                        int blockId = _method.Cfg.ReversePostOrder[orderIndex];
                        var block = _method.Blocks[blockId];
                        AssertionSet mergedIn;

                        if (blockId == 0)
                        {
                            mergedIn = AssertionSet.Empty(assertionCount);
                        }
                        else
                        {
                            AssertionSet? newIn = null;
                            for (int predecessorIndex = 0; predecessorIndex < block.CfgBlock.Predecessors.Length; predecessorIndex++)
                            {
                                var predecessor = block.CfgBlock.Predecessors[predecessorIndex];
                                if (predecessor.Kind == CfgEdgeKind.Exception)
                                    continue;

                                if (!_edgeOut.TryGetValue(predecessor, out var predecessorOut))
                                    continue;

                                if (newIn is null)
                                    newIn = predecessorOut.Clone();
                                else
                                    newIn.IntersectWith(predecessorOut);
                            }

                            for (int regionIndex = 0; regionIndex < _method.Cfg.ExceptionRegions.Length; regionIndex++)
                            {
                                var region = _method.Cfg.ExceptionRegions[regionIndex];
                                if (region.HandlerStartBlockId != blockId)
                                    continue;
                                if ((uint)region.TryStartBlockId >= (uint)_blockIn.Length)
                                    continue;

                                if (newIn is null)
                                    newIn = _blockIn[region.TryStartBlockId].Clone();
                                else
                                    newIn.IntersectWith(_blockIn[region.TryStartBlockId]);
                            }

                            mergedIn = newIn ?? AssertionSet.Empty(assertionCount);
                        }

                        if (!_blockIn[blockId].Equals(mergedIn))
                        {
                            _blockIn[blockId] = mergedIn;
                            changed = true;
                        }

                        var normalOut = mergedIn.Clone();
                        normalOut.UnionWith(blockGen[blockId]);

                        for (int successorIndex = 0; successorIndex < block.CfgBlock.Successors.Length; successorIndex++)
                        {
                            var edge = block.CfgBlock.Successors[successorIndex];
                            var newOut = edge.Kind == CfgEdgeKind.Exception
                                ? AssertionSet.Empty(assertionCount)
                                : normalOut.Clone();

                            if (edge.Kind != CfgEdgeKind.Exception && _generatedOnEdge.TryGetValue(edge, out var generated))
                            {
                                for (int i = 0; i < generated.Count; i++)
                                    newOut.Add(generated[i]);
                            }

                            if (!_edgeOut[edge].Equals(newOut))
                            {
                                _edgeOut[edge] = newOut;
                                changed = true;
                            }
                        }
                    }
                }
                while (changed);
            }

            private SsaAssertionPropagationResult Rewrite()
            {
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(_method.Blocks.Length);
                bool changed = false;

                for (int blockIndex = 0; blockIndex < _method.Blocks.Length; blockIndex++)
                {
                    var block = _method.Blocks[blockIndex];
                    var active = _blockIn[block.Id].Clone();
                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    var treeLists = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(block.StatementTreeLists.Length);
                    bool blockChanged = false;

                    for (int statementIndex = 0; statementIndex < block.Statements.Length; statementIndex++)
                    {
                        var originalRoot = block.Statements[statementIndex];
                        var originalList = block.StatementTreeLists[statementIndex];
                        var rewrittenByTree = new Dictionary<SsaTree, SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                        var draftTreeList = ImmutableArray.CreateBuilder<SsaTree>(originalList.Length + 4);
                        var appended = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                        bool statementChanged = false;

                        for (int treeIndex = 0; treeIndex < originalList.Length; treeIndex++)
                        {
                            var original = originalList[treeIndex];
                            var candidate = RebuildWithOperands(original, rewrittenByTree, ref statementChanged);
                            var rewritten = PropagateAssertion(original, candidate, active, ref statementChanged);
                            rewrittenByTree.Add(original, rewritten);
                            AppendTreePreservingExistingOrder(rewritten, appended, draftTreeList);

                            if (_generatedAfter.TryGetValue(original.Source, out var generated))
                            {
                                for (int i = 0; i < generated.Count; i++)
                                    active.Add(generated[i]);
                            }
                        }

                        var rewrittenRoot = rewrittenByTree[originalRoot];
                        if (statementChanged)
                            GenTreeMorpher.NormalizeTreeFlags(rewrittenRoot.Source, _method.GenTreeMethod.Target);
                        var materializedList = ProjectReachableTreeList(rewrittenRoot, draftTreeList.ToImmutable());

                        statements.Add(rewrittenRoot);
                        treeLists.Add(materializedList);
                        blockChanged |= statementChanged;
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

                return new SsaAssertionPropagationResult(blocks.ToImmutable(), changed, _nextSyntheticTreeId);
            }

            private static void AppendTreePreservingExistingOrder(
                SsaTree tree,
                HashSet<SsaTree> appended,
                ImmutableArray<SsaTree>.Builder builder)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    AppendTreePreservingExistingOrder(tree.Operands[i], appended, builder);
                if (appended.Add(tree))
                    builder.Add(tree);
            }

            private static ImmutableArray<SsaTree> ProjectReachableTreeList(
                SsaTree root,
                ImmutableArray<SsaTree> candidateOrder)
            {
                var reachable = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                MarkReachable(root, reachable);

                var builder = ImmutableArray.CreateBuilder<SsaTree>(reachable.Count);
                var appended = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
                for (int i = 0; i < candidateOrder.Length; i++)
                {
                    var tree = candidateOrder[i];
                    if (reachable.Contains(tree) && appended.Add(tree))
                        builder.Add(tree);
                }

                AppendMissingReachable(root, reachable, appended, builder);
                if (builder.Count == 0 || !ReferenceEquals(builder[builder.Count - 1], root))
                    throw new InvalidOperationException("Assertion propagation produced an invalid SSA statement tree list.");
                return builder.ToImmutable();
            }

            private static void MarkReachable(SsaTree tree, HashSet<SsaTree> reachable)
            {
                if (!reachable.Add(tree))
                    return;
                for (int i = 0; i < tree.Operands.Length; i++)
                    MarkReachable(tree.Operands[i], reachable);
            }

            private static void AppendMissingReachable(
                SsaTree tree,
                HashSet<SsaTree> reachable,
                HashSet<SsaTree> appended,
                ImmutableArray<SsaTree>.Builder builder)
            {
                if (!reachable.Contains(tree) || appended.Contains(tree))
                    return;
                for (int i = 0; i < tree.Operands.Length; i++)
                    AppendMissingReachable(tree.Operands[i], reachable, appended, builder);
                if (appended.Add(tree))
                    builder.Add(tree);
            }

            private SsaTree RebuildWithOperands(
                SsaTree tree,
                Dictionary<SsaTree, SsaTree> rewrittenByTree,
                ref bool changed)
            {
                if (tree.Operands.IsDefaultOrEmpty)
                    return tree;

                ImmutableArray<SsaTree>.Builder? rewrittenOperands = null;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (!rewrittenByTree.TryGetValue(tree.Operands[i], out var rewrittenOperand))
                        throw new InvalidOperationException("SSA statement tree list is not in execution order.");

                    if (!ReferenceEquals(rewrittenOperand, tree.Operands[i]) && rewrittenOperands is null)
                    {
                        rewrittenOperands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                        for (int j = 0; j < i; j++)
                            rewrittenOperands.Add(tree.Operands[j]);
                    }
                    rewrittenOperands?.Add(rewrittenOperand);
                }

                if (rewrittenOperands is null)
                    return tree;

                changed = true;
                var operands = rewrittenOperands.ToImmutable();
                var source = CloneSource(tree.Source, operands, tree.Source.Flags);
                return new SsaTree(
                    source,
                    operands,
                    tree.Value,
                    tree.StoreTarget,
                    tree.LocalFieldBaseValue,
                    tree.LocalField,
                    tree.MemoryUses,
                    tree.MemoryDefinitions);
            }

            private SsaTree PropagateAssertion(
                SsaTree original,
                SsaTree candidate,
                AssertionSet active,
                ref bool changed)
            {
                if (IsPureLocalSsaUse(original) &&
                    TryGetNormalValueNumber(original.Source, out var localValue) &&
                    IsValueNumberCompatibleWithTree(localValue, original.Source) &&
                    TryGetKnownConstantForPropagation(localValue, active, out var constant) &&
                    CanReplaceWithConstant(original.Source, constant))
                {
                    changed = true;
                    return new SsaTree(CreateConstantTree(candidate.Source, constant), ImmutableArray<SsaTree>.Empty);
                }

                if (candidate.Kind == GenTreeKind.Binary &&
                    candidate.Operands.Length == 2 &&
                    IsRelationalOperator(candidate.Source.SourceOp) &&
                    IsPureTree(candidate) &&
                    TryEvaluateRelational(original, active, out bool relationValue))
                {
                    changed = true;
                    return new SsaTree(CreateBooleanConstant(candidate.Source, relationValue), ImmutableArray<SsaTree>.Empty);
                }

                GenTreeFlags flags = candidate.Source.Flags;
                BytecodeOp sourceOp = candidate.Source.SourceOp;
                if (candidate.Kind is GenTreeKind.NullCheck or GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.StoreField or GenTreeKind.VirtualCall or
                    GenTreeKind.DelegateInvoke or GenTreeKind.ArrayLength or GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or
                    GenTreeKind.StoreArrayElement or GenTreeKind.ArrayDataRef)
                {
                    if (original.Operands.Length != 0 &&
                        TryGetNormalValueNumber(original.Operands[0].Source, out var receiver) &&
                        IsKnownNonNullOrNonZero(receiver, original.Operands[0].Source, active) &&
                        (flags & GenTreeFlags.NullCheckEliminated) == 0)
                    {
                        flags |= GenTreeFlags.NullCheckEliminated | GenTreeFlags.Ordered;
                    }
                }

                if (candidate.Kind is GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect)
                {
                    if (original.Operands.Length != 0 &&
                        TryGetNormalValueNumber(original.Operands[0].Source, out var address) &&
                        IsKnownNonZero(address, original.Operands[0].Source, active) &&
                        (flags & GenTreeFlags.NullCheckEliminated) == 0)
                    {
                        flags |= GenTreeFlags.NullCheckEliminated | GenTreeFlags.Ordered;
                    }
                }

                if (candidate.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement)
                {
                    if (original.Operands.Length >= 2 &&
                        IsBoundsCheckRedundant(original, active) &&
                        (flags & GenTreeFlags.BoundsCheckEliminated) == 0)
                    {
                        flags |= GenTreeFlags.BoundsCheckEliminated | GenTreeFlags.Ordered;
                    }
                }

                if (candidate.Kind == GenTreeKind.Binary &&
                    candidate.Operands.Length == 2 &&
                    sourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un &&
                    GenTreeArithmeticSemantics.IsIntegralArithmeticType(candidate.Source.Type, candidate.Source.StackKind))
                {
                    if (TryGetNormalValueNumber(original.Operands[1].Source, out var divisor) &&
                        IsKnownNonZero(divisor, original.Operands[1].Source, active) &&
                        (flags & GenTreeFlags.DivModNoByZero) == 0)
                    {
                        flags |= GenTreeFlags.DivModNoByZero | GenTreeFlags.Ordered;
                    }

                    bool dividendNonNegative = TryGetAssertionValue(original.Operands[0].Source, out var dividendValue) &&
                                               IsKnownNonNegative(dividendValue, active);
                    bool divisorNonNegative = TryGetAssertionValue(original.Operands[1].Source, out var divisorValue) &&
                                              IsKnownNonNegative(divisorValue, active);

                    if (sourceOp is BytecodeOp.Div or BytecodeOp.Rem && dividendNonNegative && divisorNonNegative)
                        sourceOp = sourceOp == BytecodeOp.Div ? BytecodeOp.Div_Un : BytecodeOp.Rem_Un;

                    if ((sourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un || dividendNonNegative || divisorNonNegative) &&
                        (flags & GenTreeFlags.DivModNoOverflow) == 0)
                    {
                        flags |= GenTreeFlags.DivModNoOverflow | GenTreeFlags.Ordered;
                    }
                }

                if (flags != candidate.Source.Flags || sourceOp != candidate.Source.SourceOp)
                {
                    changed = true;
                    var source = CloneSource(candidate.Source, candidate.Operands, flags, sourceOp);
                    return new SsaTree(
                        source,
                        candidate.Operands,
                        candidate.Value,
                        candidate.StoreTarget,
                        candidate.LocalFieldBaseValue,
                        candidate.LocalField,
                        candidate.MemoryUses,
                        candidate.MemoryDefinitions);
                }

                return candidate;
            }

            private bool IsBoundsCheckRedundant(SsaTree tree, AssertionSet active)
            {
                if (!TryGetNormalValueNumber(tree.Operands[0].Source, out var arrayValue) ||
                    !TryGetAssertionValue(tree.Operands[1].Source, out var indexValue))
                {
                    return false;
                }

                ValueNumber lengthValueNumber = _store.VNForFunc(
                    GenStackKind.I4,
                    type: null,
                    ValueNumberFunction.ArrayLength,
                    arrayValue);
                AssertionValue lengthValue = AssertionValue.ForValueNumber(lengthValueNumber);

                if (IsRelationImplied(SsaAssertionKind.LessThanUnsigned, indexValue, lengthValue, active))
                    return true;

                if (IsKnownNonNegative(indexValue, active) &&
                    IsRelationImplied(SsaAssertionKind.LessThan, indexValue, lengthValue, active))
                {
                    return true;
                }

                if (IsBoundsCheckRedundantByValueNumberShape(indexValue, lengthValueNumber, active))
                    return true;

                return TryGetIntegralRange(indexValue, active, out var indexRange) &&
                       TryGetIntegralRange(lengthValue, active, out var lengthRange) &&
                       indexRange.Lower >= 0 &&
                       indexRange.Upper < lengthRange.Lower;
            }

            private bool IsBoundsCheckRedundantByValueNumberShape(
                AssertionValue indexValue,
                ValueNumber lengthValue,
                AssertionSet active)
            {
                if (indexValue.Kind != AssertionValueKind.ValueNumber ||
                    !_store.TryGetEntry(indexValue.ValueNumber, out var indexEntry) ||
                    indexEntry.Args.Length != 2)
                {
                    return false;
                }

                if (indexEntry.Function == ValueNumberFunction.RemUn && indexEntry.Args[1] == lengthValue)
                    return true;

                long amount;
                if (indexEntry.Function == ValueNumberFunction.Sub && indexEntry.Args[0] == lengthValue &&
                    TryConvertConstant(indexEntry.Args[1], out var subtrahend) &&
                    subtrahend.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64)
                {
                    amount = subtrahend.Kind == AssertionValueKind.ConstantInt32
                        ? unchecked((int)subtrahend.Constant)
                        : subtrahend.Constant;
                }
                else
                {
                    if (indexEntry.Function != ValueNumberFunction.Add ||
                        !TryGetLengthAndNegativeOffset(indexEntry, lengthValue, out amount))
                    {
                        return false;
                    }
                }

                return amount > 0 &&
                       amount <= int.MaxValue &&
                       TryGetIntegralRange(lengthValue, active, out var lengthRange) &&
                       lengthRange.Lower >= amount;
            }

            private bool TryGetLengthAndNegativeOffset(
                ValueNumberEntry entry,
                ValueNumber lengthValue,
                out long amount)
            {
                ValueNumber offsetValue;
                if (entry.Args[0] == lengthValue)
                    offsetValue = entry.Args[1];
                else if (entry.Args[1] == lengthValue)
                    offsetValue = entry.Args[0];
                else
                {
                    amount = 0;
                    return false;
                }

                if (!TryConvertConstant(offsetValue, out var offset) ||
                    offset.Kind is not (AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64))
                {
                    amount = 0;
                    return false;
                }

                long delta = offset.Kind == AssertionValueKind.ConstantInt32
                    ? unchecked((int)offset.Constant)
                    : offset.Constant;
                if (delta >= 0 || delta == long.MinValue)
                {
                    amount = 0;
                    return false;
                }

                amount = -delta;
                return true;
            }

            private bool IsKnownNonZero(ValueNumber value, GenTree template, AssertionSet active)
            {
                if (IsValueNumberKnownNonZero(value, template.StackKind) ||
                    IsKnownStackAllocationAddress(value, active))
                {
                    return true;
                }

                if (TryGetKnownConstant(value, active, out var constant))
                {
                    return constant.Kind switch
                    {
                        AssertionValueKind.ConstantInt32 => unchecked((int)constant.Constant) != 0,
                        AssertionValueKind.ConstantInt64 => constant.Constant != 0,
                        AssertionValueKind.Null => false,
                        _ => false,
                    };
                }

                if (IsRelationImplied(
                    SsaAssertionKind.NotEqual,
                    AssertionValue.ForValueNumber(value),
                    ZeroFor(template),
                    active))
                {
                    return true;
                }

                return TryGetIntegralRange(value, active, out var range) &&
                       (range.Lower > 0 || range.Upper < 0);
            }

            private bool IsKnownNonNullOrNonZero(ValueNumber value, GenTree template, AssertionSet active)
            {
                return template.StackKind switch
                {
                    GenStackKind.Ref or GenStackKind.Null => IsKnownNonNull(value, template, active),
                    GenStackKind.ByRef or GenStackKind.Ptr or GenStackKind.NativeInt or GenStackKind.NativeUInt =>
                        IsKnownNonZero(value, template, active),
                    _ => false,
                };
            }

            private bool IsKnownNonNull(ValueNumber value, GenTree template, AssertionSet active)
            {
                if (template.StackKind is not (GenStackKind.Ref or GenStackKind.Null) ||
                    !_store.TryGetEntry(value, out var entry) ||
                    !IsObjectReferenceStackKind(entry.StackKind))
                {
                    return false;
                }

                if (entry.Function is ValueNumberFunction.NewObject or
                    ValueNumberFunction.NewArray or
                    ValueNumberFunction.ExceptionObject)
                {
                    return true;
                }

                if (_store.TryGetConstant(value, out var constant))
                {
                    return constant.Kind switch
                    {
                        ValueNumberConstantKind.Null => false,
                        ValueNumberConstantKind.String => true,
                        _ => false,
                    };
                }

                return IsRelationImplied(
                    SsaAssertionKind.NotEqual,
                    AssertionValue.ForValueNumber(value),
                    AssertionValue.Null,
                    active);
            }

            private bool IsKnownStackAllocationAddress(ValueNumber value, AssertionSet active)
            {
                if (_method.GenTreeMethod.Target.IsRegisterBytecode)
                    return false;

                value = _store.VNNormalValue(value);
                long byteOffset = 0;

                for (int depth = 0; depth < 32; depth++)
                {
                    if (!_store.TryGetEntry(value, out var entry))
                        return false;

                    if (entry.Function == ValueNumberFunction.SsaNormalize && entry.Args.Length >= 1)
                    {
                        value = _store.VNNormalValue(entry.Args[0]);
                        continue;
                    }

                    if (entry.Function == ValueNumberFunction.PointerElementAddr)
                    {
                        if (entry.Args.Length != 3 ||
                            !TryGetKnownIntegralConstant(entry.Args[1], active, out long index) ||
                            !TryGetKnownIntegralConstant(entry.Args[2], active, out long elementSize) ||
                            elementSize <= 0)
                        {
                            return false;
                        }

                        try
                        {
                            byteOffset = checked(byteOffset + checked(index * elementSize));
                        }
                        catch (OverflowException)
                        {
                            return false;
                        }

                        value = _store.VNNormalValue(entry.Args[0]);
                        continue;
                    }

                    if (entry.Function != ValueNumberFunction.StackAlloc || entry.Args.Length != 2)
                        return false;

                    if (byteOffset == 0)
                        return true;
                    if (byteOffset < 0 ||
                        !TryGetKnownIntegralConstant(entry.Args[1], active, out long allocationElementSize) ||
                        allocationElementSize <= 0 ||
                        !TryGetIntegralRange(entry.Args[0], active, out var countRange) ||
                        countRange.Lower <= 0)
                    {
                        return false;
                    }

                    try
                    {
                        long minimumAllocationSize = checked(countRange.Lower * allocationElementSize);
                        return byteOffset < minimumAllocationSize;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
                }

                return false;
            }

            private bool TryGetKnownIntegralConstant(ValueNumber value, AssertionSet active, out long result)
            {
                value = _store.VNNormalValue(value);
                if (TryGetKnownConstant(value, active, out var constant))
                {
                    switch (constant.Kind)
                    {
                        case AssertionValueKind.ConstantInt32:
                            result = unchecked((int)constant.Constant);
                            return true;
                        case AssertionValueKind.ConstantInt64:
                            result = constant.Constant;
                            return true;
                    }
                }

                result = 0;
                return false;
            }

            private bool IsValueNumberKnownNonZero(ValueNumber value, GenStackKind stackKind)
            {
                if (!_store.TryGetEntry(value, out var entry))
                    return false;

                if (stackKind is GenStackKind.Ref or GenStackKind.Null)
                {
                    return entry.Function is ValueNumberFunction.NewObject or
                        ValueNumberFunction.NewArray or
                        ValueNumberFunction.ExceptionObject;
                }

                if (stackKind is GenStackKind.Ptr or GenStackKind.ByRef or GenStackKind.NativeInt or GenStackKind.NativeUInt)
                {
                    return entry.Function is ValueNumberFunction.PtrToLoc or
                        ValueNumberFunction.PtrToStatic or
                        ValueNumberFunction.FunctionPointer or
                        ValueNumberFunction.NewObject or
                        ValueNumberFunction.NewArray or
                        ValueNumberFunction.ExceptionObject;
                }

                return false;
            }

            private bool TryEvaluateRelational(
                SsaTree tree,
                AssertionSet active,
                out bool result)
            {
                if (!TryGetAssertionValue(tree.Operands[0].Source, out var left) ||
                    !TryGetAssertionValue(tree.Operands[1].Source, out var right))
                {
                    result = false;
                    return false;
                }

                SsaAssertionKind kind = tree.Source.SourceOp switch
                {
                    BytecodeOp.Ceq => SsaAssertionKind.Equal,
                    BytecodeOp.Clt => SsaAssertionKind.LessThan,
                    BytecodeOp.Clt_Un => SsaAssertionKind.LessThanUnsigned,
                    BytecodeOp.Cgt => SsaAssertionKind.GreaterThan,
                    BytecodeOp.Cgt_Un => SsaAssertionKind.GreaterThanUnsigned,
                    _ => throw new InvalidOperationException("Unsupported relational operator."),
                };

                if (kind != SsaAssertionKind.Equal &&
                    !SupportsOrderedAssertion(
                        kind,
                        tree.Operands[0].Source,
                        tree.Operands[1].Source,
                        left,
                        right))
                {
                    result = false;
                    return false;
                }

                if (IsRelationImplied(kind, left, right, active))
                {
                    result = true;
                    return true;
                }

                SsaAssertionKind complementKind = SsaAssertionDescriptor.ComplementKind(kind);
                if (IsRelationImplied(complementKind, left, right, active))
                {
                    result = false;
                    return true;
                }

                AssertionValue resolvedLeft = ResolveKnownConstant(left, active);
                AssertionValue resolvedRight = ResolveKnownConstant(right, active);
                NormalizeReferenceZero(
                    tree.Operands[0].Source.StackKind,
                    tree.Operands[1].Source.StackKind,
                    ref resolvedLeft,
                    ref resolvedRight);
                return TryEvaluateConstants(kind, resolvedLeft, resolvedRight, out result);
            }

            private static void NormalizeReferenceZero(
                GenStackKind leftStackKind,
                GenStackKind rightStackKind,
                ref AssertionValue left,
                ref AssertionValue right)
            {
                bool leftReference = leftStackKind is GenStackKind.Ref or GenStackKind.Null;
                bool rightReference = rightStackKind is GenStackKind.Ref or GenStackKind.Null;

                if (leftReference && IsZero(right))
                    right = AssertionValue.Null;
                if (rightReference && IsZero(left))
                    left = AssertionValue.Null;
            }

            private void NormalizeReferenceZero(
                AssertionValue originalLeft,
                AssertionValue originalRight,
                ref AssertionValue left,
                ref AssertionValue right)
            {
                bool leftReference = originalLeft.Kind == AssertionValueKind.Null ||
                    originalLeft.Kind == AssertionValueKind.ValueNumber &&
                    _store.TryGetEntry(originalLeft.ValueNumber, out var leftEntry) &&
                    IsObjectReferenceStackKind(leftEntry.StackKind);
                bool rightReference = originalRight.Kind == AssertionValueKind.Null ||
                    originalRight.Kind == AssertionValueKind.ValueNumber &&
                    _store.TryGetEntry(originalRight.ValueNumber, out var rightEntry) &&
                    IsObjectReferenceStackKind(rightEntry.StackKind);

                if (leftReference && IsZero(right))
                    right = AssertionValue.Null;
                if (rightReference && IsZero(left))
                    left = AssertionValue.Null;
            }

            private bool IsRelationImplied(
                SsaAssertionKind kind,
                AssertionValue left,
                AssertionValue right,
                AssertionSet active)
            {
                bool hasValueNumber = left.Kind == AssertionValueKind.ValueNumber ||
                                      right.Kind == AssertionValueKind.ValueNumber;
                SsaAssertionDescriptor direct = default;
                if (hasValueNumber && !TryCreateRelation(kind, left, right, out direct))
                    return false;

                AssertionValue resolvedLeft = ResolveKnownConstant(left, active);
                AssertionValue resolvedRight = ResolveKnownConstant(right, active);
                NormalizeReferenceZero(left, right, ref resolvedLeft, ref resolvedRight);
                if (TryEvaluateConstants(kind, resolvedLeft, resolvedRight, out bool constantResult))
                    return constantResult;

                if (hasValueNumber && active.Contains(_table.Find(direct)))
                    return true;

                if (IsRelationPresentThroughEqualities(kind, left, right, active))
                    return true;

                if (left.Kind == AssertionValueKind.ValueNumber &&
                    right.Kind == AssertionValueKind.ValueNumber &&
                    left.ValueNumber == right.ValueNumber &&
                    IsReflexiveValueNumber(left.ValueNumber))
                {
                    return kind is SsaAssertionKind.Equal or
                        SsaAssertionKind.LessThanOrEqual or
                        SsaAssertionKind.LessThanOrEqualUnsigned or
                        SsaAssertionKind.GreaterThanOrEqual or
                        SsaAssertionKind.GreaterThanOrEqualUnsigned;
                }

                if (!TryGetIntegralRange(left, active, out var leftRange) ||
                    !TryGetIntegralRange(right, active, out var rightRange))
                {
                    return false;
                }

                return kind switch
                {
                    SsaAssertionKind.Equal => leftRange.IsExact && rightRange.IsExact && leftRange.Lower == rightRange.Lower,
                    SsaAssertionKind.NotEqual => leftRange.Upper < rightRange.Lower || rightRange.Upper < leftRange.Lower,
                    SsaAssertionKind.LessThan => leftRange.Upper < rightRange.Lower,
                    SsaAssertionKind.LessThanOrEqual => leftRange.Upper <= rightRange.Lower,
                    SsaAssertionKind.GreaterThan => leftRange.Lower > rightRange.Upper,
                    SsaAssertionKind.GreaterThanOrEqual => leftRange.Lower >= rightRange.Upper,
                    SsaAssertionKind.LessThanUnsigned => leftRange.Lower >= 0 && rightRange.Lower >= 0 && leftRange.Upper < rightRange.Lower,
                    SsaAssertionKind.LessThanOrEqualUnsigned => leftRange.Lower >= 0 && rightRange.Lower >= 0 && leftRange.Upper <= rightRange.Lower,
                    SsaAssertionKind.GreaterThanUnsigned => leftRange.Lower >= 0 && rightRange.Lower >= 0 && leftRange.Lower > rightRange.Upper,
                    SsaAssertionKind.GreaterThanOrEqualUnsigned => leftRange.Lower >= 0 && rightRange.Lower >= 0 && leftRange.Lower >= rightRange.Upper,
                    _ => false,
                };
            }

            private bool IsRelationPresentThroughEqualities(
                SsaAssertionKind kind,
                AssertionValue left,
                AssertionValue right,
                AssertionSet active)
            {
                HashSet<ValueNumber>? leftAliases = left.Kind == AssertionValueKind.ValueNumber
                    ? CollectEqualityAliases(left.ValueNumber, active)
                    : null;
                HashSet<ValueNumber>? rightAliases = right.Kind == AssertionValueKind.ValueNumber
                    ? CollectEqualityAliases(right.ValueNumber, active)
                    : null;

                if (leftAliases is null && rightAliases is null)
                    return false;

                if (leftAliases is null)
                {
                    foreach (ValueNumber rightAlias in rightAliases!)
                    {
                        if (TryCreateRelation(kind, left, AssertionValue.ForValueNumber(rightAlias), out var assertion) &&
                            active.Contains(_table.Find(assertion)))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                if (rightAliases is null)
                {
                    foreach (ValueNumber leftAlias in leftAliases)
                    {
                        if (TryCreateRelation(kind, AssertionValue.ForValueNumber(leftAlias), right, out var assertion) &&
                            active.Contains(_table.Find(assertion)))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                foreach (ValueNumber leftAlias in leftAliases)
                {
                    foreach (ValueNumber rightAlias in rightAliases)
                    {
                        if (TryCreateRelation(
                                kind,
                                AssertionValue.ForValueNumber(leftAlias),
                                AssertionValue.ForValueNumber(rightAlias),
                                out var assertion) &&
                            active.Contains(_table.Find(assertion)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private HashSet<ValueNumber> CollectEqualityAliases(ValueNumber value, AssertionSet active)
            {
                var aliases = new HashSet<ValueNumber> { value };
                var queue = new Queue<ValueNumber>();
                queue.Enqueue(value);

                while (queue.Count != 0)
                {
                    ValueNumber current = queue.Dequeue();
                    foreach (int assertionIndex in active.Enumerate())
                    {
                        var assertion = _table.Get(assertionIndex);
                        if (assertion.Kind != SsaAssertionKind.Equal ||
                            assertion.Operand1Kind != SsaAssertionOperand1Kind.ValueNumber ||
                            assertion.Operand2Kind != SsaAssertionOperand2Kind.ValueNumberPlusConstant ||
                            assertion.Operand2Constant != 0)
                        {
                            continue;
                        }

                        ValueNumber next;
                        if (assertion.Operand1Value == current)
                            next = assertion.Operand2Value;
                        else if (assertion.Operand2Value == current)
                            next = assertion.Operand1Value;
                        else
                            continue;

                        if (!AreValueNumbersEqualityCompatible(current, next) ||
                            !AreValueNumbersEqualityCompatible(value, next))
                        {
                            continue;
                        }

                        if (aliases.Add(next))
                            queue.Enqueue(next);
                    }
                }

                return aliases;
            }

            private bool AreValueNumbersEqualityCompatible(ValueNumber left, ValueNumber right)
            {
                if (!_store.TryGetEntry(left, out var leftEntry) ||
                    !_store.TryGetEntry(right, out var rightEntry))
                {
                    return false;
                }

                if (leftEntry.TypeKey.Equals(rightEntry.TypeKey))
                    return true;
                if (IsObjectReferenceStackKind(leftEntry.StackKind) &&
                    IsObjectReferenceStackKind(rightEntry.StackKind))
                {
                    return true;
                }

                if (leftEntry.StackKind == rightEntry.StackKind &&
                    leftEntry.StackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.R4 or GenStackKind.R8)
                {
                    return true;
                }

                return IsNativePointerStackKind(leftEntry.StackKind) &&
                       IsNativePointerStackKind(rightEntry.StackKind);
            }

            private bool AreValueNumbersOrderedCompatible(ValueNumber left, ValueNumber right)
            {
                if (!_store.TryGetEntry(left, out var leftEntry) ||
                    !_store.TryGetEntry(right, out var rightEntry))
                {
                    return false;
                }

                if (leftEntry.StackKind == rightEntry.StackKind)
                    return IsStableOrderedAssertionStackKind(leftEntry.StackKind);

                return IsNativePointerStackKind(leftEntry.StackKind) &&
                       IsNativePointerStackKind(rightEntry.StackKind);
            }

            private bool IsReflexiveValueNumber(ValueNumber value)
            {
                if (!_store.TryGetEntry(value, out var entry))
                    return false;
                return entry.StackKind is not (GenStackKind.R4 or GenStackKind.R8);
            }

            private bool IsKnownNonNegative(AssertionValue value, AssertionSet active)
                => TryGetIntegralRange(value, active, out var range) && range.Lower >= 0;

            private bool TryGetIntegralRange(AssertionValue value, AssertionSet active, out AssertionRange range)
            {
                AssertionValue resolved = ResolveKnownConstant(value, active);
                switch (resolved.Kind)
                {
                    case AssertionValueKind.ConstantInt32:
                        long int32Value = unchecked((int)resolved.Constant);
                        range = new AssertionRange(int32Value, int32Value);
                        return true;
                    case AssertionValueKind.ConstantInt64:
                        range = new AssertionRange(resolved.Constant, resolved.Constant);
                        return true;
                    case AssertionValueKind.ValueNumber:
                        return TryGetIntegralRange(resolved.ValueNumber, active, out range);
                    default:
                        range = default;
                        return false;
                }
            }

            private bool TryGetIntegralRange(ValueNumber value, AssertionSet active, out AssertionRange range)
            {
                int budget = 256;
                var visited = new HashSet<ValueNumber>();
                return TryGetIntegralRangeWorker(value, active, visited, ref budget, out range);
            }

            private bool TryGetIntegralRangeWorker(
                ValueNumber value,
                AssertionSet active,
                HashSet<ValueNumber> visited,
                ref int budget,
                out AssertionRange range)
            {
                range = default;
                if (!_store.TryGetEntry(value, out var entry) || !TryGetBaseIntegralRange(entry, out var baseRange))
                    return false;

                if (TryConvertConstant(value, out var constant))
                {
                    return IsAssertionValueCompatibleWithEntry(entry, constant) &&
                           TryGetIntegralRange(constant, active, out range);
                }

                range = baseRange;
                if (budget > 0 && visited.Add(value))
                {
                    try
                    {
                        budget--;
                        if (entry.Function == ValueNumberFunction.PhiDef &&
                            _phiByValueNumber.TryGetValue(value, out var phi) &&
                            TryGetPhiRange(value, entry, phi, visited, ref budget, out var phiRange))
                        {
                            range = range.Intersect(phiRange);
                        }
                        else if (TryGetFunctionRange(entry, active, visited, ref budget, baseRange, out var functionRange))
                        {
                            range = range.Intersect(functionRange);
                        }
                    }
                    finally
                    {
                        visited.Remove(value);
                    }
                }

                ApplyAssertionsToRange(value, entry, active, ref range);
                return range.IsValid;
            }

            private bool TryGetFunctionRange(
                ValueNumberEntry entry,
                AssertionSet active,
                HashSet<ValueNumber> visited,
                ref int budget,
                AssertionRange typeRange,
                out AssertionRange range)
            {
                range = default;
                switch (entry.Function)
                {
                    case ValueNumberFunction.SsaNormalize when entry.Args.Length >= 1:
                        if (IsRangePreservingIntegralSsaNormalize(entry, entry.Args[0]) &&
                            TryGetIntegralRangeWorker(entry.Args[0], active, visited, ref budget, out var normalizedRange))
                        {
                            range = normalizedRange.Intersect(typeRange);
                            return range.IsValid;
                        }
                        return false;

                    case ValueNumberFunction.Neg when entry.Args.Length == 1:
                        if (TryGetIntegralRangeWorker(entry.Args[0], active, visited, ref budget, out var operandRange))
                            return TryNegateRange(operandRange, typeRange, out range);
                        return false;

                    case ValueNumberFunction.Add:
                    case ValueNumberFunction.Sub:
                    case ValueNumberFunction.Mul:
                    case ValueNumberFunction.And:
                    case ValueNumberFunction.Or:
                    case ValueNumberFunction.Shl:
                    case ValueNumberFunction.Shr:
                    case ValueNumberFunction.ShrUn:
                    case ValueNumberFunction.DivUn:
                    case ValueNumberFunction.RemUn:
                        if (entry.Args.Length != 2 ||
                            !TryGetIntegralRangeWorker(entry.Args[0], active, visited, ref budget, out var leftRange) ||
                            !TryGetIntegralRangeWorker(entry.Args[1], active, visited, ref budget, out var rightRange))
                        {
                            return false;
                        }

                        return entry.Function switch
                        {
                            ValueNumberFunction.Add => TryAddRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.Sub => TrySubtractRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.Mul => TryMultiplyRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.And => TryAndRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.Or => TryOrRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.Shl => TryShiftLeftRange(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.Shr => TryShiftRightRange(leftRange, rightRange, typeRange, logical: false, out range),
                            ValueNumberFunction.ShrUn => TryShiftRightRange(leftRange, rightRange, typeRange, logical: true, out range),
                            ValueNumberFunction.DivUn => TryUnsignedDivideRanges(leftRange, rightRange, typeRange, out range),
                            ValueNumberFunction.RemUn => TryUnsignedRemainderRanges(leftRange, rightRange, typeRange, out range),
                            _ => false,
                        };

                    default:
                        return false;
                }
            }

            private bool TryGetPhiRange(
                ValueNumber value,
                ValueNumberEntry entry,
                SsaPhi phi,
                HashSet<ValueNumber> visited,
                ref int budget,
                out AssertionRange range)
            {
                var inductionVisited = new HashSet<ValueNumber>(visited);
                int inductionBudget = budget;
                if (TryGetInductionPhiRange(value, entry, phi, inductionVisited, ref inductionBudget, out range))
                {
                    budget = inductionBudget;
                    return true;
                }

                if (!TryGetBaseIntegralRange(entry, out var typeRange))
                    return false;

                bool hasRange = false;
                range = default;
                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    var input = phi.Inputs[i];
                    if (!_valueNumbers.TryGetSsaValue(input.Value, out var pair))
                        return false;

                    ValueNumber inputValue = _store.VNNormalValue(pair.Conservative);
                    if (!inputValue.IsValid)
                        return false;

                    AssertionSet edgeAssertions = GetPhiInputAssertions(phi, input.PredecessorBlockId);
                    if (!TryGetIntegralRangeWorker(inputValue, edgeAssertions, visited, ref budget, out var inputRange) ||
                        IsFullRange(inputRange, typeRange))
                    {
                        return false;
                    }

                    range = hasRange ? range.Union(inputRange) : inputRange;
                    hasRange = true;
                }

                return hasRange && range.IsValid;
            }

            private bool TryGetInductionPhiRange(
                ValueNumber value,
                ValueNumberEntry entry,
                SsaPhi phi,
                HashSet<ValueNumber> visited,
                ref int budget,
                out AssertionRange range)
            {
                range = default;
                if (!TryGetBaseIntegralRange(entry, out var typeRange) || phi.Inputs.Length < 2)
                    return false;

                bool hasEntry = false;
                bool hasRecurrence = false;
                int direction = 0;
                AssertionRange entryRange = default;
                long recurrenceLower = long.MaxValue;
                long recurrenceUpper = long.MinValue;

                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    var input = phi.Inputs[i];
                    if (!_valueNumbers.TryGetSsaValue(input.Value, out var pair))
                        return false;

                    ValueNumber inputValue = _store.VNNormalValue(pair.Conservative);
                    if (!inputValue.IsValid)
                        return false;

                    AssertionSet edgeAssertions = GetPhiInputAssertions(phi, input.PredecessorBlockId);
                    if (TryGetInductionStep(inputValue, value, out long step))
                    {
                        if (step == 0)
                            return false;

                        int stepDirection = step > 0 ? 1 : -1;
                        if (direction != 0 && direction != stepDirection)
                            return false;
                        direction = stepDirection;

                        var sourceRange = typeRange;
                        ApplyAssertionsToRange(value, entry, edgeAssertions, ref sourceRange);
                        if (!sourceRange.IsValid)
                            return false;

                        if (step > 0)
                        {
                            var nextUpper = (System.Numerics.BigInteger)sourceRange.Upper + step;
                            if (nextUpper > typeRange.Upper)
                                return false;
                            recurrenceUpper = Math.Max(recurrenceUpper, (long)nextUpper);
                        }
                        else
                        {
                            var nextLower = (System.Numerics.BigInteger)sourceRange.Lower + step;
                            if (nextLower < typeRange.Lower)
                                return false;
                            recurrenceLower = Math.Min(recurrenceLower, (long)nextLower);
                        }

                        hasRecurrence = true;
                        continue;
                    }

                    if (!TryProveDoesNotContainValueNumber(inputValue, value, new HashSet<ValueNumber>(), 32))
                        return false;

                    if (!TryGetIntegralRangeWorker(inputValue, edgeAssertions, visited, ref budget, out var inputRange))
                        return false;

                    entryRange = hasEntry ? entryRange.Union(inputRange) : inputRange;
                    hasEntry = true;
                }

                if (!hasEntry || !hasRecurrence)
                    return false;

                if (direction > 0)
                {
                    if (recurrenceUpper == long.MinValue)
                        return false;
                    range = new AssertionRange(entryRange.Lower, Math.Max(entryRange.Upper, recurrenceUpper));
                }
                else
                {
                    if (recurrenceLower == long.MaxValue)
                        return false;
                    range = new AssertionRange(Math.Min(entryRange.Lower, recurrenceLower), entryRange.Upper);
                }

                return range.IsValid && range.Lower >= typeRange.Lower && range.Upper <= typeRange.Upper;
            }

            private AssertionSet GetPhiInputAssertions(SsaPhi phi, int predecessorBlockId)
            {
                AssertionSet? result = null;
                if ((uint)phi.BlockId < (uint)_method.Blocks.Length)
                {
                    var predecessors = _method.Blocks[phi.BlockId].CfgBlock.Predecessors;
                    for (int i = 0; i < predecessors.Length; i++)
                    {
                        var edge = predecessors[i];
                        if (edge.Kind == CfgEdgeKind.Exception || edge.FromBlockId != predecessorBlockId ||
                            !_edgeOut.TryGetValue(edge, out var assertions))
                        {
                            continue;
                        }

                        if (result is null)
                            result = assertions.Clone();
                        else
                            result.IntersectWith(assertions);
                    }
                }

                return result ?? AssertionSet.Empty(_table.Count);
            }

            private bool TryGetInductionStep(ValueNumber expression, ValueNumber phiValue, out long step)
            {
                step = 0;
                while (TryUnwrapRangePreservingIntegralSsaNormalize(expression, out var unwrapped))
                    expression = unwrapped;

                if (!_store.TryGetEntry(expression, out var entry) || entry.Args.Length != 2)
                    return false;

                if (entry.Function == ValueNumberFunction.Add)
                {
                    if (entry.Args[0] == phiValue && TryGetIntegralConstantValue(entry.Args[1], out step))
                        return true;
                    if (entry.Args[1] == phiValue && TryGetIntegralConstantValue(entry.Args[0], out step))
                        return true;
                    return false;
                }

                if (entry.Function == ValueNumberFunction.Sub && entry.Args[0] == phiValue &&
                    TryGetIntegralConstantValue(entry.Args[1], out long subtrahend) && subtrahend != long.MinValue)
                {
                    step = -subtrahend;
                    return true;
                }

                return false;
            }

            private bool TryGetIntegralConstantValue(ValueNumber value, out long constant)
            {
                if (TryConvertConstant(value, out var converted))
                {
                    if (converted.Kind == AssertionValueKind.ConstantInt32)
                    {
                        constant = unchecked((int)converted.Constant);
                        return true;
                    }
                    if (converted.Kind == AssertionValueKind.ConstantInt64)
                    {
                        constant = converted.Constant;
                        return true;
                    }
                }

                constant = 0;
                return false;
            }

            private bool TryProveDoesNotContainValueNumber(ValueNumber expression, ValueNumber target, HashSet<ValueNumber> visited, int budget)
            {
                if (expression == target || budget <= 0 || !visited.Add(expression))
                    return false;

                try
                {
                    if (!_store.TryGetEntry(expression, out var entry))
                        return false;

                    for (int i = 0; i < entry.Args.Length; i++)
                    {
                        if (!TryProveDoesNotContainValueNumber(entry.Args[i], target, visited, budget - 1))
                            return false;
                    }

                    return true;
                }
                finally
                {
                    visited.Remove(expression);
                }
            }

            private void ApplyAssertionsToRange(
                ValueNumber value,
                ValueNumberEntry entry,
                AssertionSet active,
                ref AssertionRange range)
            {
                var aliases = CollectEqualityAliases(value, active);
                foreach (ValueNumber alias in aliases)
                {
                    if (alias == value ||
                        !_store.TryGetEntry(alias, out var aliasEntry) ||
                        aliasEntry.StackKind != entry.StackKind ||
                        !TryGetBaseIntegralRange(aliasEntry, out var aliasRange))
                    {
                        continue;
                    }

                    range = range.Intersect(aliasRange);
                }

                foreach (int assertionIndex in active.Enumerate())
                {
                    var assertion = _table.Get(assertionIndex);
                    if (assertion.Operand1Kind != SsaAssertionOperand1Kind.ValueNumber ||
                        !aliases.Contains(assertion.Operand1Value))
                    {
                        continue;
                    }

                    if (assertion.Kind == SsaAssertionKind.Subrange &&
                        assertion.Operand2Kind == SsaAssertionOperand2Kind.Subrange)
                    {
                        range = range.Intersect(new AssertionRange(assertion.RangeLower, assertion.RangeUpper));
                        continue;
                    }

                    if (!_store.TryGetEntry(assertion.Operand1Value, out var assertionEntry) ||
                        !TryGetAssertionConstant(assertion, out long assertionConstant, out bool isInt32) ||
                        !IsAssertionConstantCompatible(assertionEntry, assertion.Operand2Kind))
                    {
                        continue;
                    }

                    switch (assertion.Kind)
                    {
                        case SsaAssertionKind.Equal:
                            range = range.Intersect(new AssertionRange(assertionConstant, assertionConstant));
                            break;
                        case SsaAssertionKind.NotEqual:
                            if (assertionConstant == 0)
                            {
                                if (range.Lower == 0)
                                    range = range.Intersect(new AssertionRange(1, long.MaxValue));
                                else if (range.Upper == 0)
                                    range = range.Intersect(new AssertionRange(long.MinValue, -1));
                            }
                            break;
                        case SsaAssertionKind.LessThan:
                            if (assertionConstant != long.MinValue)
                                range = range.Intersect(new AssertionRange(long.MinValue, assertionConstant - 1));
                            break;
                        case SsaAssertionKind.LessThanOrEqual:
                            range = range.Intersect(new AssertionRange(long.MinValue, assertionConstant));
                            break;
                        case SsaAssertionKind.GreaterThan:
                            if (assertionConstant != long.MaxValue)
                                range = range.Intersect(new AssertionRange(assertionConstant + 1, long.MaxValue));
                            break;
                        case SsaAssertionKind.GreaterThanOrEqual:
                            range = range.Intersect(new AssertionRange(assertionConstant, long.MaxValue));
                            break;
                        case SsaAssertionKind.LessThanUnsigned:
                        case SsaAssertionKind.LessThanOrEqualUnsigned:
                            if (TryGetUnsignedUpperBound(assertion.Kind, assertionConstant, isInt32, out long unsignedUpper))
                                range = range.Intersect(new AssertionRange(0, unsignedUpper));
                            break;
                    }
                }
            }

            private static bool TryAddRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
                => TryCreateRange(
                    (System.Numerics.BigInteger)left.Lower + right.Lower,
                    (System.Numerics.BigInteger)left.Upper + right.Upper,
                    typeRange,
                    out range);

            private static bool TrySubtractRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
                => TryCreateRange(
                    (System.Numerics.BigInteger)left.Lower - right.Upper,
                    (System.Numerics.BigInteger)left.Upper - right.Lower,
                    typeRange,
                    out range);

            private static bool TryMultiplyRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
            {
                var p0 = (System.Numerics.BigInteger)left.Lower * right.Lower;
                var p1 = (System.Numerics.BigInteger)left.Lower * right.Upper;
                var p2 = (System.Numerics.BigInteger)left.Upper * right.Lower;
                var p3 = (System.Numerics.BigInteger)left.Upper * right.Upper;
                var lower = System.Numerics.BigInteger.Min(System.Numerics.BigInteger.Min(p0, p1), System.Numerics.BigInteger.Min(p2, p3));
                var upper = System.Numerics.BigInteger.Max(System.Numerics.BigInteger.Max(p0, p1), System.Numerics.BigInteger.Max(p2, p3));
                return TryCreateRange(lower, upper, typeRange, out range);
            }

            private static bool TryNegateRange(AssertionRange operand, AssertionRange typeRange, out AssertionRange range)
                => TryCreateRange(
                    -(System.Numerics.BigInteger)operand.Upper,
                    -(System.Numerics.BigInteger)operand.Lower,
                    typeRange,
                    out range);

            private static bool TryAndRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
            {
                if (left.Lower >= 0 || right.Lower >= 0)
                {
                    long upper = left.Lower >= 0 && right.Lower >= 0
                        ? Math.Min(left.Upper, right.Upper)
                        : left.Lower >= 0 ? left.Upper : right.Upper;
                    range = new AssertionRange(0, upper).Intersect(typeRange);
                    return range.IsValid;
                }

                range = default;
                return false;
            }

            private static bool TryOrRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
            {
                if (left.Upper < 0 || right.Upper < 0)
                {
                    range = new AssertionRange(typeRange.Lower, -1).Intersect(typeRange);
                    return range.IsValid;
                }

                if (left.Lower < 0 || right.Lower < 0)
                {
                    range = default;
                    return false;
                }

                ulong upper = BitwiseUpperBound(unchecked((ulong)left.Upper) | unchecked((ulong)right.Upper));
                if (upper > long.MaxValue)
                {
                    range = default;
                    return false;
                }

                range = new AssertionRange(Math.Max(left.Lower, right.Lower), (long)upper).Intersect(typeRange);
                return range.IsValid;
            }

            private static ulong BitwiseUpperBound(ulong value)
            {
                value |= value >> 1;
                value |= value >> 2;
                value |= value >> 4;
                value |= value >> 8;
                value |= value >> 16;
                value |= value >> 32;
                return value;
            }

            private static bool TryShiftLeftRange(AssertionRange value, AssertionRange shift, AssertionRange typeRange, out AssertionRange range)
            {
                if (!TryGetShiftAmount(shift, typeRange, out int amount))
                {
                    range = default;
                    return false;
                }

                return TryCreateRange(
                    (System.Numerics.BigInteger)value.Lower << amount,
                    (System.Numerics.BigInteger)value.Upper << amount,
                    typeRange,
                    out range);
            }

            private static bool TryShiftRightRange(
                AssertionRange value,
                AssertionRange shift,
                AssertionRange typeRange,
                bool logical,
                out AssertionRange range)
            {
                if (!TryGetShiftAmount(shift, typeRange, out int amount))
                {
                    range = default;
                    return false;
                }

                if (!logical || amount == 0)
                {
                    range = new AssertionRange(value.Lower >> amount, value.Upper >> amount).Intersect(typeRange);
                    return range.IsValid;
                }

                int bits = IntegralBitWidth(typeRange);
                System.Numerics.BigInteger modulus = System.Numerics.BigInteger.One << bits;
                System.Numerics.BigInteger lower;
                System.Numerics.BigInteger upper;

                if (value.Lower >= 0)
                {
                    lower = (System.Numerics.BigInteger)value.Lower >> amount;
                    upper = (System.Numerics.BigInteger)value.Upper >> amount;
                }
                else if (value.Upper < 0)
                {
                    lower = (modulus + value.Lower) >> amount;
                    upper = (modulus + value.Upper) >> amount;
                }
                else
                {
                    lower = System.Numerics.BigInteger.Zero;
                    upper = (modulus - 1) >> amount;
                }

                return TryCreateRange(lower, upper, typeRange, out range);
            }

            private static bool TryGetShiftAmount(AssertionRange shift, AssertionRange typeRange, out int amount)
            {
                if (!shift.IsExact)
                {
                    amount = 0;
                    return false;
                }

                int mask = IntegralBitWidth(typeRange) - 1;
                amount = unchecked((int)shift.Lower) & mask;
                return true;
            }

            private static int IntegralBitWidth(AssertionRange typeRange)
                => typeRange.Lower == int.MinValue && typeRange.Upper == int.MaxValue ? 32 : 64;

            private static bool IsFullRange(AssertionRange range, AssertionRange typeRange)
                => range.Lower == typeRange.Lower && range.Upper == typeRange.Upper;

            private static bool TryUnsignedDivideRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
            {
                if (left.Lower < 0 || right.Lower <= 0)
                {
                    range = default;
                    return false;
                }

                range = new AssertionRange(left.Lower / Math.Max(1, right.Upper), left.Upper / right.Lower).Intersect(typeRange);
                return range.IsValid;
            }

            private static bool TryUnsignedRemainderRanges(AssertionRange left, AssertionRange right, AssertionRange typeRange, out AssertionRange range)
            {
                if (left.Lower < 0 || right.Lower <= 0)
                {
                    range = default;
                    return false;
                }

                long upper = Math.Min(left.Upper, right.Upper - 1);
                range = new AssertionRange(0, upper).Intersect(typeRange);
                return range.IsValid;
            }

            private static bool TryCreateRange(
                System.Numerics.BigInteger lower,
                System.Numerics.BigInteger upper,
                AssertionRange typeRange,
                out AssertionRange range)
            {
                if (lower < typeRange.Lower || upper > typeRange.Upper || lower > upper)
                {
                    range = default;
                    return false;
                }

                range = new AssertionRange((long)lower, (long)upper);
                return true;
            }

            private bool IsAssertionConstantCompatible(
                ValueNumberEntry entry,
                SsaAssertionOperand2Kind constantKind)
            {
                return entry.StackKind switch
                {
                    GenStackKind.I4 => constantKind == SsaAssertionOperand2Kind.ConstantInt32,
                    GenStackKind.I8 => constantKind is SsaAssertionOperand2Kind.ConstantInt32 or SsaAssertionOperand2Kind.ConstantInt64,
                    GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr or GenStackKind.ByRef =>
                        _method.GenTreeMethod.Target.PointerSize == 8
                            ? constantKind is SsaAssertionOperand2Kind.ConstantInt32 or SsaAssertionOperand2Kind.ConstantInt64
                            : constantKind == SsaAssertionOperand2Kind.ConstantInt32,
                    _ => false,
                };
            }

            private bool TryGetBaseIntegralRange(ValueNumberEntry entry, out AssertionRange range)
            {
                if (entry.Function == ValueNumberFunction.ArrayLength)
                {
                    range = new AssertionRange(0, int.MaxValue);
                    return true;
                }

                if (entry.Function is ValueNumberFunction.Ceq or
                    ValueNumberFunction.Clt or
                    ValueNumberFunction.CltUn or
                    ValueNumberFunction.Cgt or
                    ValueNumberFunction.CgtUn)
                {
                    range = new AssertionRange(0, 1);
                    return true;
                }

                switch (entry.StackKind)
                {
                    case GenStackKind.I4:
                        range = new AssertionRange(int.MinValue, int.MaxValue);
                        return true;
                    case GenStackKind.I8:
                        range = new AssertionRange(long.MinValue, long.MaxValue);
                        return true;
                    case GenStackKind.NativeInt:
                    case GenStackKind.NativeUInt:
                    case GenStackKind.Ptr:
                    case GenStackKind.ByRef:
                        range = _method.GenTreeMethod.Target.PointerSize == 8
                            ? new AssertionRange(long.MinValue, long.MaxValue)
                            : new AssertionRange(int.MinValue, int.MaxValue);
                        return true;
                    default:
                        range = default;
                        return false;
                }
            }

            private static bool TryGetAssertionConstant(
                SsaAssertionDescriptor assertion,
                out long value,
                out bool isInt32)
            {
                switch (assertion.Operand2Kind)
                {
                    case SsaAssertionOperand2Kind.ConstantInt32:
                        value = unchecked((int)assertion.Operand2Constant);
                        isInt32 = true;
                        return true;
                    case SsaAssertionOperand2Kind.ConstantInt64:
                        value = assertion.Operand2Constant;
                        isInt32 = false;
                        return true;
                    case SsaAssertionOperand2Kind.Null:
                        value = 0;
                        isInt32 = false;
                        return true;
                    default:
                        value = 0;
                        isInt32 = false;
                        return false;
                }
            }

            private static bool TryGetUnsignedUpperBound(
                SsaAssertionKind kind,
                long constant,
                bool isInt32,
                out long upper)
            {
                if (isInt32)
                {
                    uint value = unchecked((uint)(int)constant);
                    if (kind == SsaAssertionKind.LessThanUnsigned)
                    {
                        if (value == 0 || value > 0x80000000u)
                        {
                            upper = 0;
                            return false;
                        }
                        upper = value - 1L;
                        return true;
                    }

                    if (value > int.MaxValue)
                    {
                        upper = 0;
                        return false;
                    }
                    upper = value;
                    return true;
                }

                ulong value64 = unchecked((ulong)constant);
                if (kind == SsaAssertionKind.LessThanUnsigned)
                {
                    if (value64 == 0 || value64 > 0x8000000000000000UL)
                    {
                        upper = 0;
                        return false;
                    }
                    upper = value64 == 0x8000000000000000UL ? long.MaxValue : (long)(value64 - 1);
                    return true;
                }

                if (value64 > long.MaxValue)
                {
                    upper = 0;
                    return false;
                }
                upper = (long)value64;
                return true;
            }

            private AssertionValue ResolveKnownConstant(AssertionValue value, AssertionSet active)
            {
                if (value.Kind != AssertionValueKind.ValueNumber)
                    return value;
                return TryGetKnownConstant(value.ValueNumber, active, out var constant) ? constant : value;
            }

            private bool TryGetKnownConstantForPropagation(ValueNumber value, AssertionSet active, out AssertionValue constant)
                => TryGetKnownConstantCore(value, active, false, out constant);

            private bool TryGetKnownConstant(ValueNumber value, AssertionSet active, out AssertionValue constant)
                => TryGetKnownConstantCore(value, active, true, out constant);

            private bool TryGetKnownConstantCore(
                ValueNumber value,
                AssertionSet active,
                bool includeSsaNormalizedDirectConstants,
                out AssertionValue constant)
            {
                if (!_store.TryGetEntry(value, out var valueEntry))
                {
                    constant = default;
                    return false;
                }

                bool found = false;
                constant = default;
                var aliases = CollectEqualityAliases(value, active);
                foreach (ValueNumber alias in aliases)
                {
                    if (!_store.TryGetEntry(alias, out var aliasEntry) ||
                        !AreValueNumbersEqualityCompatible(value, alias))
                    {
                        continue;
                    }

                    AssertionValue directConstant;
                    bool hasDirectConstant = includeSsaNormalizedDirectConstants
                        ? TryConvertConstant(alias, out directConstant)
                        : TryConvertLiteralConstant(alias, out directConstant);
                    if (hasDirectConstant &&
                        IsAssertionValueCompatibleWithEntry(aliasEntry, directConstant) &&
                        IsAssertionValueCompatibleWithEntry(valueEntry, directConstant) &&
                        !TryMergeKnownConstant(directConstant, ref found, ref constant))
                    {
                        constant = default;
                        return false;
                    }

                    foreach (int assertionIndex in active.Enumerate())
                    {
                        var assertion = _table.Get(assertionIndex);
                        if (assertion.Kind != SsaAssertionKind.Equal ||
                            assertion.Operand1Kind != SsaAssertionOperand1Kind.ValueNumber ||
                            assertion.Operand1Value != alias ||
                            !TryGetAssertionConstantValue(assertion, out var assertionConstant) ||
                            !IsAssertionValueCompatibleWithEntry(aliasEntry, assertionConstant) ||
                            !IsAssertionValueCompatibleWithEntry(valueEntry, assertionConstant))
                        {
                            continue;
                        }

                        if (!TryMergeKnownConstant(assertionConstant, ref found, ref constant))
                        {
                            constant = default;
                            return false;
                        }
                    }
                }

                return found;
            }

            private static bool TryMergeKnownConstant(
                AssertionValue candidate,
                ref bool found,
                ref AssertionValue constant)
            {
                if (!found)
                {
                    found = true;
                    constant = candidate;
                    return true;
                }

                if (constant.Kind == AssertionValueKind.Null || candidate.Kind == AssertionValueKind.Null)
                    return constant.Kind == candidate.Kind;

                bool constantInteger = constant.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64;
                bool candidateInteger = candidate.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64;
                return constantInteger && candidateInteger && constant.Constant == candidate.Constant;
            }

            private static bool TryGetAssertionConstantValue(
                SsaAssertionDescriptor assertion,
                out AssertionValue constant)
            {
                switch (assertion.Operand2Kind)
                {
                    case SsaAssertionOperand2Kind.ConstantInt32:
                        constant = AssertionValue.ForInt32(unchecked((int)assertion.Operand2Constant));
                        return true;
                    case SsaAssertionOperand2Kind.ConstantInt64:
                        constant = AssertionValue.ForInt64(assertion.Operand2Constant);
                        return true;
                    case SsaAssertionOperand2Kind.Null:
                        constant = AssertionValue.Null;
                        return true;
                    default:
                        constant = default;
                        return false;
                }
            }

            private bool IsAssertionValueCompatibleWithEntry(
                ValueNumberEntry entry,
                AssertionValue value)
            {
                if (value.Kind == AssertionValueKind.Null)
                    return IsObjectReferenceStackKind(entry.StackKind);

                if (value.Kind is not (AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64))
                    return false;

                return entry.StackKind switch
                {
                    GenStackKind.I4 => value.Kind == AssertionValueKind.ConstantInt32,
                    GenStackKind.I8 => true,
                    GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr or GenStackKind.ByRef =>
                        _method.GenTreeMethod.Target.PointerSize == 8 ||
                        value.Kind == AssertionValueKind.ConstantInt32,
                    _ => false,
                };
            }

            private bool IsValueNumberCompatibleWithTree(ValueNumber value, GenTree tree)
            {
                return _store.TryGetEntry(value, out var entry) &&
                       entry.TypeKey.Equals(ValueNumberType.For(tree.StackKind, tree.Type));
            }

            private bool CanReplaceWithConstant(GenTree template, AssertionValue constant)
            {
                if (template.CanThrow)
                    return false;

                return template.StackKind switch
                {
                    GenStackKind.Ref or GenStackKind.Null => constant.Kind == AssertionValueKind.Null,
                    GenStackKind.I4 => constant.Kind == AssertionValueKind.ConstantInt32,
                    GenStackKind.I8 => constant.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64,
                    GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr =>
                        _method.GenTreeMethod.Target.PointerSize == 8
                            ? constant.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64
                            : constant.Kind == AssertionValueKind.ConstantInt32,
                    _ => false,
                };
            }

            private static bool IsPureLocalSsaUse(SsaTree tree)
                => tree.Value.HasValue && tree.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp;

            private static bool IsRelationalOperator(BytecodeOp op)
                => op is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un;

            private static bool IsPureTree(SsaTree tree)
            {
                const GenTreeFlags observable = GenTreeFlags.ContainsCall |
                                                GenTreeFlags.CanThrow |
                                                GenTreeFlags.SideEffect |
                                                GenTreeFlags.MemoryRead |
                                                GenTreeFlags.MemoryWrite |
                                                GenTreeFlags.ControlFlow |
                                                GenTreeFlags.ExceptionFlow |
                                                GenTreeFlags.Ordered;

                if ((tree.Source.Flags & observable) != 0)
                    return false;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (!IsPureTree(tree.Operands[i]))
                        return false;
                }
                return true;
            }

            private bool TryGetAssertionValue(GenTree tree, out AssertionValue value)
            {
                if (!TryGetNormalValueNumber(tree, out var valueNumber))
                {
                    value = default;
                    return false;
                }

                if (TryConvertConstant(valueNumber, out value))
                    return true;

                value = AssertionValue.ForValueNumber(valueNumber);
                return true;
            }

            private bool TryGetNormalValueNumber(GenTree tree, out ValueNumber value)
            {
                if (_valueNumbers.TryGetTreeValue(tree, out var pair))
                {
                    value = _store.VNNormalValue(pair.Conservative);
                    return value.IsValid;
                }

                value = default;
                return false;
            }

            private bool TryConvertConstant(ValueNumber valueNumber, out AssertionValue value)
            {
                while (TryUnwrapRangePreservingIntegralSsaNormalize(valueNumber, out var unwrapped))
                    valueNumber = unwrapped;

                return TryConvertLiteralConstant(valueNumber, out value);
            }

            private bool TryConvertLiteralConstant(ValueNumber valueNumber, out AssertionValue value)
            {
                if (_store.TryGetConstant(valueNumber, out var constant))
                {
                    switch (constant.Kind)
                    {
                        case ValueNumberConstantKind.Int32:
                            value = AssertionValue.ForInt32(unchecked((int)constant.A));
                            return true;
                        case ValueNumberConstantKind.Int64:
                            value = AssertionValue.ForInt64(constant.A);
                            return true;
                        case ValueNumberConstantKind.Null:
                            value = AssertionValue.Null;
                            return true;
                    }
                }

                value = default;
                return false;
            }

            private bool TryUnwrapRangePreservingIntegralSsaNormalize(ValueNumber value, out ValueNumber operand)
            {
                operand = default;
                if (!_store.TryGetEntry(value, out var entry) ||
                    entry.Function != ValueNumberFunction.SsaNormalize ||
                    entry.Args.Length < 1 ||
                    !IsRangePreservingIntegralSsaNormalize(entry, entry.Args[0]))
                {
                    return false;
                }

                operand = _store.VNNormalValue(entry.Args[0]);
                return operand.IsValid;
            }

            private bool IsRangePreservingIntegralSsaNormalize(ValueNumberEntry targetEntry, ValueNumber operand)
            {
                operand = _store.VNNormalValue(operand);
                return operand.IsValid &&
                       _store.TryGetEntry(operand, out var operandEntry) &&
                       TryGetBaseIntegralRange(targetEntry, out var targetRange) &&
                       TryGetBaseIntegralRange(operandEntry, out var operandRange) &&
                       targetRange.Lower == operandRange.Lower &&
                       targetRange.Upper == operandRange.Upper;
            }

            private AssertionValue ZeroFor(GenTree tree)
            {
                bool use64 = tree.StackKind == GenStackKind.I8 ||
                             ((tree.StackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr or GenStackKind.ByRef) &&
                              _method.GenTreeMethod.Target.PointerSize == 8);
                return use64 ? AssertionValue.ForInt64(0) : AssertionValue.ForInt32(0);
            }

            private bool TryCreateRelation(
                SsaAssertionKind kind,
                AssertionValue left,
                AssertionValue right,
                out SsaAssertionDescriptor assertion)
            {
                if (kind == SsaAssertionKind.Subrange)
                {
                    assertion = default;
                    return false;
                }

                if (left.Kind != AssertionValueKind.ValueNumber)
                {
                    if (right.Kind != AssertionValueKind.ValueNumber)
                    {
                        assertion = default;
                        return false;
                    }

                    (left, right) = (right, left);
                    kind = SsaAssertionDescriptor.ReverseKind(kind);
                }

                if (right.Kind == AssertionValueKind.ValueNumber && left.ValueNumber.Id > right.ValueNumber.Id)
                {
                    (left, right) = (right, left);
                    kind = SsaAssertionDescriptor.ReverseKind(kind);
                }

                if (IsZero(right) &&
                    _store.TryGetEntry(left.ValueNumber, out var leftEntry) &&
                    IsObjectReferenceStackKind(leftEntry.StackKind))
                {
                    right = AssertionValue.Null;
                }

                if (!AreAssertionOperandsCompatible(kind, left, right))
                {
                    assertion = default;
                    return false;
                }

                if (IsZero(right))
                {
                    if (kind == SsaAssertionKind.GreaterThanUnsigned)
                        kind = SsaAssertionKind.NotEqual;
                    else if (kind == SsaAssertionKind.LessThanOrEqualUnsigned)
                        kind = SsaAssertionKind.Equal;
                }

                SsaAssertionOperand2Kind operand2Kind;
                ValueNumber operand2Value = default;
                long operand2Constant = 0;

                switch (right.Kind)
                {
                    case AssertionValueKind.ValueNumber:
                        operand2Kind = SsaAssertionOperand2Kind.ValueNumberPlusConstant;
                        operand2Value = right.ValueNumber;
                        operand2Constant = 0;
                        break;
                    case AssertionValueKind.ConstantInt32:
                        operand2Kind = SsaAssertionOperand2Kind.ConstantInt32;
                        operand2Constant = unchecked((int)right.Constant);
                        break;
                    case AssertionValueKind.ConstantInt64:
                        operand2Kind = SsaAssertionOperand2Kind.ConstantInt64;
                        operand2Constant = right.Constant;
                        break;
                    case AssertionValueKind.Null:
                        operand2Kind = SsaAssertionOperand2Kind.Null;
                        break;
                    default:
                        assertion = default;
                        return false;
                }

                assertion = new SsaAssertionDescriptor(
                    kind,
                    SsaAssertionOperand1Kind.ValueNumber,
                    left.ValueNumber,
                    operand2Kind,
                    operand2Value,
                    operand2Constant);
                return true;
            }

            private bool AreAssertionOperandsCompatible(
                SsaAssertionKind kind,
                AssertionValue left,
                AssertionValue right)
            {
                if (left.Kind != AssertionValueKind.ValueNumber ||
                    !_store.TryGetEntry(left.ValueNumber, out var leftEntry))
                {
                    return false;
                }

                if (right.Kind == AssertionValueKind.ValueNumber)
                {
                    if (kind is SsaAssertionKind.Equal or SsaAssertionKind.NotEqual)
                        return AreValueNumbersEqualityCompatible(left.ValueNumber, right.ValueNumber);

                    return AreValueNumbersOrderedCompatible(left.ValueNumber, right.ValueNumber);
                }

                if (right.Kind == AssertionValueKind.Null)
                {
                    if (!IsObjectReferenceStackKind(leftEntry.StackKind))
                        return false;

                    return kind is SsaAssertionKind.Equal or
                        SsaAssertionKind.NotEqual or
                        SsaAssertionKind.LessThanUnsigned or
                        SsaAssertionKind.LessThanOrEqualUnsigned or
                        SsaAssertionKind.GreaterThanUnsigned or
                        SsaAssertionKind.GreaterThanOrEqualUnsigned;
                }

                if (!IsAssertionValueCompatibleWithEntry(leftEntry, right))
                    return false;

                return kind is SsaAssertionKind.Equal or
                    SsaAssertionKind.NotEqual or
                    SsaAssertionKind.LessThan or
                    SsaAssertionKind.LessThanUnsigned or
                    SsaAssertionKind.LessThanOrEqual or
                    SsaAssertionKind.LessThanOrEqualUnsigned or
                    SsaAssertionKind.GreaterThan or
                    SsaAssertionKind.GreaterThanUnsigned or
                    SsaAssertionKind.GreaterThanOrEqual or
                    SsaAssertionKind.GreaterThanOrEqualUnsigned;
            }

            private static bool IsNativePointerStackKind(GenStackKind kind)
                => kind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr or GenStackKind.ByRef;

            private static bool IsZero(AssertionValue value)
                => value.Kind == AssertionValueKind.Null ||
                   ((value.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64) && value.Constant == 0);

            private static bool TryEvaluateConstants(
                SsaAssertionKind kind,
                AssertionValue left,
                AssertionValue right,
                out bool result)
            {
                if (left.Kind == AssertionValueKind.Null || right.Kind == AssertionValueKind.Null)
                {
                    if (kind is not (SsaAssertionKind.Equal or SsaAssertionKind.NotEqual) ||
                        left.Kind != AssertionValueKind.Null ||
                        right.Kind != AssertionValueKind.Null)
                    {
                        result = false;
                        return false;
                    }

                    result = kind == SsaAssertionKind.Equal;
                    return true;
                }

                bool leftInteger = left.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64;
                bool rightInteger = right.Kind is AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64;
                if (!leftInteger || !rightInteger)
                {
                    result = false;
                    return false;
                }

                bool use64 = left.Kind == AssertionValueKind.ConstantInt64 || right.Kind == AssertionValueKind.ConstantInt64;
                long signedLeft = use64 ? left.Constant : unchecked((int)left.Constant);
                long signedRight = use64 ? right.Constant : unchecked((int)right.Constant);
                ulong unsignedLeft = use64 ? unchecked((ulong)signedLeft) : unchecked((uint)signedLeft);
                ulong unsignedRight = use64 ? unchecked((ulong)signedRight) : unchecked((uint)signedRight);

                result = kind switch
                {
                    SsaAssertionKind.Equal => signedLeft == signedRight,
                    SsaAssertionKind.NotEqual => signedLeft != signedRight,
                    SsaAssertionKind.LessThan => signedLeft < signedRight,
                    SsaAssertionKind.LessThanUnsigned => unsignedLeft < unsignedRight,
                    SsaAssertionKind.LessThanOrEqual => signedLeft <= signedRight,
                    SsaAssertionKind.LessThanOrEqualUnsigned => unsignedLeft <= unsignedRight,
                    SsaAssertionKind.GreaterThan => signedLeft > signedRight,
                    SsaAssertionKind.GreaterThanUnsigned => unsignedLeft > unsignedRight,
                    SsaAssertionKind.GreaterThanOrEqual => signedLeft >= signedRight,
                    SsaAssertionKind.GreaterThanOrEqualUnsigned => unsignedLeft >= unsignedRight,
                    _ => false,
                };
                return kind != SsaAssertionKind.Subrange;
            }

            private GenTree CreateConstantTree(GenTree template, AssertionValue constant)
            {
                if (constant.Kind == AssertionValueKind.Null)
                {
                    return new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstNull,
                        template.Pc,
                        BytecodeOp.Ldnull,
                        type: template.Type,
                        stackKind: GenStackKind.Null,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty);
                }

                if (constant.Kind is not (AssertionValueKind.ConstantInt32 or AssertionValueKind.ConstantInt64))
                    throw new InvalidOperationException("Assertion propagation attempted to materialize an unsupported constant.");

                bool use64 = template.StackKind == GenStackKind.I8 ||
                             ((template.StackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr) &&
                              _method.GenTreeMethod.Target.PointerSize == 8) ||
                             constant.Kind == AssertionValueKind.ConstantInt64;

                if (use64)
                {
                    return new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI8,
                        template.Pc,
                        BytecodeOp.Ldc_I8,
                        type: null,
                        stackKind: template.StackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr
                            ? template.StackKind
                            : GenStackKind.I8,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int64: constant.Kind == AssertionValueKind.ConstantInt32
                            ? unchecked((int)constant.Constant)
                            : constant.Constant);
                }

                return new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.ConstI4,
                    template.Pc,
                    BytecodeOp.Ldc_I4,
                    type: null,
                    stackKind: template.StackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr
                        ? template.StackKind
                        : GenStackKind.I4,
                    flags: GenTreeFlags.None,
                    operands: ImmutableArray<GenTree>.Empty,
                    int32: unchecked((int)constant.Constant));
            }

            private GenTree CreateBooleanConstant(GenTree template, bool value)
                => new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.ConstI4,
                    template.Pc,
                    BytecodeOp.Ldc_I4,
                    type: null,
                    stackKind: GenStackKind.I4,
                    flags: GenTreeFlags.None,
                    operands: ImmutableArray<GenTree>.Empty,
                    int32: value ? 1 : 0);

            private static GenTree CloneSource(GenTree source, ImmutableArray<SsaTree> operands, GenTreeFlags flags)
                => CloneSource(source, operands, flags, source.SourceOp);

            private static GenTree CloneSource(GenTree source, ImmutableArray<SsaTree> operands, GenTreeFlags flags, BytecodeOp sourceOp)
            {
                var genOperands = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    genOperands.Add(operands[i].Source);

                return new GenTree(
                    source.Id,
                    source.Kind,
                    source.Pc,
                    sourceOp,
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
                    source.TargetBlockId);
            }
        }
    }
}
