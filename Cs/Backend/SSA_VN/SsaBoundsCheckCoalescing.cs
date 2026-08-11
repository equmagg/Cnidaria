using System;
using System.Collections.Generic;

namespace Cnidaria.Cs
{
    internal static class SsaBoundsCheckCoalescer
    {
        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public readonly int BarrierCount;
            public readonly ValueNumber LengthValueNumber;

            public GroupKey(int barrierCount, ValueNumber lengthValueNumber)
            {
                BarrierCount = barrierCount;
                LengthValueNumber = lengthValueNumber;
            }

            public bool Equals(GroupKey other)
                => BarrierCount == other.BarrierCount && LengthValueNumber == other.LengthValueNumber;

            public override bool Equals(object? obj)
                => obj is GroupKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(BarrierCount, LengthValueNumber);
        }

        private readonly struct ArrayTypeCheckKey : IEquatable<ArrayTypeCheckKey>
        {
            public readonly ValueNumber ArrayValueNumber;
            public readonly int ElementTypeId;
            public readonly bool RequireExact;

            public ArrayTypeCheckKey(ValueNumber arrayValueNumber, int elementTypeId, bool requireExact)
            {
                ArrayValueNumber = arrayValueNumber;
                ElementTypeId = elementTypeId;
                RequireExact = requireExact;
            }

            public bool Equals(ArrayTypeCheckKey other)
                => ArrayValueNumber == other.ArrayValueNumber &&
                   ElementTypeId == other.ElementTypeId &&
                   RequireExact == other.RequireExact;

            public override bool Equals(object? obj)
                => obj is ArrayTypeCheckKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(ArrayValueNumber, ElementTypeId, RequireExact);
        }

        private sealed class Candidate
        {
            public GenTree BoundsCheckOwner { get; }
            public int OriginalOffset { get; }
            public int MaximumOffset { get; set; }

            public Candidate(GenTree boundsCheckOwner, int offset)
            {
                BoundsCheckOwner = boundsCheckOwner;
                OriginalOffset = offset;
                MaximumOffset = offset;
            }
        }

        public static bool OptimizeMethod(SsaMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.ValueNumbers is null || method.Blocks.IsDefaultOrEmpty)
                return false;

            var valueNumbers = method.ValueNumbers;
            var handlerLiveLocals = FindHandlerLiveLocalNumbers(method);
            bool modified = false;

