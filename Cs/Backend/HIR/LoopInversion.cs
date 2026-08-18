using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    public sealed class LoopInversionOptions
    {
        public static LoopInversionOptions Default { get; } = new LoopInversionOptions();

        public int MaxLoopTreeNodes { get; set; } = 100;
        public int MaxDuplicatedCodeSize { get; set; } = 34;
    }

    internal static class GenTreeLoopInverter
    {
        private readonly struct LoopCandidate
        {
            public readonly ImmutableArray<int> ConditionBlocks;
            public readonly GenTree Conditional;
            public readonly int TrueSuccessor;
            public readonly int FalseSuccessor;
            public readonly int StayInLoopSuccessor;
            public readonly int ExitSuccessor;

            public LoopCandidate(
                ImmutableArray<int> conditionBlocks,
                GenTree conditional,
                int trueSuccessor,
                int falseSuccessor,
                int stayInLoopSuccessor,
                int exitSuccessor)
            {
                ConditionBlocks = conditionBlocks;
                Conditional = conditional;
                TrueSuccessor = trueSuccessor;
                FalseSuccessor = falseSuccessor;
                StayInLoopSuccessor = stayInLoopSuccessor;
                ExitSuccessor = exitSuccessor;
            }
        }

        public static GenTreeMethod InvertLoops(GenTreeMethod method, LoopInversionOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= LoopInversionOptions.Default;
            ValidateOptions(options);

            if (method.Blocks.IsDefaultOrEmpty)
                return method;

            method = CanonicalizeLoops(method);
            int remaining = checked(method.Blocks.Length + 1);

            while (remaining-- > 0)
            {
                var cfg = ControlFlowGraph.Build(method);
                var loops = LoopsInPostOrder(cfg.NaturalLoops);
                bool changed = false;

                for (int i = 0; i < loops.Count; i++)
                {
                    if (!TryInvertLoop(method, cfg, loops[i], options, out var rewritten))
                        continue;

                    method = CanonicalizeLoops(rewritten);
                    changed = true;
                    break;
                }

                if (!changed)
                    break;
            }

            if (remaining < 0)
                throw new InvalidOperationException("Loop inversion did not converge.");

            return method;
        }

        private static void ValidateOptions(LoopInversionOptions options)
        {
            if (options.MaxLoopTreeNodes < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxLoopTreeNodes));
            if (options.MaxDuplicatedCodeSize < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxDuplicatedCodeSize));
        }

        internal static GenTreeMethod CanonicalizeLoops(GenTreeMethod method)
        {
            long blockCount = method.Blocks.Length;
            long remaining = Math.Max(1024L, checked(blockCount * blockCount * 4 + blockCount * 16 + 256));
            while (remaining-- > 0)
            {
                var cfg = ControlFlowGraph.Build(method);
                var reversePostOrder = LoopsInReversePostOrder(cfg.NaturalLoops);
                bool changed = false;

                for (int i = 0; i < reversePostOrder.Count; i++)
                {
                    var loop = reversePostOrder[i];
                    if (!loop.IsReducible || loop.IsCanonicalPreheader || loop.EntryEdges.IsDefaultOrEmpty)
                        continue;
                    if (!AllEntriesTargetHeader(loop))
                        continue;

                    var rewritten = GenTreeCriticalEdgeSplitter.CreateLoopPreheader(method, cfg, loop);
                    if (ReferenceEquals(rewritten, method))
                        continue;
                    method = rewritten;
                    changed = true;
                    break;
                }
                if (changed)
                    continue;

                cfg = ControlFlowGraph.Build(method);
                reversePostOrder = LoopsInReversePostOrder(cfg.NaturalLoops);
                for (int i = 0; i < reversePostOrder.Count; i++)
                {
                    var loop = reversePostOrder[i];
                    if (!loop.IsReducible || loop.BackEdges.Length <= 1)
                        continue;

                    var rewritten = GenTreeCriticalEdgeSplitter.CreateLoopLatch(method, cfg, loop);
                    if (ReferenceEquals(rewritten, method))
                        continue;
                    method = rewritten;
                    changed = true;
                    break;
                }
                if (changed)
                    continue;

                cfg = ControlFlowGraph.Build(method);
                var postOrder = LoopsInPostOrder(cfg.NaturalLoops);
                for (int i = 0; i < postOrder.Count; i++)
                {
                    var loop = postOrder[i];
                    if (!loop.IsReducible)
                        continue;
                    for (int e = 0; e < loop.ExitDestinations.Length; e++)
                    {
                        var rewritten = GenTreeCriticalEdgeSplitter.CreateLoopExit(
                            method,
                            cfg,
                            loop,
                            loop.ExitDestinations[e]);
                        if (ReferenceEquals(rewritten, method))
                            continue;
                        method = rewritten;
                        changed = true;
                        break;
                    }
                    if (changed)
                        break;
                }

                if (!changed)
                    return method;
            }

            throw new InvalidOperationException("Loop canonicalization did not converge.");
        }

        private static List<CfgLoop> LoopsInReversePostOrder(ImmutableArray<CfgLoop> loops)
        {
            var result = new List<CfgLoop>(loops.Length);
            for (int i = 0; i < loops.Length; i++)
                result.Add(loops[i]);

            result.Sort(static (left, right) =>
            {
                int c = left.Depth.CompareTo(right.Depth);
                if (c != 0)
                    return c;
                c = left.Parent.CompareTo(right.Parent);
                if (c != 0)
                    return c;
                return left.Header.CompareTo(right.Header);
            });
            return result;
        }

        private static bool AllEntriesTargetHeader(CfgLoop loop)
        {
            for (int i = 0; i < loop.EntryEdges.Length; i++)
            {
                if (loop.EntryEdges[i].Kind == CfgEdgeKind.Exception || loop.EntryEdges[i].ToBlockId != loop.Header)
                    return false;
            }
            return true;
        }

        private static List<CfgLoop> LoopsInPostOrder(ImmutableArray<CfgLoop> loops)
        {
            var result = new List<CfgLoop>(loops.Length);
            for (int i = 0; i < loops.Length; i++)
                result.Add(loops[i]);

            result.Sort(static (left, right) =>
            {
                int c = right.Depth.CompareTo(left.Depth);
                if (c != 0)
                    return c;
                c = left.Parent.CompareTo(right.Parent);
                if (c != 0)
                    return c;
                return left.Header.CompareTo(right.Header);
            });
            return result;
        }

        private static bool TryInvertLoop(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopInversionOptions options,
            out GenTreeMethod rewritten)
        {
            rewritten = method;

            if (!loop.IsReducible || !loop.IsCanonicalPreheader)
                return false;
            if (loop.Preheader < 0 || (uint)loop.Preheader >= (uint)method.Blocks.Length)
                return false;
            if ((method.Blocks[loop.Preheader].Flags & GenTreeBlockFlags.LoopInvertedPreheader) != 0)
                return false;
            if (loop.EntryEdges.Length != 1)
                return false;

            var entryEdge = loop.EntryEdges[0];
            if (entryEdge.Kind == CfgEdgeKind.Exception ||
                entryEdge.FromBlockId != loop.Preheader ||
                entryEdge.ToBlockId != loop.Header)
            {
                return false;
            }

            if (!TryFindCandidate(method, cfg, loop, out var candidate))
                return false;
            if (HasBottomTestedBackedge(method, cfg, loop, candidate))
                return false;
            if (!IsProfitable(method, cfg, loop, candidate, options))
                return false;

            rewritten = RewriteLoop(method, loop, candidate);
            return !ReferenceEquals(rewritten, method);
        }

        private static bool TryFindCandidate(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            out LoopCandidate candidate)
        {
            candidate = default;

            var preheaderBlock = method.Blocks[loop.Preheader];
            if (!HasSingleNormalSuccessor(cfg.Blocks[loop.Preheader], loop.Header))
                return false;
            if (preheaderBlock.ExitStackDepth != 0 || preheaderBlock.EntryStackDepth != 0)
                return false;

            var preheaderCfgBlock = cfg.Blocks[loop.Preheader];
            var conditionBlocks = ImmutableArray.CreateBuilder<int>();
            var visited = new HashSet<int>();
            int current = loop.Header;

            while (true)
            {
                if ((uint)current >= (uint)method.Blocks.Length || !loop.Contains(current))
                    return false;
                if (!visited.Add(current))
                    return false;
                var block = method.Blocks[current];
                var cfgBlock = cfg.Blocks[current];
                if (block.EntryStackDepth != 0 || block.ExitStackDepth != 0)
                    return false;
                if (!SameEhRegion(preheaderCfgBlock, cfgBlock))
                    return false;

                conditionBlocks.Add(current);

                if (block.JumpKind == GenTreeBlockJumpKind.Always)
                {
                    if (!TryGetSingleNormalSuccessor(cfgBlock, out int successor))
                        return false;
                    if (!loop.Contains(successor))
                        return false;
                    if (!HasValidUnconditionalTerminator(block, successor))
                        return false;
                    current = successor;
                    continue;
                }

                if (block.JumpKind != GenTreeBlockJumpKind.Conditional)
                    return false;
                if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out var conditional, out var appendedFallThrough))
                    return false;
                if (!TryGetLogicalSuccessors(cfgBlock, conditional, appendedFallThrough, out int trueSuccessor, out int falseSuccessor))
                    return false;

                bool trueInside = loop.Contains(trueSuccessor);
                bool falseInside = loop.Contains(falseSuccessor);
                if (trueInside == falseInside)
                    return false;

                int stayInLoop = trueInside ? trueSuccessor : falseSuccessor;
                int exit = trueInside ? falseSuccessor : trueSuccessor;
                if (stayInLoop == loop.Header)
                    return false;
                if ((uint)exit >= (uint)method.Blocks.Length || exit == loop.Preheader)
                    return false;
                if (!SameEhRegion(preheaderCfgBlock, cfg.Blocks[exit]) ||
                    GenTreeCriticalEdgeSplitter.IsExceptionRegionEntry(cfg, exit))
                {
                    return false;
                }
                if (method.Blocks[exit].EntryStackDepth != 0)
                    return false;
                if (!AllExitEdgesHaveCompatibleStack(method, loop, exit))
                    return false;
                if (!CanDuplicateConditionChain(method, conditionBlocks, conditional))
                    return false;

                candidate = new LoopCandidate(
                    conditionBlocks.ToImmutable(),
                    conditional,
                    trueSuccessor,
                    falseSuccessor,
                    stayInLoop,
                    exit);
                return true;
            }
        }

        private static bool HasSingleNormalSuccessor(CfgBlock block, int expectedSuccessor)
            => TryGetSingleNormalSuccessor(block, out int successor) && successor == expectedSuccessor;

        private static bool TryGetSingleNormalSuccessor(CfgBlock block, out int successor)
        {
            successor = -1;
            int count = 0;
            for (int i = 0; i < block.Successors.Length; i++)
            {
                var edge = block.Successors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (count != 0 && successor != edge.ToBlockId)
                    return false;
                successor = edge.ToBlockId;
                count++;
            }
            return count == 1;
        }

        private static bool HasValidUnconditionalTerminator(GenTreeBlock block, int successor)
        {
            if (block.Statements.IsDefaultOrEmpty)
                return false;

            var terminator = block.Statements[block.Statements.Length - 1];
            return terminator.Kind == GenTreeKind.Branch &&
                   terminator.TargetBlockId == successor &&
                   terminator.SourceOp != BytecodeOp.Leave;
        }

        private static bool TryGetLogicalSuccessors(
            CfgBlock cfgBlock,
            GenTree conditional,
            GenTree? appendedFallThrough,
            out int trueSuccessor,
            out int falseSuccessor)
        {
            trueSuccessor = -1;
            falseSuccessor = -1;

            var normalSuccessors = new List<int>(2);
            for (int i = 0; i < cfgBlock.Successors.Length; i++)
            {
                var edge = cfgBlock.Successors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (!normalSuccessors.Contains(edge.ToBlockId))
                    normalSuccessors.Add(edge.ToBlockId);
            }

            if (normalSuccessors.Count != 2 || !normalSuccessors.Contains(conditional.TargetBlockId))
                return false;

            int other = normalSuccessors[0] == conditional.TargetBlockId
                ? normalSuccessors[1]
                : normalSuccessors[0];

            if (appendedFallThrough is not null && appendedFallThrough.TargetBlockId != other)
                return false;

            if (conditional.Kind == GenTreeKind.BranchTrue)
            {
                trueSuccessor = conditional.TargetBlockId;
                falseSuccessor = other;
                return true;
            }

            if (conditional.Kind == GenTreeKind.BranchFalse)
            {
                trueSuccessor = other;
                falseSuccessor = conditional.TargetBlockId;
                return true;
            }

            return false;
        }

        private static bool AllExitEdgesHaveCompatibleStack(GenTreeMethod method, CfgLoop loop, int exit)
        {
            int entryDepth = method.Blocks[exit].EntryStackDepth;
            for (int i = 0; i < loop.ExitEdges.Length; i++)
            {
                var edge = loop.ExitEdges[i];
                if (edge.Kind == CfgEdgeKind.Exception || edge.ToBlockId != exit)
                    continue;
                if ((uint)edge.FromBlockId >= (uint)method.Blocks.Length)
                    return false;
                if (method.Blocks[edge.FromBlockId].ExitStackDepth != entryDepth)
                    return false;
            }
            return true;
        }

        private static bool CanDuplicateConditionChain(
            GenTreeMethod method,
            ImmutableArray<int>.Builder conditionBlocks,
            GenTree conditional)
        {
            for (int i = 0; i < conditionBlocks.Count; i++)
            {
                var block = method.Blocks[conditionBlocks[i]];
                int payloadEnd;
                if (i + 1 == conditionBlocks.Count)
                {
                    if (!TryGetConditionalPayloadEnd(block, conditional, out payloadEnd))
                        return false;
                }
                else
                {
                    if (!TryGetUnconditionalPayloadEnd(block, out payloadEnd))
                        return false;
                }

                for (int s = 0; s < payloadEnd; s++)
                {
                    if (ContainsControlFlow(block.Statements[s]))
                        return false;
                }
            }

            return conditional.Operands.Length == 1;
        }

        private static bool TryGetUnconditionalPayloadEnd(GenTreeBlock block, out int payloadEnd)
        {
            payloadEnd = 0;
            if (block.Statements.IsDefaultOrEmpty)
                return false;
            int last = block.Statements.Length - 1;
            if (block.Statements[last].Kind != GenTreeKind.Branch)
                return false;
            payloadEnd = last;
            return true;
        }

        private static bool TryGetConditionalPayloadEnd(GenTreeBlock block, GenTree conditional, out int payloadEnd)
        {
            payloadEnd = 0;
            if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out var found, out var appended))
                return false;
            if (!ReferenceEquals(found, conditional))
                return false;
            payloadEnd = appended is null ? block.Statements.Length - 1 : block.Statements.Length - 2;
            return payloadEnd >= 0;
        }

        private static bool ContainsControlFlow(GenTree node)
        {
            if ((node.Flags & (GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow)) != 0 ||
                node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                    GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally)
            {
                return true;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (ContainsControlFlow(node.Operands[i]))
                    return true;
            }
            return false;
        }

        private static bool HasBottomTestedBackedge(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopCandidate candidate)
        {
            int candidateConditionBlock = candidate.ConditionBlocks[candidate.ConditionBlocks.Length - 1];
            for (int i = 0; i < loop.BackEdges.Length; i++)
            {
                int latch = loop.BackEdges[i].FromBlockId;
                if ((uint)latch >= (uint)method.Blocks.Length)
                    return true;

                if (latch != candidateConditionBlock &&
                    IsExitingConditionalBlock(method, cfg, loop, latch) &&
                    IsRecognizedIterationTestBlock(method, loop, method.Blocks[latch]))
                {
                    return true;
                }

                var block = method.Blocks[latch];
                if (!IsEmptyAlwaysBlock(block))
                    continue;

                var predecessors = cfg.Blocks[latch].Predecessors;
                for (int p = 0; p < predecessors.Length; p++)
                {
                    int predecessor = predecessors[p].FromBlockId;
                    if (!loop.Contains(predecessor) || predecessor == candidateConditionBlock)
                        continue;
                    if (IsExitingConditionalBlock(method, cfg, loop, predecessor) &&
                        IsRecognizedIterationTestBlock(method, loop, method.Blocks[predecessor]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsExitingConditionalBlock(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            int blockId)
        {
            if ((uint)blockId >= (uint)method.Blocks.Length)
                return false;
            var block = method.Blocks[blockId];
            if (block.JumpKind != GenTreeBlockJumpKind.Conditional)
                return false;
            if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out var conditional, out var appended))
                return false;
            if (!TryGetLogicalSuccessors(cfg.Blocks[blockId], conditional, appended, out int trueSuccessor, out int falseSuccessor))
                return false;
            return loop.Contains(trueSuccessor) != loop.Contains(falseSuccessor);
        }

        private static bool IsRecognizedIterationTestBlock(
            GenTreeMethod method,
            CfgLoop loop,
            GenTreeBlock block)
        {
            if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out var conditional, out _))
                return false;

            var uses = new HashSet<(GenTreeKind kind, int index)>();
            for (int i = 0; i < conditional.Operands.Length; i++)
                CollectLocalUses(conditional.Operands[i], uses);
            if (uses.Count == 0)
                return false;

            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                if ((uint)blockId >= (uint)method.Blocks.Length)
                    return false;
                var statements = method.Blocks[blockId].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (IsInductionIncrement(statements[s], uses))
                        return true;
                }
            }
            return false;
        }

        private static bool IsEmptyAlwaysBlock(GenTreeBlock block)
            => block.JumpKind == GenTreeBlockJumpKind.Always &&
               block.Statements.Length == 1 &&
               block.Statements[0].Kind == GenTreeKind.Branch;

        private static bool IsProfitable(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopCandidate candidate,
            LoopInversionOptions options)
        {
            var nestedBlocks = ComputeNestedLoopBlocks(cfg.NaturalLoops, loop);
            int loopNodeCount = 0;
            bool ownBoundsCheck = false;
            bool nestedBoundsCheck = false;
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                bool nested = nestedBlocks.Contains(blockId);
                var statements = method.Blocks[blockId].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    loopNodeCount = checked(loopNodeCount + CountTreeNodes(statements[s]));
                    if (!ContainsBoundsCheck(statements[s]))
                        continue;
                    if (nested)
                        nestedBoundsCheck = true;
                    else
                        ownBoundsCheck = true;
                }
            }

            if (loopNodeCount > options.MaxLoopTreeNodes)
            {
                if (!ownBoundsCheck || nestedBoundsCheck)
                    return false;
            }

            int duplicatedCost = 0;
            int arrayLengthCount = 0;
            int classInitCount = 0;
            for (int i = 0; i < candidate.ConditionBlocks.Length; i++)
            {
                var block = method.Blocks[candidate.ConditionBlocks[i]];
                int payloadEnd = i + 1 == candidate.ConditionBlocks.Length
                    ? ConditionalPayloadEnd(block)
                    : block.Statements.Length - 1;

                for (int s = 0; s < payloadEnd; s++)
                {
                    duplicatedCost = checked(duplicatedCost + EstimateCodeSize(block.Statements[s]));
                    CountSpecialNodes(block.Statements[s], ref arrayLengthCount, ref classInitCount);
                }
            }

            duplicatedCost = checked(duplicatedCost + EstimateCodeSize(candidate.Conditional));
            CountSpecialNodes(candidate.Conditional, ref arrayLengthCount, ref classInitCount);

            int maximumCost = options.MaxDuplicatedCodeSize;
            if (duplicatedCost <= maximumCost)
                return true;

            maximumCost = checked(maximumCost + Math.Min(classInitCount, 9) * 24);
            maximumCost = checked(maximumCost + arrayLengthCount * 8);
            if ((ownBoundsCheck || nestedBoundsCheck) && HasSplitInductionVariable(method, loop, candidate.Conditional))
                maximumCost = checked(maximumCost + 24);

            return duplicatedCost <= maximumCost;
        }

        private static HashSet<int> ComputeNestedLoopBlocks(ImmutableArray<CfgLoop> loops, CfgLoop loop)
        {
            var result = new HashSet<int>();
            for (int i = 0; i < loops.Length; i++)
            {
                var candidate = loops[i];
                if (candidate.Index == loop.Index || !IsDescendantLoop(loops, candidate, loop.Index))
                    continue;
                for (int b = 0; b < candidate.Blocks.Length; b++)
                    result.Add(candidate.Blocks[b]);
            }
            return result;
        }

        private static bool IsDescendantLoop(ImmutableArray<CfgLoop> loops, CfgLoop candidate, int ancestorIndex)
        {
            int parent = candidate.Parent;
            var visited = new HashSet<int>();
            while (parent >= 0)
            {
                if (parent == ancestorIndex)
                    return true;
                if ((uint)parent >= (uint)loops.Length || !visited.Add(parent))
                    return false;
                parent = loops[parent].Parent;
            }
            return false;
        }

        private static int ConditionalPayloadEnd(GenTreeBlock block)
        {
            if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out _, out var appended))
                throw new InvalidOperationException("Malformed conditional loop block.");
            return appended is null ? block.Statements.Length - 1 : block.Statements.Length - 2;
        }

        private static int CountTreeNodes(GenTree node)
        {
            int count = 1;
            for (int i = 0; i < node.Operands.Length; i++)
                count = checked(count + CountTreeNodes(node.Operands[i]));
            return count;
        }

        private static bool ContainsBoundsCheck(GenTree node)
        {
            if ((node.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement) &&
                (node.Flags & GenTreeFlags.BoundsCheckEliminated) == 0)
            {
                return true;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (ContainsBoundsCheck(node.Operands[i]))
                    return true;
            }
            return false;
        }

        private static int EstimateCodeSize(GenTree node)
        {
            int cost = node.Kind switch
            {
                GenTreeKind.Intrinsic or GenTreeKind.Call or GenTreeKind.IndirectCall or GenTreeKind.VirtualCall => 8,
                GenTreeKind.NewObject or GenTreeKind.NewArray or GenTreeKind.NewDelegate => 8,
                GenTreeKind.ClassInit => 8,
                GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement => 5,
                GenTreeKind.ArrayLength => 2,
                GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.StaticField or GenTreeKind.StaticFieldAddr => 3,
                GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect => 3,
                GenTreeKind.Binary => (node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un) ? 6 : 2,
                GenTreeKind.Conv => 2,
                _ => 1,
            };

            for (int i = 0; i < node.Operands.Length; i++)
                cost = checked(cost + EstimateCodeSize(node.Operands[i]));
            return cost;
        }

        private static void CountSpecialNodes(GenTree node, ref int arrayLengthCount, ref int classInitCount)
        {
            if (node.Kind == GenTreeKind.ArrayLength)
                arrayLengthCount++;
            else if (node.Kind == GenTreeKind.ClassInit)
                classInitCount++;

            for (int i = 0; i < node.Operands.Length; i++)
                CountSpecialNodes(node.Operands[i], ref arrayLengthCount, ref classInitCount);
        }

        private static bool HasSplitInductionVariable(GenTreeMethod method, CfgLoop loop, GenTree conditional)
        {
            var uses = new HashSet<(GenTreeKind kind, int index)>();
            for (int i = 0; i < conditional.Operands.Length; i++)
                CollectLocalUses(conditional.Operands[i], uses);
            if (uses.Count == 0)
                return false;

            for (int i = 0; i < loop.Latches.Length; i++)
            {
                var statements = method.Blocks[loop.Latches[i]].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (IsInductionIncrement(statements[s], uses))
                        return true;
                }
            }
            return false;
        }

        private static void CollectLocalUses(GenTree node, HashSet<(GenTreeKind kind, int index)> uses)
        {
            if (node.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp)
                uses.Add((node.Kind, node.Int32));
            for (int i = 0; i < node.Operands.Length; i++)
                CollectLocalUses(node.Operands[i], uses);
        }

        private static bool IsInductionIncrement(GenTree node, HashSet<(GenTreeKind kind, int index)> uses)
        {
            if ((node.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp) && node.Operands.Length == 1)
            {
                GenTreeKind loadKind = node.Kind switch
                {
                    GenTreeKind.StoreLocal => GenTreeKind.Local,
                    GenTreeKind.StoreArg => GenTreeKind.Arg,
                    _ => GenTreeKind.Temp,
                };

                var slot = (loadKind, node.Int32);
                var value = node.Operands[0];
                if (uses.Contains(slot) &&
                    value.Kind == GenTreeKind.Binary &&
                    (value.SourceOp is BytecodeOp.Add or BytecodeOp.Sub) &&
                    value.Operands.Length == 2 &&
                    ((IsSameSlot(value.Operands[0], slot) && IsIntegerConstant(value.Operands[1])) ||
                     (value.SourceOp == BytecodeOp.Add && IsIntegerConstant(value.Operands[0]) && IsSameSlot(value.Operands[1], slot))))
                {
                    return true;
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (IsInductionIncrement(node.Operands[i], uses))
                    return true;
            }
            return false;
        }

        private static bool IsSameSlot(GenTree node, (GenTreeKind kind, int index) slot)
            => node.Kind == slot.kind && node.Int32 == slot.index;

        private static bool IsIntegerConstant(GenTree node)
            => node.Kind is GenTreeKind.ConstI4 or GenTreeKind.ConstI8;

        private static GenTreeMethod RewriteLoop(
            GenTreeMethod method,
            CfgLoop loop,
            LoopCandidate candidate)
        {
            int nextTreeId = GenTreeCriticalEdgeSplitter.NextSyntheticTreeId(method);
            int actualPreheaderId = method.Blocks.Length;
            int loopExitId = checked(method.Blocks.Length + 1);
            int actualPreheaderPc = GenTreeCriticalEdgeSplitter.FirstSyntheticPc(method);
            int loopExitPc = checked(actualPreheaderPc - 1);

            var redirects = new Dictionary<(int from, int to), GenTreeCriticalEdgeSplitter.SplitEdgeInfo>();
            for (int i = 0; i < loop.ExitEdges.Length; i++)
            {
                var edge = loop.ExitEdges[i];
                if (edge.Kind == CfgEdgeKind.Exception || edge.ToBlockId != candidate.ExitSuccessor)
                    continue;
                redirects[(edge.FromBlockId, edge.ToBlockId)] =
                    new GenTreeCriticalEdgeSplitter.SplitEdgeInfo(loopExitId, loopExitPc);
            }

            if (redirects.Count == 0)
                return method;

            var zeroTripBlock = CreateZeroTripBlock(
                method,
                loop,
                candidate,
                actualPreheaderId,
                actualPreheaderPc,
                ref nextTreeId);
            var actualPreheader = CreateActualPreheader(
                method,
                loop,
                candidate,
                actualPreheaderId,
                actualPreheaderPc,
                ref nextTreeId);
            var loopExit = CreateLoopExit(
                method,
                candidate,
                loopExitId,
                loopExitPc,
                ref nextTreeId);

            var provisionalBlocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length + 2);
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (i == candidate.ExitSuccessor)
                    provisionalBlocks.Add(loopExit);

                if (i == loop.Preheader)
                {
                    provisionalBlocks.Add(zeroTripBlock);
                    provisionalBlocks.Add(actualPreheader);
                }
                else
                {
                    provisionalBlocks.Add(GenTreeCriticalEdgeSplitter.RewriteOriginalBlock(
                        method.Blocks[i],
                        redirects,
                        ref nextTreeId));
                }
            }

            var rewritten = GenTreeCriticalEdgeSplitter.RenumberBlocks(method, provisionalBlocks.ToImmutable());
            VerifyRewrittenFlow(
                rewritten,
                method.Blocks[loop.Preheader].StartPc,
                method.Blocks[loop.Header].StartPc,
                method.Blocks[candidate.StayInLoopSuccessor].StartPc,
                method.Blocks[candidate.ExitSuccessor].StartPc,
                actualPreheaderPc,
                loopExitPc);
            return rewritten;
        }

        private static GenTreeBlock CreateZeroTripBlock(
            GenTreeMethod method,
            CfgLoop loop,
            LoopCandidate candidate,
            int actualPreheaderId,
            int actualPreheaderPc,
            ref int nextTreeId)
        {
            var oldPreheader = method.Blocks[loop.Preheader];
            int payloadEnd = PreheaderPayloadEnd(oldPreheader, loop.Header);
            var statements = ImmutableArray.CreateBuilder<GenTree>();
            for (int i = 0; i < payloadEnd; i++)
                statements.Add(oldPreheader.Statements[i]);

            for (int i = 0; i < candidate.ConditionBlocks.Length; i++)
            {
                var block = method.Blocks[candidate.ConditionBlocks[i]];
                int conditionPayloadEnd = i + 1 == candidate.ConditionBlocks.Length
                    ? ConditionalPayloadEnd(block)
                    : block.Statements.Length - 1;
                for (int s = 0; s < conditionPayloadEnd; s++)
                    statements.Add(CloneTree(block.Statements[s], ref nextTreeId));
            }

            var operands = ImmutableArray.CreateBuilder<GenTree>(candidate.Conditional.Operands.Length);
            for (int i = 0; i < candidate.Conditional.Operands.Length; i++)
                operands.Add(CloneTree(candidate.Conditional.Operands[i], ref nextTreeId));

            int trueDestination = candidate.TrueSuccessor == candidate.ExitSuccessor
                ? candidate.ExitSuccessor
                : actualPreheaderId;
            int falseDestination = candidate.FalseSuccessor == candidate.ExitSuccessor
                ? candidate.ExitSuccessor
                : actualPreheaderId;
            int truePc = trueDestination == actualPreheaderId
                ? actualPreheaderPc
                : method.Blocks[trueDestination].StartPc;
            int falsePc = falseDestination == actualPreheaderId
                ? actualPreheaderPc
                : method.Blocks[falseDestination].StartPc;

            var conditionalFlags = ClearDuplicatedFlags(candidate.Conditional.Flags) |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.Ordered;
            statements.Add(new GenTree(
                nextTreeId++,
                GenTreeKind.BranchTrue,
                candidate.Conditional.Pc,
                BranchTrueSourceOp(candidate.Conditional.SourceOp),
                type: null,
                stackKind: GenStackKind.Void,
                flags: conditionalFlags,
                operands: operands.ToImmutable(),
                targetPc: truePc,
                targetBlockId: trueDestination));
            statements.Add(CreateBranch(
                ref nextTreeId,
                candidate.Conditional.Pc,
                falsePc,
                falseDestination));

            return new GenTreeBlock(
                oldPreheader.Id,
                oldPreheader.StartPc,
                oldPreheader.EndPcExclusive,
                oldPreheader.EntryStackDepth,
                oldPreheader.ExitStackDepth,
                GenTreeBlockJumpKind.Conditional,
                oldPreheader.Flags,
                statements.ToImmutable(),
                ImmutableArray.Create(trueDestination, falseDestination),
                ImmutableArray.Create(truePc, falsePc),
                oldPreheader.RegionPc);
        }

        private static int PreheaderPayloadEnd(GenTreeBlock preheader, int header)
        {
            if (preheader.Statements.IsDefaultOrEmpty)
                return 0;

            int last = preheader.Statements.Length - 1;
            var terminator = preheader.Statements[last];
            if (terminator.Kind == GenTreeKind.Branch && terminator.TargetBlockId == header)
                return last;
            if (preheader.JumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough)
                return preheader.Statements.Length;

            throw new InvalidOperationException("Malformed canonical loop preheader.");
        }

        private static GenTreeBlock CreateActualPreheader(
            GenTreeMethod method,
            CfgLoop loop,
            LoopCandidate candidate,
            int blockId,
            int pc,
            ref int nextTreeId)
        {
            var oldPreheader = method.Blocks[loop.Preheader];
            var stay = method.Blocks[candidate.StayInLoopSuccessor];
            var branch = CreateBranch(ref nextTreeId, pc, stay.StartPc, stay.Id);
            return new GenTreeBlock(
                blockId,
                pc,
                pc,
                oldPreheader.ExitStackDepth,
                oldPreheader.ExitStackDepth,
                GenTreeBlockJumpKind.Always,
                SyntheticBlockFlags(oldPreheader, oldPreheader.ExitStackDepth) | GenTreeBlockFlags.LoopInvertedPreheader,
                ImmutableArray.Create(branch),
                ImmutableArray.Create(stay.Id),
                ImmutableArray.Create(stay.StartPc),
                oldPreheader.RegionPc);
        }

        private static GenTreeBlock CreateLoopExit(
            GenTreeMethod method,
            LoopCandidate candidate,
            int blockId,
            int pc,
            ref int nextTreeId)
        {
            var exit = method.Blocks[candidate.ExitSuccessor];
            var branch = CreateBranch(ref nextTreeId, pc, exit.StartPc, exit.Id);
            return new GenTreeBlock(
                blockId,
                pc,
                pc,
                exit.EntryStackDepth,
                exit.EntryStackDepth,
                GenTreeBlockJumpKind.Always,
                SyntheticBlockFlags(exit, exit.EntryStackDepth),
                ImmutableArray.Create(branch),
                ImmutableArray.Create(exit.Id),
                ImmutableArray.Create(exit.StartPc),
                exit.RegionPc);
        }

        private static GenTreeBlockFlags SyntheticBlockFlags(GenTreeBlock regionSource, int stackDepth)
        {
            var flags = regionSource.Flags &
                        (GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
            if (stackDepth != 0)
                flags |= GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit;
            return flags;
        }

        private static GenTree CreateBranch(
            ref int nextTreeId,
            int pc,
            int targetPc,
            int targetBlockId)
            => new GenTree(
                nextTreeId++,
                GenTreeKind.Branch,
                pc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: targetPc,
                targetBlockId: targetBlockId);

        private static GenTree CloneTree(GenTree node, ref int nextTreeId)
        {
            ImmutableArray<GenTree> operands = ImmutableArray<GenTree>.Empty;
            if (!node.Operands.IsDefaultOrEmpty)
            {
                var builder = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                    builder.Add(CloneTree(node.Operands[i], ref nextTreeId));
                operands = builder.ToImmutable();
            }

            var clone = new GenTree(
                nextTreeId++,
                node.Kind,
                node.Pc,
                node.SourceOp,
                node.Type,
                node.StackKind,
                ClearDuplicatedFlags(node.Flags),
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
                targetBlockId: node.TargetBlockId,
                boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
            clone.LocalDescriptor = node.LocalDescriptor;
            return clone;
        }

        private static GenTreeFlags ClearDuplicatedFlags(GenTreeFlags flags)
            => flags & ~(
                GenTreeFlags.AssertionProperties |
                GenTreeFlags.VarDef |
                GenTreeFlags.VarUseAsg |
                GenTreeFlags.VarDeath |
                GenTreeFlags.Prolog |
                GenTreeFlags.MakeCse |
                GenTreeFlags.ExplicitInit);

        private static BytecodeOp BranchTrueSourceOp(BytecodeOp sourceOp)
            => (sourceOp is BytecodeOp.Brtrue or BytecodeOp.Brfalse) ? BytecodeOp.Brtrue : sourceOp;

        private static bool SameEhRegion(CfgBlock left, CfgBlock right)
            => GenTreeCriticalEdgeSplitter.SameEhRegion(left, right);

        private static void VerifyRewrittenFlow(
            GenTreeMethod method,
            int zeroTripPc,
            int oldHeaderPc,
            int rotatedHeaderPc,
            int exitPc,
            int actualPreheaderPc,
            int loopExitPc)
        {
            var cfg = ControlFlowGraph.Build(method);
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("Loop inversion produced an inconsistent CFG.");

            var treeIds = new HashSet<int>();
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                var block = method.Blocks[i];
                if (block.Id != i)
                    throw new InvalidOperationException($"Loop inversion produced non-dense block id B{block.Id} at index {i}.");
                if (block.SuccessorBlockIds.Length != block.SuccessorPcs.Length)
                    throw new InvalidOperationException($"Loop inversion produced mismatched successor metadata in B{block.Id}.");

                var distinctSuccessors = new HashSet<int>();
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    int successor = block.SuccessorBlockIds[s];
                    if ((uint)successor >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"Loop inversion produced invalid CFG edge B{block.Id} -> B{successor}.");
                    distinctSuccessors.Add(successor);
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyTree(block.Statements[s], block.Id, distinctSuccessors, treeIds, method.Blocks.Length);

                if (block.JumpKind == GenTreeBlockJumpKind.Conditional)
                {
                    if (!GenTreeCriticalEdgeSplitter.TryGetConditionalTransfer(block.Statements, out var conditional, out var appended))
                        throw new InvalidOperationException($"Loop inversion produced malformed conditional block B{block.Id}.");
                    if (distinctSuccessors.Count != 2 || conditional.TargetBlockId < 0 || !distinctSuccessors.Contains(conditional.TargetBlockId))
                        throw new InvalidOperationException($"Loop inversion produced inconsistent conditional successors in B{block.Id}.");
                    if (appended is not null && (appended.TargetBlockId < 0 || !distinctSuccessors.Contains(appended.TargetBlockId)))
                        throw new InvalidOperationException($"Loop inversion produced inconsistent fall-through successor in B{block.Id}.");
                }
            }

            int zeroTrip = FindBlockByStartPc(method, zeroTripPc);
            int oldHeader = FindBlockByStartPc(method, oldHeaderPc);
            int rotatedHeader = FindBlockByStartPc(method, rotatedHeaderPc);
            int exit = FindBlockByStartPc(method, exitPc);
            int actualPreheader = FindBlockByStartPc(method, actualPreheaderPc);
            int loopExit = FindBlockByStartPc(method, loopExitPc);

            if (method.Blocks[actualPreheader].JumpKind != GenTreeBlockJumpKind.Always ||
                method.Blocks[actualPreheader].SuccessorBlockIds.Length != 1 ||
                method.Blocks[actualPreheader].SuccessorBlockIds[0] != rotatedHeader)
            {
                throw new InvalidOperationException("Loop inversion produced an invalid canonical preheader.");
            }
            if (method.Blocks[loopExit].JumpKind != GenTreeBlockJumpKind.Always ||
                method.Blocks[loopExit].SuccessorBlockIds.Length != 1 ||
                method.Blocks[loopExit].SuccessorBlockIds[0] != exit)
            {
                throw new InvalidOperationException("Loop inversion produced an invalid canonical exit.");
            }
            if (method.Blocks[zeroTrip].JumpKind != GenTreeBlockJumpKind.Conditional ||
                !ContainsSuccessor(method.Blocks[zeroTrip], actualPreheader) ||
                !ContainsSuccessor(method.Blocks[zeroTrip], exit))
            {
                throw new InvalidOperationException("Loop inversion produced an invalid zero-trip test.");
            }
            if (!SameEhRegion(cfg.Blocks[zeroTrip], cfg.Blocks[actualPreheader]) ||
                !SameEhRegion(cfg.Blocks[exit], cfg.Blocks[loopExit]))
            {
                throw new InvalidOperationException("Loop inversion changed EH-region membership of a split block.");
            }

            CfgLoop? rotatedLoop = null;
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                var loop = cfg.NaturalLoops[i];
                if (loop.Header == rotatedHeader && loop.Contains(oldHeader))
                {
                    rotatedLoop = loop;
                    break;
                }
            }
            if (!rotatedLoop.HasValue ||
                !rotatedLoop.Value.IsReducible ||
                !rotatedLoop.Value.IsCanonicalPreheader ||
                rotatedLoop.Value.Preheader != actualPreheader ||
                rotatedLoop.Value.Contains(loopExit) ||
                rotatedLoop.Value.Contains(exit))
            {
                throw new InvalidOperationException("Loop inversion did not produce a canonical rotated natural loop.");
            }

            bool exitsThroughCanonicalBlock = false;
            for (int i = 0; i < rotatedLoop.Value.ExitEdges.Length; i++)
            {
                if (rotatedLoop.Value.ExitEdges[i].ToBlockId == loopExit)
                {
                    exitsThroughCanonicalBlock = true;
                    break;
                }
            }
            if (!exitsThroughCanonicalBlock)
                throw new InvalidOperationException("Loop inversion detached the canonical loop exit.");
        }

        private static void VerifyTree(
            GenTree node,
            int blockId,
            HashSet<int> successors,
            HashSet<int> treeIds,
            int blockCount)
        {
            if (!treeIds.Add(node.Id))
                throw new InvalidOperationException($"Loop inversion produced duplicate tree id {node.Id}.");
            if (node.TargetBlockId >= 0)
            {
                if ((uint)node.TargetBlockId >= (uint)blockCount)
                    throw new InvalidOperationException($"Loop inversion produced invalid tree target B{node.TargetBlockId}.");
                if ((node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse) &&
                    !successors.Contains(node.TargetBlockId))
                {
                    throw new InvalidOperationException($"Loop inversion produced a transfer in B{blockId} outside its CFG successors.");
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                VerifyTree(node.Operands[i], blockId, successors, treeIds, blockCount);
        }

        private static int FindBlockByStartPc(GenTreeMethod method, int startPc)
        {
            int result = -1;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (method.Blocks[i].StartPc != startPc)
                    continue;
                if (result >= 0)
                    throw new InvalidOperationException($"Loop inversion produced duplicate block start PC {startPc}.");
                result = i;
            }
            if (result < 0)
                throw new InvalidOperationException($"Loop inversion lost block start PC {startPc}.");
            return result;
        }

        private static bool ContainsSuccessor(GenTreeBlock block, int successor)
        {
            for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
            {
                if (block.SuccessorBlockIds[i] == successor)
                    return true;
            }
            return false;
        }

    }
}
