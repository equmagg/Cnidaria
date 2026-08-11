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
        private enum LoopRelop : byte
        {
            Equal,
            NotEqual,
            LessThan,
            LessThanOrEqual,
            GreaterThan,
            GreaterThanOrEqual,
        }

        private enum IterationOperation : byte
        {
            Add,
            Subtract,
        }

        private readonly struct ScalarSlot : IEquatable<ScalarSlot>
        {
            public readonly GenTreeKind LoadKind;
            public readonly GenTreeKind StoreKind;
            public readonly int Index;
            public readonly GenLocalDescriptor Descriptor;

            public ScalarSlot(
                GenTreeKind loadKind,
                GenTreeKind storeKind,
                int index,
                GenLocalDescriptor descriptor)
            {
                LoadKind = loadKind;
                StoreKind = storeKind;
                Index = index;
                Descriptor = descriptor;
            }

            public bool Equals(ScalarSlot other)
                => LoadKind == other.LoadKind &&
                   StoreKind == other.StoreKind &&
                   Index == other.Index &&
                   ReferenceEquals(Descriptor, other.Descriptor);

            public override bool Equals(object? obj)
                => obj is ScalarSlot other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine((int)LoadKind, (int)StoreKind, Index, Descriptor.LclNum);
        }

        private readonly struct IntegralDomain
        {
            public readonly long Minimum;
            public readonly long Maximum;
            public readonly bool Unsigned;
            public readonly int Bits;

            public IntegralDomain(long minimum, long maximum, bool unsigned, int bits)
            {
                Minimum = minimum;
                Maximum = maximum;
                Unsigned = unsigned;
                Bits = bits;
            }
        }

        private readonly struct LoopPredicate
        {
            public readonly ScalarSlot Slot;
            public readonly LoopRelop Relop;
            public readonly bool Unsigned;
            public readonly int Limit;

            public LoopPredicate(ScalarSlot slot, LoopRelop relop, bool unsigned, int limit)
            {
                Slot = slot;
                Relop = relop;
                Unsigned = unsigned;
                Limit = limit;
            }
        }

        private readonly struct RawPredicate
        {
            public readonly GenTree Left;
            public readonly GenTree Right;
            public readonly LoopRelop Relop;
            public readonly bool Unsigned;

            public RawPredicate(GenTree left, GenTree right, LoopRelop relop, bool unsigned)
            {
                Left = left;
                Right = right;
                Relop = relop;
                Unsigned = unsigned;
            }
        }

        private readonly struct LoopTest
        {
            public readonly int BlockId;
            public readonly int ConditionalIndex;
            public readonly int TestExpressionStatementIndex;
            public readonly int ExitSuccessor;
            public readonly LoopPredicate Predicate;

            public LoopTest(
                int blockId,
                int conditionalIndex,
                int testExpressionStatementIndex,
                int exitSuccessor,
                LoopPredicate predicate)
            {
                BlockId = blockId;
                ConditionalIndex = conditionalIndex;
                TestExpressionStatementIndex = testExpressionStatementIndex;
                ExitSuccessor = exitSuccessor;
                Predicate = predicate;
            }
        }

        private readonly struct LoopIncrement
        {
            public readonly GenTree Store;
            public readonly ScalarSlot Slot;
            public readonly IterationOperation Operation;
            public readonly int Delta;
            public readonly IntegralDomain Domain;

            public LoopIncrement(
                GenTree store,
                ScalarSlot slot,
                IterationOperation operation,
                int delta,
                IntegralDomain domain)
            {
                Store = store;
                Slot = slot;
                Operation = operation;
                Delta = delta;
                Domain = domain;
            }
        }

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
                if (!TryAnalyzeLoopTest(method, cfg, loop, blockId, out var test))
                    continue;
                if (!TryFindIncrement(method, cfg, loop, test, options.AnalysisBudget, out var increment))
                    continue;
                if (!test.Predicate.Slot.Equals(increment.Slot))
                    continue;
                if (!HasSingleLoopDefinition(method, loop, increment))
                    continue;
                if (!TryFindConstantInitializer(method, cfg, loop, increment, out int initialValue))
                    continue;

                bool hasEntryGuard = ValidateEntryGuard(method, cfg, loop, test.Predicate);
                if (!hasEntryGuard &&
                    !EvaluatePredicate(
                        initialValue,
                        test.Predicate.Limit,
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

        private static bool TryAnalyzeLoopTest(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            int blockId,
            out LoopTest test)
        {
            test = default;
            if ((uint)blockId >= (uint)method.Blocks.Length || !loop.Contains(blockId))
                return false;

            var block = method.Blocks[blockId];
            if (block.JumpKind != GenTreeBlockJumpKind.Conditional ||
                !GenTreeLoopDuplicator.TryGetConditionalTransfer(
                    block.Statements,
                    out var conditional,
                    out var appended,
                    out int conditionalIndex) ||
                conditional.Operands.Length != 1 ||
                !GenTreeLoopDuplicator.TryGetLogicalSuccessors(
                    cfg.Blocks[blockId],
                    conditional,
                    appended,
                    out int trueSuccessor,
                    out int falseSuccessor))
            {
                return false;
            }

            bool trueIsBackedge = trueSuccessor == loop.Header;
            bool falseIsBackedge = falseSuccessor == loop.Header;
            if (trueIsBackedge == falseIsBackedge)
                return false;

            int exitSuccessor = trueIsBackedge ? falseSuccessor : trueSuccessor;
            if (loop.Contains(exitSuccessor))
                return false;

            if (!TryResolveConditionalPredicate(
                    block,
                    conditionalIndex,
                    conditional,
                    continueWhenConditionTrue: trueIsBackedge,
                    out var predicate,
                    out int testExpressionStatementIndex))
            {
                return false;
            }

            test = new LoopTest(
                blockId,
                conditionalIndex,
                testExpressionStatementIndex,
                exitSuccessor,
                predicate);
            return true;
        }

        private static bool TryResolveConditionalPredicate(
            GenTreeBlock block,
            int conditionalIndex,
            GenTree conditional,
            bool continueWhenConditionTrue,
            out LoopPredicate predicate,
            out int testExpressionStatementIndex)
        {
            predicate = default;
            testExpressionStatementIndex = conditionalIndex;
            var condition = conditional.Operands[0];

            if (conditionalIndex > 0 &&
                TryDecodeBooleanSlot(condition, out var tempSlot, out bool conditionTrueMeansStoredTrue))
            {
                int definitionIndex = conditionalIndex - 1;
                var definition = block.Statements[definitionIndex];
                if (TryGetScalarStore(definition, out var definitionSlot) &&
                    definitionSlot.Equals(tempSlot) &&
                    definition.Operands.Length == 1)
                {
                    bool negate = continueWhenConditionTrue != conditionTrueMeansStoredTrue;
                    if (TryParseLoopPredicate(definition.Operands[0], negate, out predicate) &&
                        IsPurePredicateTree(definition.Operands[0]))
                    {
                        testExpressionStatementIndex = definitionIndex;
                        return true;
                    }
                }
            }

            return TryParseLoopPredicate(condition, !continueWhenConditionTrue, out predicate) &&
                   IsPurePredicateTree(condition);
        }

        private static bool TryParseLoopPredicate(
            GenTree expression,
            bool negate,
            out LoopPredicate predicate)
        {
            predicate = default;
            if (!TryParseRawPredicate(expression, negate, out var raw))
                return false;

            if (TryGetScalarUse(raw.Left, out var leftSlot) &&
                TryGetConstI4(raw.Right, out int rightConstant))
            {
                predicate = new LoopPredicate(leftSlot, raw.Relop, raw.Unsigned, rightConstant);
                return true;
            }

            if (TryGetConstI4(raw.Left, out int leftConstant) &&
                TryGetScalarUse(raw.Right, out var rightSlot))
            {
                predicate = new LoopPredicate(
                    rightSlot,
                    SwapRelop(raw.Relop),
                    raw.Unsigned,
                    leftConstant);
                return true;
            }

            return false;
        }

        private static bool TryParseRawPredicate(
            GenTree expression,
            bool negate,
            out RawPredicate predicate)
        {
            predicate = default;
            if (expression.Kind != GenTreeKind.Binary || expression.Operands.Length != 2)
                return false;

            if (expression.SourceOp == BytecodeOp.Ceq)
            {
                if (TryGetBooleanConstant(expression.Operands[1], out bool rightBoolean) &&
                    TryParseRawPredicate(expression.Operands[0], negate ^ !rightBoolean, out predicate))
                {
                    return true;
                }
                if (TryGetBooleanConstant(expression.Operands[0], out bool leftBoolean) &&
                    TryParseRawPredicate(expression.Operands[1], negate ^ !leftBoolean, out predicate))
                {
                    return true;
                }
            }

            LoopRelop relop;
            bool unsigned;
            switch (expression.SourceOp)
            {
                case BytecodeOp.Ceq:
                    relop = LoopRelop.Equal;
                    unsigned = false;
                    break;
                case BytecodeOp.Clt:
                    relop = LoopRelop.LessThan;
                    unsigned = false;
                    break;
                case BytecodeOp.Clt_Un:
                    relop = LoopRelop.LessThan;
                    unsigned = true;
                    break;
                case BytecodeOp.Cgt:
                    relop = LoopRelop.GreaterThan;
                    unsigned = false;
                    break;
                case BytecodeOp.Cgt_Un:
                    relop = LoopRelop.GreaterThan;
                    unsigned = true;
                    break;
                default:
                    return false;
            }

            if (negate)
                relop = NegateRelop(relop);
            if (relop == LoopRelop.Equal)
                return false;
            predicate = new RawPredicate(expression.Operands[0], expression.Operands[1], relop, unsigned);
            return true;
        }

        private static bool TryDecodeBooleanSlot(
            GenTree expression,
            out ScalarSlot slot,
            out bool conditionTrueMeansStoredTrue)
        {
            if (TryGetScalarUse(expression, out slot))
            {
                conditionTrueMeansStoredTrue = true;
                return true;
            }

            if (expression.Kind == GenTreeKind.Binary &&
                expression.SourceOp == BytecodeOp.Ceq &&
                expression.Operands.Length == 2)
            {
                if (TryGetScalarUse(expression.Operands[0], out slot) &&
                    TryGetBooleanConstant(expression.Operands[1], out bool right))
                {
                    conditionTrueMeansStoredTrue = right;
                    return true;
                }
                if (TryGetBooleanConstant(expression.Operands[0], out bool left) &&
                    TryGetScalarUse(expression.Operands[1], out slot))
                {
                    conditionTrueMeansStoredTrue = left;
                    return true;
                }
            }

            slot = default;
            conditionTrueMeansStoredTrue = false;
            return false;
        }

        private static LoopRelop NegateRelop(LoopRelop relop)
            => relop switch
            {
                LoopRelop.Equal => LoopRelop.NotEqual,
                LoopRelop.NotEqual => LoopRelop.Equal,
                LoopRelop.LessThan => LoopRelop.GreaterThanOrEqual,
                LoopRelop.LessThanOrEqual => LoopRelop.GreaterThan,
                LoopRelop.GreaterThan => LoopRelop.LessThanOrEqual,
                LoopRelop.GreaterThanOrEqual => LoopRelop.LessThan,
                _ => throw new InvalidOperationException(),
            };

        private static LoopRelop SwapRelop(LoopRelop relop)
            => relop switch
            {
                LoopRelop.Equal => LoopRelop.Equal,
                LoopRelop.NotEqual => LoopRelop.NotEqual,
                LoopRelop.LessThan => LoopRelop.GreaterThan,
                LoopRelop.LessThanOrEqual => LoopRelop.GreaterThanOrEqual,
                LoopRelop.GreaterThan => LoopRelop.LessThan,
                LoopRelop.GreaterThanOrEqual => LoopRelop.LessThanOrEqual,
                _ => throw new InvalidOperationException(),
            };

        private static bool TryFindIncrement(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopTest test,
            int analysisBudget,
            out LoopIncrement increment)
        {
            increment = default;
            int budget = analysisBudget;
            int blockId = test.BlockId;
            int lastStatementIndex = test.TestExpressionStatementIndex - 1;
            var laterStatements = new List<GenTree>();
            var visitedBlocks = new HashSet<int>();

            while ((uint)blockId < (uint)method.Blocks.Length &&
                   loop.Contains(blockId) &&
                   visitedBlocks.Add(blockId))
            {
                var block = method.Blocks[blockId];
                for (int statementIndex = lastStatementIndex; statementIndex >= 0; statementIndex--)
                {
                    if (budget-- == 0)
                        return false;

                    var statement = block.Statements[statementIndex];
                    if (TryParseIncrement(statement, out var candidate) &&
                        candidate.Slot.Equals(test.Predicate.Slot) &&
                        !candidate.Slot.Descriptor.HasMemoryAlias &&
                        !candidate.Slot.Descriptor.IsStructField)
                    {
                        bool interveningRead = false;
                        for (int i = 0; i < laterStatements.Count; i++)
                        {
                            if (budget-- == 0)
                                return false;
                            if (TreeContainsSlotRead(laterStatements[i], candidate.Slot))
                            {
                                interveningRead = true;
                                break;
                            }
                        }

                        if (!interveningRead)
                        {
                            increment = candidate;
                            return true;
                        }
                    }

                    laterStatements.Add(statement);
                }

                if (!TryGetLinearPredecessor(cfg, loop, blockId, out int predecessor))
                    break;

                int successorBlockId = blockId;
                blockId = predecessor;
                var predecessorBlock = method.Blocks[blockId];
                lastStatementIndex = predecessorBlock.Statements.Length - 1;
                if (lastStatementIndex >= 0)
                {
                    var transfer = predecessorBlock.Statements[lastStatementIndex];
                    if (transfer.Kind == GenTreeKind.Branch && transfer.TargetBlockId == successorBlockId)
                        lastStatementIndex--;
                }
            }

            return false;
        }

        private static bool TryGetLinearPredecessor(
            ControlFlowGraph cfg,
            CfgLoop loop,
            int blockId,
            out int predecessor)
        {
            predecessor = -1;
            var predecessors = cfg.Blocks[blockId].Predecessors;
            for (int i = 0; i < predecessors.Length; i++)
            {
                var edge = predecessors[i];
                if (edge.Kind == CfgEdgeKind.Exception || !loop.Contains(edge.FromBlockId))
                    continue;
                if (predecessor >= 0 && predecessor != edge.FromBlockId)
                    return false;
                predecessor = edge.FromBlockId;
            }

            if (predecessor < 0)
                return false;

            int normalSuccessor = -1;
            var successors = cfg.Blocks[predecessor].Successors;
            for (int i = 0; i < successors.Length; i++)
            {
                var edge = successors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (normalSuccessor >= 0 && normalSuccessor != edge.ToBlockId)
                    return false;
                normalSuccessor = edge.ToBlockId;
            }

            return normalSuccessor == blockId;
        }

        private static bool TryParseIncrement(
            GenTree statement,
            out LoopIncrement increment)
        {
            increment = default;
            if (!TryGetScalarStore(statement, out var slot) || statement.Operands.Length != 1)
                return false;
            if (!TryGetIntegralDomain(slot.Descriptor, out var domain))
                return false;

            var value = statement.Operands[0];
            if (value.Kind == GenTreeKind.Conv)
            {
                if ((value.ConvFlags & NumericConvFlags.Checked) != 0 ||
                    value.Operands.Length != 1 ||
                    !ConversionMatchesDomain(value.ConvKind, domain))
                {
                    return false;
                }
                value = value.Operands[0];
            }

            if (value.Kind != GenTreeKind.Binary || value.Operands.Length != 2)
                return false;

            IterationOperation operation;
            if (value.SourceOp == BytecodeOp.Add)
                operation = IterationOperation.Add;
            else if (value.SourceOp == BytecodeOp.Sub)
                operation = IterationOperation.Subtract;
            else
                return false;

            if (!IsSlotRead(value.Operands[0], slot) ||
                !TryGetConstI4(value.Operands[1], out int rawDelta) ||
                !TryNormalizeIncrementDelta(rawDelta, domain, out int delta) ||
                delta == 0)
            {
                return false;
            }

            increment = new LoopIncrement(statement, slot, operation, delta, domain);
            return true;
        }

        private static bool TryNormalizeIncrementDelta(
            int delta,
            IntegralDomain domain,
            out int normalized)
        {
            if (domain.Bits == 32)
            {
                normalized = delta;
                return true;
            }

            if (domain.Unsigned)
            {
                uint mask = (1u << domain.Bits) - 1u;
                normalized = unchecked((int)(unchecked((uint)delta) & mask));
                return true;
            }

            normalized = domain.Bits == 8
                ? unchecked((sbyte)delta)
                : unchecked((short)delta);
            return true;
        }

        private static bool HasSingleLoopDefinition(
            GenTreeMethod method,
            CfgLoop loop,
            LoopIncrement increment)
        {
            for (int i = 0; i < loop.Blocks.Length; i++)
            {
                var statements = method.Blocks[loop.Blocks[i]].Statements;
                for (int s = 0; s < statements.Length; s++)
                {
                    if (!HasOnlyExpectedDefinition(statements[s], increment.Slot, increment.Store))
                        return false;
                }
            }
            return true;
        }

        private static bool HasOnlyExpectedDefinition(
            GenTree node,
            ScalarSlot slot,
            GenTree expectedStore)
        {
            if (TryGetScalarStore(node, out var storeSlot) &&
                storeSlot.Equals(slot) &&
                !ReferenceEquals(node, expectedStore))
            {
                return false;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (!HasOnlyExpectedDefinition(node.Operands[i], slot, expectedStore))
                    return false;
            }
            return true;
        }

        private static bool TryFindConstantInitializer(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopIncrement increment,
            out int initialValue)
        {
            initialValue = 0;
            int blockId = loop.Preheader;
            var visited = new HashSet<int>();

            while ((uint)blockId < (uint)method.Blocks.Length &&
                   !loop.Contains(blockId) &&
                   visited.Add(blockId))
            {
                var statements = method.Blocks[blockId].Statements;
                for (int s = statements.Length - 1; s >= 0; s--)
                {
                    var statement = statements[s];
                    if (TryGetScalarStore(statement, out var definitionSlot) &&
                        definitionSlot.Equals(increment.Slot))
                    {
                        return statement.Operands.Length == 1 &&
                               TryEvaluateInitializer(
                                   statement.Operands[0],
                                   increment.Domain,
                                   out initialValue);
                    }

                    if (TreeContainsSlotDefinition(statement, increment.Slot))
                        return false;
                }

                if (!TryGetUniqueNormalPredecessor(cfg, blockId, out blockId))
                    break;
            }

            return false;
        }

        private static bool TryGetUniqueNormalPredecessor(
            ControlFlowGraph cfg,
            int blockId,
            out int predecessor)
        {
            predecessor = -1;
            var predecessors = cfg.Blocks[blockId].Predecessors;
            for (int i = 0; i < predecessors.Length; i++)
            {
                var edge = predecessors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    return false;
                if (predecessor >= 0 && predecessor != edge.FromBlockId)
                    return false;
                predecessor = edge.FromBlockId;
            }

            return predecessor >= 0;
        }

        private static bool TreeContainsSlotDefinition(GenTree node, ScalarSlot slot)
        {
            if (TryGetScalarStore(node, out var storeSlot) && storeSlot.Equals(slot))
                return true;

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (TreeContainsSlotDefinition(node.Operands[i], slot))
                    return true;
            }

            return false;
        }

        private static bool TryEvaluateInitializer(
            GenTree value,
            IntegralDomain domain,
            out int result)
        {
            if (value.Kind == GenTreeKind.Conv)
            {
                if ((value.ConvFlags & NumericConvFlags.Checked) != 0 ||
                    value.Operands.Length != 1 ||
                    !ConversionMatchesDomain(value.ConvKind, domain) ||
                    !TryGetConstI4(value.Operands[0], out int converted))
                {
                    result = 0;
                    return false;
                }

                return TryNormalizeConstant(converted, domain, allowNarrowing: true, out result);
            }

            if (!TryGetConstI4(value, out int constant))
            {
                result = 0;
                return false;
            }

            return TryNormalizeConstant(constant, domain, allowNarrowing: domain.Bits == 32, out result);
        }

        private static bool ValidateEntryGuard(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            CfgLoop loop,
            LoopPredicate predicate)
        {
            int childBlockId = loop.Preheader;
            var visited = new HashSet<int>();

            while ((uint)childBlockId < (uint)method.Blocks.Length &&
                   visited.Add(childBlockId) &&
                   TryGetUniqueNormalPredecessor(cfg, childBlockId, out int guardBlockId))
            {
                if ((uint)guardBlockId >= (uint)method.Blocks.Length || loop.Contains(guardBlockId))
                    return false;

                var guard = method.Blocks[guardBlockId];
                if (guard.JumpKind == GenTreeBlockJumpKind.Conditional &&
                    GenTreeLoopDuplicator.TryGetConditionalTransfer(
                        guard.Statements,
                        out var conditional,
                        out var appended,
                        out int conditionalIndex) &&
                    conditional.Operands.Length == 1 &&
                    GenTreeLoopDuplicator.TryGetLogicalSuccessors(
                        cfg.Blocks[guardBlockId],
                        conditional,
                        appended,
                        out int trueSuccessor,
                        out int falseSuccessor))
                {
                    bool trueEntersLoop = trueSuccessor == childBlockId;
                    bool falseEntersLoop = falseSuccessor == childBlockId;
                    if (trueEntersLoop != falseEntersLoop &&
                        TryResolveConditionalPredicate(
                            guard,
                            conditionalIndex,
                            conditional,
                            continueWhenConditionTrue: trueEntersLoop,
                            out var guardPredicate,
                            out _) &&
                        guardPredicate.Slot.Equals(predicate.Slot) &&
                        guardPredicate.Relop == predicate.Relop &&
                        guardPredicate.Unsigned == predicate.Unsigned &&
                        guardPredicate.Limit == predicate.Limit)
                    {
                        return true;
                    }
                }

                childBlockId = guardBlockId;
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
            while (EvaluatePredicate(current, predicate.Limit, predicate.Relop, predicate.Unsigned))
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
                ? unchecked((uint)predicate.Limit)
                : predicate.Limit;

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
                GenTreeKind.Call or GenTreeKind.IndirectCall or GenTreeKind.VirtualCall => 8,
                GenTreeKind.NewObject or GenTreeKind.NewArray or GenTreeKind.NewDelegate => 8,
                GenTreeKind.ClassInit => 8,
                GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement => 5,
                GenTreeKind.ArrayLength => 2,
                GenTreeKind.Field or GenTreeKind.FieldAddr or GenTreeKind.StaticField or GenTreeKind.StaticFieldAddr => 3,
                GenTreeKind.LoadIndirect or GenTreeKind.StoreIndirect => 3,
                GenTreeKind.Binary => node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un ? 6 : 2,
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
                block.RegionPc);
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
                    block.RegionPc);
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
                block.RegionPc);
        }

        private static GenTree ReplaceScalarUses(GenTree node, ScalarSlot slot, int constant)
        {
            if (IsSlotRead(node, slot))
            {
                return new GenTree(
                    node.Id,
                    GenTreeKind.ConstI4,
                    node.Pc,
                    BytecodeOp.Ldc_I4,
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
                block.RegionPc);
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
                        if (TreeContainsSlotRead(statements[s], slot))
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

        private static bool TryGetScalarUse(GenTree node, out ScalarSlot slot)
        {
            GenTreeKind storeKind;
            switch (node.Kind)
            {
                case GenTreeKind.Local:
                    storeKind = GenTreeKind.StoreLocal;
                    break;
                case GenTreeKind.Arg:
                    storeKind = GenTreeKind.StoreArg;
                    break;
                case GenTreeKind.Temp:
                    storeKind = GenTreeKind.StoreTemp;
                    break;
                default:
                    slot = default;
                    return false;
            }

            if (node.LocalDescriptor is null)
            {
                slot = default;
                return false;
            }

            slot = new ScalarSlot(node.Kind, storeKind, node.Int32, node.LocalDescriptor);
            return true;
        }

        private static bool TryGetScalarStore(GenTree node, out ScalarSlot slot)
        {
            GenTreeKind loadKind;
            switch (node.Kind)
            {
                case GenTreeKind.StoreLocal:
                    loadKind = GenTreeKind.Local;
                    break;
                case GenTreeKind.StoreArg:
                    loadKind = GenTreeKind.Arg;
                    break;
                case GenTreeKind.StoreTemp:
                    loadKind = GenTreeKind.Temp;
                    break;
                default:
                    slot = default;
                    return false;
            }

            if (node.LocalDescriptor is null)
            {
                slot = default;
                return false;
            }

            slot = new ScalarSlot(loadKind, node.Kind, node.Int32, node.LocalDescriptor);
            return true;
        }

        private static bool IsSlotRead(GenTree node, ScalarSlot slot)
            => node.Kind == slot.LoadKind &&
               node.Int32 == slot.Index &&
               ReferenceEquals(node.LocalDescriptor, slot.Descriptor);

        private static bool TreeContainsSlotRead(GenTree node, ScalarSlot slot)
        {
            if (IsSlotRead(node, slot))
                return true;
            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (TreeContainsSlotRead(node.Operands[i], slot))
                    return true;
            }
            return false;
        }

        private static bool TryGetConstI4(GenTree node, out int value)
        {
            if (node.Kind == GenTreeKind.ConstI4)
            {
                value = node.Int32;
                return true;
            }
            if (node.Kind == GenTreeKind.DefaultValue && node.StackKind == GenStackKind.I4)
            {
                value = 0;
                return true;
            }
            value = 0;
            return false;
        }

        private static bool TryGetBooleanConstant(GenTree node, out bool value)
        {
            if (TryGetConstI4(node, out int constant) && (constant == 0 || constant == 1))
            {
                value = constant != 0;
                return true;
            }
            value = false;
            return false;
        }

        private static bool IsPurePredicateTree(GenTree node)
        {
            if ((node.Flags &
                 (GenTreeFlags.ContainsCall |
                  GenTreeFlags.CanThrow |
                  GenTreeFlags.SideEffect |
                  GenTreeFlags.MemoryRead |
                  GenTreeFlags.MemoryWrite |
                  GenTreeFlags.ControlFlow |
                  GenTreeFlags.ExceptionFlow |
                  GenTreeFlags.GlobalRef |
                  GenTreeFlags.Indirect |
                  GenTreeFlags.Allocation)) != 0)
            {
                return false;
            }

            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (!IsPurePredicateTree(node.Operands[i]))
                    return false;
            }
            return true;
        }

        private static bool TryGetIntegralDomain(GenLocalDescriptor descriptor, out IntegralDomain domain)
        {
            if (descriptor.StackKind != GenStackKind.I4)
            {
                domain = default;
                return false;
            }

            RuntimePrimitiveKind kind = descriptor.Type?.PrimitiveKind ?? RuntimePrimitiveKind.Int32;
            switch (kind)
            {
                case RuntimePrimitiveKind.UInt8:
                    domain = new IntegralDomain(0, byte.MaxValue, unsigned: true, bits: 8);
                    return true;
                case RuntimePrimitiveKind.Int8:
                    domain = new IntegralDomain(sbyte.MinValue, sbyte.MaxValue, unsigned: false, bits: 8);
                    return true;
                case RuntimePrimitiveKind.Char:
                case RuntimePrimitiveKind.UInt16:
                    domain = new IntegralDomain(0, ushort.MaxValue, unsigned: true, bits: 16);
                    return true;
                case RuntimePrimitiveKind.Int16:
                    domain = new IntegralDomain(short.MinValue, short.MaxValue, unsigned: false, bits: 16);
                    return true;
                case RuntimePrimitiveKind.UInt32:
                    domain = new IntegralDomain(0, uint.MaxValue, unsigned: true, bits: 32);
                    return true;
                case RuntimePrimitiveKind.Int32:
                    domain = new IntegralDomain(int.MinValue, int.MaxValue, unsigned: false, bits: 32);
                    return true;
                default:
                    domain = default;
                    return false;
            }
        }

        private static bool ConversionMatchesDomain(NumericConvKind kind, IntegralDomain domain)
        {
            if (domain.Bits == 8 && domain.Unsigned)
                return kind == NumericConvKind.U1;
            if (domain.Bits == 8)
                return kind == NumericConvKind.I1;
            if (domain.Bits == 16 && domain.Unsigned)
                return kind is NumericConvKind.U2 or NumericConvKind.Char;
            if (domain.Bits == 16)
                return kind == NumericConvKind.I2;
            if (domain.Bits == 32 && domain.Unsigned)
                return kind == NumericConvKind.U4;
            return domain.Bits == 32 && kind == NumericConvKind.I4;
        }

        private static bool TryNormalizeConstant(
            int value,
            IntegralDomain domain,
            bool allowNarrowing,
            out int normalized)
        {
            if (domain.Bits == 32)
            {
                normalized = value;
                return true;
            }

            long numeric = value;
            if (!allowNarrowing && (numeric < domain.Minimum || numeric > domain.Maximum))
            {
                normalized = 0;
                return false;
            }

            if (domain.Unsigned)
            {
                uint mask = (1u << domain.Bits) - 1u;
                normalized = unchecked((int)(unchecked((uint)value) & mask));
                return true;
            }

            normalized = domain.Bits == 8
                ? unchecked((sbyte)value)
                : unchecked((short)value);
            return true;
        }
    }
}
