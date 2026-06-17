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
        public readonly int Header;
        public readonly ImmutableArray<int> Latches;
        public readonly ImmutableArray<int> Blocks;
        public readonly ImmutableArray<int> Entries;
        public readonly ImmutableArray<int> Exits;
        public readonly ImmutableArray<int> ExitDestinations;
        public readonly int Preheader;
        public readonly int Parent;
        public readonly int Depth;
        public readonly bool IsReducible;
        public readonly bool IsCanonicalPreheader;
        public readonly bool HeaderHasExceptionalSuccessorInsideLoop;

        public CfgLoop(
            int index,
            int header,
            ImmutableArray<int> latches,
            ImmutableArray<int> blocks,
            ImmutableArray<int> entries,
            ImmutableArray<int> exits,
            ImmutableArray<int> exitDestinations,
            int preheader,
            int parent,
            int depth,
            bool isReducible,
            bool isCanonicalPreheader,
            bool headerHasExceptionalSuccessorInsideLoop)
        {
            Index = index;
            Header = header;
            Latches = latches.IsDefault ? ImmutableArray<int>.Empty : latches;
            Blocks = blocks.IsDefault ? ImmutableArray<int>.Empty : blocks;
            Entries = entries.IsDefault ? ImmutableArray<int>.Empty : entries;
            Exits = exits.IsDefault ? ImmutableArray<int>.Empty : exits;
            ExitDestinations = exitDestinations.IsDefault ? ImmutableArray<int>.Empty : exitDestinations;
            Preheader = preheader;
            Parent = parent;
            Depth = depth;
            IsReducible = isReducible;
            IsCanonicalPreheader = isCanonicalPreheader;
            HeaderHasExceptionalSuccessorInsideLoop = headerHasExceptionalSuccessorInsideLoop;
        }

        public bool Contains(int blockId)
        {
            for (int i = 0; i < Blocks.Length; i++)
            {
                if (Blocks[i] == blockId)
                    return true;
            }
            return false;
        }

        public override string ToString()
            => $"L{Index}: header=B{Header}, latches={Latches.Length}, preheader={(Preheader >= 0 ? "B" + Preheader.ToString() : "none")}, depth={Depth}";
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
        public readonly int SourceHandlerIndex;
        public readonly int EnclosingTryIndex;
        public readonly int EnclosingHandlerIndex;
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
            int sourceHandlerIndex,
            int enclosingTryIndex,
            int enclosingHandlerIndex,
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
            SourceHandlerIndex = sourceHandlerIndex;
            EnclosingTryIndex = enclosingTryIndex;
            EnclosingHandlerIndex = enclosingHandlerIndex;
            ParentIndex = parentIndex;
        }

        public bool HasParent => ParentIndex >= 0;
        public int TrySpanPc => TryEndPc - TryStartPc;
        public int HandlerSpanPc => HandlerEndPc - HandlerStartPc;

        public bool HasSameTry(in CfgExceptionRegion other)
            => TryStartPc == other.TryStartPc && TryEndPc == other.TryEndPc;

        public bool ContainsTryBlock(int blockId)
            => TryStartBlockId <= blockId && blockId < TryEndBlockIdExclusive;

        public bool ContainsHandlerBlock(int blockId)
            => HandlerStartBlockId <= blockId && blockId < HandlerEndBlockIdExclusive;

        public override string ToString()
            => $"EH{Index}:{Kind} try=B{TryStartBlockId}..B{TryEndBlockIdExclusive - 1} handler=B{HandlerStartBlockId}..B{HandlerEndBlockIdExclusive - 1} parent={ParentIndex}";
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

        private const int VirtualRootBlockId = -2;
        public ImmutableArray<int> DominatorRoots { get; }
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
            ImmutableArray<CfgExceptionRegion> exceptionRegions,
            ImmutableArray<int> dominatorRoots)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Blocks = blocks.IsDefault ? ImmutableArray<CfgBlock>.Empty : blocks;
            ReversePostOrder = reversePostOrder.IsDefault ? ImmutableArray<int>.Empty : reversePostOrder;
            ImmediateDominators = immediateDominators.IsDefault ? ImmutableArray<int>.Empty : immediateDominators;
            DominanceFrontiers = dominanceFrontiers.IsDefault ? ImmutableArray<ImmutableArray<int>>.Empty : dominanceFrontiers;
            DominatorTreeChildren = dominatorTreeChildren.IsDefault ? ImmutableArray<ImmutableArray<int>>.Empty : dominatorTreeChildren;
            NaturalLoops = naturalLoops.IsDefault ? ImmutableArray<CfgLoop>.Empty : naturalLoops;
            ExceptionRegions = exceptionRegions.IsDefault ? ImmutableArray<CfgExceptionRegion>.Empty : exceptionRegions;
            DominatorRoots = dominatorRoots.IsDefault ? ImmutableArray<int>.Empty : dominatorRoots;
        }

        public static ControlFlowGraph Build(GenTreeMethod method, bool includeExceptionEdges = false)
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
            {
                if (!blockByStartPc.ContainsKey(method.Blocks[i].StartPc))
                    blockByStartPc.Add(method.Blocks[i].StartPc, method.Blocks[i].Id);
            }

            var exceptionRegions = BuildExceptionRegions(method, blockByStartPc);

            for (int i = 0; i < n; i++)
            {
                var block = method.Blocks[i];
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                    AddEdge(succ, pred, new CfgEdge(block.Id, block.SuccessorBlockIds[s], ClassifyNormalEdge(block, block.SuccessorBlockIds[s])));
            }

            if (includeExceptionEdges)
            {
                var exceptionEdges = BuildImplicitExceptionEdges(method.Blocks, exceptionRegions);
                for (int i = 0; i < exceptionEdges.Length; i++)
                    AddEdge(succ, pred, exceptionEdges[i]);
            }

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
            var dominatorRoots = ComputeDominatorRoots(blocks, exceptionRegions);
            var rpo = ComputeReversePostOrder(blocks, dominatorRoots);
            var idom = ComputeImmediateDominators(blocks, rpo, dominatorRoots);
            var df = ComputeDominanceFrontiers(blocks, idom);
            var children = ComputeDominatorTreeChildren(idom);
            var loops = ComputeNaturalLoops(blocks, idom);

            return new ControlFlowGraph(method, blocks, rpo.ToImmutableArray(), idom.ToImmutableArray(), df, children, loops, exceptionRegions, dominatorRoots);
        }

        private readonly struct RawExceptionRegion
        {
            public readonly int SourceHandlerIndex;
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

            public RawExceptionRegion(
                int sourceHandlerIndex,
                CfgExceptionRegionKind kind,
                int tryStartPc,
                int tryEndPc,
                int handlerStartPc,
                int handlerEndPc,
                int tryStartBlockId,
                int tryEndBlockIdExclusive,
                int handlerStartBlockId,
                int handlerEndBlockIdExclusive,
                int catchTypeToken)
            {
                SourceHandlerIndex = sourceHandlerIndex;
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
            }
        }

        private static ImmutableArray<CfgExceptionRegion> BuildExceptionRegions(GenTreeMethod method, IReadOnlyDictionary<int, int> blockByStartPc)
        {
            if (method.Function.ExceptionHandlers.Length == 0)
                return ImmutableArray<CfgExceptionRegion>.Empty;

            var raw = new List<RawExceptionRegion>(method.Function.ExceptionHandlers.Length);
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

                raw.Add(new RawExceptionRegion(
                    i,
                    kind,
                    eh.TryStartPc,
                    eh.TryEndPc,
                    eh.HandlerStartPc,
                    eh.HandlerEndPc,
                    tryStartBlock,
                    tryEndBlock,
                    handlerStartBlock,
                    handlerEndBlock,
                    eh.CatchTypeToken));
            }

            if (raw.Count == 0)
                return ImmutableArray<CfgExceptionRegion>.Empty;

            var depthBySourceIndex = new Dictionary<int, int>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
                depthBySourceIndex[raw[i].SourceHandlerIndex] = ComputeRegionDepth(raw, raw[i]);

            raw.Sort((left, right) => CompareRawExceptionRegions(left, right, depthBySourceIndex));

            var enclosingTryIndexes = new int[raw.Count];
            var enclosingHandlerIndexes = new int[raw.Count];
            var parentIndexes = new int[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                enclosingTryIndexes[i] = ComputeEnclosingTryIndex(raw, i);
                enclosingHandlerIndexes[i] = ComputeEnclosingHandlerIndex(raw, i);
                parentIndexes[i] = ComputeParentRegionIndex(raw, i);
            }

            var regions = ImmutableArray.CreateBuilder<CfgExceptionRegion>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var region = raw[i];
                regions.Add(new CfgExceptionRegion(
                    i,
                    region.Kind,
                    region.TryStartPc,
                    region.TryEndPc,
                    region.HandlerStartPc,
                    region.HandlerEndPc,
                    region.TryStartBlockId,
                    region.TryEndBlockIdExclusive,
                    region.HandlerStartBlockId,
                    region.HandlerEndBlockIdExclusive,
                    region.CatchTypeToken,
                    region.SourceHandlerIndex,
                    enclosingTryIndexes[i],
                    enclosingHandlerIndexes[i],
                    parentIndexes[i]));
            }

            return regions.ToImmutable();
        }

        private static int CompareRawExceptionRegions(RawExceptionRegion left, RawExceptionRegion right, IReadOnlyDictionary<int, int> depthBySourceIndex)
        {
            if (left.SourceHandlerIndex == right.SourceHandlerIndex)
                return 0;

            if (left.TryStartPc == right.TryStartPc && left.TryEndPc == right.TryEndPc)
                return left.SourceHandlerIndex.CompareTo(right.SourceHandlerIndex);

            int leftDepth = depthBySourceIndex[left.SourceHandlerIndex];
            int rightDepth = depthBySourceIndex[right.SourceHandlerIndex];
            int c = rightDepth.CompareTo(leftDepth);
            if (c != 0) return c;

            c = left.TryStartPc.CompareTo(right.TryStartPc);
            if (c != 0) return c;
            c = right.TryEndPc.CompareTo(left.TryEndPc);
            if (c != 0) return c;
            c = left.HandlerStartPc.CompareTo(right.HandlerStartPc);
            if (c != 0) return c;
            return left.SourceHandlerIndex.CompareTo(right.SourceHandlerIndex);
        }

        private static int ComputeRegionDepth(List<RawExceptionRegion> regions, RawExceptionRegion region)
        {
            int depth = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                var candidate = regions[i];
                if (candidate.SourceHandlerIndex == region.SourceHandlerIndex)
                    continue;

                if (StrictlyContainsRange(candidate.TryStartPc, candidate.TryEndPc, region.TryStartPc, region.TryEndPc) ||
                    StrictlyContainsRange(candidate.HandlerStartPc, candidate.HandlerEndPc, region.TryStartPc, region.TryEndPc))
                {
                    depth++;
                }
            }
            return depth;
        }

        private static int ComputeEnclosingTryIndex(List<RawExceptionRegion> regions, int childIndex)
        {
            var child = regions[childIndex];
            int bestIndex = -1;
            int bestSpan = int.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                if (i == childIndex)
                    continue;

                var candidate = regions[i];
                if (!StrictlyContainsRange(candidate.TryStartPc, candidate.TryEndPc, child.TryStartPc, child.TryEndPc))
                    continue;

                int span = candidate.TryEndPc - candidate.TryStartPc;
                if (span < bestSpan || (span == bestSpan && (bestIndex < 0 || i < bestIndex)))
                {
                    bestIndex = i;
                    bestSpan = span;
                }
            }
            return bestIndex;
        }

        private static int ComputeEnclosingHandlerIndex(List<RawExceptionRegion> regions, int childIndex)
        {
            var child = regions[childIndex];
            int bestIndex = -1;
            int bestSpan = int.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                if (i == childIndex)
                    continue;

                var candidate = regions[i];
                if (!ContainsRange(candidate.HandlerStartPc, candidate.HandlerEndPc, child.TryStartPc, child.TryEndPc))
                    continue;

                int span = candidate.HandlerEndPc - candidate.HandlerStartPc;
                if (span < bestSpan || (span == bestSpan && (bestIndex < 0 || i < bestIndex)))
                {
                    bestIndex = i;
                    bestSpan = span;
                }
            }
            return bestIndex;
        }

        private static int ComputeParentRegionIndex(List<RawExceptionRegion> regions, int childIndex)
        {
            var child = regions[childIndex];
            int bestIndex = -1;
            int bestSpan = int.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                if (i == childIndex)
                    continue;

                var candidate = regions[i];
                int span = int.MaxValue;
                if (ContainsRange(candidate.TryStartPc, candidate.TryEndPc, child.HandlerStartPc, child.HandlerEndPc))
                    span = candidate.TryEndPc - candidate.TryStartPc;
                if (ContainsRange(candidate.HandlerStartPc, candidate.HandlerEndPc, child.HandlerStartPc, child.HandlerEndPc))
                    span = Math.Min(span, candidate.HandlerEndPc - candidate.HandlerStartPc);
                if (span == int.MaxValue)
                    continue;

                if (span < bestSpan || (span == bestSpan && (bestIndex < 0 || i < bestIndex)))
                {
                    bestIndex = i;
                    bestSpan = span;
                }
            }
            return bestIndex;
        }

        private static bool ContainsRange(int containerStart, int containerEnd, int rangeStart, int rangeEnd)
            => containerStart <= rangeStart && rangeEnd <= containerEnd;

        private static bool StrictlyContainsRange(int containerStart, int containerEnd, int rangeStart, int rangeEnd)
            => ContainsRange(containerStart, containerEnd, rangeStart, rangeEnd) &&
               (containerStart < rangeStart || rangeEnd < containerEnd);

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

            for (int b = 0; b < result.Length; b++)
                result[b].Sort((left, right) => CompareRegionContainment(regions[left], regions[right], tryRegion));

            return result;
        }

        private static int CompareRegionContainment(CfgExceptionRegion left, CfgExceptionRegion right, bool tryRegion)
        {
            if (left.Index == right.Index)
                return 0;

            int leftSpan = tryRegion
                ? left.TryEndBlockIdExclusive - left.TryStartBlockId
                : left.HandlerEndBlockIdExclusive - left.HandlerStartBlockId;
            int rightSpan = tryRegion
                ? right.TryEndBlockIdExclusive - right.TryStartBlockId
                : right.HandlerEndBlockIdExclusive - right.HandlerStartBlockId;

            int c = leftSpan.CompareTo(rightSpan);
            if (c != 0) return c;

            if (left.HasSameTry(right))
                return left.SourceHandlerIndex.CompareTo(right.SourceHandlerIndex);

            c = right.TryStartPc.CompareTo(left.TryStartPc);
            if (c != 0) return c;
            c = left.TryEndPc.CompareTo(right.TryEndPc);
            if (c != 0) return c;
            return left.Index.CompareTo(right.Index);
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

        internal static ImmutableArray<CfgEdge> BuildImplicitExceptionEdges(ImmutableArray<GenTreeBlock> blocks, ImmutableArray<CfgExceptionRegion> exceptionRegions)
        {
            if (blocks.IsDefaultOrEmpty || exceptionRegions.IsDefaultOrEmpty)
                return ImmutableArray<CfgEdge>.Empty;

            var result = ImmutableArray.CreateBuilder<CfgEdge>();
            var seen = new HashSet<CfgEdge>();

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                if (!BlockMayThrow(block))
                    continue;

                for (int r = 0; r < exceptionRegions.Length; r++)
                {
                    var region = exceptionRegions[r];
                    if (!region.ContainsTryBlock(block.Id))
                        continue;

                    var edge = new CfgEdge(block.Id, region.HandlerStartBlockId, CfgEdgeKind.Exception, region.Index);
                    if (seen.Add(edge))
                        result.Add(edge);
                }
            }

            result.Sort(CompareEdges);
            return result.ToImmutable();
        }

        internal static bool BlockMayThrow(GenTreeBlock block)
        {
            var statements = block.Statements;
            for (int i = 0; i < statements.Length; i++)
            {
                if (TreeMayThrow(statements[i]))
                    return true;
            }
            return false;
        }

        internal static bool TreeMayThrow(GenTree node)
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

            if (TryGetConditionalTransfer(block.Statements, out var conditional, out _))
            {
                if (conditional.TargetBlockId == successorBlockId)
                    return conditional.Kind == GenTreeKind.BranchTrue ? CfgEdgeKind.BranchTrue : CfgEdgeKind.BranchFalse;

                return CfgEdgeKind.FallThrough;
            }

            var last = block.Statements[block.Statements.Length - 1];
            return last.Kind == GenTreeKind.Branch ? CfgEdgeKind.Branch : CfgEdgeKind.FallThrough;
        }

        private static bool TryGetConditionalTransfer(
            ImmutableArray<GenTree> statements,
            out GenTree conditional,
            out GenTree? appendedFallThrough)
        {
            conditional = null!;
            appendedFallThrough = null;

            if (statements.IsDefaultOrEmpty)
                return false;

            var last = statements[statements.Length - 1];
            if (last.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                conditional = last;
                return true;
            }

            if (last.Kind == GenTreeKind.Branch && statements.Length >= 2)
            {
                var previous = statements[statements.Length - 2];
                if (previous.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                {
                    conditional = previous;
                    appendedFallThrough = last;
                    return true;
                }
            }

            return false;
        }
        private static ImmutableArray<int> ComputeDominatorRoots(ImmutableArray<CfgBlock> blocks, ImmutableArray<CfgExceptionRegion> exceptionRegions)
        {
            var roots = new SortedSet<int>();
            if (blocks.Length != 0)
                roots.Add(0);
            for (int i = 0; i < exceptionRegions.Length; i++)
            {
                int handler = exceptionRegions[i].HandlerStartBlockId;
                if ((uint)handler < (uint)blocks.Length)
                    roots.Add(handler);
            }
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Predecessors.Length == 0)
                    roots.Add(i);
            }
            return roots.ToImmutableArray();
        }
        private static List<int> ComputeReversePostOrder(ImmutableArray<CfgBlock> blocks, ImmutableArray<int> roots)
        {
            var visited = new bool[blocks.Length];
            var post = new List<int>(blocks.Length);

            for (int i = 0; i < roots.Length; i++)
                Dfs(roots[i]);

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

        private static int[] ComputeImmediateDominators(ImmutableArray<CfgBlock> blocks, List<int> rpo, ImmutableArray<int> roots)
        {
            int n = blocks.Length;
            var rpoIndex = new int[n];
            for (int i = 0; i < rpoIndex.Length; i++)
                rpoIndex[i] = int.MaxValue;
            for (int i = 0; i < rpo.Count; i++)
                rpoIndex[rpo[i]] = i;
            var rootSet = new HashSet<int>();
            for (int i = 0; i < roots.Length; i++)
            {
                int root = roots[i];
                if ((uint)root < (uint)n)
                    rootSet.Add(root);
            }

            var idom = new int[n];
            for (int i = 0; i < idom.Length; i++)
                idom[i] = -1;

            foreach (int root in rootSet)
                idom[root] = VirtualRootBlockId;

            if (n == 0)
                return idom;

            bool changed;
            do
            {
                changed = false;

                for (int r = 0; r < rpo.Count; r++)
                {
                    int b = rpo[r];
                    if (rootSet.Contains(b))
                        continue;

                    int newIdom = -1;
                    var predecessors = blocks[b].Predecessors;
                    for (int p = 0; p < predecessors.Length; p++)
                    {
                        int pred = predecessors[p].FromBlockId;
                        if ((uint)pred >= (uint)n || idom[pred] == -1)
                            continue;

                        newIdom = newIdom == -1
                            ? pred
                            : Intersect(pred, newIdom, idom, rpoIndex);
                    }

                    if (newIdom == -1)
                    {
                        newIdom = VirtualRootBlockId;
                        rootSet.Add(b);
                    }

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
                if (finger1 == VirtualRootBlockId || finger2 == VirtualRootBlockId)
                    return VirtualRootBlockId;
                while (finger1 != VirtualRootBlockId && finger2 != VirtualRootBlockId && rpoIndex[finger1] > rpoIndex[finger2])
                    finger1 = idom[finger1];
                while (finger1 != VirtualRootBlockId && finger2 != VirtualRootBlockId && rpoIndex[finger2] > rpoIndex[finger1])
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
                int stop = idom[b];
                for (int p = 0; p < predecessors.Length; p++)
                {
                    int runner = predecessors[p].FromBlockId;
                    while ((uint)runner < (uint)blocks.Length && runner != stop)
                    {
                        frontier[runner].Add(b);
                        int next = idom[runner];
                        if (next == VirtualRootBlockId || next < 0 || next == runner)
                            break;
                        runner = next;
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
            if (blocks.IsDefaultOrEmpty)
                return ImmutableArray<CfgLoop>.Empty;

            var byHeader = new Dictionary<int, LoopAccumulator>();

            for (int latch = 0; latch < blocks.Length; latch++)
            {
                var successors = blocks[latch].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    var edge = successors[i];
                    if (edge.Kind == CfgEdgeKind.Exception)
                        continue;

                    int header = edge.ToBlockId;
                    if ((uint)header >= (uint)blocks.Length)
                        continue;

                    if (!Dominates(header, latch, idom))
                        continue;

                    if (!byHeader.TryGetValue(header, out var loop))
                    {
                        loop = new LoopAccumulator(header);
                        byHeader.Add(header, loop);
                    }

                    loop.Latches.Add(latch);
                    AddNaturalLoopBody(blocks, header, latch, loop.Body);
                }
            }

            if (byHeader.Count == 0)
                return ImmutableArray<CfgLoop>.Empty;

            var loops = new List<LoopShape>(byHeader.Count);
            foreach (var pair in byHeader)
            {
                var accumulator = pair.Value;
                var body = ToImmutableSorted(accumulator.Body);
                var latches = ToImmutableSorted(accumulator.Latches);
                var entryBlocks = new SortedSet<int>();
                var exitBlocks = new SortedSet<int>();
                var exitDestinations = new SortedSet<int>();
                bool reducible = body.Length != 0 && latches.Length != 0;
                bool headerHasExceptionalSuccessorInsideLoop = false;

                for (int i = 0; i < body.Length; i++)
                {
                    int blockId = body[i];
                    var block = blocks[blockId];
                    if (!Dominates(accumulator.Header, blockId, idom))
                        reducible = false;

                    var predecessors = block.Predecessors;
                    for (int p = 0; p < predecessors.Length; p++)
                    {
                        var pred = predecessors[p];
                        if ((uint)pred.FromBlockId >= (uint)blocks.Length)
                            continue;

                        if (!accumulator.Body.Contains(pred.FromBlockId))
                        {
                            entryBlocks.Add(blockId);
                            if (blockId != accumulator.Header)
                                reducible = false;
                        }
                    }

                    var successors = block.Successors;
                    for (int s = 0; s < successors.Length; s++)
                    {
                        var succ = successors[s];
                        if ((uint)succ.ToBlockId >= (uint)blocks.Length)
                            continue;

                        bool succInLoop = accumulator.Body.Contains(succ.ToBlockId);
                        if (!succInLoop)
                        {
                            exitBlocks.Add(blockId);
                            exitDestinations.Add(succ.ToBlockId);
                        }
                        else if (blockId == accumulator.Header && succ.Kind == CfgEdgeKind.Exception)
                        {
                            headerHasExceptionalSuccessorInsideLoop = true;
                        }
                    }
                }

                int preheader = ComputePreheader(blocks, accumulator.Header, accumulator.Body);
                bool canonicalPreheader = preheader >= 0 && HeaderHasSingleOutsidePredecessor(blocks, accumulator.Header, accumulator.Body) && BlockAlwaysFallsInto(blocks, preheader, accumulator.Header);

                loops.Add(new LoopShape(
                    accumulator.Header,
                    latches,
                    body,
                    ToImmutableSorted(entryBlocks),
                    ToImmutableSorted(exitBlocks),
                    ToImmutableSorted(exitDestinations),
                    preheader,
                    reducible,
                    canonicalPreheader,
                    headerHasExceptionalSuccessorInsideLoop));
            }

            loops.Sort(static (a, b) =>
            {
                int c = a.Header.CompareTo(b.Header);
                if (c != 0) return c;
                c = a.Body.Length.CompareTo(b.Body.Length);
                if (c != 0) return c;
                return a.Latches.Length.CompareTo(b.Latches.Length);
            });

            var parentIndexes = new int[loops.Count];
            var depths = new int[loops.Count];
            for (int i = 0; i < parentIndexes.Length; i++)
                parentIndexes[i] = -1;

            for (int i = 0; i < loops.Count; i++)
            {
                int bestParent = -1;
                int bestSize = int.MaxValue;
                for (int j = 0; j < loops.Count; j++)
                {
                    if (i == j)
                        continue;
                    if (loops[j].Body.Length >= bestSize)
                        continue;
                    if (IsStrictSuperset(loops[j].Body, loops[i].Body))
                    {
                        bestParent = j;
                        bestSize = loops[j].Body.Length;
                    }
                }
                parentIndexes[i] = bestParent;
            }

            for (int i = 0; i < depths.Length; i++)
                depths[i] = ComputeLoopDepth(i, parentIndexes, new bool[depths.Length]);

            var result = ImmutableArray.CreateBuilder<CfgLoop>(loops.Count);
            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                result.Add(new CfgLoop(
                    i,
                    loop.Header,
                    loop.Latches,
                    loop.Body,
                    loop.EntryBlocks,
                    loop.ExitBlocks,
                    loop.ExitDestinationBlocks,
                    loop.Preheader,
                    parentIndexes[i],
                    depths[i],
                    loop.IsReducible,
                    loop.IsCanonicalPreheader,
                    loop.HeaderHasExceptionalSuccessorInsideLoop));
            }

            return result.ToImmutable();
        }

        private sealed class LoopAccumulator
        {
            public readonly int Header;
            public readonly SortedSet<int> Latches = new();
            public readonly SortedSet<int> Body = new();

            public LoopAccumulator(int header)
            {
                Header = header;
                Body.Add(header);
            }
        }

        private readonly struct LoopShape
        {
            public readonly int Header;
            public readonly ImmutableArray<int> Latches;
            public readonly ImmutableArray<int> Body;
            public readonly ImmutableArray<int> EntryBlocks;
            public readonly ImmutableArray<int> ExitBlocks;
            public readonly ImmutableArray<int> ExitDestinationBlocks;
            public readonly int Preheader;
            public readonly bool IsReducible;
            public readonly bool IsCanonicalPreheader;
            public readonly bool HeaderHasExceptionalSuccessorInsideLoop;

            public LoopShape(
                int header,
                ImmutableArray<int> latches,
                ImmutableArray<int> body,
                ImmutableArray<int> entryBlocks,
                ImmutableArray<int> exitBlocks,
                ImmutableArray<int> exitDestinationBlocks,
                int preheader,
                bool isReducible,
                bool isCanonicalPreheader,
                bool headerHasExceptionalSuccessorInsideLoop)
            {
                Header = header;
                Latches = latches;
                Body = body;
                EntryBlocks = entryBlocks;
                ExitBlocks = exitBlocks;
                ExitDestinationBlocks = exitDestinationBlocks;
                Preheader = preheader;
                IsReducible = isReducible;
                IsCanonicalPreheader = isCanonicalPreheader;
                HeaderHasExceptionalSuccessorInsideLoop = headerHasExceptionalSuccessorInsideLoop;
            }
        }

        private static void AddNaturalLoopBody(ImmutableArray<CfgBlock> blocks, int header, int latch, SortedSet<int> body)
        {
            body.Add(header);
            body.Add(latch);

            var stack = new Stack<int>();
            if (latch != header)
                stack.Push(latch);

            while (stack.Count != 0)
            {
                int blockId = stack.Pop();
                if ((uint)blockId >= (uint)blocks.Length)
                    continue;

                var predecessors = blocks[blockId].Predecessors;
                for (int p = 0; p < predecessors.Length; p++)
                {
                    var edge = predecessors[p];
                    if (edge.Kind == CfgEdgeKind.Exception)
                        continue;

                    int pred = edge.FromBlockId;
                    if ((uint)pred >= (uint)blocks.Length)
                        continue;

                    if (body.Add(pred) && pred != header)
                        stack.Push(pred);
                }
            }
        }

        private static ImmutableArray<int> ToImmutableSorted(SortedSet<int> values)
        {
            if (values.Count == 0)
                return ImmutableArray<int>.Empty;

            var builder = ImmutableArray.CreateBuilder<int>(values.Count);
            foreach (int value in values)
                builder.Add(value);
            return builder.ToImmutable();
        }

        private static int ComputePreheader(ImmutableArray<CfgBlock> blocks, int header, SortedSet<int> body)
        {
            int preheader = -1;
            var predecessors = blocks[header].Predecessors;
            for (int i = 0; i < predecessors.Length; i++)
            {
                var edge = predecessors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;

                int pred = edge.FromBlockId;
                if ((uint)pred >= (uint)blocks.Length || body.Contains(pred))
                    continue;

                if (preheader >= 0 && preheader != pred)
                    return -1;
                preheader = pred;
            }
            return preheader;
        }

        private static bool HeaderHasSingleOutsidePredecessor(ImmutableArray<CfgBlock> blocks, int header, SortedSet<int> body)
            => ComputePreheader(blocks, header, body) >= 0;

        private static bool BlockAlwaysFallsInto(ImmutableArray<CfgBlock> blocks, int blockId, int target)
        {
            if ((uint)blockId >= (uint)blocks.Length)
                return false;

            var successors = blocks[blockId].Successors;
            int normalSuccessors = 0;
            for (int i = 0; i < successors.Length; i++)
            {
                if (successors[i].Kind == CfgEdgeKind.Exception)
                    continue;
                normalSuccessors++;
                if (successors[i].ToBlockId != target)
                    return false;
            }
            return normalSuccessors == 1;
        }

        private static bool IsStrictSuperset(ImmutableArray<int> possibleParent, ImmutableArray<int> child)
        {
            if (possibleParent.Length <= child.Length)
                return false;

            int pi = 0;
            for (int ci = 0; ci < child.Length; ci++)
            {
                int value = child[ci];
                while (pi < possibleParent.Length && possibleParent[pi] < value)
                    pi++;
                if (pi >= possibleParent.Length || possibleParent[pi] != value)
                    return false;
            }
            return true;
        }

        private static int ComputeLoopDepth(int loopIndex, int[] parentIndexes, bool[] visiting)
        {
            if ((uint)loopIndex >= (uint)parentIndexes.Length)
                return 0;
            if (parentIndexes[loopIndex] < 0)
                return 1;
            if (visiting[loopIndex])
                return 1;

            visiting[loopIndex] = true;
            int depth = 1 + ComputeLoopDepth(parentIndexes[loopIndex], parentIndexes, visiting);
            visiting[loopIndex] = false;
            return depth;
        }
    }

    internal static class EhFuncletLayout
    {
        public static ImmutableArray<int> ComputeVmRegionOrder(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (cfg.ExceptionRegions.Length == 0)
                return ImmutableArray<int>.Empty;

            var childrenByHandler = new Dictionary<int, List<int>>();
            var topLevel = new List<int>();
            for (int i = 0; i < cfg.ExceptionRegions.Length; i++)
            {
                var region = cfg.ExceptionRegions[i];
                if (region.EnclosingHandlerIndex >= 0)
                {
                    if (!childrenByHandler.TryGetValue(region.EnclosingHandlerIndex, out var children))
                    {
                        children = new List<int>();
                        childrenByHandler.Add(region.EnclosingHandlerIndex, children);
                    }
                    children.Add(region.Index);
                }
                else
                {
                    topLevel.Add(region.Index);
                }
            }

            topLevel.Sort((left, right) => CompareFuncletRegionOrder(cfg.ExceptionRegions[left], cfg.ExceptionRegions[right]));
            foreach (var kv in childrenByHandler)
                kv.Value.Sort((left, right) => CompareFuncletRegionOrder(cfg.ExceptionRegions[left], cfg.ExceptionRegions[right]));

            var result = ImmutableArray.CreateBuilder<int>(cfg.ExceptionRegions.Length);
            var emitted = new bool[cfg.ExceptionRegions.Length];

            for (int i = 0; i < topLevel.Count; i++)
                EmitRegion(topLevel[i]);

            for (int i = 0; i < cfg.ExceptionRegions.Length; i++)
                EmitRegion(i);

            return result.ToImmutable();

            void EmitRegion(int regionIndex)
            {
                if ((uint)regionIndex >= (uint)cfg.ExceptionRegions.Length || emitted[regionIndex])
                    return;

                var region = cfg.ExceptionRegions[regionIndex];
                if (region.EnclosingHandlerIndex >= 0 && !emitted[region.EnclosingHandlerIndex])
                    EmitRegion(region.EnclosingHandlerIndex);

                if (emitted[regionIndex])
                    return;

                emitted[regionIndex] = true;
                result.Add(regionIndex);

                if (!childrenByHandler.TryGetValue(regionIndex, out var children))
                    return;

                for (int i = 0; i < children.Count; i++)
                    EmitRegion(children[i]);
            }
        }

        private static int CompareFuncletRegionOrder(CfgExceptionRegion left, CfgExceptionRegion right)
        {
            if (left.Index == right.Index)
                return 0;

            if (left.HasSameTry(right))
                return left.SourceHandlerIndex.CompareTo(right.SourceHandlerIndex);

            int c = left.TryStartPc.CompareTo(right.TryStartPc);
            if (c != 0) return c;
            c = right.TryEndPc.CompareTo(left.TryEndPc);
            if (c != 0) return c;
            c = left.HandlerStartPc.CompareTo(right.HandlerStartPc);
            if (c != 0) return c;
            return left.SourceHandlerIndex.CompareTo(right.SourceHandlerIndex);
        }


        public static ImmutableArray<CfgEdge> BuildImplicitExceptionEdges(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (cfg.ExceptionRegions.Length == 0 || cfg.Blocks.Length == 0)
                return ImmutableArray<CfgEdge>.Empty;

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(cfg.Blocks.Length);
            for (int i = 0; i < cfg.Blocks.Length; i++)
                blocks.Add(cfg.Blocks[i].SourceBlock);

            return ControlFlowGraph.BuildImplicitExceptionEdges(blocks.ToImmutable(), cfg.ExceptionRegions);
        }

        public static bool HasPotentialExceptionEdgeToHandler(ControlFlowGraph cfg, CfgExceptionRegion region)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            int handler = region.HandlerStartBlockId;
            if ((uint)handler >= (uint)cfg.Blocks.Length)
                return false;

            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                var block = cfg.Blocks[b];
                if (!ContainsRegion(block.TryRegionIndexes, region.Index))
                    continue;
                if (ControlFlowGraph.BlockMayThrow(block.SourceBlock))
                    return true;
            }

            return false;
        }

        private static bool ContainsRegion(ImmutableArray<int> regions, int regionIndex)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i] == regionIndex)
                    return true;
            }
            return false;
        }

        public static ImmutableArray<int> ComputeLinearBlockOrder(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var seen = new bool[cfg.Blocks.Length];

            void Add(int blockId)
            {
                if ((uint)blockId >= (uint)cfg.Blocks.Length)
                    throw new InvalidOperationException($"CFG block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    return;
                seen[blockId] = true;
                result.Add(blockId);
            }

            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                if (GetHandlerOwnerRegionIndex(cfg.ExceptionRegions, b) < 0)
                    Add(b);
            }

            var regionOrder = ComputeVmRegionOrder(cfg);
            for (int i = 0; i < regionOrder.Length; i++)
            {
                var region = cfg.ExceptionRegions[regionOrder[i]];
                var blocks = BuildFuncletBlockIds(cfg, region);
                for (int b = 0; b < blocks.Length; b++)
                    Add(blocks[b]);
            }

            for (int b = 0; b < cfg.Blocks.Length; b++)
                Add(b);

            return result.ToImmutable();
        }

        public static ImmutableArray<int> BuildTryBlockIds(ControlFlowGraph cfg, CfgExceptionRegion region)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            int owner = GetHandlerOwnerRegionIndex(cfg.ExceptionRegions, region.TryStartBlockId);
            var result = ImmutableArray.CreateBuilder<int>();
            var order = GetBlockIterationOrder(cfg);
            for (int i = 0; i < order.Length; i++)
            {
                int blockId = order[i];
                if (blockId < region.TryStartBlockId || blockId >= region.TryEndBlockIdExclusive)
                    continue;
                if (GetHandlerOwnerRegionIndex(cfg.ExceptionRegions, blockId) == owner)
                    result.Add(blockId);
            }

            return result.ToImmutable();
        }

        public static ImmutableArray<int> BuildRootBlockIds(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var order = GetBlockIterationOrder(cfg);
            for (int i = 0; i < order.Length; i++)
            {
                int blockId = order[i];
                if (GetHandlerOwnerRegionIndex(cfg.ExceptionRegions, blockId) < 0)
                    result.Add(blockId);
            }
            return result.ToImmutable();
        }

        public static ImmutableArray<int> BuildFuncletBlockIds(ControlFlowGraph cfg, CfgExceptionRegion region)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            var result = ImmutableArray.CreateBuilder<int>();
            var order = GetBlockIterationOrder(cfg);
            for (int i = 0; i < order.Length; i++)
            {
                int blockId = order[i];
                if (blockId < region.HandlerStartBlockId || blockId >= region.HandlerEndBlockIdExclusive)
                    continue;
                if (GetHandlerOwnerRegionIndex(cfg.ExceptionRegions, blockId) == region.Index)
                    result.Add(blockId);
            }

            return result.ToImmutable();
        }

        private static ImmutableArray<int> GetBlockIterationOrder(ControlFlowGraph cfg)
        {
            var linearOrder = cfg.Method.LinearBlockOrder;
            if (!linearOrder.IsDefaultOrEmpty && linearOrder.Length == cfg.Blocks.Length)
                return linearOrder;

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            for (int i = 0; i < cfg.Blocks.Length; i++)
                result.Add(cfg.Blocks[i].Id);
            return result.ToImmutable();
        }

        public static int GetHandlerOwnerRegionIndex(ImmutableArray<CfgExceptionRegion> regions, int blockId)
        {
            int owner = -1;
            int ownerSpan = int.MaxValue;
            int ownerHandlerStart = -1;

            for (int i = 0; i < regions.Length; i++)
            {
                var candidate = regions[i];
                if (!candidate.ContainsHandlerBlock(blockId))
                    continue;

                int span = candidate.HandlerEndBlockIdExclusive - candidate.HandlerStartBlockId;
                if (span < ownerSpan ||
                    (span == ownerSpan && candidate.HandlerStartBlockId >= ownerHandlerStart && candidate.Index > owner))
                {
                    owner = candidate.Index;
                    ownerSpan = span;
                    ownerHandlerStart = candidate.HandlerStartBlockId;
                }
            }

            return owner;
        }
    }

}
