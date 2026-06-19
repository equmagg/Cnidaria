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
        public static SsaCseResult OptimizeMethod(SsaMethod method, SsaOptimizationOptions options, int nextSyntheticTreeId)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (method.ValueNumbers is null || method.Blocks.Length == 0)
                return new SsaCseResult(method, changed: false, nextSyntheticTreeId);

            var optimizer = new Optimizer(method, nextSyntheticTreeId);
            return optimizer.Run();
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
            public GenLocalDescriptor? TempDescriptor;
            public SsaSlot TempSlot;
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
            public readonly double Weight;
            public bool IsDef;
            public bool IsUse;
            public Occurrence? ReachingDef;
            public int SsaVersion;
            public SsaValueName SsaName;

            public Occurrence(Candidate candidate, SsaBlock block, int statementIndex, int treeIndex, GenTree node, double weight)
            {
                Candidate = candidate;
                Block = block;
                BlockId = block.Id;
                StatementIndex = statementIndex;
                TreeIndex = treeIndex;
                Node = node;
                Weight = Math.Max(1.0, weight);
            }
        }

        private sealed class Optimizer
        {
            private readonly SsaMethod _method;
            private readonly SsaValueNumberingResult _vn;
            private readonly Dictionary<CseKey, Candidate> _candidateByKey = new Dictionary<CseKey, Candidate>();
            private readonly List<Candidate> _candidates = new List<Candidate>();
            private readonly Dictionary<GenTree, Occurrence> _occurrenceByNode = new Dictionary<GenTree, Occurrence>(ReferenceEqualityComparer<GenTree>.Instance);
            private int _nextSyntheticTreeId;

            public Optimizer(SsaMethod method, int nextSyntheticTreeId)
            {
                _method = method;
                _vn = method.ValueNumbers!;
                _nextSyntheticTreeId = Math.Max(nextSyntheticTreeId, MaxTreeId(method) + 1);
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

                var rewritten = RewriteSelectedCandidates();
                return new SsaCseResult(rewritten, changed: true, _nextSyntheticTreeId);
            }

            private void LocateCandidates()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    double blockWeight = BlockWeight(block.Id);
                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        var item = block.TreeList[i];
                        var node = item.Tree.Source;
                        if (node.Parent is null)
                            continue;
                        if (!CanConsider(node))
                            continue;
                        if (!_vn.TryGetTreeValue(node, out var pair))
                            continue;

                        var liberalNormal = _vn.Store.VNNormalValue(pair.Liberal);
                        if (!liberalNormal.IsValid)
                            continue;
                        if (_vn.Store.TryGetConstant(liberalNormal, out _))
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

                        var occurrence = new Occurrence(candidate, block, item.StatementIndex, item.TreeIndex, node, blockWeight);
                        candidate.Occurrences.Add(occurrence);
                        _occurrenceByNode[node] = occurrence;
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
                var inMaps = new Dictionary<Candidate, Occurrence?>[_method.Blocks.Length];
                var outMaps = new Dictionary<Candidate, Occurrence?>[_method.Blocks.Length];
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    inMaps[b] = new Dictionary<Candidate, Occurrence?>();
                    outMaps[b] = new Dictionary<Candidate, Occurrence?>();
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

                        var input = ComputeBlockInput(blockId, outMaps);
                        if (!SameMap(input, inMaps[blockId]))
                        {
                            inMaps[blockId] = input;
                            changed = true;
                        }

                        var output = TransferBlock(blockId, input, mark: false);
                        if (!SameMap(output, outMaps[blockId]))
                        {
                            outMaps[blockId] = output;
                            changed = true;
                        }
                    }
                } while (changed);

                for (int b = 0; b < _method.Blocks.Length; b++)
                    TransferBlock(b, inMaps[b], mark: true);
            }

            private Dictionary<Candidate, Occurrence?> ComputeBlockInput(int blockId, Dictionary<Candidate, Occurrence?>[] outMaps)
            {
                var preds = _method.Cfg.Blocks[blockId].Predecessors;
                if (preds.Length == 0)
                    return new Dictionary<Candidate, Occurrence?>();

                Dictionary<Candidate, Occurrence?>? result = null;
                for (int p = 0; p < preds.Length; p++)
                {
                    int predId = preds[p].FromBlockId;
                    if ((uint)predId >= (uint)outMaps.Length)
                        return new Dictionary<Candidate, Occurrence?>();

                    var predOut = outMaps[predId];
                    if (result is null)
                    {
                        result = new Dictionary<Candidate, Occurrence?>(predOut);
                        continue;
                    }

                    var remove = new List<Candidate>();
                    foreach (var item in result)
                    {
                        if (!predOut.TryGetValue(item.Key, out var predDef) || !ReferenceEquals(predDef, item.Value))
                            remove.Add(item.Key);
                    }

                    for (int i = 0; i < remove.Count; i++)
                        result.Remove(remove[i]);
                }

                return result ?? new Dictionary<Candidate, Occurrence?>();
            }

            private Dictionary<Candidate, Occurrence?> TransferBlock(int blockId, Dictionary<Candidate, Occurrence?> input, bool mark)
            {
                var current = new Dictionary<Candidate, Occurrence?>(input);
                var list = _method.Blocks[blockId].TreeList;
                for (int i = 0; i < list.Length; i++)
                {
                    var node = list[i].Tree.Source;
                    if (!_occurrenceByNode.TryGetValue(node, out var occurrence))
                        continue;
                    var candidate = occurrence.Candidate;
                    if (current.TryGetValue(candidate, out var reachingDef) && reachingDef is not null)
                    {
                        if (mark)
                        {
                            occurrence.IsUse = true;
                            occurrence.IsDef = false;
                            occurrence.ReachingDef = reachingDef;
                            candidate.UseCount++;
                            candidate.WeightedUseCount += occurrence.Weight;
                        }
                    }
                    else
                    {
                        if (mark)
                        {
                            occurrence.IsDef = true;
                            occurrence.IsUse = false;
                            occurrence.ReachingDef = null;
                            candidate.DefCount++;
                            candidate.WeightedDefCount += occurrence.Weight;
                        }
                        current[candidate] = occurrence;
                    }
                }

                return current;
            }

            private static bool SameMap(Dictionary<Candidate, Occurrence?> left, Dictionary<Candidate, Occurrence?> right)
            {
                if (left.Count != right.Count)
                    return false;
                foreach (var item in left)
                {
                    if (!right.TryGetValue(item.Key, out var rightValue))
                        return false;
                    if (!ReferenceEquals(item.Value, rightValue))
                        return false;
                }
                return true;
            }

            private bool SelectCandidates()
            {
                var selectable = new List<Candidate>();
                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (candidate.UseCount == 0 || candidate.DefCount == 0)
                        continue;
                    if (!AllDefinitionOccurrencesCanBeMaterialized(candidate))
                        continue;

                    candidate.Cost = EstimateCost(candidate.Occurrences[0].Node);
                    if (!PassesProfitabilityCheck(candidate))
                        continue;

                    selectable.Add(candidate);
                }

                selectable.Sort(static (left, right) =>
                {
                    double leftBenefit = left.EstimatedNoCseCost - left.EstimatedYesCseCost;
                    double rightBenefit = right.EstimatedNoCseCost - right.EstimatedYesCseCost;
                    int c = rightBenefit.CompareTo(leftBenefit);
                    if (c != 0)
                        return c;
                    c = right.Cost.CompareTo(left.Cost);
                    if (c != 0)
                        return c;
                    c = right.UseCount.CompareTo(left.UseCount);
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

            private bool PassesProfitabilityCheck(Candidate candidate)
            {
                if (candidate.Cost <= 1 || candidate.UseCount <= 0 || candidate.DefCount <= 0)
                    return false;

                var representative = candidate.Occurrences[0].Node;
                bool liveAcrossCall = CandidateMayBeLiveAcrossCall(candidate);
                bool canEnregister = CanEnregisterCse(representative);
                bool cheapPure = IsCheapPureExpression(representative);
                bool memoryOrAddress = IsMemoryOrAddressExpression(representative);
                double cseRefCnt = CseRefCount(candidate);

                if (liveAcrossCall && cheapPure && !memoryOrAddress)
                    return false;

                if (liveAcrossCall && !memoryOrAddress && cseRefCnt < 16.0 && candidate.Cost <= 6)
                    return false;

                EstimateCseAccessCosts(candidate, liveAcrossCall, canEnregister, memoryOrAddress, cseRefCnt, out int cseDefCost, out int cseUseCost, out int cseDefUseCost, out double extraYesCost);

                double noCseCost = candidate.WeightedUseCount * candidate.Cost;
                double yesCseCost = (candidate.WeightedDefCount * (cseDefCost + cseDefUseCost)) +
                                    (candidate.WeightedUseCount * cseUseCost) +
                                    extraYesCost;

                candidate.EstimatedNoCseCost = noCseCost;
                candidate.EstimatedYesCseCost = yesCseCost;

                int unweightedNoCseCost = candidate.UseCount * candidate.Cost;
                int unweightedYesCseCost = candidate.DefCount * (cseDefCost + cseDefUseCost) +
                                           candidate.UseCount * cseUseCost +
                                           (int)Math.Ceiling(extraYesCost);
                bool hasWeightedBenefit = candidate.WeightedUseCount != candidate.UseCount ||
                                          candidate.WeightedDefCount != candidate.DefCount;
                if (!hasWeightedBenefit && unweightedYesCseCost >= unweightedNoCseCost)
                    return false;

                double savings = noCseCost - yesCseCost;
                double requiredSavings = RequiredSavings(candidate, liveAcrossCall, cheapPure, memoryOrAddress, canEnregister);
                if (savings < requiredSavings)
                    return false;

                if (!hasWeightedBenefit && candidate.Cost <= 2 && candidate.UseCount < 3)
                    return false;

                if (!hasWeightedBenefit && candidate.Cost == 3 && candidate.UseCount < 2)
                    return false;

                return true;
            }

            private static double CseRefCount(Candidate candidate)
                => (candidate.WeightedDefCount * 2.0) + candidate.WeightedUseCount;

            private static double RequiredSavings(Candidate candidate, bool liveAcrossCall, bool cheapPure, bool memoryOrAddress, bool canEnregister)
            {
                double required = Math.Max(1.0, candidate.WeightedDefCount * 0.5);

                if (liveAcrossCall)
                    required += candidate.WeightedDefCount + Math.Max(1.0, candidate.WeightedUseCount * 0.25);

                if (cheapPure && !memoryOrAddress)
                    required += Math.Max(1.0, candidate.WeightedUseCount * 0.5);

                if (!canEnregister)
                    required += candidate.WeightedDefCount + candidate.WeightedUseCount;

                return required;
            }

            private void EstimateCseAccessCosts(
                Candidate candidate,
                bool liveAcrossCall,
                bool canEnregister,
                bool memoryOrAddress,
                double cseRefCnt,
                out int cseDefCost,
                out int cseUseCost,
                out int cseDefUseCost,
                out double extraYesCost)
            {
                extraYesCost = 0.0;

                if (canEnregister && !liveAcrossCall && cseRefCnt >= 12.0)
                {
                    cseDefCost = 1;
                    cseUseCost = 1;
                    cseDefUseCost = 1;
                    return;
                }

                if (canEnregister && !liveAcrossCall && cseRefCnt >= 6.0)
                {
                    cseDefCost = 2;
                    cseUseCost = 1;
                    cseDefUseCost = 1;
                    return;
                }

                if (canEnregister && !liveAcrossCall)
                {
                    cseDefCost = 2;
                    cseUseCost = 2;
                    cseDefUseCost = 2;
                    return;
                }

                cseDefCost = 2;
                cseUseCost = canEnregister ? 3 : 4;
                cseDefUseCost = canEnregister ? 3 : 4;

                if (liveAcrossCall)
                {
                    extraYesCost += Math.Max(1.0, candidate.WeightedDefCount);

                    if (!memoryOrAddress)
                        extraYesCost += Math.Max(1.0, candidate.WeightedUseCount * 0.5);

                    if (!canEnregister || cseRefCnt < 12.0)
                    {
                        cseUseCost++;
                        cseDefUseCost++;
                    }
                }
            }

            private static bool IsMemoryOrAddressExpression(GenTree node)
            {
                if (IsMemoryReadCandidate(node))
                    return true;

                return node.Kind is
                    GenTreeKind.FieldAddr or
                    GenTreeKind.StaticFieldAddr or
                    GenTreeKind.ArrayDataRef or
                    GenTreeKind.ArrayElementAddr or
                    GenTreeKind.PointerElementAddr or
                    GenTreeKind.PointerDiff;
            }

            private static bool IsCheapPureExpression(GenTree node)
            {
                if (IsMemoryOrAddressExpression(node))
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Unary => node.SourceOp is BytecodeOp.Neg or BytecodeOp.Not,
                    GenTreeKind.Conv => true,
                    GenTreeKind.SizeOf => true,
                    GenTreeKind.StaticData => true,
                    GenTreeKind.Binary => node.SourceOp is
                        BytecodeOp.Add or
                        BytecodeOp.Sub or
                        BytecodeOp.And or
                        BytecodeOp.Or or
                        BytecodeOp.Xor or
                        BytecodeOp.Shl or
                        BytecodeOp.Shr or
                        BytecodeOp.Shr_Un or
                        BytecodeOp.Ceq or
                        BytecodeOp.Clt or
                        BytecodeOp.Clt_Un or
                        BytecodeOp.Cgt or
                        BytecodeOp.Cgt_Un,
                    _ => false,
                };
            }

            private static bool CanEnregisterCse(GenTree node)
            {
                if (node.StackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value or GenStackKind.Null or GenStackKind.ByRef)
                    return false;

                if (node.Type is not null)
                    return MachineAbi.IsPhysicallyPromotableStorage(node.Type, node.StackKind);

                return node.StackKind is
                    GenStackKind.I4 or
                    GenStackKind.I8 or
                    GenStackKind.R4 or
                    GenStackKind.R8 or
                    GenStackKind.NativeInt or
                    GenStackKind.NativeUInt or
                    GenStackKind.Ref or
                    GenStackKind.Ptr;
            }

            private bool CandidateMayBeLiveAcrossCall(Candidate candidate)
            {
                bool methodContainsCall = false;
                for (int b = 0; b < _method.Blocks.Length && !methodContainsCall; b++)
                {
                    var statements = _method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                    {
                        if (TreeContainsCall(statements[s].Source))
                        {
                            methodContainsCall = true;
                            break;
                        }
                    }
                }

                if (!methodContainsCall)
                    return false;

                for (int i = 0; i < candidate.Occurrences.Count; i++)
                {
                    var use = candidate.Occurrences[i];
                    if (!use.IsUse || use.ReachingDef is null)
                        continue;

                    var def = use.ReachingDef;
                    if (def.BlockId != use.BlockId)
                        return true;

                    var statements = _method.Blocks[def.BlockId].Statements;
                    int first = Math.Min(def.StatementIndex + 1, statements.Length);
                    int last = Math.Min(use.StatementIndex, statements.Length - 1);
                    for (int s = first; s <= last; s++)
                    {
                        if (TreeContainsCall(statements[s].Source))
                            return true;
                    }
                }

                return false;
            }

            private double BlockWeight(int blockId)
            {
                int depth = 0;
                for (int i = 0; i < _method.Cfg.NaturalLoops.Length; i++)
                {
                    if (_method.Cfg.NaturalLoops[i].Contains(blockId))
                        depth++;
                }

                return 1.0 + (8.0 * depth);
            }

            private static bool TreeContainsCall(GenTree node)
            {
                if ((node.Flags & GenTreeFlags.ContainsCall) != 0)
                    return true;

                if (node.Kind is GenTreeKind.Call or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewDelegate or
                    GenTreeKind.DelegateCombine or
                    GenTreeKind.DelegateRemove or
                    GenTreeKind.DelegateInvoke or
                    GenTreeKind.GcPoll)
                    return true;

                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (TreeContainsCall(node.Operands[i]))
                        return true;
                }

                return false;
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
                if (occurrence.Node.Parent is null)
                    return false;

                if (!CanExtractFromStatementKind(statement.Kind))
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

                for (int i = 0; i < subtreeStart; i++)
                {
                    if (BlocksExtractionBefore(statementTreeList[i]))
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

            private static bool BlocksExtractionBefore(SsaTree tree)
            {
                var node = tree.Source;
                if (tree.Value.HasValue || tree.StoreTarget.HasValue || tree.LocalFieldBaseValue.HasValue || tree.HasMemoryEffects)
                    return true;

                if ((node.Flags & (GenTreeFlags.ContainsCall |
                                   GenTreeFlags.CanThrow |
                                   GenTreeFlags.SideEffect |
                                   GenTreeFlags.MemoryRead |
                                   GenTreeFlags.MemoryWrite |
                                   GenTreeFlags.LocalDef |
                                   GenTreeFlags.Allocation |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.ExceptionFlow |
                                   GenTreeFlags.Ordered)) != 0)
                    return true;

                return false;
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
                var valueDefinitions = _method.ValueDefinitions.ToBuilder();
                var ssaLocalDescriptors = _method.SsaLocalDescriptors.ToBuilder();
                var slots = _method.Slots.ToBuilder();
                var ssaValues = new Dictionary<SsaValueName, ValueNumberPair>();
                foreach (var item in _vn.SsaValues)
                    ssaValues[item.Key] = item.Value;
                var treeValues = new Dictionary<GenTree, ValueNumberPair>(ReferenceEqualityComparer<GenTree>.Instance);
                foreach (var item in _vn.TreeValues)
                    treeValues[item.Key] = item.Value;
                var perCandidateDescriptors = new Dictionary<Candidate, ImmutableArray<SsaDescriptor>>();

                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (!candidate.Selected)
                        continue;

                    var descriptor = _method.GenTreeMethod.AppendCompilerTemp(
                        GenTempKind.CommonSubexpression,
                        candidate.Occurrences[0].Node.Type,
                        candidate.Occurrences[0].Node.StackKind);
                    descriptor.MarkRegularPromotedScalar(NextSyntheticVarIndex());
                    candidate.TempDescriptor = descriptor;
                    candidate.TempSlot = new SsaSlot(descriptor);

                    var perSsa = ImmutableArray.CreateBuilder<SsaDescriptor>();
                    perSsa.Add(null!);
                    int version = SsaConfig.FirstSsaNumber;

                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                    {
                        var occurrence = candidate.Occurrences[o];
                        if (!occurrence.IsDef)
                            continue;

                        occurrence.SsaVersion = version++;
                        occurrence.SsaName = new SsaValueName(candidate.TempSlot, occurrence.SsaVersion);
                        var ssaDescriptor = new SsaDescriptor(
                            candidate.TempSlot,
                            occurrence.SsaVersion,
                            SsaDefinitionKind.Store,
                            occurrence.BlockId,
                            -1,
                            -1,
                            defNode: null,
                            descriptor.Type,
                            descriptor.StackKind);
                        ssaDescriptor.SetValueNumbers(candidate.NormalValue);
                        perSsa.Add(ssaDescriptor);
                    }

                    for (int o = 0; o < candidate.Occurrences.Count; o++)
                    {
                        var occurrence = candidate.Occurrences[o];
                        if (!occurrence.IsUse || occurrence.ReachingDef is null)
                            continue;
                        occurrence.SsaName = occurrence.ReachingDef.SsaName;
                        perSsa[occurrence.SsaName.Version].AddUse(occurrence.BlockId);
                    }

                    var perSsaArray = perSsa.ToImmutable();
                    descriptor.SetSsaDescriptors(perSsaArray);
                    perCandidateDescriptors[candidate] = perSsaArray;
                    ssaLocalDescriptors.Add(new SsaLocalDescriptor(
                        candidate.TempSlot,
                        descriptor.Type,
                        descriptor.StackKind,
                        addressExposed: false,
                        isSsaPromoted: true,
                        descriptor,
                        perSsaArray));
                    slots.Add(new SsaSlotInfo(
                        candidate.TempSlot,
                        descriptor.Type,
                        descriptor.StackKind,
                        addressExposed: false,
                        memoryAliased: false,
                        category: GenLocalCategory.CompilerTemp,
                        lclNum: descriptor.LclNum,
                        varIndex: descriptor.VarIndex,
                        tracked: descriptor.Tracked,
                        inSsa: true,
                        localDescriptor: descriptor));
                }

                var newStatementsByBlock = RewriteTrees(treeValues, perCandidateDescriptors);
                _method.GenTreeMethod.ReplaceBlocksPreservingFlow(MaterializeGenTreeBlocks(newStatementsByBlock));

                for (int i = 0; i < _candidates.Count; i++)
                {
                    var candidate = _candidates[i];
                    if (!candidate.Selected || !perCandidateDescriptors.TryGetValue(candidate, out var descriptors))
                        continue;

                    for (int ssaNum = SsaConfig.FirstSsaNumber; ssaNum < descriptors.Length; ssaNum++)
                    {
                        var descriptor = descriptors[ssaNum];
                        valueDefinitions.Add(new SsaValueDefinition(descriptor));
                        ssaValues[descriptor.Name] = descriptor.ValueNumbers;
                    }
                }

                valueDefinitions.Sort(static (left, right) => left.Name.CompareTo(right.Name));
                ssaLocalDescriptors.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
                slots.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));

                var newBlocks = ImmutableArray.CreateBuilder<SsaBlock>(_method.Blocks.Length);
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var oldBlock = _method.Blocks[b];
                    var statements = newStatementsByBlock[b];
                    newBlocks.Add(new SsaBlock(
                        oldBlock.CfgBlock,
                        oldBlock.Phis,
                        statements,
                        oldBlock.MemoryPhis,
                        oldBlock.MemoryIn,
                        oldBlock.MemoryOut));
                }

                var newBlocksArray = newBlocks.ToImmutable();
                RefreshDefinitionLocations(valueDefinitions, _method.MemoryDefinitions, newBlocksArray, out var valueDefinitionsArray, out var memoryDefinitionsArray);
                RecomputeDescriptorUseTracking(valueDefinitionsArray, memoryDefinitionsArray, newBlocksArray);

                var valueNumbering = new SsaValueNumberingResult(
                    _vn.Store,
                    ssaValues,
                    treeValues,
                    _vn.MemoryValues,
                    _vn.HeapIn,
                    _vn.HeapOut,
                    _vn.ByrefExposedIn,
                    _vn.ByrefExposedOut,
                    _vn.LoopMemoryDependencies);

                return new SsaMethod(
                    _method.GenTreeMethod,
                    _method.Cfg,
                    slots.ToImmutable(),
                    _method.InitialValues,
                    valueDefinitionsArray,
                    newBlocksArray,
                    valueNumbering,
                    ssaLocalDescriptors.ToImmutable(),
                    _method.InitialMemoryValues,
                    memoryDefinitionsArray);
            }

            private static void RefreshDefinitionLocations(
                ImmutableArray<SsaValueDefinition>.Builder valueDefinitions,
                ImmutableArray<SsaMemoryDefinition> memoryDefinitions,
                ImmutableArray<SsaBlock> blocks,
                out ImmutableArray<SsaValueDefinition> valueDefinitionsArray,
                out ImmutableArray<SsaMemoryDefinition> memoryDefinitionsArray)
            {
                var localDescriptors = new Dictionary<SsaValueName, SsaDescriptor>();
                for (int i = 0; i < valueDefinitions.Count; i++)
                    localDescriptors[valueDefinitions[i].Name] = valueDefinitions[i].Descriptor;

                var memoryDescriptors = new Dictionary<SsaMemoryValueName, SsaMemoryDescriptor>();
                for (int i = 0; i < memoryDefinitions.Length; i++)
                    memoryDescriptors[memoryDefinitions[i].Name] = memoryDefinitions[i].Descriptor;

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        var item = block.TreeList[i];
                        var tree = item.Tree;

                        if (tree.StoreTarget.HasValue && localDescriptors.TryGetValue(tree.StoreTarget.Value, out var localDescriptor))
                        {
                            localDescriptor.DefStatementIndex = item.StatementIndex;
                            localDescriptor.DefTreeId = tree.Source.Id;
                            localDescriptor.DefNode = tree.Source;
                        }

                        for (int m = 0; m < tree.MemoryDefinitions.Length; m++)
                        {
                            if (memoryDescriptors.TryGetValue(tree.MemoryDefinitions[m], out var memoryDescriptor))
                            {
                                memoryDescriptor.DefStatementIndex = item.StatementIndex;
                                memoryDescriptor.DefTreeId = tree.Source.Id;
                                memoryDescriptor.DefNode = tree.Source;
                            }
                        }
                    }
                }

                var rebuiltValues = ImmutableArray.CreateBuilder<SsaValueDefinition>(valueDefinitions.Count);
                for (int i = 0; i < valueDefinitions.Count; i++)
                    rebuiltValues.Add(new SsaValueDefinition(valueDefinitions[i].Descriptor));
                rebuiltValues.Sort(static (left, right) => left.Name.CompareTo(right.Name));
                valueDefinitionsArray = rebuiltValues.ToImmutable();

                var rebuiltMemory = ImmutableArray.CreateBuilder<SsaMemoryDefinition>(memoryDefinitions.Length);
                for (int i = 0; i < memoryDefinitions.Length; i++)
                    rebuiltMemory.Add(new SsaMemoryDefinition(memoryDefinitions[i].Descriptor));
                memoryDefinitionsArray = rebuiltMemory.ToImmutable();
            }

            private static void RecomputeDescriptorUseTracking(
                ImmutableArray<SsaValueDefinition> valueDefinitions,
                ImmutableArray<SsaMemoryDefinition> memoryDefinitions,
                ImmutableArray<SsaBlock> blocks)
            {
                var descriptors = new Dictionary<SsaValueName, SsaDescriptor>();
                for (int i = 0; i < valueDefinitions.Length; i++)
                {
                    var descriptor = valueDefinitions[i].Descriptor;
                    descriptor.ResetUseTracking();
                    descriptors[descriptor.Name] = descriptor;
                }

                var memoryDescriptors = new Dictionary<SsaMemoryValueName, SsaMemoryDescriptor>();
                for (int i = 0; i < memoryDefinitions.Length; i++)
                {
                    var descriptor = memoryDefinitions[i].Descriptor;
                    descriptor.ResetUseTracking();
                    memoryDescriptors[descriptor.Name] = descriptor;
                }

                for (int i = 0; i < valueDefinitions.Length; i++)
                {
                    var descriptor = valueDefinitions[i].Descriptor;
                    if (!descriptor.HasUseDefSsaNum)
                        continue;

                    var previous = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                    if (descriptors.TryGetValue(previous, out var previousDescriptor))
                        previousDescriptor.AddUse(descriptor.DefBlockId);
                }

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            if (descriptors.TryGetValue(phi.Inputs[i].Value, out var descriptor))
                                descriptor.AddPhiUse(block.Id);
                        }
                    }

                    for (int p = 0; p < block.MemoryPhis.Length; p++)
                    {
                        var phi = block.MemoryPhis[p];
                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            if (memoryDescriptors.TryGetValue(phi.Inputs[i].Value, out var descriptor))
                                descriptor.AddPhiUse(block.Id);
                        }
                    }

                    for (int i = 0; i < block.TreeList.Length; i++)
                    {
                        var tree = block.TreeList[i].Tree;
                        if (tree.Value.HasValue && descriptors.TryGetValue(tree.Value.Value, out var valueDescriptor))
                            valueDescriptor.AddUse(block.Id);

                        if (tree.LocalFieldBaseValue.HasValue && descriptors.TryGetValue(tree.LocalFieldBaseValue.Value, out var baseDescriptor))
                            baseDescriptor.AddUse(block.Id);

                        for (int m = 0; m < tree.MemoryUses.Length; m++)
                        {
                            if (memoryDescriptors.TryGetValue(tree.MemoryUses[m], out var memoryDescriptor))
                                memoryDescriptor.AddUse(block.Id);
                        }
                    }
                }
            }

            private int NextSyntheticVarIndex()
            {
                int max = -1;
                var descriptors = _method.GenTreeMethod.AllLocalDescriptors;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    if (descriptors[i].VarIndex > max)
                        max = descriptors[i].VarIndex;
                }
                return max + 1;
            }

            private ImmutableArray<GenTreeBlock> MaterializeGenTreeBlocks(ImmutableArray<SsaTree>[] statementsByBlock)
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                for (int b = 0; b < _method.GenTreeMethod.Blocks.Length; b++)
                {
                    var oldBlock = _method.GenTreeMethod.Blocks[b];
                    var statements = ImmutableArray.CreateBuilder<GenTree>(statementsByBlock[b].Length);
                    for (int s = 0; s < statementsByBlock[b].Length; s++)
                        statements.Add(statementsByBlock[b][s].Source);

                    blocks.Add(new GenTreeBlock(
                        oldBlock.Id,
                        oldBlock.StartPc,
                        oldBlock.EndPcExclusive,
                        oldBlock.EntryStackDepth,
                        oldBlock.ExitStackDepth,
                        oldBlock.JumpKind,
                        oldBlock.Flags,
                        statements.ToImmutable(),
                        oldBlock.SuccessorBlockIds,
                        oldBlock.SuccessorPcs));
                }
                return blocks.ToImmutable();
            }

            private ImmutableArray<SsaTree>[] RewriteTrees(
                Dictionary<GenTree, ValueNumberPair> treeValues,
                Dictionary<Candidate, ImmutableArray<SsaDescriptor>> perCandidateDescriptors)
            {
                var result = new ImmutableArray<SsaTree>[_method.Blocks.Length];
                var replacementByNode = new Dictionary<GenTree, GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                var storeDescriptorByNode = new Dictionary<GenTree, SsaDescriptor>(ReferenceEqualityComparer<GenTree>.Instance);
                var storesBeforeStatement = new List<GenTree>[_method.Blocks.Length][];

                for (int b = 0; b < _method.Blocks.Length; b++)
                    storesBeforeStatement[b] = new List<GenTree>[_method.Blocks[b].Statements.Length];

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
                            var use = CreateUse(candidate, occurrence);
                            replacementByNode[occurrence.Node] = use;
                            treeValues[use] = candidate.NormalValue;

                            var descriptors = perCandidateDescriptors[candidate];
                            var store = CreateStore(candidate, occurrence, descriptors);
                            treeValues[store] = candidate.NormalValue;
                            storeDescriptorByNode[store] = descriptors[occurrence.SsaVersion];

                            var blockStores = storesBeforeStatement[occurrence.BlockId];
                            var statementStores = blockStores[occurrence.StatementIndex];
                            if (statementStores is null)
                            {
                                statementStores = new List<GenTree>();
                                blockStores[occurrence.StatementIndex] = statementStores;
                            }
                            statementStores.Add(store);
                        }
                        else if (occurrence.IsUse)
                        {
                            var replacement = CreateUse(candidate, occurrence);
                            replacementByNode[occurrence.Node] = replacement;
                            treeValues[replacement] = candidate.NormalValue;
                        }
                    }
                }

                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var oldBlock = _method.Blocks[b];
                    int inserted = 0;
                    for (int s = 0; s < oldBlock.Statements.Length; s++)
                        inserted += storesBeforeStatement[b][s]?.Count ?? 0;

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(oldBlock.Statements.Length + inserted);
                    for (int s = 0; s < oldBlock.Statements.Length; s++)
                    {
                        var stores = storesBeforeStatement[b][s];
                        if (stores is not null)
                        {
                            for (int i = 0; i < stores.Count; i++)
                            {
                                RefreshFlags(stores[i]);
                                if (storeDescriptorByNode.TryGetValue(stores[i], out var descriptor))
                                    descriptor.DefStatementIndex = statements.Count;
                                statements.Add(new SsaTree(stores[i]));
                            }
                        }

                        var source = RewriteNode(oldBlock.Statements[s].Source, replacementByNode);
                        RefreshFlags(source);
                        statements.Add(new SsaTree(source));
                    }
                    result[b] = statements.ToImmutable();
                }

                return result;
            }

            private GenTree CreateStore(Candidate candidate, Occurrence occurrence, ImmutableArray<SsaDescriptor> descriptors)
            {
                var temp = candidate.TempDescriptor!;
                var store = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.StoreTemp,
                    occurrence.Node.Pc,
                    occurrence.Node.SourceOp,
                    temp.Type,
                    temp.StackKind,
                    occurrence.Node.Flags | GenTreeFlags.SideEffect | GenTreeFlags.LocalDef | GenTreeFlags.Ordered,
                    ImmutableArray.Create(occurrence.Node),
                    int32: temp.Index);
                store.LocalDescriptor = temp;
                store.AttachSsaDefinition(occurrence.SsaName, temp.Type, temp.StackKind);
                store.CseNumber = EncodeCseDef(candidate.Index);
                descriptors[occurrence.SsaVersion].DefNode = store;
                descriptors[occurrence.SsaVersion].DefTreeId = store.Id;
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
                use.AttachSsaUse(occurrence.SsaName);
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

            private static int EncodeCseDef(int index) => index << 1;
            private static int EncodeCseUse(int index) => (index << 1) | 1;

            private static bool CanConsider(GenTree node)
            {
                if (!ProducesValue(node))
                    return false;
                if (!CanMaterializeInTemp(node))
                    return false;

                if ((node.Flags & (GenTreeFlags.ContainsCall |
                                   GenTreeFlags.SideEffect |
                                   GenTreeFlags.MemoryWrite |
                                   GenTreeFlags.LocalDef |
                                   GenTreeFlags.AddressExposed |
                                   GenTreeFlags.Allocation |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.ExceptionFlow)) != 0)
                    return false;

                if (IsMemoryReadCandidate(node))
                    return true;

                if ((node.Flags & (GenTreeFlags.CanThrow |
                                   GenTreeFlags.MemoryRead |
                                   GenTreeFlags.GlobalRef |
                                   GenTreeFlags.Indirect)) != 0)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Unary => true,
                    GenTreeKind.Binary => true,
                    GenTreeKind.Conv => true,
                    GenTreeKind.SizeOf => true,
                    GenTreeKind.FieldAddr => true,
                    GenTreeKind.StaticFieldAddr => true,
                    GenTreeKind.StaticData => true,
                    GenTreeKind.PointerElementAddr => true,
                    GenTreeKind.PointerDiff => true,
                    _ => false,
                };
            }

            private static bool IsMemoryReadCandidate(GenTree node)
            {
                if (node.SsaMemoryUses.IsDefaultOrEmpty || !node.SsaMemoryDefinitions.IsDefaultOrEmpty)
                    return false;

                if ((node.Flags & GenTreeFlags.MemoryRead) == 0)
                    return false;

                if ((node.Flags & (GenTreeFlags.SideEffect |
                                   GenTreeFlags.MemoryWrite |
                                   GenTreeFlags.LocalDef |
                                   GenTreeFlags.AddressExposed |
                                   GenTreeFlags.Allocation |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.ExceptionFlow)) != 0)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Field => true,
                    GenTreeKind.StaticField => true,
                    GenTreeKind.LoadIndirect => true,
                    GenTreeKind.ArrayElement => true,
                    GenTreeKind.ArrayDataRef => true,
                    _ => false,
                };
            }

            private static bool CanMaterializeInTemp(GenTree node)
            {
                return node.StackKind is
                    GenStackKind.I4 or
                    GenStackKind.I8 or
                    GenStackKind.R4 or
                    GenStackKind.R8 or
                    GenStackKind.NativeInt or
                    GenStackKind.NativeUInt or
                    GenStackKind.Ref or
                    GenStackKind.Ptr;
            }

            private static bool ProducesValue(GenTree node)
            {
                if (node.StackKind == GenStackKind.Void)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Nop => false,
                    GenTreeKind.StoreIndirect => false,
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.StoreField => false,
                    GenTreeKind.StoreStaticField => false,
                    GenTreeKind.StoreArrayElement => false,
                    GenTreeKind.Eval => false,
                    GenTreeKind.Branch => false,
                    GenTreeKind.BranchTrue => false,
                    GenTreeKind.BranchFalse => false,
                    GenTreeKind.Return => false,
                    GenTreeKind.Throw => false,
                    GenTreeKind.Rethrow => false,
                    GenTreeKind.EndFinally => false,
                    _ => true,
                };
            }

            private static int EstimateCost(GenTree node)
            {
                int cost = node.Kind switch
                {
                    GenTreeKind.Unary => 2,
                    GenTreeKind.Binary => 3,
                    GenTreeKind.Conv => 2,
                    GenTreeKind.SizeOf => 1,
                    GenTreeKind.Field => 4,
                    GenTreeKind.StaticField => 4,
                    GenTreeKind.LoadIndirect => 4,
                    GenTreeKind.ArrayElement => 5,
                    GenTreeKind.StaticFieldAddr => 2,
                    GenTreeKind.FieldAddr => 2,
                    GenTreeKind.ArrayDataRef => 4,
                    GenTreeKind.StaticData => 1,
                    GenTreeKind.PointerElementAddr => 3,
                    GenTreeKind.PointerDiff => 3,
                    _ => 1,
                };

                for (int i = 0; i < node.Operands.Length; i++)
                    cost += Math.Max(1, EstimateCost(node.Operands[i]) / 2);
                return cost;
            }

            private static void RefreshFlags(GenTree node)
            {
                for (int i = 0; i < node.Operands.Length; i++)
                    RefreshFlags(node.Operands[i]);

                var flags = node.Flags;
                for (int i = 0; i < node.Operands.Length; i++)
                    flags |= node.Operands[i].Flags;
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
