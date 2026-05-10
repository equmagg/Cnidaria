using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal enum CfgEdgeKind : byte
    {
        FallThrough,
        Branch,
        BranchTrue,
        BranchFalse,
        Exception,
    }

    internal readonly struct CfgEdge : IEquatable<CfgEdge>
    {
        public readonly int FromBlockId;
        public readonly int ToBlockId;
        public readonly CfgEdgeKind Kind;
        public readonly int HandlerIndex;

        public CfgEdge(int fromBlockId, int toBlockId, CfgEdgeKind kind, int handlerIndex = -1)
        {
            FromBlockId = fromBlockId;
            ToBlockId = toBlockId;
            Kind = kind;
            HandlerIndex = handlerIndex;
        }

        public bool Equals(CfgEdge other)
            => FromBlockId == other.FromBlockId &&
               ToBlockId == other.ToBlockId &&
               Kind == other.Kind &&
               HandlerIndex == other.HandlerIndex;

        public override bool Equals(object? obj) => obj is CfgEdge other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FromBlockId;
                hash = (hash * 397) ^ ToBlockId;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ HandlerIndex;
                return hash;
            }
        }

        public override string ToString()
        {
            string kind = Kind.ToString();
            return HandlerIndex >= 0
                ? $"B{FromBlockId} -> B{ToBlockId} {kind}(h{HandlerIndex})"
                : $"B{FromBlockId} -> B{ToBlockId} {kind}";
        }
    }

    internal readonly struct CfgLoop
    {
        public readonly int Index;
        public readonly int HeaderBlockId;
        public readonly int LatchBlockId;
        public readonly ImmutableArray<int> BodyBlockIds;

        public CfgLoop(int index, int headerBlockId, int latchBlockId, ImmutableArray<int> bodyBlockIds)
        {
            Index = index;
            HeaderBlockId = headerBlockId;
            LatchBlockId = latchBlockId;
            BodyBlockIds = bodyBlockIds.IsDefault ? ImmutableArray<int>.Empty : bodyBlockIds;
        }

        public bool Contains(int blockId)
        {
            for (int i = 0; i < BodyBlockIds.Length; i++)
            {
                if (BodyBlockIds[i] == blockId)
                    return true;
            }
            return false;
        }

        public override string ToString() => $"L{Index}: header=B{HeaderBlockId}, latch=B{LatchBlockId}";
    }

    internal enum CfgExceptionRegionKind : byte
    {
        Catch,
        Finally,
        Fault,
        Filter,
    }

    internal readonly struct CfgExceptionRegion
    {
        public readonly int Index;
        public readonly CfgExceptionRegionKind Kind;
        public readonly int TryStartPc;
        public readonly int TryEndPc;
        public readonly int HandlerStartPc;
        public readonly int HandlerEndPc;
        public readonly int TryStartBlockId;
        public readonly int TryEndBlockIdExclusive;
        public readonly int HandlerStartBlockId;
        public readonly int HandlerEndBlockIdExclusive;
        public readonly int CatchTypeToken;
        public readonly int ParentIndex;

        public CfgExceptionRegion(
            int index,
            CfgExceptionRegionKind kind,
            int tryStartPc,
            int tryEndPc,
            int handlerStartPc,
            int handlerEndPc,
            int tryStartBlockId,
            int tryEndBlockIdExclusive,
            int handlerStartBlockId,
            int handlerEndBlockIdExclusive,
            int catchTypeToken,
            int parentIndex)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (tryStartPc < 0 || tryEndPc < tryStartPc) throw new ArgumentOutOfRangeException(nameof(tryEndPc));
            if (handlerStartPc < 0 || handlerEndPc < handlerStartPc) throw new ArgumentOutOfRangeException(nameof(handlerEndPc));
            if (tryStartBlockId < 0) throw new ArgumentOutOfRangeException(nameof(tryStartBlockId));
            if (tryEndBlockIdExclusive < tryStartBlockId) throw new ArgumentOutOfRangeException(nameof(tryEndBlockIdExclusive));
            if (handlerStartBlockId < 0) throw new ArgumentOutOfRangeException(nameof(handlerStartBlockId));
            if (handlerEndBlockIdExclusive < handlerStartBlockId) throw new ArgumentOutOfRangeException(nameof(handlerEndBlockIdExclusive));

            Index = index;
            Kind = kind;
            TryStartPc = tryStartPc;
            TryEndPc = tryEndPc;
            HandlerStartPc = handlerStartPc;
            HandlerEndPc = handlerEndPc;
            TryStartBlockId = tryStartBlockId;
            TryEndBlockIdExclusive = tryEndBlockIdExclusive;
            HandlerStartBlockId = handlerStartBlockId;
            HandlerEndBlockIdExclusive = handlerEndBlockIdExclusive;
            CatchTypeToken = catchTypeToken;
            ParentIndex = parentIndex;
        }

        public bool HasParent => ParentIndex >= 0;

        public bool ContainsTryBlock(int blockId)
            => TryStartBlockId <= blockId && blockId < TryEndBlockIdExclusive;

        public bool ContainsHandlerBlock(int blockId)
            => HandlerStartBlockId <= blockId && blockId < HandlerEndBlockIdExclusive;

        public override string ToString()
            => $"EH{Index}:{Kind} try=B{TryStartBlockId}..B{TryEndBlockIdExclusive - 1} handler=B{HandlerStartBlockId}..B{HandlerEndBlockIdExclusive - 1}";
    }

    internal sealed class CfgBlock
    {
        public GenTreeBlock SourceBlock { get; }
        public int Id => SourceBlock.Id;
        public int StartPc => SourceBlock.StartPc;
        public int EndPcExclusive => SourceBlock.EndPcExclusive;
        public ImmutableArray<CfgEdge> Predecessors { get; }
        public ImmutableArray<CfgEdge> Successors { get; }
        public ImmutableArray<int> TryRegionIndexes { get; }
        public ImmutableArray<int> HandlerRegionIndexes { get; }
        public bool IsHandlerEntry { get; }

        public bool IsInTryRegion => TryRegionIndexes.Length != 0;
        public bool IsInHandlerRegion => HandlerRegionIndexes.Length != 0;
        public bool IsFuncletBlock => IsInHandlerRegion;

        public CfgBlock(
            GenTreeBlock sourceBlock,
            ImmutableArray<CfgEdge> predecessors,
            ImmutableArray<CfgEdge> successors,
            ImmutableArray<int> tryRegionIndexes = default,
            ImmutableArray<int> handlerRegionIndexes = default,
            bool isHandlerEntry = false)
        {
            SourceBlock = sourceBlock ?? throw new ArgumentNullException(nameof(sourceBlock));
            Predecessors = predecessors.IsDefault ? ImmutableArray<CfgEdge>.Empty : predecessors;
            Successors = successors.IsDefault ? ImmutableArray<CfgEdge>.Empty : successors;
            TryRegionIndexes = tryRegionIndexes.IsDefault ? ImmutableArray<int>.Empty : tryRegionIndexes;
            HandlerRegionIndexes = handlerRegionIndexes.IsDefault ? ImmutableArray<int>.Empty : handlerRegionIndexes;
            IsHandlerEntry = isHandlerEntry;
        }
    }

    internal sealed class ControlFlowGraph
    {
        public GenTreeMethod Method { get; }
        public ImmutableArray<CfgBlock> Blocks { get; }
        public ImmutableArray<int> ReversePostOrder { get; }
        public ImmutableArray<int> ImmediateDominators { get; }
        public ImmutableArray<ImmutableArray<int>> DominanceFrontiers { get; }
        public ImmutableArray<ImmutableArray<int>> DominatorTreeChildren { get; }
        public ImmutableArray<CfgLoop> NaturalLoops { get; }
        public ImmutableArray<CfgExceptionRegion> ExceptionRegions { get; }

        public CfgBlock Entry => Blocks.Length == 0
            ? throw new InvalidOperationException("CFG has no entry block.")
            : Blocks[0];

        private ControlFlowGraph(
            GenTreeMethod method,
            ImmutableArray<CfgBlock> blocks,
            ImmutableArray<int> reversePostOrder,
            ImmutableArray<int> immediateDominators,
            ImmutableArray<ImmutableArray<int>> dominanceFrontiers,
            ImmutableArray<ImmutableArray<int>> dominatorTreeChildren,
            ImmutableArray<CfgLoop> naturalLoops,
            ImmutableArray<CfgExceptionRegion> exceptionRegions)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Blocks = blocks.IsDefault ? ImmutableArray<CfgBlock>.Empty : blocks;
            ReversePostOrder = reversePostOrder.IsDefault ? ImmutableArray<int>.Empty : reversePostOrder;
            ImmediateDominators = immediateDominators.IsDefault ? ImmutableArray<int>.Empty : immediateDominators;
            DominanceFrontiers = dominanceFrontiers.IsDefault ? ImmutableArray<ImmutableArray<int>>.Empty : dominanceFrontiers;
            DominatorTreeChildren = dominatorTreeChildren.IsDefault ? ImmutableArray<ImmutableArray<int>>.Empty : dominatorTreeChildren;
            NaturalLoops = naturalLoops.IsDefault ? ImmutableArray<CfgLoop>.Empty : naturalLoops;
            ExceptionRegions = exceptionRegions.IsDefault ? ImmutableArray<CfgExceptionRegion>.Empty : exceptionRegions;
        }

        public static ControlFlowGraph Build(GenTreeMethod method, bool includeExceptionEdges = true)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            int n = method.Blocks.Length;
            var succ = new List<CfgEdge>[n];
            var pred = new List<CfgEdge>[n];
            for (int i = 0; i < n; i++)
            {
                succ[i] = new List<CfgEdge>();
                pred[i] = new List<CfgEdge>();
            }

            var blockByStartPc = new Dictionary<int, int>(n);
            for (int i = 0; i < n; i++)
                blockByStartPc[method.Blocks[i].StartPc] = method.Blocks[i].Id;

            for (int i = 0; i < n; i++)
            {
                var block = method.Blocks[i];
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                    AddEdge(succ, pred, new CfgEdge(block.Id, block.SuccessorBlockIds[s], ClassifyNormalEdge(block, block.SuccessorBlockIds[s])));
            }

            if (includeExceptionEdges)
            {
                for (int h = 0; h < method.Function.ExceptionHandlers.Length; h++)
                {
                    var eh = method.Function.ExceptionHandlers[h];
                    if (!blockByStartPc.TryGetValue(eh.HandlerStartPc, out int handlerBlock))
                        continue;

                    for (int b = 0; b < n; b++)
                    {
                        var block = method.Blocks[b];
                        if (RangesIntersect(block.StartPc, block.EndPcExclusive, eh.TryStartPc, eh.TryEndPc) && BlockMayThrow(block))
                            AddEdge(succ, pred, new CfgEdge(block.Id, handlerBlock, CfgEdgeKind.Exception, h));
                    }
                }
            }

            var exceptionRegions = BuildExceptionRegions(method, blockByStartPc);
            var tryRegionIndexesByBlock = BuildRegionIndexMap(n, exceptionRegions, tryRegion: true);
            var handlerRegionIndexesByBlock = BuildRegionIndexMap(n, exceptionRegions, tryRegion: false);

            var blocksBuilder = ImmutableArray.CreateBuilder<CfgBlock>(n);
            for (int i = 0; i < n; i++)
            {
                pred[i].Sort(CompareEdges);
                succ[i].Sort(CompareEdges);
                blocksBuilder.Add(new CfgBlock(
                    method.Blocks[i],
                    pred[i].ToImmutableArray(),
                    succ[i].ToImmutableArray(),
                    tryRegionIndexesByBlock[i].ToImmutableArray(),
                    handlerRegionIndexesByBlock[i].ToImmutableArray(),
                    IsHandlerEntryBlock(i, exceptionRegions)));
            }

            var blocks = blocksBuilder.ToImmutable();
            var rpo = ComputeReversePostOrder(blocks);
            var idom = ComputeImmediateDominators(blocks, rpo);
            var df = ComputeDominanceFrontiers(blocks, idom);
            var children = ComputeDominatorTreeChildren(idom);
            var loops = ComputeNaturalLoops(blocks, idom);

            return new ControlFlowGraph(method, blocks, rpo.ToImmutableArray(), idom.ToImmutableArray(), df, children, loops, exceptionRegions);
        }

        private static ImmutableArray<CfgExceptionRegion> BuildExceptionRegions(GenTreeMethod method, IReadOnlyDictionary<int, int> blockByStartPc)
        {
            if (method.Function.ExceptionHandlers.Length == 0)
                return ImmutableArray<CfgExceptionRegion>.Empty;

            var regions = ImmutableArray.CreateBuilder<CfgExceptionRegion>(method.Function.ExceptionHandlers.Length);
            for (int i = 0; i < method.Function.ExceptionHandlers.Length; i++)
            {
                var eh = method.Function.ExceptionHandlers[i];
                if (!blockByStartPc.TryGetValue(eh.TryStartPc, out int tryStartBlock))
                    continue;
                if (!blockByStartPc.TryGetValue(eh.HandlerStartPc, out int handlerStartBlock))
                    continue;

                int tryEndBlock = FindBlockEndExclusive(method, eh.TryEndPc);
                int handlerEndBlock = FindBlockEndExclusive(method, eh.HandlerEndPc);
                var kind = eh.CatchTypeToken < 0
                    ? CfgExceptionRegionKind.Finally
                    : CfgExceptionRegionKind.Catch;

                regions.Add(new CfgExceptionRegion(
                    regions.Count,
                    kind,
                    eh.TryStartPc,
                    eh.TryEndPc,
                    eh.HandlerStartPc,
                    eh.HandlerEndPc,
                    tryStartBlock,
                    tryEndBlock,
                    handlerStartBlock,
                    handlerEndBlock,
                    eh.CatchTypeToken,
                    parentIndex: -1));
            }

            return AssignExceptionRegionParents(regions.ToImmutable());
        }

        private static ImmutableArray<CfgExceptionRegion> AssignExceptionRegionParents(ImmutableArray<CfgExceptionRegion> regions)
        {
            if (regions.Length == 0)
                return regions;

            var result = ImmutableArray.CreateBuilder<CfgExceptionRegion>(regions.Length);
            for (int i = 0; i < regions.Length; i++)
            {
                var child = regions[i];
                int parent = -1;
                int parentSize = int.MaxValue;

                for (int p = 0; p < regions.Length; p++)
                {
                    if (p == i)
                        continue;

                    var candidate = regions[p];
                    bool containsTry = candidate.TryStartPc <= child.TryStartPc && child.TryEndPc <= candidate.TryEndPc;
                    bool containsHandler = candidate.TryStartPc <= child.HandlerStartPc && child.HandlerEndPc <= candidate.TryEndPc;
                    if (!containsTry && !containsHandler)
                        continue;

                    int size = candidate.TryEndPc - candidate.TryStartPc;
                    if (size < parentSize)
                    {
                        parent = candidate.Index;
                        parentSize = size;
                    }
                }

                result.Add(new CfgExceptionRegion(
                    child.Index,
                    child.Kind,
                    child.TryStartPc,
                    child.TryEndPc,
                    child.HandlerStartPc,
                    child.HandlerEndPc,
                    child.TryStartBlockId,
                    child.TryEndBlockIdExclusive,
                    child.HandlerStartBlockId,
                    child.HandlerEndBlockIdExclusive,
                    child.CatchTypeToken,
                    parent));
            }

            return result.ToImmutable();
        }

        private static List<int>[] BuildRegionIndexMap(int blockCount, ImmutableArray<CfgExceptionRegion> regions, bool tryRegion)
        {
            var result = new List<int>[blockCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = new List<int>();

            for (int r = 0; r < regions.Length; r++)
            {
                var region = regions[r];
                int start = tryRegion ? region.TryStartBlockId : region.HandlerStartBlockId;
                int end = tryRegion ? region.TryEndBlockIdExclusive : region.HandlerEndBlockIdExclusive;
                for (int b = start; b < end && b < blockCount; b++)
                    result[b].Add(region.Index);
            }

            return result;
        }

        private static bool IsHandlerEntryBlock(int blockId, ImmutableArray<CfgExceptionRegion> regions)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].HandlerStartBlockId == blockId)
                    return true;
            }
            return false;
        }

        private static int FindBlockEndExclusive(GenTreeMethod method, int endPc)
        {
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (method.Blocks[i].StartPc >= endPc)
                    return method.Blocks[i].Id;
            }
            return method.Blocks.Length;
        }

        private static int CompareEdges(CfgEdge x, CfgEdge y)
        {
            int c = x.ToBlockId.CompareTo(y.ToBlockId);
            if (c != 0) return c;
            c = x.FromBlockId.CompareTo(y.FromBlockId);
            if (c != 0) return c;
            c = x.Kind.CompareTo(y.Kind);
            if (c != 0) return c;
            return x.HandlerIndex.CompareTo(y.HandlerIndex);
        }

        private static bool RangesIntersect(int aStart, int aEnd, int bStart, int bEnd)
            => aStart < bEnd && bStart < aEnd;

        private static bool BlockMayThrow(GenTreeBlock block)
        {
            var statements = block.Statements;
            for (int i = 0; i < statements.Length; i++)
            {
                if (TreeMayThrow(statements[i]))
                    return true;
            }
            return false;
        }

        private static bool TreeMayThrow(GenTree node)
        {
            if (node.CanThrow || node.Kind is GenTreeKind.Throw or GenTreeKind.Rethrow)
                return true;

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (TreeMayThrow(node.Operands[i]))
                    return true;
            }

            return false;
        }

        private static void AddEdge(List<CfgEdge>[] succ, List<CfgEdge>[] pred, CfgEdge edge)
        {
            if ((uint)edge.FromBlockId >= (uint)succ.Length || (uint)edge.ToBlockId >= (uint)succ.Length)
                throw new InvalidOperationException($"Invalid CFG edge {edge}.");

            if (!succ[edge.FromBlockId].Contains(edge))
                succ[edge.FromBlockId].Add(edge);
            if (!pred[edge.ToBlockId].Contains(edge))
                pred[edge.ToBlockId].Add(edge);
        }

        private static CfgEdgeKind ClassifyNormalEdge(GenTreeBlock block, int successorBlockId)
        {
            if (block.Statements.Length == 0)
                return CfgEdgeKind.FallThrough;

            var last = block.Statements[block.Statements.Length - 1];
            switch (last.Kind)
            {
                case GenTreeKind.Branch:
                    return CfgEdgeKind.Branch;

                case GenTreeKind.BranchTrue:
                    return last.TargetBlockId == successorBlockId ? CfgEdgeKind.BranchTrue : CfgEdgeKind.FallThrough;

                case GenTreeKind.BranchFalse:
                    return last.TargetBlockId == successorBlockId ? CfgEdgeKind.BranchFalse : CfgEdgeKind.FallThrough;

                default:
                    return CfgEdgeKind.FallThrough;
            }
        }

        private static List<int> ComputeReversePostOrder(ImmutableArray<CfgBlock> blocks)
        {
            var visited = new bool[blocks.Length];
            var post = new List<int>(blocks.Length);

            if (blocks.Length != 0)
                Dfs(0);

            for (int i = 0; i < blocks.Length; i++)
            {
                if (!visited[i])
                    Dfs(i);
            }

            post.Reverse();
            return post;

            void Dfs(int blockId)
            {
                if ((uint)blockId >= (uint)blocks.Length || visited[blockId])
                    return;

                visited[blockId] = true;
                var successors = blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                    Dfs(successors[i].ToBlockId);

                post.Add(blockId);
            }
        }

        private static int[] ComputeImmediateDominators(ImmutableArray<CfgBlock> blocks, List<int> rpo)
        {
            int n = blocks.Length;
            var rpoIndex = new int[n];
            for (int i = 0; i < rpo.Count; i++)
                rpoIndex[rpo[i]] = i;

            var idom = new int[n];
            for (int i = 0; i < idom.Length; i++)
                idom[i] = -1;

            if (n == 0)
                return idom;

            idom[0] = 0;
            bool changed;
            do
            {
                changed = false;

                for (int r = 0; r < rpo.Count; r++)
                {
                    int b = rpo[r];
                    if (b == 0)
                        continue;

                    int newIdom = -1;
                    var predecessors = blocks[b].Predecessors;
                    for (int p = 0; p < predecessors.Length; p++)
                    {
                        int pred = predecessors[p].FromBlockId;
                        if (idom[pred] == -1)
                            continue;

                        newIdom = newIdom == -1
                            ? pred
                            : Intersect(pred, newIdom, idom, rpoIndex);
                    }

                    if (newIdom == -1)
                        newIdom = b;

                    if (idom[b] != newIdom)
                    {
                        idom[b] = newIdom;
                        changed = true;
                    }
                }
            }
            while (changed);

            return idom;
        }

        private static int Intersect(int a, int b, int[] idom, int[] rpoIndex)
        {
            int finger1 = a;
            int finger2 = b;

            while (finger1 != finger2)
            {
                while (rpoIndex[finger1] > rpoIndex[finger2])
                    finger1 = idom[finger1];
                while (rpoIndex[finger2] > rpoIndex[finger1])
                    finger2 = idom[finger2];
            }

            return finger1;
        }

        private static ImmutableArray<ImmutableArray<int>> ComputeDominanceFrontiers(ImmutableArray<CfgBlock> blocks, int[] idom)
        {
            var frontier = new SortedSet<int>[blocks.Length];
            for (int i = 0; i < frontier.Length; i++)
                frontier[i] = new SortedSet<int>();

            for (int b = 0; b < blocks.Length; b++)
            {
                var predecessors = blocks[b].Predecessors;
                if (predecessors.Length < 2)
                    continue;

                for (int p = 0; p < predecessors.Length; p++)
                {
                    int runner = predecessors[p].FromBlockId;
                    while (runner >= 0 && runner != idom[b])
                    {
                        frontier[runner].Add(b);
                        if (idom[runner] == runner)
                            break;
                        runner = idom[runner];
                    }
                }
            }

            var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(blocks.Length);
            for (int i = 0; i < frontier.Length; i++)
                result.Add(frontier[i].ToImmutableArray());
            return result.ToImmutable();
        }

        private static ImmutableArray<ImmutableArray<int>> ComputeDominatorTreeChildren(int[] idom)
        {
            var children = new List<int>[idom.Length];
            for (int i = 0; i < children.Length; i++)
                children[i] = new List<int>();

            for (int b = 0; b < idom.Length; b++)
            {
                int parent = idom[b];
                if (parent >= 0 && parent != b)
                    children[parent].Add(b);
            }

            var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(idom.Length);
            for (int i = 0; i < children.Length; i++)
            {
                children[i].Sort();
                result.Add(children[i].ToImmutableArray());
            }
            return result.ToImmutable();
        }

        public bool Dominates(int dominatorBlockId, int blockId)
            => Dominates(dominatorBlockId, blockId, ImmediateDominators);

        private static bool Dominates(int dominatorBlockId, int blockId, ImmutableArray<int> idom)
        {
            if ((uint)dominatorBlockId >= (uint)idom.Length || (uint)blockId >= (uint)idom.Length)
                return false;
            if (dominatorBlockId == blockId)
                return true;

            int current = blockId;
            while ((uint)current < (uint)idom.Length)
            {
                int parent = idom[current];
                if (parent < 0 || parent == current)
                    return false;
                if (parent == dominatorBlockId)
                    return true;
                current = parent;
            }

            return false;
        }

        private static bool Dominates(int dominatorBlockId, int blockId, int[] idom)
        {
            if ((uint)dominatorBlockId >= (uint)idom.Length || (uint)blockId >= (uint)idom.Length)
                return false;
            if (dominatorBlockId == blockId)
                return true;

            int current = blockId;
            while ((uint)current < (uint)idom.Length)
            {
                int parent = idom[current];
                if (parent < 0 || parent == current)
                    return false;
                if (parent == dominatorBlockId)
                    return true;
                current = parent;
            }

            return false;
        }

        private static ImmutableArray<CfgLoop> ComputeNaturalLoops(ImmutableArray<CfgBlock> blocks, int[] idom)
        {
            var loops = new List<(int header, int latch, ImmutableArray<int> body)>();

            for (int latch = 0; latch < blocks.Length; latch++)
            {
                var successors = blocks[latch].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    var edge = successors[i];
                    if (edge.Kind == CfgEdgeKind.Exception)
                        continue;

                    int header = edge.ToBlockId;
                    if (!Dominates(header, latch, idom))
                        continue;

                    var body = new SortedSet<int> { header, latch };
                    var stack = new Stack<int>();
                    if (latch != header)
                        stack.Push(latch);

                    while (stack.Count != 0)
                    {
                        int blockId = stack.Pop();
                        var predecessors = blocks[blockId].Predecessors;
                        for (int p = 0; p < predecessors.Length; p++)
                        {
                            int pred = predecessors[p].FromBlockId;
                            if (predecessors[p].Kind == CfgEdgeKind.Exception)
                                continue;

                            if (body.Add(pred) && pred != header)
                                stack.Push(pred);
                        }
                    }

                    loops.Add((header, latch, body.ToImmutableArray()));
                }
            }

            loops.Sort(static (a, b) =>
            {
                int c = a.header.CompareTo(b.header);
                return c != 0 ? c : a.latch.CompareTo(b.latch);
            });

            var result = ImmutableArray.CreateBuilder<CfgLoop>(loops.Count);
            for (int i = 0; i < loops.Count; i++)
                result.Add(new CfgLoop(i, loops[i].header, loops[i].latch, loops[i].body));

            return result.ToImmutable();
        }
    }

    internal static class GenTreeCriticalEdgeSplitter
    {
        private readonly struct SplitEdgeInfo
        {
            public readonly int SplitBlockId;
            public readonly int SplitPc;

            public SplitEdgeInfo(int splitBlockId, int splitPc)
            {
                SplitBlockId = splitBlockId;
                SplitPc = splitPc;
            }
        }

        public static GenTreeMethod SplitCriticalEdges(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (method.Blocks.Length == 0)
                return method;

            var cfg = ControlFlowGraph.Build(method, includeExceptionEdges: false);
            var splitEdges = FindCriticalNormalEdges(cfg);
            if (splitEdges.Count == 0)
                return method;

            int nextBlockId = method.Blocks.Length;
            int nextTreeId = NextSyntheticTreeId(method);
            int nextSyntheticPc = FirstSyntheticPc(method);

            var splitInfo = new Dictionary<(int from, int to), SplitEdgeInfo>(splitEdges.Count);
            foreach (var edge in splitEdges)
            {
                var info = new SplitEdgeInfo(nextBlockId++, nextSyntheticPc--);
                splitInfo.Add((edge.FromBlockId, edge.ToBlockId), info);
            }

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(nextBlockId);
            for (int i = 0; i < method.Blocks.Length; i++)
                blocks.Add(RewriteOriginalBlock(method.Blocks[i], splitInfo));

            foreach (var edge in splitEdges)
            {
                var info = splitInfo[(edge.FromBlockId, edge.ToBlockId)];
                var from = method.Blocks[edge.FromBlockId];
                var to = method.Blocks[edge.ToBlockId];
                blocks.Add(CreateSplitBlock(info, from, to, ref nextTreeId));
            }

            return new GenTreeMethod(
                method.Module,
                method.RuntimeMethod,
                method.Function,
                method.ArgTypes,
                method.LocalTypes,
                method.Temps,
                blocks.ToImmutable(),
                method.DirectDependencies,
                method.VirtualDependencies);
        }

        private static List<CfgEdge> FindCriticalNormalEdges(ControlFlowGraph cfg)
        {
            var result = new List<CfgEdge>();
            var seen = new HashSet<(int from, int to)>();

            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                var from = cfg.Blocks[b];
                if (from.Successors.Length <= 1)
                    continue;

                for (int s = 0; s < from.Successors.Length; s++)
                {
                    var edge = from.Successors[s];
                    if (edge.Kind == CfgEdgeKind.Exception)
                        continue;

                    var to = cfg.Blocks[edge.ToBlockId];
                    if (to.Predecessors.Length <= 1)
                        continue;

                    if (seen.Add((edge.FromBlockId, edge.ToBlockId)))
                        result.Add(edge);
                }
            }

            result.Sort(static (a, b) =>
            {
                int c = a.FromBlockId.CompareTo(b.FromBlockId);
                return c != 0 ? c : a.ToBlockId.CompareTo(b.ToBlockId);
            });
            return result;
        }

        private static GenTreeBlock RewriteOriginalBlock(
            GenTreeBlock block,
            Dictionary<(int from, int to), SplitEdgeInfo> splitInfo)
        {
            var successors = ImmutableArray.CreateBuilder<int>(block.SuccessorBlockIds.Length);
            var successorPcs = ImmutableArray.CreateBuilder<int>(block.SuccessorBlockIds.Length);

            for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
            {
                int successor = block.SuccessorBlockIds[i];
                if (splitInfo.TryGetValue((block.Id, successor), out var info))
                {
                    successors.Add(info.SplitBlockId);
                    successorPcs.Add(info.SplitPc);
                }
                else
                {
                    successors.Add(successor);
                    successorPcs.Add(i < block.SuccessorPcs.Length ? block.SuccessorPcs[i] : -1);
                }
            }

            var statements = block.Statements;
            if (statements.Length != 0)
            {
                var last = statements[statements.Length - 1];
                if (TryRewriteTerminator(block, last, splitInfo, out var rewrittenLast))
                {
                    var rewritten = ImmutableArray.CreateBuilder<GenTree>(statements.Length);
                    for (int i = 0; i + 1 < statements.Length; i++)
                        rewritten.Add(statements[i]);
                    rewritten.Add(rewrittenLast);
                    statements = rewritten.ToImmutable();
                }
            }

            return new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                block.JumpKind,
                block.Flags,
                statements,
                successors.ToImmutable(),
                successorPcs.ToImmutable());
        }

        private static bool TryRewriteTerminator(
            GenTreeBlock block,
            GenTree terminator,
            Dictionary<(int from, int to), SplitEdgeInfo> splitInfo,
            out GenTree rewritten)
        {
            if (terminator.TargetBlockId < 0)
            {
                rewritten = terminator;
                return false;
            }

            if (splitInfo.TryGetValue((block.Id, terminator.TargetBlockId), out var targetInfo))
            {
                rewritten = CloneWithTarget(terminator, terminator.Kind, terminator.SourceOp, targetInfo.SplitPc, targetInfo.SplitBlockId);
                return true;
            }

            if (terminator.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                {
                    int successor = block.SuccessorBlockIds[i];
                    if (successor == terminator.TargetBlockId)
                        continue;

                    if (splitInfo.TryGetValue((block.Id, successor), out var fallThroughInfo))
                    {
                        var invertedKind = terminator.Kind == GenTreeKind.BranchTrue
                            ? GenTreeKind.BranchFalse
                            : GenTreeKind.BranchTrue;
                        var invertedOp = terminator.SourceOp == BytecodeOp.Brtrue
                            ? BytecodeOp.Brfalse
                            : terminator.SourceOp == BytecodeOp.Brfalse
                                ? BytecodeOp.Brtrue
                                : terminator.SourceOp;
                        rewritten = CloneWithTarget(terminator, invertedKind, invertedOp, fallThroughInfo.SplitPc, fallThroughInfo.SplitBlockId);
                        return true;
                    }
                }
            }

            rewritten = terminator;
            return false;
        }

        private static GenTreeBlock CreateSplitBlock(
            SplitEdgeInfo info,
            GenTreeBlock from,
            GenTreeBlock to,
            ref int nextTreeId)
        {
            var branch = new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                info.SplitPc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: to.StartPc,
                targetBlockId: to.Id);

            return new GenTreeBlock(
                info.SplitBlockId,
                info.SplitPc,
                info.SplitPc,
                from.ExitStackDepth,
                from.ExitStackDepth,
                GenTreeBlockJumpKind.Always,
                ComputeSplitBlockFlags(from, from.ExitStackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(to.Id),
                ImmutableArray.Create(to.StartPc));
        }

        private static GenTreeBlockFlags ComputeSplitBlockFlags(GenTreeBlock predecessor, int stackDepth)
        {
            var flags = predecessor.Flags & (GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
            if (stackDepth != 0)
                flags |= GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit;
            return flags;
        }

        private static GenTree CloneWithTarget(GenTree source, GenTreeKind kind, BytecodeOp sourceOp, int targetPc, int targetBlockId)
            => new GenTree(
                source.Id,
                kind,
                source.Pc,
                sourceOp,
                source.Type,
                source.StackKind,
                source.Flags,
                source.Operands,
                source.Int32,
                source.Int64,
                source.Text,
                source.RuntimeType,
                source.Field,
                source.Method,
                source.ConvKind,
                source.ConvFlags,
                targetPc,
                targetBlockId);

        private static int NextSyntheticTreeId(GenTreeMethod method)
        {
            int max = -1;
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Visit(statements[s]);
            }
            return max + 1;

            void Visit(GenTree node)
            {
                if (node.Id > max)
                    max = node.Id;
                for (int i = 0; i < node.Operands.Length; i++)
                    Visit(node.Operands[i]);
            }
        }

        private static int FirstSyntheticPc(GenTreeMethod method)
        {
            int min = 0;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                min = Math.Min(min, method.Blocks[i].StartPc);
                min = Math.Min(min, method.Blocks[i].EndPcExclusive);
            }
            return min - 1;
        }
    }

    internal enum SsaSlotKind : byte
    {
        Arg,
        Local,
        Temp,
    }

    internal readonly struct SsaSlot : IEquatable<SsaSlot>, IComparable<SsaSlot>
    {
        public readonly SsaSlotKind Kind;
        public readonly int Index;

        public SsaSlot(SsaSlotKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public bool Equals(SsaSlot other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object? obj) => obj is SsaSlot other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;

        public int CompareTo(SsaSlot other)
        {
            int c = Kind.CompareTo(other.Kind);
            return c != 0 ? c : Index.CompareTo(other.Index);
        }

        public override string ToString()
        {
            char prefix = Kind switch
            {
                SsaSlotKind.Arg => 'a',
                SsaSlotKind.Local => 'l',
                SsaSlotKind.Temp => 't',
                _ => '?',
            };
            return prefix + Index.ToString();
        }
    }

    internal readonly struct SsaValueName : IEquatable<SsaValueName>, IComparable<SsaValueName>
    {
        public readonly SsaSlot Slot;
        public readonly int Version;

        public SsaValueName(SsaSlot slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public bool Equals(SsaValueName other) => Slot.Equals(other.Slot) && Version == other.Version;
        public override bool Equals(object? obj) => obj is SsaValueName other && Equals(other);
        public override int GetHashCode() => (Slot.GetHashCode() * 397) ^ Version;

        public int CompareTo(SsaValueName other)
        {
            int c = Slot.CompareTo(other.Slot);
            return c != 0 ? c : Version.CompareTo(other.Version);
        }

        public override string ToString() => $"{Slot}_{Version}";
    }

    internal readonly struct SsaValueDefinition
    {
        public readonly SsaValueName Name;
        public readonly int DefBlockId;
        public readonly int DefStatementIndex;
        public readonly int DefTreeId;
        public readonly bool IsInitial;
        public readonly bool IsPhi;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;

        public SsaValueDefinition(
            SsaValueName name,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            bool isInitial,
            bool isPhi,
            RuntimeType? type,
            GenStackKind stackKind)
        {
            Name = name;
            DefBlockId = defBlockId;
            DefStatementIndex = defStatementIndex;
            DefTreeId = defTreeId;
            IsInitial = isInitial;
            IsPhi = isPhi;
            Type = type;
            StackKind = stackKind;
        }
    }

    internal readonly struct SsaSlotInfo
    {
        public readonly SsaSlot Slot;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;
        public readonly bool AddressExposed;

        public SsaSlotInfo(SsaSlot slot, RuntimeType? type, GenStackKind stackKind, bool addressExposed)
        {
            Slot = slot;
            Type = type;
            StackKind = stackKind;
            AddressExposed = addressExposed;
        }
    }

    internal readonly struct SsaPhiInput
    {
        public readonly int PredecessorBlockId;
        public readonly SsaValueName Value;

        public SsaPhiInput(int predecessorBlockId, SsaValueName value)
        {
            PredecessorBlockId = predecessorBlockId;
            Value = value;
        }
    }

    internal sealed class SsaPhi
    {
        public int BlockId { get; }
        public SsaSlot Slot { get; }
        public SsaValueName Target { get; }
        public ImmutableArray<SsaPhiInput> Inputs { get; }

        public SsaPhi(int blockId, SsaSlot slot, SsaValueName target, ImmutableArray<SsaPhiInput> inputs)
        {
            BlockId = blockId;
            Slot = slot;
            Target = target;
            Inputs = inputs.IsDefault ? ImmutableArray<SsaPhiInput>.Empty : inputs;
        }
    }

    internal sealed class SsaTree
    {
        public GenTree Source { get; }
        public GenTreeKind Kind => Source.Kind;
        public ImmutableArray<SsaTree> Operands { get; }
        public SsaValueName? Value { get; }
        public SsaValueName? StoreTarget { get; }

        public SsaTree(GenTree source, ImmutableArray<SsaTree> operands, SsaValueName? value = null, SsaValueName? storeTarget = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Operands = operands.IsDefault ? ImmutableArray<SsaTree>.Empty : operands;
            Value = value;
            StoreTarget = storeTarget;
        }

        public override string ToString() => SsaDumper.FormatTree(this);
    }

    internal sealed class SsaBlock
    {
        public CfgBlock CfgBlock { get; }
        public ImmutableArray<SsaPhi> Phis { get; }
        public ImmutableArray<SsaTree> Statements { get; }

        public int Id => CfgBlock.Id;

        public SsaBlock(CfgBlock cfgBlock, ImmutableArray<SsaPhi> phis, ImmutableArray<SsaTree> statements)
        {
            CfgBlock = cfgBlock ?? throw new ArgumentNullException(nameof(cfgBlock));
            Phis = phis.IsDefault ? ImmutableArray<SsaPhi>.Empty : phis;
            Statements = statements.IsDefault ? ImmutableArray<SsaTree>.Empty : statements;
        }
    }

    internal sealed class SsaMethod
    {
        public GenTreeMethod GenTreeMethod { get; }
        public ControlFlowGraph Cfg { get; }
        public ImmutableArray<SsaSlotInfo> Slots { get; }
        public ImmutableArray<SsaValueName> InitialValues { get; }
        public ImmutableArray<SsaValueDefinition> ValueDefinitions { get; }
        public ImmutableArray<SsaBlock> Blocks { get; }

        public SsaMethod(
            GenTreeMethod genTreeMethod,
            ControlFlowGraph cfg,
            ImmutableArray<SsaSlotInfo> slots,
            ImmutableArray<SsaValueName> initialValues,
            ImmutableArray<SsaValueDefinition> valueDefinitions,
            ImmutableArray<SsaBlock> blocks)
        {
            GenTreeMethod = genTreeMethod ?? throw new ArgumentNullException(nameof(genTreeMethod));
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Slots = slots.IsDefault ? ImmutableArray<SsaSlotInfo>.Empty : slots;
            InitialValues = initialValues.IsDefault ? ImmutableArray<SsaValueName>.Empty : initialValues;
            ValueDefinitions = valueDefinitions.IsDefault ? ImmutableArray<SsaValueDefinition>.Empty : valueDefinitions;
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
        }
    }

    internal sealed class SsaProgram
    {
        public ImmutableArray<SsaMethod> Methods { get; }
        public IReadOnlyDictionary<int, SsaMethod> MethodsByRuntimeMethodId { get; }

        public SsaProgram(ImmutableArray<SsaMethod> methods)
        {
            Methods = methods.IsDefault ? ImmutableArray<SsaMethod>.Empty : methods;
            var map = new Dictionary<int, SsaMethod>();
            for (int i = 0; i < Methods.Length; i++)
                map[Methods[i].GenTreeMethod.RuntimeMethod.MethodId] = Methods[i];
            MethodsByRuntimeMethodId = map;
        }
    }

    internal static class GenTreeSsaBuilder
    {
        public static SsaProgram BuildProgram(GenTreeProgram program, bool includeExceptionEdges = true, bool validate = true, bool optimize = true)
        {
            if (program is null) throw new ArgumentNullException(nameof(program));

            var methods = ImmutableArray.CreateBuilder<SsaMethod>(program.Methods.Length);
            for (int i = 0; i < program.Methods.Length; i++)
                methods.Add(BuildMethod(program.Methods[i], includeExceptionEdges, validate, optimize));
            return new SsaProgram(methods.ToImmutable());
        }

        public static SsaMethod BuildMethod(GenTreeMethod method, bool includeExceptionEdges = true, bool validate = true, bool optimize = true)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            method = GenTreeCriticalEdgeSplitter.SplitCriticalEdges(method);

            var cfg = ControlFlowGraph.Build(method, includeExceptionEdges);
            var slotTable = SlotTable.Build(method, cfg);
            var liveness = Liveness.Build(cfg, slotTable.PromotableSlots);
            var phis = InsertPhis(cfg, slotTable.PromotableSlots, liveness);
            var rename = new RenameState(cfg, slotTable, phis);
            var blocks = rename.Run();
            var initialValues = rename.InitialValues.ToImmutableArray();
            var valueDefinitions = BuildValueDefinitions(slotTable, initialValues, blocks);

            var result = new SsaMethod(
                method,
                cfg,
                slotTable.AllSlots.ToImmutableArray(),
                initialValues,
                valueDefinitions,
                blocks);

            if (optimize)
                result = SsaOptimizer.OptimizeMethod(result, SsaOptimizationOptions.DefaultWithoutValidation);

            if (validate)
                SsaVerifier.Verify(result);

            return result;
        }

        private static ImmutableArray<SsaValueDefinition> BuildValueDefinitions(
            SlotTable slotTable,
            ImmutableArray<SsaValueName> initialValues,
            ImmutableArray<SsaBlock> blocks)
        {
            var definitions = new List<SsaValueDefinition>();
            var seen = new HashSet<SsaValueName>();

            for (int i = 0; i < initialValues.Length; i++)
            {
                var name = initialValues[i];
                var info = slotTable.GetInfo(name.Slot);
                Add(new SsaValueDefinition(name, -1, -1, -1, true, false, info.Type, info.StackKind));
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    var info = slotTable.GetInfo(phi.Target.Slot);
                    Add(new SsaValueDefinition(phi.Target, block.Id, -1, -1, false, true, info.Type, info.StackKind));
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    CollectStatementDefinitions(block.Statements[s], block.Id, s);
            }

            definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            return definitions.ToImmutableArray();

            void CollectStatementDefinitions(SsaTree tree, int blockId, int statementIndex)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStatementDefinitions(tree.Operands[i], blockId, statementIndex);

                if (tree.StoreTarget.HasValue)
                {
                    var name = tree.StoreTarget.Value;
                    var info = slotTable.GetInfo(name.Slot);
                    Add(new SsaValueDefinition(
                        name,
                        blockId,
                        statementIndex,
                        tree.Source.Id,
                        false,
                        false,
                        info.Type,
                        info.StackKind));
                }
            }

            void Add(SsaValueDefinition definition)
            {
                if (!seen.Add(definition.Name))
                    throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                definitions.Add(definition);
            }
        }

        private static MutablePhi[][] InsertPhis(ControlFlowGraph cfg, ImmutableArray<SsaSlot> promotableSlots, Liveness liveness)
        {
            int n = cfg.Blocks.Length;
            var byBlock = new List<MutablePhi>[n];
            for (int i = 0; i < n; i++)
                byBlock[i] = new List<MutablePhi>();

            var hasPhi = new HashSet<(SsaSlot slot, int blockId)>();

            for (int s = 0; s < promotableSlots.Length; s++)
            {
                var slot = promotableSlots[s];
                var work = new Queue<int>();
                var inWork = new HashSet<int>();

                for (int b = 0; b < n; b++)
                {
                    if (liveness.Defs[b].Contains(slot))
                    {
                        work.Enqueue(b);
                        inWork.Add(b);
                    }
                }

                while (work.Count != 0)
                {
                    int x = work.Dequeue();
                    inWork.Remove(x);

                    var df = cfg.DominanceFrontiers[x];
                    for (int i = 0; i < df.Length; i++)
                    {
                        int y = df[i];
                        if (!liveness.LiveIn[y].Contains(slot))
                            continue;

                        if (hasPhi.Add((slot, y)))
                        {
                            byBlock[y].Add(new MutablePhi(y, slot));
                            if (!liveness.Defs[y].Contains(slot) && inWork.Add(y))
                                work.Enqueue(y);
                        }
                    }
                }
            }

            var result = new MutablePhi[n][];
            for (int i = 0; i < n; i++)
            {
                byBlock[i].Sort((a, b) => a.Slot.CompareTo(b.Slot));
                result[i] = byBlock[i].ToArray();
            }
            return result;
        }

        private sealed class SlotTable
        {
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _infoBySlot;
            private readonly HashSet<SsaSlot> _promotable;

            public ImmutableArray<SsaSlotInfo> AllSlots { get; }
            public ImmutableArray<SsaSlot> PromotableSlots { get; }

            private SlotTable(ImmutableArray<SsaSlotInfo> allSlots, ImmutableArray<SsaSlot> promotableSlots)
            {
                AllSlots = allSlots;
                PromotableSlots = promotableSlots;
                _infoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>();
                _promotable = new HashSet<SsaSlot>();

                for (int i = 0; i < allSlots.Length; i++)
                    _infoBySlot[allSlots[i].Slot] = allSlots[i];
                for (int i = 0; i < promotableSlots.Length; i++)
                    _promotable.Add(promotableSlots[i]);
            }

            public bool IsPromotable(SsaSlot slot) => _promotable.Contains(slot);

            public SsaSlotInfo GetInfo(SsaSlot slot)
            {
                if (_infoBySlot.TryGetValue(slot, out var info))
                    return info;

                return new SsaSlotInfo(slot, type: null, stackKind: GenStackKind.Unknown, addressExposed: false);
            }

            public static SlotTable Build(GenTreeMethod method, ControlFlowGraph cfg)
            {
                if (cfg is null) throw new ArgumentNullException(nameof(cfg));

                var addressExposed = new HashSet<SsaSlot>();
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CollectAddressExposed(statements[s], addressExposed);
                }

                var ehExposed = BuildEhExposedSlots(cfg);

                var slots = new List<SsaSlotInfo>();
                var promotable = new List<SsaSlot>();

                for (int i = 0; i < method.ArgTypes.Length; i++)
                    AddSlot(new SsaSlot(SsaSlotKind.Arg, i), method.ArgTypes[i], StackKindOf(method.ArgTypes[i]));

                for (int i = 0; i < method.LocalTypes.Length; i++)
                    AddSlot(new SsaSlot(SsaSlotKind.Local, i), method.LocalTypes[i], StackKindOf(method.LocalTypes[i]));

                for (int i = 0; i < method.Temps.Length; i++)
                {
                    var t = method.Temps[i];
                    AddSlot(new SsaSlot(SsaSlotKind.Temp, t.Index), t.Type, t.StackKind);
                }

                slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                promotable.Sort();

                return new SlotTable(slots.ToImmutableArray(), promotable.ToImmutableArray());

                void AddSlot(SsaSlot slot, RuntimeType? type, GenStackKind stackKind)
                {
                    bool exposed = addressExposed.Contains(slot) || ehExposed.Contains(slot);
                    var info = new SsaSlotInfo(slot, type, stackKind, exposed);
                    slots.Add(info);

                    if (!exposed && IsPromotableStorageSlot(type, stackKind))
                        promotable.Add(slot);
                }

                static bool IsPromotableStorageSlot(RuntimeType? type, GenStackKind stackKind)
                {
                    if (stackKind is GenStackKind.Void or GenStackKind.Unknown)
                        return false;

                    if (type is not null && type.Kind == RuntimeTypeKind.TypeParam)
                        return false;

                    if (type is null)
                        return stackKind is
                            GenStackKind.I4 or
                            GenStackKind.I8 or
                            GenStackKind.R8 or
                            GenStackKind.NativeInt or
                            GenStackKind.NativeUInt or
                            GenStackKind.Ref or
                            GenStackKind.Ptr or
                            GenStackKind.ByRef or
                            GenStackKind.Null;

                    return MachineAbi.IsPhysicallyPromotableStorage(type, stackKind);
                }
            }

            private static HashSet<SsaSlot> BuildEhExposedSlots(ControlFlowGraph cfg)
            {
                var exposed = new HashSet<SsaSlot>();
                if (cfg.ExceptionRegions.Length == 0)
                    return exposed;

                int blockCount = cfg.Blocks.Length;
                var uses = NewSetArray(blockCount);
                var defs = NewSetArray(blockCount);
                var touched = NewSetArray(blockCount);

                for (int b = 0; b < blockCount; b++)
                {
                    var statements = cfg.Blocks[b].SourceBlock.Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CollectUseDefAndTouch(statements[s], uses[b], defs[b], touched[b]);
                }

                var liveIn = NewSetArray(blockCount);
                var liveOut = NewSetArray(blockCount);
                bool changed;
                do
                {
                    changed = false;
                    for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                    {
                        int b = cfg.ReversePostOrder[r];

                        var newOut = new HashSet<SsaSlot>();
                        var successors = cfg.Blocks[b].Successors;
                        for (int i = 0; i < successors.Length; i++)
                            newOut.UnionWith(liveIn[successors[i].ToBlockId]);

                        var newIn = new HashSet<SsaSlot>(newOut);
                        newIn.ExceptWith(defs[b]);
                        newIn.UnionWith(uses[b]);

                        if (!liveOut[b].SetEquals(newOut))
                        {
                            liveOut[b] = newOut;
                            changed = true;
                        }

                        if (!liveIn[b].SetEquals(newIn))
                        {
                            liveIn[b] = newIn;
                            changed = true;
                        }
                    }
                }
                while (changed);

                for (int b = 0; b < blockCount; b++)
                {
                    var successors = cfg.Blocks[b].Successors;
                    for (int i = 0; i < successors.Length; i++)
                    {
                        var edge = successors[i];
                        if (edge.Kind != CfgEdgeKind.Exception)
                            continue;

                        exposed.UnionWith(liveOut[b]);
                        exposed.UnionWith(liveIn[edge.ToBlockId]);
                    }
                }

                var touchedFunclets = new Dictionary<SsaSlot, int>();
                for (int b = 0; b < blockCount; b++)
                {
                    int funcletId = FuncletIdentity(cfg.Blocks[b]);
                    foreach (var slot in touched[b])
                    {
                        if (!touchedFunclets.TryGetValue(slot, out int previous))
                        {
                            touchedFunclets.Add(slot, funcletId);
                        }
                        else if (previous != funcletId)
                        {
                            exposed.Add(slot);
                        }
                    }
                }

                return exposed;

                static HashSet<SsaSlot>[] NewSetArray(int count)
                {
                    var result = new HashSet<SsaSlot>[count];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = new HashSet<SsaSlot>();
                    return result;
                }

                static int FuncletIdentity(CfgBlock block)
                    => block.HandlerRegionIndexes.Length == 0
                        ? 0
                        : block.HandlerRegionIndexes[block.HandlerRegionIndexes.Length - 1] + 1;

                static void CollectUseDefAndTouch(GenTree node, HashSet<SsaSlot> uses, HashSet<SsaSlot> defs, HashSet<SsaSlot> touched)
                {
                    if (TryGetStoreSlot(node, out var storeSlot))
                    {
                        for (int i = 0; i < node.Operands.Length; i++)
                            CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);

                        defs.Add(storeSlot);
                        touched.Add(storeSlot);
                        return;
                    }

                    if (TryGetLoadSlot(node, out var loadSlot))
                    {
                        if (!defs.Contains(loadSlot))
                            uses.Add(loadSlot);
                        touched.Add(loadSlot);
                        return;
                    }

                    for (int i = 0; i < node.Operands.Length; i++)
                        CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);
                }
            }

            private static void CollectAddressExposed(GenTree node, HashSet<SsaSlot> addressExposed)
            {
                switch (node.Kind)
                {
                    case GenTreeKind.LocalAddr:
                        addressExposed.Add(new SsaSlot(SsaSlotKind.Local, node.Int32));
                        break;

                    case GenTreeKind.ArgAddr:
                        addressExposed.Add(new SsaSlot(SsaSlotKind.Arg, node.Int32));
                        break;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    CollectAddressExposed(node.Operands[i], addressExposed);
            }
        }

        private sealed class Liveness
        {
            public HashSet<SsaSlot>[] Uses { get; }
            public HashSet<SsaSlot>[] Defs { get; }
            public HashSet<SsaSlot>[] LiveIn { get; }
            public HashSet<SsaSlot>[] LiveOut { get; }

            private Liveness(HashSet<SsaSlot>[] uses, HashSet<SsaSlot>[] defs, HashSet<SsaSlot>[] liveIn, HashSet<SsaSlot>[] liveOut)
            {
                Uses = uses;
                Defs = defs;
                LiveIn = liveIn;
                LiveOut = liveOut;
            }

            public static Liveness Build(ControlFlowGraph cfg, ImmutableArray<SsaSlot> promotableSlots)
            {
                int n = cfg.Blocks.Length;
                var promotable = new HashSet<SsaSlot>(promotableSlots);
                var uses = NewSetArray(n);
                var defs = NewSetArray(n);
                var liveIn = NewSetArray(n);
                var liveOut = NewSetArray(n);

                for (int b = 0; b < n; b++)
                {
                    var statements = cfg.Blocks[b].SourceBlock.Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CollectUseDef(statements[s], promotable, uses[b], defs[b]);
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                    {
                        int b = cfg.ReversePostOrder[r];

                        var newOut = new HashSet<SsaSlot>();
                        var successors = cfg.Blocks[b].Successors;
                        for (int i = 0; i < successors.Length; i++)
                            newOut.UnionWith(liveIn[successors[i].ToBlockId]);

                        var newIn = new HashSet<SsaSlot>(newOut);
                        newIn.ExceptWith(defs[b]);
                        newIn.UnionWith(uses[b]);

                        if (!SetEquals(liveOut[b], newOut))
                        {
                            liveOut[b] = newOut;
                            changed = true;
                        }

                        if (!SetEquals(liveIn[b], newIn))
                        {
                            liveIn[b] = newIn;
                            changed = true;
                        }
                    }
                }
                while (changed);

                return new Liveness(uses, defs, liveIn, liveOut);
            }

            private static HashSet<SsaSlot>[] NewSetArray(int count)
            {
                var result = new HashSet<SsaSlot>[count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new HashSet<SsaSlot>();
                return result;
            }

            private static bool SetEquals(HashSet<SsaSlot> left, HashSet<SsaSlot> right)
                => left.Count == right.Count && left.SetEquals(right);

            private static void CollectUseDef(GenTree node, HashSet<SsaSlot> promotable, HashSet<SsaSlot> uses, HashSet<SsaSlot> defs)
            {
                if (TryGetStoreSlot(node, out var storeSlot))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                        CollectUseDef(node.Operands[i], promotable, uses, defs);

                    if (promotable.Contains(storeSlot))
                        defs.Add(storeSlot);
                    return;
                }

                if (TryGetLoadSlot(node, out var loadSlot))
                {
                    if (promotable.Contains(loadSlot) && !defs.Contains(loadSlot))
                        uses.Add(loadSlot);
                    return;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    CollectUseDef(node.Operands[i], promotable, uses, defs);
            }
        }

        private sealed class RenameState
        {
            private readonly ControlFlowGraph _cfg;
            private readonly SlotTable _slots;
            private readonly MutablePhi[][] _phis;
            private readonly Dictionary<SsaSlot, int> _nextVersion = new();
            private readonly Dictionary<SsaSlot, Stack<SsaValueName>> _stacks = new();
            private readonly List<SsaValueName> _initialValues = new();
            private readonly List<SsaTree>[] _renamedStatements;
            private readonly bool[] _visited;

            public IReadOnlyList<SsaValueName> InitialValues => _initialValues;

            public RenameState(ControlFlowGraph cfg, SlotTable slots, MutablePhi[][] phis)
            {
                _cfg = cfg;
                _slots = slots;
                _phis = phis;
                _renamedStatements = new List<SsaTree>[cfg.Blocks.Length];
                _visited = new bool[cfg.Blocks.Length];
                for (int i = 0; i < _renamedStatements.Length; i++)
                    _renamedStatements[i] = new List<SsaTree>();

                for (int i = 0; i < slots.PromotableSlots.Length; i++)
                {
                    var slot = slots.PromotableSlots[i];
                    var initial = new SsaValueName(slot, 0);
                    _nextVersion[slot] = 0;
                    _stacks[slot] = new Stack<SsaValueName>();
                    _stacks[slot].Push(initial);
                    _initialValues.Add(initial);
                }

                _initialValues.Sort();
            }

            public ImmutableArray<SsaBlock> Run()
            {
                if (_cfg.Blocks.Length != 0)
                    RenameBlock(0);

                for (int i = 0; i < _cfg.Blocks.Length; i++)
                {
                    if (!_visited[i])
                        RenameBlock(i);
                }

                _initialValues.Sort();

                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(_cfg.Blocks.Length);
                for (int b = 0; b < _cfg.Blocks.Length; b++)
                {
                    var phiBuilder = ImmutableArray.CreateBuilder<SsaPhi>(_phis[b].Length);
                    for (int i = 0; i < _phis[b].Length; i++)
                        phiBuilder.Add(_phis[b][i].Freeze(_cfg.Blocks[b]));

                    blocks.Add(new SsaBlock(
                        _cfg.Blocks[b],
                        phiBuilder.ToImmutable(),
                        _renamedStatements[b].ToImmutableArray()));
                }
                return blocks.ToImmutable();
            }

            private void RenameBlock(int blockId)
            {
                if (_visited[blockId])
                    return;
                _visited[blockId] = true;

                var pushed = new List<SsaSlot>();

                for (int i = 0; i < _phis[blockId].Length; i++)
                {
                    var phi = _phis[blockId][i];
                    phi.Target = NewVersion(phi.Slot);
                    Push(phi.Slot, phi.Target);
                    pushed.Add(phi.Slot);
                }

                var statements = _cfg.Blocks[blockId].SourceBlock.Statements;
                for (int i = 0; i < statements.Length; i++)
                {
                    var renamed = RenameTree(statements[i], pushed);
                    _renamedStatements[blockId].Add(renamed);
                }

                var successors = _cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    int succ = successors[i].ToBlockId;
                    for (int p = 0; p < _phis[succ].Length; p++)
                    {
                        var phi = _phis[succ][p];
                        phi.SetInput(blockId, Top(phi.Slot));
                    }
                }

                var children = _cfg.DominatorTreeChildren[blockId];
                for (int i = 0; i < children.Length; i++)
                    RenameBlock(children[i]);

                for (int i = pushed.Count - 1; i >= 0; i--)
                    Pop(pushed[i]);
            }

            private SsaTree RenameTree(GenTree node, List<SsaSlot> pushed)
            {
                if (TryGetLoadSlot(node, out var loadSlot) && _slots.IsPromotable(loadSlot))
                    return new SsaTree(node, ImmutableArray<SsaTree>.Empty, value: Top(loadSlot));

                var operands = ImmutableArray.CreateBuilder<SsaTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                    operands.Add(RenameTree(node.Operands[i], pushed));

                if (TryGetStoreSlot(node, out var storeSlot) && _slots.IsPromotable(storeSlot))
                {
                    var target = NewVersion(storeSlot);
                    Push(storeSlot, target);
                    pushed.Add(storeSlot);
                    return new SsaTree(node, operands.ToImmutable(), storeTarget: target);
                }

                return new SsaTree(node, operands.ToImmutable());
            }

            private SsaValueName Top(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                {
                    var initial = new SsaValueName(slot, 0);
                    _nextVersion[slot] = Math.Max(_nextVersion.TryGetValue(slot, out int n) ? n : 0, 0);
                    stack = new Stack<SsaValueName>();
                    stack.Push(initial);
                    _stacks[slot] = stack;
                    if (!_initialValues.Contains(initial))
                        _initialValues.Add(initial);
                    return initial;
                }

                return stack.Peek();
            }

            private SsaValueName NewVersion(SsaSlot slot)
            {
                int next = _nextVersion.TryGetValue(slot, out int current) ? current + 1 : 1;
                _nextVersion[slot] = next;
                return new SsaValueName(slot, next);
            }

            private void Push(SsaSlot slot, SsaValueName value)
            {
                if (!_stacks.TryGetValue(slot, out var stack))
                {
                    stack = new Stack<SsaValueName>();
                    _stacks[slot] = stack;
                }
                stack.Push(value);
            }

            private void Pop(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                    throw new InvalidOperationException($"SSA rename stack underflow for {slot}.");
                stack.Pop();
            }
        }

        private sealed class MutablePhi
        {
            private readonly Dictionary<int, SsaValueName> _inputs = new();

            public int BlockId { get; }
            public SsaSlot Slot { get; }
            public SsaValueName Target { get; set; }

            public MutablePhi(int blockId, SsaSlot slot)
            {
                BlockId = blockId;
                Slot = slot;
            }

            public void SetInput(int predecessorBlockId, SsaValueName value)
            {
                _inputs[predecessorBlockId] = value;
            }

            public SsaPhi Freeze(CfgBlock block)
            {
                var inputs = ImmutableArray.CreateBuilder<SsaPhiInput>(block.Predecessors.Length);
                var seen = new HashSet<int>();

                for (int i = 0; i < block.Predecessors.Length; i++)
                {
                    int pred = block.Predecessors[i].FromBlockId;
                    if (!seen.Add(pred))
                        continue;

                    if (!_inputs.TryGetValue(pred, out var value))
                        throw new InvalidOperationException($"Missing SSA phi input for {Slot} in B{BlockId} from B{pred}.");

                    inputs.Add(new SsaPhiInput(pred, value));
                }

                return new SsaPhi(BlockId, Slot, Target, inputs.ToImmutable());
            }
        }

        private static bool TryGetLoadSlot(GenTree node, out SsaSlot slot)
        {
            switch (node.Kind)
            {
                case GenTreeKind.Arg:
                    slot = new SsaSlot(SsaSlotKind.Arg, node.Int32);
                    return true;
                case GenTreeKind.Local:
                    slot = new SsaSlot(SsaSlotKind.Local, node.Int32);
                    return true;
                case GenTreeKind.Temp:
                    slot = new SsaSlot(SsaSlotKind.Temp, node.Int32);
                    return true;
                default:
                    slot = default;
                    return false;
            }
        }

        private static bool TryGetStoreSlot(GenTree node, out SsaSlot slot)
        {
            switch (node.Kind)
            {
                case GenTreeKind.StoreArg:
                    slot = new SsaSlot(SsaSlotKind.Arg, node.Int32);
                    return true;
                case GenTreeKind.StoreLocal:
                    slot = new SsaSlot(SsaSlotKind.Local, node.Int32);
                    return true;
                case GenTreeKind.StoreTemp:
                    slot = new SsaSlot(SsaSlotKind.Temp, node.Int32);
                    return true;
                default:
                    slot = default;
                    return false;
            }
        }

        private static GenStackKind StackKindOf(RuntimeType? type)
        {
            if (type is null)
                return GenStackKind.Unknown;

            if (type.Namespace == "System" && type.Name == "Void")
                return GenStackKind.Void;

            if (type.IsReferenceType)
                return GenStackKind.Ref;

            if (type.Kind == RuntimeTypeKind.Pointer)
                return GenStackKind.Ptr;

            if (type.Kind == RuntimeTypeKind.ByRef)
                return GenStackKind.ByRef;

            if (type.Kind == RuntimeTypeKind.TypeParam)
                return GenStackKind.Value;

            if (type.Kind == RuntimeTypeKind.Enum)
                return type.SizeOf <= 4 ? GenStackKind.I4 : GenStackKind.I8;

            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return GenStackKind.I4;
                    case "Int64":
                    case "UInt64":
                        return GenStackKind.I8;
                    case "Single":
                    case "Double":
                        return GenStackKind.R8;
                    case "IntPtr":
                        return GenStackKind.NativeInt;
                    case "UIntPtr":
                        return GenStackKind.NativeUInt;
                }
            }

            return GenStackKind.Value;
        }
    }

    internal sealed class SsaOptimizationOptions
    {
        public static SsaOptimizationOptions Default => new SsaOptimizationOptions();
        public static SsaOptimizationOptions DefaultWithoutValidation => new SsaOptimizationOptions { Validate = false };

        public bool Validate { get; set; } = true;
        public bool PropagateCopies { get; set; } = true;
        public bool PropagateConstants { get; set; } = true;
        public bool FoldConstants { get; set; } = true;
        public bool RemoveDeadDefinitions { get; set; } = true;
        public int MaxIterations { get; set; } = 8;
    }

    internal static class SsaOptimizer
    {
        public static SsaProgram OptimizeProgram(SsaProgram program, SsaOptimizationOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= SsaOptimizationOptions.Default;

            var methods = ImmutableArray.CreateBuilder<SsaMethod>(program.Methods.Length);
            for (int i = 0; i < program.Methods.Length; i++)
                methods.Add(OptimizeMethod(program.Methods[i], options));

            return new SsaProgram(methods.ToImmutable());
        }

        public static SsaMethod OptimizeMethod(SsaMethod method, SsaOptimizationOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= SsaOptimizationOptions.Default;
            if (options.MaxIterations <= 0)
                return method;

            var optimizer = new MethodOptimizer(method, options);
            var result = optimizer.Run();

            if (options.Validate)
                SsaVerifier.Verify(result);

            return result;
        }

        private enum ConstKind : byte
        {
            I4,
            I8,
            Null,
        }

        private readonly struct ConstValue : IEquatable<ConstValue>
        {
            public readonly ConstKind Kind;
            public readonly int I4;
            public readonly long I8;

            private ConstValue(ConstKind kind, int i4, long i8)
            {
                Kind = kind;
                I4 = i4;
                I8 = i8;
            }

            public static ConstValue ForI4(int value) => new ConstValue(ConstKind.I4, value, value);
            public static ConstValue ForI8(long value) => new ConstValue(ConstKind.I8, unchecked((int)value), value);
            public static ConstValue Null => new ConstValue(ConstKind.Null, 0, 0);

            public bool Equals(ConstValue other) => Kind == other.Kind && I4 == other.I4 && I8 == other.I8;
            public override bool Equals(object? obj) => obj is ConstValue other && Equals(other);
            public override int GetHashCode() => ((int)Kind * 397) ^ I4 ^ I8.GetHashCode();
        }

        private enum ValueFactKind : byte
        {
            Unknown,
            Constant,
            Alias,
        }

        private readonly struct ValueFact : IEquatable<ValueFact>
        {
            public readonly ValueFactKind Kind;
            public readonly ConstValue Constant;
            public readonly SsaValueName Alias;

            private ValueFact(ValueFactKind kind, ConstValue constant, SsaValueName alias)
            {
                Kind = kind;
                Constant = constant;
                Alias = alias;
            }

            public static ValueFact Unknown => default;
            public static ValueFact ForConstant(ConstValue constant) => new ValueFact(ValueFactKind.Constant, constant, default);
            public static ValueFact ForAlias(SsaValueName alias) => new ValueFact(ValueFactKind.Alias, default, alias);

            public bool Equals(ValueFact other)
            {
                if (Kind != other.Kind)
                    return false;

                return Kind switch
                {
                    ValueFactKind.Constant => Constant.Equals(other.Constant),
                    ValueFactKind.Alias => Alias.Equals(other.Alias),
                    _ => true,
                };
            }

            public override bool Equals(object? obj) => obj is ValueFact other && Equals(other);
            public override int GetHashCode() => Kind switch
            {
                ValueFactKind.Constant => ((int)Kind * 397) ^ Constant.GetHashCode(),
                ValueFactKind.Alias => ((int)Kind * 397) ^ Alias.GetHashCode(),
                _ => 0,
            };
        }

        private sealed class OptimizationResult
        {
            public ImmutableArray<SsaBlock> Blocks { get; }
            public bool Changed { get; }

            public OptimizationResult(ImmutableArray<SsaBlock> blocks, bool changed)
            {
                Blocks = blocks;
                Changed = changed;
            }
        }

        private sealed class MethodOptimizer
        {
            private readonly SsaMethod _original;
            private readonly SsaOptimizationOptions _options;
            private int _nextSyntheticTreeId;

            public MethodOptimizer(SsaMethod method, SsaOptimizationOptions options)
            {
                _original = method;
                _options = options;
                _nextSyntheticTreeId = MaxTreeId(method) + 1;
            }

            public SsaMethod Run()
            {
                var current = _original;

                for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
                {
                    var facts = ComputeFacts(current);
                    var rewrite = Rewrite(current, facts);
                    var afterRewrite = WithBlocks(current, rewrite.Blocks);

                    OptimizationResult dce = _options.RemoveDeadDefinitions
                        ? EliminateDeadDefinitions(afterRewrite)
                        : new OptimizationResult(afterRewrite.Blocks, changed: false);

                    var next = WithBlocks(afterRewrite, dce.Blocks);
                    bool changed = rewrite.Changed || dce.Changed;
                    current = next;

                    if (!changed)
                        break;
                }

                var definitions = BuildValueDefinitions(current.Slots, current.InitialValues, current.Blocks);
                return new SsaMethod(
                    current.GenTreeMethod,
                    current.Cfg,
                    current.Slots,
                    current.InitialValues,
                    definitions,
                    current.Blocks);
            }

            private Dictionary<SsaValueName, ValueFact> ComputeFacts(SsaMethod method)
            {
                var facts = new Dictionary<SsaValueName, ValueFact>();
                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                    facts[method.ValueDefinitions[i].Name] = ValueFact.Unknown;

                bool changed;
                int iteration = 0;
                do
                {
                    changed = false;

                    for (int b = 0; b < method.Blocks.Length; b++)
                    {
                        var block = method.Blocks[b];
                        for (int p = 0; p < block.Phis.Length; p++)
                        {
                            var phi = block.Phis[p];
                            var fact = EvaluatePhi(phi, facts);
                            if (SetFact(facts, phi.Target, fact))
                                changed = true;
                        }

                        for (int s = 0; s < block.Statements.Length; s++)
                            CollectStoreFacts(block.Statements[s], facts, ref changed);
                    }

                    iteration++;
                }
                while (changed && iteration < _options.MaxIterations);

                return facts;
            }

            private ValueFact EvaluatePhi(SsaPhi phi, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (phi.Inputs.Length == 0)
                    return ValueFact.Unknown;

                ValueFact? merged = null;
                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    var input = NormalizeValue(phi.Inputs[i].Value, facts);
                    if (input.Kind == ValueFactKind.Alias && input.Alias.Equals(phi.Target))
                        input = ValueFact.Unknown;

                    if (!merged.HasValue)
                    {
                        merged = input;
                        continue;
                    }

                    if (!merged.Value.Equals(input))
                        return ValueFact.Unknown;
                }

                return merged.GetValueOrDefault(ValueFact.Unknown);
            }

            private void CollectStoreFacts(SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, ref bool changed)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStoreFacts(tree.Operands[i], facts, ref changed);

                if (!tree.StoreTarget.HasValue)
                    return;

                var value = tree.Operands.Length == 1
                    ? EvaluateTree(tree.Operands[0], facts)
                    : ValueFact.Unknown;

                if (value.Kind == ValueFactKind.Alias && value.Alias.Equals(tree.StoreTarget.Value))
                    value = ValueFact.Unknown;

                if (SetFact(facts, tree.StoreTarget.Value, value))
                    changed = true;
            }

            private bool SetFact(Dictionary<SsaValueName, ValueFact> facts, SsaValueName name, ValueFact fact)
            {
                if (fact.Kind == ValueFactKind.Alias)
                {
                    fact = NormalizeValue(fact.Alias, facts);
                    if (fact.Kind == ValueFactKind.Alias && fact.Alias.Equals(name))
                        fact = ValueFact.Unknown;
                }

                if (!facts.TryGetValue(name, out var current))
                {
                    facts.Add(name, fact);
                    return true;
                }

                if (current.Equals(fact))
                    return false;

                facts[name] = fact;
                return true;
            }

            private ValueFact EvaluateTree(SsaTree tree, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (tree.Value.HasValue)
                    return NormalizeValue(tree.Value.Value, facts);

                if (TryGetSourceConstant(tree.Source, out var sourceConstant))
                    return ValueFact.ForConstant(sourceConstant);

                if (!_options.FoldConstants)
                    return ValueFact.Unknown;

                if (tree.Kind == GenTreeKind.Unary && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(tree.Operands[0], facts);
                    if (operand.Kind == ValueFactKind.Constant && TryFoldUnary(tree.Source, operand.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Binary && tree.Operands.Length == 2)
                {
                    var left = EvaluateTree(tree.Operands[0], facts);
                    var right = EvaluateTree(tree.Operands[1], facts);
                    if (left.Kind == ValueFactKind.Constant && right.Kind == ValueFactKind.Constant && TryFoldBinary(tree.Source, left.Constant, right.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Conv && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(tree.Operands[0], facts);
                    if (operand.Kind == ValueFactKind.Constant && TryFoldConversion(tree.Source, operand.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                return ValueFact.Unknown;
            }

            private ValueFact NormalizeValue(SsaValueName name, Dictionary<SsaValueName, ValueFact> facts)
            {
                var seen = new HashSet<SsaValueName>();
                var current = name;

                while (seen.Add(current) && facts.TryGetValue(current, out var fact))
                {
                    if (fact.Kind == ValueFactKind.Constant)
                        return fact;

                    if (fact.Kind != ValueFactKind.Alias)
                        break;

                    current = fact.Alias;
                }

                return _options.PropagateCopies ? ValueFact.ForAlias(current) : ValueFact.Unknown;
            }

            private OptimizationResult Rewrite(SsaMethod method, Dictionary<SsaValueName, ValueFact> facts)
            {
                bool changed = false;
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(method.Blocks.Length);

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var newInputs = ImmutableArray.CreateBuilder<SsaPhiInput>(phi.Inputs.Length);
                        bool phiChanged = false;

                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            var fact = NormalizeValue(input.Value, facts);
                            if (_options.PropagateCopies &&
                                fact.Kind == ValueFactKind.Alias &&
                                !fact.Alias.Equals(input.Value) &&
                                fact.Alias.Slot.Equals(phi.Slot))
                            {
                                newInputs.Add(new SsaPhiInput(input.PredecessorBlockId, fact.Alias));
                                phiChanged = true;
                            }
                            else
                            {
                                newInputs.Add(input);
                            }
                        }

                        var rewrittenPhi = phiChanged
                            ? new SsaPhi(phi.BlockId, phi.Slot, phi.Target, newInputs.ToImmutable())
                            : phi;

                        if (PhiIsTrivial(rewrittenPhi, facts))
                        {
                            changed = true;
                            continue;
                        }

                        if (phiChanged)
                            changed = true;

                        phis.Add(rewrittenPhi);
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var rewritten = RewriteTree(block.Statements[s], facts, ref changed);
                        statements.Add(rewritten);
                    }

                    blocks.Add(new SsaBlock(block.CfgBlock, phis.ToImmutable(), statements.ToImmutable()));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private bool PhiIsTrivial(SsaPhi phi, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (!_options.PropagateCopies && !_options.PropagateConstants)
                    return false;

                var fact = NormalizeValue(phi.Target, facts);
                if (fact.Kind == ValueFactKind.Constant && _options.PropagateConstants)
                    return true;

                if (fact.Kind == ValueFactKind.Alias &&
                    !fact.Alias.Equals(phi.Target) &&
                    fact.Alias.Slot.Equals(phi.Slot))
                {
                    return true;
                }

                SsaValueName? single = null;
                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    var input = NormalizeValue(phi.Inputs[i].Value, facts);
                    if (input.Kind != ValueFactKind.Alias || !input.Alias.Slot.Equals(phi.Slot))
                        return false;

                    if (!single.HasValue)
                        single = input.Alias;
                    else if (!single.Value.Equals(input.Alias))
                        return false;
                }

                return single.HasValue && !single.Value.Equals(phi.Target);
            }

            private SsaTree RewriteTree(SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, ref bool changed)
            {
                if (tree.Value.HasValue)
                {
                    var fact = NormalizeValue(tree.Value.Value, facts);
                    if (_options.PropagateConstants && fact.Kind == ValueFactKind.Constant)
                    {
                        changed = true;
                        return new SsaTree(CreateConstantTree(tree.Source, fact.Constant), ImmutableArray<SsaTree>.Empty);
                    }

                    if (_options.PropagateCopies &&
                        fact.Kind == ValueFactKind.Alias &&
                        !fact.Alias.Equals(tree.Value.Value) &&
                        fact.Alias.Slot.Equals(tree.Value.Value.Slot))
                    {
                        changed = true;
                        return new SsaTree(tree.Source, ImmutableArray<SsaTree>.Empty, value: fact.Alias);
                    }

                    return tree;
                }

                var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                bool operandChanged = false;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var rewritten = RewriteTree(tree.Operands[i], facts, ref changed);
                    if (!ReferenceEquals(rewritten, tree.Operands[i]))
                        operandChanged = true;
                    operands.Add(rewritten);
                }

                var newOperands = operandChanged ? operands.ToImmutable() : tree.Operands;
                var candidate = operandChanged
                    ? new SsaTree(tree.Source, newOperands, tree.Value, tree.StoreTarget)
                    : tree;

                if (_options.FoldConstants && !candidate.StoreTarget.HasValue && ProducesValue(candidate.Source))
                {
                    var fact = EvaluateTree(candidate, facts);
                    if (fact.Kind == ValueFactKind.Constant && !TryGetSourceConstant(candidate.Source, out _))
                    {
                        changed = true;
                        return new SsaTree(CreateConstantTree(candidate.Source, fact.Constant), ImmutableArray<SsaTree>.Empty);
                    }
                }

                if (operandChanged)
                    changed = true;

                return candidate;
            }

            private OptimizationResult EliminateDeadDefinitions(SsaMethod method)
            {
                var live = ComputeLiveValues(method);
                bool changed = false;
                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(method.Blocks.Length);

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        if (live.Contains(phi.Target))
                            phis.Add(phi);
                        else
                            changed = true;
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var statement = block.Statements[s];
                        if (statement.StoreTarget.HasValue && !live.Contains(statement.StoreTarget.Value))
                        {
                            var sideEffects = ExtractSideEffects(statement);
                            if (sideEffects is not null)
                                statements.Add(sideEffects);
                            changed = true;
                            continue;
                        }

                        if (!statement.StoreTarget.HasValue && !HasObservableEffect(statement))
                        {
                            changed = true;
                            continue;
                        }

                        statements.Add(statement);
                    }

                    blocks.Add(new SsaBlock(block.CfgBlock, phis.ToImmutable(), statements.ToImmutable()));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private HashSet<SsaValueName> ComputeLiveValues(SsaMethod method)
            {
                var phiDefs = new Dictionary<SsaValueName, SsaPhi>();
                var storeDefs = new Dictionary<SsaValueName, SsaTree>();

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                        phiDefs[block.Phis[p].Target] = block.Phis[p];
                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectStoreDefinitions(block.Statements[s], storeDefs);
                }

                var live = new HashSet<SsaValueName>();
                var work = new Queue<SsaValueName>();

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                    {
                        var statement = statements[s];
                        if (statement.StoreTarget.HasValue)
                        {
                            if (StoreRhsHasObservableEffect(statement))
                                MarkUses(statement, live, work, includeStoreTarget: false);
                        }
                        else if (HasObservableEffect(statement))
                        {
                            MarkUses(statement, live, work, includeStoreTarget: false);
                        }
                    }
                }

                while (work.Count != 0)
                {
                    var value = work.Dequeue();
                    if (storeDefs.TryGetValue(value, out var store))
                    {
                        MarkUses(store, live, work, includeStoreTarget: false);
                        continue;
                    }

                    if (phiDefs.TryGetValue(value, out var phi))
                    {
                        for (int i = 0; i < phi.Inputs.Length; i++)
                            MarkValue(phi.Inputs[i].Value, live, work);
                    }
                }

                return live;
            }

            private void CollectStoreDefinitions(SsaTree tree, Dictionary<SsaValueName, SsaTree> storeDefs)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStoreDefinitions(tree.Operands[i], storeDefs);

                if (tree.StoreTarget.HasValue)
                    storeDefs[tree.StoreTarget.Value] = tree;
            }

            private void MarkUses(SsaTree tree, HashSet<SsaValueName> live, Queue<SsaValueName> work, bool includeStoreTarget)
            {
                if (tree.Value.HasValue)
                    MarkValue(tree.Value.Value, live, work);

                if (includeStoreTarget && tree.StoreTarget.HasValue)
                    MarkValue(tree.StoreTarget.Value, live, work);

                for (int i = 0; i < tree.Operands.Length; i++)
                    MarkUses(tree.Operands[i], live, work, includeStoreTarget: true);
            }

            private static void MarkValue(SsaValueName value, HashSet<SsaValueName> live, Queue<SsaValueName> work)
            {
                if (live.Add(value))
                    work.Enqueue(value);
            }

            private SsaTree? ExtractSideEffects(SsaTree deadStore)
            {
                if (deadStore.Operands.Length == 0)
                    return null;

                if (deadStore.Operands.Length == 1)
                {
                    var operand = deadStore.Operands[0];
                    if (!HasObservableEffect(operand))
                        return null;

                    var evalSource = CreateEvalTree(deadStore.Source, ImmutableArray.Create(operand));
                    return new SsaTree(evalSource, ImmutableArray.Create(operand));
                }

                var sideEffects = ImmutableArray.CreateBuilder<SsaTree>(deadStore.Operands.Length);
                for (int i = 0; i < deadStore.Operands.Length; i++)
                {
                    if (HasObservableEffect(deadStore.Operands[i]))
                        sideEffects.Add(deadStore.Operands[i]);
                }

                if (sideEffects.Count == 0)
                    return null;

                var operands = sideEffects.ToImmutable();
                return new SsaTree(CreateEvalTree(deadStore.Source, operands), operands);
            }

            private bool StoreRhsHasObservableEffect(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableEffect(tree.Operands[i]))
                        return true;
                }
                return false;
            }

            private bool HasObservableEffect(SsaTree tree)
            {
                if (tree.Value.HasValue)
                    return false;

                if (tree.StoreTarget.HasValue)
                    return StoreRhsHasObservableEffect(tree);

                switch (tree.Kind)
                {
                    case GenTreeKind.ConstI4:
                    case GenTreeKind.ConstI8:
                    case GenTreeKind.ConstR8Bits:
                    case GenTreeKind.ConstNull:
                    case GenTreeKind.ConstString:
                    case GenTreeKind.DefaultValue:
                    case GenTreeKind.SizeOf:
                    case GenTreeKind.Local:
                    case GenTreeKind.Arg:
                    case GenTreeKind.Temp:
                        return false;
                }

                var flags = tree.Source.Flags;
                if ((flags & (GenTreeFlags.ContainsCall |
                              GenTreeFlags.CanThrow |
                              GenTreeFlags.SideEffect |
                              GenTreeFlags.MemoryWrite |
                              GenTreeFlags.ControlFlow |
                              GenTreeFlags.ExceptionFlow |
                              GenTreeFlags.Ordered)) != 0)
                    return true;

                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableEffect(tree.Operands[i]))
                        return true;
                }

                return false;
            }

            private GenTree CreateConstantTree(GenTree template, ConstValue constant)
            {
                return constant.Kind switch
                {
                    ConstKind.I4 => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI4,
                        template.Pc,
                        BytecodeOp.Ldc_I4,
                        type: null,
                        stackKind: GenStackKind.I4,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int32: constant.I4),
                    ConstKind.I8 => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI8,
                        template.Pc,
                        BytecodeOp.Ldc_I8,
                        type: null,
                        stackKind: GenStackKind.I8,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int64: constant.I8),
                    ConstKind.Null => new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstNull,
                        template.Pc,
                        BytecodeOp.Ldnull,
                        type: template.Type,
                        stackKind: GenStackKind.Null,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty),
                    _ => throw new InvalidOperationException("Unknown SSA constant kind."),
                };
            }

            private GenTree CreateEvalTree(GenTree template, ImmutableArray<SsaTree> operands)
            {
                var genOperands = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                GenTreeFlags flags = GenTreeFlags.None;
                for (int i = 0; i < operands.Length; i++)
                {
                    genOperands.Add(operands[i].Source);
                    flags |= operands[i].Source.Flags;
                }

                return new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Eval,
                    template.Pc,
                    BytecodeOp.Pop,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: flags,
                    operands: genOperands.ToImmutable());
            }

            private static bool TryGetSourceConstant(GenTree source, out ConstValue constant)
            {
                switch (source.Kind)
                {
                    case GenTreeKind.ConstI4:
                        constant = ConstValue.ForI4(source.Int32);
                        return true;
                    case GenTreeKind.ConstI8:
                        constant = ConstValue.ForI8(source.Int64);
                        return true;
                    case GenTreeKind.ConstNull:
                        constant = ConstValue.Null;
                        return true;
                    default:
                        constant = default;
                        return false;
                }
            }

            private static bool TryFoldUnary(GenTree source, ConstValue operand, out ConstValue result)
            {
                result = default;
                if (source.SourceOp == BytecodeOp.Neg)
                {
                    if (operand.Kind == ConstKind.I4)
                    {
                        result = ConstValue.ForI4(unchecked(-operand.I4));
                        return true;
                    }
                    if (operand.Kind == ConstKind.I8)
                    {
                        result = ConstValue.ForI8(unchecked(-operand.I8));
                        return true;
                    }
                }

                if (source.SourceOp == BytecodeOp.Not)
                {
                    if (operand.Kind == ConstKind.I4)
                    {
                        result = ConstValue.ForI4(~operand.I4);
                        return true;
                    }
                    if (operand.Kind == ConstKind.I8)
                    {
                        result = ConstValue.ForI8(~operand.I8);
                        return true;
                    }
                }

                return false;
            }

            private static bool TryFoldBinary(GenTree source, ConstValue left, ConstValue right, out ConstValue result)
            {
                result = default;

                if (left.Kind == ConstKind.Null || right.Kind == ConstKind.Null)
                {
                    if (source.SourceOp == BytecodeOp.Ceq)
                    {
                        result = ConstValue.ForI4(left.Kind == ConstKind.Null && right.Kind == ConstKind.Null ? 1 : 0);
                        return true;
                    }
                    return false;
                }

                if (left.Kind == ConstKind.I8 || right.Kind == ConstKind.I8 || source.StackKind == GenStackKind.I8)
                    return TryFoldBinaryI8(source.SourceOp, left.Kind == ConstKind.I8 ? left.I8 : left.I4, right.Kind == ConstKind.I8 ? right.I8 : right.I4, out result);

                return TryFoldBinaryI4(source.SourceOp, left.I4, right.I4, out result);
            }

            private static bool TryFoldBinaryI4(BytecodeOp op, int left, int right, out ConstValue result)
            {
                result = default;
                switch (op)
                {
                    case BytecodeOp.Add:
                        result = ConstValue.ForI4(unchecked(left + right));
                        return true;
                    case BytecodeOp.Sub:
                        result = ConstValue.ForI4(unchecked(left - right));
                        return true;
                    case BytecodeOp.Mul:
                        result = ConstValue.ForI4(unchecked(left * right));
                        return true;
                    case BytecodeOp.Div:
                        if (right == 0 || (left == int.MinValue && right == -1)) return false;
                        result = ConstValue.ForI4(left / right);
                        return true;
                    case BytecodeOp.Div_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI4(unchecked((int)((uint)left / (uint)right)));
                        return true;
                    case BytecodeOp.Rem:
                        if (right == 0 || (left == int.MinValue && right == -1)) return false;
                        result = ConstValue.ForI4(left % right);
                        return true;
                    case BytecodeOp.Rem_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI4(unchecked((int)((uint)left % (uint)right)));
                        return true;
                    case BytecodeOp.And:
                        result = ConstValue.ForI4(left & right);
                        return true;
                    case BytecodeOp.Or:
                        result = ConstValue.ForI4(left | right);
                        return true;
                    case BytecodeOp.Xor:
                        result = ConstValue.ForI4(left ^ right);
                        return true;
                    case BytecodeOp.Shl:
                        result = ConstValue.ForI4(left << (right & 31));
                        return true;
                    case BytecodeOp.Shr:
                        result = ConstValue.ForI4(left >> (right & 31));
                        return true;
                    case BytecodeOp.Shr_Un:
                        result = ConstValue.ForI4(unchecked((int)((uint)left >> (right & 31))));
                        return true;
                    case BytecodeOp.Ceq:
                        result = ConstValue.ForI4(left == right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt:
                        result = ConstValue.ForI4(left < right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt_Un:
                        result = ConstValue.ForI4((uint)left < (uint)right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt:
                        result = ConstValue.ForI4(left > right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt_Un:
                        result = ConstValue.ForI4((uint)left > (uint)right ? 1 : 0);
                        return true;
                    default:
                        return false;
                }
            }

            private static bool TryFoldBinaryI8(BytecodeOp op, long left, long right, out ConstValue result)
            {
                result = default;
                switch (op)
                {
                    case BytecodeOp.Add:
                        result = ConstValue.ForI8(unchecked(left + right));
                        return true;
                    case BytecodeOp.Sub:
                        result = ConstValue.ForI8(unchecked(left - right));
                        return true;
                    case BytecodeOp.Mul:
                        result = ConstValue.ForI8(unchecked(left * right));
                        return true;
                    case BytecodeOp.Div:
                        if (right == 0 || (left == long.MinValue && right == -1)) return false;
                        result = ConstValue.ForI8(left / right);
                        return true;
                    case BytecodeOp.Div_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI8(unchecked((long)((ulong)left / (ulong)right)));
                        return true;
                    case BytecodeOp.Rem:
                        if (right == 0 || (left == long.MinValue && right == -1)) return false;
                        result = ConstValue.ForI8(left % right);
                        return true;
                    case BytecodeOp.Rem_Un:
                        if (right == 0) return false;
                        result = ConstValue.ForI8(unchecked((long)((ulong)left % (ulong)right)));
                        return true;
                    case BytecodeOp.And:
                        result = ConstValue.ForI8(left & right);
                        return true;
                    case BytecodeOp.Or:
                        result = ConstValue.ForI8(left | right);
                        return true;
                    case BytecodeOp.Xor:
                        result = ConstValue.ForI8(left ^ right);
                        return true;
                    case BytecodeOp.Shl:
                        result = ConstValue.ForI8(left << ((int)right & 63));
                        return true;
                    case BytecodeOp.Shr:
                        result = ConstValue.ForI8(left >> ((int)right & 63));
                        return true;
                    case BytecodeOp.Shr_Un:
                        result = ConstValue.ForI8(unchecked((long)((ulong)left >> ((int)right & 63))));
                        return true;
                    case BytecodeOp.Ceq:
                        result = ConstValue.ForI4(left == right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt:
                        result = ConstValue.ForI4(left < right ? 1 : 0);
                        return true;
                    case BytecodeOp.Clt_Un:
                        result = ConstValue.ForI4((ulong)left < (ulong)right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt:
                        result = ConstValue.ForI4(left > right ? 1 : 0);
                        return true;
                    case BytecodeOp.Cgt_Un:
                        result = ConstValue.ForI4((ulong)left > (ulong)right ? 1 : 0);
                        return true;
                    default:
                        return false;
                }
            }

            private static bool TryFoldConversion(GenTree source, ConstValue operand, out ConstValue result)
            {
                result = default;
                if (operand.Kind == ConstKind.Null)
                    return false;

                bool isChecked = (source.ConvFlags & NumericConvFlags.Checked) != 0;
                bool sourceUnsigned = (source.ConvFlags & NumericConvFlags.SourceUnsigned) != 0;

                try
                {
                    switch (source.ConvKind)
                    {
                        case NumericConvKind.I1:
                            result = ConstValue.ForI4(isChecked
                                ? checked((sbyte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((sbyte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U1:
                            result = ConstValue.ForI4(isChecked
                                ? checked((byte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((byte)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I2:
                            result = ConstValue.ForI4(isChecked
                                ? checked((short)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((short)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U2:
                        case NumericConvKind.Char:
                            result = ConstValue.ForI4(isChecked
                                ? checked((ushort)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4))
                                : unchecked((ushort)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I4:
                        case NumericConvKind.Bool:
                            result = ConstValue.ForI4(isChecked && operand.Kind == ConstKind.I8
                                ? checked((int)operand.I8)
                                : unchecked((int)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.U4:
                            result = ConstValue.ForI4(isChecked
                                ? unchecked((int)checked((uint)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)))
                                : unchecked((int)(uint)(operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4)));
                            return true;
                        case NumericConvKind.I8:
                        case NumericConvKind.NativeInt:
                            result = ConstValue.ForI8(operand.Kind == ConstKind.I8
                                ? operand.I8
                                : sourceUnsigned ? (long)(uint)operand.I4 : operand.I4);
                            return true;
                        case NumericConvKind.U8:
                        case NumericConvKind.NativeUInt:
                            if (sourceUnsigned)
                            {
                                result = ConstValue.ForI8(operand.Kind == ConstKind.I8
                                    ? operand.I8
                                    : unchecked((long)(uint)operand.I4));
                            }
                            else
                            {
                                long signed = operand.Kind == ConstKind.I8 ? operand.I8 : operand.I4;
                                result = ConstValue.ForI8(isChecked
                                    ? unchecked((long)checked((ulong)signed))
                                    : unchecked((long)(ulong)signed));
                            }
                            return true;
                        default:
                            return false;
                    }
                }
                catch (OverflowException)
                {
                    return false;
                }
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

            private static SsaMethod WithBlocks(SsaMethod method, ImmutableArray<SsaBlock> blocks)
            {
                var definitions = BuildValueDefinitions(method.Slots, method.InitialValues, blocks);
                return new SsaMethod(
                    method.GenTreeMethod,
                    method.Cfg,
                    method.Slots,
                    method.InitialValues,
                    definitions,
                    blocks);
            }

            private static ImmutableArray<SsaValueDefinition> BuildValueDefinitions(
                ImmutableArray<SsaSlotInfo> slots,
                ImmutableArray<SsaValueName> initialValues,
                ImmutableArray<SsaBlock> blocks)
            {
                var infoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>();
                for (int i = 0; i < slots.Length; i++)
                    infoBySlot[slots[i].Slot] = slots[i];

                var definitions = new List<SsaValueDefinition>();
                var seen = new HashSet<SsaValueName>();

                for (int i = 0; i < initialValues.Length; i++)
                {
                    var name = initialValues[i];
                    var info = GetSlotInfo(infoBySlot, name.Slot);
                    Add(new SsaValueDefinition(name, -1, -1, -1, true, false, info.Type, info.StackKind));
                }

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var info = GetSlotInfo(infoBySlot, phi.Target.Slot);
                        Add(new SsaValueDefinition(phi.Target, block.Id, -1, -1, false, true, info.Type, info.StackKind));
                    }

                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectDefinitions(block.Statements[s], block.Id, s);
                }

                definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
                return definitions.ToImmutableArray();

                void CollectDefinitions(SsaTree tree, int blockId, int statementIndex)
                {
                    for (int i = 0; i < tree.Operands.Length; i++)
                        CollectDefinitions(tree.Operands[i], blockId, statementIndex);

                    if (!tree.StoreTarget.HasValue)
                        return;

                    var name = tree.StoreTarget.Value;
                    var info = GetSlotInfo(infoBySlot, name.Slot);
                    Add(new SsaValueDefinition(name, blockId, statementIndex, tree.Source.Id, false, false, info.Type, info.StackKind));
                }

                void Add(SsaValueDefinition definition)
                {
                    if (!seen.Add(definition.Name))
                        throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                    definitions.Add(definition);
                }
            }

            private static SsaSlotInfo GetSlotInfo(Dictionary<SsaSlot, SsaSlotInfo> infoBySlot, SsaSlot slot)
            {
                if (infoBySlot.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(slot, null, GenStackKind.Unknown, addressExposed: false);
            }

            private static int MaxTreeId(SsaMethod method)
            {
                int max = 0;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var block = method.Blocks[b];
                    for (int s = 0; s < block.Statements.Length; s++)
                        Visit(block.Statements[s]);
                }
                return max;

                void Visit(SsaTree tree)
                {
                    if (tree.Source.Id > max)
                        max = tree.Source.Id;
                    for (int i = 0; i < tree.Operands.Length; i++)
                        Visit(tree.Operands[i]);
                }
            }
        }
    }

    internal static class SsaVerifier
    {
        public static void Verify(SsaMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var definitions = BuildDefinitionMap(method);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                VerifyPhis(method, definitions, block);

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyStatement(method, definitions, block.Id, s, block.Statements[s]);
            }
        }

        private static Dictionary<SsaValueName, SsaValueDefinition> BuildDefinitionMap(SsaMethod method)
        {
            var result = new Dictionary<SsaValueName, SsaValueDefinition>();

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                if (definition.Name.Version < 0)
                    throw new InvalidOperationException($"Invalid SSA version {definition.Name}.");

                if (definition.IsInitial)
                {
                    if (definition.Name.Version != 0 || definition.DefBlockId != -1)
                        throw new InvalidOperationException($"Malformed initial SSA definition {definition.Name}.");
                }
                else if ((uint)definition.DefBlockId >= (uint)method.Blocks.Length)
                {
                    throw new InvalidOperationException($"SSA definition {definition.Name} has invalid block B{definition.DefBlockId}.");
                }

                if (definition.IsPhi && definition.DefStatementIndex != -1)
                    throw new InvalidOperationException($"Phi definition {definition.Name} has statement index {definition.DefStatementIndex}.");

                if (!definition.IsPhi && !definition.IsInitial && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException($"Tree definition {definition.Name} has no statement index.");

                if (result.ContainsKey(definition.Name))
                    throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");

                result.Add(definition.Name, definition);
            }

            for (int i = 0; i < method.InitialValues.Length; i++)
            {
                if (!result.TryGetValue(method.InitialValues[i], out var definition) || !definition.IsInitial)
                    throw new InvalidOperationException($"Initial SSA value {method.InitialValues[i]} is missing from definition table.");
            }

            return result;
        }

        private static void VerifyPhis(SsaMethod method, Dictionary<SsaValueName, SsaValueDefinition> definitions, SsaBlock block)
        {
            var expectedPreds = new HashSet<int>();
            for (int p = 0; p < block.CfgBlock.Predecessors.Length; p++)
                expectedPreds.Add(block.CfgBlock.Predecessors[p].FromBlockId);

            for (int i = 0; i < block.Phis.Length; i++)
            {
                var phi = block.Phis[i];
                if (!definitions.TryGetValue(phi.Target, out var definition))
                    throw new InvalidOperationException($"Phi target {phi.Target} in B{block.Id} is missing from definition table.");

                if (!definition.IsPhi || definition.DefBlockId != block.Id)
                    throw new InvalidOperationException($"Definition table entry for {phi.Target} does not match phi in B{block.Id}.");

                if (!phi.Target.Slot.Equals(phi.Slot))
                    throw new InvalidOperationException($"Phi target {phi.Target} in B{block.Id} does not belong to phi slot {phi.Slot}.");

                var actualPreds = new HashSet<int>();
                for (int p = 0; p < phi.Inputs.Length; p++)
                {
                    var input = phi.Inputs[p];
                    if (!input.Value.Slot.Equals(phi.Slot))
                        throw new InvalidOperationException($"Phi {phi.Target} in B{block.Id} has cross-slot input {input.Value} from B{input.PredecessorBlockId}; phi operands must remain on the same tracked local slot.");
                    actualPreds.Add(input.PredecessorBlockId);
                    VerifyEdgeUse(method, definitions, phi.Target, input.Value, input.PredecessorBlockId);
                }

                if (!expectedPreds.SetEquals(actualPreds))
                    throw new InvalidOperationException($"Malformed phi {phi.Target} in B{block.Id}: predecessor set mismatch.");
            }
        }

        private static void VerifyStatement(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            VerifyTreeUses(method, definitions, blockId, statementIndex, tree);
            VerifyTreeDefinitions(method, definitions, blockId, statementIndex, tree);
        }

        private static void VerifyTreeUses(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            if (tree.Value.HasValue)
                VerifyLocalUse(method, definitions, tree.Value.Value, blockId, statementIndex, tree.Source.Id);

            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeUses(method, definitions, blockId, statementIndex, tree.Operands[i]);
        }

        private static void VerifyTreeDefinitions(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeDefinitions(method, definitions, blockId, statementIndex, tree.Operands[i]);

            if (!tree.StoreTarget.HasValue)
                return;

            var name = tree.StoreTarget.Value;
            if (!definitions.TryGetValue(name, out var definition))
                throw new InvalidOperationException($"Store target {name} at node {tree.Source.Id} is missing from definition table.");

            if (definition.IsInitial || definition.IsPhi || definition.DefBlockId != blockId || definition.DefStatementIndex != statementIndex || definition.DefTreeId != tree.Source.Id)
                throw new InvalidOperationException($"Definition table entry for {name} does not match store node {tree.Source.Id}.");
        }

        private static void VerifyLocalUse(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            SsaValueName use,
            int useBlockId,
            int useStatementIndex,
            int useTreeId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Use of undefined SSA value {use} at node {useTreeId}.");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, useBlockId))
                throw new InvalidOperationException($"SSA definition {use} in B{definition.DefBlockId} does not dominate use at node {useTreeId} in B{useBlockId}.");

            if (definition.DefBlockId == useBlockId && !definition.IsPhi && definition.DefStatementIndex >= useStatementIndex)
                throw new InvalidOperationException($"SSA definition {use} at statement {definition.DefStatementIndex} does not precede use at statement {useStatementIndex} in B{useBlockId}.");
        }

        private static void VerifyEdgeUse(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            SsaValueName phiTarget,
            SsaValueName use,
            int predecessorBlockId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Phi {phiTarget} uses undefined value {use} from B{predecessorBlockId}.");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, predecessorBlockId))
                throw new InvalidOperationException($"Phi {phiTarget} input {use} from B{predecessorBlockId} is not dominated by its definition in B{definition.DefBlockId}.");
        }
    }

    internal static class SsaDumper
    {
        public static string Dump(SsaProgram program)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < program.Methods.Length; i++)
            {
                DumpMethod(sb, program.Methods[i]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Dump(SsaMethod method)
        {
            var sb = new StringBuilder();
            DumpMethod(sb, method);
            return sb.ToString();
        }

        public static string FormatTree(SsaTree tree)
        {
            var sb = new StringBuilder();
            AppendTree(sb, tree);
            return sb.ToString();
        }

        private static void DumpMethod(StringBuilder sb, SsaMethod method)
        {
            var rm = method.GenTreeMethod.RuntimeMethod;
            sb.Append("ssa method ")
              .Append(method.GenTreeMethod.Module.Name)
              .Append("::")
              .Append(TypeName(rm.DeclaringType))
              .Append('.')
              .Append(rm.Name)
              .Append(" #")
              .Append(rm.MethodId)
              .AppendLine();

            if (method.InitialValues.Length != 0)
            {
                sb.Append("  initial: ");
                for (int i = 0; i < method.InitialValues.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append(method.InitialValues[i]);
                }
                sb.AppendLine();
            }

            if (method.Cfg.NaturalLoops.Length != 0)
            {
                sb.Append("  loops: ");
                for (int i = 0; i < method.Cfg.NaturalLoops.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    var loop = method.Cfg.NaturalLoops[i];
                    sb.Append('L').Append(loop.Index).Append("(H=B").Append(loop.HeaderBlockId).Append(", L=B").Append(loop.LatchBlockId).Append(')');
                }
                sb.AppendLine();
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                sb.Append("  B").Append(block.Id)
                  .Append(" [pc ").Append(block.CfgBlock.StartPc).Append("..").Append(block.CfgBlock.EndPcExclusive).Append(']');

                if (block.CfgBlock.Successors.Length != 0)
                {
                    sb.Append(" -> ");
                    for (int i = 0; i < block.CfgBlock.Successors.Length; i++)
                    {
                        if (i != 0) sb.Append(", ");
                        var e = block.CfgBlock.Successors[i];
                        sb.Append('B').Append(e.ToBlockId).Append(':').Append(e.Kind);
                    }
                }
                sb.AppendLine();

                for (int i = 0; i < block.Phis.Length; i++)
                {
                    var phi = block.Phis[i];
                    sb.Append("    ").Append(phi.Target).Append(" = phi(");
                    for (int p = 0; p < phi.Inputs.Length; p++)
                    {
                        if (p != 0) sb.Append(", ");
                        sb.Append("B").Append(phi.Inputs[p].PredecessorBlockId).Append(':').Append(phi.Inputs[p].Value);
                    }
                    sb.AppendLine(")");
                }

                for (int i = 0; i < block.Statements.Length; i++)
                {
                    sb.Append("    ");
                    AppendTree(sb, block.Statements[i]);
                    sb.AppendLine();
                }
            }
        }

        private static void AppendTree(StringBuilder sb, SsaTree tree)
        {
            if (tree.Value.HasValue)
            {
                sb.Append(tree.Value.Value);
                return;
            }

            switch (tree.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(tree.Source.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(tree.Source.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(tree.Source.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(tree.Source.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append('l').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append('a').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append('t').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("&l").Append(tree.Source.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("&a").Append(tree.Source.Int32);
                    return;
                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    if (tree.StoreTarget.HasValue)
                        sb.Append(tree.StoreTarget.Value);
                    else
                        AppendOriginalStoreTarget(sb, tree.Source);
                    sb.Append(" = ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Unary:
                    sb.Append(tree.Source.SourceOp.ToString().ToLowerInvariant()).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Binary:
                    if (tree.Operands.Length == 2)
                    {
                        sb.Append('(');
                        AppendTree(sb, tree.Operands[0]);
                        sb.Append(' ').Append(tree.Source.SourceOp).Append(' ');
                        AppendTree(sb, tree.Operands[1]);
                        sb.Append(')');
                        return;
                    }
                    break;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(tree.Source.ConvKind).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                    sb.Append(tree.Kind == GenTreeKind.VirtualCall ? "callvirt " : "call ")
                      .Append(MethodName(tree.Source.Method)).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(tree.Source.Method)).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(tree.Source.TargetBlockId);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(tree.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    AppendOperandList(sb, tree);
                    sb.Append(" -> B").Append(tree.Source.TargetBlockId);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret");
                    if (tree.Operands.Length != 0)
                    {
                        sb.Append(' ');
                        AppendOperandList(sb, tree);
                    }
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(tree.Kind).Append('(');
            AppendOperandList(sb, tree);
            sb.Append(')');
        }

        private static void AppendOriginalStoreTarget(StringBuilder sb, GenTree source)
        {
            switch (source.Kind)
            {
                case GenTreeKind.StoreArg:
                    sb.Append('a').Append(source.Int32);
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append('l').Append(source.Int32);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append('t').Append(source.Int32);
                    return;
                default:
                    sb.Append("<store>");
                    return;
            }
        }

        private static void AppendOperandList(StringBuilder sb, SsaTree tree)
        {
            for (int i = 0; i < tree.Operands.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                AppendTree(sb, tree.Operands[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null) return "?";
            if (string.IsNullOrEmpty(type.Namespace)) return type.Name;
            return type.Namespace + "." + type.Name;
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null) return "<method?>";
            return TypeName(method.DeclaringType) + "." + method.Name;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
