using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    public sealed class LoopUnrollingOptions
    {
        public static LoopUnrollingOptions Default { get; } = new LoopUnrollingOptions();

        public int MaxIterationCount { get; set; } = 4;
        public int HardIterationLimit { get; set; } = 10;
        public int MaxUnrollSize { get; set; } = 300;
        public int FixedLoopCost { get; set; } = 8;
        public int AnalysisBudget { get; set; } = 100;
        public int MaxRetryPasses { get; set; } = 10;
    }

    internal static class GenTreeLoopUnroller
    {
        private readonly struct LoopCandidate
        {
            public readonly LoopTest Test;
            public readonly LoopIncrement Increment;
            public readonly ImmutableArray<int> IterationValues;

            public LoopCandidate(
                LoopTest test,
                LoopIncrement increment,
                ImmutableArray<int> iterationValues)
            {
                Test = test;
                Increment = increment;
                IterationValues = iterationValues;
            }
        }

        public static GenTreeMethod UnrollLoops(GenTreeMethod method, LoopUnrollingOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= LoopUnrollingOptions.Default;
            ValidateOptions(options);

            if (method.Blocks.IsDefaultOrEmpty)
                return method;

            method = GenTreeLoopInverter.CanonicalizeLoops(method);

            for (int pass = 0; pass < options.MaxRetryPasses; pass++)
            {
                var initialCfg = ControlFlowGraph.Build(method);
                var headers = LoopsInPostOrder(initialCfg, method);
                if (headers.Count == 0)
                    break;

                var retryHeaders = new HashSet<int>();
                bool changed = false;

                for (int i = 0; i < headers.Count; i++)
                {
                    int headerPc = headers[i];
                    if (retryHeaders.Contains(headerPc))
                        continue;

                    var cfg = ControlFlowGraph.Build(method);
                    if (!TryFindLoopByHeaderPc(method, cfg, headerPc, out var loop))
                        continue;

                    var ancestors = CollectAncestorHeaderPcs(method, cfg, loop);
                    if (!TryUnrollLoop(method, cfg, loop, options, out var rewritten))
                        continue;

                    method = GenTreeLoopInverter.CanonicalizeLoops(rewritten);
                    for (int a = 0; a < ancestors.Count; a++)
                        retryHeaders.Add(ancestors[a]);
                    changed = true;
                }

                if (!changed)
                    break;
            }

            return GenTreeMorpher.MorphMethod(method, GenTreeMethodPhase.GlobalMorphedHir);
        }

        private static void ValidateOptions(LoopUnrollingOptions options)
        {
            if (options.MaxIterationCount < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxIterationCount));
            if (options.HardIterationLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(options.HardIterationLimit));
            if (options.MaxIterationCount > options.HardIterationLimit)
                throw new ArgumentOutOfRangeException(nameof(options.MaxIterationCount));
            if (options.MaxUnrollSize < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxUnrollSize));
            if (options.FixedLoopCost < 0)
                throw new ArgumentOutOfRangeException(nameof(options.FixedLoopCost));
            if (options.AnalysisBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.AnalysisBudget));
            if (options.MaxRetryPasses <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRetryPasses));
        }

        private static List<int> LoopsInPostOrder(ControlFlowGraph cfg, GenTreeMethod method)
        {
            var loops = new List<CfgLoop>(cfg.NaturalLoops.Length);
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                loops.Add(cfg.NaturalLoops[i]);

            loops.Sort(static (left, right) =>
            {
                int c = right.Depth.CompareTo(left.Depth);
                if (c != 0)
                    return c;
                c = left.Parent.CompareTo(right.Parent);
                if (c != 0)
                    return c;
                return left.Header.CompareTo(right.Header);
            });

            var result = new List<int>(loops.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < loops.Count; i++)
            {
                int header = loops[i].Header;
                if ((uint)header >= (uint)method.Blocks.Length)
                    continue;
                int pc = method.Blocks[header].StartPc;
                if (seen.Add(pc))
                    result.Add(pc);
            }
            return result;
        }

        private static bool TryFindLoopByHeaderPc(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            int headerPc,
            out CfgLoop loop)
        {
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                var candidate = cfg.NaturalLoops[i];
                if ((uint)candidate.Header < (uint)method.Blocks.Length &&
                    method.Blocks[candidate.Header].StartPc == headerPc)
                {
                    loop = candidate;
                    return true;
                }
            }

            loop = default;
            return false;
        }

        private static List<int> CollectAncestorHeaderPcs(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop)
        {
            var result = new List<int>();
            var visited = new HashSet<int>();
            int parent = loop.Parent;
            while (parent >= 0 && (uint)parent < (uint)cfg.NaturalLoops.Length && visited.Add(parent))
            {
                var ancestor = cfg.NaturalLoops[parent];
                if ((uint)ancestor.Header < (uint)method.Blocks.Length)
                    result.Add(method.Blocks[ancestor.Header].StartPc);
                parent = ancestor.Parent;
            }
            return result;
        }

        private static bool TryUnrollLoop(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopUnrollingOptions options,
            out GenTreeMethod rewritten)
        {
            rewritten = method;

            if (!IsCandidateLoop(method, cfg, loop))
                return false;
            if (!TryAnalyzeLoop(method, cfg, loop, options, out var candidate))
                return false;
            if (candidate.IterationValues.Length > 1 &&
                candidate.IterationValues.Length > options.MaxIterationCount)
            {
                return false;
            }
            if (!IsProfitable(method, loop, candidate.IterationValues.Length, options))
                return false;

            rewritten = RewriteLoop(method, cfg, loop, candidate);
            return !ReferenceEquals(rewritten, method);
        }

        private static bool IsCandidateLoop(GenTreeMethod method, ControlFlowGraph cfg, CfgLoop loop)
        {
            if (!loop.IsReducible || !loop.IsCanonicalPreheader || loop.Blocks.IsDefaultOrEmpty)
                return false;
            if ((uint)loop.Header >= (uint)method.Blocks.Length ||
                (uint)loop.Preheader >= (uint)method.Blocks.Length)
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
            if (!HasSingleSuccessorTransfer(method.Blocks[loop.Preheader], loop.Header))
                return false;
            if ((method.Blocks[loop.Header].Flags & GenTreeBlockFlags.LoopCloneSlowPath) != 0)
                return false;
            if (!GenTreeLoopDuplicator.CanDuplicate(method, cfg, loop))
                return false;

            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                int blockId = loop.Blocks[i];
                var predecessors = cfg.Blocks[blockId].Predecessors;
                for (int p = 0; p < predecessors.Length; p++)
                {
                    var edge = predecessors[p];
                    if (edge.Kind == CfgEdgeKind.Exception)
                        return false;
                    if (!loop.Contains(edge.FromBlockId) &&
                        (blockId != loop.Header || edge.FromBlockId != loop.Preheader))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool HasSingleSuccessorTransfer(GenTreeBlock block, int target)
        {
            if (block.SuccessorBlockIds.Length != 1 || block.SuccessorBlockIds[0] != target)
                return false;

            if (block.JumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough)
                return true;
            if (block.JumpKind != GenTreeBlockJumpKind.Always || block.Statements.IsDefaultOrEmpty)
                return false;

            var branch = block.Statements[block.Statements.Length - 1];
            return branch.Kind == GenTreeKind.Branch && branch.TargetBlockId == target;
        }

        private static bool TryAnalyzeLoop(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopUnrollingOptions options,
            out LoopCandidate candidate)
        {
            candidate = default;

            for (int i = 0; i < loop.Exits.Length; i++)
            {
                int blockId = loop.Exits[i];
                if (!GenTreeLoopIteration.TryAnalyzeLoopTest(method, cfg, loop, blockId, out var test) || !test.Predicate.Limit.IsConstant)
                    continue;
                if (!GenTreeLoopIteration.TryFindIncrement(method, cfg, loop, test, options.AnalysisBudget, out var increment))
                    continue;
                if (!test.Predicate.Slot.Equals(increment.Slot))
                    continue;
                if (!GenTreeLoopIteration.HasSingleLoopDefinition(method, loop, increment))
                    continue;
                if (!GenTreeLoopIteration.TryFindConstantInitializer(method, cfg, loop, increment, out int initialValue))
                    continue;

                bool hasEntryGuard = GenTreeLoopIteration.ValidateEntryGuard(method, cfg, loop, test.Predicate);
                if (!hasEntryGuard &&
                    !EvaluatePredicate(
                        initialValue,
                        test.Predicate.Limit.Constant,
                        test.Predicate.Relop,
                        test.Predicate.Unsigned))
                {
                    continue;
                }
                if (!TryComputeIterationValues(
                        initialValue,
                        test.Predicate,
                        increment,
                        options.HardIterationLimit,
                        out var iterationValues))
                {
                    continue;
                }

                candidate = new LoopCandidate(test, increment, iterationValues);
                return true;
            }

            return false;
        }

        private static bool TryComputeIterationValues(
            int initialValue,
            LoopPredicate predicate,
            LoopIncrement increment,
            int hardIterationLimit,
            out ImmutableArray<int> iterationValues)
        {
            if (!HasCompatibleIterationDirection(initialValue, predicate, increment))
            {
                iterationValues = ImmutableArray<int>.Empty;
                return false;
            }

            var values = ImmutableArray.CreateBuilder<int>();
            int current = initialValue;
            while (EvaluatePredicate(current, predicate.Limit.Constant, predicate.Relop, predicate.Unsigned))
            {
                if (values.Count >= hardIterationLimit)
                {
                    iterationValues = ImmutableArray<int>.Empty;
                    return false;
                }

                values.Add(current);
                if (!TryAdvance(current, increment.Operation, increment.Delta, increment.Domain, out current))
                {
                    iterationValues = ImmutableArray<int>.Empty;
                    return false;
                }
            }

            iterationValues = values.ToImmutable();
            return true;
        }

        private static bool HasCompatibleIterationDirection(
            int initialValue,
            LoopPredicate predicate,
            LoopIncrement increment)
        {
            long step = increment.Operation == IterationOperation.Add
                ? increment.Delta
                : -(long)increment.Delta;
            if (step == 0)
                return false;

            long initial = predicate.Unsigned
                ? unchecked((uint)initialValue)
                : initialValue;
            long limit = predicate.Unsigned
                ? unchecked((uint)predicate.Limit.Constant)
                : predicate.Limit.Constant;

            return step > 0 ? limit >= initial : limit <= initial;
        }

        private static bool EvaluatePredicate(
            int left,
            int right,
            LoopRelop relop,
            bool unsigned)
        {
            if (unsigned)
            {
                uint l = unchecked((uint)left);
                uint r = unchecked((uint)right);
                return relop switch
                {
                    LoopRelop.Equal => l == r,
                    LoopRelop.NotEqual => l != r,
                    LoopRelop.LessThan => l < r,
                    LoopRelop.LessThanOrEqual => l <= r,
                    LoopRelop.GreaterThan => l > r,
                    LoopRelop.GreaterThanOrEqual => l >= r,
                    _ => false,
                };
            }

            return relop switch
            {
                LoopRelop.Equal => left == right,
                LoopRelop.NotEqual => left != right,
                LoopRelop.LessThan => left < right,
                LoopRelop.LessThanOrEqual => left <= right,
                LoopRelop.GreaterThan => left > right,
                LoopRelop.GreaterThanOrEqual => left >= right,
                _ => false,
            };
        }

        private static bool TryAdvance(
            int current,
            IterationOperation operation,
            int delta,
            IntegralDomain domain,
            out int next)
        {
            long currentValue = DomainValue(current, domain);
            long deltaValue = delta;
            long value;
            try
            {
                value = operation == IterationOperation.Add
                    ? checked(currentValue + deltaValue)
                    : checked(currentValue - deltaValue);
            }
            catch (OverflowException)
            {
                next = 0;
                return false;
            }

            if (value < domain.Minimum || value > domain.Maximum)
            {
                next = 0;
                return false;
            }

            next = domain.Unsigned
                ? unchecked((int)(uint)value)
                : unchecked((int)value);
            return true;
        }

        private static long DomainValue(int value, IntegralDomain domain)
        {
            if (!domain.Unsigned)
                return value;
            if (domain.Bits == 32)
                return unchecked((uint)value);
            uint mask = (1u << domain.Bits) - 1u;
            return unchecked((uint)value) & mask;
        }

        private static bool IsProfitable(
            GenTreeMethod method,
            CfgLoop loop,
            int iterationCount,
            LoopUnrollingOptions options)
        {
            if (iterationCount <= 1)
                return true;

            long loopCost = 0;
            long expansion;
            try
            {
                for (int i = 0; i < loop.Blocks.Length; i++)
                {
                    var statements = method.Blocks[loop.Blocks[i]].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        loopCost = checked(loopCost + EstimateCodeSize(statements[s]));
                }

                expansion = checked(loopCost * iterationCount - checked(loopCost + options.FixedLoopCost));
            }
            catch (OverflowException)
            {
                return false;
            }

            return expansion <= options.MaxUnrollSize;
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
                GenTreeKind.Binary => node.Operator is GenTreeOperator.Div or GenTreeOperator.DivUn or GenTreeOperator.Rem or GenTreeOperator.RemUn ? 6 : 2,
                GenTreeKind.Conv => 2,
                _ => 1,
            };

            for (int i = 0; i < node.Operands.Length; i++)
                cost = checked(cost + EstimateCodeSize(node.Operands[i]));
            return cost;
        }

        private static GenTreeMethod RewriteLoop(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopCandidate candidate)
        {
            var allocator = GenTreeSyntheticAllocator.Create(method);
            int iterationCount = candidate.IterationValues.Length;
            var duplications = new GenTreeLoopDuplicationResult[iterationCount];
            int nextBlockId = method.Blocks.Length;

            for (int i = 0; i < iterationCount; i++)
            {
                duplications[i] = GenTreeLoopDuplicator.Duplicate(
                    method,
                    cfg,
                    loop,
                    nextBlockId,
                    allocator);
                nextBlockId = checked(nextBlockId + loop.Blocks.Length);
            }

            var rewrittenClones = new List<GenTreeBlock>(checked(iterationCount * loop.Blocks.Length));
            for (int i = 0; i < iterationCount; i++)
            {
                int currentValue = candidate.IterationValues[i];
                if (!TryAdvance(
                        currentValue,
                        candidate.Increment.Operation,
                        candidate.Increment.Delta,
                        candidate.Increment.Domain,
                        out int postIncrementValue))
                {
                    throw new InvalidOperationException("Loop unrolling lost a proven induction-variable value.");
                }

                int nextTargetBlockId;
                int nextTargetPc;
                if (i + 1 < iterationCount)
                {
                    nextTargetBlockId = duplications[i + 1].GetBlock(loop.Header);
                    nextTargetPc = duplications[i + 1].GetPc(loop.Header);
                }
                else
                {
                    nextTargetBlockId = candidate.Test.ExitSuccessor;
                    nextTargetPc = method.Blocks[candidate.Test.ExitSuccessor].StartPc;
                }

                var blocks = duplications[i].Blocks;
                int clonedTestBlockId = duplications[i].GetBlock(candidate.Test.BlockId);
                for (int b = 0; b < blocks.Length; b++)
                {
                    rewrittenClones.Add(RewriteClonedBlock(
                        blocks[b],
                        clonedTestBlockId,
                        candidate.Increment.Slot,
                        currentValue,
                        postIncrementValue,
                        candidate.Test.ConditionalIndex,
                        candidate.Test.TestExpressionStatementIndex,
                        nextTargetBlockId,
                        nextTargetPc,
                        allocator));
                }
            }

            int entryTargetBlockId = iterationCount == 0
                ? candidate.Test.ExitSuccessor
                : duplications[0].GetBlock(loop.Header);
            int entryTargetPc = iterationCount == 0
                ? method.Blocks[candidate.Test.ExitSuccessor].StartPc
                : duplications[0].GetPc(loop.Header);

            var loopBlocks = new HashSet<int>();
            for (int i = 0; i < loop.Blocks.Length; i++)
                loopBlocks.Add(loop.Blocks[i]);

            var provisional = ImmutableArray.CreateBuilder<GenTreeBlock>(
                checked(method.Blocks.Length - loop.Blocks.Length + rewrittenClones.Count));
            bool insertedClones = false;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (i == loop.Header && !insertedClones)
                {
                    for (int b = 0; b < rewrittenClones.Count; b++)
                        provisional.Add(rewrittenClones[b]);
                    insertedClones = true;
                }

                if (loopBlocks.Contains(i))
                    continue;

                var block = method.Blocks[i];
                if (i == loop.Preheader)
                    block = RedirectSingleSuccessor(block, loop.Header, entryTargetBlockId, entryTargetPc, allocator);
                provisional.Add(block);
            }

            if (!insertedClones)
            {
                for (int b = 0; b < rewrittenClones.Count; b++)
                    provisional.Add(rewrittenClones[b]);
            }

            var rewritten = GenTreeCriticalEdgeSplitter.RenumberBlocks(method, provisional.ToImmutable());
            VerifyRewrittenLoop(
                rewritten,
                method.Blocks[loop.Preheader].StartPc,
                method.Blocks[candidate.Test.ExitSuccessor].StartPc,
                candidate.Increment.Slot,
                duplications,
                candidate.Test.BlockId,
                loop.Header);
            return rewritten;
        }

        private static GenTreeBlock RewriteClonedBlock(
            GenTreeBlock block,
            int clonedTestBlockId,
            ScalarSlot slot,
            int currentValue,
            int postIncrementValue,
            int expectedConditionalIndex,
            int testExpressionStatementIndex,
            int nextTargetBlockId,
            int nextTargetPc,
            GenTreeSyntheticAllocator allocator)
        {
            var statements = ImmutableArray.CreateBuilder<GenTree>(block.Statements.Length);
            for (int i = 0; i < block.Statements.Length; i++)
            {
                int replacement = block.Id == clonedTestBlockId &&
                                  testExpressionStatementIndex < expectedConditionalIndex &&
                                  i == testExpressionStatementIndex
                    ? postIncrementValue
                    : currentValue;
                statements.Add(ReplaceScalarUses(block.Statements[i], slot, replacement));
            }

            var rewrittenStatements = statements.ToImmutable();
            if (block.Id != clonedTestBlockId)
                return NormalizeClonedFlow(block, rewrittenStatements, allocator);

            if (!GenTreeLoopDuplicator.TryGetConditionalTransfer(
                    rewrittenStatements,
                    out _,
                    out var appended,
                    out int conditionalIndex))
            {
                throw new InvalidOperationException("Malformed duplicated loop test block.");
            }
            if (conditionalIndex != expectedConditionalIndex)
                throw new InvalidOperationException("Duplicated loop test changed statement shape.");

            int transferEnd = appended is null ? conditionalIndex + 1 : conditionalIndex + 2;
            if (transferEnd != rewrittenStatements.Length)
                throw new InvalidOperationException("Loop test transfer is not terminal.");

            var finalStatements = ImmutableArray.CreateBuilder<GenTree>(conditionalIndex + 1);
            for (int i = 0; i < conditionalIndex; i++)
                finalStatements.Add(rewrittenStatements[i]);
            finalStatements.Add(GenTreeLoopDuplicator.CreateBranch(
                allocator,
                block.EndPcExclusive,
                nextTargetPc,
                nextTargetBlockId));

            return new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                GenTreeBlockJumpKind.Always,
                block.Flags,
                finalStatements.ToImmutable(),
                ImmutableArray.Create(nextTargetBlockId),
                ImmutableArray.Create(nextTargetPc),
                block.Region);
        }

        private static GenTreeBlock NormalizeClonedFlow(
            GenTreeBlock block,
            ImmutableArray<GenTree> statements,
            GenTreeSyntheticAllocator allocator)
        {
            if (block.JumpKind is GenTreeBlockJumpKind.None or GenTreeBlockJumpKind.FallThrough)
            {
                if (block.SuccessorBlockIds.Length != 1 || block.SuccessorPcs.Length != 1)
                    throw new InvalidOperationException("Malformed duplicated fall-through block.");

                var explicitStatements = ImmutableArray.CreateBuilder<GenTree>(statements.Length + 1);
                explicitStatements.AddRange(statements);
                explicitStatements.Add(GenTreeLoopDuplicator.CreateBranch(
                    allocator,
                    block.EndPcExclusive,
                    block.SuccessorPcs[0],
                    block.SuccessorBlockIds[0]));

                return new GenTreeBlock(
                    block.Id,
                    block.StartPc,
                    block.EndPcExclusive,
                    block.EntryStackDepth,
                    block.ExitStackDepth,
                    GenTreeBlockJumpKind.Always,
                    block.Flags,
                    explicitStatements.ToImmutable(),
                    block.SuccessorBlockIds,
                    block.SuccessorPcs,
                    block.Region);
            }

            if (block.JumpKind == GenTreeBlockJumpKind.Conditional)
            {
                if (!GenTreeLoopDuplicator.TryGetConditionalTransfer(
                        statements,
                        out var conditional,
                        out var appended,
                        out _))
                {
                    throw new InvalidOperationException("Malformed duplicated conditional block.");
                }

                if (appended is null)
                {
                    int otherTarget = -1;
                    int otherPc = 0;
                    for (int i = 0; i < block.SuccessorBlockIds.Length; i++)
                    {
                        if (block.SuccessorBlockIds[i] == conditional.TargetBlockId)
                            continue;
                        if (otherTarget >= 0 && otherTarget != block.SuccessorBlockIds[i])
                            throw new InvalidOperationException("Duplicated conditional has ambiguous fall-through.");
                        otherTarget = block.SuccessorBlockIds[i];
                        otherPc = block.SuccessorPcs[i];
                    }

                    if (otherTarget < 0)
                        throw new InvalidOperationException("Duplicated conditional has no fall-through target.");

                    var explicitStatements = ImmutableArray.CreateBuilder<GenTree>(statements.Length + 1);
                    explicitStatements.AddRange(statements);
                    explicitStatements.Add(GenTreeLoopDuplicator.CreateBranch(
                        allocator,
                        block.EndPcExclusive,
                        otherPc,
                        otherTarget));
                    statements = explicitStatements.ToImmutable();
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
                block.SuccessorBlockIds,
                block.SuccessorPcs,
                block.Region);
        }

        private static GenTree ReplaceScalarUses(GenTree node, ScalarSlot slot, int constant)
        {
            if (GenTreeLoopIteration.IsSlotRead(node, slot))
            {
                return new GenTree(
                    node.Id,
                    GenTreeKind.ConstI4,
                    node.Pc,
                    GenTreeOperator.None,
                    type: null,
                    stackKind: GenStackKind.I4,
                    flags: GenTreeFlags.None,
                    operands: ImmutableArray<GenTree>.Empty,
                    int32: constant);
            }

            if (node.Operands.IsDefaultOrEmpty)
                return node;

            bool changed = false;
            var operands = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
            for (int i = 0; i < node.Operands.Length; i++)
            {
                var rewritten = ReplaceScalarUses(node.Operands[i], slot, constant);
                changed |= !ReferenceEquals(rewritten, node.Operands[i]);
                operands.Add(rewritten);
            }

            if (!changed)
                return node;

            return GenTreeLoopDuplicator.CloneTreeWithExistingId(
                node,
                operands.ToImmutable(),
                node.Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.ExplicitInit),
                node.TargetBlockId,
                node.TargetPc);
        }

        private static GenTreeBlock RedirectSingleSuccessor(
            GenTreeBlock block,
            int oldTarget,
            int newTarget,
            int newTargetPc,
            GenTreeSyntheticAllocator allocator)
        {
            if (!HasSingleSuccessorTransfer(block, oldTarget))
                throw new InvalidOperationException("Malformed canonical loop preheader.");

            var statements = ImmutableArray.CreateBuilder<GenTree>(block.Statements.Length + 1);
            if (block.JumpKind == GenTreeBlockJumpKind.Always)
            {
                int last = block.Statements.Length - 1;
                for (int i = 0; i < last; i++)
                    statements.Add(block.Statements[i]);

                var branch = block.Statements[last];
                statements.Add(GenTreeLoopDuplicator.CloneTreeWithExistingId(
                    branch,
                    branch.Operands,
                    branch.Flags,
                    newTarget,
                    newTargetPc));
            }
            else
            {
                statements.AddRange(block.Statements);
                statements.Add(GenTreeLoopDuplicator.CreateBranch(
                    allocator,
                    block.EndPcExclusive,
                    newTargetPc,
                    newTarget));
            }

            return new GenTreeBlock(
                block.Id,
                block.StartPc,
                block.EndPcExclusive,
                block.EntryStackDepth,
                block.ExitStackDepth,
                GenTreeBlockJumpKind.Always,
                block.Flags,
                statements.ToImmutable(),
                ImmutableArray.Create(newTarget),
                ImmutableArray.Create(newTargetPc),
                block.Region);
        }

        private static void VerifyRewrittenLoop(
            GenTreeMethod method,
            int preheaderPc,
            int exitPc,
            ScalarSlot slot,
            GenTreeLoopDuplicationResult[] duplications,
            int originalTestBlockId,
            int originalHeaderId)
        {
            var cfg = ControlFlowGraph.Build(method);
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("Loop unrolling produced an inconsistent CFG.");

            var treeIds = new HashSet<int>();
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                var block = method.Blocks[i];
                if (block.Id != i)
                    throw new InvalidOperationException($"Loop unrolling produced non-dense block id B{block.Id} at index {i}.");
                if (block.SuccessorBlockIds.Length != block.SuccessorPcs.Length)
                    throw new InvalidOperationException($"Loop unrolling produced mismatched successor metadata in B{block.Id}.");

                var successors = new HashSet<int>();
                for (int s = 0; s < block.SuccessorBlockIds.Length; s++)
                {
                    int successor = block.SuccessorBlockIds[s];
                    if ((uint)successor >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"Loop unrolling produced invalid CFG edge B{block.Id} -> B{successor}.");
                    successors.Add(successor);
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyTree(block.Statements[s], block.Id, successors, treeIds, method.Blocks.Length);
            }

            int preheader = FindBlockByStartPc(method, preheaderPc);
            int exit = FindBlockByStartPc(method, exitPc);
            if (method.Blocks[preheader].JumpKind != GenTreeBlockJumpKind.Always ||
                method.Blocks[preheader].SuccessorBlockIds.Length != 1)
            {
                throw new InvalidOperationException("Loop unrolling produced an invalid preheader transfer.");
            }

            if (duplications.Length == 0)
            {
                if (method.Blocks[preheader].SuccessorBlockIds[0] != exit)
                    throw new InvalidOperationException("Zero-trip loop unrolling did not bypass the loop.");
                return;
            }

            int firstHeader = FindBlockByStartPc(method, duplications[0].GetPc(originalHeaderId));
            if (method.Blocks[preheader].SuccessorBlockIds[0] != firstHeader)
                throw new InvalidOperationException("Loop unrolling did not connect the preheader to the first clone.");

            for (int i = 0; i < duplications.Length; i++)
            {
                int testBlock = FindBlockByStartPc(method, duplications[i].GetPc(originalTestBlockId));
                int expected = i + 1 < duplications.Length
                    ? FindBlockByStartPc(method, duplications[i + 1].GetPc(originalHeaderId))
                    : exit;
                var block = method.Blocks[testBlock];
                if (block.JumpKind != GenTreeBlockJumpKind.Always ||
                    block.SuccessorBlockIds.Length != 1 ||
                    block.SuccessorBlockIds[0] != expected)
                {
                    throw new InvalidOperationException("Loop unrolling produced an invalid clone chain.");
                }

                for (int b = 0; b < duplications[i].Blocks.Length; b++)
                {
                    int clone = FindBlockByStartPc(method, duplications[i].Blocks[b].StartPc);
                    var cloneBlock = method.Blocks[clone];
                    VerifyExplicitCloneFlow(cloneBlock);
                    var statements = cloneBlock.Statements;
                    for (int s = 0; s < statements.Length; s++)
                    {
                        if (GenTreeLoopIteration.TreeContainsSlotRead(statements[s], slot))
                            throw new InvalidOperationException("Loop unrolling left an induction-variable use in a clone.");
                    }
                }
            }
        }

        private static void VerifyExplicitCloneFlow(GenTreeBlock block)
        {
            switch (block.JumpKind)
            {
                case GenTreeBlockJumpKind.Always:
                    if (block.SuccessorBlockIds.Length != 1 ||
                        block.Statements.IsDefaultOrEmpty ||
                        block.Statements[block.Statements.Length - 1].Kind != GenTreeKind.Branch ||
                        block.Statements[block.Statements.Length - 1].TargetBlockId != block.SuccessorBlockIds[0])
                    {
                        throw new InvalidOperationException("Loop unrolling produced an implicit or malformed unconditional clone transfer.");
                    }
                    return;

                case GenTreeBlockJumpKind.Conditional:
                    if (!GenTreeLoopDuplicator.TryGetConditionalTransfer(
                            block.Statements,
                            out var conditional,
                            out var appended,
                            out _) ||
                        appended is null ||
                        block.SuccessorBlockIds.Length != 2 ||
                        conditional.TargetBlockId == appended.TargetBlockId)
                    {
                        throw new InvalidOperationException("Loop unrolling produced an implicit or malformed conditional clone transfer.");
                    }
                    return;

                case GenTreeBlockJumpKind.Return:
                case GenTreeBlockJumpKind.Throw:
                    if (block.SuccessorBlockIds.Length != 0)
                        throw new InvalidOperationException("Loop unrolling produced a terminal clone with successors.");
                    return;

                default:
                    throw new InvalidOperationException("Loop unrolling produced a clone with unsupported implicit flow.");
            }
        }

        private static void VerifyTree(
            GenTree node,
            int blockId,
            HashSet<int> blockSuccessors,
            HashSet<int> treeIds,
            int blockCount)
        {
            if (node.Id < 0 || !treeIds.Add(node.Id))
                throw new InvalidOperationException($"Loop unrolling produced duplicate tree id {node.Id}.");
            if (node.TargetBlockId >= 0)
            {
                if ((uint)node.TargetBlockId >= (uint)blockCount)
                    throw new InvalidOperationException($"Loop unrolling produced invalid tree target B{node.TargetBlockId}.");
                if ((node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse) &&
                    !blockSuccessors.Contains(node.TargetBlockId))
                {
                    throw new InvalidOperationException($"Loop unrolling produced a transfer in B{blockId} outside its successor set.");
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                VerifyTree(node.Operands[i], blockId, blockSuccessors, treeIds, blockCount);
        }

        private static int FindBlockByStartPc(GenTreeMethod method, int startPc)
        {
            int found = -1;
            for (int i = 0; i < method.Blocks.Length; i++)
            {
                if (method.Blocks[i].StartPc != startPc)
                    continue;
                if (found >= 0)
                    throw new InvalidOperationException($"Duplicate block start PC {startPc}.");
                found = i;
            }
            if (found < 0)
                throw new InvalidOperationException($"Missing block start PC {startPc}.");
            return found;
        }

    }
}
