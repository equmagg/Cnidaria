using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal readonly struct DeadStoreRemovalResult
    {
        public GenTreeMethod Method { get; }
        public int RemovedStoreCount { get; }
        public bool Changed => RemovedStoreCount != 0;

        public DeadStoreRemovalResult(GenTreeMethod method, int removedStoreCount)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RemovedStoreCount = removedStoreCount;
        }
    }

    internal static class SsaDeadStoreRemoval
    {
        public static DeadStoreRemovalResult Run(SsaMethod method, bool validate = true)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (method.ValueNumbers is null)
                throw new InvalidOperationException("Dead-store removal requires value numbering.");

            if (validate)
                SsaVerifier.Verify(method);

            if (HasPromotedStructStorage(method.GenTreeMethod))
                return new DeadStoreRemovalResult(method.GenTreeMethod, 0);

            var redundantStores = FindRedundantStores(method);
            if (redundantStores.Count == 0)
                return new DeadStoreRemovalResult(method.GenTreeMethod, 0);

            foreach (var candidate in redundantStores)
                RewriteAsEval(candidate.Store, candidate.Data);

            var rewrittenMethod = method.GenTreeMethod.CloneWithBlocks(method.GenTreeMethod.Blocks);
            NormalizeTreeFlags(rewrittenMethod);
            return new DeadStoreRemovalResult(rewrittenMethod, redundantStores.Count);
        }

        private static List<StoreCandidate> FindRedundantStores(SsaMethod method)
        {
            var result = new List<StoreCandidate>();
            var seenStores = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            var valueNumbers = method.ValueNumbers!;
            bool isAsyncStateMachineMoveNext = IsAsyncStateMachineMoveNext(method.GenTreeMethod.RuntimeMethod);

            for (int l = 0; l < method.SsaLocalDescriptors.Length; l++)
            {
                var local = method.SsaLocalDescriptors[l];
                if (!CanOptimizeLocal(local, isAsyncStateMachineMoveNext))
                    continue;

                for (int ssaNumber = SsaConfig.FirstSsaNumber + 1; ssaNumber < local.PerSsaData.Length; ssaNumber++)
                {
                    var definition = local.PerSsaData[ssaNumber];
                    if (definition is null || !definition.IsStore || definition.IsPartialDefinition || definition.DefNode is null)
                        continue;

                    var store = definition.DefNode;
                    if ((store.Flags & GenTreeFlags.ExplicitInit) != 0)
                        continue;
                    if (!store.SsaStoreTargetName.HasValue || !store.SsaStoreTargetName.Value.Equals(definition.Name))
                        continue;
                    if (!TryGetDirectStoreData(store, definition.BaseLocal, out var data))
                        continue;
                    if (!valueNumbers.TryGetTreeValue(data, out var dataValue) || !dataValue.Conservative.IsValid)
                        continue;

                    var previous = local.PerSsaData[ssaNumber - 1];
                    if (previous is null || !previous.ValueNumbers.Conservative.IsValid)
                        continue;
                    if (!DefinitionsShareBlock(method, previous, definition))
                        continue;
                    if (ssaNumber == SsaConfig.FirstSsaNumber + 1)
                        continue;
                    if (previous.ValueNumbers.Conservative != dataValue.Conservative)
                        continue;
                    if (!seenStores.Add(store))
                        throw new InvalidOperationException("One store is referenced by multiple SSA definitions.");

                    result.Add(new StoreCandidate(store, data));
                }
            }

            return result;
        }

        private static bool CanOptimizeLocal(SsaLocalDescriptor local, bool isAsyncStateMachineMoveNext)
        {
            if (!local.IsSsaPromoted || local.PerSsaData.Length <= SsaConfig.FirstSsaNumber + 1)
                return false;
            if (local.LocalDescriptor is null)
                return false;
            if (local.LocalDescriptor.IsStructField || local.LocalDescriptor.IsStructMaterializationTemp)
                return false;
            if (local.StackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                return false;
            if (local.Type?.Kind is RuntimeTypeKind.Struct or RuntimeTypeKind.TypeParam)
                return false;
            if (MustSkipLocal(isAsyncStateMachineMoveNext, local))
                return false;

            return true;
        }

        private static bool TryGetDirectStoreData(GenTree store, SsaSlot expectedSlot, out GenTree data)
        {
            data = null!;

            if (store.Operands.Length != 1)
                return false;
            if (!SsaSlotHelpers.TryGetDirectStoreSlot(store, out var slot) || !slot.Equals(expectedSlot))
                return false;

            data = store.Operands[0];
            return true;
        }

        private static bool DefinitionsShareBlock(SsaMethod method, SsaDescriptor previous, SsaDescriptor current)
        {
            if (!previous.IsInitial)
                return previous.DefBlockId == current.DefBlockId;

            if ((uint)current.DefBlockId >= (uint)method.GenTreeMethod.Blocks.Length)
                return false;

            return (method.GenTreeMethod.Blocks[current.DefBlockId].Flags & GenTreeBlockFlags.Entry) != 0;
        }

        private static bool HasPromotedStructStorage(GenTreeMethod method)
        {
            var descriptors = method.AllLocalDescriptors;
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor.IsStructField || descriptor.Promoted || descriptor.IsStructMaterializationTemp)
                    return true;
                if (descriptor.Type?.Kind == RuntimeTypeKind.Struct)
                    return true;
            }

            return false;
        }

        private static bool MustSkipLocal(bool isAsyncStateMachineMoveNext, SsaLocalDescriptor local)
        {
            if (!isAsyncStateMachineMoveNext)
                return false;
            if (local.StackKind == GenStackKind.ByRef || local.Type?.Kind == RuntimeTypeKind.ByRef)
                return true;

            return false;
        }

        private static bool IsAsyncStateMachineMoveNext(RuntimeMethod method)
        {
            if (!StringComparer.Ordinal.Equals(method.Name, "MoveNext"))
                return false;

            for (RuntimeType? type = method.DeclaringType; type is not null; type = type.BaseType)
            {
                var interfaces = type.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                {
                    var candidate = interfaces[i];
                    if (StringComparer.Ordinal.Equals(candidate.Namespace, "System.Runtime.CompilerServices") &&
                        StringComparer.Ordinal.Equals(candidate.Name, "IAsyncStateMachine"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RewriteAsEval(GenTree store, GenTree data)
        {
            var oldOperands = store.Operands;
            for (int i = 0; i < oldOperands.Length; i++)
            {
                if (!ReferenceEquals(oldOperands[i], data))
                    oldOperands[i].SetParent(null);
            }

            store.Kind = GenTreeKind.Eval;
            store.SourceOp = BytecodeOp.Pop;
            store.Type = null;
            store.StackKind = GenStackKind.Void;
            store.Flags = data.Flags & ~(GenTreeFlags.AssertionProperties | GenTreeFlags.MakeCse | GenTreeFlags.ExplicitInit);
            store.LocalDescriptor = null;
            store.SetOperands(ImmutableArray.Create(data));
            store.ClearSsaAnnotation();
        }

        private static void NormalizeTreeFlags(GenTreeMethod method)
        {
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    GenTreeMorpher.NormalizeTreeFlags(statements[s], method.Target);
            }
        }

        private readonly struct StoreCandidate
        {
            public GenTree Store { get; }
            public GenTree Data { get; }

            public StoreCandidate(GenTree store, GenTree data)
            {
                Store = store;
                Data = data;
            }
        }
    }
}
