using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class GenTreeSyntheticAllocator
    {
        private int _nextTreeId;
        private int _nextPc;

        private GenTreeSyntheticAllocator(int nextTreeId, int nextPc)
        {
            _nextTreeId = nextTreeId;
            _nextPc = nextPc;
        }

        public static GenTreeSyntheticAllocator Create(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            return new GenTreeSyntheticAllocator(
                GenTreeCriticalEdgeSplitter.NextSyntheticTreeId(method),
                GenTreeCriticalEdgeSplitter.FirstSyntheticPc(method));
        }

        public int AllocateTreeId()
        {
            int id = _nextTreeId;
            _nextTreeId = checked(_nextTreeId + 1);
            return id;
        }

        public int AllocatePc()
        {
            int pc = _nextPc;
            _nextPc = checked(_nextPc - 1);
            return pc;
        }

        public void EnsureNextTreeIdAtLeast(int nextTreeId)
        {
            if (nextTreeId < 0)
                throw new ArgumentOutOfRangeException(nameof(nextTreeId));
            if (_nextTreeId < nextTreeId)
                _nextTreeId = nextTreeId;
        }
    }

    internal sealed class GenTreeLoopDuplicationResult
    {
        public ImmutableArray<GenTreeBlock> Blocks { get; }
        public IReadOnlyDictionary<int, int> BlockMap { get; }
        public IReadOnlyDictionary<int, int> PcMap { get; }
        public IReadOnlyDictionary<int, int> TreeMap { get; }

        public GenTreeLoopDuplicationResult(
            ImmutableArray<GenTreeBlock> blocks,
            IReadOnlyDictionary<int, int> blockMap,
            IReadOnlyDictionary<int, int> pcMap,
            IReadOnlyDictionary<int, int> treeMap)
        {
            Blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;
            BlockMap = blockMap ?? throw new ArgumentNullException(nameof(blockMap));
            PcMap = pcMap ?? throw new ArgumentNullException(nameof(pcMap));
            TreeMap = treeMap ?? throw new ArgumentNullException(nameof(treeMap));
        }

        public int GetBlock(int originalBlockId)
            => BlockMap.TryGetValue(originalBlockId, out int cloneBlockId)
                ? cloneBlockId
                : throw new KeyNotFoundException();

        public int GetPc(int originalBlockId)
            => PcMap.TryGetValue(originalBlockId, out int clonePc)
                ? clonePc
                : throw new KeyNotFoundException();

        public int GetTree(int originalTreeId)
            => TreeMap.TryGetValue(originalTreeId, out int cloneTreeId)
                ? cloneTreeId
                : throw new KeyNotFoundException();
    }

    internal static class GenTreeLoopDuplicator
    {
        private const GenTreeBlockFlags NonDuplicableBlockFlags =
            GenTreeBlockFlags.Entry |
            GenTreeBlockFlags.TryEntry |
            GenTreeBlockFlags.HandlerEntry;

        public static bool CanDuplicate(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (!loop.IsReducible || loop.Blocks.IsDefaultOrEmpty)
                return false;
            if ((uint)loop.Header >= (uint)method.Blocks.Length || !loop.Contains(loop.Header))
                return false;
            if (!ReferenceEquals(cfg.Method, method) || cfg.Blocks.Length != method.Blocks.Length)
                return false;

            var seenBlocks = new HashSet<int>();
            var seenNodes = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            var seenTreeIds = new HashSet<int>();
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                if ((uint)blockId >= (uint)method.Blocks.Length || !seenBlocks.Add(blockId))
                    return false;

                var block = method.Blocks[blockId];
                var cfgBlock = cfg.Blocks[blockId];
                if (block.Id != blockId || cfgBlock.Id != blockId)
                    return false;
                if (block.EntryStackDepth != 0 ||
                    block.ExitStackDepth != 0 ||
                    (block.Flags & (GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit)) != 0)
                {
                    return false;
                }
                if (!IsOutsideExceptionRegions(block, cfgBlock))
                    return false;
                if (GenTreeCriticalEdgeSplitter.IsExceptionRegionEntry(cfg, blockId))
                    return false;
                if (!HasCloneableFlowShape(method, cfg, block))
                    return false;
                for (int s = 0; s < block.Statements.Length; s++)
                {
                    if (!HasCloneableTree(method, block, block.Statements[s], seenNodes, seenTreeIds))
                        return false;
                }

                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    int successor = block.SuccessorBlockIds[s];
                    if ((uint)successor >= (uint)method.Blocks.Length)
                        return false;
                    if (loop.Contains(successor))
                        continue;

                    var successorBlock = method.Blocks[successor];
                    if (successorBlock.EntryStackDepth != 0 ||
                        (successorBlock.Flags & GenTreeBlockFlags.HasStackEntry) != 0 ||
                        !IsOutsideExceptionRegions(successorBlock, cfg.Blocks[successor]) ||
                        GenTreeCriticalEdgeSplitter.IsExceptionRegionEntry(cfg, successor))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static GenTreeLoopDuplicationResult Duplicate(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            int firstBlockId,
            GenTreeSyntheticAllocator allocator)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (allocator is null)
                throw new ArgumentNullException(nameof(allocator));
            if (!CanDuplicate(method, cfg, loop))
                throw new InvalidOperationException("Loop cannot be duplicated.");
            if (firstBlockId < method.Blocks.Length)
                throw new ArgumentOutOfRangeException(nameof(firstBlockId));
            _ = checked(firstBlockId + loop.Blocks.Length);

            var seenBlocks = new HashSet<int>();
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                if ((uint)blockId >= (uint)method.Blocks.Length ||
                    method.Blocks[blockId].Id != blockId ||
                    !seenBlocks.Add(blockId))
                {
                    throw new InvalidOperationException("Loop duplication received an invalid loop block set.");
                }
            }
            if (!seenBlocks.Contains(loop.Header))
                throw new InvalidOperationException("Loop duplication received a loop without its header.");

            var blockMap = new Dictionary<int, int>(loop.Blocks.Length);
            var pcMap = new Dictionary<int, int>(loop.Blocks.Length);
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int originalBlockId = loop.Blocks[i];
                blockMap.Add(originalBlockId, checked(firstBlockId + i));
                pcMap.Add(originalBlockId, allocator.AllocatePc());
            }

            var treeMap = new Dictionary<int, int>();
            var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(loop.Blocks.Length);
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int originalBlockId = loop.Blocks[i];
                blocks.Add(CloneBlock(
                    method,
                    method.Blocks[originalBlockId],
                    blockMap[originalBlockId],
                    pcMap[originalBlockId],
                    blockMap,
                    pcMap,
                    treeMap,
                    allocator));
            }

            var result = new GenTreeLoopDuplicationResult(
                blocks.ToImmutable(),
                blockMap,
                pcMap,
                treeMap);
            Verify(method, loop, result);
            return result;
        }

        internal static GenTree CloneTreeWithExistingId(
            GenTree node,
            ImmutableArray<GenTree> operands,
            GenTreeFlags flags,
            int targetBlockId,
            int targetPc)
            => CloneTree(node, node.Id, operands, flags, targetBlockId, targetPc);

        internal static GenTree CloneTree(
            GenTree node,
            int id,
            ImmutableArray<GenTree> operands,
            GenTreeFlags flags,
            int targetBlockId,
            int targetPc)
        {
            var clone = new GenTree(
                id,
                node.Kind,
                node.Pc,
                node.SourceOp,
                node.Type,
                node.StackKind,
                flags,
                operands,
                int32: node.Int32,
                int64: node.Int64,
                text: node.Text,
                runtimeType: node.RuntimeType,
                field: node.Field,
                method: node.Method,
                convKind: node.ConvKind,
                convFlags: node.ConvFlags,
                targetPc: targetPc,
                targetBlockId: targetBlockId,
                boundsCheckIndexOverride: node.BoundsCheckIndexOverride);
            clone.LocalDescriptor = node.LocalDescriptor;
            return clone;
        }

        private static GenTreeFlags PrepareDuplicatedFlags(GenTreeFlags flags)
            => flags & ~(
                GenTreeFlags.AssertionProperties |
                GenTreeFlags.VarDef |
                GenTreeFlags.VarUseAsg |
                GenTreeFlags.VarDeath |
                GenTreeFlags.Prolog |
                GenTreeFlags.MakeCse |
                GenTreeFlags.ExplicitInit);

        internal static GenTree CreateBranch(
            GenTreeSyntheticAllocator allocator,
            int pc,
            int targetPc,
            int targetBlockId)
            => new GenTree(
                allocator.AllocateTreeId(),
                GenTreeKind.Branch,
                pc,
                BytecodeOp.Br,
                type: null,
                stackKind: GenStackKind.Void,
                flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                operands: ImmutableArray<GenTree>.Empty,
                targetPc: targetPc,
                targetBlockId: targetBlockId);

        internal static bool TryGetConditionalTransfer(
            ImmutableArray<GenTree> statements,
            out GenTree conditional,
            out GenTree? appended,
            out int conditionalIndex)
        {
            conditional = null!;
            appended = null;
            conditionalIndex = -1;
            if (statements.IsDefaultOrEmpty)
                return false;

            int last = statements.Length - 1;
            if (statements[last].Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                conditional = statements[last];
                conditionalIndex = last;
                return true;
            }

            if (last > 0 &&
                statements[last].Kind == GenTreeKind.Branch &&
                statements[last - 1].Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
            {
                conditional = statements[last - 1];
                appended = statements[last];
                conditionalIndex = last - 1;
                return true;
            }

            return false;
        }

        internal static bool TryGetLogicalSuccessors(
            CfgBlock block,
            GenTree conditional,
            GenTree? appended,
            out int trueSuccessor,
            out int falseSuccessor)
        {
            trueSuccessor = -1;
            falseSuccessor = -1;

            int branchTarget = conditional.TargetBlockId;
            int otherTarget = -1;
            if (appended is not null)
            {
                otherTarget = appended.TargetBlockId;
            }
            else
            {
                for (int i = 0; i < block.Successors.Length; i++)
                {
                    var edge = block.Successors[i];
                    if (edge.Kind == CfgEdgeKind.Exception || edge.ToBlockId == branchTarget)
                        continue;
                    if (otherTarget >= 0 && otherTarget != edge.ToBlockId)
                        return false;
                    otherTarget = edge.ToBlockId;
                }
            }

            if (branchTarget < 0 || otherTarget < 0 || branchTarget == otherTarget)
                return false;

            if (conditional.Kind == GenTreeKind.BranchTrue)
            {
                trueSuccessor = branchTarget;
                falseSuccessor = otherTarget;
            }
            else if (conditional.Kind == GenTreeKind.BranchFalse)
            {
                trueSuccessor = otherTarget;
                falseSuccessor = branchTarget;
            }
            else
            {
                return false;
            }

            return true;
        }

        private static bool HasCloneableTree(
            GenTreeMethod method,
            GenTreeBlock block,
            GenTree node,
            HashSet<GenTree> seenNodes,
            HashSet<int> seenTreeIds)
        {
            if (node.Id < 0 || !seenNodes.Add(node) || !seenTreeIds.Add(node.Id))
                return false;

            if (node.TargetBlockId >= 0)
            {
                if ((uint)node.TargetBlockId >= (uint)method.Blocks.Length)
                    return false;
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
                        return false;
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (!HasCloneableTree(method, block, node.Operands[i], seenNodes, seenTreeIds))
                    return false;
            }
            return true;
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

        private static bool HasCloneableFlowShape(GenTreeMethod method, ControlFlowGraph cfg, GenTreeBlock block)
        {
            if (block.SuccessorBlockIds.Length != block.SuccessorPcs.Length)
                return false;

            var distinctSuccessors = new HashSet<int>();
            for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
            {
                int successor = block.SuccessorBlockIds[s];
                if ((uint)successor >= (uint)method.Blocks.Length)
                    return false;
                distinctSuccessors.Add(successor);
            }

            switch (block.JumpKind)
            {
                case GenTreeBlockJumpKind.None:
                case GenTreeBlockJumpKind.FallThrough:
                    return block.SuccessorBlockIds.Length == 1 &&
                           distinctSuccessors.Count == 1 &&
                           !ContainsControlTransfer(block.Statements, block.Statements.Length);

                case GenTreeBlockJumpKind.Always:
                    {
                        if (block.SuccessorBlockIds.Length != 1 ||
                            distinctSuccessors.Count != 1 ||
                            block.Statements.IsDefaultOrEmpty)
                        {
                            return false;
                        }

                        int branchIndex = block.Statements.Length - 1;
                        var branch = block.Statements[branchIndex];
                        return branch.Kind == GenTreeKind.Branch &&
                               branch.Operands.Length == 0 &&
                               branch.SourceOp != BytecodeOp.Leave &&
                               distinctSuccessors.Contains(branch.TargetBlockId) &&
                               !ContainsControlTransfer(block.Statements, branchIndex);
                    }

                case GenTreeBlockJumpKind.Conditional:
                    {
                        if (block.SuccessorBlockIds.Length != 2 ||
                            distinctSuccessors.Count != 2 ||
                            !TryGetConditionalTransfer(block.Statements, out var conditional, out var appended, out int conditionalIndex) ||
                            conditional.Operands.Length != 1 ||
                            !distinctSuccessors.Contains(conditional.TargetBlockId) ||
                            !TryGetLogicalSuccessors(cfg.Blocks[block.Id], conditional, appended, out _, out _) ||
                            ContainsControlTransfer(block.Statements, conditionalIndex))
                        {
                            return false;
                        }

                        return appended is null ||
                               (appended.Operands.Length == 0 &&
                                appended.SourceOp != BytecodeOp.Leave &&
                                distinctSuccessors.Contains(appended.TargetBlockId));
                    }

                case GenTreeBlockJumpKind.Return:
                case GenTreeBlockJumpKind.Throw:
                    {
                        if (distinctSuccessors.Count != 0 || block.Statements.IsDefaultOrEmpty)
                            return false;

                        int transferIndex = block.Statements.Length - 1;
                        GenTreeKind expectedKind = block.JumpKind == GenTreeBlockJumpKind.Return
                            ? GenTreeKind.Return
                            : GenTreeKind.Throw;
                        return block.Statements[transferIndex].Kind == expectedKind &&
                               !ContainsControlTransfer(block.Statements, transferIndex);
                    }

                default:
                    return false;
            }
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

        private static GenTreeBlock CloneBlock(
            GenTreeMethod method,
            GenTreeBlock original,
            int cloneId,
            int clonePc,
            IReadOnlyDictionary<int, int> blockMap,
            IReadOnlyDictionary<int, int> pcMap,
            Dictionary<int, int> treeMap,
            GenTreeSyntheticAllocator allocator)
        {
            var successors = ImmutableArray.CreateBuilder<int>(original.SuccessorBlockIds.Length);
            var successorPcs = ImmutableArray.CreateBuilder<int>(original.SuccessorBlockIds.Length);
            for (int i = 0; i < original.SuccessorBlockIds.Length; i++)
            {
                int successor = original.SuccessorBlockIds[i];
                if (blockMap.TryGetValue(successor, out int mapped))
                {
                    successors.Add(mapped);
                    successorPcs.Add(pcMap[successor]);
                }
                else
                {
                    successors.Add(successor);
                    successorPcs.Add(method.Blocks[successor].StartPc);
                }
            }

            var statements = ImmutableArray.CreateBuilder<GenTree>(original.Statements.Length + 1);
            for (int i = 0; i < original.Statements.Length; i++)
            {
                statements.Add(CloneTreeRecursive(
                    method,
                    original.Statements[i],
                    blockMap,
                    pcMap,
                    treeMap,
                    allocator));
            }

            GenTreeBlockJumpKind jumpKind = original.JumpKind;
            MakeTransferExplicit(successors, successorPcs, statements, ref jumpKind, allocator, clonePc);

            GenTreeBlockFlags flags = original.Flags & ~NonDuplicableBlockFlags;
            return new GenTreeBlock(
                cloneId,
                clonePc,
                clonePc,
                original.EntryStackDepth,
                original.ExitStackDepth,
                jumpKind,
                flags,
                statements.ToImmutable(),
                successors.ToImmutable(),
                successorPcs.ToImmutable(),
                clonePc);
        }

        private static GenTree CloneTreeRecursive(
            GenTreeMethod method,
            GenTree node,
            IReadOnlyDictionary<int, int> blockMap,
            IReadOnlyDictionary<int, int> pcMap,
            Dictionary<int, int> treeMap,
            GenTreeSyntheticAllocator allocator)
        {
            var operands = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
            for (int i = 0; i < node.Operands.Length; i++)
            {
                operands.Add(CloneTreeRecursive(
                    method,
                    node.Operands[i],
                    blockMap,
                    pcMap,
                    treeMap,
                    allocator));
            }

            int targetBlockId = node.TargetBlockId;
            int targetPc = node.TargetPc;
            if (targetBlockId >= 0 && blockMap.TryGetValue(targetBlockId, out int mapped))
            {
                targetPc = pcMap[targetBlockId];
                targetBlockId = mapped;
            }
            else if (targetBlockId >= 0 && (uint)targetBlockId < (uint)method.Blocks.Length)
            {
                targetPc = method.Blocks[targetBlockId].StartPc;
            }

            int cloneId = allocator.AllocateTreeId();
            if (!treeMap.TryAdd(node.Id, cloneId))
                throw new InvalidOperationException("Loop duplication encountered a repeated tree id.");

            return CloneTree(
                node,
                cloneId,
                operands.ToImmutable(),
                PrepareDuplicatedFlags(node.Flags),
                targetBlockId,
                targetPc);
        }

        private static void MakeTransferExplicit(
            ImmutableArray<int>.Builder successors,
            ImmutableArray<int>.Builder successorPcs,
            ImmutableArray<GenTree>.Builder statements,
            ref GenTreeBlockJumpKind jumpKind,
            GenTreeSyntheticAllocator allocator,
            int pc)
        {
            if (jumpKind == GenTreeBlockJumpKind.Conditional)
            {
                if (!TryGetConditionalTransfer(statements.ToImmutable(), out var conditional, out var appended, out _))
                    throw new InvalidOperationException("Malformed duplicated conditional block.");
                if (appended is not null)
                    return;

                int other = -1;
                int otherPc = -1;
                for (int i = 0; i < successors.Count; i++)
                {
                    if (successors[i] == conditional.TargetBlockId)
                        continue;
                    if (other >= 0 && other != successors[i])
                        throw new InvalidOperationException("Malformed duplicated conditional successors.");
                    other = successors[i];
                    otherPc = successorPcs[i];
                }

                if (other < 0)
                    throw new InvalidOperationException("Missing duplicated conditional fall-through.");
                statements.Add(CreateBranch(allocator, pc, otherPc, other));
                return;
            }

            if (jumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough)
            {
                if (successors.Count != 1)
                    throw new InvalidOperationException("Malformed duplicated fall-through block.");
                statements.Add(CreateBranch(allocator, pc, successorPcs[0], successors[0]));
                jumpKind = GenTreeBlockJumpKind.Always;
                return;
            }

            if (jumpKind == GenTreeBlockJumpKind.Always &&
                (statements.Count == 0 || statements[statements.Count - 1].Kind != GenTreeKind.Branch))
            {
                if (successors.Count != 1)
                    throw new InvalidOperationException("Malformed duplicated unconditional block.");
                statements.Add(CreateBranch(allocator, pc, successorPcs[0], successors[0]));
            }
        }

        private static void Verify(GenTreeMethod method, CfgLoop loop, GenTreeLoopDuplicationResult result)
        {
            if (result.Blocks.Length != loop.Blocks.Length ||
                result.BlockMap.Count != loop.Blocks.Length ||
                result.PcMap.Count != loop.Blocks.Length)
            {
                throw new InvalidOperationException("Loop duplication produced an incomplete block mapping.");
            }

            var cloneIds = new HashSet<int>();
            var clonePcs = new HashSet<int>();
            var cloneTreeIds = new HashSet<int>();
            int originalTreeCount = 0;

            for (int i = 0; i < result.Blocks.Length; i++)
            {
                int originalBlockId = loop.Blocks[i];
                var original = method.Blocks[originalBlockId];
                var clone = result.Blocks[i];

                if (result.GetBlock(originalBlockId) != clone.Id ||
                    result.GetPc(originalBlockId) != clone.StartPc ||
                    clone.RegionPc != clone.StartPc ||
                    !cloneIds.Add(clone.Id) ||
                    !clonePcs.Add(clone.StartPc) ||
                    clone.Id < method.Blocks.Length)
                {
                    throw new InvalidOperationException("Loop duplication produced an invalid block mapping.");
                }
                if (clone.SuccessorBlockIds.Length != original.SuccessorBlockIds.Length ||
                    clone.SuccessorBlockIds.Length != clone.SuccessorPcs.Length)
                {
                    throw new InvalidOperationException("Loop duplication produced inconsistent successor metadata.");
                }

                for (int s = 0; s < original.SuccessorBlockIds.Length; s++)
                {
                    int originalTarget = original.SuccessorBlockIds[s];
                    int expectedTarget = loop.Contains(originalTarget)
                        ? result.GetBlock(originalTarget)
                        : originalTarget;
                    int expectedPc = loop.Contains(originalTarget)
                        ? result.GetPc(originalTarget)
                        : method.Blocks[originalTarget].StartPc;
                    if (clone.SuccessorBlockIds[s] != expectedTarget || clone.SuccessorPcs[s] != expectedPc)
                        throw new InvalidOperationException("Loop duplication produced an invalid successor mapping.");
                }

                for (int s = 0; s < original.Statements.Length; s++)
                    CountOriginalTree(original.Statements[s]);
                for (int s = 0; s < clone.Statements.Length; s++)
                    VerifyTree(clone.Statements[s], clone);
            }

            if (result.TreeMap.Count != originalTreeCount)
                throw new InvalidOperationException("Loop duplication produced an incomplete tree mapping.");
            foreach (var mapping in result.TreeMap)
            {
                if (!cloneTreeIds.Contains(mapping.Value))
                    throw new InvalidOperationException("Loop duplication produced an invalid tree mapping.");
            }

            void CountOriginalTree(GenTree node)
            {
                originalTreeCount++;
                for (int i = 0; i < node.Operands.Length; i++)
                    CountOriginalTree(node.Operands[i]);
            }

            void VerifyTree(GenTree node, GenTreeBlock block)
            {
                if (!cloneTreeIds.Add(node.Id))
                    throw new InvalidOperationException("Loop duplication produced a repeated tree id.");

                if (node.TargetBlockId >= 0)
                {
                    bool found = false;
                    for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                    {
                        if (block.SuccessorBlockIds[i] == node.TargetBlockId &&
                            block.SuccessorPcs[i] == node.TargetPc)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        throw new InvalidOperationException("Loop duplication produced a tree target outside successor metadata.");
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    VerifyTree(node.Operands[i], block);
            }
        }
    }
}
