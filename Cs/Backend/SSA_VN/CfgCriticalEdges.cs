using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class GenTreeCriticalEdgeSplitter
    {
        internal readonly struct SplitEdgeInfo
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
            => SplitCriticalEdges(method, canSplitEdge: null);

        public static GenTreeMethod CreateLoopPreheader(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (!loop.IsReducible || loop.IsCanonicalPreheader || loop.Header < 0 || (uint)loop.Header >= (uint)method.Blocks.Length)
                return method;
            if (loop.Entries.Length != 1 || loop.Entries[0] != loop.Header)
                return method;
            if (cfg.Blocks.Length != method.Blocks.Length)
                return method;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (method.Blocks[i].Id != i || cfg.Blocks[i].Id != i)
                    return method;
            }

            var header = method.Blocks[loop.Header];
            var headerCfg = cfg.Blocks[loop.Header];
            bool headerIsTryEntry = IsTryRegionEntry(cfg, loop.Header);
            if (IsHandlerRegionEntry(cfg, loop.Header))
                return method;
            if (headerIsTryEntry)
            {
                for (int i = 0; i < loop.BackEdges.Length; i++)
                {
                    var edge = loop.BackEdges[i];
                    if (edge.Kind == CfgEdgeKind.Exception ||
                        edge.ToBlockId != loop.Header ||
                        (uint)edge.FromBlockId >= (uint)cfg.Blocks.Length ||
                        !SameEhRegion(cfg.Blocks[edge.FromBlockId], headerCfg))
                    {
                        return method;
                    }
                }
            }

            var outsideEdges = new List<CfgEdge>();
            var seenPairs = new HashSet<(int from, int to)>();
            for (int i = 0; i < loop.EntryEdges.Length; i++)
            {
                var edge = loop.EntryEdges[i];
                if (edge.Kind == CfgEdgeKind.Exception || edge.ToBlockId != loop.Header)
                    return method;
                if ((uint)edge.FromBlockId >= (uint)method.Blocks.Length || loop.Contains(edge.FromBlockId))
                    return method;
                if (!headerIsTryEntry && !SameEhRegion(cfg.Blocks[edge.FromBlockId], headerCfg))
                    return method;
                if (seenPairs.Add((edge.FromBlockId, edge.ToBlockId)))
                    outsideEdges.Add(edge);
            }

            if (outsideEdges.Count == 0)
                return method;

            int stackDepth = method.Blocks[outsideEdges[0].FromBlockId].ExitStackDepth;
            if (stackDepth != header.EntryStackDepth)
                return method;
            for (int i = 1; i < outsideEdges.Count; i++)
            {
                if (method.Blocks[outsideEdges[i].FromBlockId].ExitStackDepth != stackDepth)
                    return method;
            }

            int preheaderId = method.Blocks.Length;
            int nextTreeId = NextSyntheticTreeId(method);
            int preheaderPc = FirstSyntheticPc(method);
            var info = new SplitEdgeInfo(preheaderId, preheaderPc);
            var splitInfo = new Dictionary<(int from, int to), SplitEdgeInfo>(outsideEdges.Count);
            for (int i = 0; i < outsideEdges.Count; i++)
                splitInfo.Add((outsideEdges[i].FromBlockId, loop.Header), info);

            var branch = new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                preheaderPc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: header.StartPc,
                targetBlockId: header.Id);
            GenTreeBlockFlags preheaderFlags;
            if (headerIsTryEntry)
            {
                preheaderFlags = header.Flags &
                    (GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
                preheaderFlags |= GenTreeBlockFlags.TryEntry;
                if (stackDepth != 0)
                    preheaderFlags |= GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit;
            }
            else
            {
                preheaderFlags = ComputeSplitBlockFlags(method.Blocks[outsideEdges[0].FromBlockId], header, stackDepth);
            }

            var preheader = new GenTreeBlock(
                preheaderId,
                preheaderPc,
                preheaderPc,
                stackDepth,
                stackDepth,
                GenTreeBlockJumpKind.Always,
                preheaderFlags,
                ImmutableArray.Create(branch),
                ImmutableArray.Create(header.Id),
                ImmutableArray.Create(header.StartPc),
                header.RegionPc);

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length + 1);
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (i == loop.Header)
                    blocks.Add(preheader);
                var rewritten = RewriteOriginalBlock(method.Blocks[i], splitInfo, ref nextTreeId);
                if (i == loop.Header && headerIsTryEntry)
                    rewritten = CloneBlockWithFlags(rewritten, rewritten.Flags & ~GenTreeBlockFlags.TryEntry);
                blocks.Add(rewritten);
            }

            var rewrittenMethod = RenumberBlocks(method, blocks.ToImmutable());
            VerifyLoopPreheader(
                rewrittenMethod,
                preheaderPc,
                header.StartPc,
                headerIsTryEntry);
            return rewrittenMethod;
        }

        public static GenTreeMethod CreateLoopLatch(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (!loop.IsReducible || loop.BackEdges.Length <= 1 || (uint)loop.Header >= (uint)method.Blocks.Length)
                return method;
            if (cfg.Blocks.Length != method.Blocks.Length || IsExceptionRegionEntry(cfg, loop.Header))
                return method;

            var header = method.Blocks[loop.Header];
            var headerCfg = cfg.Blocks[loop.Header];
            int stackDepth = header.EntryStackDepth;
            var redirects = new Dictionary<(int from, int to), SplitEdgeInfo>();
            int latchId = method.Blocks.Length;
            int latchPc = FirstSyntheticPc(method);
            var info = new SplitEdgeInfo(latchId, latchPc);

            for (int i = 0; i < loop.BackEdges.Length; i++)
            {
                var edge = loop.BackEdges[i];
                if (edge.Kind == CfgEdgeKind.Exception || edge.ToBlockId != loop.Header || !loop.Contains(edge.FromBlockId))
                    return method;
                if ((uint)edge.FromBlockId >= (uint)method.Blocks.Length)
                    return method;
                if (method.Blocks[edge.FromBlockId].ExitStackDepth != stackDepth ||
                    !SameEhRegion(cfg.Blocks[edge.FromBlockId], headerCfg))
                {
                    return method;
                }
                redirects[(edge.FromBlockId, edge.ToBlockId)] = info;
            }

            if (redirects.Count == 0)
                return method;

            int nextTreeId = NextSyntheticTreeId(method);
            var branch = new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                latchPc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: header.StartPc,
                targetBlockId: header.Id);
            var firstSource = method.Blocks[loop.BackEdges[0].FromBlockId];
            var latch = new GenTreeBlock(
                latchId,
                latchPc,
                latchPc,
                stackDepth,
                stackDepth,
                GenTreeBlockJumpKind.Always,
                ComputeSplitBlockFlags(firstSource, header, stackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(header.Id),
                ImmutableArray.Create(header.StartPc),
                header.RegionPc);

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length + 1);
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (i == loop.Header)
                    blocks.Add(latch);
                blocks.Add(RewriteOriginalBlock(method.Blocks[i], redirects, ref nextTreeId));
            }
            return RenumberBlocks(method, blocks.ToImmutable());
        }

        public static GenTreeMethod CreateLoopExit(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop, int exitBlockId)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (!loop.IsReducible || (uint)exitBlockId >= (uint)method.Blocks.Length || loop.Contains(exitBlockId))
                return method;
            if (cfg.Blocks.Length != method.Blocks.Length || IsExceptionRegionEntry(cfg, exitBlockId))
                return method;

            var exit = method.Blocks[exitBlockId];
            var exitCfg = cfg.Blocks[exitBlockId];
            bool hasLoopPredecessor = false;
            bool hasOutsidePredecessor = false;
            var loopPredecessors = new HashSet<int>();
            for (int i = 0; i < exitCfg.Predecessors.Length; i++)
            {
                var edge = exitCfg.Predecessors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (loop.Contains(edge.FromBlockId))
                {
                    hasLoopPredecessor = true;
                    loopPredecessors.Add(edge.FromBlockId);
                }
                else
                {
                    hasOutsidePredecessor = true;
                }
            }

            if (!hasLoopPredecessor || !hasOutsidePredecessor)
                return method;

            int stackDepth = exit.EntryStackDepth;
            foreach (int predecessor in loopPredecessors)
            {
                if ((uint)predecessor >= (uint)method.Blocks.Length ||
                    method.Blocks[predecessor].ExitStackDepth != stackDepth ||
                    !SameEhRegion(cfg.Blocks[predecessor], exitCfg))
                {
                    return method;
                }
            }

            int newExitId = method.Blocks.Length;
            int newExitPc = FirstSyntheticPc(method);
            var info = new SplitEdgeInfo(newExitId, newExitPc);
            var redirects = new Dictionary<(int from, int to), SplitEdgeInfo>(loopPredecessors.Count);
            foreach (int predecessor in loopPredecessors)
                redirects.Add((predecessor, exitBlockId), info);

            int nextTreeId = NextSyntheticTreeId(method);
            var branch = new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                newExitPc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: exit.StartPc,
                targetBlockId: exit.Id);
            int firstPredecessor = int.MaxValue;
            foreach (int predecessor in loopPredecessors)
                firstPredecessor = Math.Min(firstPredecessor, predecessor);
            var firstSource = method.Blocks[firstPredecessor];
            var newExit = new GenTreeBlock(
                newExitId,
                newExitPc,
                newExitPc,
                stackDepth,
                stackDepth,
                GenTreeBlockJumpKind.Always,
                ComputeSplitBlockFlags(firstSource, exit, stackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(exit.Id),
                ImmutableArray.Create(exit.StartPc),
                exit.RegionPc);

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length + 1);
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (i == exitBlockId)
                    blocks.Add(newExit);
                blocks.Add(RewriteOriginalBlock(method.Blocks[i], redirects, ref nextTreeId));
            }
            return RenumberBlocks(method, blocks.ToImmutable());
        }

        public static GenTreeMethod SplitCriticalEdges(GenTreeMethod method, Func<CfgEdge, bool>? canSplitEdge)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (method.Blocks.Length == 0)
                return method;

            var splitEdges = FindCriticalNormalEdges(method, canSplitEdge);
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

            var splitEdgesByDestination = new Dictionary<int, List<CfgEdge>>();
            for (int i = 0; i < splitEdges.Count; i++)
            {
                var edge = splitEdges[i];
                if (!splitEdgesByDestination.TryGetValue(edge.ToBlockId, out var edges))
                {
                    edges = new List<CfgEdge>();
                    splitEdgesByDestination.Add(edge.ToBlockId, edges);
                }
                edges.Add(edge);
            }

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(nextBlockId);
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (splitEdgesByDestination.TryGetValue(i, out var incomingEdges))
                {
                    for (int e = 0; e < incomingEdges.Count; e++)
                    {
                        var edge = incomingEdges[e];
                        var info = splitInfo[(edge.FromBlockId, edge.ToBlockId)];
                        blocks.Add(CreateSplitBlock(
                            info,
                            method.Blocks[edge.FromBlockId],
                            method.Blocks[edge.ToBlockId],
                            ref nextTreeId));
                    }
                }

                blocks.Add(RewriteOriginalBlock(method.Blocks[i], splitInfo, ref nextTreeId));
            }

            return RenumberBlocks(method, blocks.ToImmutable());
        }

        private static List<CfgEdge> FindCriticalNormalEdges(GenTreeMethod method, Func<CfgEdge, bool>? canSplitEdge)
        {
            int n = method.Blocks.Length;
            var successorCounts = new int[n];
            var predecessorCounts = new int[n];
            var seenEdges = new HashSet<CfgEdge>();

            for (int b = 0; b < n; b++)
            {
                var block = method.Blocks[b];
                if (block.Id != b)
                    throw new InvalidOperationException($"Critical edge splitting requires dense block ids. B{b} expected, found B{block.Id}.");
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    int to = block.SuccessorBlockIds[s];
                    if ((uint)to >= (uint)n)
                        throw new InvalidOperationException($"Invalid CFG edge B{block.Id} -> B{to}.");

                    var edge = new CfgEdge(block.Id, to, ClassifyNormalEdge(block, to));
                    if (seenEdges.Add(edge))
                    {
                        successorCounts[block.Id]++;
                        predecessorCounts[to]++;
                    }
                }
            }

            var result = new List<CfgEdge>();
            var seenPairs = new HashSet<(int from, int to)>();

            for (int b = 0; b < n; b++)
            {
                if (successorCounts[b] <= 1)
                    continue;
                var block = method.Blocks[b];
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    int to = block.SuccessorBlockIds[s];
                    if (predecessorCounts[to] <= 1)
                        continue;
                    var edge = new CfgEdge(block.Id, to, ClassifyNormalEdge(block, to));
                    if (canSplitEdge is not null && !canSplitEdge(edge))
                        continue;
                    if (seenPairs.Add((edge.FromBlockId, edge.ToBlockId)))
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

        internal static bool TryGetConditionalTransfer(
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
        internal static GenTreeBlock RewriteOriginalBlock(
            GenTreeBlock block,
            Dictionary<(int from, int to), SplitEdgeInfo> splitInfo,
            ref int nextTreeId)
        {
            var successors = ImmutableArray.CreateBuilder<int>(block.SuccessorBlockIds.Length);
            var successorPcs = ImmutableArray.CreateBuilder<int>(block.SuccessorBlockIds.Length);
            bool successorChanged = false;

            for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
            {
                int successor = block.SuccessorBlockIds[i];
                if (splitInfo.TryGetValue((block.Id, successor), out var info))
                {
                    successors.Add(info.SplitBlockId);
                    successorPcs.Add(info.SplitPc);
                    successorChanged = true;
                }
                else
                {
                    successors.Add(successor);
                    successorPcs.Add(i < block.SuccessorPcs.Length ? block.SuccessorPcs[i] : -1);
                }
            }

            var statements = block.Statements;
            var jumpKind = block.JumpKind;
            if (statements.Length != 0 &&
                TryRewriteBlockStatements(block, statements, splitInfo, ref nextTreeId, out var rewrittenStatements))
            {
                statements = rewrittenStatements;
            }
            else if (successorChanged)
            {
                if (successors.Count == 1 &&
                    block.JumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough or GenTreeBlockJumpKind.Always)
                {
                    var branch = new GenTree(
                        nextTreeId++,
                        GenTreeKind.Branch,
                        block.EndPcExclusive,
                        BytecodeOp.Br,
                        type: null,
                        stackKind: GenStackKind.Void,
                        flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                        operands: ImmutableArray<GenTree>.Empty,
                        targetPc: successorPcs[0],
                        targetBlockId: successors[0]);
                    statements = statements.Add(branch);
                    jumpKind = GenTreeBlockJumpKind.Always;
                }
                else
                {
                    throw new InvalidOperationException($"Cannot rewrite CFG transfer in B{block.Id}.");
                }
            }

            return new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                jumpKind,
                block.Flags,
                statements,
                successors.ToImmutable(),
                successorPcs.ToImmutable(),
                block.RegionPc);
        }

        private static bool TryRewriteBlockStatements(
            GenTreeBlock block,
            ImmutableArray<GenTree> statements,
            Dictionary<(int from, int to), SplitEdgeInfo> splitInfo,
            ref int nextTreeId,
            out ImmutableArray<GenTree> rewrittenStatements)
        {
            rewrittenStatements = statements;

            if (TryGetConditionalTransfer(statements, out var conditional, out var appendedFallThrough))
            {
                int conditionalIndex = appendedFallThrough is null ? statements.Length - 1 : statements.Length - 2;
                int appendedIndex = appendedFallThrough is null ? -1 : statements.Length - 1;
                GenTree rewrittenConditional = conditional;
                GenTree? rewrittenAppended = appendedFallThrough;
                GenTree? appendedBranch = null;
                bool changed = false;

                if (conditional.TargetBlockId >= 0 &&
                    splitInfo.TryGetValue((block.Id, conditional.TargetBlockId), out var conditionalInfo))
                {
                    rewrittenConditional = CloneWithTarget(conditional, conditional.Kind, conditional.SourceOp, conditionalInfo.SplitPc, conditionalInfo.SplitBlockId);
                    changed = true;
                }

                if (appendedFallThrough is not null)
                {
                    if (appendedFallThrough.TargetBlockId >= 0 &&
                        splitInfo.TryGetValue((block.Id, appendedFallThrough.TargetBlockId), out var appendedInfo))
                    {
                        rewrittenAppended = CloneWithTarget(appendedFallThrough, appendedFallThrough.Kind, appendedFallThrough.SourceOp, appendedInfo.SplitPc, appendedInfo.SplitBlockId);
                        changed = true;
                    }
                }
                else
                {
                    for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                    {
                        int successor = block.SuccessorBlockIds[i];
                        if (successor == conditional.TargetBlockId)
                            continue;

                        if (splitInfo.TryGetValue((block.Id, successor), out var fallThroughInfo))
                        {
                            appendedBranch = CreateBranchToSplit(conditional, fallThroughInfo, ref nextTreeId);
                            changed = true;
                            break;
                        }
                    }
                }

                if (!changed)
                    return false;

                var builder = ImmutableArray.CreateBuilder<GenTree>(statements.Length + (appendedBranch is null ? 0 : 1));
                for (int i = 0; i < statements.Length; i++)
                {
                    if (i == conditionalIndex)
                        builder.Add(rewrittenConditional);
                    else if (i == appendedIndex && rewrittenAppended is not null)
                        builder.Add(rewrittenAppended);
                    else
                        builder.Add(statements[i]);
                }

                if (appendedBranch is not null)
                    builder.Add(appendedBranch);

                rewrittenStatements = builder.ToImmutable();
                return true;
            }

            var last = statements[statements.Length - 1];
            if (!TryRewriteTerminator(block, last, splitInfo, ref nextTreeId, out var rewrittenLast, out var appendedBranchForTerminator))
                return false;

            var rewritten = ImmutableArray.CreateBuilder<GenTree>(statements.Length + (appendedBranchForTerminator is null ? 0 : 1));
            for (int i = 0; i + 1 < statements.Length; i++)
                rewritten.Add(statements[i]);
            rewritten.Add(rewrittenLast);
            if (appendedBranchForTerminator is not null)
                rewritten.Add(appendedBranchForTerminator);
            rewrittenStatements = rewritten.ToImmutable();
            return true;
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
                ComputeSplitBlockFlags(from, to, from.ExitStackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(to.Id),
                ImmutableArray.Create(to.StartPc),
                to.RegionPc);
        }

        private static GenTreeBlockFlags ComputeSplitBlockFlags(GenTreeBlock predecessor, GenTreeBlock successor, int stackDepth)
        {
            var flags = predecessor.Flags & successor.Flags & (GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
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

        internal static bool IsExceptionRegionEntry(ControlFlowGraph cfg, int blockId)
            => IsTryRegionEntry(cfg, blockId) || IsHandlerRegionEntry(cfg, blockId);

        internal static bool IsTryRegionEntry(ControlFlowGraph cfg, int blockId)
        {
            for (int i = 0; i < cfg.ExceptionRegions.Length; i++)
            {
                if (cfg.ExceptionRegions[i].TryStartBlockId == blockId)
                    return true;
            }
            return false;
        }

        internal static bool IsHandlerRegionEntry(ControlFlowGraph cfg, int blockId)
        {
            for (int i = 0; i < cfg.ExceptionRegions.Length; i++)
            {
                if (cfg.ExceptionRegions[i].HandlerStartBlockId == blockId)
                    return true;
            }
            return false;
        }

        internal static bool SameEhRegion(CfgBlock left, CfgBlock right)
            => SequenceEqual(left.TryRegionIndexes, right.TryRegionIndexes) &&
               SequenceEqual(left.HandlerRegionIndexes, right.HandlerRegionIndexes);

        private static bool SequenceEqual(ImmutableArray<int> left, ImmutableArray<int> right)
        {
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static GenTreeBlock CloneBlockWithFlags(GenTreeBlock block, GenTreeBlockFlags flags)
            => new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                block.JumpKind,
                flags,
                block.Statements,
                block.SuccessorBlockIds,
                block.SuccessorPcs,
                block.RegionPc);

        private static void VerifyLoopPreheader(
            GenTreeMethod method,
            int preheaderPc,
            int headerPc,
            bool movedTryEntry)
        {
            var cfg = ControlFlowGraph.Build(method);
            int preheaderId = FindUniqueBlockByStartPc(method, preheaderPc);
            int headerId = FindUniqueBlockByStartPc(method, headerPc);
            var preheader = method.Blocks[preheaderId];
            if (preheader.JumpKind != GenTreeBlockJumpKind.Always ||
                preheader.SuccessorBlockIds.Length != 1 ||
                preheader.SuccessorBlockIds[0] != headerId)
            {
                throw new InvalidOperationException("Loop preheader canonicalization produced an invalid transfer.");
            }

            if (!movedTryEntry)
                return;

            if (!IsTryRegionEntry(cfg, preheaderId) ||
                IsTryRegionEntry(cfg, headerId) ||
                !SameEhRegion(cfg.Blocks[preheaderId], cfg.Blocks[headerId]) ||
                (preheader.Flags & GenTreeBlockFlags.TryEntry) == 0 ||
                (method.Blocks[headerId].Flags & GenTreeBlockFlags.TryEntry) != 0)
            {
                throw new InvalidOperationException("Loop preheader canonicalization produced an invalid try-region entry.");
            }
        }

        private static int FindUniqueBlockByStartPc(GenTreeMethod method, int startPc)
        {
            int result = -1;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (method.Blocks[i].StartPc != startPc)
                    continue;
                if (result >= 0)
                    throw new InvalidOperationException($"Duplicate block start PC {startPc}.");
                result = i;
            }
            if (result < 0)
                throw new InvalidOperationException($"Missing block start PC {startPc}.");
            return result;
        }

        internal static GenTreeMethod RenumberBlocks(GenTreeMethod method, ImmutableArray<GenTreeBlock> provisionalBlocks)
        {
            var idMap = new Dictionary<int, int>(provisionalBlocks.Length);
            for (int i = 0; i < provisionalBlocks.Length; i++)
            {
                if (!idMap.TryAdd(provisionalBlocks[i].Id, i))
                    throw new InvalidOperationException($"Duplicate provisional block id B{provisionalBlocks[i].Id}.");
            }

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(provisionalBlocks.Length);
            for (int i = 0; i < provisionalBlocks.Length; i++)
            {
                var block = provisionalBlocks[i];
                var successors = ImmutableArray.CreateBuilder<int>(block.SuccessorBlockIds.Length);
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    if (!idMap.TryGetValue(block.SuccessorBlockIds[s], out int mapped))
                        throw new InvalidOperationException($"Invalid provisional CFG edge B{block.Id} -> B{block.SuccessorBlockIds[s]}.");
                    successors.Add(mapped);
                }

                var statements = ImmutableArray.CreateBuilder<GenTree>(block.Statements.Length);
                for (int s = 0; s < block.Statements.Length; s++)
                    statements.Add(RemapTreeTargets(block.Statements[s], idMap));

                blocks.Add(new GenTreeBlock(
                    i,
                    block.StartPc,
                    block.EndPcExclusive,
                    block.EntryStackDepth,
                    block.ExitStackDepth,
                    block.JumpKind,
                    block.Flags,
                    statements.ToImmutable(),
                    successors.ToImmutable(),
                    block.SuccessorPcs,
                    block.RegionPc));
            }

            return method.CloneWithBlocks(blocks.ToImmutable());
        }

        private static GenTree RemapTreeTargets(GenTree node, IReadOnlyDictionary<int, int> idMap)
        {
            int targetBlockId = node.TargetBlockId;
            bool targetChanged = false;
            if (targetBlockId >= 0)
            {
                if (!idMap.TryGetValue(targetBlockId, out int mappedTarget))
                    throw new InvalidOperationException($"Invalid provisional tree target B{targetBlockId}.");
                targetChanged = mappedTarget != targetBlockId;
                targetBlockId = mappedTarget;
            }

            ImmutableArray<GenTree> operands = node.Operands;
            bool operandsChanged = false;
            if (!node.Operands.IsDefaultOrEmpty)
            {
                var builder = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var operand = RemapTreeTargets(node.Operands[i], idMap);
                    operandsChanged |= !ReferenceEquals(operand, node.Operands[i]);
                    builder.Add(operand);
                }
                if (operandsChanged)
                    operands = builder.ToImmutable();
            }

            if (!targetChanged && !operandsChanged)
                return node;

            var clone = new GenTree(
                node.Id,
                node.Kind,
                node.Pc,
                node.SourceOp,
                node.Type,
                node.StackKind,
                node.Flags,
                operands,
                int32: node.Int32,
                int64: node.Int64,
                text: node.Text,
                runtimeType: node.RuntimeType,
                field: node.Field,
                method: node.Method,
                convKind: node.ConvKind,
                convFlags: node.ConvFlags,
                targetPc: node.TargetPc,
                targetBlockId: targetBlockId,
                boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
            clone.LocalDescriptor = node.LocalDescriptor;
            clone.CseNumber = node.CseNumber;
            return clone;
        }

        internal static int NextSyntheticTreeId(GenTreeMethod method)
        {
            int max = -1;
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Visit(statements[s]);
            }
            return checked(max + 1);

            void Visit(GenTree node)
            {
                if (node.Id > max)
                    max = node.Id;
                for (int i = 0; i < node.Operands.Length; i++)
                    Visit(node.Operands[i]);
            }
        }

        internal static int FirstSyntheticPc(GenTreeMethod method)
        {
            int min = 0;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                min = Math.Min(min, method.Blocks[i].StartPc);
                min = Math.Min(min, method.Blocks[i].EndPcExclusive);
            }
            return checked(min - 1);
        }
    }
}
