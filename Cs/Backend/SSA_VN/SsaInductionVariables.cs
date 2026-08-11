using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal readonly struct InductionVariableOccurrence
    {
        public readonly int BlockId;
        public readonly GenTree Statement;
        public readonly GenTree Node;

        public InductionVariableOccurrence(int blockId, GenTree statement, GenTree node)
        {
            BlockId = blockId;
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
            Node = node ?? throw new ArgumentNullException(nameof(node));
        }
    }

    internal sealed class InductionVariablePerLoopInfo
    {
        private sealed class LoopInfo
        {
            public readonly Dictionary<SsaSlot, List<InductionVariableOccurrence>> Occurrences = new();
        }

        private readonly SsaMethod _method;
        private readonly ImmutableArray<ImmutableArray<int>> _children;
        private readonly Dictionary<int, LoopInfo> _info = new();

        public InductionVariablePerLoopInfo(SsaMethod method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            var childBuilders = new List<int>[method.Cfg.NaturalLoops.Length];
            for (int i = 0; i < childBuilders.Length; i++)
                childBuilders[i] = new List<int>();

            for (int i = 0; i < method.Cfg.NaturalLoops.Length; i++)
            {
                int parent = method.Cfg.NaturalLoops[i].Parent;
                if ((uint)parent < (uint)childBuilders.Length)
                    childBuilders[parent].Add(i);
            }

            var children = ImmutableArray.CreateBuilder<ImmutableArray<int>>(childBuilders.Length);
            for (int i = 0; i < childBuilders.Length; i++)
            {
                childBuilders[i].Sort((left, right) =>
                {
                    var leftLoop = method.Cfg.NaturalLoops[left];
                    var rightLoop = method.Cfg.NaturalLoops[right];
                    int c = rightLoop.Depth.CompareTo(leftLoop.Depth);
                    if (c != 0)
                        return c;
                    return leftLoop.Index.CompareTo(rightLoop.Index);
                });
                children.Add(childBuilders[i].ToImmutableArray());
            }
            _children = children.ToImmutable();
        }

        public bool VisitOccurrences(CfgLoop loop, SsaSlot slot, Func<InductionVariableOccurrence, bool> visitor)
        {
            if (visitor is null)
                throw new ArgumentNullException(nameof(visitor));

            var children = _children[loop.Index];
            for (int i = 0; i < children.Length; i++)
            {
                var child = _method.Cfg.NaturalLoops[children[i]];
                if (!VisitOccurrences(child, slot, visitor))
                    return false;
            }

            var info = GetOrCreateInfo(loop);
            if (!info.Occurrences.TryGetValue(slot, out var occurrences))
                return true;

            for (int i = 0; i < occurrences.Count; i++)
            {
                if (!visitor(occurrences[i]))
                    return false;
            }
            return true;
        }

        public bool VisitStatementsWithOccurrences(
            CfgLoop loop,
            SsaSlot slot,
            HashSet<GenTree> ignoredStatements,
            Func<int, GenTree, bool> visitor)
        {
            if (ignoredStatements is null)
                throw new ArgumentNullException(nameof(ignoredStatements));
            if (visitor is null)
                throw new ArgumentNullException(nameof(visitor));

            var visited = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            return VisitOccurrences(loop, slot, occurrence =>
            {
                if (ignoredStatements.Contains(occurrence.Statement) || !visited.Add(occurrence.Statement))
                    return true;
                return visitor(occurrence.BlockId, occurrence.Statement);
            });
        }

        public bool HasAnyOccurrences(CfgLoop loop, SsaSlot slot, HashSet<GenTree> ignoredStatements)
        {
            bool found = false;
            VisitOccurrences(loop, slot, occurrence =>
            {
                if (ignoredStatements.Contains(occurrence.Statement))
                    return true;
                found = true;
                return false;
            });
            return found;
        }

        public void Invalidate(CfgLoop loop)
        {
            var children = _children[loop.Index];
            for (int i = 0; i < children.Length; i++)
                Invalidate(_method.Cfg.NaturalLoops[children[i]]);
            _info.Remove(loop.Index);
        }

        private LoopInfo GetOrCreateInfo(CfgLoop loop)
        {
            if (_info.TryGetValue(loop.Index, out var existing))
                return existing;

            var children = _children[loop.Index];
            var descendantBlocks = new HashSet<int>();
            for (int i = 0; i < children.Length; i++)
            {
                var child = _method.Cfg.NaturalLoops[children[i]];
                GetOrCreateInfo(child);
                for (int b = 0; b < child.Blocks.Length; b++)
                    descendantBlocks.Add(child.Blocks[b]);
            }

            var info = new LoopInfo();
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                if (descendantBlocks.Contains(blockId))
                    continue;

                var block = _method.Blocks[blockId];
                for (int s = 0; s < block.Statements.Length; s++)
                {
                    var statement = block.Statements[s].Source;
                    CollectOccurrences(info, blockId, statement, statement);
                }
            }

            _info.Add(loop.Index, info);
            return info;
        }

        private static void CollectOccurrences(LoopInfo info, int blockId, GenTree statement, GenTree node)
        {
            bool hasFirst = false;
            SsaSlot first = default;
            bool hasSecond = false;
            SsaSlot second = default;
            bool hasThird = false;
            SsaSlot third = default;

            if (SsaSlotHelpers.TryGetLoadSlot(node, out var loadSlot))
            {
                AddOccurrence(info, loadSlot, blockId, statement, node);
                first = loadSlot;
                hasFirst = true;
            }

            if (SsaSlotHelpers.TryGetStoreSlot(node, out var storeSlot) && (!hasFirst || !storeSlot.Equals(first)))
            {
                AddOccurrence(info, storeSlot, blockId, statement, node);
                if (!hasFirst)
                {
                    first = storeSlot;
                    hasFirst = true;
                }
                else
                {
                    second = storeSlot;
                    hasSecond = true;
                }
            }

            if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out var addressSlot) &&
                (!hasFirst || !addressSlot.Equals(first)) &&
                (!hasSecond || !addressSlot.Equals(second)))
            {
                AddOccurrence(info, addressSlot, blockId, statement, node);
                if (!hasFirst)
                {
                    first = addressSlot;
                    hasFirst = true;
                }
                else if (!hasSecond)
                {
                    second = addressSlot;
                    hasSecond = true;
                }
                else
                {
                    third = addressSlot;
                    hasThird = true;
                }
            }

            if (node.SsaLocalFieldBaseValue.HasValue)
            {
                var baseSlot = node.SsaLocalFieldBaseValue.Value.Slot;
                if ((!hasFirst || !baseSlot.Equals(first)) &&
                    (!hasSecond || !baseSlot.Equals(second)) &&
                    (!hasThird || !baseSlot.Equals(third)))
                {
                    AddOccurrence(info, baseSlot, blockId, statement, node);
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (SsaSlotHelpers.IsContainedLocalFieldAddressUse(node, i))
                    continue;
                CollectOccurrences(info, blockId, statement, node.Operands[i]);
            }
        }

        private static void AddOccurrence(LoopInfo info, SsaSlot slot, int blockId, GenTree statement, GenTree node)
        {
            if (!info.Occurrences.TryGetValue(slot, out var occurrences))
            {
                occurrences = new List<InductionVariableOccurrence>();
                info.Occurrences.Add(slot, occurrences);
            }
            occurrences.Add(new InductionVariableOccurrence(blockId, statement, node));
        }
    }

    internal readonly struct SsaInductionVariableOptimizationResult
    {
        public readonly SsaMethod Method;
        public readonly bool Changed;
        public readonly int StrengthReducedInductionVariables;
        public readonly int RemovedUnusedInductionVariables;

        public SsaInductionVariableOptimizationResult(
            SsaMethod method,
            bool changed,
            int strengthReducedInductionVariables,
            int removedUnusedInductionVariables)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Changed = changed;
            StrengthReducedInductionVariables = strengthReducedInductionVariables;
            RemovedUnusedInductionVariables = removedUnusedInductionVariables;
        }
    }

    internal static class SsaInductionVariableOptimizer
    {
        public static SsaInductionVariableOptimizationResult OptimizeMethod(
            SsaMethod method,
            SsaOptimizationOptions options,
            bool validate)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (!options.EnableInductionVariableOptimization || method.Cfg.NaturalLoops.IsDefaultOrEmpty)
                return new SsaInductionVariableOptimizationResult(method, false, 0, 0);

            if (validate)
                SsaVerifier.Verify(method);

            var current = method;
            int strengthReduced = 0;
            var remainingCandidates = CollectStrengthReductionCandidates(method);
            while (remainingCandidates.Count != 0 &&
                   TryStrengthReduceOne(current, remainingCandidates, out var rewritten))
            {
                current = RebuildSsa(current, rewritten, validate);
                strengthReduced++;
            }

            var statementsToRemove = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            int removedUnused = 0;
            if (!current.Cfg.NaturalLoops.IsDefaultOrEmpty)
            {
                var loopInfo = new InductionVariablePerLoopInfo(current);
                var liveness = SsaLocalLiveness.Build(current);
                var loops = OrderLoops(current.Cfg);

                for (int i = 0; i < loops.Length; i++)
                {
                    var loop = loops[i];
                    if (!loop.IsReducible || !loop.IsCanonicalPreheader || loop.Preheader < 0)
                        continue;

                    removedUnused += FindUnusedInductionVariables(current, loop, loopInfo, liveness, statementsToRemove);
                }
            }

            if (statementsToRemove.Count != 0)
            {
                var rewritten = RemoveStatements(current.GenTreeMethod, statementsToRemove);
                current = RebuildSsa(current, rewritten, validate);
            }

            if (validate)
                SsaVerifier.Verify(current);

            bool changed = strengthReduced != 0 || removedUnused != 0;
            return new SsaInductionVariableOptimizationResult(current, changed, strengthReduced, removedUnused);
        }

        private readonly struct StrengthReductionCandidateKey : IEquatable<StrengthReductionCandidateKey>
        {
            public readonly int Header;
            public readonly SsaSlot Slot;

            public StrengthReductionCandidateKey(int header, SsaSlot slot)
            {
                Header = header;
                Slot = slot;
            }

            public bool Equals(StrengthReductionCandidateKey other)
                => Header == other.Header && Slot.Equals(other.Slot);

            public override bool Equals(object? obj)
                => obj is StrengthReductionCandidateKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Header, Slot);
        }

        private static HashSet<StrengthReductionCandidateKey> CollectStrengthReductionCandidates(SsaMethod method)
        {
            var candidates = new HashSet<StrengthReductionCandidateKey>();
            var loops = OrderLoops(method.Cfg);
            for (int i = 0; i < loops.Length; i++)
            {
                var loop = loops[i];
                if (!loop.IsReducible || !loop.IsCanonicalPreheader || loop.Preheader < 0 || loop.BackEdges.IsDefaultOrEmpty)
                    continue;

                var header = method.Blocks[loop.Header];
                for (int p = 0; p < header.Phis.Length; p++)
                    candidates.Add(new StrengthReductionCandidateKey(loop.Header, header.Phis[p].Slot));
            }
            return candidates;
        }

        private static bool TryStrengthReduceOne(
            SsaMethod method,
            HashSet<StrengthReductionCandidateKey> remainingCandidates,
            out GenTreeMethod rewritten)
        {
            rewritten = null!;
            if (method.Cfg.NaturalLoops.IsDefaultOrEmpty || remainingCandidates.Count == 0)
                return false;

            var loopInfo = new InductionVariablePerLoopInfo(method);
            var liveness = SsaLocalLiveness.Build(method);
            var scev = new ScalarEvolutionContext(method);
            var loops = OrderLoops(method.Cfg);

            for (int i = 0; i < loops.Length; i++)
            {
                var loop = loops[i];
                if (!loop.IsReducible || !loop.IsCanonicalPreheader || loop.Preheader < 0 || loop.BackEdges.IsDefaultOrEmpty)
                    continue;

                scev.ResetForLoop(loop);
                var context = new StrengthReductionContext(method, loop, loopInfo, liveness, scev);
                var header = method.Blocks[loop.Header];
                for (int p = 0; p < header.Phis.Length; p++)
                {
                    var phi = header.Phis[p];
                    var key = new StrengthReductionCandidateKey(loop.Header, phi.Slot);
                    if (!remainingCandidates.Remove(key))
                        continue;
                    if (context.TryStrengthReduce(phi, out rewritten))
                        return true;
                }
            }

            remainingCandidates.Clear();
            return false;
        }

        private sealed class StrengthReductionContext
        {
            private sealed class CursorInfo
            {
                public readonly int BlockId;
                public readonly GenTree Statement;
                public GenTree Tree;
                public ScevAddRec? IV;

                public CursorInfo(int blockId, GenTree statement, GenTree tree, ScevAddRec? iv)
                {
                    BlockId = blockId;
                    Statement = statement;
                    Tree = tree;
                    IV = iv;
                }
            }

            private readonly SsaMethod _method;
            private readonly CfgLoop _loop;
            private readonly InductionVariablePerLoopInfo _loopInfo;
            private readonly SsaLocalLiveness _liveness;
            private readonly ScalarEvolutionContext _scev;
            private readonly ScevSimplificationAssumptions _simplificationAssumptions;
            private readonly List<CursorInfo> _cursors1 = new();
            private readonly List<CursorInfo> _cursors2 = new();
            private readonly HashSet<GenTree> _intermediateIVStores = new(ReferenceEqualityComparer<GenTree>.Instance);
            private int _nextTreeId;

            public StrengthReductionContext(
                SsaMethod method,
                CfgLoop loop,
                InductionVariablePerLoopInfo loopInfo,
                SsaLocalLiveness liveness,
                ScalarEvolutionContext scev)
            {
                _method = method;
                _loop = loop;
                _loopInfo = loopInfo;
                _liveness = liveness;
                _scev = scev;
                _simplificationAssumptions = InitializeSimplificationAssumptions();
                _nextTreeId = MaxTreeId(method) + 1;
            }

            private ScevSimplificationAssumptions InitializeSimplificationAssumptions()
            {
                var bounds = ImmutableArray.CreateBuilder<Scev>();
                var seenExitingBlocks = new HashSet<int>();
                for (int i = 0; i < _loop.ExitEdges.Length; i++)
                {
                    int exitingBlock = _loop.ExitEdges[i].FromBlockId;
                    if (!seenExitingBlocks.Add(exitingBlock))
                        continue;

                    bool boundsBackEdges = true;
                    for (int b = 0; b < _loop.BackEdges.Length; b++)
                    {
                        if (!_method.Cfg.Dominates(exitingBlock, _loop.BackEdges[b].FromBlockId))
                        {
                            boundsBackEdges = false;
                            break;
                        }
                    }
                    if (!boundsBackEdges)
                        continue;

                    var bound = _scev.ComputeExitNotTakenCount(exitingBlock);
                    if (bound is not null)
                        bounds.Add(bound);
                }

                return new ScevSimplificationAssumptions(bounds.ToImmutable());
            }

            public bool TryStrengthReduce(SsaPhi phi, out GenTreeMethod rewritten)
            {
                rewritten = null!;
                var analyzed = _scev.Analyze(phi.Target);
                if (analyzed is null)
                    return false;

                analyzed = _scev.Simplify(analyzed, _simplificationAssumptions);
                if (analyzed is not ScevAddRec primaryIV || primaryIV.LoopIndex != _loop.Index)
                    return false;
                if (primaryIV.Type is ScevType.Ref or ScevType.ByRef)
                    return false;
                if (HasUnmodeledOrNonLoopUses(_method, _loop, _loopInfo, _liveness, phi.Slot))
                    return false;
                if (!InitializeCursors(phi, primaryIV))
                    return false;

                List<CursorInfo> cursors = _cursors1;
                List<CursorInfo> nextCursors = _cursors2;
                int derivedLevel = 0;
                ScevAddRec currentIV = primaryIV;

                while (true)
                {
                    AdvanceCursors(cursors, nextCursors);
                    if (!CheckAdvancedCursors(nextCursors, out var nextIV))
                        break;
                    if (nextIV.Type is ScevType.Ref or ScevType.ByRef)
                        break;

                    ExpandStoredCursors(nextCursors, cursors);
                    derivedLevel++;
                    (cursors, nextCursors) = (nextCursors, cursors);
                    currentIV = nextIV;
                }

                if (derivedLevel <= 0)
                    return false;
                if (!IsProfitable(primaryIV, currentIV))
                    return false;
                return TryReplaceUsesWithNewPrimaryIV(cursors, currentIV, out rewritten);
            }

            private bool InitializeCursors(SsaPhi phi, ScevAddRec primaryIV)
            {
                _cursors1.Clear();
                _cursors2.Clear();
                _intermediateIVStores.Clear();

                bool ok = _loopInfo.VisitOccurrences(_loop, phi.Slot, occurrence =>
                {
                    if (IsPureStoreToSlot(occurrence.Statement, phi.Slot))
                        return true;
                    if (!SsaSlotHelpers.TryGetDirectLoadSlot(occurrence.Node, out var useSlot) || !useSlot.Equals(phi.Slot))
                        return false;
                    if (!occurrence.Node.SsaValueName.HasValue || !occurrence.Node.SsaValueName.Value.Equals(phi.Target))
                        return false;

                    var analyzed = _scev.Analyze(occurrence.BlockId, occurrence.Node);
                    if (analyzed is null)
                        return false;
                    analyzed = _scev.Simplify(analyzed, _simplificationAssumptions);
                    if (analyzed is not ScevAddRec addRec || !Scev.StructuralEquals(addRec, primaryIV))
                        return false;

                    _cursors1.Add(new CursorInfo(occurrence.BlockId, occurrence.Statement, occurrence.Node, primaryIV));
                    _cursors2.Add(new CursorInfo(occurrence.BlockId, occurrence.Statement, occurrence.Node, primaryIV));
                    return true;
                });

                if (!ok || _cursors1.Count == 0)
                    return false;

                ExpandStoredCursors(_cursors1, _cursors2);
                return _cursors1.Count != 0;
            }

            private void AdvanceCursors(List<CursorInfo> cursors, List<CursorInfo> nextCursors)
            {
                if (cursors.Count != nextCursors.Count)
                    throw new InvalidOperationException("Strength-reduction cursor lists are not parallel.");

                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    var nextCursor = nextCursors[i];
                    if (cursor.BlockId != nextCursor.BlockId || !ReferenceEquals(cursor.Statement, nextCursor.Statement))
                        throw new InvalidOperationException("Strength-reduction cursor lists lost correspondence.");

                    nextCursor.Tree = cursor.Tree;
                    nextCursor.IV = cursor.IV;
                    do
                    {
                        var parent = nextCursor.Tree.Parent;
                        if (parent is null)
                        {
                            nextCursor.IV = null;
                            break;
                        }

                        nextCursor.Tree = parent;
                        var parentIV = _scev.Analyze(nextCursor.BlockId, parent);
                        if (parentIV is null)
                        {
                            nextCursor.IV = null;
                            break;
                        }

                        parentIV = _scev.Simplify(parentIV, _simplificationAssumptions);
                        if (parentIV is not ScevAddRec addRec)
                        {
                            nextCursor.IV = null;
                            break;
                        }

                        nextCursor.IV = addRec;
                    }
                    while (nextCursor.IV is not null && cursor.IV is not null && Scev.StructuralEquals(nextCursor.IV, cursor.IV));
                }
            }

            private void ExpandStoredCursors(List<CursorInfo> cursors, List<CursorInfo> otherCursors)
            {
                if (cursors.Count != otherCursors.Count)
                    throw new InvalidOperationException("Strength-reduction cursor lists are not parallel.");

                for (int i = 0; i < cursors.Count; i++)
                {
                    bool removed = false;
                    while (true)
                    {
                        var cursor = cursors[i];
                        var cur = cursor.Tree;
                        var parent = cur.Parent;
                        if (parent is null)
                            break;

                        if (SsaSlotHelpers.TryGetDirectStoreSlot(parent, out var storedSlot))
                        {
                            if (parent.Operands.Length == 1 &&
                                ReferenceEquals(parent.Operands[0], cur) &&
                                !HasObservableEffects(cur) &&
                                parent.SsaStoreTargetName.HasValue &&
                                parent.SsaStoreTargetName.Value.Slot.Equals(storedSlot) &&
                                !HasUnmodeledOrNonLoopUses(_method, _loop, _loopInfo, _liveness, storedSlot))
                            {
                                var storedName = parent.SsaStoreTargetName.Value;
                                var cursorIV = cursor.IV;
                                if (cursorIV is null)
                                    break;

                                var extraCurrent = new List<CursorInfo>();
                                var extraOther = new List<CursorInfo>();
                                bool expanded = _loopInfo.VisitOccurrences(_loop, storedSlot, occurrence =>
                                {
                                    if (ReferenceEquals(occurrence.Node, parent))
                                        return true;
                                    if (!SsaSlotHelpers.TryGetDirectLoadSlot(occurrence.Node, out var useSlot) || !useSlot.Equals(storedSlot))
                                        return false;
                                    if (!occurrence.Node.SsaValueName.HasValue || !occurrence.Node.SsaValueName.Value.Equals(storedName))
                                        return false;

                                    var analyzed = _scev.Analyze(occurrence.BlockId, occurrence.Node);
                                    if (analyzed is null)
                                        return false;
                                    analyzed = _scev.Simplify(analyzed, _simplificationAssumptions);
                                    if (analyzed is not ScevAddRec addRec || !Scev.StructuralEquals(addRec, cursorIV))
                                        return false;

                                    extraCurrent.Add(new CursorInfo(occurrence.BlockId, occurrence.Statement, occurrence.Node, cursorIV));
                                    extraOther.Add(new CursorInfo(occurrence.BlockId, occurrence.Statement, occurrence.Node, cursorIV));
                                    return true;
                                });

                                if (expanded)
                                {
                                    _intermediateIVStores.Add(parent);
                                    cursors.RemoveAt(i);
                                    otherCursors.RemoveAt(i);
                                    cursors.AddRange(extraCurrent);
                                    otherCursors.AddRange(extraOther);
                                    i--;
                                    removed = true;
                                }
                            }
                            break;
                        }

                        var parentIV = _scev.Analyze(cursor.BlockId, parent);
                        if (parentIV is null)
                            break;
                        parentIV = _scev.Simplify(parentIV, _simplificationAssumptions);
                        if (cursor.IV is null || !Scev.StructuralEquals(parentIV, cursor.IV))
                            break;

                        cursor.Tree = parent;
                    }

                    if (removed)
                        continue;
                }
            }

            private bool CheckAdvancedCursors(List<CursorInfo> cursors, out ScevAddRec nextIV)
            {
                nextIV = null!;
                bool hasNext = false;
                bool allowRephrasingNext = true;

                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    if (cursor.IV is null)
                        return false;

                    bool allowScaling = true;
                    if (!hasNext)
                    {
                        nextIV = cursor.IV;
                        allowRephrasingNext = allowScaling;
                        hasNext = true;
                        continue;
                    }

                    var common = ComputeRephrasableIV(cursor.IV, allowScaling, nextIV, allowRephrasingNext);
                    if (common is null)
                        return false;

                    nextIV = common;
                    allowRephrasingNext &= allowScaling;
                }

                return hasNext;
            }

            private ScevAddRec? ComputeRephrasableIV(
                ScevAddRec iv1,
                bool allowScalingIV1,
                ScevAddRec iv2,
                bool allowScalingIV2)
            {
                if (!Scev.StructuralEquals(iv1.Start, iv2.Start))
                    return null;
                if (Scev.StructuralEquals(iv1.Step, iv2.Step))
                    return iv1;
                if (iv1.Type != iv2.Type)
                    return null;

                return iv1.Type switch
                {
                    ScevType.Int32 => ComputeRephrasableIVByScaling32(iv1, allowScalingIV1, iv2, allowScalingIV2),
                    ScevType.Int64 => ComputeRephrasableIVByScaling64(iv1, allowScalingIV1, iv2, allowScalingIV2),
                    _ => null,
                };
            }

            private ScevAddRec? ComputeRephrasableIVByScaling32(
                ScevAddRec iv1,
                bool allowScalingIV1,
                ScevAddRec iv2,
                bool allowScalingIV2)
            {
                if (!TryGetConstant(iv1.Start, out var start1) || unchecked((int)start1) != 0 ||
                    !TryGetConstant(iv2.Start, out var start2) || unchecked((int)start2) != 0 ||
                    !TryGetConstant(iv1.Step, out var rawStep1) ||
                    !TryGetConstant(iv2.Step, out var rawStep2))
                {
                    return null;
                }

                int step1 = unchecked((int)rawStep1);
                int step2 = unchecked((int)rawStep2);
                if (!TryGcd(step1, step2, out int gcd))
                    return null;
                if ((!allowScalingIV1 && gcd != step1) || (!allowScalingIV2 && gcd != step2))
                    return null;
                if (gcd == step1)
                    return iv1;
                if (gcd == step2)
                    return iv2;
                if (gcd is 1 or -1)
                    return null;

                return _scev.NewAddRec(iv1.Start, _scev.NewConstant(iv1.RuntimeType, iv1.StackKind, gcd));
            }

            private ScevAddRec? ComputeRephrasableIVByScaling64(
                ScevAddRec iv1,
                bool allowScalingIV1,
                ScevAddRec iv2,
                bool allowScalingIV2)
            {
                if (!TryGetConstant(iv1.Start, out var start1) || start1 != 0 ||
                    !TryGetConstant(iv2.Start, out var start2) || start2 != 0 ||
                    !TryGetConstant(iv1.Step, out var step1) ||
                    !TryGetConstant(iv2.Step, out var step2))
                {
                    return null;
                }

                if (!TryGcd(step1, step2, out long gcd))
                    return null;
                if ((!allowScalingIV1 && gcd != step1) || (!allowScalingIV2 && gcd != step2))
                    return null;
                if (gcd == step1)
                    return iv1;
                if (gcd == step2)
                    return iv2;
                if (gcd is 1 or -1)
                    return null;

                return _scev.NewAddRec(iv1.Start, _scev.NewConstant(iv1.RuntimeType, iv1.StackKind, gcd));
            }

            private bool IsProfitable(ScevAddRec primaryIV, ScevAddRec derivedIV)
            {
                if (Scev.StructuralEquals(primaryIV.Step, derivedIV.Step))
                    return false;

                if (derivedIV.Step.Type == ScevType.Int64 &&
                    primaryIV.Step.Type == ScevType.Int32 &&
                    TryGetConstant(derivedIV.Step, out var derivedStep) &&
                    TryGetConstant(primaryIV.Step, out var primaryStep) &&
                    unchecked((int)derivedStep) == unchecked((int)primaryStep))
                {
                    return false;
                }

                return true;
            }

            private bool TryReplaceUsesWithNewPrimaryIV(
                List<CursorInfo> cursors,
                ScevAddRec iv,
                out GenTreeMethod rewritten)
            {
                rewritten = null!;
                if (cursors.Count == 0)
                    return false;
                if (!TryGetConstant(iv.Step, out _))
                    return false;
                if (!iv.Start.IsInvariant() || !CanMaterialize(iv.Start) || !CanMaterialize(iv.Step))
                    return false;
                if (!FindUpdateInsertionPoint(cursors, out int insertionBlock, out int updateAfterStatementIndex))
                    return false;

                var cursorTrees = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    if (cursor.IV is null ||
                        HasObservableEffects(cursor.Tree) ||
                        !CanRephraseIV(cursor.IV, iv) ||
                        !cursorTrees.Add(cursor.Tree))
                    {
                        return false;
                    }
                }

                var initValue = Materialize(iv.Start);
                var stepValue = Materialize(iv.Step);
                if (initValue is null || stepValue is null)
                    return false;

                var newPrimaryIV = _method.GenTreeMethod.AppendCompilerTemp(
                    GenTempKind.CommonSubexpression,
                    iv.RuntimeType,
                    iv.StackKind);

                var replacements = new Dictionary<GenTree, GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    var sourceUse = CreateTempUse(newPrimaryIV, cursor.Tree.Pc);
                    var replacement = RephraseIV(cursor.IV!, iv, sourceUse);
                    if (replacement is null)
                        throw new InvalidOperationException("A validated IV rephrase could not be materialized.");
                    replacements.Add(cursor.Tree, replacement);
                }

                var initStore = CreateTempStore(newPrimaryIV, initValue, _method.GenTreeMethod.Blocks[_loop.Preheader].EndPcExclusive);
                var updateUse = CreateTempUse(newPrimaryIV, _method.GenTreeMethod.Blocks[insertionBlock].EndPcExclusive);
                var nextValue = new GenTree(
                    _nextTreeId++,
                    GenTreeKind.Binary,
                    _method.GenTreeMethod.Blocks[insertionBlock].EndPcExclusive,
                    BytecodeOp.Add,
                    iv.RuntimeType,
                    iv.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray.Create(updateUse, stepValue));
                GenTreeMorpher.NormalizeTreeFlags(nextValue, _method.GenTreeMethod.Target);
                var updateStore = CreateTempStore(newPrimaryIV, nextValue, _method.GenTreeMethod.Blocks[insertionBlock].EndPcExclusive);

                rewritten = RewriteMethod(replacements, initStore, updateStore, insertionBlock, updateAfterStatementIndex);
                return true;
            }

            private bool FindUpdateInsertionPoint(
                List<CursorInfo> cursors,
                out int insertionBlock,
                out int updateAfterStatementIndex)
            {
                insertionBlock = -1;
                updateAfterStatementIndex = -1;
                int commonDominator = -1;
                for (int i = 0; i < _loop.BackEdges.Length; i++)
                {
                    int source = _loop.BackEdges[i].FromBlockId;
                    commonDominator = commonDominator < 0 ? source : IntersectDominators(commonDominator, source);
                    if (commonDominator < 0)
                        return false;
                }

                if (_method.GenTreeMethod.Target.Architecture == TargetArchitectureKind.Arm64 &&
                    TryFindPostUseUpdateInsertionPoint(cursors, commonDominator, out insertionBlock, out updateAfterStatementIndex))
                {
                    return true;
                }

                while (commonDominator >= 0 && _loop.Contains(commonDominator) && MayExecuteMultipleTimesPerIteration(commonDominator))
                {
                    int idom = _method.Cfg.ImmediateDominators[commonDominator];
                    if (idom < 0 || idom == commonDominator)
                    {
                        commonDominator = -1;
                        break;
                    }
                    commonDominator = idom;
                }

                if (commonDominator < 0 || !_loop.Contains(commonDominator) ||
                    !InsertionPointPostDominatesUses(commonDominator, cursors))
                {
                    return false;
                }

                insertionBlock = commonDominator;
                return true;
            }

            private bool TryFindPostUseUpdateInsertionPoint(
                List<CursorInfo> cursors,
                int backEdgeDominator,
                out int insertionBlock,
                out int updateAfterStatementIndex)
            {
                insertionBlock = -1;
                updateAfterStatementIndex = -1;

                var blocksWithUses = new HashSet<int>();
                for (int i = 0; i < cursors.Count; i++)
                    blocksWithUses.Add(cursors[i].BlockId);

                int candidate = backEdgeDominator;
                while (candidate >= 0 && _loop.Contains(candidate))
                {
                    if (!blocksWithUses.Contains(candidate))
                    {
                        int idom = _method.Cfg.ImmediateDominators[candidate];
                        if (idom < 0 || idom == candidate)
                            return false;
                        candidate = idom;
                        continue;
                    }

                    if (MayExecuteMultipleTimesPerIteration(candidate))
                        return false;

                    int latestStatementIndex = -1;
                    var statements = _method.Blocks[candidate].Statements;
                    for (int i = 0; i < cursors.Count; i++)
                    {
                        if (cursors[i].BlockId != candidate)
                            continue;

                        int statementIndex = FindStatementIndex(statements, cursors[i].Statement);
                        if (statementIndex < 0)
                            return false;
                        if (statementIndex > latestStatementIndex)
                            latestStatementIndex = statementIndex;
                    }

                    if (latestStatementIndex < 0 || !InsertionPointPostDominatesUses(candidate, cursors))
                        return false;

                    insertionBlock = candidate;
                    updateAfterStatementIndex = latestStatementIndex;
                    return true;
                }

                return false;
            }

            private static int FindStatementIndex(ImmutableArray<SsaTree> statements, GenTree statement)
            {
                for (int i = 0; i < statements.Length; i++)
                {
                    if (ReferenceEquals(statements[i].Source, statement))
                        return i;
                }

                return -1;
            }

            private bool InsertionPointPostDominatesUses(int insertionBlock, List<CursorInfo> cursors)
            {
                for (int i = 0; i < cursors.Count; i++)
                {
                    var cursor = cursors[i];
                    if (cursor.BlockId == insertionBlock)
                    {
                        var statements = _method.Blocks[insertionBlock].Statements;
                        if (statements.Length != 0 &&
                            ReferenceEquals(cursor.Statement, statements[statements.Length - 1].Source) &&
                            IsTerminator(cursor.Statement))
                        {
                            return false;
                        }
                    }
                    else if (!IsPostDominatedOnLoopIteration(cursor.BlockId, insertionBlock))
                    {
                        return false;
                    }
                }

                return true;
            }

            private int IntersectDominators(int first, int second)
            {
                if (first == second)
                    return first;

                var ancestors = new HashSet<int>();
                int current = first;
                while ((uint)current < (uint)_method.Cfg.ImmediateDominators.Length && ancestors.Add(current))
                {
                    int parent = _method.Cfg.ImmediateDominators[current];
                    if (parent < 0 || parent == current)
                        break;
                    current = parent;
                }

                current = second;
                var seen = new HashSet<int>();
                while ((uint)current < (uint)_method.Cfg.ImmediateDominators.Length && seen.Add(current))
                {
                    if (ancestors.Contains(current))
                        return current;
                    int parent = _method.Cfg.ImmediateDominators[current];
                    if (parent < 0 || parent == current)
                        break;
                    current = parent;
                }

                return -1;
            }

            private bool MayExecuteMultipleTimesPerIteration(int blockId)
            {
                for (int i = 0; i < _method.Cfg.NaturalLoops.Length; i++)
                {
                    var candidate = _method.Cfg.NaturalLoops[i];
                    if (candidate.Index == _loop.Index || candidate.Depth <= _loop.Depth || !candidate.Contains(blockId))
                        continue;

                    int parent = candidate.Parent;
                    while (parent >= 0)
                    {
                        if (parent == _loop.Index)
                            return true;
                        parent = _method.Cfg.NaturalLoops[parent].Parent;
                    }
                }

                return false;
            }

            private bool IsPostDominatedOnLoopIteration(int useBlock, int insertionBlock)
            {
                var backEdgeSources = new HashSet<int>();
                for (int i = 0; i < _loop.BackEdges.Length; i++)
                    backEdgeSources.Add(_loop.BackEdges[i].FromBlockId);

                var stack = new Stack<int>();
                var visited = new HashSet<int>();
                stack.Push(useBlock);

                while (stack.Count != 0)
                {
                    int blockId = stack.Pop();
                    if (blockId == insertionBlock)
                        continue;
                    if (!visited.Add(blockId))
                        continue;
                    if (backEdgeSources.Contains(blockId))
                        return false;

                    var successors = _method.Cfg.Blocks[blockId].Successors;
                    for (int i = 0; i < successors.Length; i++)
                    {
                        var edge = successors[i];
                        if (!_loop.Contains(edge.ToBlockId))
                            continue;
                        stack.Push(edge.ToBlockId);
                    }
                }

                return true;
            }

            private bool CanMaterialize(Scev scev)
            {
                scev = _scev.Simplify(scev, _simplificationAssumptions);
                return scev switch
                {
                    ScevConstant constant => IsMaterializableConstant(constant),
                    ScevLocal local => TryGetLocalDescriptor(local, out _),
                    ScevUnary unary => unary.Type == ScevType.Int64 && CanMaterialize(unary.Operand),
                    ScevBinary binary =>
                        binary.Oper is ScevOper.Add or ScevOper.Mul or ScevOper.Lsh &&
                        CanMaterializeBinary(binary) &&
                        CanMaterialize(binary.Left) &&
                        CanMaterialize(binary.Right),
                    _ => false,
                };
            }

            private bool CanMaterializeBinary(ScevBinary binary)
            {
                if (binary.Oper != ScevOper.Mul || binary.Type != ScevType.Int64 || _method.GenTreeMethod.Target.PointerSize >= 8)
                    return true;

                return IsConstantMinusOne(binary.Left) || IsConstantMinusOne(binary.Right);
            }

            private bool IsConstantMinusOne(Scev value)
                => TryGetConstant(value, out var constant) && constant == -1;

            private GenTree? Materialize(Scev scev)
            {
                scev = _scev.Simplify(scev, _simplificationAssumptions);
                switch (scev)
                {
                    case ScevConstant constant:
                        return CreateConstant(constant);

                    case ScevLocal local:
                        if (!TryGetLocalDescriptor(local, out var descriptor))
                            return null;
                        return CreateLocalUse(descriptor, local.StackKind, local.RuntimeType, -1);

                    case ScevUnary unary:
                        {
                            var operand = Materialize(unary.Operand);
                            if (operand is null || unary.Type != ScevType.Int64)
                                return null;
                            var result = new GenTree(
                                _nextTreeId++,
                                GenTreeKind.Conv,
                                -1,
                                BytecodeOp.Conv,
                                unary.RuntimeType,
                                unary.StackKind,
                                GenTreeFlags.None,
                                ImmutableArray.Create(operand),
                                convKind: unary.Oper == ScevOper.ZeroExtend ? NumericConvKind.U8 : NumericConvKind.I8,
                                convFlags: unary.Oper == ScevOper.ZeroExtend ? NumericConvFlags.SourceUnsigned : NumericConvFlags.None);
                            GenTreeMorpher.NormalizeTreeFlags(result, _method.GenTreeMethod.Target);
                            return result;
                        }

                    case ScevBinary binary:
                        {
                            var left = Materialize(binary.Left);
                            var right = Materialize(binary.Right);
                            if (left is null || right is null)
                                return null;

                            if (binary.Oper == ScevOper.Mul && IsConstantMinusOne(binary.Left))
                                return CreateNegation(right, binary);
                            if (binary.Oper == ScevOper.Mul && IsConstantMinusOne(binary.Right))
                                return CreateNegation(left, binary);
                            if (!CanMaterializeBinary(binary))
                                return null;

                            BytecodeOp op = binary.Oper switch
                            {
                                ScevOper.Add => BytecodeOp.Add,
                                ScevOper.Mul => BytecodeOp.Mul,
                                ScevOper.Lsh => BytecodeOp.Shl,
                                _ => throw new InvalidOperationException(),
                            };
                            var result = new GenTree(
                                _nextTreeId++,
                                GenTreeKind.Binary,
                                -1,
                                op,
                                binary.RuntimeType,
                                binary.StackKind,
                                GenTreeFlags.None,
                                ImmutableArray.Create(left, right));
                            GenTreeMorpher.NormalizeTreeFlags(result, _method.GenTreeMethod.Target);
                            return result;
                        }

                    default:
                        return null;
                }
            }

            private GenTree CreateNegation(GenTree operand, ScevBinary binary)
            {
                var result = new GenTree(
                    _nextTreeId++,
                    GenTreeKind.Unary,
                    -1,
                    BytecodeOp.Neg,
                    binary.RuntimeType,
                    binary.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray.Create(operand));
                GenTreeMorpher.NormalizeTreeFlags(result, _method.GenTreeMethod.Target);
                return result;
            }

            private bool CanRephraseIV(ScevAddRec iv, ScevAddRec sourceIV)
            {
                if (!Scev.StructuralEquals(iv.Start, sourceIV.Start))
                    return false;
                if (Scev.StructuralEquals(iv.Step, sourceIV.Step))
                    return true;
                if (iv.Type != sourceIV.Type || iv.Type is not (ScevType.Int32 or ScevType.Int64))
                    return false;
                if (!TryGetConstant(iv.Step, out var ivStep) || !TryGetConstant(sourceIV.Step, out var sourceStep))
                    return false;

                if (iv.Type == ScevType.Int32)
                {
                    int numerator = unchecked((int)ivStep);
                    int denominator = unchecked((int)sourceStep);
                    if (denominator == 0)
                        return false;
                    try
                    {
                        return numerator % denominator == 0;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
                }

                if (sourceStep == 0)
                    return false;
                try
                {
                    return ivStep % sourceStep == 0;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            private GenTree? RephraseIV(ScevAddRec iv, ScevAddRec sourceIV, GenTree sourceTree)
            {
                if (Scev.StructuralEquals(iv.Step, sourceIV.Step))
                    return sourceTree;
                if (!CanRephraseIV(iv, sourceIV) ||
                    !TryGetConstant(iv.Step, out var rawIVStep) ||
                    !TryGetConstant(sourceIV.Step, out var rawSourceStep))
                {
                    return null;
                }

                long scale;
                if (iv.Type == ScevType.Int32)
                {
                    int ivStep = unchecked((int)rawIVStep);
                    int sourceStep = unchecked((int)rawSourceStep);
                    try
                    {
                        scale = ivStep / sourceStep;
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }
                }
                else
                {
                    try
                    {
                        scale = rawIVStep / rawSourceStep;
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }
                }

                bool useShift = scale > 0 && IsPowerOfTwo(unchecked((ulong)scale));
                BytecodeOp op = useShift ? BytecodeOp.Shl : BytecodeOp.Mul;
                long rhsValue = useShift ? Log2(unchecked((ulong)scale)) : scale;
                var rhs = CreateIntegralConstant(iv.RuntimeType, iv.StackKind, rhsValue);
                var result = new GenTree(
                    _nextTreeId++,
                    GenTreeKind.Binary,
                    sourceTree.Pc,
                    op,
                    iv.RuntimeType,
                    iv.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray.Create(sourceTree, rhs));
                GenTreeMorpher.NormalizeTreeFlags(result, _method.GenTreeMethod.Target);
                return result;
            }

            private GenTreeMethod RewriteMethod(
                Dictionary<GenTree, GenTree> replacements,
                GenTree initStore,
                GenTree updateStore,
                int insertionBlock,
                int updateAfterStatementIndex)
            {
                var statementsByBlock = new List<GenTree>[_method.GenTreeMethod.Blocks.Length];
                for (int b = 0; b < _method.GenTreeMethod.Blocks.Length; b++)
                {
                    var oldBlock = _method.GenTreeMethod.Blocks[b];
                    var statements = new List<GenTree>(oldBlock.Statements.Length + 1);
                    for (int s = 0; s < oldBlock.Statements.Length; s++)
                    {
                        var root = RewriteTree(oldBlock.Statements[s], replacements);
                        GenTreeMorpher.NormalizeTreeFlags(root, _method.GenTreeMethod.Target);
                        statements.Add(root);
                    }
                    statementsByBlock[b] = statements;
                }

                InsertNearEnd(statementsByBlock[_loop.Preheader], initStore);
                if (updateAfterStatementIndex >= 0)
                {
                    var insertionStatements = statementsByBlock[insertionBlock];
                    if ((uint)updateAfterStatementIndex >= (uint)insertionStatements.Count)
                        throw new InvalidOperationException("Strength-reduction update statement index is outside the insertion block.");
                    GenTreeMorpher.NormalizeTreeFlags(updateStore, _method.GenTreeMethod.Target);
                    insertionStatements.Insert(updateAfterStatementIndex + 1, updateStore);
                }
                else
                {
                    InsertNearEnd(statementsByBlock[insertionBlock], updateStore);
                }

                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                for (int b = 0; b < _method.GenTreeMethod.Blocks.Length; b++)
                {
                    var oldBlock = _method.GenTreeMethod.Blocks[b];
                    var statements = statementsByBlock[b];
                    for (int s = 0; s < statements.Count; s++)
                        GenTreeMorpher.NormalizeTreeFlags(statements[s], _method.GenTreeMethod.Target);

                    blocks.Add(new GenTreeBlock(
                        oldBlock.Id,
                        oldBlock.StartPc,
                        oldBlock.EndPcExclusive,
                        oldBlock.EntryStackDepth,
                        oldBlock.ExitStackDepth,
                        oldBlock.JumpKind,
                        oldBlock.Flags,
                        ImmutableArray.CreateRange(statements),
                        oldBlock.SuccessorBlockIds,
                        oldBlock.SuccessorPcs,
                        oldBlock.RegionPc));
                }

                return _method.GenTreeMethod.CloneWithBlocks(blocks.ToImmutable());
            }

            private GenTree RewriteTree(GenTree node, Dictionary<GenTree, GenTree> replacements)
            {
                if (replacements.TryGetValue(node, out var replacement))
                    return replacement;

                if (_intermediateIVStores.Contains(node))
                {
                    if (!SsaSlotHelpers.TryGetDirectStoreSlot(node, out _) || node.Operands.Length != 1)
                        throw new InvalidOperationException("Intermediate IV store is not a direct local store.");
                    var zero = CreateZero(node.Operands[0]);
                    node.SetOperands(ImmutableArray.Create(zero));
                    return node;
                }

                if (node.Operands.Length == 0)
                    return node;

                bool changed = false;
                var operands = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var oldOperand = node.Operands[i];
                    var newOperand = RewriteTree(oldOperand, replacements);
                    operands.Add(newOperand);
                    changed |= !ReferenceEquals(oldOperand, newOperand);
                }
                if (changed)
                    node.SetOperands(operands.ToImmutable());
                return node;
            }

            private void InsertNearEnd(List<GenTree> statements, GenTree statement)
            {
                GenTreeMorpher.NormalizeTreeFlags(statement, _method.GenTreeMethod.Target);
                int index = statements.Count;
                if (index != 0 && IsTerminator(statements[index - 1]))
                    index--;
                statements.Insert(index, statement);
            }

            private GenTree CreateTempUse(GenLocalDescriptor temp, int pc)
            {
                var use = new GenTree(
                    _nextTreeId++,
                    GenTreeKind.Temp,
                    pc,
                    BytecodeOp.Nop,
                    temp.Type,
                    temp.StackKind,
                    GenTreeFlags.LocalUse,
                    ImmutableArray<GenTree>.Empty,
                    int32: temp.Index);
                use.LocalDescriptor = temp;
                return use;
            }

            private GenTree CreateTempStore(GenLocalDescriptor temp, GenTree data, int pc)
            {
                var store = new GenTree(
                    _nextTreeId++,
                    GenTreeKind.StoreTemp,
                    pc,
                    BytecodeOp.Nop,
                    temp.Type,
                    temp.StackKind,
                    GenTreeFlags.SideEffect | GenTreeFlags.LocalDef | GenTreeFlags.Ordered,
                    ImmutableArray.Create(data),
                    int32: temp.Index);
                store.LocalDescriptor = temp;
                GenTreeMorpher.NormalizeTreeFlags(store, _method.GenTreeMethod.Target);
                return store;
            }

            private GenTree CreateLocalUse(GenLocalDescriptor descriptor, GenStackKind stackKind, RuntimeType? type, int pc)
            {
                GenTreeKind kind = descriptor.Kind switch
                {
                    GenLocalKind.Argument => GenTreeKind.Arg,
                    GenLocalKind.Local => GenTreeKind.Local,
                    GenLocalKind.Temporary => GenTreeKind.Temp,
                    _ => throw new InvalidOperationException(),
                };
                BytecodeOp op = descriptor.Kind == GenLocalKind.Argument ? BytecodeOp.Ldarg : descriptor.Kind == GenLocalKind.Local ? BytecodeOp.Ldloc : BytecodeOp.Nop;
                var use = new GenTree(
                    _nextTreeId++,
                    kind,
                    pc,
                    op,
                    type,
                    stackKind,
                    GenTreeFlags.LocalUse,
                    ImmutableArray<GenTree>.Empty,
                    int32: descriptor.Index);
                use.LocalDescriptor = descriptor;
                return use;
            }

            private bool TryGetLocalDescriptor(ScevLocal local, out GenLocalDescriptor descriptor)
            {
                if (_method.TryGetSsaLocalDescriptor(local.Value.Slot, out var ssaLocal) &&
                    ssaLocal.LocalDescriptor is not null &&
                    ssaLocal.StackKind == local.StackKind &&
                    ReferenceEquals(ssaLocal.Type, local.RuntimeType))
                {
                    descriptor = ssaLocal.LocalDescriptor;
                    return true;
                }

                descriptor = null!;
                return false;
            }

            private bool IsMaterializableConstant(ScevConstant constant)
                => constant.StackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

            private GenTree CreateConstant(ScevConstant constant)
                => CreateIntegralConstant(constant.RuntimeType, constant.StackKind, constant.Value);

            private GenTree CreateIntegralConstant(RuntimeType? type, GenStackKind stackKind, long value)
            {
                int bits = stackKind == GenStackKind.Ptr
                    ? _method.GenTreeMethod.Target.PointerSize * 8
                    : GenTreeArithmeticSemantics.IntegralBits(type, stackKind, _method.GenTreeMethod.Target);
                if (bits <= 32)
                {
                    return new GenTree(
                        _nextTreeId++,
                        GenTreeKind.ConstI4,
                        -1,
                        BytecodeOp.Ldc_I4,
                        type,
                        stackKind,
                        GenTreeFlags.None,
                        ImmutableArray<GenTree>.Empty,
                        int32: unchecked((int)value));
                }

                return new GenTree(
                    _nextTreeId++,
                    GenTreeKind.ConstI8,
                    -1,
                    BytecodeOp.Ldc_I8,
                    type,
                    stackKind,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty,
                    int64: value);
            }

            private GenTree CreateZero(GenTree template)
                => CreateIntegralConstant(template.Type, template.StackKind, 0);

            private bool TryGetConstant(Scev scev, out long value)
            {
                var simplified = _scev.Simplify(scev, _simplificationAssumptions);
                if (simplified is ScevConstant constant)
                {
                    value = constant.Value;
                    return true;
                }

                value = 0;
                return false;
            }

            private static bool TryGcd(int a, int b, out int gcd)
            {
                try
                {
                    while (a != 0)
                    {
                        int newA = b % a;
                        b = a;
                        a = newA;
                    }
                    gcd = b;
                    return true;
                }
                catch (OverflowException)
                {
                    gcd = 0;
                    return false;
                }
            }

            private static bool TryGcd(long a, long b, out long gcd)
            {
                try
                {
                    while (a != 0)
                    {
                        long newA = b % a;
                        b = a;
                        a = newA;
                    }
                    gcd = b;
                    return true;
                }
                catch (OverflowException)
                {
                    gcd = 0;
                    return false;
                }
            }

            private static bool HasObservableEffects(GenTree tree)
            {
                const GenTreeFlags disallowed =
                    GenTreeFlags.ContainsCall |
                    GenTreeFlags.CanThrow |
                    GenTreeFlags.SideEffect |
                    GenTreeFlags.MemoryWrite |
                    GenTreeFlags.ControlFlow |
                    GenTreeFlags.ExceptionFlow |
                    GenTreeFlags.Allocation |
                    GenTreeFlags.Ordered;
                return (tree.Flags & disallowed) != 0;
            }

            private static bool IsTerminator(GenTree statement)
                => statement.Kind is
                    GenTreeKind.Branch or
                    GenTreeKind.BranchTrue or
                    GenTreeKind.BranchFalse or
                    GenTreeKind.Return or
                    GenTreeKind.Throw or
                    GenTreeKind.Rethrow or
                    GenTreeKind.EndFinally ||
                   (statement.Flags & GenTreeFlags.ControlFlow) != 0;

            private static bool IsPowerOfTwo(ulong value)
                => value != 0 && (value & (value - 1)) == 0;

            private static int Log2(ulong value)
            {
                int result = 0;
                while (value > 1)
                {
                    value >>= 1;
                    result++;
                }
                return result;
            }
        }

        private static ImmutableArray<CfgLoop> OrderLoops(ControlFlowGraph cfg)
        {
            var loops = new CfgLoop[cfg.NaturalLoops.Length];
            cfg.NaturalLoops.CopyTo(loops);
            var rpoIndex = new int[cfg.Blocks.Length];
            for (int i = 0; i < rpoIndex.Length; i++)
                rpoIndex[i] = int.MaxValue;
            for (int i = 0; i < cfg.ReversePostOrder.Length; i++)
            {
                int blockId = cfg.ReversePostOrder[i];
                if ((uint)blockId < (uint)rpoIndex.Length)
                    rpoIndex[blockId] = i;
            }

            Array.Sort(loops, (left, right) =>
            {
                int c = rpoIndex[left.Header].CompareTo(rpoIndex[right.Header]);
                if (c != 0)
                    return c;
                c = left.Depth.CompareTo(right.Depth);
                if (c != 0)
                    return c;
                return left.Index.CompareTo(right.Index);
            });
            return ImmutableArray.CreateRange(loops);
        }

        private static int FindUnusedInductionVariables(
            SsaMethod method,
            CfgLoop loop,
            InductionVariablePerLoopInfo loopInfo,
            SsaLocalLiveness liveness,
            HashSet<GenTree> statementsToRemove)
        {
            var header = method.Blocks[loop.Header];
            int removed = 0;

            for (int p = 0; p < header.Phis.Length; p++)
            {
                var phi = header.Phis[p];
                var slot = phi.Slot;
                if (HasParentStructOccurrences(method, loop, loopInfo, slot, statementsToRemove) ||
                    HasNonLoopUses(method, loop, liveness, slot))
                {
                    continue;
                }

                var removable = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
                bool allRemovable = loopInfo.VisitStatementsWithOccurrences(
                    loop,
                    slot,
                    statementsToRemove,
                    (blockId, statement) =>
                    {
                        if (!IsPureStoreToSlot(statement, slot))
                            return false;
                        removable.Add(statement);
                        return true;
                    });

                if (!allRemovable || removable.Count == 0)
                    continue;

                foreach (var statement in removable)
                    statementsToRemove.Add(statement);
                removed++;
            }

            return removed;
        }

        private static bool HasParentStructOccurrences(
            SsaMethod method,
            CfgLoop loop,
            InductionVariablePerLoopInfo loopInfo,
            SsaSlot slot,
            HashSet<GenTree> ignoredStatements)
        {
            if (!method.TryGetSsaLocalDescriptor(slot, out var local) ||
                local.LocalDescriptor is not { IsStructField: true, ParentLclNum: >= 0 } descriptor)
            {
                return false;
            }

            for (int i = 0; i < method.Slots.Length; i++)
            {
                var candidate = method.Slots[i];
                if (candidate.LclNum == descriptor.ParentLclNum)
                    return loopInfo.HasAnyOccurrences(loop, candidate.Slot, ignoredStatements);
            }

            return true;
        }

        private static bool HasUnmodeledOrNonLoopUses(
            SsaMethod method,
            CfgLoop loop,
            InductionVariablePerLoopInfo loopInfo,
            SsaLocalLiveness liveness,
            SsaSlot slot)
        {
            var ignored = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            return HasParentStructOccurrences(method, loop, loopInfo, slot, ignored) ||
                   HasNonLoopUses(method, loop, liveness, slot);
        }

        private static bool HasNonLoopUses(
            SsaMethod method,
            CfgLoop loop,
            SsaLocalLiveness liveness,
            SsaSlot slot)
        {
            if (!method.TryGetSsaLocalDescriptor(slot, out _))
                return true;

            for (int i = 0; i < method.Cfg.ExceptionRegions.Length; i++)
            {
                int handler = method.Cfg.ExceptionRegions[i].HandlerStartBlockId;
                if ((uint)handler < (uint)method.Blocks.Length && liveness.IsLiveIn(handler, slot))
                    return true;
            }

            for (int i = 0; i < loop.ExitEdges.Length; i++)
            {
                var edge = loop.ExitEdges[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (liveness.IsLiveIn(edge.ToBlockId, slot))
                    return true;
            }

            return false;
        }

        private static bool IsPureStoreToSlot(GenTree root, SsaSlot slot)
        {
            GenTree data;
            if (SsaSlotHelpers.TryGetDirectStoreSlot(root, out var directStoreSlot))
            {
                if (!directStoreSlot.Equals(slot) || root.Operands.Length != 1)
                    return false;
                data = root.Operands[0];
            }
            else if (SsaSlotHelpers.TryGetLocalFieldAccess(root, out var fieldAccess) &&
                     fieldAccess.IsFullDefinition &&
                     fieldAccess.IsPromotedFieldAccess)
            {
                if (!fieldAccess.Slot.Equals(slot) || root.Kind != GenTreeKind.StoreField || root.Operands.Length < 2)
                    return false;
                data = root.Operands[1];
            }
            else
            {
                return false;
            }

            const GenTreeFlags disallowed =
                GenTreeFlags.ContainsCall |
                GenTreeFlags.CanThrow |
                GenTreeFlags.SideEffect |
                GenTreeFlags.MemoryWrite |
                GenTreeFlags.ControlFlow |
                GenTreeFlags.ExceptionFlow |
                GenTreeFlags.Allocation |
                GenTreeFlags.Ordered;

            return (data.Flags & disallowed) == 0;
        }

        private static GenTreeMethod RemoveStatements(GenTreeMethod method, HashSet<GenTree> statementsToRemove)
        {
            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length);
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var oldBlock = method.Blocks[b];
                var statements = ImmutableArray.CreateBuilder<GenTree>(oldBlock.Statements.Length);
                for (int s = 0; s < oldBlock.Statements.Length; s++)
                {
                    var statement = oldBlock.Statements[s];
                    if (!statementsToRemove.Contains(statement))
                        statements.Add(statement);
                }

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
                    oldBlock.SuccessorPcs,
                    oldBlock.RegionPc));
            }

            return method.CloneWithBlocks(blocks.ToImmutable());
        }

        private static SsaMethod RebuildSsa(SsaMethod previous, GenTreeMethod rewritten, bool validate)
            => SsaGenTreeRewriter.RebuildAfterGenTreeRewrite(previous, rewritten, validate);

        private static int MaxTreeId(SsaMethod method)
        {
            int max = 0;
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Visit(statements[s].Source);
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