            for (int blockIndex = 0; blockIndex < method.Blocks.Length; blockIndex++)
            {
                SsaBlock block = method.Blocks[blockIndex];
                int barrierCount = 0;
                bool blockHasEhSuccessors = HasPotentialEhSuccessors(block);
                var candidates = new List<Candidate>();
                var groups = new Dictionary<GroupKey, int>();
                var knownNonNullArrays = new HashSet<ValueNumber>();
                var knownArrayTypes = new HashSet<ArrayTypeCheckKey>();

                for (int nodeIndex = 0; nodeIndex < block.TreeList.Length; nodeIndex++)
                {
                    SsaTree tree = block.TreeList[nodeIndex].Tree;
                    GenTree node = tree.Source;

                    if (IsArrayBoundsCheckOwner(node))
                    {
                        ValueNumber arrayValueNumber = GetArrayValueNumber(tree, valueNumbers);

                        if (HasArrayPreCheckBarrier(node, arrayValueNumber, knownNonNullArrays, knownArrayTypes))
                            barrierCount++;

                        RecordArrayPreCheckProofs(node, arrayValueNumber, knownNonNullArrays, knownArrayTypes);

                        if (TryGetCandidate(node, tree, valueNumbers, arrayValueNumber, out int offset, out ValueNumber lengthValueNumber))
                        {
                            var key = new GroupKey(barrierCount, lengthValueNumber);
                            if (!groups.TryGetValue(key, out int candidateIndex))
                            {
                                groups.Add(key, candidates.Count);
                                candidates.Add(new Candidate(node, offset));
                            }
                            else if (offset > candidates[candidateIndex].MaximumOffset)
                            {
                                candidates[candidateIndex].MaximumOffset = offset;
                            }
                        }

                        if (HasArrayPostCheckBarrier(node))
                            barrierCount++;

                        continue;
                    }

                    if (IsSideEffectBarrier(method, node, blockHasEhSuccessors, handlerLiveLocals))
                        barrierCount++;
                }

                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    Candidate candidate = candidates[candidateIndex];
                    if (candidate.MaximumOffset == candidate.OriginalOffset)
                        continue;

                    candidate.BoundsCheckOwner.SetBoundsCheckIndexOverride(candidate.MaximumOffset);
                    modified = true;
                }
            }

            return modified;
        }

        private static bool IsArrayBoundsCheckOwner(GenTree node)
            => node.Kind is GenTreeKind.ArrayElement or GenTreeKind.ArrayElementAddr or GenTreeKind.StoreArrayElement;

        private static bool TryGetCandidate(
            GenTree node,
            SsaTree tree,
            SsaValueNumberingResult valueNumbers,
            ValueNumber arrayValueNumber,
            out int offset,
            out ValueNumber lengthValueNumber)
        {
            offset = 0;
            lengthValueNumber = default;

            if ((node.Flags & GenTreeFlags.BoundsCheckEliminated) != 0 || tree.Operands.Length < 2)
                return false;

            if (node.HasBoundsCheckIndexOverride)
            {
                offset = node.BoundsCheckIndexOverride;
            }
            else if (!TryGetInt32Constant(tree.Operands[1].Source, out offset))
            {
                return false;
            }

            if (offset < 0 || !arrayValueNumber.IsValid)
                return false;

            lengthValueNumber = valueNumbers.Store.VNForFunc(
                GenStackKind.I4,
                type: null,
                ValueNumberFunction.ArrayLength,
                arrayValueNumber);
            lengthValueNumber = valueNumbers.Store.VNNormalValue(lengthValueNumber);
            return lengthValueNumber.IsValid;
        }

        private static ValueNumber GetArrayValueNumber(SsaTree tree, SsaValueNumberingResult valueNumbers)
        {
            if (tree.Operands.Length == 0 ||
                !valueNumbers.TryGetTreeValue(tree.Operands[0].Source, out ValueNumberPair pair))
            {
                return default;
            }

            return valueNumbers.Store.VNNormalValue(pair.Conservative);
        }

        private static bool TryGetInt32Constant(GenTree node, out int value)
        {
            if (node.Kind == GenTreeKind.ConstI4)
            {
                value = node.Int32;
                return true;
            }

            if (node.Kind == GenTreeKind.ConstI8 && node.Int64 >= int.MinValue && node.Int64 <= int.MaxValue)
            {
                value = (int)node.Int64;
                return true;
            }

            value = 0;
            return false;
        }

        private static bool HasArrayPreCheckBarrier(
            GenTree node,
            ValueNumber arrayValueNumber,
            HashSet<ValueNumber> knownNonNullArrays,
            HashSet<ArrayTypeCheckKey> knownArrayTypes)
        {
            if (!arrayValueNumber.IsValid)
                return true;

            if ((node.Flags & GenTreeFlags.NullCheckEliminated) == 0 &&
                !knownNonNullArrays.Contains(arrayValueNumber))
            {
                return true;
            }

            RuntimeType? elementType = node.RuntimeType;
            if (elementType is null)
                return true;

            var key = new ArrayTypeCheckKey(
                arrayValueNumber,
                elementType.TypeId,
                node.Kind == GenTreeKind.ArrayElementAddr);
            return !knownArrayTypes.Contains(key);
        }

        private static void RecordArrayPreCheckProofs(
            GenTree node,
            ValueNumber arrayValueNumber,
            HashSet<ValueNumber> knownNonNullArrays,
            HashSet<ArrayTypeCheckKey> knownArrayTypes)
        {
            if (!arrayValueNumber.IsValid)
                return;

            knownNonNullArrays.Add(arrayValueNumber);

            RuntimeType? elementType = node.RuntimeType;
            if (elementType is null)
                return;

            knownArrayTypes.Add(new ArrayTypeCheckKey(
                arrayValueNumber,
                elementType.TypeId,
                node.Kind == GenTreeKind.ArrayElementAddr));
        }

        private static bool HasArrayPostCheckBarrier(GenTree node)
            => node.Kind == GenTreeKind.StoreArrayElement;

        private static bool IsSideEffectBarrier(
            SsaMethod method,
            GenTree node,
            bool blockHasEhSuccessors,
            HashSet<int> handlerLiveLocals)
        {
            if (RequiresCallFlag(node, method.GenTreeMethod.Target))
                return true;

            if (HasNodeOwnedOrderedSideEffect(node))
                return true;

            if (HasNodeOwnedNonRangeException(node, method.GenTreeMethod.Target))
                return true;

            if (!IsAssignment(node))
                return false;

            if (!IsLocalStore(node))
                return true;

            if (!blockHasEhSuccessors)
                return false;

            GenLocalDescriptor? descriptor = node.LocalDescriptor;
            return descriptor is null || !descriptor.Tracked || handlerLiveLocals.Contains(descriptor.LclNum);
        }

        private static bool RequiresCallFlag(GenTree node, TargetInfo target)
        {
            if (node.Kind is GenTreeKind.GcPoll or
                GenTreeKind.ClassInit or
                GenTreeKind.Call or
                GenTreeKind.IndirectCall or
                GenTreeKind.VirtualCall or
                GenTreeKind.DelegateInvoke or
                GenTreeKind.NewObject or
                GenTreeKind.NewDelegate or
                GenTreeKind.DelegateCombine or
                GenTreeKind.DelegateRemove or
                GenTreeKind.NewArray or
                GenTreeKind.Box or
                GenTreeKind.StaticData or
                GenTreeKind.StackAlloc)
            {
                return true;
            }

            return target.PointerSize == 4 &&
                   node.Kind == GenTreeKind.Binary &&
                   node.StackKind == GenStackKind.I8 &&
                   node.SourceOp is BytecodeOp.Shl or BytecodeOp.Shr or BytecodeOp.Shr_Un &&
                   (node.Operands.Length < 2 || !TryGetInt32Constant(node.Operands[1], out _));
        }

        private static bool HasNodeOwnedOrderedSideEffect(GenTree node)
            => node.Kind is GenTreeKind.NullCheck or
                GenTreeKind.Branch or
                GenTreeKind.BranchTrue or
                GenTreeKind.BranchFalse or
                GenTreeKind.Return or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow or
                GenTreeKind.EndFinally;

        private static bool HasNodeOwnedNonRangeException(GenTree node, TargetInfo target)
        {
            switch (node.Kind)
            {
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                case GenTreeKind.LoadIndirect:
                case GenTreeKind.NullCheck:
                case GenTreeKind.ArrayLength:
                case GenTreeKind.StoreField:
                case GenTreeKind.StoreIndirect:
                    return (node.Flags & GenTreeFlags.NullCheckEliminated) == 0;

                case GenTreeKind.ArrayDataRef:
                case GenTreeKind.StoreStaticField:
                case GenTreeKind.NewObject:
                case GenTreeKind.NewDelegate:
                case GenTreeKind.DelegateCombine:
                case GenTreeKind.DelegateRemove:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                case GenTreeKind.StaticData:
                case GenTreeKind.StackAlloc:
                case GenTreeKind.CastClass:
                case GenTreeKind.UnboxAny:
                case GenTreeKind.Throw:
                case GenTreeKind.Rethrow:
                    return true;

                case GenTreeKind.Binary:
                    return GenTreeArithmeticSemantics.BinaryOperationCanThrow(
                        node.SourceOp,
                        node.Type,
                        node.StackKind,
                        node.Operands,
                        target);

                case GenTreeKind.Conv:
                    return (node.ConvFlags & NumericConvFlags.Checked) != 0;

                default:
                    return false;
            }
        }

        private static bool IsAssignment(GenTree node)
            => node.Kind is GenTreeKind.StoreLocal or
                GenTreeKind.StoreArg or
                GenTreeKind.StoreTemp or
                GenTreeKind.StoreField or
                GenTreeKind.StoreStaticField or
                GenTreeKind.StoreIndirect or
                GenTreeKind.StoreArrayElement;

        private static bool IsLocalStore(GenTree node)
            => node.Kind is GenTreeKind.StoreLocal or GenTreeKind.StoreArg or GenTreeKind.StoreTemp;

        private static bool HasPotentialEhSuccessors(SsaBlock block)
            => !block.CfgBlock.TryRegionIndexes.IsDefaultOrEmpty &&
               ControlFlowGraph.BlockMayThrow(block.CfgBlock.SourceBlock);

        private static HashSet<int> FindHandlerLiveLocalNumbers(SsaMethod method)
        {
            var result = new HashSet<int>();
            GenTreeLocalLiveness? liveness = method.GenTreeMethod.HirLiveness;
            if (liveness is null)
            {
                for (int localIndex = 0; localIndex < method.GenTreeMethod.AllLocalDescriptors.Length; localIndex++)
                {
                    GenLocalDescriptor descriptor = method.GenTreeMethod.AllLocalDescriptors[localIndex];
                    if (descriptor.Tracked)
                        result.Add(descriptor.LclNum);
                }

                return result;
            }

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
    }
}
