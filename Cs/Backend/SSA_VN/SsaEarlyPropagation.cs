using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal static class SsaEarlyPropagator
    {
        private const int RecursionBound = 5;

        public static SsaMethod OptimizeMethod(SsaMethod method, bool validate = true)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.ValueNumbers is not null)
                throw new InvalidOperationException("Early propagation must run before value numbering.");

            var rewriter = new Rewriter(method);
            var blocks = rewriter.RewriteBlocks();
            if (!rewriter.Changed)
                return method;

            var rewritten = method.GenTreeMethod.CloneWithBlocks(blocks);
            NormalizeTreeFlags(rewritten);

            bool includeExceptionEdges = HasExceptionEdges(method.Cfg);
            var cfg = ControlFlowGraph.Build(rewritten, includeExceptionEdges);
            rewritten.AttachFlowGraph(cfg);

            var liveness = GenTreeLocalLiveness.Build(rewritten, cfg);
            rewritten.AttachHirLiveness(liveness);

            return GenTreeSsaBuilder.BuildMethod(rewritten, cfg, liveness, validate);
        }

        private static void NormalizeTreeFlags(GenTreeMethod method)
        {
            for (int blockIndex = 0; blockIndex < method.Blocks.Length; blockIndex++)
            {
                var statements = method.Blocks[blockIndex].Statements;
                for (int statementIndex = 0; statementIndex < statements.Length; statementIndex++)
                    GenTreeMorpher.NormalizeTreeFlags(statements[statementIndex], method.Target);
            }
        }

        private static bool HasExceptionEdges(ControlFlowGraph cfg)
        {
            for (int blockIndex = 0; blockIndex < cfg.Blocks.Length; blockIndex++)
            {
                var successors = cfg.Blocks[blockIndex].Successors;
                for (int successorIndex = 0; successorIndex < successors.Length; successorIndex++)
                {
                    if (successors[successorIndex].Kind == CfgEdgeKind.Exception)
                        return true;
                }
            }

            return false;
        }

        private sealed class Rewriter
        {
            private readonly SsaMethod _method;
            private readonly HashSet<int> _handlerLiveLocalNumbers;

            public bool Changed { get; private set; }

            public Rewriter(SsaMethod method)
            {
                _method = method;
                _handlerLiveLocalNumbers = FindHandlerLiveLocalNumbers(method);
            }

            public ImmutableArray<GenTreeBlock> RewriteBlocks()
            {
                var blocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.GenTreeMethod.Blocks.Length);
                for (int blockIndex = 0; blockIndex < _method.GenTreeMethod.Blocks.Length; blockIndex++)
                {
                    var block = _method.GenTreeMethod.Blocks[blockIndex];
                    bool[] removedStatements = FindFoldedNullChecks(block);
                    int statementCount = block.Statements.Length;
                    for (int statementIndex = 0; statementIndex < removedStatements.Length; statementIndex++)
                    {
                        if (removedStatements[statementIndex])
                            statementCount--;
                    }

                    var statements = ImmutableArray.CreateBuilder<GenTree>(statementCount);
                    bool blockChanged = statementCount != block.Statements.Length;

                    for (int statementIndex = 0; statementIndex < block.Statements.Length; statementIndex++)
                    {
                        if (removedStatements[statementIndex])
                            continue;

                        GenTree original = block.Statements[statementIndex];
                        GenTree rewritten = RewriteTree(original);
                        statements.Add(rewritten);
                        blockChanged |= !ReferenceEquals(original, rewritten);
                    }

                    if (blockChanged)
                    {
                        Changed = true;
                        blocks.Add(new GenTreeBlock(
                            block.Id,
                            block.StartPc,
                            block.EndPcExclusive,
                            block.EntryStackDepth,
                            block.ExitStackDepth,
                            block.JumpKind,
                            block.Flags,
                            statements.ToImmutable(),
                            block.SuccessorBlockIds,
                            block.SuccessorPcs));
                    }
                    else
                    {
                        blocks.Add(block);
                    }
                }

                return blocks.ToImmutable();
            }

            private bool[] FindFoldedNullChecks(GenTreeBlock block)
            {
                var removed = new bool[block.Statements.Length];
                bool isInsideTry = (block.Flags & GenTreeBlockFlags.InTryRegion) != 0;

                for (int statementIndex = 0; statementIndex < block.Statements.Length; statementIndex++)
                {
                    if (!TryGetExplicitNullCheckValue(block.Statements[statementIndex], out SsaValueName value))
                        continue;

                    int nodesWalked = 0;
                    for (int laterIndex = statementIndex + 1; laterIndex < block.Statements.Length; laterIndex++)
                    {
                        bool blocked;
                        if (TryFindAbsorbingDereference(
                            block.Statements[laterIndex],
                            value,
                            isInsideTry,
                            ref nodesWalked,
                            out blocked))
                        {
                            removed[statementIndex] = true;
                            Changed = true;
                            break;
                        }

                        if (blocked)
                            break;
                    }
                }

                return removed;
            }

            private static bool TryGetExplicitNullCheckValue(GenTree statement, out SsaValueName value)
            {
                if (statement.Kind == GenTreeKind.NullCheck &&
                    statement.Operands.Length == 1 &&
                    statement.Operands[0].StackKind is GenStackKind.Ref or GenStackKind.Null)
                {
                    return TryGetSsaLocalUse(statement.Operands[0], out value);
                }

                value = default;
                return false;
            }

            private bool TryFindAbsorbingDereference(
                GenTree node,
                SsaValueName value,
                bool isInsideTry,
                ref int nodesWalked,
                out bool blocked)
            {
                for (int operandIndex = 0; operandIndex < node.Operands.Length; operandIndex++)
                {
                    if (TryFindAbsorbingDereference(
                        node.Operands[operandIndex],
                        value,
                        isInsideTry,
                        ref nodesWalked,
                        out blocked))
                        return true;
                    if (blocked)
                        return false;
                }

                if (nodesWalked++ > 50)
                {
                    blocked = true;
                    return false;
                }

                if (CanAbsorbNullCheck(node, value))
                {
                    blocked = false;
                    return true;
                }

                blocked = !CanMoveNullCheckPast(node, isInsideTry);
                return false;
            }

            private static bool CanAbsorbNullCheck(GenTree node, SsaValueName value)
            {
                if ((node.Flags & GenTreeFlags.NullCheckEliminated) != 0 || node.Operands.Length == 0)
                    return false;

                if (node.Kind is not (
                    GenTreeKind.Field or
                    GenTreeKind.FieldAddr or
                    GenTreeKind.StoreField or
                    GenTreeKind.ArrayLength or
                    GenTreeKind.ArrayElement or
                    GenTreeKind.ArrayElementAddr or
                    GenTreeKind.StoreArrayElement or
                    GenTreeKind.ArrayDataRef))
                {
                    return false;
                }

                return TryGetSsaLocalUse(node.Operands[0], out SsaValueName receiver) && receiver.Equals(value);
            }

            private bool CanMoveNullCheckPast(GenTree node, bool isInsideTry)
            {
                if (node.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp)
                {
                    if (node.LocalDescriptor is not { HasMemoryAlias: false } descriptor)
                        return false;

                    return !isInsideTry ||
                           (descriptor.Tracked && !_handlerLiveLocalNumbers.Contains(descriptor.LclNum));
                }

                if (node.CanThrow || node.ContainsCall || node.ReadsMemory || node.WritesMemory)
                    return false;

                if ((node.Flags & (GenTreeFlags.GlobalRef |
                                   GenTreeFlags.Allocation |
                                   GenTreeFlags.ControlFlow |
                                   GenTreeFlags.ExceptionFlow |
                                   GenTreeFlags.Ordered)) != 0)
                {
                    return false;
                }

                return !node.HasSideEffect;
            }


            private static HashSet<int> FindHandlerLiveLocalNumbers(SsaMethod method)
            {
                var result = new HashSet<int>();
                GenTreeLocalLiveness? liveness = method.GenTreeMethod.HirLiveness;
                if (liveness is null)
                    return result;

                for (int blockIndex = 0; blockIndex < method.Cfg.Blocks.Length; blockIndex++)
                {
                    CfgBlock block = method.Cfg.Blocks[blockIndex];
                    if (!block.IsInHandlerRegion && !block.IsHandlerEntry)
                        continue;

                    for (int localIndex = 0; localIndex < method.GenTreeMethod.AllLocalDescriptors.Length; localIndex++)
                    {
                        GenLocalDescriptor descriptor = method.GenTreeMethod.AllLocalDescriptors[localIndex];
                        if (!descriptor.Tracked)
                            continue;

                        var slot = new SsaSlot(descriptor);
                        if (liveness.IsLiveIn(block.Id, slot) || liveness.IsLiveOut(block.Id, slot))
                            result.Add(descriptor.LclNum);
                    }
                }

                return result;
            }

            private GenTree RewriteTree(GenTree node)
            {
                ImmutableArray<GenTree> operands = node.Operands;
                ImmutableArray<GenTree>.Builder? rewrittenOperands = null;

                for (int operandIndex = 0; operandIndex < operands.Length; operandIndex++)
                {
                    GenTree rewrittenOperand = RewriteTree(operands[operandIndex]);
                    if (!ReferenceEquals(rewrittenOperand, operands[operandIndex]))
                    {
                        rewrittenOperands ??= operands.ToBuilder();
                        rewrittenOperands[operandIndex] = rewrittenOperand;
                    }
                }

                ImmutableArray<GenTree> actualOperands = rewrittenOperands is null
                    ? operands
                    : rewrittenOperands.ToImmutable();

                if (node.Kind == GenTreeKind.ArrayLength &&
                    actualOperands.Length == 1 &&
                    TryResolveConstantArrayLength(actualOperands[0], out int length))
                {
                    Changed = true;
                    return new GenTree(
                        node.Id,
                        GenTreeKind.ConstI4,
                        node.Pc,
                        BytecodeOp.Ldc_I4,
                        type: node.Type,
                        stackKind: GenStackKind.I4,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int32: length);
                }

                GenTreeFlags flags = node.Flags;
                if ((node.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement) &&
                    (flags & GenTreeFlags.BoundsCheckEliminated) == 0 &&
                    actualOperands.Length >= 2 &&
                    TryResolveConstantArrayLength(actualOperands[0], out int arrayLength) &&
                    TryGetArrayIndexConstant(actualOperands[1], out long index) &&
                    index >= 0 &&
                    index < arrayLength)
                {
                    flags |= GenTreeFlags.BoundsCheckEliminated;
                }

                if (rewrittenOperands is null && flags == node.Flags)
                    return node;

                Changed = true;
                return CloneWithOperands(node, actualOperands, flags);
            }

            private bool TryResolveConstantArrayLength(GenTree receiver, out int length)
            {
                length = 0;
                if (!TryGetSsaLocalUse(receiver, out SsaValueName value))
                    return false;

                return TryResolveConstantArrayLength(value, 0, new HashSet<SsaValueName>(), out length);
            }

            private bool TryResolveConstantArrayLength(
                SsaValueName value,
                int depth,
                HashSet<SsaValueName> visited,
                out int length)
            {
                length = 0;
                if (depth > RecursionBound || !visited.Add(value))
                    return false;

                if (!_method.TryGetSsaDescriptor(value, out SsaDescriptor descriptor) ||
                    !descriptor.IsStore ||
                    descriptor.IsPartialDefinition ||
                    descriptor.DefNode is null)
                {
                    return false;
                }

                GenTree store = descriptor.DefNode;
                if (store.Kind is not (GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp) ||
                    store.Operands.Length != 1 ||
                    !store.SsaStoreTargetName.HasValue ||
                    !store.SsaStoreTargetName.Value.Equals(value))
                {
                    return false;
                }

                GenTree storedValue = store.Operands[0];
                if (TryGetSsaLocalUse(storedValue, out SsaValueName sourceValue))
                    return TryResolveConstantArrayLength(sourceValue, depth + 1, visited, out length);

                if (storedValue.Kind != GenTreeKind.NewArray || storedValue.Operands.Length != 1)
                    return false;

                return TryGetValidArrayLengthConstant(storedValue.Operands[0], out length);
            }

            private static bool TryGetSsaLocalUse(GenTree node, out SsaValueName value)
            {
                if ((node.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp) &&
                    node.SsaValueName.HasValue &&
                    node.SsaValueName.Value.Version > SsaConfig.ReservedSsaNumber)
                {
                    value = node.SsaValueName.Value;
                    return true;
                }

                value = default;
                return false;
            }

            private static bool TryGetValidArrayLengthConstant(GenTree node, out int length)
            {
                long value;
                if (node.Kind == GenTreeKind.ConstI4)
                {
                    value = node.Int32;
                }
                else if (node.Kind == GenTreeKind.ConstI8)
                {
                    value = node.Int64;
                }
                else
                {
                    length = 0;
                    return false;
                }

                if (value < 0 || value > Array.MaxLength)
                {
                    length = 0;
                    return false;
                }

                length = (int)value;
                return true;
            }

            private static bool TryGetArrayIndexConstant(GenTree node, out long index)
            {
                if (node.Kind == GenTreeKind.ConstI4)
                {
                    index = node.Int32;
                    return true;
                }

                if (node.Kind == GenTreeKind.ConstI8)
                {
                    index = node.Int64;
                    return true;
                }

                index = 0;
                return false;
            }


            private static bool NodeReadsMemory(GenTreeKind kind)
                => kind is
                    GenTreeKind.Field or
                    GenTreeKind.FieldAddr or
                    GenTreeKind.StaticField or
                    GenTreeKind.StaticFieldAddr or
                    GenTreeKind.LoadIndirect or
                    GenTreeKind.ArrayLength or
                    GenTreeKind.ArrayElement or
                    GenTreeKind.ArrayElementAddr or
                    GenTreeKind.ArrayDataRef;

            private static bool OperandsReadMemory(ImmutableArray<GenTree> operands)
            {
                for (int operandIndex = 0; operandIndex < operands.Length; operandIndex++)
                {
                    if (operands[operandIndex].ReadsMemory)
                        return true;
                }

                return false;
            }

            private static GenTree CloneWithOperands(
                GenTree source,
                ImmutableArray<GenTree> operands,
                GenTreeFlags flags)
            {
                if (!NodeReadsMemory(source.Kind) && !OperandsReadMemory(operands))
                    flags &= ~GenTreeFlags.MemoryRead;

                return new GenTree(
                    source.Id,
                    source.Kind,
                    source.Pc,
                    source.SourceOp,
                    source.Type,
                    source.StackKind,
                    flags,
                    operands,
                    source.Int32,
                    source.Int64,
                    source.Text,
                    source.RuntimeType,
                    source.Field,
                    source.Method,
                    source.ConvKind,
                    source.ConvFlags,
                    source.TargetPc,
                    source.TargetBlockId);
            }
        }
    }
}
