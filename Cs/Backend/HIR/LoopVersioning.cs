using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class GenTreeLoopVersioningResult
    {
        public GenTreeMethod Method { get; }
        public int FastPreheaderBlockId { get; }
        public int SlowPreheaderBlockId { get; }
        public GenTreeLoopDuplicationResult SlowLoop { get; }

        public GenTreeLoopVersioningResult(
            GenTreeMethod method,
            int fastPreheaderBlockId,
            int slowPreheaderBlockId,
            GenTreeLoopDuplicationResult slowLoop)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            FastPreheaderBlockId = fastPreheaderBlockId;
            SlowPreheaderBlockId = slowPreheaderBlockId;
            SlowLoop = slowLoop ?? throw new ArgumentNullException(nameof(slowLoop));
        }
    }

    internal static class GenTreeLoopVersioner
    {
        public static bool CanVersion(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (!loop.IsReducible || !loop.IsCanonicalPreheader ||
                loop.Preheader < 0 ||
                (uint)loop.Preheader >= (uint)method.Blocks.Length ||
                cfg.Blocks.Length != method.Blocks.Length)
            {
                return false;
            }
            if (loop.EntryEdges.Length != 1 ||
                loop.EntryEdges[0].Kind == CfgEdgeKind.Exception ||
                loop.EntryEdges[0].FromBlockId != loop.Preheader ||
                loop.EntryEdges[0].ToBlockId != loop.Header)
            {
                return false;
            }

            var preheader = method.Blocks[loop.Preheader];
            var cfgPreheader = cfg.Blocks[loop.Preheader];
            if (preheader.Id != loop.Preheader ||
                cfgPreheader.Id != loop.Preheader ||
                preheader.EntryStackDepth != 0 ||
                preheader.ExitStackDepth != 0 ||
                (preheader.Flags & (GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit)) != 0 ||
                !IsOutsideExceptionRegions(preheader, cfgPreheader) ||
                GenTreeCriticalEdgeSplitter.IsExceptionRegionEntry(cfg, loop.Preheader) ||
                preheader.SuccessorBlockIds.Length != 1 ||
                preheader.SuccessorPcs.Length != 1 ||
                preheader.SuccessorBlockIds[0] != loop.Header ||
                !HasRedirectablePreheaderFlow(preheader, loop.Header))
            {
                return false;
            }

            return GenTreeLoopDuplicator.CanDuplicate(method, cfg, loop);
        }

        public static GenTreeMethod ApplyFastPathRewrite(
            GenTreeMethod method,
            CfgLoop loop,
            IReadOnlyDictionary<int, GenTreeBlock> fastBlocks)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (fastBlocks is null)
                throw new ArgumentNullException(nameof(fastBlocks));

            _ = ValidateFastBlocks(method, loop, fastBlocks);

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(method.Blocks.Length);
            for (int i = 0; i < method.Blocks.Length; i++)
                blocks.Add(loop.Contains(i) ? fastBlocks[i] : method.Blocks[i]);
            return method.CloneWithBlocks(blocks.ToImmutable());
        }

        public static GenTreeLoopVersioningResult Version(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            ImmutableArray<GenTree> conditions,
            IReadOnlyDictionary<int, GenTreeBlock> fastBlocks,
            GenTreeSyntheticAllocator allocator)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (fastBlocks is null)
                throw new ArgumentNullException(nameof(fastBlocks));
            if (allocator is null)
                throw new ArgumentNullException(nameof(allocator));
            if (conditions.IsDefaultOrEmpty)
                throw new InvalidOperationException("Loop versioning requires at least one runtime condition.");
            if (!CanVersion(method, cfg, loop))
                throw new InvalidOperationException("Loop cannot be versioned.");

            var fastTreeIds = ValidateFastBlocks(method, loop, fastBlocks);
            int maxConditionTreeId = ValidateConditions(fastTreeIds, conditions);
            int maxFastTreeId = -1;
            foreach (int treeId in fastTreeIds)
                maxFastTreeId = Math.Max(maxFastTreeId, treeId);
            allocator.EnsureNextTreeIdAtLeast(checked(Math.Max(maxFastTreeId, maxConditionTreeId) + 1));

            int oldBlockCount = method.Blocks.Length;
            int firstConditionBlockId = oldBlockCount;
            int fastPreheaderBlockId = checked(firstConditionBlockId + conditions.Length);
            int slowPreheaderBlockId = checked(fastPreheaderBlockId + 1);
            int firstSlowBlockId = checked(slowPreheaderBlockId + 1);

            var conditionPcs = new int[conditions.Length];
            for (int i = 0; i < conditionPcs.Length; i++)
                conditionPcs[i] = allocator.AllocatePc();
            int fastPreheaderPc = allocator.AllocatePc();
            int slowPreheaderPc = allocator.AllocatePc();

            var slowLoop = GenTreeLoopDuplicator.Duplicate(
                method,
                cfg,
                loop,
                firstSlowBlockId,
                allocator);
            slowLoop = MarkSlowLoop(slowLoop);

            int firstChoiceBlockId = firstConditionBlockId;
            int firstChoicePc = conditionPcs[0];

            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(
                checked(oldBlockCount + conditions.Length + 2 + slowLoop.Blocks.Length));

            for (int i = 0; i < oldBlockCount; i++)
            {
                if (i == loop.Preheader)
                {
                    blocks.Add(RewritePreheader(
                        method.Blocks[i],
                        loop.Header,
                        firstChoiceBlockId,
                        firstChoicePc,
                        allocator));
                }
                else if (loop.Contains(i))
                {
                    blocks.Add(MarkFastBlock(fastBlocks[i]));
                }
                else
                {
                    blocks.Add(method.Blocks[i]);
                }
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                int successBlockId = i + 1 < conditions.Length
                    ? firstConditionBlockId + i + 1
                    : fastPreheaderBlockId;
                int successPc = i + 1 < conditions.Length
                    ? conditionPcs[i + 1]
                    : fastPreheaderPc;

                blocks.Add(CreateConditionBlock(
                    firstConditionBlockId + i,
                    conditionPcs[i],
                    conditions[i],
                    successBlockId,
                    successPc,
                    slowPreheaderBlockId,
                    slowPreheaderPc,
                    allocator));
            }

            blocks.Add(CreatePreheader(
                fastPreheaderBlockId,
                fastPreheaderPc,
                loop.Header,
                method.Blocks[loop.Header].StartPc,
                GenTreeBlockFlags.LoopCloneFastPreheader,
                allocator));
            blocks.Add(CreatePreheader(
                slowPreheaderBlockId,
                slowPreheaderPc,
                slowLoop.GetBlock(loop.Header),
                slowLoop.GetPc(loop.Header),
                GenTreeBlockFlags.LoopCloneSlowPreheader,
                allocator));
            blocks.AddRange(slowLoop.Blocks);

            var rewritten = method.CloneWithBlocks(blocks.ToImmutable());
            Verify(
                rewritten,
                loop,
                conditions.Length,
                firstConditionBlockId,
                fastPreheaderBlockId,
                slowPreheaderBlockId,
                slowLoop);

            return new GenTreeLoopVersioningResult(
                rewritten,
                fastPreheaderBlockId,
                slowPreheaderBlockId,
                slowLoop);
        }


        private static bool IsOutsideExceptionRegions(GenTreeBlock block, CfgBlock cfgBlock)
            => !cfgBlock.IsInTryRegion &&
               !cfgBlock.IsInHandlerRegion &&
               !cfgBlock.IsHandlerEntry &&
               (block.Flags &
                (GenTreeBlockFlags.TryEntry |
                 GenTreeBlockFlags.HandlerEntry |
                 GenTreeBlockFlags.InTryRegion |
                 GenTreeBlockFlags.InHandlerRegion)) == 0;

        private static HashSet<int> ValidateFastBlocks(
            GenTreeMethod method,
            CfgLoop loop,
            IReadOnlyDictionary<int, GenTreeBlock> fastBlocks)
        {
            if (!loop.IsReducible || loop.Blocks.IsDefaultOrEmpty)
                throw new InvalidOperationException("Fast-path rewrite requires a non-empty reducible loop.");
            if (fastBlocks.Count != loop.Blocks.Length)
                throw new InvalidOperationException("Fast-path rewrite must cover exactly the loop blocks.");

            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                if ((uint)blockId >= (uint)method.Blocks.Length || !fastBlocks.TryGetValue(blockId, out var rewritten))
                    throw new InvalidOperationException("Fast-path rewrite does not cover the complete loop.");

                var original = method.Blocks[blockId];
                if (rewritten.Id != blockId ||
                    rewritten.StartPc != original.StartPc ||
                    rewritten.EndPcExclusive != original.EndPcExclusive ||
                    rewritten.RegionPc != original.RegionPc ||
                    rewritten.EntryStackDepth != original.EntryStackDepth ||
                    rewritten.ExitStackDepth != original.ExitStackDepth ||
                    rewritten.JumpKind != original.JumpKind ||
                    rewritten.Flags != original.Flags ||
                    !Same(rewritten.SuccessorBlockIds, original.SuccessorBlockIds) ||
                    !Same(rewritten.SuccessorPcs, original.SuccessorPcs))
                {
                    throw new InvalidOperationException("Fast-path rewrite changed loop control flow.");
                }
            }

            var nodes = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            var treeIds = new HashSet<int>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = loop.Contains(b) ? fastBlocks[b] : method.Blocks[b];
                for (int s = 0; s < block.Statements.Length; s++)
                    ValidateTree(block.Statements[s]);
            }

            return treeIds;

            void ValidateTree(GenTree node)
            {
                if (node.Id < 0 || !nodes.Add(node) || !treeIds.Add(node.Id))
                    throw new InvalidOperationException("Fast-path rewrite produced invalid, shared, or repeated trees.");
                for (int i = 0; i < node.Operands.Length; i++)
                    ValidateTree(node.Operands[i]);
            }
        }

        private static int ValidateConditions(IReadOnlySet<int> methodTreeIds, ImmutableArray<GenTree> conditions)
        {
            var conditionNodes = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            var conditionTreeIds = new HashSet<int>();
            int maxTreeId = -1;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].StackKind != GenStackKind.I4)
                    throw new InvalidOperationException("Loop versioning condition must produce I4.");
                ValidateConditionTree(conditions[i]);
            }

            return maxTreeId;

            void ValidateConditionTree(GenTree node)
            {
                if (node.Id < 0 ||
                    !conditionNodes.Add(node) ||
                    !conditionTreeIds.Add(node.Id) ||
                    methodTreeIds.Contains(node.Id))
                {
                    throw new InvalidOperationException("Loop versioning conditions contain shared or repeated trees.");
                }
                maxTreeId = Math.Max(maxTreeId, node.Id);
                const GenTreeFlags invalidFlags =
                    GenTreeFlags.ContainsCall |
                    GenTreeFlags.SideEffect |
                    GenTreeFlags.MemoryWrite |
                    GenTreeFlags.LocalDef |
                    GenTreeFlags.Allocation |
                    GenTreeFlags.ControlFlow |
                    GenTreeFlags.ExceptionFlow;
                if ((node.Flags & invalidFlags) != 0 ||
                    node.TargetBlockId >= 0 ||
                    node.Kind is
                        GenTreeKind.Branch or
                        GenTreeKind.BranchTrue or
                        GenTreeKind.BranchFalse or
                        GenTreeKind.Return or
                        GenTreeKind.Throw or
                        GenTreeKind.Rethrow or
                        GenTreeKind.EndFinally)
                {
                    throw new InvalidOperationException("Loop versioning condition is not a pure guard.");
                }
                for (int i = 0; i < node.Operands.Length; i++)
                    ValidateConditionTree(node.Operands[i]);
            }
        }

        private static bool HasRedirectablePreheaderFlow(GenTreeBlock preheader, int targetBlockId)
        {
            if (preheader.JumpKind == GenTreeBlockJumpKind.Always)
            {
                if (preheader.Statements.IsDefaultOrEmpty)
                    return false;
                int branchIndex = preheader.Statements.Length - 1;
                var branch = preheader.Statements[branchIndex];
                return branch.Kind == GenTreeKind.Branch &&
                       branch.Operands.Length == 0 &&
                       branch.SourceOp != BytecodeOp.Leave &&
                       branch.TargetBlockId == targetBlockId &&
                       !ContainsControlTransfer(preheader.Statements, branchIndex);
            }

            return preheader.JumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough &&
                   !ContainsControlTransfer(preheader.Statements, preheader.Statements.Length);
        }

        private static bool ContainsControlTransfer(ImmutableArray<GenTree> statements, int endExclusive)
        {
            for (int i = 0; i < endExclusive; i++)
            {
                if (ContainsControlTransfer(statements[i]))
                    return true;
            }
            return false;
        }

        private static bool ContainsControlTransfer(GenTree node)
        {
            if (node.Kind is
                GenTreeKind.Branch or
                GenTreeKind.BranchTrue or
                GenTreeKind.BranchFalse or
                GenTreeKind.Return or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow or
                GenTreeKind.EndFinally)
            {
                return true;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (ContainsControlTransfer(node.Operands[i]))
                    return true;
            }
            return false;
        }

        private static bool Same(ImmutableArray<int> left, ImmutableArray<int> right)
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
        private static GenTreeBlock RewritePreheader(
            GenTreeBlock preheader,
            int oldHeader,
            int targetBlockId,
            int targetPc,
            GenTreeSyntheticAllocator allocator)
        {
            if (!HasRedirectablePreheaderFlow(preheader, oldHeader))
                throw new InvalidOperationException("Malformed loop preheader.");

            int retainedStatementCount = preheader.Statements.Length;
            int branchPc = retainedStatementCount == 0
                ? preheader.StartPc
                : preheader.Statements[retainedStatementCount - 1].Pc;
            if (preheader.JumpKind == GenTreeBlockJumpKind.Always)
            {
                retainedStatementCount--;
                branchPc = preheader.Statements[retainedStatementCount].Pc;
            }

            var statements = ImmutableArray.CreateBuilder<GenTree>(checked(retainedStatementCount + 1));
            for (int i = 0; i < retainedStatementCount; i++)
                statements.Add(preheader.Statements[i]);
            statements.Add(GenTreeLoopDuplicator.CreateBranch(
                allocator,
                branchPc,
                targetPc,
                targetBlockId));

            var flags = (preheader.Flags & ~GenTreeBlockFlags.LoopInvertedPreheader) |
                        GenTreeBlockFlags.LoopCloneChoice;
            return new GenTreeBlock(
                preheader.Id,
                preheader.StartPc,
                preheader.EndPcExclusive,
                preheader.EntryStackDepth,
                preheader.ExitStackDepth,
                GenTreeBlockJumpKind.Always,
                flags,
                statements.ToImmutable(),
                ImmutableArray.Create(targetBlockId),
                ImmutableArray.Create(targetPc),
                preheader.RegionPc);
        }

        private static GenTreeLoopDuplicationResult MarkSlowLoop(GenTreeLoopDuplicationResult slowLoop)
        {
            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(slowLoop.Blocks.Length);
            for (int i = 0; i < slowLoop.Blocks.Length; i++)
            {
                var block = slowLoop.Blocks[i];
                blocks.Add(new GenTreeBlock(
                    block.Id,
                    block.StartPc,
                    block.EndPcExclusive,
                    block.EntryStackDepth,
                    block.ExitStackDepth,
                    block.JumpKind,
                    block.Flags | GenTreeBlockFlags.LoopCloneSlowPath,
                    block.Statements,
                    block.SuccessorBlockIds,
                    block.SuccessorPcs,
                    block.RegionPc));
            }

            return new GenTreeLoopDuplicationResult(
                blocks.ToImmutable(),
                slowLoop.BlockMap,
                slowLoop.PcMap,
                slowLoop.TreeMap);
        }

        private static GenTreeBlock MarkFastBlock(GenTreeBlock block)
            => new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                block.JumpKind,
                block.Flags | GenTreeBlockFlags.LoopCloneFastPath,
                block.Statements,
                block.SuccessorBlockIds,
                block.SuccessorPcs,
                block.RegionPc);

        private static GenTreeBlock CreateConditionBlock(
            int blockId,
            int pc,
            GenTree condition,
            int successBlockId,
            int successPc,
            int slowBlockId,
            int slowPc,
            GenTreeSyntheticAllocator allocator)
        {
            if (condition.StackKind != GenStackKind.I4)
                throw new InvalidOperationException("Loop versioning condition must produce I4.");

            var branchFalse = new GenTree(
                allocator.AllocateTreeId(),
                GenTreeKind.BranchFalse,
                pc,
                BytecodeOp.Brfalse,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray.Create(condition),
                targetPc: slowPc,
                targetBlockId: slowBlockId);
            var successBranch = GenTreeLoopDuplicator.CreateBranch(
                allocator,
                pc,
                successPc,
                successBlockId);

            return new GenTreeBlock(
                blockId,
                pc,
                pc,
                0,
                0,
                GenTreeBlockJumpKind.Conditional,
                GenTreeBlockFlags.LoopCloneChoice,
                ImmutableArray.Create(branchFalse, successBranch),
                ImmutableArray.Create(slowBlockId, successBlockId),
                ImmutableArray.Create(slowPc, successPc),
                pc);
        }

        private static GenTreeBlock CreatePreheader(
            int blockId,
            int pc,
            int targetBlockId,
            int targetPc,
            GenTreeBlockFlags flag,
            GenTreeSyntheticAllocator allocator)
            => new GenTreeBlock(
                blockId,
                pc,
                pc,
                0,
                0,
                GenTreeBlockJumpKind.Always,
                flag | GenTreeBlockFlags.LoopInvertedPreheader,
                ImmutableArray.Create(GenTreeLoopDuplicator.CreateBranch(
                    allocator,
                    pc,
                    targetPc,
                    targetBlockId)),
                ImmutableArray.Create(targetBlockId),
                ImmutableArray.Create(targetPc),
                pc);

        private static void Verify(
            GenTreeMethod method,
            CfgLoop originalLoop,
            int conditionCount,
            int firstConditionBlockId,
            int fastPreheaderBlockId,
            int slowPreheaderBlockId,
            GenTreeLoopDuplicationResult slowLoop)
        {
            if (method.Blocks[originalLoop.Preheader].SuccessorBlockIds.Length != 1)
                throw new InvalidOperationException("Loop versioning produced an invalid entry.");

            if (conditionCount <= 0)
                throw new InvalidOperationException("Loop versioning produced an empty condition chain.");
            if (method.Blocks[originalLoop.Preheader].SuccessorBlockIds[0] != firstConditionBlockId)
                throw new InvalidOperationException("Loop versioning produced an invalid choice entry.");

            if (method.Blocks[fastPreheaderBlockId].SuccessorBlockIds.Length != 1 ||
                method.Blocks[fastPreheaderBlockId].SuccessorBlockIds[0] != originalLoop.Header)
            {
                throw new InvalidOperationException("Loop versioning produced an invalid fast preheader.");
            }

            if (method.Blocks[slowPreheaderBlockId].SuccessorBlockIds.Length != 1 ||
                method.Blocks[slowPreheaderBlockId].SuccessorBlockIds[0] != slowLoop.GetBlock(originalLoop.Header))
            {
                throw new InvalidOperationException("Loop versioning produced an invalid slow preheader.");
            }

            for (int i = 0; i < conditionCount; i++)
            {
                var block = method.Blocks[firstConditionBlockId + i];
                int successBlockId = i + 1 < conditionCount
                    ? firstConditionBlockId + i + 1
                    : fastPreheaderBlockId;
                if (block.JumpKind != GenTreeBlockJumpKind.Conditional ||
                    block.SuccessorBlockIds.Length != 2 ||
                    block.SuccessorPcs.Length != 2 ||
                    block.SuccessorBlockIds[0] != slowPreheaderBlockId ||
                    block.SuccessorPcs[0] != method.Blocks[slowPreheaderBlockId].StartPc ||
                    block.SuccessorBlockIds[1] != successBlockId ||
                    block.SuccessorPcs[1] != method.Blocks[successBlockId].StartPc ||
                    block.Statements.Length != 2 ||
                    block.Statements[0].Kind != GenTreeKind.BranchFalse ||
                    block.Statements[0].TargetBlockId != slowPreheaderBlockId ||
                    block.Statements[0].TargetPc != method.Blocks[slowPreheaderBlockId].StartPc ||
                    block.Statements[1].Kind != GenTreeKind.Branch ||
                    block.Statements[1].TargetBlockId != successBlockId ||
                    block.Statements[1].TargetPc != method.Blocks[successBlockId].StartPc)
                {
                    throw new InvalidOperationException("Loop versioning produced an invalid condition chain.");
                }
            }

            var nodes = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            var treeIds = new HashSet<int>();
            for (int blockId = 0; blockId < method.Blocks.Length; blockId++)
            {
                var block = method.Blocks[blockId];
                if (block.Id != blockId || block.SuccessorBlockIds.Length != block.SuccessorPcs.Length)
                    throw new InvalidOperationException("Loop versioning produced invalid block metadata.");
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    if ((uint)block.SuccessorBlockIds[s] >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException("Loop versioning produced an invalid successor.");
                }
                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyTree(block.Statements[s], block);
            }

            var cfg = ControlFlowGraph.Build(method);
            bool foundFastLoop = false;
            bool foundSlowLoop = false;
            int slowHeaderBlockId = slowLoop.GetBlock(originalLoop.Header);
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                var loop = cfg.NaturalLoops[i];
                if (loop.Header == originalLoop.Header && loop.Preheader == fastPreheaderBlockId)
                    foundFastLoop = loop.IsReducible && loop.IsCanonicalPreheader;
                if (loop.Header == slowHeaderBlockId && loop.Preheader == slowPreheaderBlockId)
                    foundSlowLoop = loop.IsReducible && loop.IsCanonicalPreheader;
            }
            if (!foundFastLoop || !foundSlowLoop)
                throw new InvalidOperationException("Loop versioning did not produce canonical fast and slow loops.");

            void VerifyTree(GenTree node, GenTreeBlock block)
            {
                if (node.Id < 0 || !nodes.Add(node) || !treeIds.Add(node.Id))
                    throw new InvalidOperationException("Loop versioning produced invalid, shared, or repeated trees.");
                if (node.TargetBlockId >= 0)
                {
                    if ((uint)node.TargetBlockId >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException("Loop versioning produced an invalid tree target.");
                    if (node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                    {
                        bool found = false;
                        for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                        {
                            if (block.SuccessorBlockIds[i] == node.TargetBlockId)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                            throw new InvalidOperationException("Loop versioning produced a tree target outside successor metadata.");
                    }
                }
                for (int i = 0; i < node.Operands.Length; i++)
                    VerifyTree(node.Operands[i], block);
            }
        }
    }
}
