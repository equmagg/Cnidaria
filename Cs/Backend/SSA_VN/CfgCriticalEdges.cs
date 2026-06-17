using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
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
            => SplitCriticalEdges(method, canSplitEdge: null);

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
                ComputeSplitBlockFlags(from, to, from.ExitStackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(to.Id),
                ImmutableArray.Create(to.StartPc));
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
}
