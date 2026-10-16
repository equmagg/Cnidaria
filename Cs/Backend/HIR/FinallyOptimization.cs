using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class GenTreeFinallyOptimizer
    {
        private const int MaxClonedBlocks = 8;
        private const int MaxClonedNodes = 64;

        public static GenTreeMethod OptimizeMethod(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.ExceptionHandlers.Length == 0)
                return method;

            var flow = new FinallyFlow(method);
            for (int clause = 0; clause < flow.ClauseCount;)
            {
                if (flow.IsFinally(clause) && (flow.TryRemoveEmptyTry(clause) || flow.TryRemoveEmptyFinally(clause)))
                    flow.RemoveClause(clause);
                else
                    clause++;
            }
            if (flow.Changed)
            {
                method = flow.Build();
                if (method.ExceptionHandlers.Length == 0)
                    return method;
                flow = new FinallyFlow(method);
            }

            for (int clause = 0; clause < flow.ClauseCount; clause++)
            {
                if (flow.IsFinally(clause))
                    flow.TryCloneFinally(clause);
            }
            if (flow.Changed)
            {
                method = flow.Build();
                flow = new FinallyFlow(method);
            }

            for (int clause = 0; clause < flow.ClauseCount;)
            {
                if (flow.TryRemoveUnreachableHandler(clause))
                    flow.RemoveClause(clause);
                else
                    clause++;
            }
            return flow.Changed ? flow.Build() : method;
        }

        private sealed class FinallyFlow
        {
            private readonly GenTreeMethod _method;
            private readonly List<ExceptionHandler> _handlers;
            private readonly GenTreeEhRegion[] _regions;
            private readonly bool[] _removed;
            private readonly GenTree?[] _terminators;
            private readonly Dictionary<int, ImmutableArray<GenTreeBlock>> _inserted = new();
            private GenTreeSyntheticAllocator? _allocator;
            private int _nextBlockId;

            public bool Changed { get; private set; }
            public int ClauseCount => _handlers.Count;

            public FinallyFlow(GenTreeMethod method)
            {
                _method = method;
                _handlers = new List<ExceptionHandler>(method.ExceptionHandlers);
                _regions = new GenTreeEhRegion[method.Blocks.Length];
                for (int b = 0; b < _regions.Length; b++)
                    _regions[b] = method.Blocks[b].Region;
                _removed = new bool[method.Blocks.Length];
                _terminators = new GenTree?[method.Blocks.Length];
                _nextBlockId = method.Blocks.Length;
            }

            public bool IsFinally(int clause) => _handlers[clause].CatchTypeToken < 0;

            // A try whose only block leaves through the callfinally of its finally runs that finally inline
            public bool TryRemoveEmptyTry(int clause)
            {
                int tryBlock = -1;
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (_removed[b] || !InTry(b, clause))
                        continue;
                    if (tryBlock >= 0)
                        return false;
                    tryBlock = b;
                }
                if (tryBlock < 0 || _regions[tryBlock].TryIndex != clause ||
                    _method.Blocks[tryBlock].Statements.Length != 1 || Terminator(tryBlock).Kind != GenTreeKind.Branch)
                {
                    return false;
                }

                int callFinally = Terminator(tryBlock).TargetBlockId;
                int entry = HandlerEntry(clause);
                if (entry < 0 || HandlerHoldsClause(clause) || !CallsFinally(callFinally, entry))
                    return false;

                int continuation = Terminator(callFinally + 1).TargetBlockId;
                GenTreeEhRegion outer = _regions[callFinally];
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (_removed[b] || !InHandler(b, clause))
                        continue;
                    _regions[b] = outer;
                    if (EndsWith(b, GenTreeKind.EndFinally))
                        Retarget(b, continuation);
                }
                Retarget(tryBlock, entry);
                Remove(callFinally);
                Remove(callFinally + 1);
                return true;
            }

            // Callers of a finally that does nothing continue straight to where it would return
            public bool TryRemoveEmptyFinally(int clause)
            {
                int entry = HandlerEntry(clause);
                if (entry < 0)
                    return false;
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (!_removed[b] && b != entry && InHandler(b, clause))
                        return false;
                }
                if (_method.Blocks[entry].Statements.Length != 1 || Terminator(entry).Kind != GenTreeKind.EndFinally)
                    return false;

                for (int b = 0; b < _regions.Length; b++)
                {
                    if (_removed[b] || !CallsFinally(b, entry))
                        continue;
                    Retarget(b, Terminator(b + 1).TargetBlockId);
                    Remove(b + 1);
                }
                Remove(entry);
                return true;
            }

            // The last leave of the try runs a copy of the finally in the region of its callfinally instead of calling it
            public bool TryCloneFinally(int clause)
            {
                int entry = HandlerEntry(clause);
                if (entry < 0 || HandlerHoldsClause(clause))
                    return false;

                var handlerBlocks = ImmutableArray.CreateBuilder<int>();
                int nodes = 0;
                bool returns = false;
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (_removed[b] || !InHandler(b, clause))
                        continue;
                    GenTreeBlock block = _method.Blocks[b];
                    nodes += block.LinearNodes.Length;
                    if (_terminators[b] is not null ||
                        (block.Flags & (GenTreeBlockFlags.HasStackEntry | GenTreeBlockFlags.HasStackExit)) != 0 ||
                        handlerBlocks.Count == MaxClonedBlocks ||
                        nodes > MaxClonedNodes)
                    {
                        return false;
                    }
                    handlerBlocks.Add(b);
                    returns |= block.JumpKind == GenTreeBlockJumpKind.EndFinally;
                }
                if (!returns)
                    return false;

                int callFinally = -1;
                for (int b = _regions.Length - 1; b >= 0 && callFinally < 0; b--)
                {
                    if (!_removed[b] && InTry(b, clause) && EndsWith(b, GenTreeKind.Branch) && CallsFinally(Terminator(b).TargetBlockId, entry))
                        callFinally = Terminator(b).TargetBlockId;
                }
                if (callFinally < 0)
                    return false;

                _allocator ??= GenTreeSyntheticAllocator.Create(_method);
                var copy = GenTreeLoopDuplicator.DuplicateBlocks(_method, handlerBlocks.ToImmutable(), _nextBlockId, _allocator);
                _nextBlockId += handlerBlocks.Count;

                int continuation = Terminator(callFinally + 1).TargetBlockId;
                int continuationPc = _method.Blocks[continuation].StartPc;
                GenTreeEhRegion region = _regions[callFinally];
                var clones = ImmutableArray.CreateBuilder<GenTreeBlock>(copy.Blocks.Length);
                foreach (GenTreeBlock clone in copy.Blocks)
                {
                    ImmutableArray<GenTree> statements = clone.Statements;
                    ImmutableArray<int> successors = clone.SuccessorBlockIds;
                    ImmutableArray<int> successorPcs = clone.SuccessorPcs;
                    GenTreeBlockJumpKind jumpKind = clone.JumpKind;
                    if (jumpKind == GenTreeBlockJumpKind.EndFinally)
                    {
                        statements = statements.SetItem(statements.Length - 1,
                            GenTreeLoopDuplicator.CreateBranch(_allocator, clone.StartPc, continuationPc, continuation));
                        successors = ImmutableArray.Create(continuation);
                        successorPcs = ImmutableArray.Create(continuationPc);
                        jumpKind = GenTreeBlockJumpKind.Always;
                    }
                    clones.Add(new GenTreeBlock(
                        clone.Id,
                        clone.StartPc,
                        clone.EndPcExclusive,
                        clone.EntryStackDepth,
                        clone.ExitStackDepth,
                        jumpKind,
                        clone.Flags,
                        statements,
                        successors,
                        successorPcs,
                        region));
                }

                _inserted.Add(callFinally, clones.MoveToImmutable());
                Retarget(callFinally, copy.GetBlock(entry), copy.GetPc(entry));
                Remove(callFinally + 1);
                return true;
            }

            // Nothing reaches a handler whose try cannot throw and whose finally no callfinally runs
            public bool TryRemoveUnreachableHandler(int clause)
            {
                int entry = HandlerEntry(clause);
                if (entry < 0 || HandlerHoldsClause(clause))
                    return false;
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (_removed[b])
                        continue;
                    if (InTry(b, clause) && ControlFlowGraph.BlockMayThrow(_method.Blocks[b]))
                        return false;
                    if (CallsFinally(b, entry))
                        return false;
                }

                for (int b = 0; b < _regions.Length; b++)
                {
                    if (!_removed[b] && InHandler(b, clause))
                        Remove(b);
                }
                return true;
            }

            // Blocks of the removed clause move to the innermost clause enclosing it, as the importer would have chosen
            public void RemoveClause(int clause)
            {
                ExceptionHandler removed = _handlers[clause];
                int parentTry = -1;
                int parentHandler = -1;
                for (int k = 0; k < _handlers.Count; k++)
                {
                    if (k == clause)
                        continue;
                    ExceptionHandler h = _handlers[k];
                    if (Contains(h.TryStartPc, h.TryEndPc, removed.TryStartPc, removed.TryEndPc) &&
                        (parentTry < 0 || h.TryEndPc - h.TryStartPc < _handlers[parentTry].TryEndPc - _handlers[parentTry].TryStartPc))
                    {
                        parentTry = k;
                    }
                    if (Contains(h.HandlerStartPc, h.HandlerEndPc, removed.HandlerStartPc, removed.HandlerEndPc) &&
                        (parentHandler < 0 || h.HandlerEndPc - h.HandlerStartPc < _handlers[parentHandler].HandlerEndPc - _handlers[parentHandler].HandlerStartPc))
                    {
                        parentHandler = k;
                    }
                }

                for (int b = 0; b < _regions.Length; b++)
                {
                    GenTreeEhRegion region = _regions[b];
                    _regions[b] = new GenTreeEhRegion(
                        Shift(region.TryIndex == clause ? parentTry : region.TryIndex, clause),
                        Shift(region.HandlerIndex == clause ? parentHandler : region.HandlerIndex, clause));
                }
                _handlers.RemoveAt(clause);
                Changed = true;
            }

            public GenTreeMethod Build()
            {
                var order = new List<(GenTreeBlock Block, GenTreeEhRegion Region, GenTree? Terminator)>(_nextBlockId);
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (!_removed[b])
                        order.Add((_method.Blocks[b], _regions[b], _terminators[b]));
                    if (_inserted.TryGetValue(b, out var clones))
                    {
                        foreach (GenTreeBlock clone in clones)
                            order.Add((clone, clone.Region, null));
                    }
                }

                var firstTry = new int[_handlers.Count];
                var firstHandler = new int[_handlers.Count];
                Array.Fill(firstTry, -1);
                Array.Fill(firstHandler, -1);
                for (int i = 0; i < order.Count; i++)
                {
                    for (int k = 0; k < _handlers.Count; k++)
                    {
                        if (firstTry[k] < 0 && InTry(order[i].Region, k))
                            firstTry[k] = i;
                        if (firstHandler[k] < 0 && InHandler(order[i].Region, k))
                            firstHandler[k] = i;
                    }
                }

                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(order.Count);
                for (int i = 0; i < order.Count; i++)
                {
                    (GenTreeBlock block, GenTreeEhRegion region, GenTree? terminator) = order[i];
                    GenTreeBlockFlags flags = block.Flags & ~(GenTreeBlockFlags.TryEntry | GenTreeBlockFlags.HandlerEntry |
                        GenTreeBlockFlags.InTryRegion | GenTreeBlockFlags.InHandlerRegion);
                    if (region.TryIndex >= 0) flags |= GenTreeBlockFlags.InTryRegion;
                    if (region.HandlerIndex >= 0) flags |= GenTreeBlockFlags.InHandlerRegion;
                    if (Array.IndexOf(firstTry, i) >= 0) flags |= GenTreeBlockFlags.TryEntry;
                    if (Array.IndexOf(firstHandler, i) >= 0 || StartsFilteredHandler(block)) flags |= GenTreeBlockFlags.HandlerEntry;

                    ImmutableArray<GenTree> statements = block.Statements;
                    ImmutableArray<int> successors = block.SuccessorBlockIds;
                    ImmutableArray<int> successorPcs = block.SuccessorPcs;
                    GenTreeBlockJumpKind jumpKind = block.JumpKind;
                    bool returnsChanged = jumpKind == GenTreeBlockJumpKind.EndFinally && LosesFinallyReturns(ref successors, ref successorPcs);
                    if (terminator is null && !returnsChanged && region == block.Region && flags == block.Flags)
                    {
                        blocks.Add(block);
                        continue;
                    }

                    if (terminator is not null)
                    {
                        statements = statements.SetItem(statements.Length - 1, terminator);
                        successors = ImmutableArray.Create(terminator.TargetBlockId);
                        successorPcs = ImmutableArray.Create(terminator.TargetPc);
                        jumpKind = GenTreeBlockJumpKind.Always;
                    }
                    blocks.Add(new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        jumpKind,
                        flags,
                        statements,
                        successors,
                        successorPcs,
                        region));
                }
                return GenTreeCriticalEdgeSplitter.RenumberBlocks(_method, blocks.ToImmutable(), _handlers.ToImmutableArray());
            }

            // A finally no longer returns to the callfinally return blocks this phase removed
            private bool LosesFinallyReturns(ref ImmutableArray<int> successors, ref ImmutableArray<int> successorPcs)
            {
                int kept = 0;
                foreach (int successor in successors)
                {
                    if (!_removed[successor])
                        kept++;
                }
                if (kept == successors.Length)
                    return false;

                var keptSuccessors = ImmutableArray.CreateBuilder<int>(kept);
                var keptPcs = ImmutableArray.CreateBuilder<int>(kept);
                for (int i = 0; i < successors.Length; i++)
                {
                    if (_removed[successors[i]])
                        continue;
                    keptSuccessors.Add(successors[i]);
                    keptPcs.Add(successorPcs[i]);
                }
                successors = keptSuccessors.MoveToImmutable();
                successorPcs = keptPcs.MoveToImmutable();
                return true;
            }

            private GenTree Terminator(int block)
                => _terminators[block] ?? _method.Blocks[block].Statements[^1];

            private bool EndsWith(int block, GenTreeKind kind)
            {
                if (_terminators[block] is GenTree terminator)
                    return terminator.Kind == kind;
                ImmutableArray<GenTree> statements = _method.Blocks[block].Statements;
                return statements.Length != 0 && statements[^1].Kind == kind;
            }

            private bool CallsFinally(int block, int entry)
                => EndsWith(block, GenTreeKind.CallFinally) && Terminator(block).TargetBlockId == entry;

            private void Retarget(int block, int target)
                => Retarget(block, target, _method.Blocks[target].StartPc);

            private void Retarget(int block, int target, int targetPc)
            {
                GenTree old = Terminator(block);
                _terminators[block] = new GenTree(
                    old.Id,
                    GenTreeKind.Branch,
                    old.Pc,
                    GenTreeOperator.None,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: GenTreeFlags.ControlFlow | GenTreeFlags.Ordered,
                    operands: ImmutableArray<GenTree>.Empty,
                    targetPc: targetPc,
                    targetBlockId: target);
            }

            private void Remove(int block)
            {
                _removed[block] = true;
                Changed = true;
            }

            private bool StartsFilteredHandler(GenTreeBlock block)
            {
                foreach (ExceptionHandler handler in _handlers)
                {
                    if (handler.FilterEndPc == block.StartPc)
                        return true;
                }
                return false;
            }

            private bool HandlerHoldsClause(int clause)
            {
                ExceptionHandler handler = _handlers[clause];
                for (int k = 0; k < _handlers.Count; k++)
                {
                    if (k != clause && Contains(handler.HandlerStartPc, handler.HandlerEndPc, _handlers[k].TryStartPc, _handlers[k].TryEndPc))
                        return true;
                }
                return false;
            }

            private int HandlerEntry(int clause)
            {
                for (int b = 0; b < _regions.Length; b++)
                {
                    if (!_removed[b] && InHandler(b, clause))
                        return b;
                }
                return -1;
            }

            private bool InTry(int block, int clause) => InTry(_regions[block], clause);

            private bool InHandler(int block, int clause) => InHandler(_regions[block], clause);

            private bool InTry(GenTreeEhRegion region, int clause)
            {
                int index = region.TryIndex;
                return index >= 0 && Contains(_handlers[clause].TryStartPc, _handlers[clause].TryEndPc, _handlers[index].TryStartPc, _handlers[index].TryEndPc);
            }

            private bool InHandler(GenTreeEhRegion region, int clause)
            {
                int index = region.HandlerIndex;
                return index >= 0 &&
                    Contains(_handlers[clause].HandlerStartPc, _handlers[clause].HandlerEndPc, _handlers[index].HandlerStartPc, _handlers[index].HandlerEndPc);
            }

            private static int Shift(int index, int removed) => index > removed ? index - 1 : index;

            private static bool Contains(int outerStart, int outerEnd, int innerStart, int innerEnd)
                => outerStart <= innerStart && innerEnd <= outerEnd;
        }
    }
}
