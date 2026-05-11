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
                blocks.Add(RewriteOriginalBlock(method.Blocks[i], splitInfo, ref nextTreeId));

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
            Dictionary<(int from, int to), SplitEdgeInfo> splitInfo,
            ref int nextTreeId)
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
                if (TryRewriteTerminator(block, last, splitInfo, ref nextTreeId, out var rewrittenLast, out var appendedBranch))
                {
                    var rewritten = ImmutableArray.CreateBuilder<GenTree>(statements.Length + (appendedBranch is null ? 0 : 1));
                    for (int i = 0; i + 1 < statements.Length; i++)
                        rewritten.Add(statements[i]);
                    rewritten.Add(rewrittenLast);
                    if (appendedBranch is not null)
                        rewritten.Add(appendedBranch);
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
            ref int nextTreeId,
            out GenTree rewritten,
            out GenTree? appendedBranch)
        {
            rewritten = terminator;
            appendedBranch = null;
            bool changed = false;

            if (terminator.TargetBlockId >= 0 &&
                splitInfo.TryGetValue((block.Id, terminator.TargetBlockId), out var targetInfo))
            {
                rewritten = CloneWithTarget(terminator, terminator.Kind, terminator.SourceOp, targetInfo.SplitPc, targetInfo.SplitBlockId);
                changed = true;
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
                        appendedBranch = CreateBranchToSplit(terminator, fallThroughInfo, ref nextTreeId);
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        private static GenTree CreateBranchToSplit(GenTree source, SplitEdgeInfo target, ref int nextTreeId)
            => new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                source.Pc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: target.SplitPc,
                targetBlockId: target.SplitBlockId);

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

    internal static class SsaConfig
    {
        public const int ReservedSsaNumber = 0;
        public const int FirstSsaNumber = 1;
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
        public readonly int LclNum;
        private readonly bool _hasLclNum;

        public SsaSlot(SsaSlotKind kind, int index)
            : this(kind, index, lclNum: -1)
        {
        }

        public SsaSlot(SsaSlotKind kind, int index, int lclNum)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (lclNum < -1) throw new ArgumentOutOfRangeException(nameof(lclNum));
            Kind = kind;
            Index = index;
            LclNum = lclNum;
            _hasLclNum = lclNum >= 0;
        }

        public SsaSlot(GenLocalDescriptor descriptor)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
            Kind = descriptor.Kind switch
            {
                GenLocalKind.Argument => SsaSlotKind.Arg,
                GenLocalKind.Local => SsaSlotKind.Local,
                GenLocalKind.Temporary => SsaSlotKind.Temp,
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
            };
            Index = descriptor.Index;
            LclNum = descriptor.LclNum;
            _hasLclNum = true;
        }

        public bool HasLclNum => _hasLclNum;

        public bool Equals(SsaSlot other)
        {
            if (HasLclNum || other.HasLclNum)
                return HasLclNum && other.HasLclNum && LclNum == other.LclNum;

            return Kind == other.Kind && Index == other.Index;
        }

        public override bool Equals(object? obj) => obj is SsaSlot other && Equals(other);

        public override int GetHashCode()
            => HasLclNum ? LclNum : (((int)Kind * 397) ^ Index);

        public int CompareTo(SsaSlot other)
        {
            if (HasLclNum && other.HasLclNum)
                return LclNum.CompareTo(other.LclNum);
            if (HasLclNum != other.HasLclNum)
                return HasLclNum ? -1 : 1;

            int c = Kind.CompareTo(other.Kind);
            return c != 0 ? c : Index.CompareTo(other.Index);
        }

        public override string ToString()
        {
            if (HasLclNum)
                return "V" + LclNum.ToString();

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
            if (version <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(version));
            if (!slot.HasLclNum)
                throw new ArgumentException("SSA value identity must include a concrete lclNum.", nameof(slot));

            Slot = slot;
            Version = version;
        }

        public bool IsReserved => Version == SsaConfig.ReservedSsaNumber;
        public bool IsValid => Version > SsaConfig.ReservedSsaNumber;

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


    internal enum SsaDefinitionKind : byte
    {
        Initial,
        Phi,
        Store,
    }

    internal sealed class SsaDescriptor
    {
        public SsaSlot BaseLocal { get; }
        public int SsaNumber { get; }
        public SsaValueName Name => new SsaValueName(BaseLocal, SsaNumber);
        public SsaDefinitionKind DefinitionKind { get; }
        public int DefBlockId { get; }
        public CfgBlock? DefBlock { get; }
        public int DefStatementIndex { get; }
        public int DefTreeId { get; }
        public GenTree? DefNode { get; }
        public SsaPhi? Phi { get; }
        public RuntimeType? Type { get; }
        public GenStackKind StackKind { get; }
        public int PreviousSsaNumber { get; private set; }
        public int UseCount { get; private set; }
        public bool HasPhiUse { get; private set; }
        public bool HasGlobalUse { get; private set; }
        public ValueNumberPair ValueNumbers { get; private set; }

        public bool IsInitial => DefinitionKind == SsaDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaDefinitionKind.Store;
        public bool IsPartialDefinition => PreviousSsaNumber != SsaConfig.ReservedSsaNumber;
        public SsaValueName? PreviousDefinition => IsPartialDefinition ? new SsaValueName(BaseLocal, PreviousSsaNumber) : null;

        public SsaDescriptor(
            SsaSlot baseLocal,
            int ssaNumber,
            SsaDefinitionKind definitionKind,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            GenTree? defNode,
            RuntimeType? type,
            GenStackKind stackKind,
            int previousSsaNumber = SsaConfig.ReservedSsaNumber,
            CfgBlock? defBlock = null,
            SsaPhi? phiDescriptor = null)
        {
            if (ssaNumber <= SsaConfig.ReservedSsaNumber) throw new ArgumentOutOfRangeException(nameof(ssaNumber));
            if (previousSsaNumber < SsaConfig.ReservedSsaNumber) throw new ArgumentOutOfRangeException(nameof(previousSsaNumber));
            if (!baseLocal.HasLclNum) throw new ArgumentException("SSA descriptor base local must include a concrete lclNum.", nameof(baseLocal));
            BaseLocal = baseLocal;
            SsaNumber = ssaNumber;
            DefinitionKind = definitionKind;
            DefBlockId = defBlockId;
            DefBlock = defBlock;
            DefStatementIndex = defStatementIndex;
            DefTreeId = defTreeId;
            DefNode = defNode;
            Phi = phiDescriptor;
            Type = type;
            StackKind = stackKind;
            PreviousSsaNumber = previousSsaNumber;
            ValueNumbers = default;
        }

        internal void SetPreviousDefinition(int previousSsaNumber)
        {
            if (previousSsaNumber < SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(previousSsaNumber));
            if (previousSsaNumber == SsaNumber)
                throw new InvalidOperationException("Partial SSA definition cannot use itself as the previous definition: " + Name + ".");
            PreviousSsaNumber = previousSsaNumber;
        }

        internal void AddUse(int useBlockId)
        {
            if (UseCount < ushort.MaxValue)
                UseCount++;
            if (DefBlockId >= 0 && useBlockId >= 0 && useBlockId != DefBlockId)
                HasGlobalUse = true;
        }

        internal void AddPhiUse(int useBlockId)
        {
            HasPhiUse = true;
            AddUse(useBlockId);
        }

        internal void SetValueNumbers(ValueNumberPair valueNumbers)
        {
            ValueNumbers = valueNumbers;
        }

        public override string ToString()
        {
            string def = DefinitionKind switch
            {
                SsaDefinitionKind.Initial => "init",
                SsaDefinitionKind.Phi => "phi",
                SsaDefinitionKind.Store => "store",
                _ => DefinitionKind.ToString(),
            };
            string partial = IsPartialDefinition ? " prev=" + PreviousSsaNumber.ToString() : string.Empty;
            string uses = UseCount != 0 ? " uses=" + UseCount.ToString() : string.Empty;
            string hints = (HasPhiUse ? " phi-use" : string.Empty) + (HasGlobalUse ? " global-use" : string.Empty);
            string vn = ValueNumbers.Liberal.IsValid || ValueNumbers.Conservative.IsValid ? " vn=" + ValueNumbers.ToString() : string.Empty;
            return BaseLocal.ToString() + "_" + SsaNumber.ToString() + " " + def + partial + uses + hints + vn;
        }
    }

    internal sealed class SsaLocalDescriptor
    {
        public SsaSlot Slot { get; }
        public RuntimeType? Type { get; }
        public GenStackKind StackKind { get; }
        public bool AddressExposed { get; }
        public bool IsSsaPromoted { get; }
        public GenLocalDescriptor? LocalDescriptor { get; }
        public ImmutableArray<SsaDescriptor> PerSsaData { get; }

        public SsaLocalDescriptor(
            SsaSlot slot,
            RuntimeType? type,
            GenStackKind stackKind,
            bool addressExposed,
            bool isSsaPromoted,
            GenLocalDescriptor? localDescriptor,
            ImmutableArray<SsaDescriptor> perSsaData)
        {
            Slot = slot;
            Type = type;
            StackKind = stackKind;
            AddressExposed = addressExposed;
            IsSsaPromoted = isSsaPromoted;
            LocalDescriptor = localDescriptor;
            PerSsaData = perSsaData.IsDefault ? ImmutableArray<SsaDescriptor>.Empty : perSsaData;
        }

        public SsaDescriptor GetSsaDefByNumber(int ssaNumber)
        {
            if (ssaNumber <= SsaConfig.ReservedSsaNumber || (uint)ssaNumber >= (uint)PerSsaData.Length)
                throw new ArgumentOutOfRangeException(nameof(ssaNumber));

            var descriptor = PerSsaData[ssaNumber];
            if (descriptor is null || descriptor.SsaNumber != ssaNumber)
                throw new InvalidOperationException("SSA descriptor table is not dense for " + Slot + ".");
            return descriptor;
        }

        public bool TryGetSsaDefByNumber(int ssaNumber, out SsaDescriptor descriptor)
        {
            if (ssaNumber > SsaConfig.ReservedSsaNumber && (uint)ssaNumber < (uint)PerSsaData.Length)
            {
                descriptor = PerSsaData[ssaNumber];
                if (descriptor is not null && descriptor.SsaNumber == ssaNumber)
                    return true;
            }

            descriptor = null!;
            return false;
        }

        public override string ToString()
        {
            int defCount = PerSsaData.IsDefaultOrEmpty ? 0 : Math.Max(0, PerSsaData.Length - 1);
            return Slot.ToString() + " defs=" + defCount.ToString() + (AddressExposed ? " addr-exposed" : string.Empty);
        }
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
        public readonly GenTree? DefNode;
        public readonly SsaPhi? Phi;
        public readonly int PreviousSsaNumber;
        public readonly SsaDescriptor Descriptor;

        public SsaValueDefinition(SsaDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Name = descriptor.Name;
            DefBlockId = descriptor.DefBlockId;
            DefStatementIndex = descriptor.DefStatementIndex;
            DefTreeId = descriptor.DefTreeId;
            IsInitial = descriptor.IsInitial;
            IsPhi = descriptor.IsPhi;
            Type = descriptor.Type;
            StackKind = descriptor.StackKind;
            DefNode = descriptor.DefNode;
            Phi = descriptor.Phi;
            PreviousSsaNumber = descriptor.PreviousSsaNumber;
        }

        public SsaValueDefinition(
            SsaValueName name,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            bool isInitial,
            bool isPhi,
            RuntimeType? type,
            GenStackKind stackKind)
            : this(new SsaDescriptor(
                name.Slot,
                name.Version,
                isInitial ? SsaDefinitionKind.Initial : isPhi ? SsaDefinitionKind.Phi : SsaDefinitionKind.Store,
                defBlockId,
                defStatementIndex,
                defTreeId,
                defNode: null,
                type,
                stackKind))
        {
        }
    }

    internal readonly struct SsaSlotInfo
    {
        public readonly SsaSlot Slot;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;
        public readonly bool AddressExposed;
        public readonly bool MemoryAliased;
        public readonly GenLocalCategory Category;
        public readonly int LclNum;
        public readonly int VarIndex;
        public readonly bool Tracked;
        public readonly bool InSsa;
        public readonly GenLocalDescriptor? LocalDescriptor;

        public SsaSlotInfo(
            SsaSlot slot,
            RuntimeType? type,
            GenStackKind stackKind,
            bool addressExposed,
            bool memoryAliased = false,
            GenLocalCategory category = GenLocalCategory.Unclassified,
            int lclNum = -1,
            int varIndex = -1,
            bool tracked = false,
            bool inSsa = false,
            GenLocalDescriptor? localDescriptor = null)
        {
            Slot = slot;
            Type = type;
            StackKind = stackKind;
            AddressExposed = addressExposed;
            MemoryAliased = memoryAliased;
            Category = category;
            LclNum = lclNum;
            VarIndex = varIndex;
            Tracked = tracked;
            InSsa = inSsa;
            LocalDescriptor = localDescriptor;
        }

        public bool IsScalarSsaCandidate =>
            InSsa &&
            Tracked &&
            VarIndex >= 0 &&
            !AddressExposed &&
            !MemoryAliased &&
            LocalDescriptor is { CanBeSsaRenamedAsScalar: true };
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

    internal enum SsaMemoryKind : byte
    {
        ByrefExposed = 0,
        GcHeap = 1,
    }

    [Flags]
    internal enum SsaMemoryKindSet : byte
    {
        None = 0,
        ByrefExposed = 1,
        GcHeap = 2,
        All = ByrefExposed | GcHeap,
    }

    internal static class SsaMemoryKinds
    {
        public static readonly ImmutableArray<SsaMemoryKind> All = ImmutableArray.Create(SsaMemoryKind.ByrefExposed, SsaMemoryKind.GcHeap);

        public static SsaMemoryKindSet SetOf(SsaMemoryKind kind) => (SsaMemoryKindSet)(1 << (int)kind);

        public static bool Contains(this SsaMemoryKindSet set, SsaMemoryKind kind) => (set & SetOf(kind)) != 0;

        public static SsaMemoryKindSet Add(this SsaMemoryKindSet set, SsaMemoryKind kind) => set | SetOf(kind);

        public static SsaMemoryKindSet Remove(this SsaMemoryKindSet set, SsaMemoryKind kind) => set & ~SetOf(kind);

        public static string Name(SsaMemoryKind kind)
            => kind switch
            {
                SsaMemoryKind.ByrefExposed => "ByrefExposed",
                SsaMemoryKind.GcHeap => "GcHeap",
                _ => kind.ToString(),
            };
    }

    internal readonly struct SsaMemoryValueName : IEquatable<SsaMemoryValueName>, IComparable<SsaMemoryValueName>
    {
        public readonly SsaMemoryKind Kind;
        public readonly int Version;

        public SsaMemoryValueName(SsaMemoryKind kind, int version)
        {
            if (version <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(version));

            Kind = kind;
            Version = version;
        }

        public bool Equals(SsaMemoryValueName other) => Kind == other.Kind && Version == other.Version;
        public override bool Equals(object? obj) => obj is SsaMemoryValueName other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Version;

        public int CompareTo(SsaMemoryValueName other)
        {
            int c = Kind.CompareTo(other.Kind);
            return c != 0 ? c : Version.CompareTo(other.Version);
        }

        public override string ToString()
            => "M" + SsaMemoryKinds.Name(Kind) + "_" + Version.ToString();
    }

    internal readonly struct SsaMemoryPhiInput
    {
        public readonly int PredecessorBlockId;
        public readonly SsaMemoryValueName Value;

        public SsaMemoryPhiInput(int predecessorBlockId, SsaMemoryValueName value)
        {
            PredecessorBlockId = predecessorBlockId;
            Value = value;
        }
    }

    internal sealed class SsaMemoryPhi
    {
        public int BlockId { get; }
        public SsaMemoryKind Kind { get; }
        public SsaMemoryValueName Target { get; }
        public ImmutableArray<SsaMemoryPhiInput> Inputs { get; }

        public SsaMemoryPhi(int blockId, SsaMemoryKind kind, SsaMemoryValueName target, ImmutableArray<SsaMemoryPhiInput> inputs)
        {
            if (target.Kind != kind)
                throw new ArgumentException("Memory phi target kind does not match phi kind.", nameof(target));

            BlockId = blockId;
            Kind = kind;
            Target = target;
            Inputs = inputs.IsDefault ? ImmutableArray<SsaMemoryPhiInput>.Empty : inputs;
        }
    }

    internal enum SsaMemoryDefinitionKind : byte
    {
        Initial,
        Phi,
        Store,
        BlockOut,
    }

    internal sealed class SsaMemoryDescriptor
    {
        public SsaMemoryValueName Name { get; }
        public SsaMemoryDefinitionKind DefinitionKind { get; }
        public int DefBlockId { get; }
        public CfgBlock? DefBlock { get; }
        public int DefStatementIndex { get; }
        public int DefTreeId { get; }
        public GenTree? DefNode { get; }
        public SsaMemoryPhi? Phi { get; }
        public int UseCount { get; private set; }
        public bool HasPhiUse { get; private set; }
        public bool HasGlobalUse { get; private set; }
        public ValueNumber ValueNumber { get; private set; }

        public bool IsInitial => DefinitionKind == SsaMemoryDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaMemoryDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaMemoryDefinitionKind.Store;
        public bool IsBlockOut => DefinitionKind == SsaMemoryDefinitionKind.BlockOut;

        public SsaMemoryDescriptor(
            SsaMemoryValueName name,
            SsaMemoryDefinitionKind definitionKind,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            GenTree? defNode,
            CfgBlock? defBlock = null,
            SsaMemoryPhi? phi = null)
        {
            Name = name;
            DefinitionKind = definitionKind;
            DefBlockId = defBlockId;
            DefBlock = defBlock;
            DefStatementIndex = defStatementIndex;
            DefTreeId = defTreeId;
            DefNode = defNode;
            Phi = phi;
        }

        internal void AddUse(int useBlockId)
        {
            if (UseCount < ushort.MaxValue)
                UseCount++;
            if (DefBlockId >= 0 && useBlockId >= 0 && useBlockId != DefBlockId)
                HasGlobalUse = true;
        }

        internal void AddPhiUse(int useBlockId)
        {
            HasPhiUse = true;
            AddUse(useBlockId);
        }

        internal void SetValueNumber(ValueNumber valueNumber)
        {
            ValueNumber = valueNumber;
        }
    }

    internal readonly struct SsaMemoryDefinition
    {
        public readonly SsaMemoryValueName Name;
        public readonly SsaMemoryDefinitionKind DefinitionKind;
        public readonly int DefBlockId;
        public readonly int DefStatementIndex;
        public readonly int DefTreeId;
        public readonly GenTree? DefNode;
        public readonly SsaMemoryPhi? Phi;
        public readonly SsaMemoryDescriptor Descriptor;

        public SsaMemoryDefinition(SsaMemoryDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Name = descriptor.Name;
            DefinitionKind = descriptor.DefinitionKind;
            DefBlockId = descriptor.DefBlockId;
            DefStatementIndex = descriptor.DefStatementIndex;
            DefTreeId = descriptor.DefTreeId;
            DefNode = descriptor.DefNode;
            Phi = descriptor.Phi;
        }

        public bool IsInitial => DefinitionKind == SsaMemoryDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaMemoryDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaMemoryDefinitionKind.Store;
        public bool IsBlockOut => DefinitionKind == SsaMemoryDefinitionKind.BlockOut;
    }

    internal sealed class SsaTree
    {
        public GenTree Source { get; }
        public GenTreeKind Kind => Source.Kind;
        public ImmutableArray<SsaTree> Operands { get; }
        public SsaValueName? Value { get; }
        public SsaValueName? StoreTarget { get; }
        public SsaValueName? LocalFieldBaseValue { get; }
        public RuntimeField? LocalField { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryUses { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryDefinitions { get; }
        public bool IsPartialDefinition => StoreTarget.HasValue && LocalFieldBaseValue.HasValue;
        public bool IsLocalFieldAccess => LocalFieldBaseValue.HasValue && LocalField is not null;
        public bool HasMemoryEffects => !MemoryUses.IsDefaultOrEmpty || !MemoryDefinitions.IsDefaultOrEmpty;

        public SsaTree(
            GenTree source,
            ImmutableArray<SsaTree> operands,
            SsaValueName? value = null,
            SsaValueName? storeTarget = null,
            SsaValueName? localFieldBaseValue = null,
            RuntimeField? localField = null,
            ImmutableArray<SsaMemoryValueName> memoryUses = default,
            ImmutableArray<SsaMemoryValueName> memoryDefinitions = default)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Operands = operands.IsDefault ? ImmutableArray<SsaTree>.Empty : operands;
            Value = value;
            StoreTarget = storeTarget;
            LocalFieldBaseValue = localFieldBaseValue;
            LocalField = localField;
            MemoryUses = memoryUses.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryUses;
            MemoryDefinitions = memoryDefinitions.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryDefinitions;
        }

        public bool TryGetMemoryUse(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryUses, kind, out value);

        public bool TryGetMemoryDefinition(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryDefinitions, kind, out value);

        private static bool TryGetMemoryValue(ImmutableArray<SsaMemoryValueName> values, SsaMemoryKind kind, out SsaMemoryValueName value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Kind == kind)
                {
                    value = values[i];
                    return true;
                }
            }

            value = default;
            return false;
        }

        public override string ToString() => SsaDumper.FormatTree(this);
    }

    internal sealed class SsaBlock
    {
        public CfgBlock CfgBlock { get; }
        public ImmutableArray<SsaPhi> Phis { get; }
        public ImmutableArray<SsaMemoryPhi> MemoryPhis { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryIn { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryOut { get; }
        public ImmutableArray<SsaTree> Statements { get; }

        public int Id => CfgBlock.Id;

        public SsaBlock(
            CfgBlock cfgBlock,
            ImmutableArray<SsaPhi> phis,
            ImmutableArray<SsaTree> statements,
            ImmutableArray<SsaMemoryPhi> memoryPhis = default,
            ImmutableArray<SsaMemoryValueName> memoryIn = default,
            ImmutableArray<SsaMemoryValueName> memoryOut = default)
        {
            CfgBlock = cfgBlock ?? throw new ArgumentNullException(nameof(cfgBlock));
            Phis = phis.IsDefault ? ImmutableArray<SsaPhi>.Empty : phis;
            MemoryPhis = memoryPhis.IsDefault ? ImmutableArray<SsaMemoryPhi>.Empty : memoryPhis;
            MemoryIn = memoryIn.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryIn;
            MemoryOut = memoryOut.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryOut;
            Statements = statements.IsDefault ? ImmutableArray<SsaTree>.Empty : statements;
        }

        public bool TryGetMemoryIn(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryIn, kind, out value);

        public bool TryGetMemoryOut(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryOut, kind, out value);

        private static bool TryGetMemoryValue(ImmutableArray<SsaMemoryValueName> values, SsaMemoryKind kind, out SsaMemoryValueName value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Kind == kind)
                {
                    value = values[i];
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    internal sealed class SsaMethod
    {
        public GenTreeMethod GenTreeMethod { get; }
        public ControlFlowGraph Cfg { get; }
        public ImmutableArray<SsaSlotInfo> Slots { get; }
        public ImmutableArray<SsaLocalDescriptor> SsaLocalDescriptors { get; }
        public ImmutableArray<SsaValueName> InitialValues { get; }
        public ImmutableArray<SsaMemoryValueName> InitialMemoryValues { get; }
        public ImmutableArray<SsaValueDefinition> ValueDefinitions { get; }
        public ImmutableArray<SsaMemoryDefinition> MemoryDefinitions { get; }
        public ImmutableArray<SsaBlock> Blocks { get; }
        public SsaValueNumberingResult? ValueNumbers { get; }

        public SsaMethod(
            GenTreeMethod genTreeMethod,
            ControlFlowGraph cfg,
            ImmutableArray<SsaSlotInfo> slots,
            ImmutableArray<SsaValueName> initialValues,
            ImmutableArray<SsaValueDefinition> valueDefinitions,
            ImmutableArray<SsaBlock> blocks,
            SsaValueNumberingResult? valueNumbers = null,
            ImmutableArray<SsaLocalDescriptor> ssaLocalDescriptors = default,
            ImmutableArray<SsaMemoryValueName> initialMemoryValues = default,
            ImmutableArray<SsaMemoryDefinition> memoryDefinitions = default)
        {
            GenTreeMethod = genTreeMethod ?? throw new ArgumentNullException(nameof(genTreeMethod));
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Slots = slots.IsDefault ? ImmutableArray<SsaSlotInfo>.Empty : slots;
            SsaLocalDescriptors = ssaLocalDescriptors.IsDefault ? ImmutableArray<SsaLocalDescriptor>.Empty : ssaLocalDescriptors;
            InitialValues = initialValues.IsDefault ? ImmutableArray<SsaValueName>.Empty : initialValues;
            InitialMemoryValues = initialMemoryValues.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : initialMemoryValues;
            ValueDefinitions = valueDefinitions.IsDefault ? ImmutableArray<SsaValueDefinition>.Empty : valueDefinitions;
            MemoryDefinitions = memoryDefinitions.IsDefault ? ImmutableArray<SsaMemoryDefinition>.Empty : memoryDefinitions;
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
            ValueNumbers = valueNumbers;
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



    internal enum SsaLocalAccessKind : byte
    {
        None,
        Use,
        FullDefinition,
        PartialDefinition,
        Address,
    }

    internal readonly struct SsaLocalAccess
    {
        public readonly SsaLocalAccessKind Kind;
        public readonly SsaSlot Slot;
        public readonly SsaSlot BaseSlot;
        public readonly RuntimeField? Field;
        public readonly GenTree? Receiver;
        public readonly int ReceiverOperandIndex;

        public SsaLocalAccess(
            SsaLocalAccessKind kind,
            SsaSlot slot,
            RuntimeField? field = null,
            GenTree? receiver = null,
            int receiverOperandIndex = -1,
            SsaSlot? baseSlot = null)
        {
            Kind = kind;
            Slot = slot;
            BaseSlot = baseSlot ?? slot;
            Field = field;
            Receiver = receiver;
            ReceiverOperandIndex = receiverOperandIndex;
        }

        public bool IsUse => Kind == SsaLocalAccessKind.Use;
        public bool IsFullDefinition => Kind == SsaLocalAccessKind.FullDefinition;
        public bool IsPartialDefinition => Kind == SsaLocalAccessKind.PartialDefinition;
        public bool IsAddress => Kind == SsaLocalAccessKind.Address;
        public bool IsDefinition => IsFullDefinition || IsPartialDefinition;
        public bool IsPromotedFieldAccess => Field is not null && !Slot.Equals(BaseSlot);
    }

    internal static class SsaSlotHelpers
    {
        public static bool TryGetLoadSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (TryGetDirectLoadSlot(node, out slot))
                return true;

            if (TryGetLocalFieldAccess(node, out var access) && access.Kind == SsaLocalAccessKind.Use)
            {
                slot = access.Slot;
                return true;
            }

            slot = default;
            return false;
        }

        public static bool TryGetStoreSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (TryGetDirectStoreSlot(node, out slot))
                return true;

            if (TryGetLocalFieldAccess(node, out var access) && access.IsDefinition)
            {
                slot = access.Slot;
                return true;
            }

            slot = default;
            return false;
        }

        public static bool TryGetDirectLoadSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.Arg:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.Local:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                case GenTreeKind.Temp:
                    return TryMakeSlot(node, SsaSlotKind.Temp, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetDirectStoreSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.StoreArg:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.StoreLocal:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                case GenTreeKind.StoreTemp:
                    return TryMakeSlot(node, SsaSlotKind.Temp, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetAddressExposedSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.ArgAddr:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.LocalAddr:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetLocalFieldAccess(GenTree node, out SsaLocalAccess access)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (node.Kind == GenTreeKind.Field && node.Operands.Length != 0)
            {
                var receiver = node.Operands[0];
                if (TryGetContainedLocalAddressSlot(receiver, out var parentSlot))
                {
                    var slot = ResolvePromotedFieldSlot(receiver, parentSlot, node.Field);
                    access = new SsaLocalAccess(SsaLocalAccessKind.Use, slot, node.Field, receiver, 0, parentSlot);
                    return true;
                }
            }

            if (node.Kind == GenTreeKind.StoreField && node.Operands.Length >= 2)
            {
                var receiver = node.Operands[0];
                if (TryGetContainedLocalAddressSlot(receiver, out var parentSlot))
                {
                    var slot = ResolvePromotedFieldSlot(receiver, parentSlot, node.Field);
                    var kind = slot.Equals(parentSlot) ? SsaLocalAccessKind.PartialDefinition : SsaLocalAccessKind.FullDefinition;
                    access = new SsaLocalAccess(kind, slot, node.Field, receiver, 0, parentSlot);
                    return true;
                }
            }

            access = default;
            return false;
        }

        private static SsaSlot ResolvePromotedFieldSlot(GenTree receiver, SsaSlot parentSlot, RuntimeField? field)
        {
            if (field is not null && receiver.LocalDescriptor is not null && receiver.LocalDescriptor.TryGetPromotedField(field, out var fieldDescriptor))
                return new SsaSlot(fieldDescriptor);

            return parentSlot;
        }

        public static bool IsContainedLocalFieldAddressUse(GenTree parent, int operandIndex)
        {
            if (parent is null)
                return false;

            return operandIndex == 0 && parent.Kind is GenTreeKind.Field or GenTreeKind.StoreField;
        }

        private static bool TryGetContainedLocalAddressSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (node.Kind == GenTreeKind.ArgAddr)
                return TryMakeSlot(node, SsaSlotKind.Arg, out slot);

            if (node.Kind == GenTreeKind.LocalAddr)
                return TryMakeSlot(node, SsaSlotKind.Local, out slot);

            slot = default;
            return false;
        }

        private static bool TryMakeSlot(GenTree node, SsaSlotKind expectedKind, out SsaSlot slot)
        {
            if (node.LocalDescriptor is not null)
            {
                slot = new SsaSlot(node.LocalDescriptor);
                if (slot.Kind != expectedKind)
                    throw new InvalidOperationException("GenTree local descriptor kind does not match node kind: " + node + ".");
                if (node.LocalDescriptor.Index != node.Int32)
                    throw new InvalidOperationException("GenTree local descriptor index does not match node index: " + node + ".");
                return true;
            }

            slot = new SsaSlot(expectedKind, node.Int32);
            return true;
        }
    }


    internal sealed class GenTreeLocalTrackingResult
    {
        public ImmutableArray<SsaSlotInfo> AllSlots { get; }
        public ImmutableArray<SsaSlot> TrackedSlots { get; }
        public ImmutableArray<SsaSlot> SsaCandidateSlots { get; }
        public TrackedLocalTable TrackedLocals { get; }

        public GenTreeLocalTrackingResult(
            ImmutableArray<SsaSlotInfo> allSlots,
            ImmutableArray<SsaSlot> trackedSlots,
            ImmutableArray<SsaSlot> ssaCandidateSlots,
            TrackedLocalTable trackedLocals)
        {
            AllSlots = allSlots.IsDefault ? ImmutableArray<SsaSlotInfo>.Empty : allSlots;
            TrackedSlots = trackedSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : trackedSlots;
            SsaCandidateSlots = ssaCandidateSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : ssaCandidateSlots;
            TrackedLocals = trackedLocals ?? TrackedLocalTable.Empty;
            if (TrackedLocals.Count != TrackedSlots.Length)
                throw new InvalidOperationException("Tracked local table and tracked local slot list disagree.");
        }
    }

    internal sealed class TrackedLocalTable
    {
        public static readonly TrackedLocalTable Empty = new TrackedLocalTable(ImmutableArray<SsaSlot>.Empty);

        private readonly Dictionary<SsaSlot, int> _varIndexBySlot;

        public ImmutableArray<SsaSlot> Slots { get; }
        public int Count => Slots.Length;

        public TrackedLocalTable(ImmutableArray<SsaSlot> slots)
        {
            Slots = slots.IsDefault ? ImmutableArray<SsaSlot>.Empty : slots;
            _varIndexBySlot = new Dictionary<SsaSlot, int>(Slots.Length);

            for (int i = 0; i < Slots.Length; i++)
            {
                if (!_varIndexBySlot.TryAdd(Slots[i], i))
                    throw new InvalidOperationException("Duplicate tracked local slot " + Slots[i] + ".");
            }
        }

        public bool Contains(SsaSlot slot) => _varIndexBySlot.ContainsKey(slot);

        public bool TryGetVarIndex(SsaSlot slot, out int varIndex) => _varIndexBySlot.TryGetValue(slot, out varIndex);

        public int GetVarIndex(SsaSlot slot)
        {
            if (_varIndexBySlot.TryGetValue(slot, out int varIndex))
                return varIndex;

            throw new InvalidOperationException("Local " + slot + " is not a tracked local.");
        }

        public SsaSlot GetSlot(int varIndex)
        {
            if ((uint)varIndex >= (uint)Slots.Length)
                throw new ArgumentOutOfRangeException(nameof(varIndex));
            return Slots[varIndex];
        }

        public TrackedLocalSet NewEmptySet() => new TrackedLocalSet(this);
    }

    internal sealed class TrackedLocalSet
    {
        private readonly ulong[] _bits;

        public TrackedLocalTable Table { get; }
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _bits.Length; i++)
                    count += PopCount(_bits[i]);
                return count;
            }
        }

        public TrackedLocalSet(TrackedLocalTable table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            _bits = new ulong[(Table.Count + 63) >> 6];
        }

        private TrackedLocalSet(TrackedLocalTable table, ulong[] bits)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            _bits = bits ?? throw new ArgumentNullException(nameof(bits));
        }

        public TrackedLocalSet Clone()
        {
            var copy = new ulong[_bits.Length];
            Array.Copy(_bits, copy, _bits.Length);
            return new TrackedLocalSet(Table, copy);
        }

        public bool Add(SsaSlot slot)
        {
            if (!Table.TryGetVarIndex(slot, out int varIndex))
                return false;
            return AddIndex(varIndex);
        }

        public bool AddIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            int word = varIndex >> 6;
            ulong mask = 1UL << (varIndex & 63);
            ulong old = _bits[word];
            _bits[word] = old | mask;
            return (old & mask) == 0;
        }

        public bool Remove(SsaSlot slot)
        {
            if (!Table.TryGetVarIndex(slot, out int varIndex))
                return false;
            return RemoveIndex(varIndex);
        }

        public bool RemoveIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            int word = varIndex >> 6;
            ulong mask = 1UL << (varIndex & 63);
            ulong old = _bits[word];
            _bits[word] = old & ~mask;
            return (old & mask) != 0;
        }

        public bool Contains(SsaSlot slot)
            => Table.TryGetVarIndex(slot, out int varIndex) && ContainsIndex(varIndex);

        public bool ContainsIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            return (_bits[varIndex >> 6] & (1UL << (varIndex & 63))) != 0;
        }

        public bool UnionWith(TrackedLocalSet other)
        {
            CheckCompatible(other);
            bool changed = false;
            for (int i = 0; i < _bits.Length; i++)
            {
                ulong old = _bits[i];
                ulong next = old | other._bits[i];
                _bits[i] = next;
                changed |= next != old;
            }
            return changed;
        }

        public bool ExceptWith(TrackedLocalSet other)
        {
            CheckCompatible(other);
            bool changed = false;
            for (int i = 0; i < _bits.Length; i++)
            {
                ulong old = _bits[i];
                ulong next = old & ~other._bits[i];
                _bits[i] = next;
                changed |= next != old;
            }
            return changed;
        }

        public bool SetEquals(TrackedLocalSet other)
        {
            CheckCompatible(other);
            for (int i = 0; i < _bits.Length; i++)
            {
                if (_bits[i] != other._bits[i])
                    return false;
            }
            return true;
        }

        public ImmutableArray<SsaSlot> ToImmutableSlots()
        {
            var builder = ImmutableArray.CreateBuilder<SsaSlot>();
            for (int i = 0; i < Table.Count; i++)
            {
                if (ContainsIndex(i))
                    builder.Add(Table.GetSlot(i));
            }
            return builder.ToImmutable();
        }

        private void CheckVarIndex(int varIndex)
        {
            if ((uint)varIndex >= (uint)Table.Count)
                throw new ArgumentOutOfRangeException(nameof(varIndex));
        }

        private void CheckCompatible(TrackedLocalSet other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            if (!ReferenceEquals(Table, other.Table))
                throw new InvalidOperationException("Tracked local bitsets were built from different tracked-local tables.");
        }

        private static int PopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        public override string ToString() => "{" + string.Join(", ", ToImmutableSlots()) + "}";
    }

    internal static class GenTreeLocalTracking
    {
        private const int MaxTrackedLocals = 512;

        public static GenTreeLocalTrackingResult AssignTrackedLocals(GenTreeMethod method, ControlFlowGraph cfg)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (cfg is null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("local tracking requires a CFG that matches the method block count.");

            ResetDescriptors(method.ArgDescriptors);
            ResetDescriptors(method.LocalDescriptors);
            ResetDescriptors(method.TempDescriptors);
            method.EnsurePromotedStructFieldLocals();

            var addressExposed = new HashSet<SsaSlot>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    CollectAddressExposed(statements[s], addressExposed);
            }

            var ehExposed = BuildEhExposedSlots(cfg);
            var structPromotionBlockedParents = BuildStructPromotionBlockedParents(method);
            var weightedUses = ComputeWeightedSlotUses(method, cfg);
            var allSlots = new List<SsaSlotInfo>();
            var trackingCandidates = new List<SsaSlot>();
            var descriptors = new Dictionary<SsaSlot, GenLocalDescriptor>();

            for (int i = 0; i < method.ArgDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.ArgDescriptors[i]), method.ArgDescriptors[i]);

            for (int i = 0; i < method.LocalDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.LocalDescriptors[i]), method.LocalDescriptors[i]);

            for (int i = 0; i < method.TempDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.TempDescriptors[i]), method.TempDescriptors[i]);

            trackingCandidates.Sort((a, b) =>
            {
                weightedUses.TryGetValue(a, out int aw);
                weightedUses.TryGetValue(b, out int bw);
                int c = bw.CompareTo(aw);
                return c != 0 ? c : a.CompareTo(b);
            });

            var tracked = new HashSet<SsaSlot>();
            int trackedCount = Math.Min(MaxTrackedLocals, trackingCandidates.Count);
            for (int i = 0; i < trackedCount; i++)
                tracked.Add(trackingCandidates[i]);

            var trackedList = new List<SsaSlot>(tracked);
            trackedList.Sort();
            for (int i = 0; i < trackedList.Count; i++)
            {
                var slot = trackedList[i];
                var descriptor = descriptors[slot];
                if (CanTrackAsScalar(slot, descriptor))
                {
                    descriptor.MarkRegularPromotedScalar(i);
                }
                else
                {
                    descriptor.MarkTrackedButNotSsa(i, descriptor.Category is GenLocalCategory.Unclassified or GenLocalCategory.UntrackedLocal
                        ? GenLocalCategory.TrackedNonSsaLocal
                        : descriptor.Category);
                }
            }

            foreach (var kv in descriptors)
            {
                if (tracked.Contains(kv.Key))
                    continue;

                var descriptor = kv.Value;
                if (descriptor.AddressExposed)
                    descriptor.MarkAddressExposed();
                else if (descriptor.MemoryAliased)
                    descriptor.MarkMemoryAliased();
                else if (descriptor.IsImplicitByRef || descriptor.Pinned || descriptor.IsRefLike)
                    descriptor.MarkUntracked();
                else if (descriptor.IsCompilerTemp)
                {
                    descriptor.MarkUntracked();
                    descriptor.Category = GenLocalCategory.CompilerTemp;
                }
                else if (descriptor.Promoted && descriptor.Category == GenLocalCategory.PromotedStruct)
                    descriptor.MarkPromotedStructParent();
                else
                    descriptor.MarkUntracked();
            }

            allSlots.Clear();
            foreach (var kv in descriptors)
                allSlots.Add(CreateSlotInfo(kv.Key, kv.Value));
            allSlots.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            var ssaCandidates = new List<SsaSlot>();
            for (int i = 0; i < trackedList.Count; i++)
            {
                var slot = trackedList[i];
                if (descriptors[slot].CanBeSsaRenamedAsScalar)
                    ssaCandidates.Add(slot);
            }

            var trackedSlots = trackedList.ToImmutableArray();
            return new GenTreeLocalTrackingResult(
                allSlots.ToImmutableArray(),
                trackedSlots,
                ssaCandidates.ToImmutableArray(),
                new TrackedLocalTable(trackedSlots));

            void AddDescriptor(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                bool exposed = descriptor.AddressExposed || addressExposed.Contains(slot) || ehExposed.Contains(slot);
                if (exposed)
                    descriptor.MarkAddressExposed();

                if (descriptor.IsStructField && structPromotionBlockedParents.Contains(descriptor.ParentLclNum))
                    descriptor.MarkMemoryAliased();

                descriptors[slot] = descriptor;

                if (!CanTrackForLiveness(slot, descriptor))
                    return;

                if (!weightedUses.ContainsKey(slot))
                    return;

                trackingCandidates.Add(slot);
            }

            static SsaSlotInfo CreateSlotInfo(SsaSlot slot, GenLocalDescriptor descriptor)
                => new SsaSlotInfo(
                    slot,
                    descriptor.Type,
                    descriptor.StackKind,
                    descriptor.AddressExposed,
                    descriptor.MemoryAliased,
                    descriptor.Category,
                    descriptor.LclNum,
                    descriptor.VarIndex,
                    descriptor.Tracked,
                    descriptor.SsaPromoted,
                    descriptor);

            static bool CanTrackForLiveness(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                if (descriptor.AddressExposed || descriptor.MemoryAliased)
                    return false;

                if (descriptor.IsImplicitByRef || descriptor.Pinned || descriptor.IsRefLike)
                    return false;

                if (descriptor.Category is GenLocalCategory.AddressExposedLocal or GenLocalCategory.MemoryAliasedLocal or GenLocalCategory.ImplicitByRefPinnedRefLikeLocal)
                    return false;

                if (descriptor.Category == GenLocalCategory.PromotedStruct)
                    return false;

                if (descriptor.StackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                    return false;

                return true;
            }

            static bool CanTrackAsScalar(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                if (!CanTrackForLiveness(slot, descriptor))
                    return false;

                if (descriptor.DoNotEnregister && !descriptor.IsCompilerTemp)
                    return false;

                if (descriptor.Category == GenLocalCategory.PromotedStruct)
                    return false;

                if (descriptor.IsStructField && descriptor.Category != GenLocalCategory.PromotedStructField)
                    return false;

                if (!descriptor.IsStructField && descriptor.Category is GenLocalCategory.PromotedStructField or GenLocalCategory.AddressExposedLocal or GenLocalCategory.MemoryAliasedLocal or GenLocalCategory.ImplicitByRefPinnedRefLikeLocal)
                    return false;

                return IsPromotableStorageSlot(descriptor.Type, descriptor.StackKind);
            }
        }

        public static ImmutableArray<SsaSlot> CurrentTrackedSlots(GenTreeMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var result = new List<SsaSlot>();
            AddTracked(method.ArgDescriptors, SsaSlotKind.Arg, result);
            AddTracked(method.LocalDescriptors, SsaSlotKind.Local, result);
            AddTracked(method.TempDescriptors, SsaSlotKind.Temp, result);
            result.Sort();
            return result.ToImmutableArray();
        }

        private static void AddTracked(ImmutableArray<GenLocalDescriptor> descriptors, SsaSlotKind kind, List<SsaSlot> result)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor.IsTrackedForLiveness)
                    result.Add(new SsaSlot(descriptor));
            }
        }

        private static void ResetDescriptors(ImmutableArray<GenLocalDescriptor> descriptors)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                descriptor.ResetTrackingAndLivenessState();
            }
        }

        private static void CollectAddressExposed(GenTree node, HashSet<SsaSlot> addressExposed)
        {
            CollectAddressExposed(node, parent: null, operandIndex: -1, addressExposed);
        }

        private static void CollectAddressExposed(GenTree node, GenTree? parent, int operandIndex, HashSet<SsaSlot> addressExposed)
        {
            if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot) &&
                (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
            {
                addressExposed.Add(slot);
                if (node.LocalDescriptor is not null)
                {
                    node.LocalDescriptor.MarkAddressExposed();
                    node.Flags |= GenTreeFlags.AddressExposed;
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                CollectAddressExposed(node.Operands[i], node, i, addressExposed);
        }

        private static HashSet<int> BuildStructPromotionBlockedParents(GenTreeMethod method)
        {
            var blocked = new HashSet<int>();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Visit(statements[s], parent: null, operandIndex: -1);
            }

            return blocked;

            void Visit(GenTree node, GenTree? parent, int operandIndex)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        Visit(node.Operands[i], node, i);
                    }
                    return;
                }

                if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out _) ||
                    SsaSlotHelpers.TryGetDirectStoreSlot(node, out _))
                {
                    if (node.LocalDescriptor is { HasPromotedStructFields: true } descriptor)
                        blocked.Add(descriptor.LclNum);
                }

                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _) &&
                    (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
                {
                    if (node.LocalDescriptor is { HasPromotedStructFields: true } descriptor)
                        blocked.Add(descriptor.LclNum);
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    Visit(node.Operands[i], node, i);
            }
        }

        private static Dictionary<SsaSlot, int> ComputeWeightedSlotUses(GenTreeMethod method, ControlFlowGraph cfg)
        {
            var result = new Dictionary<SsaSlot, int>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                int weight = 1 + 8 * LoopDepth(cfg, b);
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    CountSlotUses(statements[s], result, weight);
            }
            return result;
        }

        private static int LoopDepth(ControlFlowGraph cfg, int blockId)
        {
            int depth = 0;
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                if (cfg.NaturalLoops[i].Contains(blockId))
                    depth++;
            }
            return depth;
        }

        private static void CountSlotUses(GenTree node, Dictionary<SsaSlot, int> counts, int weight)
        {
            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
            {
                if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                {
                    AddUse(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight);
                }
                else if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    AddUse(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight);
                    AddDef(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight, partial: true);
                }
                else if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                {
                    AddDef(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight, partial: false);
                }

                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i == fieldAccess.ReceiverOperandIndex)
                        continue;
                    CountSlotUses(node.Operands[i], counts, weight);
                }
                return;
            }

            if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
            {
                AddUse(loadSlot, node.LocalDescriptor, weight);
                return;
            }

            if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
            {
                for (int i = 0; i < node.Operands.Length; i++)
                    CountSlotUses(node.Operands[i], counts, weight);
                AddDef(storeSlot, node.LocalDescriptor, weight, partial: false);
                return;
            }

            for (int i = 0; i < node.Operands.Length; i++)
                CountSlotUses(node.Operands[i], counts, weight);

            GenLocalDescriptor? DescriptorForFieldAccess(SsaLocalAccess access, GenTree node)
            {
                if (access.Field is not null && access.Receiver?.LocalDescriptor is not null && access.Receiver.LocalDescriptor.TryGetPromotedField(access.Field, out var fieldDescriptor))
                    return fieldDescriptor;
                return node.LocalDescriptor ?? access.Receiver?.LocalDescriptor;
            }

            void AddUse(SsaSlot slot, GenLocalDescriptor? descriptor, int w)
            {
                counts.TryGetValue(slot, out int current);
                counts[slot] = current + Math.Max(1, w);
                descriptor?.AddUse(w);
            }

            void AddDef(SsaSlot slot, GenLocalDescriptor? descriptor, int w, bool partial)
            {
                counts.TryGetValue(slot, out int current);
                counts[slot] = current + Math.Max(1, w);
                if (partial)
                    descriptor?.AddPartialDefinition(w);
                else
                    descriptor?.AddFullDefinition(w);
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
                        touchedFunclets.Add(slot, funcletId);
                    else if (previous != funcletId)
                        exposed.Add(slot);
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
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);
                    }

                    if (fieldAccess.Kind != SsaLocalAccessKind.FullDefinition && !defs.Contains(fieldAccess.Slot))
                        uses.Add(fieldAccess.Slot);
                    if (fieldAccess.IsDefinition)
                        defs.Add(fieldAccess.Slot);
                    touched.Add(fieldAccess.Slot);
                    return;
                }

                if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                        CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);

                    defs.Add(storeSlot);
                    touched.Add(storeSlot);
                    return;
                }

                if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
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

        private static bool IsPromotableStorageSlot(RuntimeType? type, GenStackKind stackKind)
        {
            if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                return false;

            if (stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                return true;

            if (type is not null)
            {
                if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                    return true;

                if (type.IsValueType && type.ContainsGcPointers)
                    return false;
            }

            if (type is null)
                return stackKind is
                    GenStackKind.I4 or
                    GenStackKind.I8 or
                    GenStackKind.R4 or
                    GenStackKind.R8 or
                    GenStackKind.NativeInt or
                    GenStackKind.NativeUInt or
                    GenStackKind.Ptr;

            return MachineAbi.IsPhysicallyPromotableStorage(type, stackKind);
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
                        return GenStackKind.R4;
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

    internal sealed class GenTreeLocalLiveness
    {
        public ControlFlowGraph Cfg { get; }
        public TrackedLocalTable TrackedLocals { get; }
        public ImmutableArray<SsaSlot> TrackedSlots { get; }
        public ImmutableArray<SsaSlot> SsaCandidateSlots { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> Uses { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> Defs { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> LiveIn { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> LiveOut { get; }
        public ImmutableArray<TrackedLocalSet> UseBits { get; }
        public ImmutableArray<TrackedLocalSet> DefBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveInBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveOutBits { get; }

        private GenTreeLocalLiveness(
            ControlFlowGraph cfg,
            TrackedLocalTable trackedLocals,
            ImmutableArray<SsaSlot> ssaCandidateSlots,
            TrackedLocalSet[] uses,
            TrackedLocalSet[] defs,
            TrackedLocalSet[] liveIn,
            TrackedLocalSet[] liveOut)
        {
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            TrackedLocals = trackedLocals ?? TrackedLocalTable.Empty;
            TrackedSlots = TrackedLocals.Slots;
            SsaCandidateSlots = ssaCandidateSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : ssaCandidateSlots;
            Uses = FreezeSets(uses);
            Defs = FreezeSets(defs);
            LiveIn = FreezeSets(liveIn);
            LiveOut = FreezeSets(liveOut);
            UseBits = FreezeBitSets(uses);
            DefBits = FreezeBitSets(defs);
            LiveInBits = FreezeBitSets(liveIn);
            LiveOutBits = FreezeBitSets(liveOut);
        }

        public bool IsLiveIn(int blockId, SsaSlot slot) => Contains(LiveInBits, blockId, slot);
        public bool IsLiveOut(int blockId, SsaSlot slot) => Contains(LiveOutBits, blockId, slot);
        public bool IsUsedInBlock(int blockId, SsaSlot slot) => Contains(UseBits, blockId, slot);
        public bool IsDefinedInBlock(int blockId, SsaSlot slot) => Contains(DefBits, blockId, slot);

        public static GenTreeLocalLiveness Build(GenTreeMethod method, ControlFlowGraph cfg)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("HIR liveness CFG does not match method block count.");

            var tracking = GenTreeLocalTracking.AssignTrackedLocals(method, cfg);
            var trackedLocals = tracking.TrackedLocals;
            int blockCount = cfg.Blocks.Length;
            var uses = NewSetArray(blockCount, trackedLocals);
            var defs = NewSetArray(blockCount, trackedLocals);
            var liveIn = NewSetArray(blockCount, trackedLocals);
            var liveOut = NewSetArray(blockCount, trackedLocals);

            for (int b = 0; b < blockCount; b++)
            {
                var statements = cfg.Blocks[b].SourceBlock.Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    ClearLocalDataflowFlags(statements[s]);
                    CollectUseDef(statements[s], trackedLocals, uses[b], defs[b]);
                }
            }

            bool changed;
            do
            {
                changed = false;
                for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                {
                    int blockId = cfg.ReversePostOrder[r];
                    var newOut = trackedLocals.NewEmptySet();
                    var successors = cfg.Blocks[blockId].Successors;
                    for (int i = 0; i < successors.Length; i++)
                        newOut.UnionWith(liveIn[successors[i].ToBlockId]);

                    var newIn = newOut.Clone();
                    newIn.ExceptWith(defs[blockId]);
                    newIn.UnionWith(uses[blockId]);

                    if (!liveOut[blockId].SetEquals(newOut))
                    {
                        liveOut[blockId] = newOut;
                        changed = true;
                    }

                    if (!liveIn[blockId].SetEquals(newIn))
                    {
                        liveIn[blockId] = newIn;
                        changed = true;
                    }
                }
            }
            while (changed);

            MarkLastUses(cfg, trackedLocals, liveOut);

            return new GenTreeLocalLiveness(
                cfg,
                trackedLocals,
                tracking.SsaCandidateSlots,
                uses,
                defs,
                liveIn,
                liveOut);
        }

        private static void MarkLastUses(ControlFlowGraph cfg, TrackedLocalTable trackedLocals, TrackedLocalSet[] liveOut)
        {
            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                var live = liveOut[b].Clone();
                var events = new List<LocalLivenessEvent>();
                var statements = cfg.Blocks[b].SourceBlock.Statements;

                for (int s = 0; s < statements.Length; s++)
                    CollectLivenessEvents(statements[s], trackedLocals, events);

                for (int i = events.Count - 1; i >= 0; i--)
                {
                    var e = events[i];
                    if (e.IsDefinition)
                        live.Remove(e.Slot);

                    if (e.IsUse)
                    {
                        if (!live.Contains(e.Slot))
                            e.Node.Flags |= GenTreeFlags.VarDeath;
                        live.Add(e.Slot);
                    }
                }
            }
        }

        private readonly struct LocalLivenessEvent
        {
            public readonly GenTree Node;
            public readonly SsaSlot Slot;
            public readonly bool IsUse;
            public readonly bool IsDefinition;

            public LocalLivenessEvent(GenTree node, SsaSlot slot, bool isUse, bool isDefinition)
            {
                Node = node;
                Slot = slot;
                IsUse = isUse;
                IsDefinition = isDefinition;
            }
        }

        private static void CollectLivenessEvents(GenTree node, TrackedLocalTable trackedLocals, List<LocalLivenessEvent> events)
        {
            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
            {
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i == fieldAccess.ReceiverOperandIndex)
                        continue;
                    CollectLivenessEvents(node.Operands[i], trackedLocals, events);
                }

                if (trackedLocals.Contains(fieldAccess.Slot))
                {
                    events.Add(new LocalLivenessEvent(
                        node,
                        fieldAccess.Slot,
                        isUse: fieldAccess.Kind != SsaLocalAccessKind.FullDefinition,
                        isDefinition: fieldAccess.IsDefinition));
                }
                return;
            }

            if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
            {
                for (int i = 0; i < node.Operands.Length; i++)
                    CollectLivenessEvents(node.Operands[i], trackedLocals, events);

                if (trackedLocals.Contains(storeSlot))
                    events.Add(new LocalLivenessEvent(node, storeSlot, isUse: false, isDefinition: true));
                return;
            }

            if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
            {
                if (trackedLocals.Contains(loadSlot))
                    events.Add(new LocalLivenessEvent(node, loadSlot, isUse: true, isDefinition: false));
                return;
            }

            for (int i = 0; i < node.Operands.Length; i++)
                CollectLivenessEvents(node.Operands[i], trackedLocals, events);
        }

        private static bool Contains(ImmutableArray<TrackedLocalSet> sets, int blockId, SsaSlot slot)
        {
            if ((uint)blockId >= (uint)sets.Length)
                throw new ArgumentOutOfRangeException(nameof(blockId));
            return sets[blockId].Contains(slot);
        }

        private static void ClearLocalDataflowFlags(GenTree node)
        {
            node.Flags &= ~(GenTreeFlags.VarDef | GenTreeFlags.VarUseAsg | GenTreeFlags.VarDeath);
            for (int i = 0; i < node.Operands.Length; i++)
                ClearLocalDataflowFlags(node.Operands[i]);
        }

        private static void CollectUseDef(GenTree node, TrackedLocalTable trackedLocals, TrackedLocalSet uses, TrackedLocalSet defs)
        {
            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
            {
                if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                {
                    MarkUse(node, fieldAccess.Slot, trackedLocals, uses, defs);
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CollectUseDef(node.Operands[i], trackedLocals, uses, defs);
                    }
                    return;
                }

                if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CollectUseDef(node.Operands[i], trackedLocals, uses, defs);
                    }

                    MarkUse(node, fieldAccess.Slot, trackedLocals, uses, defs);
                    if (trackedLocals.Contains(fieldAccess.Slot))
                        defs.Add(fieldAccess.Slot);
                    node.Flags |= GenTreeFlags.LocalUse | GenTreeFlags.LocalDef | GenTreeFlags.VarDef | GenTreeFlags.VarUseAsg;
                    return;
                }

                if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CollectUseDef(node.Operands[i], trackedLocals, uses, defs);
                    }

                    if (trackedLocals.Contains(fieldAccess.Slot))
                        defs.Add(fieldAccess.Slot);
                    node.Flags |= GenTreeFlags.LocalDef | GenTreeFlags.VarDef;
                    return;
                }
            }

            if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
            {
                for (int i = 0; i < node.Operands.Length; i++)
                    CollectUseDef(node.Operands[i], trackedLocals, uses, defs);

                if (trackedLocals.Contains(storeSlot))
                    defs.Add(storeSlot);
                node.Flags |= GenTreeFlags.LocalDef | GenTreeFlags.VarDef;
                return;
            }

            if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
            {
                MarkUse(node, loadSlot, trackedLocals, uses, defs);
                return;
            }

            for (int i = 0; i < node.Operands.Length; i++)
                CollectUseDef(node.Operands[i], trackedLocals, uses, defs);
        }

        private static void MarkUse(GenTree node, SsaSlot slot, TrackedLocalTable trackedLocals, TrackedLocalSet uses, TrackedLocalSet defs)
        {
            if (trackedLocals.Contains(slot) && !defs.Contains(slot))
                uses.Add(slot);
            node.Flags |= GenTreeFlags.LocalUse;
        }

        private static TrackedLocalSet[] NewSetArray(int count, TrackedLocalTable table)
        {
            var result = new TrackedLocalSet[count];
            for (int i = 0; i < result.Length; i++)
                result[i] = table.NewEmptySet();
            return result;
        }

        private static ImmutableArray<TrackedLocalSet> FreezeBitSets(TrackedLocalSet[] sets)
        {
            var builder = ImmutableArray.CreateBuilder<TrackedLocalSet>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
                builder.Add(sets[i].Clone());
            return builder.ToImmutable();
        }

        private static ImmutableArray<ImmutableArray<SsaSlot>> FreezeSets(TrackedLocalSet[] sets)
        {
            var builder = ImmutableArray.CreateBuilder<ImmutableArray<SsaSlot>>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
                builder.Add(sets[i].ToImmutableSlots());
            return builder.ToImmutable();
        }
    }

    internal static class SsaSourceAnnotations
    {
        public static void Clear(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    ClearTree(statements[s]);
            }
        }

        public static void Attach(SsaMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            Clear(method.GenTreeMethod);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    AttachTree(method, statements[s]);
            }
        }

        private static void ClearTree(GenTree node)
        {
            node.ClearSsaAnnotation();
            for (int i = 0; i < node.Operands.Length; i++)
                ClearTree(node.Operands[i]);
        }

        private static void AttachTree(SsaMethod method, SsaTree tree)
        {
            if (tree.Value.HasValue)
            {
                tree.Source.AttachSsaUse(tree.Value.Value);
                AttachDescriptor(method.GenTreeMethod, tree.Source, tree.Value.Value.Slot);
            }

            for (int i = 0; i < tree.Operands.Length; i++)
                AttachTree(method, tree.Operands[i]);

            if (tree.StoreTarget.HasValue)
            {
                var target = tree.StoreTarget.Value;
                var info = GetSlotInfo(method, target.Slot);
                tree.Source.AttachSsaDefinition(target, info.Type, info.StackKind);
                AttachDescriptor(method.GenTreeMethod, tree.Source, target.Slot);
            }
        }

        private static SsaSlotInfo GetSlotInfo(SsaMethod method, SsaSlot slot)
        {
            for (int i = 0; i < method.Slots.Length; i++)
            {
                if (method.Slots[i].Slot.Equals(slot))
                    return method.Slots[i];
            }

            return new SsaSlotInfo(slot, null, GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
        }

        private static void AttachDescriptor(GenTreeMethod method, GenTree node, SsaSlot slot)
        {
            if (node.LocalDescriptor is not null)
                return;

            if (TryGetDescriptor(method, slot, out var descriptor))
                node.LocalDescriptor = descriptor;
        }

        private static bool TryGetDescriptor(GenTreeMethod method, SsaSlot slot, out GenLocalDescriptor descriptor)
        {
            if (slot.HasLclNum)
            {
                var all = method.AllLocalDescriptors;
                if ((uint)slot.LclNum < (uint)all.Length)
                {
                    descriptor = all[slot.LclNum];
                    return descriptor.Kind switch
                    {
                        GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg,
                        GenLocalKind.Local => slot.Kind == SsaSlotKind.Local,
                        GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp,
                        _ => false,
                    };
                }
            }

            switch (slot.Kind)
            {
                case SsaSlotKind.Arg:
                    if ((uint)slot.Index < (uint)method.ArgDescriptors.Length)
                    {
                        descriptor = method.ArgDescriptors[slot.Index];
                        return true;
                    }
                    break;
                case SsaSlotKind.Local:
                    if ((uint)slot.Index < (uint)method.LocalDescriptors.Length)
                    {
                        descriptor = method.LocalDescriptors[slot.Index];
                        return true;
                    }
                    break;
                case SsaSlotKind.Temp:
                    for (int i = 0; i < method.TempDescriptors.Length; i++)
                    {
                        if (method.TempDescriptors[i].Index == slot.Index)
                        {
                            descriptor = method.TempDescriptors[i];
                            return true;
                        }
                    }
                    break;
            }

            descriptor = null!;
            return false;
        }
    }

    internal static class GenTreeSsaBuilder
    {
        public static SsaMethod BuildMethod(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            GenTreeLocalLiveness? hirLiveness = null,
            bool validate = true)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (cfg is null) throw new ArgumentNullException(nameof(cfg));
            if (method.Phase < GenTreeMethodPhase.HirLiveness)
                throw new InvalidOperationException("SSA construction requires morph, local rewriting, CFG construction, and HIR liveness to have already run.");

            if (!ReferenceEquals(cfg, method.Cfg))
                throw new InvalidOperationException("SSA construction was given a CFG that is not attached to the GenTree method.");

            hirLiveness ??= method.HirLiveness ?? GenTreeLocalLiveness.Build(method, cfg);
            if (!ReferenceEquals(hirLiveness.Cfg, cfg))
                throw new InvalidOperationException("SSA construction was given HIR liveness for a different CFG.");

            SsaSourceAnnotations.Clear(method);

            var slotTable = SlotTable.Build(method, cfg, hirLiveness);
            var liveness = Liveness.Build(cfg, slotTable.PromotableSlots);
            var memoryLiveness = MemoryLiveness.Build(cfg, slotTable);
            var phis = InsertPhis(cfg, slotTable.PromotableSlots, liveness);
            var memoryPhis = InsertMemoryPhis(cfg, memoryLiveness);
            var rename = new RenameState(cfg, slotTable, phis, memoryPhis, memoryLiveness);
            var blocks = rename.Run();
            var initialValues = rename.InitialValues.ToImmutableArray();
            var initialMemoryValues = rename.InitialMemoryValues.ToImmutableArray();
            var valueDefinitions = BuildValueDefinitions(slotTable, initialValues, blocks);
            var memoryDefinitions = BuildMemoryDefinitions(initialMemoryValues, blocks);
            AnnotateSsaUses(valueDefinitions, memoryDefinitions, blocks);
            var ssaLocalDescriptors = BuildSsaLocalDescriptors(slotTable, valueDefinitions);

            var result = new SsaMethod(
                method,
                cfg,
                slotTable.AllSlots.ToImmutableArray(),
                initialValues,
                valueDefinitions,
                blocks,
                valueNumbers: null,
                ssaLocalDescriptors: ssaLocalDescriptors,
                initialMemoryValues: initialMemoryValues,
                memoryDefinitions: memoryDefinitions);

            SsaSourceAnnotations.Attach(result);

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
                Add(new SsaValueDefinition(new SsaDescriptor(
                    name.Slot,
                    name.Version,
                    SsaDefinitionKind.Initial,
                    -1,
                    -1,
                    -1,
                    defNode: null,
                    info.Type,
                    info.StackKind)));
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    var info = slotTable.GetInfo(phi.Target.Slot);
                    Add(new SsaValueDefinition(new SsaDescriptor(
                        phi.Target.Slot,
                        phi.Target.Version,
                        SsaDefinitionKind.Phi,
                        block.Id,
                        -1,
                        -1,
                        defNode: null,
                        info.Type,
                        info.StackKind,
                        defBlock: block.CfgBlock,
                        phiDescriptor: phi)));
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    CollectStatementDefinitions(block.Statements[s], block, s);
            }

            definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            return definitions.ToImmutableArray();

            void CollectStatementDefinitions(SsaTree tree, SsaBlock block, int statementIndex)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStatementDefinitions(tree.Operands[i], block, statementIndex);

                int blockId = block.Id;

                if (tree.StoreTarget.HasValue)
                {
                    var name = tree.StoreTarget.Value;
                    var info = slotTable.GetInfo(name.Slot);
                    Add(new SsaValueDefinition(new SsaDescriptor(
                        name.Slot,
                        name.Version,
                        SsaDefinitionKind.Store,
                        blockId,
                        statementIndex,
                        tree.Source.Id,
                        tree.Source,
                        info.Type,
                        info.StackKind,
                        tree.LocalFieldBaseValue.HasValue ? tree.LocalFieldBaseValue.Value.Version : SsaConfig.ReservedSsaNumber,
                        block.CfgBlock)));
                }
            }

            void Add(SsaValueDefinition definition)
            {
                if (!seen.Add(definition.Name))
                    throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                definitions.Add(definition);
            }
        }

        internal static ImmutableArray<SsaMemoryDefinition> BuildMemoryDefinitions(
            ImmutableArray<SsaMemoryValueName> initialMemoryValues,
            ImmutableArray<SsaBlock> blocks)
        {
            var definitions = new List<SsaMemoryDefinition>();
            var seen = new HashSet<SsaMemoryValueName>();

            for (int i = 0; i < initialMemoryValues.Length; i++)
            {
                var name = initialMemoryValues[i];
                Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                    name,
                    SsaMemoryDefinitionKind.Initial,
                    -1,
                    -1,
                    -1,
                    defNode: null)));
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.MemoryPhis.Length; p++)
                {
                    var phi = block.MemoryPhis[p];
                    Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                        phi.Target,
                        SsaMemoryDefinitionKind.Phi,
                        block.Id,
                        -1,
                        -1,
                        defNode: null,
                        defBlock: block.CfgBlock,
                        phi: phi)));
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    CollectStatementMemoryDefinitions(block.Statements[s], block, s);

                for (int i = 0; i < block.MemoryOut.Length; i++)
                {
                    var name = block.MemoryOut[i];
                    if (seen.Contains(name))
                        continue;

                    Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                        name,
                        SsaMemoryDefinitionKind.BlockOut,
                        block.Id,
                        block.Statements.Length,
                        -1,
                        defNode: null,
                        defBlock: block.CfgBlock)));
                }
            }

            definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            return definitions.ToImmutableArray();

            void CollectStatementMemoryDefinitions(SsaTree tree, SsaBlock block, int statementIndex)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectStatementMemoryDefinitions(tree.Operands[i], block, statementIndex);

                for (int i = 0; i < tree.MemoryDefinitions.Length; i++)
                {
                    var name = tree.MemoryDefinitions[i];
                    Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                        name,
                        SsaMemoryDefinitionKind.Store,
                        block.Id,
                        statementIndex,
                        tree.Source.Id,
                        tree.Source,
                        block.CfgBlock)));
                }
            }

            void Add(SsaMemoryDefinition definition)
            {
                if (!seen.Add(definition.Name))
                    throw new InvalidOperationException($"Duplicate memory SSA definition {definition.Name}.");
                definitions.Add(definition);
            }
        }

        internal static void AnnotateSsaUses(
            ImmutableArray<SsaValueDefinition> valueDefinitions,
            ImmutableArray<SsaMemoryDefinition> memoryDefinitions,
            ImmutableArray<SsaBlock> blocks)
        {
            var descriptors = new Dictionary<SsaValueName, SsaDescriptor>();
            for (int i = 0; i < valueDefinitions.Length; i++)
                descriptors[valueDefinitions[i].Name] = valueDefinitions[i].Descriptor;

            var memoryDescriptors = new Dictionary<SsaMemoryValueName, SsaMemoryDescriptor>();
            for (int i = 0; i < memoryDefinitions.Length; i++)
                memoryDescriptors[memoryDefinitions[i].Name] = memoryDefinitions[i].Descriptor;

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

                for (int s = 0; s < block.Statements.Length; s++)
                    AnnotateTreeUses(block.Statements[s], block.Id, descriptors, memoryDescriptors);
            }

            static void AnnotateTreeUses(
                SsaTree tree,
                int blockId,
                Dictionary<SsaValueName, SsaDescriptor> descriptors,
                Dictionary<SsaMemoryValueName, SsaMemoryDescriptor> memoryDescriptors)
            {
                if (tree.Value.HasValue && descriptors.TryGetValue(tree.Value.Value, out var valueDescriptor))
                    valueDescriptor.AddUse(blockId);

                if (tree.LocalFieldBaseValue.HasValue && descriptors.TryGetValue(tree.LocalFieldBaseValue.Value, out var baseDescriptor))
                    baseDescriptor.AddUse(blockId);

                for (int i = 0; i < tree.MemoryUses.Length; i++)
                {
                    if (memoryDescriptors.TryGetValue(tree.MemoryUses[i], out var memoryDescriptor))
                        memoryDescriptor.AddUse(blockId);
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    AnnotateTreeUses(tree.Operands[i], blockId, descriptors, memoryDescriptors);
            }
        }

        private static ImmutableArray<SsaLocalDescriptor> BuildSsaLocalDescriptors(
            SlotTable slotTable,
            ImmutableArray<SsaValueDefinition> valueDefinitions)
        {
            var descriptorsBySlot = new Dictionary<SsaSlot, List<SsaDescriptor>>();
            for (int i = 0; i < valueDefinitions.Length; i++)
            {
                var descriptor = valueDefinitions[i].Descriptor;
                if (!descriptorsBySlot.TryGetValue(descriptor.BaseLocal, out var list))
                {
                    list = new List<SsaDescriptor>();
                    descriptorsBySlot.Add(descriptor.BaseLocal, list);
                }
                list.Add(descriptor);
            }

            var result = ImmutableArray.CreateBuilder<SsaLocalDescriptor>(slotTable.AllSlots.Length);
            for (int i = 0; i < slotTable.AllSlots.Length; i++)
            {
                var info = slotTable.AllSlots[i];
                descriptorsBySlot.TryGetValue(info.Slot, out var list);
                var perSsaData = DensePerSsaData(info.Slot, list);
                GenLocalDescriptor? localDescriptor = slotTable.GetLocalDescriptorOrNull(info.Slot);
                var local = new SsaLocalDescriptor(
                    info.Slot,
                    info.Type,
                    info.StackKind,
                    info.AddressExposed,
                    slotTable.IsPromotable(info.Slot),
                    localDescriptor,
                    perSsaData);
                result.Add(local);
                localDescriptor?.SetSsaDescriptors(perSsaData);
            }

            return result.ToImmutable();

            static ImmutableArray<SsaDescriptor> DensePerSsaData(SsaSlot slot, List<SsaDescriptor>? descriptors)
            {
                if (descriptors is null || descriptors.Count == 0)
                    return ImmutableArray<SsaDescriptor>.Empty;

                int max = -1;
                for (int i = 0; i < descriptors.Count; i++)
                    max = Math.Max(max, descriptors[i].SsaNumber);

                var table = new SsaDescriptor?[max + 1];
                for (int i = 0; i < descriptors.Count; i++)
                {
                    var descriptor = descriptors[i];
                    if (table[descriptor.SsaNumber] is not null)
                        throw new InvalidOperationException("Duplicate SSA descriptor " + descriptor.Name + ".");
                    table[descriptor.SsaNumber] = descriptor;
                }

                var builder = ImmutableArray.CreateBuilder<SsaDescriptor>(table.Length);
                for (int i = 0; i < table.Length; i++)
                {
                    if (i == SsaConfig.ReservedSsaNumber)
                    {
                        builder.Add(null!);
                        continue;
                    }

                    if (table[i] is null)
                        throw new InvalidOperationException("Missing SSA descriptor " + slot + "_" + i.ToString() + ".");
                    builder.Add(table[i]!);
                }
                return builder.ToImmutable();
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

        private static MutableMemoryPhi[][] InsertMemoryPhis(ControlFlowGraph cfg, MemoryLiveness liveness)
        {
            int n = cfg.Blocks.Length;
            var byBlock = new List<MutableMemoryPhi>[n];
            for (int i = 0; i < n; i++)
                byBlock[i] = new List<MutableMemoryPhi>();

            var hasPhi = new HashSet<(SsaMemoryKind kind, int blockId)>();

            foreach (var kind in SsaMemoryKinds.All)
            {
                var work = new Queue<int>();
                var inWork = new HashSet<int>();

                for (int b = 0; b < n; b++)
                {
                    if (liveness.Defs[b].Contains(kind))
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
                        if (!liveness.LiveIn[y].Contains(kind))
                            continue;

                        if (hasPhi.Add((kind, y)))
                        {
                            byBlock[y].Add(new MutableMemoryPhi(y, kind));
                            if (!liveness.Defs[y].Contains(kind) && inWork.Add(y))
                                work.Enqueue(y);
                        }
                    }
                }
            }

            var result = new MutableMemoryPhi[n][];
            for (int i = 0; i < n; i++)
            {
                byBlock[i].Sort((a, b) => a.Kind.CompareTo(b.Kind));
                result[i] = byBlock[i].ToArray();
            }
            return result;
        }

        private sealed class SlotTable
        {
            private const int MaxTrackedPromotableSlots = 512;

            private readonly Dictionary<SsaSlot, SsaSlotInfo> _infoBySlot;
            private readonly Dictionary<SsaSlot, GenLocalDescriptor> _localDescriptorBySlot;
            private readonly HashSet<SsaSlot> _promotable;

            public ImmutableArray<SsaSlotInfo> AllSlots { get; }
            public ImmutableArray<SsaSlot> PromotableSlots { get; }

            private SlotTable(
                ImmutableArray<SsaSlotInfo> allSlots,
                ImmutableArray<SsaSlot> promotableSlots,
                Dictionary<SsaSlot, GenLocalDescriptor> localDescriptorBySlot)
            {
                AllSlots = allSlots;
                PromotableSlots = promotableSlots;
                _infoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>();
                _localDescriptorBySlot = new Dictionary<SsaSlot, GenLocalDescriptor>(localDescriptorBySlot);
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

                return new SsaSlotInfo(slot, type: null, stackKind: GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
            }

            public GenLocalDescriptor? GetLocalDescriptorOrNull(SsaSlot slot)
            {
                _localDescriptorBySlot.TryGetValue(slot, out var descriptor);
                return descriptor;
            }

            public static SlotTable Build(GenTreeMethod method, ControlFlowGraph cfg, GenTreeLocalLiveness hirLiveness)
            {
                if (method is null) throw new ArgumentNullException(nameof(method));
                if (cfg is null) throw new ArgumentNullException(nameof(cfg));
                if (hirLiveness is null) throw new ArgumentNullException(nameof(hirLiveness));
                if (!ReferenceEquals(hirLiveness.Cfg, cfg))
                    throw new InvalidOperationException("SSA slot table requires HIR liveness for the same CFG.");

                var slots = new List<SsaSlotInfo>();
                var promotable = new List<SsaSlot>();
                var descriptorBySlot = new Dictionary<SsaSlot, GenLocalDescriptor>();

                for (int i = 0; i < method.ArgDescriptors.Length; i++)
                {
                    var descriptor = method.ArgDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                for (int i = 0; i < method.LocalDescriptors.Length; i++)
                {
                    var descriptor = method.LocalDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                for (int i = 0; i < method.TempDescriptors.Length; i++)
                {
                    var descriptor = method.TempDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                promotable.Sort();
                VerifyTrackedSplit(hirLiveness.SsaCandidateSlots, promotable);

                return new SlotTable(slots.ToImmutableArray(), promotable.ToImmutableArray(), descriptorBySlot);

                void AddSlot(SsaSlot slot, RuntimeType? type, GenStackKind stackKind, GenLocalDescriptor descriptor)
                {
                    slots.Add(new SsaSlotInfo(
                        slot,
                        type,
                        stackKind,
                        descriptor.AddressExposed,
                        descriptor.MemoryAliased,
                        descriptor.Category,
                        descriptor.LclNum,
                        descriptor.VarIndex,
                        descriptor.Tracked,
                        descriptor.SsaPromoted,
                        descriptor));
                    descriptorBySlot[slot] = descriptor;

                    if (!descriptor.SsaPromoted)
                        return;

                    if (!descriptor.CanBeSsaRenamedAsScalar)
                        throw new InvalidOperationException("LclVarDsc marked non-scalar or memory-aliased local as SSA-renamable: " + descriptor + ".");

                    if (!IsPromotableStorageSlot(type, stackKind))
                        throw new InvalidOperationException("tracked SSA local has non-promotable storage kind: " + slot + ".");

                    promotable.Add(slot);
                }

                static void VerifyTrackedSplit(ImmutableArray<SsaSlot> ssaCandidateSlots, List<SsaSlot> ssaSlots)
                {
                    if (ssaCandidateSlots.Length != ssaSlots.Count)
                        throw new InvalidOperationException("SSA local set does not match HIR tracked-local SSA candidate set.");

                    var sortedCandidates = new List<SsaSlot>(ssaCandidateSlots);
                    sortedCandidates.Sort();
                    for (int i = 0; i < sortedCandidates.Count; i++)
                    {
                        if (!sortedCandidates[i].Equals(ssaSlots[i]))
                            throw new InvalidOperationException("SSA local set does not match HIR tracked-local SSA candidate set.");
                    }
                }

                static bool IsPromotableStorageSlot(RuntimeType? type, GenStackKind stackKind)
                {
                    if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                        return false;

                    if (stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                        return true;

                    if (type is not null)
                    {
                        if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                            return true;

                        if (type.IsValueType && type.ContainsGcPointers)
                            return false;
                    }

                    if (type is null)
                        return stackKind is
                            GenStackKind.I4 or
                            GenStackKind.I8 or
                            GenStackKind.R4 or
                            GenStackKind.R8 or
                            GenStackKind.NativeInt or
                            GenStackKind.NativeUInt or
                            GenStackKind.Ptr;

                    return MachineAbi.IsPhysicallyPromotableStorage(type, stackKind);
                }
            }

            private static Dictionary<SsaSlot, int> ComputeWeightedSlotUses(GenTreeMethod method, ControlFlowGraph cfg)
            {
                var result = new Dictionary<SsaSlot, int>();
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    int weight = 1 + 8 * LoopDepth(cfg, b);
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CountSlotUses(statements[s], result, weight);
                }
                return result;
            }

            private static int LoopDepth(ControlFlowGraph cfg, int blockId)
            {
                int depth = 0;
                for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                {
                    if (cfg.NaturalLoops[i].Contains(blockId))
                        depth++;
                }
                return depth;
            }

            private static void CountSlotUses(GenTree node, Dictionary<SsaSlot, int> counts, int weight)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    if (fieldAccess.Kind != SsaLocalAccessKind.FullDefinition)
                        Add(fieldAccess.Slot);

                    if (fieldAccess.IsDefinition)
                        Add(fieldAccess.Slot);

                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CountSlotUses(node.Operands[i], counts, weight);
                    }
                    return;
                }

                if (TryGetDirectLoadSlot(node, out var loadSlot) || TryGetDirectStoreSlot(node, out loadSlot))
                    Add(loadSlot);

                for (int i = 0; i < node.Operands.Length; i++)
                    CountSlotUses(node.Operands[i], counts, weight);

                void Add(SsaSlot slot)
                {
                    counts.TryGetValue(slot, out int current);
                    counts[slot] = current + Math.Max(1, weight);
                }
            }

            private static bool NeedsDescriptorHomeGcReporting(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null)
                {
                    if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                        return true;
                    if (type.IsValueType && type.ContainsGcPointers)
                        return true;
                }

                return stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;
            }

            private static void ApplyTrackedSlotBudget(
                List<SsaSlot> promotable,
                Dictionary<SsaSlot, GenLocalDescriptor> descriptorBySlot,
                Dictionary<SsaSlot, int> weightedUses)
            {
                if (promotable.Count <= MaxTrackedPromotableSlots)
                    return;

                promotable.Sort((a, b) =>
                {
                    weightedUses.TryGetValue(a, out int aw);
                    weightedUses.TryGetValue(b, out int bw);
                    int c = bw.CompareTo(aw);
                    return c != 0 ? c : a.CompareTo(b);
                });

                var kept = new HashSet<SsaSlot>();
                for (int i = 0; i < MaxTrackedPromotableSlots; i++)
                    kept.Add(promotable[i]);

                for (int i = promotable.Count - 1; i >= 0; i--)
                {
                    var slot = promotable[i];
                    if (kept.Contains(slot))
                        continue;

                    promotable.RemoveAt(i);
                    if (descriptorBySlot.TryGetValue(slot, out var descriptor))
                    {
                        descriptor.MarkUntracked();
                    }
                }
            }

            private static void ResetSsaPromotionState(GenTreeMethod method)
            {
                Reset(method.ArgDescriptors);
                Reset(method.LocalDescriptors);
                Reset(method.TempDescriptors);

                static void Reset(ImmutableArray<GenLocalDescriptor> descriptors)
                {
                    for (int i = 0; i < descriptors.Length; i++)
                        descriptors[i].ResetTrackingAndLivenessState();
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
                    if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                    {
                        for (int i = 0; i < node.Operands.Length; i++)
                        {
                            if (i == fieldAccess.ReceiverOperandIndex)
                                continue;
                            CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);
                        }

                        if (fieldAccess.Kind != SsaLocalAccessKind.FullDefinition && !defs.Contains(fieldAccess.Slot))
                            uses.Add(fieldAccess.Slot);

                        if (fieldAccess.IsDefinition)
                            defs.Add(fieldAccess.Slot);

                        touched.Add(fieldAccess.Slot);
                        return;
                    }

                    if (TryGetDirectStoreSlot(node, out var storeSlot))
                    {
                        for (int i = 0; i < node.Operands.Length; i++)
                            CollectUseDefAndTouch(node.Operands[i], uses, defs, touched);

                        defs.Add(storeSlot);
                        touched.Add(storeSlot);
                        return;
                    }

                    if (TryGetDirectLoadSlot(node, out var loadSlot))
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
                CollectAddressExposed(node, parent: null, operandIndex: -1, addressExposed);
            }

            private static void CollectAddressExposed(GenTree node, GenTree? parent, int operandIndex, HashSet<SsaSlot> addressExposed)
            {
                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot) &&
                    (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
                    addressExposed.Add(slot);

                for (int i = 0; i < node.Operands.Length; i++)
                    CollectAddressExposed(node.Operands[i], node, i, addressExposed);
            }
        }

        private sealed class Liveness
        {
            public TrackedLocalTable Table { get; }
            public TrackedLocalSet[] Uses { get; }
            public TrackedLocalSet[] Defs { get; }
            public TrackedLocalSet[] LiveIn { get; }
            public TrackedLocalSet[] LiveOut { get; }

            private Liveness(TrackedLocalTable table, TrackedLocalSet[] uses, TrackedLocalSet[] defs, TrackedLocalSet[] liveIn, TrackedLocalSet[] liveOut)
            {
                Table = table;
                Uses = uses;
                Defs = defs;
                LiveIn = liveIn;
                LiveOut = liveOut;
            }

            public static Liveness Build(ControlFlowGraph cfg, ImmutableArray<SsaSlot> promotableSlots)
            {
                int n = cfg.Blocks.Length;
                var table = new TrackedLocalTable(promotableSlots);
                var uses = NewSetArray(n, table);
                var defs = NewSetArray(n, table);
                var liveIn = NewSetArray(n, table);
                var liveOut = NewSetArray(n, table);

                for (int b = 0; b < n; b++)
                {
                    var statements = cfg.Blocks[b].SourceBlock.Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CollectUseDef(statements[s], table, uses[b], defs[b]);
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                    {
                        int b = cfg.ReversePostOrder[r];

                        var newOut = table.NewEmptySet();
                        var successors = cfg.Blocks[b].Successors;
                        for (int i = 0; i < successors.Length; i++)
                            newOut.UnionWith(liveIn[successors[i].ToBlockId]);

                        var newIn = newOut.Clone();
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

                return new Liveness(table, uses, defs, liveIn, liveOut);
            }

            private static TrackedLocalSet[] NewSetArray(int count, TrackedLocalTable table)
            {
                var result = new TrackedLocalSet[count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = table.NewEmptySet();
                return result;
            }

            private static void CollectUseDef(GenTree node, TrackedLocalTable promotable, TrackedLocalSet uses, TrackedLocalSet defs)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CollectUseDef(node.Operands[i], promotable, uses, defs);
                    }

                    if (fieldAccess.Kind != SsaLocalAccessKind.FullDefinition && promotable.Contains(fieldAccess.Slot) && !defs.Contains(fieldAccess.Slot))
                        uses.Add(fieldAccess.Slot);
                    if (fieldAccess.IsDefinition && promotable.Contains(fieldAccess.Slot))
                        defs.Add(fieldAccess.Slot);
                    return;
                }

                if (TryGetDirectStoreSlot(node, out var storeSlot))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                        CollectUseDef(node.Operands[i], promotable, uses, defs);

                    if (promotable.Contains(storeSlot))
                        defs.Add(storeSlot);
                    return;
                }

                if (TryGetDirectLoadSlot(node, out var loadSlot))
                {
                    if (promotable.Contains(loadSlot) && !defs.Contains(loadSlot))
                        uses.Add(loadSlot);
                    return;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    CollectUseDef(node.Operands[i], promotable, uses, defs);
            }
        }

        private sealed class MemoryLiveness
        {
            public SsaMemoryKindSet[] Uses { get; }
            public SsaMemoryKindSet[] Defs { get; }
            public SsaMemoryKindSet[] LiveIn { get; }
            public SsaMemoryKindSet[] LiveOut { get; }

            private MemoryLiveness(SsaMemoryKindSet[] uses, SsaMemoryKindSet[] defs, SsaMemoryKindSet[] liveIn, SsaMemoryKindSet[] liveOut)
            {
                Uses = uses;
                Defs = defs;
                LiveIn = liveIn;
                LiveOut = liveOut;
            }

            public static MemoryLiveness Build(ControlFlowGraph cfg, SlotTable slots)
            {
                int n = cfg.Blocks.Length;
                var uses = new SsaMemoryKindSet[n];
                var defs = new SsaMemoryKindSet[n];
                var liveIn = new SsaMemoryKindSet[n];
                var liveOut = new SsaMemoryKindSet[n];

                for (int b = 0; b < n; b++)
                {
                    var statements = cfg.Blocks[b].SourceBlock.Statements;
                    for (int s = 0; s < statements.Length; s++)
                        CollectMemoryUseDef(statements[s], slots, ref uses[b], ref defs[b]);
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                    {
                        int b = cfg.ReversePostOrder[r];

                        SsaMemoryKindSet newOut = SsaMemoryKindSet.None;
                        var successors = cfg.Blocks[b].Successors;
                        for (int i = 0; i < successors.Length; i++)
                            newOut |= liveIn[successors[i].ToBlockId];

                        SsaMemoryKindSet newIn = uses[b] | (newOut & ~defs[b]);

                        if (liveOut[b] != newOut)
                        {
                            liveOut[b] = newOut;
                            changed = true;
                        }

                        if (liveIn[b] != newIn)
                        {
                            liveIn[b] = newIn;
                            changed = true;
                        }
                    }
                }
                while (changed);

                return new MemoryLiveness(uses, defs, liveIn, liveOut);
            }
        }

        private readonly struct MemoryEffects
        {
            public readonly SsaMemoryKindSet Uses;
            public readonly SsaMemoryKindSet Definitions;

            public MemoryEffects(SsaMemoryKindSet uses, SsaMemoryKindSet definitions)
            {
                Uses = uses;
                Definitions = definitions;
            }
        }

        private static void CollectMemoryUseDef(GenTree node, SlotTable slots, ref SsaMemoryKindSet uses, ref SsaMemoryKindSet defs)
        {
            for (int i = 0; i < node.Operands.Length; i++)
                CollectMemoryUseDef(node.Operands[i], slots, ref uses, ref defs);

            var effects = GetMemoryEffects(node, slots);
            SsaMemoryKindSet nodeUses = effects.Uses | effects.Definitions;
            uses |= nodeUses & ~defs;
            defs |= effects.Definitions;
        }

        private static MemoryEffects GetMemoryEffects(GenTree node, SlotTable slots)
        {
            SsaMemoryKindSet uses = SsaMemoryKindSet.None;
            SsaMemoryKindSet defs = SsaMemoryKindSet.None;

            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localFieldAccess))
            {
                var fieldSlotInfo = slots.GetInfo(localFieldAccess.Slot);
                var baseSlotInfo = slots.GetInfo(localFieldAccess.BaseSlot);
                bool fieldIsByrefExposed = fieldSlotInfo.AddressExposed || fieldSlotInfo.MemoryAliased || baseSlotInfo.AddressExposed || baseSlotInfo.MemoryAliased;

                if (localFieldAccess.Kind == SsaLocalAccessKind.Use && !_slotsArePromotable(slots, localFieldAccess))
                {
                    if (fieldIsByrefExposed)
                        uses = uses.Add(SsaMemoryKind.ByrefExposed);
                }
                else if (localFieldAccess.IsDefinition && !_slotsArePromotable(slots, localFieldAccess))
                {
                    if (fieldIsByrefExposed)
                        defs = defs.Add(SsaMemoryKind.ByrefExposed);
                }
            }

            switch (node.Kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
                    {
                        var info = slots.GetInfo(loadSlot);
                        if ((info.AddressExposed || info.MemoryAliased) && !slots.IsPromotable(loadSlot))
                            uses = uses.Add(SsaMemoryKind.ByrefExposed);
                    }
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
                    {
                        var info = slots.GetInfo(storeSlot);
                        if (info.AddressExposed || info.MemoryAliased)
                            defs = defs.Add(SsaMemoryKind.ByrefExposed);
                    }
                    break;

                case GenTreeKind.Field:
                    if (!SsaSlotHelpers.TryGetLocalFieldAccess(node, out _))
                        uses = uses.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StaticField:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayDataRef:
                    uses = uses.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.LoadIndirect:
                    uses |= IndirectMemoryKinds(node);
                    break;

                case GenTreeKind.StoreField:
                    if (!SsaSlotHelpers.TryGetLocalFieldAccess(node, out _))
                        defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StoreStaticField:
                case GenTreeKind.StoreArrayElement:
                    defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StoreIndirect:
                    defs |= IndirectMemoryKinds(node);
                    break;

                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                    uses = SsaMemoryKindSet.All;
                    defs = SsaMemoryKindSet.All;
                    break;

                case GenTreeKind.NewObject:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;
            }

            if (node.WritesMemory && defs == SsaMemoryKindSet.None)
                defs = defs.Add(SsaMemoryKind.GcHeap);

            if (node.ReadsMemory && uses == SsaMemoryKindSet.None)
                uses = uses.Add(SsaMemoryKind.GcHeap);

            return new MemoryEffects(uses, defs);

            static bool _slotsArePromotable(SlotTable slots, SsaLocalAccess access)
                => access.IsPromotedFieldAccess
                    ? slots.IsPromotable(access.Slot)
                    : slots.IsPromotable(access.BaseSlot);

            static SsaMemoryKindSet IndirectMemoryKinds(GenTree node)
            {
                if (node.Operands.Length != 0 && IsLocalOrByrefAddress(node.Operands[0]))
                    return SsaMemoryKindSet.ByrefExposed;

                return SsaMemoryKindSet.All;
            }

            static bool IsLocalOrByrefAddress(GenTree node)
            {
                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _))
                    return true;

                if (node.Kind == GenTreeKind.PointerToByRef)
                    return true;

                if (node.Kind == GenTreeKind.FieldAddr && node.Operands.Length != 0)
                    return IsLocalOrByrefAddress(node.Operands[0]);

                if (node.Kind == GenTreeKind.ArrayElementAddr || node.Kind == GenTreeKind.ArrayDataRef)
                    return false;

                return false;
            }
        }

        private sealed class RenameState
        {
            private readonly ControlFlowGraph _cfg;
            private readonly SlotTable _slots;
            private readonly MutablePhi[][] _phis;
            private readonly MutableMemoryPhi[][] _memoryPhis;
            private readonly MemoryLiveness _memoryLiveness;
            private readonly Dictionary<SsaSlot, int> _nextVersion = new();
            private readonly Dictionary<SsaSlot, Stack<SsaValueName>> _stacks = new();
            private readonly Dictionary<SsaMemoryKind, int> _nextMemoryVersion = new();
            private readonly Dictionary<SsaMemoryKind, Stack<SsaMemoryValueName>> _memoryStacks = new();
            private readonly List<SsaValueName> _initialValues = new();
            private readonly List<SsaMemoryValueName> _initialMemoryValues = new();
            private readonly List<SsaTree>[] _renamedStatements;
            private readonly ImmutableArray<SsaMemoryValueName>[] _memoryIn;
            private readonly ImmutableArray<SsaMemoryValueName>[] _memoryOut;
            private readonly bool[] _visited;

            public IReadOnlyList<SsaValueName> InitialValues => _initialValues;
            public IReadOnlyList<SsaMemoryValueName> InitialMemoryValues => _initialMemoryValues;

            public RenameState(ControlFlowGraph cfg, SlotTable slots, MutablePhi[][] phis, MutableMemoryPhi[][] memoryPhis, MemoryLiveness memoryLiveness)
            {
                _cfg = cfg;
                _slots = slots;
                _phis = phis;
                _memoryPhis = memoryPhis;
                _memoryLiveness = memoryLiveness;
                _renamedStatements = new List<SsaTree>[cfg.Blocks.Length];
                _memoryIn = new ImmutableArray<SsaMemoryValueName>[cfg.Blocks.Length];
                _memoryOut = new ImmutableArray<SsaMemoryValueName>[cfg.Blocks.Length];
                _visited = new bool[cfg.Blocks.Length];
                for (int i = 0; i < _renamedStatements.Length; i++)
                    _renamedStatements[i] = new List<SsaTree>();

                for (int i = 0; i < slots.PromotableSlots.Length; i++)
                {
                    var slot = slots.PromotableSlots[i];
                    var initial = new SsaValueName(slot, SsaConfig.FirstSsaNumber);
                    _nextVersion[slot] = SsaConfig.FirstSsaNumber;
                    _stacks[slot] = new Stack<SsaValueName>();
                    _stacks[slot].Push(initial);
                    _initialValues.Add(initial);
                }

                foreach (var kind in SsaMemoryKinds.All)
                {
                    var initial = new SsaMemoryValueName(kind, SsaConfig.FirstSsaNumber);
                    _nextMemoryVersion[kind] = SsaConfig.FirstSsaNumber;
                    _memoryStacks[kind] = new Stack<SsaMemoryValueName>();
                    _memoryStacks[kind].Push(initial);
                    _initialMemoryValues.Add(initial);
                }

                _initialValues.Sort();
                _initialMemoryValues.Sort();
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
                _initialMemoryValues.Sort();

                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(_cfg.Blocks.Length);
                for (int b = 0; b < _cfg.Blocks.Length; b++)
                {
                    var phiBuilder = ImmutableArray.CreateBuilder<SsaPhi>(_phis[b].Length);
                    for (int i = 0; i < _phis[b].Length; i++)
                        phiBuilder.Add(_phis[b][i].Freeze(_cfg.Blocks[b]));

                    var memoryPhiBuilder = ImmutableArray.CreateBuilder<SsaMemoryPhi>(_memoryPhis[b].Length);
                    for (int i = 0; i < _memoryPhis[b].Length; i++)
                        memoryPhiBuilder.Add(_memoryPhis[b][i].Freeze(_cfg.Blocks[b]));

                    blocks.Add(new SsaBlock(
                        _cfg.Blocks[b],
                        phiBuilder.ToImmutable(),
                        _renamedStatements[b].ToImmutableArray(),
                        memoryPhiBuilder.ToImmutable(),
                        _memoryIn[b],
                        _memoryOut[b]));
                }
                return blocks.ToImmutable();
            }

            private void RenameBlock(int blockId)
            {
                if (_visited[blockId])
                    return;
                _visited[blockId] = true;

                var pushed = new List<SsaSlot>();
                var pushedMemory = new List<SsaMemoryKind>();

                RenameIncomingMemoryStates(blockId, pushedMemory);

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
                    var renamed = RenameTree(statements[i], pushed, pushedMemory);
                    _renamedStatements[blockId].Add(renamed);
                }

                RenameOutgoingMemoryStates(blockId, pushedMemory);

                var successors = _cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    int succ = successors[i].ToBlockId;
                    for (int p = 0; p < _phis[succ].Length; p++)
                    {
                        var phi = _phis[succ][p];
                        phi.SetInput(blockId, Top(phi.Slot));
                    }

                    for (int p = 0; p < _memoryPhis[succ].Length; p++)
                    {
                        var phi = _memoryPhis[succ][p];
                        phi.SetInput(blockId, TopMemory(phi.Kind));
                    }
                }

                var children = _cfg.DominatorTreeChildren[blockId];
                for (int i = 0; i < children.Length; i++)
                    RenameBlock(children[i]);

                for (int i = pushed.Count - 1; i >= 0; i--)
                    Pop(pushed[i]);

                for (int i = pushedMemory.Count - 1; i >= 0; i--)
                    PopMemory(pushedMemory[i]);
            }

            private void RenameIncomingMemoryStates(int blockId, List<SsaMemoryKind> pushedMemory)
            {
                var builder = ImmutableArray.CreateBuilder<SsaMemoryValueName>(SsaMemoryKinds.All.Length);
                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (TryGetMemoryPhi(blockId, kind, out var phi))
                    {
                        phi.Target = NewMemoryVersion(kind);
                        PushMemory(kind, phi.Target);
                        pushedMemory.Add(kind);
                        builder.Add(phi.Target);
                    }
                    else
                    {
                        builder.Add(TopMemory(kind));
                    }
                }

                _memoryIn[blockId] = builder.ToImmutable();
            }

            private void RenameOutgoingMemoryStates(int blockId, List<SsaMemoryKind> pushedMemory)
            {
                var builder = ImmutableArray.CreateBuilder<SsaMemoryValueName>(SsaMemoryKinds.All.Length);
                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (_memoryLiveness.Defs[blockId].Contains(kind))
                    {
                        var outValue = NewMemoryVersion(kind);
                        PushMemory(kind, outValue);
                        pushedMemory.Add(kind);
                        builder.Add(outValue);
                    }
                    else
                    {
                        builder.Add(TopMemory(kind));
                    }
                }

                _memoryOut[blockId] = builder.ToImmutable();
            }

            private bool TryGetMemoryPhi(int blockId, SsaMemoryKind kind, out MutableMemoryPhi phi)
            {
                for (int i = 0; i < _memoryPhis[blockId].Length; i++)
                {
                    if (_memoryPhis[blockId][i].Kind == kind)
                    {
                        phi = _memoryPhis[blockId][i];
                        return true;
                    }
                }

                phi = null!;
                return false;
            }

            private SsaTree RenameTree(GenTree node, List<SsaSlot> pushed, List<SsaMemoryKind> pushedMemory)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                    {
                        if (fieldAccess.IsPromotedFieldAccess && _slots.IsPromotable(fieldAccess.Slot))
                        {
                            var operands = RenameLocalFieldNonReceiverOperands(node, fieldAccess, pushed, pushedMemory);
                            return AttachMemory(node, operands, pushedMemory, value: Top(fieldAccess.Slot));
                        }

                        if (!fieldAccess.IsPromotedFieldAccess && fieldAccess.Field is not null && _slots.IsPromotable(fieldAccess.BaseSlot))
                        {
                            var operands = RenameLocalFieldNonReceiverOperands(node, fieldAccess, pushed, pushedMemory);
                            return AttachMemory(node, operands, pushedMemory, localFieldBaseValue: Top(fieldAccess.BaseSlot), localField: fieldAccess.Field);
                        }
                    }
                    else if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                    {
                        if (_slots.IsPromotable(fieldAccess.Slot))
                        {
                            var operands = RenameLocalFieldNonReceiverOperands(node, fieldAccess, pushed, pushedMemory);
                            var target = NewVersion(fieldAccess.Slot);
                            Push(fieldAccess.Slot, target);
                            pushed.Add(fieldAccess.Slot);
                            return AttachMemory(node, operands, pushedMemory, storeTarget: target);
                        }
                    }
                    else if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                    {
                        if (_slots.IsPromotable(fieldAccess.BaseSlot))
                        {
                            var operands = RenameLocalFieldNonReceiverOperands(node, fieldAccess, pushed, pushedMemory);
                            var previous = Top(fieldAccess.BaseSlot);
                            var target = NewVersion(fieldAccess.BaseSlot);
                            Push(fieldAccess.BaseSlot, target);
                            pushed.Add(fieldAccess.BaseSlot);
                            return AttachMemory(node, operands, pushedMemory, storeTarget: target, localFieldBaseValue: previous, localField: fieldAccess.Field);
                        }
                    }
                }

                if (TryGetDirectLoadSlot(node, out var loadSlot) && _slots.IsPromotable(loadSlot))
                    return AttachMemory(node, ImmutableArray<SsaTree>.Empty, pushedMemory, value: Top(loadSlot));

                {
                    var operands = ImmutableArray.CreateBuilder<SsaTree>(node.Operands.Length);
                    for (int i = 0; i < node.Operands.Length; i++)
                        operands.Add(RenameTree(node.Operands[i], pushed, pushedMemory));

                    if (TryGetDirectStoreSlot(node, out var storeSlot) && _slots.IsPromotable(storeSlot))
                    {
                        var target = NewVersion(storeSlot);
                        Push(storeSlot, target);
                        pushed.Add(storeSlot);
                        return AttachMemory(node, operands.ToImmutable(), pushedMemory, storeTarget: target);
                    }

                    return AttachMemory(node, operands.ToImmutable(), pushedMemory);
                }
            }

            private SsaTree AttachMemory(
                GenTree node,
                ImmutableArray<SsaTree> operands,
                List<SsaMemoryKind> pushedMemory,
                SsaValueName? value = null,
                SsaValueName? storeTarget = null,
                SsaValueName? localFieldBaseValue = null,
                RuntimeField? localField = null)
            {
                var effects = GetMemoryEffects(node, _slots);
                var memoryUses = ImmutableArray.CreateBuilder<SsaMemoryValueName>();
                var memoryDefs = ImmutableArray.CreateBuilder<SsaMemoryValueName>();

                SsaMemoryKindSet useSet = effects.Uses | effects.Definitions;
                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (useSet.Contains(kind))
                        memoryUses.Add(TopMemory(kind));
                }

                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (!effects.Definitions.Contains(kind))
                        continue;

                    var def = NewMemoryVersion(kind);
                    PushMemory(kind, def);
                    pushedMemory.Add(kind);
                    memoryDefs.Add(def);
                }

                return new SsaTree(
                    node,
                    operands,
                    value,
                    storeTarget,
                    localFieldBaseValue,
                    localField,
                    memoryUses.ToImmutable(),
                    memoryDefs.ToImmutable());
            }

            private ImmutableArray<SsaTree> RenameLocalFieldNonReceiverOperands(GenTree node, SsaLocalAccess fieldAccess, List<SsaSlot> pushed, List<SsaMemoryKind> pushedMemory)
            {
                var operands = ImmutableArray.CreateBuilder<SsaTree>(Math.Max(0, node.Operands.Length - 1));
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i == fieldAccess.ReceiverOperandIndex)
                        continue;
                    operands.Add(RenameTree(node.Operands[i], pushed, pushedMemory));
                }
                return operands.ToImmutable();
            }

            private SsaValueName Top(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                {
                    var initial = new SsaValueName(slot, SsaConfig.FirstSsaNumber);
                    _nextVersion[slot] = Math.Max(_nextVersion.TryGetValue(slot, out int n) ? n : SsaConfig.FirstSsaNumber, SsaConfig.FirstSsaNumber);
                    stack = new Stack<SsaValueName>();
                    stack.Push(initial);
                    _stacks[slot] = stack;
                    if (!_initialValues.Contains(initial))
                        _initialValues.Add(initial);
                    return initial;
                }

                return stack.Peek();
            }

            private SsaMemoryValueName TopMemory(SsaMemoryKind kind)
            {
                if (!_memoryStacks.TryGetValue(kind, out var stack) || stack.Count == 0)
                {
                    var initial = new SsaMemoryValueName(kind, SsaConfig.FirstSsaNumber);
                    _nextMemoryVersion[kind] = Math.Max(_nextMemoryVersion.TryGetValue(kind, out int n) ? n : SsaConfig.FirstSsaNumber, SsaConfig.FirstSsaNumber);
                    stack = new Stack<SsaMemoryValueName>();
                    stack.Push(initial);
                    _memoryStacks[kind] = stack;
                    if (!_initialMemoryValues.Contains(initial))
                        _initialMemoryValues.Add(initial);
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

            private SsaMemoryValueName NewMemoryVersion(SsaMemoryKind kind)
            {
                int next = _nextMemoryVersion.TryGetValue(kind, out int current) ? current + 1 : 1;
                _nextMemoryVersion[kind] = next;
                return new SsaMemoryValueName(kind, next);
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

            private void PushMemory(SsaMemoryKind kind, SsaMemoryValueName value)
            {
                if (value.Kind != kind)
                    throw new InvalidOperationException("Memory SSA push kind mismatch: " + value + " for " + kind.ToString() + ".");

                if (!_memoryStacks.TryGetValue(kind, out var stack))
                {
                    stack = new Stack<SsaMemoryValueName>();
                    _memoryStacks[kind] = stack;
                }
                stack.Push(value);
            }

            private void Pop(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                    throw new InvalidOperationException($"SSA rename stack underflow for {slot}.");
                stack.Pop();
            }

            private void PopMemory(SsaMemoryKind kind)
            {
                if (!_memoryStacks.TryGetValue(kind, out var stack) || stack.Count == 0)
                    throw new InvalidOperationException($"Memory SSA rename stack underflow for {kind}.");
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

        private sealed class MutableMemoryPhi
        {
            private readonly Dictionary<int, SsaMemoryValueName> _inputs = new();

            public int BlockId { get; }
            public SsaMemoryKind Kind { get; }
            public SsaMemoryValueName Target { get; set; }

            public MutableMemoryPhi(int blockId, SsaMemoryKind kind)
            {
                BlockId = blockId;
                Kind = kind;
            }

            public void SetInput(int predecessorBlockId, SsaMemoryValueName value)
            {
                if (value.Kind != Kind)
                    throw new InvalidOperationException($"Memory phi input kind mismatch for {Kind}: {value}.");
                _inputs[predecessorBlockId] = value;
            }

            public SsaMemoryPhi Freeze(CfgBlock block)
            {
                var inputs = ImmutableArray.CreateBuilder<SsaMemoryPhiInput>(block.Predecessors.Length);
                var seen = new HashSet<int>();

                for (int i = 0; i < block.Predecessors.Length; i++)
                {
                    int pred = block.Predecessors[i].FromBlockId;
                    if (!seen.Add(pred))
                        continue;

                    if (!_inputs.TryGetValue(pred, out var value))
                        throw new InvalidOperationException($"Missing memory SSA phi input for {Kind} in B{BlockId} from B{pred}.");

                    inputs.Add(new SsaMemoryPhiInput(pred, value));
                }

                return new SsaMemoryPhi(BlockId, Kind, Target, inputs.ToImmutable());
            }
        }

        private static bool TryGetLoadSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetLoadSlot(node, out slot);

        private static bool TryGetStoreSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetStoreSlot(node, out slot);

        private static bool TryGetDirectLoadSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetDirectLoadSlot(node, out slot);

        private static bool TryGetDirectStoreSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetDirectStoreSlot(node, out slot);

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
                        return GenStackKind.R4;
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
        public bool PropagateConstants { get; set; } = true;
        public bool FoldConstants { get; set; } = true;
        public bool SimplifyAlgebraicIdentities { get; set; } = true;
        public bool RemoveDeadDefinitions { get; set; } = true;
        public int MaxIterations { get; set; } = 8;
    }

    internal static class SsaOptimizer
    {
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
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _slotInfos = new();
            private int _nextSyntheticTreeId;

            public MethodOptimizer(SsaMethod method, SsaOptimizationOptions options)
            {
                _original = method;
                _options = options;
                for (int i = 0; i < method.Slots.Length; i++)
                    _slotInfos[method.Slots[i].Slot] = method.Slots[i];
                _nextSyntheticTreeId = MaxTreeId(method) + 1;
            }

            public SsaMethod Run()
            {
                var current = EnsureValueNumbers(_original);

                for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
                {
                    current = EnsureValueNumbers(current);

                    var facts = ComputeFacts(current);
                    var rewrite = Rewrite(current, facts);
                    var afterRewrite = WithBlocks(current, rewrite.Blocks);

                    OptimizationResult dce = _options.RemoveDeadDefinitions
                        ? EliminateDeadDefinitions(afterRewrite)
                        : new OptimizationResult(afterRewrite.Blocks, changed: false);

                    var next = WithBlocks(afterRewrite, dce.Blocks);
                    bool changed = rewrite.Changed || dce.Changed;

                    if (!changed)
                    {
                        current = EnsureValueNumbers(next);
                        break;
                    }

                    current = EnsureValueNumbers(next);
                }

                var definitions = BuildValueDefinitions(current.Slots, current.InitialValues, current.Blocks);
                var memoryDefinitions = GenTreeSsaBuilder.BuildMemoryDefinitions(current.InitialMemoryValues, current.Blocks);
                GenTreeSsaBuilder.AnnotateSsaUses(definitions, memoryDefinitions, current.Blocks);
                var localDescriptors = BuildSsaLocalDescriptors(current.Slots, definitions, current.SsaLocalDescriptors);
                var finalWithoutVn = new SsaMethod(
                    current.GenTreeMethod,
                    current.Cfg,
                    current.Slots,
                    current.InitialValues,
                    definitions,
                    current.Blocks,
                    valueNumbers: null,
                    ssaLocalDescriptors: localDescriptors,
                    initialMemoryValues: current.InitialMemoryValues,
                    memoryDefinitions: memoryDefinitions);
                return SsaValueNumbering.BuildMethod(finalWithoutVn);
            }

            private Dictionary<SsaValueName, ValueFact> ComputeFacts(SsaMethod method)
            {
                method = EnsureValueNumbers(method);

                var facts = new Dictionary<SsaValueName, ValueFact>();
                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                    facts[method.ValueDefinitions[i].Name] = ValueFact.Unknown;

                if (method.ValueNumbers is null)
                    return facts;

                if (_options.PropagateConstants)
                {
                    for (int i = 0; i < method.ValueDefinitions.Length; i++)
                    {
                        var name = method.ValueDefinitions[i].Name;
                        if (TryGetSsaConstant(method, name, out var constant))
                            SetFact(facts, name, ValueFact.ForConstant(constant));
                    }
                }

                return facts;
            }

            private void CollectVnFactsForTree(
                SsaMethod method,
                SsaTree tree,
                Dictionary<SsaValueName, ValueFact> facts,
                Dictionary<ValueNumber, SsaValueName> available)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectVnFactsForTree(method, tree.Operands[i], facts, available);

                if (!tree.StoreTarget.HasValue)
                    return;

                var target = tree.StoreTarget.Value;
                ValueFact value = ValueFact.Unknown;

                if (tree.Operands.Length == 1)
                    value = EvaluateTree(method, tree.Operands[0], facts);

                if (_options.PropagateConstants && TryGetSsaConstant(method, target, out var constant))
                    value = ValueFact.ForConstant(constant);

                if (value.Kind == ValueFactKind.Alias && value.Alias.Equals(target))
                    value = ValueFact.Unknown;

                if (value.Kind == ValueFactKind.Alias)
                    value = ValueFact.Unknown;

                if (value.Kind != ValueFactKind.Unknown)
                    SetFact(facts, target, value);

                PublishDefinition(method, target, facts, available);
            }

            private void PublishDefinition(
                SsaMethod method,
                SsaValueName target,
                Dictionary<SsaValueName, ValueFact> facts,
                Dictionary<ValueNumber, SsaValueName> available)
            {
                if (!TryGetSsaValueNumber(method, target, out var vn) || !vn.IsValid)
                    return;

                if (_options.PropagateConstants && TryGetConstantFromValueNumber(method, vn, out var constant))
                {
                    SetFact(facts, target, ValueFact.ForConstant(constant));
                    return;
                }


                if (CanPublishAsValueNumber(target) && !available.ContainsKey(vn))
                    available.Add(vn, target);
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

            private ValueFact EvaluateTree(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (tree.Value.HasValue)
                    return NormalizeValue(tree.Value.Value, facts);

                if (TryGetSourceConstant(tree.Source, out var sourceConstant))
                    return ValueFact.ForConstant(sourceConstant);

                if (_options.FoldConstants && TryGetTreeConstant(method, tree, out var vnConstant))
                    return ValueFact.ForConstant(vnConstant);

                if (!_options.FoldConstants)
                    return ValueFact.Unknown;

                if (tree.Kind == GenTreeKind.Unary && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(method, tree.Operands[0], facts);
                    if (operand.Kind == ValueFactKind.Constant && TryFoldUnary(tree.Source, operand.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Binary && tree.Operands.Length == 2)
                {
                    var left = EvaluateTree(method, tree.Operands[0], facts);
                    var right = EvaluateTree(method, tree.Operands[1], facts);
                    if (left.Kind == ValueFactKind.Constant && right.Kind == ValueFactKind.Constant && TryFoldBinary(tree.Source, left.Constant, right.Constant, out var folded))
                        return ValueFact.ForConstant(folded);
                }

                if (tree.Kind == GenTreeKind.Conv && tree.Operands.Length == 1)
                {
                    var operand = EvaluateTree(method, tree.Operands[0], facts);
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

                return ValueFact.Unknown;
            }

            private static SsaMethod EnsureValueNumbers(SsaMethod method)
                => method.ValueNumbers is null ? SsaValueNumbering.BuildMethod(method) : method;

            private bool TryGetSsaConstant(SsaMethod method, SsaValueName name, out ConstValue constant)
            {
                constant = default;
                if (!TryGetSsaValueNumber(method, name, out var vn))
                    return false;
                return TryGetConstantFromValueNumber(method, vn, out constant);
            }

            private bool TryGetTreeConstant(SsaMethod method, SsaTree tree, out ConstValue constant)
            {
                constant = default;

                if (tree.Value.HasValue)
                    return TryGetSsaConstant(method, tree.Value.Value, out constant);

                if (tree.Source is null || method.ValueNumbers is null)
                    return false;

                if (!method.ValueNumbers.TryGetTreeValue(tree.Source, out var pair))
                    return false;

                return TryGetConstantFromValueNumber(method, pair.Liberal, out constant);
            }

            private static bool TryGetSsaValueNumber(SsaMethod method, SsaValueName name, out ValueNumber vn)
            {
                vn = ValueNumberStore.NoVN;
                if (method.ValueNumbers is null)
                    return false;
                if (!method.ValueNumbers.TryGetSsaValue(name, out var pair))
                    return false;
                vn = pair.Liberal;
                return vn.IsValid;
            }

            private bool TryGetTreeValueNumber(SsaMethod method, SsaTree tree, out ValueNumber vn)
            {
                vn = ValueNumberStore.NoVN;

                if (tree.Value.HasValue)
                    return TryGetSsaValueNumber(method, tree.Value.Value, out vn);

                if (method.ValueNumbers is null || tree.Source is null)
                    return false;

                if (!method.ValueNumbers.TryGetTreeValue(tree.Source, out var pair))
                    return false;

                vn = pair.Liberal;
                return vn.IsValid;
            }

            private bool TryGetConstantFromValueNumber(SsaMethod method, ValueNumber vn, out ConstValue constant)
            {
                constant = default;
                if (method.ValueNumbers is null || !vn.IsValid)
                    return false;

                if (!method.ValueNumbers.Store.TryGetConstant(vn, out var key))
                    return false;

                switch (key.Kind)
                {
                    case ValueNumberConstantKind.Int32:
                        constant = ConstValue.ForI4((int)key.A);
                        return true;
                    case ValueNumberConstantKind.Int64:
                        constant = ConstValue.ForI8(key.A);
                        return true;
                    case ValueNumberConstantKind.Null:
                        constant = ConstValue.Null;
                        return true;
                    default:
                        return false;
                }
            }

            private bool SameValueNumber(SsaMethod method, SsaTree left, SsaTree right)
            {
                return TryGetTreeValueNumber(method, left, out var leftVn) &&
                       TryGetTreeValueNumber(method, right, out var rightVn) &&
                       leftVn.IsValid &&
                       leftVn == rightVn;
            }

            private bool CanPublishAsValueNumber(SsaValueName target)
            {
                if (!_slotInfos.TryGetValue(target.Slot, out var info))
                    return false;

                if (info.AddressExposed)
                    return false;

                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                return abi.PassingKind is AbiValuePassingKind.ScalarRegister or AbiValuePassingKind.MultiRegister;
            }

            private static bool IsGcOrManagedPointerKind(GenStackKind stackKind)
                => stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null;

            private static bool CanReplaceWithConstant(GenTree template, ConstValue constant)
            {
                if (constant.Kind == ConstKind.Null)
                {
                    return template.Kind == GenTreeKind.ConstNull;
                }

                return IsIntegerLike(template.StackKind);
            }

            private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left is null || right is null)
                    return false;
                return left.Namespace == right.Namespace &&
                       left.Name == right.Name &&
                       left.Kind == right.Kind &&
                       left.SizeOf == right.SizeOf &&
                       left.IsReferenceType == right.IsReferenceType;
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
                            newInputs.Add(input);
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
                        var rewritten = RewriteTree(method, block.Statements[s], facts, ref changed);
                        statements.Add(rewritten);
                    }

                    blocks.Add(new SsaBlock(block.CfgBlock, phis.ToImmutable(), statements.ToImmutable(), block.MemoryPhis, block.MemoryIn, block.MemoryOut));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private bool PhiIsTrivial(SsaPhi phi, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (!_options.PropagateConstants)
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

            private SsaTree RewriteTree(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, ref bool changed)
            {
                if (tree.Value.HasValue)
                {
                    var fact = NormalizeValue(tree.Value.Value, facts);
                    if (_options.PropagateConstants && fact.Kind == ValueFactKind.Constant && CanReplaceWithConstant(tree.Source, fact.Constant))
                    {
                        changed = true;
                        return new SsaTree(CreateConstantTree(tree.Source, fact.Constant), ImmutableArray<SsaTree>.Empty);
                    }

                    return tree;
                }

                var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                bool operandChanged = false;
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var rewritten = RewriteTree(method, tree.Operands[i], facts, ref changed);
                    if (!ReferenceEquals(rewritten, tree.Operands[i]))
                        operandChanged = true;
                    operands.Add(rewritten);
                }

                var newOperands = operandChanged ? operands.ToImmutable() : tree.Operands;
                var candidate = operandChanged
                    ? new SsaTree(tree.Source, newOperands, tree.Value, tree.StoreTarget, tree.LocalFieldBaseValue, tree.LocalField, tree.MemoryUses, tree.MemoryDefinitions)
                    : tree;

                if (_options.SimplifyAlgebraicIdentities && TrySimplifyTree(method, candidate, facts, out var simplified))
                {
                    changed = true;
                    return simplified;
                }

                if (_options.FoldConstants && !candidate.StoreTarget.HasValue && ProducesValue(candidate.Source))
                {
                    var fact = EvaluateTree(method, candidate, facts);
                    if (fact.Kind == ValueFactKind.Constant &&
                        !TryGetSourceConstant(candidate.Source, out _) &&
                        CanReplaceWithConstant(candidate.Source, fact.Constant))
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
                            if (MustPreserveDeadStore(statement.StoreTarget.Value))
                            {
                                statements.Add(statement);
                                continue;
                            }

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

                    blocks.Add(new SsaBlock(block.CfgBlock, phis.ToImmutable(), statements.ToImmutable(), block.MemoryPhis, block.MemoryIn, block.MemoryOut));
                }

                return new OptimizationResult(blocks.ToImmutable(), changed);
            }

            private bool MustPreserveDeadStore(SsaValueName target)
            {
                if (!_slotInfos.TryGetValue(target.Slot, out var info))
                    return true;

                if (IsGcOrManagedPointerKind(info.StackKind))
                    return true;

                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                return abi.ContainsGcPointers;
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

                if (tree.LocalFieldBaseValue.HasValue)
                    MarkValue(tree.LocalFieldBaseValue.Value, live, work);

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
                if (tree.HasMemoryEffects)
                    return true;

                if (tree.Value.HasValue)
                    return false;

                if (tree.StoreTarget.HasValue)
                    return StoreRhsHasObservableEffect(tree);

                switch (tree.Kind)
                {
                    case GenTreeKind.ConstI4:
                    case GenTreeKind.ConstI8:
                    case GenTreeKind.ConstR4Bits:
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

            private bool TrySimplifyTree(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                if (tree.StoreTarget.HasValue || !ProducesValue(tree.Source))
                    return false;

                if (tree.Kind == GenTreeKind.Unary && tree.Operands.Length == 1)
                    return TrySimplifyUnary(method, tree, facts, out simplified);

                if (tree.Kind == GenTreeKind.Binary && tree.Operands.Length == 2)
                    return TrySimplifyBinary(method, tree, facts, out simplified);

                if (tree.Kind == GenTreeKind.Conv && tree.Operands.Length == 1)
                    return TrySimplifyConversion(method, tree, facts, out simplified);

                return false;
            }

            private bool TrySimplifyUnary(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;
                var operand = tree.Operands[0];
                var fact = EvaluateTree(method, operand, facts);

                if (tree.Source.SourceOp == BytecodeOp.Neg && IsZero(fact))
                {
                    simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source));
                    return true;
                }

                if (tree.Source.SourceOp == BytecodeOp.Not && IsAllBitsSet(fact))
                {
                    simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source));
                    return true;
                }

                return false;
            }

            private bool TrySimplifyConversion(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                if ((tree.Source.ConvFlags & NumericConvFlags.Checked) != 0)
                    return false;

                var operand = tree.Operands[0];
                if (!operand.Value.HasValue)
                    return false;

                if (!_slotInfos.TryGetValue(operand.Value.Value.Slot, out var operandInfo))
                    return false;

                var sourceAbi = MachineAbi.ClassifyStorageValue(operandInfo.Type, operandInfo.StackKind);
                var destinationAbi = MachineAbi.ClassifyStorageValue(tree.Source.Type, tree.Source.StackKind);
                if (sourceAbi.PassingKind == destinationAbi.PassingKind &&
                    sourceAbi.RegisterClass == destinationAbi.RegisterClass &&
                    sourceAbi.Size == destinationAbi.Size &&
                    sourceAbi.ContainsGcPointers == destinationAbi.ContainsGcPointers)
                {
                    simplified = operand;
                    return true;
                }

                return false;
            }

            private bool TrySimplifyBinary(SsaMethod method, SsaTree tree, Dictionary<SsaValueName, ValueFact> facts, out SsaTree simplified)
            {
                simplified = null!;

                if (!IsIntegerLike(tree.Source.StackKind) && tree.Source.StackKind is not (GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null))
                    return false;

                var left = tree.Operands[0];
                var right = tree.Operands[1];
                var leftFact = EvaluateTree(method, left, facts);
                var rightFact = EvaluateTree(method, right, facts);
                var op = tree.Source.SourceOp;

                switch (op)
                {
                    case BytecodeOp.Add:
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        break;

                    case BytecodeOp.Sub:
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (SameValue(method, left, right, facts)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Mul:
                        if (IsOne(rightFact)) { simplified = left; return true; }
                        if (IsOne(leftFact)) { simplified = right; return true; }
                        if (IsZero(rightFact) && !HasObservableEffect(left)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsZero(leftFact) && !HasObservableEffect(right)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsIntegerLike(tree.Source.StackKind) && TryGetPositivePowerOfTwoShift(rightFact, out int rightShift)) { simplified = CreateShiftLeftSsaTree(tree.Source, left, rightShift); return true; }
                        if (IsIntegerLike(tree.Source.StackKind) && TryGetPositivePowerOfTwoShift(leftFact, out int leftShift)) { simplified = CreateShiftLeftSsaTree(tree.Source, right, leftShift); return true; }
                        break;

                    case BytecodeOp.Div:
                    case BytecodeOp.Div_Un:
                        if (IsOne(rightFact)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Rem:
                    case BytecodeOp.Rem_Un:
                        if (IsOne(rightFact) && !HasObservableEffect(left)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.And:
                        if (IsZero(rightFact) && !HasObservableEffect(left)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsZero(leftFact) && !HasObservableEffect(right)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        if (IsAllBitsSet(rightFact)) { simplified = left; return true; }
                        if (IsAllBitsSet(leftFact)) { simplified = right; return true; }
                        if (SameValue(method, left, right, facts)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Or:
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        if (IsAllBitsSet(rightFact) && !HasObservableEffect(left)) { simplified = CreateConstantSsaTree(tree.Source, AllBitsSetFor(tree.Source)); return true; }
                        if (IsAllBitsSet(leftFact) && !HasObservableEffect(right)) { simplified = CreateConstantSsaTree(tree.Source, AllBitsSetFor(tree.Source)); return true; }
                        if (SameValue(method, left, right, facts)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Xor:
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        if (IsZero(leftFact)) { simplified = right; return true; }
                        if (SameValue(method, left, right, facts)) { simplified = CreateConstantSsaTree(tree.Source, ZeroFor(tree.Source)); return true; }
                        break;

                    case BytecodeOp.Shl:
                    case BytecodeOp.Shr:
                    case BytecodeOp.Shr_Un:
                        if (IsZero(rightFact)) { simplified = left; return true; }
                        break;

                    case BytecodeOp.Ceq:
                        if (SameValue(method, left, right, facts)) { simplified = CreateConstantSsaTree(tree.Source, ConstValue.ForI4(1)); return true; }
                        break;

                    case BytecodeOp.Clt:
                    case BytecodeOp.Clt_Un:
                    case BytecodeOp.Cgt:
                    case BytecodeOp.Cgt_Un:
                        if (SameValue(method, left, right, facts)) { simplified = CreateConstantSsaTree(tree.Source, ConstValue.ForI4(0)); return true; }
                        break;
                }

                return false;
            }

            private SsaTree CreateConstantSsaTree(GenTree template, ConstValue value)
                => new SsaTree(CreateConstantTree(template, value), ImmutableArray<SsaTree>.Empty);

            private SsaTree CreateShiftLeftSsaTree(GenTree template, SsaTree value, int shift)
            {
                var shiftConst = CreateConstantSsaTree(template, ConstValue.ForI4(shift));
                var operands = ImmutableArray.Create(value, shiftConst);
                var genOperands = ImmutableArray.Create(value.Source, shiftConst.Source);
                var source = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Binary,
                    template.Pc,
                    BytecodeOp.Shl,
                    template.Type,
                    template.StackKind,
                    template.Flags & ~(GenTreeFlags.CanThrow | GenTreeFlags.ContainsCall | GenTreeFlags.MemoryRead | GenTreeFlags.MemoryWrite | GenTreeFlags.SideEffect | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow),
                    genOperands);

                return new SsaTree(source, operands);
            }

            private GenTree CreateLoadTree(GenTree template, SsaValueName value)
            {
                var info = _slotInfos.TryGetValue(value.Slot, out var slotInfo)
                    ? slotInfo
                    : new SsaSlotInfo(value.Slot, template.Type, template.StackKind, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);

                return new GenTree(
                    _nextSyntheticTreeId++,
                    LoadKindFor(value.Slot.Kind),
                    template.Pc,
                    BytecodeOp.Nop,
                    info.Type,
                    info.StackKind,
                    GenTreeFlags.LocalUse,
                    ImmutableArray<GenTree>.Empty,
                    int32: value.Slot.Index);
            }

            private static GenTreeKind LoadKindFor(SsaSlotKind kind)
                => kind switch
                {
                    SsaSlotKind.Arg => GenTreeKind.Arg,
                    SsaSlotKind.Local => GenTreeKind.Local,
                    SsaSlotKind.Temp => GenTreeKind.Temp,
                    _ => GenTreeKind.Nop,
                };

            private bool SameValue(SsaMethod method, SsaTree left, SsaTree right, Dictionary<SsaValueName, ValueFact> facts)
            {
                if (SameValueNumber(method, left, right))
                    return true;

                var leftFact = EvaluateTree(method, left, facts);
                var rightFact = EvaluateTree(method, right, facts);

                if (leftFact.Kind == ValueFactKind.Constant && rightFact.Kind == ValueFactKind.Constant)
                    return leftFact.Constant.Equals(rightFact.Constant);

                if (leftFact.Kind == ValueFactKind.Alias && rightFact.Kind == ValueFactKind.Alias)
                    return leftFact.Alias.Equals(rightFact.Alias);

                if (left.Value.HasValue && right.Value.HasValue)
                    return left.Value.Value.Equals(right.Value.Value);

                return false;
            }

            private static bool IsZero(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == 0 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == 0 ||
                    fact.Constant.Kind == ConstKind.Null);

            private static bool IsOne(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == 1 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == 1);

            private static bool IsAllBitsSet(ValueFact fact)
                => fact.Kind == ValueFactKind.Constant &&
                   (fact.Constant.Kind == ConstKind.I4 && fact.Constant.I4 == -1 ||
                    fact.Constant.Kind == ConstKind.I8 && fact.Constant.I8 == -1);

            private static bool TryGetPositivePowerOfTwoShift(ValueFact fact, out int shift)
            {
                shift = 0;
                if (fact.Kind != ValueFactKind.Constant || fact.Constant.Kind == ConstKind.Null)
                    return false;

                ulong value = fact.Constant.Kind == ConstKind.I8
                    ? (ulong)fact.Constant.I8
                    : (uint)fact.Constant.I4;

                if (value <= 1 || (value & (value - 1)) != 0)
                    return false;

                while ((value >>= 1) != 0)
                    shift++;
                return true;
            }

            private static ConstValue ZeroFor(GenTree template)
                => template.StackKind == GenStackKind.I8 ? ConstValue.ForI8(0) : ConstValue.ForI4(0);

            private static ConstValue AllBitsSetFor(GenTree template)
                => template.StackKind == GenStackKind.I8 ? ConstValue.ForI8(-1) : ConstValue.ForI4(-1);

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

            private static bool IsIntegerLike(GenStackKind stackKind)
                => stackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

            private static bool IsPotentiallyThrowingBinaryOp(BytecodeOp op)
                => op is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un;

            private static bool IsCommutativeBinaryOp(BytecodeOp op)
                => op is BytecodeOp.Add or BytecodeOp.Mul or BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or BytecodeOp.Ceq;

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
                var compacted = CompactSsaNumbers(method.InitialValues, blocks);
                var definitions = BuildValueDefinitions(method.Slots, compacted.InitialValues, compacted.Blocks);
                var memoryDefinitions = GenTreeSsaBuilder.BuildMemoryDefinitions(method.InitialMemoryValues, compacted.Blocks);
                GenTreeSsaBuilder.AnnotateSsaUses(definitions, memoryDefinitions, compacted.Blocks);
                var localDescriptors = BuildSsaLocalDescriptors(method.Slots, definitions, method.SsaLocalDescriptors);
                return new SsaMethod(
                    method.GenTreeMethod,
                    method.Cfg,
                    method.Slots,
                    compacted.InitialValues,
                    definitions,
                    compacted.Blocks,
                    valueNumbers: null,
                    ssaLocalDescriptors: localDescriptors,
                    initialMemoryValues: method.InitialMemoryValues,
                    memoryDefinitions: memoryDefinitions);
            }

            private readonly struct SsaCompactionResult
            {
                public readonly ImmutableArray<SsaValueName> InitialValues;
                public readonly ImmutableArray<SsaBlock> Blocks;

                public SsaCompactionResult(ImmutableArray<SsaValueName> initialValues, ImmutableArray<SsaBlock> blocks)
                {
                    InitialValues = initialValues;
                    Blocks = blocks;
                }
            }

            private static SsaCompactionResult CompactSsaNumbers(ImmutableArray<SsaValueName> initialValues, ImmutableArray<SsaBlock> blocks)
            {
                var map = new Dictionary<SsaValueName, SsaValueName>();
                var nextBySlot = new Dictionary<SsaSlot, int>();
                var compactedInitialValues = ImmutableArray.CreateBuilder<SsaValueName>(initialValues.Length);

                for (int i = 0; i < initialValues.Length; i++)
                {
                    var oldInitial = initialValues[i];
                    var newInitial = new SsaValueName(oldInitial.Slot, SsaConfig.FirstSsaNumber);
                    if (!map.TryAdd(oldInitial, newInitial))
                        throw new InvalidOperationException("Duplicate initial SSA value " + oldInitial + ".");
                    nextBySlot[oldInitial.Slot] = SsaConfig.FirstSsaNumber;
                    compactedInitialValues.Add(newInitial);
                }

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                        AssignDefinition(block.Phis[p].Target);

                    for (int s = 0; s < block.Statements.Length; s++)
                        AssignDefinitions(block.Statements[s]);
                }

                var compactedBlocks = ImmutableArray.CreateBuilder<SsaBlock>(blocks.Length);
                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var inputs = ImmutableArray.CreateBuilder<SsaPhiInput>(phi.Inputs.Length);
                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            inputs.Add(new SsaPhiInput(input.PredecessorBlockId, MapUse(input.Value, "phi input")));
                        }

                        phis.Add(new SsaPhi(block.Id, phi.Slot, MapDefinition(phi.Target), inputs.ToImmutable()));
                    }

                    var statements = ImmutableArray.CreateBuilder<SsaTree>(block.Statements.Length);
                    for (int s = 0; s < block.Statements.Length; s++)
                        statements.Add(RewriteTree(block.Statements[s]));

                    compactedBlocks.Add(new SsaBlock(block.CfgBlock, phis.ToImmutable(), statements.ToImmutable(), block.MemoryPhis, block.MemoryIn, block.MemoryOut));
                }

                var initialList = new List<SsaValueName>(compactedInitialValues.Count);
                for (int i = 0; i < compactedInitialValues.Count; i++)
                    initialList.Add(compactedInitialValues[i]);
                initialList.Sort();
                return new SsaCompactionResult(initialList.ToImmutableArray(), compactedBlocks.ToImmutable());

                void AssignDefinitions(SsaTree tree)
                {
                    for (int i = 0; i < tree.Operands.Length; i++)
                        AssignDefinitions(tree.Operands[i]);

                    if (tree.StoreTarget.HasValue)
                        AssignDefinition(tree.StoreTarget.Value);
                }

                void AssignDefinition(SsaValueName oldName)
                {
                    if (oldName.Version <= SsaConfig.ReservedSsaNumber)
                        throw new InvalidOperationException("Non-initial SSA definition reused the reserved SSA number: " + oldName + ".");
                    if (map.ContainsKey(oldName))
                        throw new InvalidOperationException("Duplicate active SSA definition " + oldName + ".");

                    int next = nextBySlot.TryGetValue(oldName.Slot, out int current) ? current + 1 : SsaConfig.FirstSsaNumber;
                    var newName = new SsaValueName(oldName.Slot, next);
                    nextBySlot[oldName.Slot] = next;
                    map.Add(oldName, newName);
                }

                SsaValueName MapDefinition(SsaValueName oldName)
                {
                    if (map.TryGetValue(oldName, out var newName))
                        return newName;
                    throw new InvalidOperationException("Active SSA definition " + oldName + " was not assigned a compact SSA number.");
                }

                SsaValueName MapUse(SsaValueName oldName, string context)
                {
                    if (map.TryGetValue(oldName, out var newName))
                        return newName;
                    throw new InvalidOperationException("SSA " + context + " uses removed or never-defined value " + oldName + ".");
                }

                SsaTree RewriteTree(SsaTree tree)
                {
                    var operands = ImmutableArray.CreateBuilder<SsaTree>(tree.Operands.Length);
                    for (int i = 0; i < tree.Operands.Length; i++)
                        operands.Add(RewriteTree(tree.Operands[i]));

                    SsaValueName? value = tree.Value.HasValue
                        ? MapUse(tree.Value.Value, "tree use")
                        : null;
                    SsaValueName? target = tree.StoreTarget.HasValue
                        ? MapDefinition(tree.StoreTarget.Value)
                        : null;
                    SsaValueName? localFieldBase = tree.LocalFieldBaseValue.HasValue
                        ? MapUse(tree.LocalFieldBaseValue.Value, "local field base")
                        : null;
                    return new SsaTree(tree.Source, operands.ToImmutable(), value, target, localFieldBase, tree.LocalField, tree.MemoryUses, tree.MemoryDefinitions);
                }
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
                    Add(new SsaValueDefinition(new SsaDescriptor(
                    name.Slot,
                    name.Version,
                    SsaDefinitionKind.Initial,
                    -1,
                    -1,
                    -1,
                    defNode: null,
                    info.Type,
                    info.StackKind)));
                }

                for (int b = 0; b < blocks.Length; b++)
                {
                    var block = blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        var info = GetSlotInfo(infoBySlot, phi.Target.Slot);
                        Add(new SsaValueDefinition(new SsaDescriptor(
                        phi.Target.Slot,
                        phi.Target.Version,
                        SsaDefinitionKind.Phi,
                        block.Id,
                        -1,
                        -1,
                        defNode: null,
                        info.Type,
                        info.StackKind,
                        defBlock: block.CfgBlock,
                        phiDescriptor: phi)));
                    }

                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectDefinitions(block.Statements[s], block, s);
                }

                definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
                return definitions.ToImmutableArray();

                void CollectDefinitions(SsaTree tree, SsaBlock block, int statementIndex)
                {
                    for (int i = 0; i < tree.Operands.Length; i++)
                        CollectDefinitions(tree.Operands[i], block, statementIndex);

                    int blockId = block.Id;

                    if (!tree.StoreTarget.HasValue)
                        return;

                    var name = tree.StoreTarget.Value;
                    var info = GetSlotInfo(infoBySlot, name.Slot);
                    Add(new SsaValueDefinition(new SsaDescriptor(
                        name.Slot,
                        name.Version,
                        SsaDefinitionKind.Store,
                        blockId,
                        statementIndex,
                        tree.Source.Id,
                        tree.Source,
                        info.Type,
                        info.StackKind,
                        tree.LocalFieldBaseValue.HasValue ? tree.LocalFieldBaseValue.Value.Version : SsaConfig.ReservedSsaNumber,
                        block.CfgBlock)));
                }

                void Add(SsaValueDefinition definition)
                {
                    if (!seen.Add(definition.Name))
                        throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                    definitions.Add(definition);
                }
            }

            private static ImmutableArray<SsaLocalDescriptor> BuildSsaLocalDescriptors(
                ImmutableArray<SsaSlotInfo> slots,
                ImmutableArray<SsaValueDefinition> valueDefinitions,
                ImmutableArray<SsaLocalDescriptor> previousDescriptors)
            {
                var previousBySlot = new Dictionary<SsaSlot, SsaLocalDescriptor>();
                for (int i = 0; i < previousDescriptors.Length; i++)
                    previousBySlot[previousDescriptors[i].Slot] = previousDescriptors[i];

                var descriptorsBySlot = new Dictionary<SsaSlot, List<SsaDescriptor>>();
                for (int i = 0; i < valueDefinitions.Length; i++)
                {
                    var descriptor = valueDefinitions[i].Descriptor;
                    if (!descriptorsBySlot.TryGetValue(descriptor.BaseLocal, out var list))
                    {
                        list = new List<SsaDescriptor>();
                        descriptorsBySlot.Add(descriptor.BaseLocal, list);
                    }
                    list.Add(descriptor);
                }

                var result = ImmutableArray.CreateBuilder<SsaLocalDescriptor>(slots.Length);
                for (int i = 0; i < slots.Length; i++)
                {
                    var info = slots[i];
                    descriptorsBySlot.TryGetValue(info.Slot, out var list);
                    var perSsaData = DensePerSsaData(info.Slot, list);
                    previousBySlot.TryGetValue(info.Slot, out var previous);
                    var local = new SsaLocalDescriptor(
                        info.Slot,
                        info.Type,
                        info.StackKind,
                        info.AddressExposed,
                        previous?.IsSsaPromoted ?? perSsaData.Length != 0,
                        previous?.LocalDescriptor,
                        perSsaData);
                    previous?.LocalDescriptor?.SetSsaDescriptors(perSsaData);
                    result.Add(local);
                }

                return result.ToImmutable();

                static ImmutableArray<SsaDescriptor> DensePerSsaData(SsaSlot slot, List<SsaDescriptor>? descriptors)
                {
                    if (descriptors is null || descriptors.Count == 0)
                        return ImmutableArray<SsaDescriptor>.Empty;

                    int max = -1;
                    for (int i = 0; i < descriptors.Count; i++)
                        max = Math.Max(max, descriptors[i].SsaNumber);

                    var table = new SsaDescriptor?[max + 1];
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        var descriptor = descriptors[i];
                        if (table[descriptor.SsaNumber] is not null)
                            throw new InvalidOperationException("Duplicate SSA descriptor " + descriptor.Name + ".");
                        table[descriptor.SsaNumber] = descriptor;
                    }

                    var builder = ImmutableArray.CreateBuilder<SsaDescriptor>(table.Length);
                    for (int i = 0; i < table.Length; i++)
                    {
                        if (i == SsaConfig.ReservedSsaNumber)
                        {
                            builder.Add(null!);
                            continue;
                        }

                        if (table[i] is null)
                            throw new InvalidOperationException("Missing SSA descriptor " + slot + "_" + i.ToString() + ".");
                        builder.Add(table[i]!);
                    }
                    return builder.ToImmutable();
                }
            }

            private static SsaSlotInfo GetSlotInfo(Dictionary<SsaSlot, SsaSlotInfo> infoBySlot, SsaSlot slot)
            {
                if (infoBySlot.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(slot, null, GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
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
            var memoryDefinitions = BuildMemoryDefinitionMap(method);
            VerifyDescriptorTables(method, definitions);
            VerifyLclVarDscState(method);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                VerifyPhis(method, definitions, block);
                VerifyMemoryBlockStates(method, memoryDefinitions, block);
                VerifyMemoryPhis(method, memoryDefinitions, block);

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyStatement(method, definitions, memoryDefinitions, block.Id, s, block.Statements[s]);
            }
        }

        private static Dictionary<SsaValueName, SsaValueDefinition> BuildDefinitionMap(SsaMethod method)
        {
            var result = new Dictionary<SsaValueName, SsaValueDefinition>();

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                if (definition.Name.Version <= SsaConfig.ReservedSsaNumber)
                    throw new InvalidOperationException($"Invalid SSA version {definition.Name}.");

                if (!definition.Name.Slot.HasLclNum)
                    throw new InvalidOperationException($"SSA definition {definition.Name} does not carry a concrete lclNum.");

                if (definition.IsInitial)
                {
                    if (definition.Name.Version != SsaConfig.FirstSsaNumber || definition.DefBlockId != -1)
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

        private static Dictionary<SsaMemoryValueName, SsaMemoryDefinition> BuildMemoryDefinitionMap(SsaMethod method)
        {
            var result = new Dictionary<SsaMemoryValueName, SsaMemoryDefinition>();

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var definition = method.MemoryDefinitions[i];
                if (definition.Name.Version <= SsaConfig.ReservedSsaNumber)
                    throw new InvalidOperationException("Invalid memory SSA version " + definition.Name + ".");

                if (definition.IsInitial)
                {
                    if (definition.Name.Version != SsaConfig.FirstSsaNumber || definition.DefBlockId != -1)
                        throw new InvalidOperationException("Malformed initial memory SSA definition " + definition.Name + ".");
                }
                else if ((uint)definition.DefBlockId >= (uint)method.Blocks.Length)
                {
                    throw new InvalidOperationException("Memory SSA definition " + definition.Name + " has invalid block B" + definition.DefBlockId.ToString() + ".");
                }

                if (definition.IsPhi && definition.DefStatementIndex != -1)
                    throw new InvalidOperationException("Memory phi definition " + definition.Name + " has statement index " + definition.DefStatementIndex.ToString() + ".");

                if (definition.IsStore && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException("Memory store definition " + definition.Name + " has no statement index.");

                if (definition.IsBlockOut && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException("Memory block-out definition " + definition.Name + " has no statement index.");

                if (result.ContainsKey(definition.Name))
                    throw new InvalidOperationException("Duplicate memory SSA definition " + definition.Name + ".");

                result.Add(definition.Name, definition);
            }

            for (int i = 0; i < method.InitialMemoryValues.Length; i++)
            {
                if (!result.TryGetValue(method.InitialMemoryValues[i], out var definition) || !definition.IsInitial)
                    throw new InvalidOperationException("Initial memory SSA value " + method.InitialMemoryValues[i] + " is missing from definition table.");
            }

            return result;
        }

        private static void VerifyDescriptorTables(SsaMethod method, Dictionary<SsaValueName, SsaValueDefinition> definitions)
        {
            var localsBySlot = new Dictionary<SsaSlot, SsaLocalDescriptor>();
            for (int i = 0; i < method.SsaLocalDescriptors.Length; i++)
            {
                var local = method.SsaLocalDescriptors[i];
                if (!local.Slot.HasLclNum)
                    throw new InvalidOperationException("SSA local descriptor does not carry a concrete lclNum: " + local.Slot + ".");
                if (local.LocalDescriptor is not null && local.LocalDescriptor.LclNum != local.Slot.LclNum)
                    throw new InvalidOperationException("SSA local descriptor lclNum disagrees with LclVarDsc: " + local.Slot + " vs " + local.LocalDescriptor + ".");
                if (localsBySlot.ContainsKey(local.Slot))
                    throw new InvalidOperationException("Duplicate SSA local descriptor for " + local.Slot + ".");
                localsBySlot.Add(local.Slot, local);

                for (int ssaNum = SsaConfig.FirstSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                {
                    var descriptor = local.PerSsaData[ssaNum];
                    if (descriptor is null || !descriptor.BaseLocal.Equals(local.Slot) || descriptor.SsaNumber != ssaNum)
                        throw new InvalidOperationException("Malformed SSA descriptor table entry for " + local.Slot + " at index " + ssaNum.ToString() + ".");
                    if (!definitions.TryGetValue(descriptor.Name, out var definition))
                        throw new InvalidOperationException("SSA descriptor " + descriptor.Name + " is missing from definition table.");
                    if (!ReferenceEquals(definition.Descriptor, descriptor))
                        throw new InvalidOperationException("Definition table and descriptor table disagree for " + descriptor.Name + ".");
                    if (descriptor.IsPartialDefinition)
                    {
                        var previous = new SsaValueName(descriptor.BaseLocal, descriptor.PreviousSsaNumber);
                        if (!definitions.ContainsKey(previous))
                            throw new InvalidOperationException("Partial SSA definition " + descriptor.Name + " references missing previous definition " + previous + ".");
                    }
                }
            }

            foreach (var item in definitions)
            {
                if (!localsBySlot.TryGetValue(item.Key.Slot, out var local))
                    throw new InvalidOperationException("SSA definition " + item.Key + " is missing its base local descriptor.");
                if (!local.TryGetSsaDefByNumber(item.Key.Version, out var descriptor) || !ReferenceEquals(descriptor, item.Value.Descriptor))
                    throw new InvalidOperationException("SSA definition " + item.Key + " is not reachable through base local descriptor.");
            }
        }

        private static void VerifyLclVarDscState(SsaMethod method)
        {
            var descriptors = method.GenTreeMethod.AllLocalDescriptors;
            var seenVarIndex = new Dictionary<int, GenLocalDescriptor>();

            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor.LclNum != i)
                    throw new InvalidOperationException("LclVarDsc table is not dense at lclNum " + i.ToString() + ".");

                if (descriptor.VarIndex >= 0)
                {
                    if (!descriptor.Tracked)
                        throw new InvalidOperationException("Untracked LclVarDsc has lvVarIndex: " + descriptor + ".");

                    if (descriptor.AddressExposed || descriptor.MemoryAliased)
                        throw new InvalidOperationException("Address-exposed or memory-aliased LclVarDsc participates in tracked-local liveness: " + descriptor + ".");

                    if (seenVarIndex.TryGetValue(descriptor.VarIndex, out var other))
                        throw new InvalidOperationException("Duplicate dense lvVarIndex " + descriptor.VarIndex.ToString() + " for " + other + " and " + descriptor + ".");

                    seenVarIndex.Add(descriptor.VarIndex, descriptor);
                }

                if (descriptor.SsaPromoted && !descriptor.CanBeSsaRenamedAsScalar)
                    throw new InvalidOperationException("Memory-aliased or non-scalar LclVarDsc is marked lvInSsa: " + descriptor + ".");
            }

            for (int i = 0; i < seenVarIndex.Count; i++)
            {
                if (!seenVarIndex.ContainsKey(i))
                    throw new InvalidOperationException("lvVarIndex is not dense; missing index " + i.ToString() + ".");
            }

            for (int i = 0; i < method.SsaLocalDescriptors.Length; i++)
            {
                var local = method.SsaLocalDescriptors[i];
                var descriptor = local.LocalDescriptor;
                if (descriptor is null)
                    continue;

                if (local.PerSsaData.Length != descriptor.PerSsaData.Length)
                    throw new InvalidOperationException("SsaLocalDescriptor and LclVarDsc lvPerSsaData length disagree for " + descriptor + ".");

                for (int ssaNum = SsaConfig.ReservedSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                {
                    if (!ReferenceEquals(local.PerSsaData[ssaNum], descriptor.PerSsaData[ssaNum]))
                        throw new InvalidOperationException("SsaLocalDescriptor and LclVarDsc lvPerSsaData entry disagree for " + descriptor + " at " + ssaNum.ToString() + ".");
                }
            }
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

                if (!ReferenceEquals(definition.Phi, phi) || !ReferenceEquals(definition.Descriptor.Phi, phi))
                    throw new InvalidOperationException($"SSA descriptor for phi {phi.Target} in B{block.Id} does not point back to its phi node.");

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
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            VerifyTreeUses(method, definitions, memoryDefinitions, blockId, statementIndex, tree);
            VerifyTreeDefinitions(method, definitions, memoryDefinitions, blockId, statementIndex, tree);
        }

        private static void VerifyTreeUses(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            if (tree.Value.HasValue)
                VerifyLocalUse(method, definitions, tree.Value.Value, blockId, statementIndex, tree.Source.Id);

            if (tree.LocalFieldBaseValue.HasValue)
                VerifyLocalUse(method, definitions, tree.LocalFieldBaseValue.Value, blockId, statementIndex, tree.Source.Id);

            for (int i = 0; i < tree.MemoryUses.Length; i++)
                VerifyMemoryUse(method, memoryDefinitions, tree.MemoryUses[i], blockId, statementIndex, tree.Source.Id);

            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeUses(method, definitions, memoryDefinitions, blockId, statementIndex, tree.Operands[i]);
        }

        private static void VerifyTreeDefinitions(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeDefinitions(method, definitions, memoryDefinitions, blockId, statementIndex, tree.Operands[i]);

            if (tree.LocalFieldBaseValue.HasValue)
            {
                if (tree.LocalField is null)
                    throw new InvalidOperationException($"SSA local-field node {tree.Source.Id} has a base value but no field.");

                if (tree.StoreTarget.HasValue && !tree.StoreTarget.Value.Slot.Equals(tree.LocalFieldBaseValue.Value.Slot))
                    throw new InvalidOperationException($"Partial definition at node {tree.Source.Id} changes slot {tree.StoreTarget.Value.Slot} from base slot {tree.LocalFieldBaseValue.Value.Slot}.");
            }

            for (int i = 0; i < tree.MemoryDefinitions.Length; i++)
            {
                var memoryName = tree.MemoryDefinitions[i];
                if (!memoryDefinitions.TryGetValue(memoryName, out var memoryDefinition))
                    throw new InvalidOperationException("Memory definition " + memoryName + " at node " + tree.Source.Id.ToString() + " is missing from definition table.");
                if (!memoryDefinition.IsStore || memoryDefinition.DefBlockId != blockId || memoryDefinition.DefStatementIndex != statementIndex || memoryDefinition.DefTreeId != tree.Source.Id)
                    throw new InvalidOperationException("Memory definition table entry for " + memoryName + " does not match store node " + tree.Source.Id.ToString() + ".");
            }

            if (!tree.StoreTarget.HasValue)
                return;

            if (tree.LocalFieldBaseValue.HasValue != tree.IsPartialDefinition)
                throw new InvalidOperationException($"Store node {tree.Source.Id} has inconsistent partial-definition metadata.");

            var name = tree.StoreTarget.Value;
            if (!definitions.TryGetValue(name, out var definition))
                throw new InvalidOperationException($"Store target {name} at node {tree.Source.Id} is missing from definition table.");

            if (definition.IsInitial || definition.IsPhi || definition.DefBlockId != blockId || definition.DefStatementIndex != statementIndex || definition.DefTreeId != tree.Source.Id)
                throw new InvalidOperationException($"Definition table entry for {name} does not match store node {tree.Source.Id}.");
        }

        private static void VerifyMemoryBlockStates(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            SsaBlock block)
        {
            for (int k = 0; k < SsaMemoryKinds.All.Length; k++)
            {
                var kind = SsaMemoryKinds.All[k];
                if (!block.TryGetMemoryIn(kind, out var memoryIn))
                    throw new InvalidOperationException("Block B" + block.Id.ToString() + " has no incoming memory SSA value for " + SsaMemoryKinds.Name(kind) + ".");
                if (!memoryDefinitions.ContainsKey(memoryIn))
                    throw new InvalidOperationException("Block B" + block.Id.ToString() + " has undefined incoming memory SSA value " + memoryIn + ".");

                if (!block.TryGetMemoryOut(kind, out var memoryOut))
                    throw new InvalidOperationException("Block B" + block.Id.ToString() + " has no outgoing memory SSA value for " + SsaMemoryKinds.Name(kind) + ".");
                if (!memoryDefinitions.ContainsKey(memoryOut))
                    throw new InvalidOperationException("Block B" + block.Id.ToString() + " has undefined outgoing memory SSA value " + memoryOut + ".");
            }
        }

        private static void VerifyMemoryPhis(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            SsaBlock block)
        {
            var expectedPreds = new HashSet<int>();
            for (int p = 0; p < block.CfgBlock.Predecessors.Length; p++)
                expectedPreds.Add(block.CfgBlock.Predecessors[p].FromBlockId);

            for (int i = 0; i < block.MemoryPhis.Length; i++)
            {
                var phi = block.MemoryPhis[i];
                if (!memoryDefinitions.TryGetValue(phi.Target, out var definition))
                    throw new InvalidOperationException("Memory phi target " + phi.Target + " in B" + block.Id.ToString() + " is missing from definition table.");

                if (!definition.IsPhi || definition.DefBlockId != block.Id)
                    throw new InvalidOperationException("Definition table entry for memory phi " + phi.Target + " does not match B" + block.Id.ToString() + ".");

                if (!ReferenceEquals(definition.Phi, phi) || !ReferenceEquals(definition.Descriptor.Phi, phi))
                    throw new InvalidOperationException("Memory SSA descriptor for phi " + phi.Target + " in B" + block.Id.ToString() + " does not point back to its phi node.");

                if (phi.Target.Kind != phi.Kind)
                    throw new InvalidOperationException("Memory phi target " + phi.Target + " in B" + block.Id.ToString() + " does not belong to phi kind " + SsaMemoryKinds.Name(phi.Kind) + ".");

                if (!block.TryGetMemoryIn(phi.Kind, out var memoryIn) || !memoryIn.Equals(phi.Target))
                    throw new InvalidOperationException("Block B" + block.Id.ToString() + " incoming memory state for " + SsaMemoryKinds.Name(phi.Kind) + " does not point at its phi target.");

                var actualPreds = new HashSet<int>();
                for (int p = 0; p < phi.Inputs.Length; p++)
                {
                    var input = phi.Inputs[p];
                    if (input.Value.Kind != phi.Kind)
                        throw new InvalidOperationException("Memory phi " + phi.Target + " in B" + block.Id.ToString() + " has cross-kind input " + input.Value + " from B" + input.PredecessorBlockId.ToString() + ".");
                    actualPreds.Add(input.PredecessorBlockId);
                    VerifyMemoryEdgeUse(method, memoryDefinitions, phi.Target, input.Value, input.PredecessorBlockId);
                }

                if (!expectedPreds.SetEquals(actualPreds))
                    throw new InvalidOperationException("Malformed memory phi " + phi.Target + " in B" + block.Id.ToString() + ": predecessor set mismatch.");
            }
        }

        private static void VerifyMemoryUse(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> definitions,
            SsaMemoryValueName use,
            int useBlockId,
            int useStatementIndex,
            int useTreeId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException("Use of undefined memory SSA value " + use + " at node " + useTreeId.ToString() + ".");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, useBlockId))
                throw new InvalidOperationException("Memory SSA definition " + use + " in B" + definition.DefBlockId.ToString() + " does not dominate use at node " + useTreeId.ToString() + " in B" + useBlockId.ToString() + ".");

            if (definition.DefBlockId == useBlockId && !definition.IsPhi && definition.DefStatementIndex >= useStatementIndex)
                throw new InvalidOperationException("Memory SSA definition " + use + " at statement " + definition.DefStatementIndex.ToString() + " does not precede use at statement " + useStatementIndex.ToString() + " in B" + useBlockId.ToString() + ".");
        }

        private static void VerifyMemoryEdgeUse(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> definitions,
            SsaMemoryValueName phiTarget,
            SsaMemoryValueName use,
            int predecessorBlockId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException("Memory phi " + phiTarget + " uses undefined value " + use + " from B" + predecessorBlockId.ToString() + ".");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, predecessorBlockId))
                throw new InvalidOperationException("Memory phi " + phiTarget + " input " + use + " from B" + predecessorBlockId.ToString() + " is not dominated by its definition in B" + definition.DefBlockId.ToString() + ".");
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

            if (method.InitialMemoryValues.Length != 0)
            {
                sb.Append("  initial-memory: ");
                AppendMemoryValueList(sb, method.InitialMemoryValues);
                sb.AppendLine();
            }

            if (method.SsaLocalDescriptors.Length != 0)
            {
                sb.AppendLine("  ssa descriptors:");
                for (int l = 0; l < method.SsaLocalDescriptors.Length; l++)
                {
                    var local = method.SsaLocalDescriptors[l];
                    if (local.PerSsaData.IsDefaultOrEmpty)
                        continue;
                    sb.Append("    ").Append(local.Slot).Append(':');
                    for (int ssaNum = SsaConfig.FirstSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                    {
                        var descriptor = local.PerSsaData[ssaNum];
                        if (descriptor is null)
                            continue;
                        if (ssaNum != SsaConfig.FirstSsaNumber)
                            sb.Append(';');
                        sb.Append(' ').Append(descriptor);
                    }
                    sb.AppendLine();
                }
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

                if (block.MemoryIn.Length != 0)
                {
                    sb.Append("    memory-in: ");
                    AppendMemoryValueList(sb, block.MemoryIn);
                    sb.AppendLine();
                }

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

                for (int i = 0; i < block.MemoryPhis.Length; i++)
                {
                    var phi = block.MemoryPhis[i];
                    sb.Append("    ").Append(phi.Target).Append(" = memory-phi(");
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
                    AppendMemoryAnnotation(sb, block.Statements[i]);
                    sb.AppendLine();
                }

                if (block.MemoryOut.Length != 0)
                {
                    sb.Append("    memory-out: ");
                    AppendMemoryValueList(sb, block.MemoryOut);
                    sb.AppendLine();
                }
            }
        }

        private static void AppendMemoryValueList(StringBuilder sb, ImmutableArray<SsaMemoryValueName> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                sb.Append(values[i]);
            }
        }

        private static void AppendMemoryAnnotation(StringBuilder sb, SsaTree tree)
        {
            if (!tree.HasMemoryEffects)
                return;

            sb.Append("  ; mem");
            if (tree.MemoryUses.Length != 0)
            {
                sb.Append(" use=[");
                AppendMemoryValueList(sb, tree.MemoryUses);
                sb.Append(']');
            }
            if (tree.MemoryDefinitions.Length != 0)
            {
                sb.Append(" def=[");
                AppendMemoryValueList(sb, tree.MemoryDefinitions);
                sb.Append(']');
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
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(tree.Source.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
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
