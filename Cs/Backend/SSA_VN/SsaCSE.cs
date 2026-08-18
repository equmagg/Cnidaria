using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class SsaCseResult
    {
        public SsaMethod Method { get; }
        public bool Changed { get; }
        public int NextSyntheticTreeId { get; }

        public SsaCseResult(SsaMethod method, bool changed, int nextSyntheticTreeId)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Changed = changed;
            NextSyntheticTreeId = nextSyntheticTreeId;
        }
    }

    internal static class SsaCommonSubexpressionEliminator
    {
        private const int MaxCandidateCount = 64;

        public static SsaCseResult OptimizeMethod(SsaMethod method, SsaOptimizationOptions options, int nextSyntheticTreeId)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (method.ValueNumbers is null || method.Blocks.Length == 0)
                return new SsaCseResult(method, changed: false, nextSyntheticTreeId);

            return new Optimizer(method, options, nextSyntheticTreeId).Run();
        }

        private readonly struct CseKey : IEquatable<CseKey>
        {
            public readonly ValueNumber Value;
            public readonly ValueNumberType Type;

            public CseKey(ValueNumber value, GenStackKind stackKind, RuntimeType? runtimeType)
            {
                Value = value;
                Type = ValueNumberType.For(stackKind, runtimeType);
            }

            public bool Equals(CseKey other) => Value == other.Value && Type.Equals(other.Type);
            public override bool Equals(object? obj) => obj is CseKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Value, Type);
        }

        private sealed class Candidate
        {
            public readonly int Index;
            public readonly CseKey Key;
            public readonly ValueNumberPair NormalValue;
            public readonly List<Occurrence> Occurrences = new List<Occurrence>();
            public bool Selected;
            public bool ForceCse;
            public bool LiveAcrossCall;
            public GenLocalDescriptor? TempDescriptor;
            public int Cost;
            public int UseCount;
            public int DefCount;
            public double WeightedUseCount;
            public double WeightedDefCount;
            public double EstimatedNoCseCost;
            public double EstimatedYesCseCost;

            public Candidate(int index, CseKey key, ValueNumberPair normalValue)
            {
                Index = index;
                Key = key;
                NormalValue = normalValue;
            }
        }

        private sealed class Occurrence
        {
            public readonly Candidate Candidate;
            public readonly SsaBlock Block;
            public readonly int BlockId;
            public readonly int StatementIndex;
            public readonly int TreeIndex;
            public readonly GenTree Node;
            public readonly ValueNumber ExceptionSet;
            public readonly double Weight;
            public bool IsDef;
            public bool IsUse;

            public Occurrence(
                Candidate candidate,
                SsaBlock block,
                int statementIndex,
                int treeIndex,
                GenTree node,
                ValueNumber exceptionSet,
                double weight)
            {
                Candidate = candidate;
                Block = block;
                BlockId = block.Id;
                StatementIndex = statementIndex;
                TreeIndex = treeIndex;
                Node = node;
                ExceptionSet = exceptionSet;
                Weight = Math.Max(1.0, weight);
            }
        }

        private sealed class AvailabilityState
        {
            public readonly HashSet<Candidate> Available;
            public readonly HashSet<Candidate> AvailableAcrossCall;

            public AvailabilityState()
            {
                Available = new HashSet<Candidate>();
                AvailableAcrossCall = new HashSet<Candidate>();
            }

            public AvailabilityState(IEnumerable<Candidate> candidates)
            {
                Available = new HashSet<Candidate>(candidates);
                AvailableAcrossCall = new HashSet<Candidate>(candidates);
            }

            private AvailabilityState(HashSet<Candidate> available, HashSet<Candidate> availableAcrossCall)
            {
                Available = available;
                AvailableAcrossCall = availableAcrossCall;
            }

            public AvailabilityState Clone()
                => new AvailabilityState(new HashSet<Candidate>(Available), new HashSet<Candidate>(AvailableAcrossCall));

            public bool SetEquals(AvailabilityState other)
                => Available.SetEquals(other.Available) && AvailableAcrossCall.SetEquals(other.AvailableAcrossCall);

            public void IntersectWith(AvailabilityState other)
            {
                Available.IntersectWith(other.Available);
                AvailableAcrossCall.IntersectWith(other.AvailableAcrossCall);
            }
        }

        private readonly struct RefThresholds
        {
            public readonly double Aggressive;
            public readonly double Moderate;

            public RefThresholds(double aggressive, double moderate)
            {
                Aggressive = aggressive;
                Moderate = moderate;
            }
        }

        private enum PromotionKind : byte
        {
            Aggressive,
            Moderate,
            Conservative,
        }

        private readonly struct PendingStore
        {
            public readonly int TreeIndex;
            public readonly GenTree Store;

            public PendingStore(int treeIndex, GenTree store)
            {
                TreeIndex = treeIndex;
                Store = store;
            }
        }

        private sealed class Optimizer
        {
            private readonly SsaMethod _method;
            private readonly SsaOptimizationOptions _options;
            private readonly TargetInfo _target;
            private readonly SsaValueNumberingResult _vn;
            private readonly Dictionary<CseKey, Candidate> _candidateByKey = new Dictionary<CseKey, Candidate>();
            private readonly List<Candidate> _candidates = new List<Candidate>();
            private readonly Dictionary<GenTree, Occurrence> _occurrenceByNode = new Dictionary<GenTree, Occurrence>(ReferenceEqualityComparer<GenTree>.Instance);
            private readonly RefThresholds _generalThresholds;
            private readonly RefThresholds _floatThresholds;
            private int _nextSyntheticTreeId;

            public Optimizer(SsaMethod method, SsaOptimizationOptions options, int nextSyntheticTreeId)
            {
                _method = method;
                _options = options;
                _target = method.GenTreeMethod.Target;
                _vn = method.ValueNumbers!;
                _nextSyntheticTreeId = Math.Max(nextSyntheticTreeId, MaxTreeId(method) + 1);
                _generalThresholds = BuildRefThresholds(RegisterClass.General);
                _floatThresholds = BuildRefThresholds(RegisterClass.Float);
            }

            public SsaCseResult Run()
            {
                LocateCandidates();
                RemoveSingletonCandidates();
                if (_candidates.Count == 0)
                    return new SsaCseResult(_method, changed: false, _nextSyntheticTreeId);

                ComputeAvailability();
                if (!SelectCandidates())
                    return new SsaCseResult(_method, changed: false, _nextSyntheticTreeId);

                return new SsaCseResult(RewriteSelectedCandidates(), changed: true, _nextSyntheticTreeId);
            }

            private void LocateCandidates()
            {
                SeedForcedCandidates();

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    double blockWeight = BlockWeight(block.Id);
                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        var item = block.TreeList[i];
                        var node = item.Tree.Source;
                        if (node.Parent is null || !SsaCseCandidatePolicy.CanConsider(node))
                            continue;
                        if (!_vn.TryGetTreeValue(node, out var pair))
                            continue;

                        var liberalNormal = _vn.Store.VNNormalValue(pair.Liberal);
                        if (!liberalNormal.IsValid || _vn.Store.TryGetConstant(liberalNormal, out _))
                            continue;

                        var conservativeNormal = _vn.Store.VNNormalValue(pair.Conservative);
                        if (!conservativeNormal.IsValid)
                            conservativeNormal = liberalNormal;

                        var key = new CseKey(liberalNormal, node.StackKind, node.Type);
                        bool forceCse = (node.Flags & GenTreeFlags.MakeCse) != 0;
                        if (!_candidateByKey.TryGetValue(key, out var candidate))
                        {
                            if (_candidates.Count >= MaxCandidateCount && !forceCse)
                                continue;

                            candidate = new Candidate(
                                _candidates.Count + 1,
                                key,
                                new ValueNumberPair(liberalNormal, conservativeNormal));
                            _candidateByKey.Add(key, candidate);
                            _candidates.Add(candidate);
                        }

                        candidate.ForceCse |= forceCse;

                        var exceptionSet = _vn.Store.VNExceptionSet(pair.Conservative);
                        var occurrence = new Occurrence(
                            candidate,
                            block,
                            item.StatementIndex,
                            item.TreeIndex,
                            node,
                            exceptionSet,
                            blockWeight);
                        candidate.Occurrences.Add(occurrence);
                        _occurrenceByNode[node] = occurrence;
                    }
                }
            }

            private void SeedForcedCandidates()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        var node = block.TreeList[i].Tree.Source;
                        if (node.Parent is null ||
                            (node.Flags & GenTreeFlags.MakeCse) == 0 ||
                            !SsaCseCandidatePolicy.CanConsider(node) ||
                            !_vn.TryGetTreeValue(node, out var pair))
                        {
                            continue;
                        }

                        var liberalNormal = _vn.Store.VNNormalValue(pair.Liberal);
                        if (!liberalNormal.IsValid || _vn.Store.TryGetConstant(liberalNormal, out _))
                            continue;

                        var conservativeNormal = _vn.Store.VNNormalValue(pair.Conservative);
                        if (!conservativeNormal.IsValid)
                            conservativeNormal = liberalNormal;

                        var key = new CseKey(liberalNormal, node.StackKind, node.Type);
                        if (!_candidateByKey.TryGetValue(key, out var candidate))
                        {
                            candidate = new Candidate(
                                _candidates.Count + 1,
                                key,
                                new ValueNumberPair(liberalNormal, conservativeNormal));
                            _candidateByKey.Add(key, candidate);
                            _candidates.Add(candidate);
                        }

                        candidate.ForceCse = true;
                    }
                }
            }

            private void RemoveSingletonCandidates()
            {
                for (int i = _candidates.Count - 1; i >= 0; i--)
                {
                    var candidate = _candidates[i];
                    if (candidate.Occurrences.Count >= 2)
                        continue;

                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                        _occurrenceByNode.Remove(candidate.Occurrences[o].Node);
                    _candidateByKey.Remove(candidate.Key);
                    _candidates.RemoveAt(i);
                }
            }

            private void ComputeAvailability()
            {
                var inStates = new AvailabilityState[_method.Blocks.Length];
                var outStates = new AvailabilityState[_method.Blocks.Length];
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    bool boundary = IsAvailabilityBoundary(b);
                    inStates[b] = boundary ? new AvailabilityState() : new AvailabilityState(_candidates);
                    outStates[b] = boundary ? TransferBlock(b, inStates[b], mark: false) : new AvailabilityState(_candidates);
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int r = 0; r < _method.Cfg.ReversePostOrder.Length; r++)
                    {
                        int blockId = _method.Cfg.ReversePostOrder[r];
                        if ((uint)blockId >= (uint)_method.Blocks.Length)
                            continue;

                        var input = ComputeBlockInput(blockId, outStates);
                        if (!input.SetEquals(inStates[blockId]))
                        {
                            inStates[blockId] = input;
                            changed = true;
                        }

                        var output = TransferBlock(blockId, input, mark: false);
                        if (!output.SetEquals(outStates[blockId]))
                        {
                            outStates[blockId] = output;
                            changed = true;
                        }
                    }
                }
                while (changed);

                ResetOccurrenceClassification();
                for (int b = 0; b < _method.Blocks.Length; b++)
                    TransferBlock(b, inStates[b], mark: true);
            }

            private AvailabilityState ComputeBlockInput(int blockId, AvailabilityState[] outStates)
            {
                if (IsAvailabilityBoundary(blockId))
                    return new AvailabilityState();

                var preds = _method.Cfg.Blocks[blockId].Predecessors;
                if (preds.Length == 0)
                    return new AvailabilityState();

                AvailabilityState? result = null;
                for (int p = 0; p < preds.Length; p++)
                {
                    int predId = preds[p].FromBlockId;
                    if ((uint)predId >= (uint)outStates.Length)
                        return new AvailabilityState();

                    if (result is null)
                        result = outStates[predId].Clone();
                    else
                        result.IntersectWith(outStates[predId]);
                }

                return result ?? new AvailabilityState();
            }

            private AvailabilityState TransferBlock(int blockId, AvailabilityState input, bool mark)
            {
                var current = input.Clone();
                var list = _method.Blocks[blockId].TreeList;
                for (int i = 0; i < list.Length; i++)
                {
                    var node = list[i].Tree.Source;
                    if (IsCallBoundary(node))
                        current.AvailableAcrossCall.Clear();

                    if (!_occurrenceByNode.TryGetValue(node, out var occurrence))
                        continue;

                    var candidate = occurrence.Candidate;
                    if (current.Available.Contains(candidate))
                    {
                        if (mark)
                        {
                            occurrence.IsUse = true;
                            candidate.UseCount++;
                            candidate.WeightedUseCount += occurrence.Weight;
                            if (!current.AvailableAcrossCall.Contains(candidate))
                                candidate.LiveAcrossCall = true;
                        }
                    }
                    else
                    {
                        if (mark)
                        {
                            occurrence.IsDef = true;
                            candidate.DefCount++;
                            candidate.WeightedDefCount += occurrence.Weight;
                        }
                        current.Available.Add(candidate);
                    }

                    current.AvailableAcrossCall.Add(candidate);
                }

                return current;
            }

            private void ResetOccurrenceClassification()
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    candidate.UseCount = 0;
                    candidate.DefCount = 0;
                    candidate.WeightedUseCount = 0;
                    candidate.WeightedDefCount = 0;
                    candidate.LiveAcrossCall = false;
                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                    {
                        candidate.Occurrences[o].IsDef = false;
                        candidate.Occurrences[o].IsUse = false;
                    }
                }
            }

            private bool IsAvailabilityBoundary(int blockId)
            {
                var block = _method.Cfg.Blocks[blockId];
                if (block.Predecessors.Length == 0)
                    return true;

                for (int i = 0; i < block.Predecessors.Length; i++)
                {
                    if (block.Predecessors[i].Kind == CfgEdgeKind.Exception)
                        return true;
                }

                return false;
            }

            private bool SelectCandidates()
            {
                var selectable = new List<Candidate>();
                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (candidate.UseCount == 0 || candidate.DefCount == 0)
                        continue;
                    if (!HasCompatibleExceptionSets(candidate))
                        continue;
                    if (!AllDefinitionOccurrencesCanBeMaterialized(candidate))
                        continue;

                    candidate.Cost = SsaCseCandidatePolicy.EstimateCost(candidate.Occurrences[0].Node);
                    if (!PassesProfitabilityCheck(candidate))
                        continue;

                    selectable.Add(candidate);
                }

                selectable.Sort(static (left, right) =>
                {
                    int c = right.ForceCse.CompareTo(left.ForceCse);
                    if (c != 0)
                        return c;
                    c = right.Cost.CompareTo(left.Cost);
                    if (c != 0)
                        return c;
                    c = right.WeightedUseCount.CompareTo(left.WeightedUseCount);
                    if (c != 0)
                        return c;
                    c = left.WeightedDefCount.CompareTo(right.WeightedDefCount);
                    if (c != 0)
                        return c;
                    return left.Index.CompareTo(right.Index);
                });

                var occupied = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                bool any = false;
                for (int i = 0; i < selectable.Count; i++)
                {
                    var candidate = selectable[i];
                    if (ConflictsWithSelected(candidate, occupied))
                        continue;

                    candidate.Selected = true;
                    any = true;
                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                        AddSubtree(candidate.Occurrences[o].Node, occupied);
                }

                return any;
            }

            private bool HasCompatibleExceptionSets(Candidate candidate)
            {
                ValueNumber required = _vn.Store.VNForEmptyExcSet();
                for (int i = 0; i < candidate.Occurrences.Count; i++)
                {
                    var occurrence = candidate.Occurrences[i];
                    if (!occurrence.IsUse || _vn.Store.IsEmptyExcSet(occurrence.ExceptionSet))
                        continue;

                    if (_vn.Store.IsEmptyExcSet(required))
                        required = occurrence.ExceptionSet;
                    else if (required != occurrence.ExceptionSet)
                        return false;
                }

                if (_vn.Store.IsEmptyExcSet(required))
                    return true;

                for (int i = 0; i < candidate.Occurrences.Count; i++)
                {
                    var occurrence = candidate.Occurrences[i];
                    if (occurrence.IsDef && occurrence.ExceptionSet != required)
                        return false;
                    if (occurrence.IsUse && !_vn.Store.IsEmptyExcSet(occurrence.ExceptionSet) && occurrence.ExceptionSet != required)
                        return false;
                }

                return true;
            }

            private bool PassesProfitabilityCheck(Candidate candidate)
            {
                if (candidate.UseCount <= 0 || candidate.DefCount <= 0)
                    return false;
                if (candidate.ForceCse)
                    return true;
                if (candidate.Cost <= 1)
                    return false;

                var representative = candidate.Occurrences[0].Node;
                bool canEnregister = CanEnregisterCse(representative);
                PromotionKind promotion = ClassifyPromotion(candidate, representative, canEnregister);
                EstimateCseAccessCosts(candidate, promotion, canEnregister, out int cseDefCost, out int cseUseCost, out double extraYesCost);

                double noCseCost = candidate.WeightedUseCount * candidate.Cost;
                double yesCseCost = candidate.WeightedDefCount * cseDefCost +
                                    candidate.WeightedUseCount * cseUseCost +
                                    extraYesCost;

                candidate.EstimatedNoCseCost = noCseCost;
                candidate.EstimatedYesCseCost = yesCseCost;
                return noCseCost > yesCseCost;
            }

            private PromotionKind ClassifyPromotion(Candidate candidate, GenTree representative, bool canEnregister)
            {
                if (!canEnregister)
                    return PromotionKind.Conservative;

                double refCount = candidate.WeightedDefCount * 2.0 + candidate.WeightedUseCount;
                RefThresholds thresholds = IsFloatStackKind(representative.StackKind) ? _floatThresholds : _generalThresholds;
                if (refCount >= thresholds.Aggressive)
                    return PromotionKind.Aggressive;
                if (refCount >= thresholds.Moderate)
                    return PromotionKind.Moderate;
                return PromotionKind.Conservative;
            }

            private static void EstimateCseAccessCosts(
                Candidate candidate,
                PromotionKind promotion,
                bool canEnregister,
                out int cseDefCost,
                out int cseUseCost,
                out double extraYesCost)
            {
                extraYesCost = 0;
                if (!canEnregister)
                {
                    cseDefCost = 2;
                    cseUseCost = 3;
                    return;
                }

                switch (promotion)
                {
                    case PromotionKind.Aggressive:
                        cseDefCost = 1;
                        cseUseCost = 1;
                        break;
                    case PromotionKind.Moderate:
                        cseDefCost = 2;
                        cseUseCost = 1;
                        break;
                    default:
                        cseDefCost = 2;
                        cseUseCost = 2;
                        break;
                }

                if (candidate.LiveAcrossCall && promotion == PromotionKind.Conservative)
                    extraYesCost = Math.Max(1.0, candidate.WeightedDefCount * 0.5);
            }

            private RefThresholds BuildRefThresholds(RegisterClass registerClass)
            {
                var weights = new List<double>();
                var descriptors = _method.GenTreeMethod.AllLocalDescriptors;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (!descriptor.LRACandidate || descriptor.DoNotEnregister || descriptor.HasMemoryAlias)
                        continue;
                    if (RegisterClassOf(descriptor.StackKind) != registerClass)
                        continue;

                    double weight = descriptor.WeightedDefCount * 2.0 + descriptor.WeightedUseCount;
                    if (weight > 0)
                        weights.Add(weight);
                }

                weights.Sort(static (left, right) => right.CompareTo(left));
                var allocatable = registerClass == RegisterClass.Float
                    ? RegisterInfo.AllocatableFloatingRegisters(_target)
                    : RegisterInfo.AllocatableGeneralRegisters(_target);

                int calleeSaved = 0;
                int callerSaved = 0;
                for (int i = 0; i < allocatable.Length; i++)
                {
                    if (RegisterInfo.IsCalleeSaved(_target, allocatable[i]))
                        calleeSaved++;
                    else if (RegisterInfo.IsCallerSaved(_target, allocatable[i]))
                        callerSaved++;
                }

                int aggressiveCount = Math.Max(1, calleeSaved * 3 / 2);
                int moderateCount = Math.Max(1, calleeSaved * 3 + callerSaved * 2);
                double aggressive = WeightAt(weights, aggressiveCount, addUnity: true);
                double moderate = WeightAt(weights, moderateCount, addUnity: false);
                int multiplier = weights.Count > 4 ? 3 : weights.Count > 2 ? 2 : 1;
                aggressive = Math.Max(multiplier, aggressive);
                moderate = Math.Max(multiplier * 0.5, moderate);
                if (moderate > aggressive)
                    moderate = aggressive;
                return new RefThresholds(aggressive, moderate);
            }

            private static double WeightAt(List<double> weights, int oneBasedIndex, bool addUnity)
            {
                if (oneBasedIndex <= 0 || oneBasedIndex > weights.Count)
                    return 0;
                double value = weights[oneBasedIndex - 1];
                return addUnity ? value + 1.0 : value;
            }

            private bool CanEnregisterCse(GenTree node)
            {
                if (node.StackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value or GenStackKind.Null or GenStackKind.ByRef)
                    return false;

                if (node.Type is not null)
                    return MachineAbi.IsPhysicallyPromotableStorage(node.Type, node.StackKind, _target);

                return RegisterClassOf(node.StackKind) is RegisterClass.General or RegisterClass.Float;
            }

            private static RegisterClass RegisterClassOf(GenStackKind stackKind)
                => IsFloatStackKind(stackKind) ? RegisterClass.Float :
                   stackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ref or GenStackKind.Ptr
                       ? RegisterClass.General
                       : RegisterClass.Invalid;

            private static bool IsFloatStackKind(GenStackKind stackKind)
                => stackKind is GenStackKind.R4 or GenStackKind.R8;

            private double BlockWeight(int blockId)
            {
                int depth = 0;
                for (int i = 0; i < _method.Cfg.NaturalLoops.Length; i++)
                {
                    if (_method.Cfg.NaturalLoops[i].Contains(blockId))
                        depth++;
                }

                double weight = 1.0;
                for (int i = 0; i < depth; i++)
                    weight *= 8.0;
                return weight;
            }

            private static bool IsCallBoundary(GenTree node)
            {
                return node.Kind is
                    GenTreeKind.Intrinsic or
                    GenTreeKind.Call or
                    GenTreeKind.IndirectCall or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewDelegate or
                    GenTreeKind.DelegateCombine or
                    GenTreeKind.DelegateRemove or
                    GenTreeKind.DelegateInvoke or
                    GenTreeKind.GcPoll;
            }

            private static bool AllDefinitionOccurrencesCanBeMaterialized(Candidate candidate)
            {
                for (int i = 0; i < candidate.Occurrences.Count; i++)
                {
                    var occurrence = candidate.Occurrences[i];
                    if (occurrence.IsDef && !CanMaterializeDefinitionOccurrence(occurrence))
                        return false;
                }
                return true;
            }

            private static bool CanMaterializeDefinitionOccurrence(Occurrence occurrence)
            {
                if ((uint)occurrence.StatementIndex >= (uint)occurrence.Block.Statements.Length)
                    return false;

                var statement = occurrence.Block.Statements[occurrence.StatementIndex].Source;
                if (occurrence.Node.Parent is null || !CanExtractFromStatementKind(statement.Kind))
                    return false;

                var statementTreeList = occurrence.Block.StatementTreeLists[occurrence.StatementIndex];
                int occurrenceIndex = -1;
                for (int i = 0; i < statementTreeList.Length; i++)
                {
                    if (ReferenceEquals(statementTreeList[i].Source, occurrence.Node))
                    {
                        occurrenceIndex = i;
                        break;
                    }
                }

                if (occurrenceIndex < 0)
                    return false;

                var subtree = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                AddSubtree(occurrence.Node, subtree);
                int subtreeStart = occurrenceIndex;
                for (int i = 0; i < occurrenceIndex; i++)
                {
                    if (subtree.Contains(statementTreeList[i].Source))
                        subtreeStart = Math.Min(subtreeStart, i);
                }

                bool movingCanThrow = (occurrence.Node.Flags & GenTreeFlags.CanThrow) != 0;
                for (int i = 0; i < subtreeStart; i++)
                {
                    if (BlocksExtractionBefore(statementTreeList[i].Source, movingCanThrow))
                        return false;
                }

                return true;
            }

            private static bool CanExtractFromStatementKind(GenTreeKind kind)
            {
                return kind is
                    GenTreeKind.StoreLocal or
                    GenTreeKind.StoreArg or
                    GenTreeKind.StoreTemp or
                    GenTreeKind.StoreField or
                    GenTreeKind.StoreStaticField or
                    GenTreeKind.StoreArrayElement or
                    GenTreeKind.StoreIndirect or
                    GenTreeKind.Eval or
                    GenTreeKind.Return or
                    GenTreeKind.BranchTrue or
                    GenTreeKind.BranchFalse;
            }

            private static bool BlocksExtractionBefore(GenTree node, bool movingCanThrow)
            {
                if ((node.Flags & (GenTreeFlags.ContainsCall |
                                   GenTreeFlags.SideEffect |
                                   GenTreeFlags.MemoryWrite |
                                   GenTreeFlags.LocalDef |
                                   GenTreeFlags.AddressExposed |
                                   GenTreeFlags.Allocation |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.ExceptionFlow |
                                   GenTreeFlags.Ordered)) != 0)
                    return true;

                return movingCanThrow && (node.Flags & GenTreeFlags.CanThrow) != 0;
            }

            private static bool ConflictsWithSelected(Candidate candidate, HashSet<GenTree> occupied)
            {
                for (int i = 0; i < candidate.Occurrences.Count; i++)
                {
                    var node = candidate.Occurrences[i].Node;
                    if (HasOccupiedSubtree(node, occupied))
                        return true;
                    for (var parent = node.Parent; parent is not null; parent = parent.Parent)
                    {
                        if (occupied.Contains(parent))
                            return true;
                    }
                }
                return false;
            }

            private static bool HasOccupiedSubtree(GenTree node, HashSet<GenTree> occupied)
            {
                if (occupied.Contains(node))
                    return true;
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (HasOccupiedSubtree(node.Operands[i], occupied))
                        return true;
                }
                return false;
            }

            private static void AddSubtree(GenTree node, HashSet<GenTree> occupied)
            {
                occupied.Add(node);
                for (int i = 0; i < node.Operands.Length; i++)
                    AddSubtree(node.Operands[i], occupied);
            }

            private SsaMethod RewriteSelectedCandidates()
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (!candidate.Selected)
                        continue;

                    candidate.TempDescriptor = _method.GenTreeMethod.AppendCompilerTemp(
                        GenTempKind.CommonSubexpression,
                        candidate.Occurrences[0].Node.Type,
                        candidate.Occurrences[0].Node.StackKind);
                }

                var statements = RewriteTrees();
                var rewritten = _method.GenTreeMethod.CloneWithBlocks(MaterializeGenTreeBlocks(statements));
                bool includeExceptionEdges = HasExceptionEdges(_method.Cfg);
                var cfg = ControlFlowGraph.Build(rewritten, includeExceptionEdges);
                rewritten.AttachFlowGraph(cfg);
                var liveness = GenTreeLocalLiveness.Build(rewritten, cfg);
                rewritten.AttachHirLiveness(liveness);
                var rebuilt = GenTreeSsaBuilder.BuildMethod(rewritten, cfg, liveness, validate: _options.Validate);
                return SsaValueNumbering.BuildMethod(rebuilt, validate: _options.Validate);
            }

            private ImmutableArray<GenTree>[] RewriteTrees()
            {
                var result = new ImmutableArray<GenTree>[_method.Blocks.Length];
                var replacementByNode = new Dictionary<GenTree, GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                var storesBeforeStatement = new List<PendingStore>[_method.Blocks.Length][];
                for (int b = 0; b < _method.Blocks.Length; b++)
                    storesBeforeStatement[b] = new List<PendingStore>[_method.Blocks[b].Statements.Length];

                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (!candidate.Selected || candidate.TempDescriptor is null)
                        continue;

                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                    {
                        var occurrence = candidate.Occurrences[o];
                        if (occurrence.IsDef)
                        {
                            replacementByNode[occurrence.Node] = CreateUse(candidate, occurrence);
                            var blockStores = storesBeforeStatement[occurrence.BlockId];
                            var statementStores = blockStores[occurrence.StatementIndex];
                            if (statementStores is null)
                            {
                                statementStores = new List<PendingStore>();
                                blockStores[occurrence.StatementIndex] = statementStores;
                            }
                            statementStores.Add(new PendingStore(occurrence.TreeIndex, CreateStore(candidate, occurrence)));
                        }
                        else if (occurrence.IsUse)
                        {
                            replacementByNode[occurrence.Node] = CreateUse(candidate, occurrence);
                        }
                    }
                }

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var oldBlock = _method.Blocks[b];
                    int inserted = 0;
                    for (int s = 0; s < oldBlock.Statements.Length; s++)
                        inserted += storesBeforeStatement[b][s]?.Count ?? 0;

                    var rewritten = ImmutableArray.CreateBuilder<GenTree>(oldBlock.Statements.Length + inserted);
                    for (int s = 0; s < oldBlock.Statements.Length; s++)
                    {
                        var stores = storesBeforeStatement[b][s];
                        if (stores is not null)
                        {
                            stores.Sort(static (left, right) => left.TreeIndex.CompareTo(right.TreeIndex));
                            for (int i = 0; i < stores.Count; i++)
                            {
                                RefreshFlags(stores[i].Store);
                                rewritten.Add(stores[i].Store);
                            }
                        }

                        var source = RewriteNode(oldBlock.Statements[s].Source, replacementByNode);
                        RefreshFlags(source);
                        rewritten.Add(source);
                    }
                    result[b] = rewritten.ToImmutable();
                }

                return result;
            }

            private ImmutableArray<GenTreeBlock> MaterializeGenTreeBlocks(ImmutableArray<GenTree>[] statementsByBlock)
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                for (int b = 0; b < _method.GenTreeMethod.Blocks.Length; b++)
                {
                    var oldBlock = _method.GenTreeMethod.Blocks[b];
                    blocks.Add(new GenTreeBlock(
                        oldBlock.Id,
                        oldBlock.StartPc,
                        oldBlock.EndPcExclusive,
                        oldBlock.EntryStackDepth,
                        oldBlock.ExitStackDepth,
                        oldBlock.JumpKind,
                        oldBlock.Flags,
                        statementsByBlock[b],
                        oldBlock.SuccessorBlockIds,
                        oldBlock.SuccessorPcs));
                }
                return blocks.ToImmutable();
            }

            private GenTree CreateStore(Candidate candidate, Occurrence occurrence)
            {
                var temp = candidate.TempDescriptor!;
                occurrence.Node.Flags &= ~GenTreeFlags.MakeCse;
                var store = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.StoreTemp,
                    occurrence.Node.Pc,
                    occurrence.Node.SourceOp,
                    temp.Type,
                    temp.StackKind,
                    (occurrence.Node.Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.ExplicitInit)) |
                    GenTreeFlags.SideEffect |
                    GenTreeFlags.LocalDef |
                    GenTreeFlags.Ordered,
                    ImmutableArray.Create(occurrence.Node),
                    int32: temp.Index);
                store.LocalDescriptor = temp;
                store.CseNumber = EncodeCseDef(candidate.Index);
                return store;
            }

            private GenTree CreateUse(Candidate candidate, Occurrence occurrence)
            {
                var temp = candidate.TempDescriptor!;
                var use = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Temp,
                    occurrence.Node.Pc,
                    occurrence.Node.SourceOp,
                    temp.Type,
                    temp.StackKind,
                    GenTreeFlags.LocalUse,
                    ImmutableArray<GenTree>.Empty,
                    int32: temp.Index);
                use.LocalDescriptor = temp;
                use.CseNumber = EncodeCseUse(candidate.Index);
                return use;
            }

            private static GenTree RewriteNode(GenTree node, Dictionary<GenTree, GenTree> replacements)
            {
                if (replacements.TryGetValue(node, out var replacement))
                    return replacement;
                if (node.Operands.Length == 0)
                    return node;

                bool changed = false;
                var operands = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var oldOperand = node.Operands[i];
                    var newOperand = RewriteNode(oldOperand, replacements);
                    operands.Add(newOperand);
                    changed |= !ReferenceEquals(oldOperand, newOperand);
                }

                if (changed)
                    node.SetOperands(operands.ToImmutable());
                return node;
            }

            private static bool HasExceptionEdges(ControlFlowGraph cfg)
            {
                for (int b = 0; b < cfg.Blocks.Length; b++)
                {
                    var successors = cfg.Blocks[b].Successors;
                    for (int s = 0; s < successors.Length; s++)
                    {
                        if (successors[s].Kind == CfgEdgeKind.Exception)
                            return true;
                    }
                }
                return false;
            }

            private static int EncodeCseDef(int index) => index << 1;
            private static int EncodeCseUse(int index) => (index << 1) | 1;

            private static void RefreshFlags(GenTree node)
            {
                for (int i = 0; i < node.Operands.Length; i++)
                    RefreshFlags(node.Operands[i]);

                var flags = node.Kind == GenTreeKind.Eval
                    ? node.Flags & (GenTreeFlags.Prolog | GenTreeFlags.AssertionProperties | GenTreeFlags.MakeCse)
                    : node.Flags;
                for (int i = 0; i < node.Operands.Length; i++)
                    flags |= node.Operands[i].Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.ExplicitInit);
                node.Flags = flags;
            }

            private static int MaxTreeId(SsaMethod method)
            {
                int max = 0;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    for (int s = 0; s < block.Statements.Length; s++)
                        Visit(block.Statements[s].Source);
                }
                return max;

                void Visit(GenTree node)
                {
                    if (node.Id > max)
                        max = node.Id;
                    for (int i = 0; i < node.Operands.Length; i++)
                        Visit(node.Operands[i]);
                }
            }
        }
    }
}
