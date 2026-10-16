using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum LoopRelop : byte
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }

    internal enum IterationOperation : byte
    {
        Add,
        Subtract,
    }

    internal readonly struct ScalarSlot : IEquatable<ScalarSlot>
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

    internal readonly struct IntegralDomain
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

    internal enum LoopLimitKind : byte
    {
        Constant,
        Local,
        ArrayLength,
    }

    // A local limit names the local; an array length limit names the array local whose length it reads
    internal readonly struct LoopLimit : IEquatable<LoopLimit>
    {
        public readonly LoopLimitKind Kind;
        public readonly int Constant;
        public readonly ScalarSlot Slot;

        private LoopLimit(LoopLimitKind kind, int constant, ScalarSlot slot)
        {
            Kind = kind;
            Constant = constant;
            Slot = slot;
        }

        public bool IsConstant => Kind == LoopLimitKind.Constant;

        public static LoopLimit ForConstant(int constant) => new LoopLimit(LoopLimitKind.Constant, constant, default);

        public static LoopLimit ForLocal(ScalarSlot slot) => new LoopLimit(LoopLimitKind.Local, 0, slot);

        public static LoopLimit ForArrayLength(ScalarSlot array) => new LoopLimit(LoopLimitKind.ArrayLength, 0, array);

        public bool Equals(LoopLimit other)
            => Kind == other.Kind && (IsConstant ? Constant == other.Constant : Slot.Equals(other.Slot));

        public override bool Equals(object? obj)
            => obj is LoopLimit other && Equals(other);

        public override int GetHashCode()
            => IsConstant ? HashCode.Combine(Kind, Constant) : HashCode.Combine(Kind, Slot);
    }

    internal readonly struct LoopPredicate
    {
        public readonly ScalarSlot Slot;
        public readonly LoopRelop Relop;
        public readonly bool Unsigned;
        public readonly LoopLimit Limit;
        public readonly GenTree LimitNode;

        public LoopPredicate(ScalarSlot slot, LoopRelop relop, bool unsigned, LoopLimit limit, GenTree limitNode)
        {
            Slot = slot;
            Relop = relop;
            Unsigned = unsigned;
            Limit = limit;
            LimitNode = limitNode;
        }
    }

    internal readonly struct RawPredicate
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

    internal readonly struct LoopTest
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

    internal readonly struct LoopIncrement
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

    internal static class GenTreeLoopIteration
    {
        internal static bool TryAnalyzeLoopTest(
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

        internal static bool TryResolveConditionalPredicate(
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
                    if (TryParseLoopPredicate(definition.Operands[0], negate, out predicate))
                    {
                        testExpressionStatementIndex = definitionIndex;
                        return true;
                    }
                }
            }

            return TryParseLoopPredicate(condition, !continueWhenConditionTrue, out predicate);
        }

        internal static bool TryParseLoopPredicate(
            GenTree expression,
            bool negate,
            out LoopPredicate predicate)
        {
            predicate = default;
            if (!TryParseRawPredicate(expression, negate, out var raw))
                return false;

            if (TryGetScalarUse(raw.Left, out var leftSlot) &&
                TryParseLimit(raw.Right, out var rightLimit))
            {
                predicate = new LoopPredicate(leftSlot, raw.Relop, raw.Unsigned, rightLimit, raw.Right);
                return IsPurePredicateTree(raw.Left) && IsPureLimit(raw.Right, rightLimit);
            }

            if (TryParseLimit(raw.Left, out var leftLimit) &&
                TryGetScalarUse(raw.Right, out var rightSlot))
            {
                predicate = new LoopPredicate(
                    rightSlot,
                    SwapRelop(raw.Relop),
                    raw.Unsigned,
                    leftLimit,
                    raw.Left);
                return IsPurePredicateTree(raw.Right) && IsPureLimit(raw.Left, leftLimit);
            }

            return false;
        }

        private static bool TryParseLimit(GenTree node, out LoopLimit limit)
        {
            if (TryGetConstI4(node, out int constant))
            {
                limit = LoopLimit.ForConstant(constant);
                return true;
            }
            if (TryGetScalarUse(node, out var slot))
            {
                limit = LoopLimit.ForLocal(slot);
                return true;
            }
            if (node.Kind == GenTreeKind.ArrayLength && node.Operands.Length == 1 && TryGetScalarUse(node.Operands[0], out var array))
            {
                limit = LoopLimit.ForArrayLength(array);
                return true;
            }
            limit = default;
            return false;
        }

        // An array never changes length, so reading it is as pure as reading the array local
        private static bool IsPureLimit(GenTree node, LoopLimit limit)
            => IsPurePredicateTree(limit.Kind == LoopLimitKind.ArrayLength ? node.Operands[0] : node);

        internal static bool TryParseRawPredicate(
            GenTree expression,
            bool negate,
            out RawPredicate predicate)
        {
            predicate = default;
            if (expression.Kind != GenTreeKind.Binary || expression.Operands.Length != 2)
                return false;

            if (expression.Operator == GenTreeOperator.Ceq)
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
            switch (expression.Operator)
            {
                case GenTreeOperator.Ceq:
                    relop = LoopRelop.Equal;
                    unsigned = false;
                    break;
                case GenTreeOperator.Clt:
                    relop = LoopRelop.LessThan;
                    unsigned = false;
                    break;
                case GenTreeOperator.CltUn:
                    relop = LoopRelop.LessThan;
                    unsigned = true;
                    break;
                case GenTreeOperator.Cgt:
                    relop = LoopRelop.GreaterThan;
                    unsigned = false;
                    break;
                case GenTreeOperator.CgtUn:
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

        internal static bool TryDecodeBooleanSlot(
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
                expression.Operator == GenTreeOperator.Ceq &&
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

        internal static LoopRelop NegateRelop(LoopRelop relop)
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

        internal static LoopRelop SwapRelop(LoopRelop relop)
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

        internal static bool TryFindIncrement(
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

        internal static bool TryGetLinearPredecessor(
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

        internal static bool TryParseIncrement(
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
            if (value.Operator == GenTreeOperator.Add)
                operation = IterationOperation.Add;
            else if (value.Operator == GenTreeOperator.Sub)
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

        internal static bool TryNormalizeIncrementDelta(
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

        internal static bool HasSingleLoopDefinition(
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

        internal static bool HasOnlyExpectedDefinition(
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

        internal static bool TryFindConstantInitializer(
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

        internal static bool TryGetUniqueNormalPredecessor(
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

        internal static bool TreeContainsSlotDefinition(GenTree node, ScalarSlot slot)
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

        internal static bool TryEvaluateInitializer(
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

        internal static bool ValidateEntryGuard(
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
                        guardPredicate.Limit.Equals(predicate.Limit))
                    {
                        return true;
                    }
                }

                childBlockId = guardBlockId;
            }

            return false;
        }

        internal static bool TryGetScalarUse(GenTree node, out ScalarSlot slot)
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

        internal static bool TryGetScalarStore(GenTree node, out ScalarSlot slot)
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

        internal static bool IsSlotRead(GenTree node, ScalarSlot slot)
            => node.Kind == slot.LoadKind &&
               node.Int32 == slot.Index &&
               ReferenceEquals(node.LocalDescriptor, slot.Descriptor);

        internal static bool TreeContainsSlotRead(GenTree node, ScalarSlot slot)
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

        internal static bool TryGetConstI4(GenTree node, out int value)
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

        internal static bool TryGetBooleanConstant(GenTree node, out bool value)
        {
            if (TryGetConstI4(node, out int constant) && (constant == 0 || constant == 1))
            {
                value = constant != 0;
                return true;
            }
            value = false;
            return false;
        }

        internal static bool IsPurePredicateTree(GenTree node)
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

        internal static bool TryGetIntegralDomain(GenLocalDescriptor descriptor, out IntegralDomain domain)
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

        internal static bool ConversionMatchesDomain(NumericConvKind kind, IntegralDomain domain)
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

        internal static bool TryNormalizeConstant(
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
