using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class SsaVerifier
    {
        public static void Verify(SsaMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var definitions = BuildDefinitionMap(method);
            var memoryDefinitions = BuildMemoryDefinitionMap(method);
            var definitionsBySlot = BuildDefinitionsBySlot(definitions);
            var memoryDefinitionsByKind = BuildMemoryDefinitionsByKind(memoryDefinitions);
            var localLiveness = SsaLocalLiveness.Build(method);
            VerifyDescriptorTables(method, definitions);
            VerifySourceTreeIdentity(method);
            VerifyLclVarDscState(method);
            VerifyPrunedSsaLiveness(method, localLiveness);
            VerifyValueNumberBindings(method, definitions, memoryDefinitions);
            VerifyDescriptorUseCounts(method, definitions, memoryDefinitions);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                VerifyPhis(method, definitions, definitionsBySlot, localLiveness, block);
                VerifyMemoryBlockStates(method, memoryDefinitions, block);
                VerifyMemoryPhis(method, memoryDefinitions, memoryDefinitionsByKind, block);
                VerifyMemoryFlowWithinBlock(memoryDefinitions, block);

                for (int s = 0; s < block.Statements.Length; s++)
                    VerifyStatement(method, definitions, definitionsBySlot, memoryDefinitions, memoryDefinitionsByKind, localLiveness, block.Id, s, block.Statements[s]);
            }
        }

        private static void VerifySourceTreeIdentity(SsaMethod method)
        {
            if (!ReferenceEquals(method.GenTreeMethod.Ssa, method))
            {
                var attached = method.GenTreeMethod.Ssa;
                if (attached is not null && !ReferenceEquals(attached.GenTreeMethod, method.GenTreeMethod))
                    throw new InvalidOperationException("SSA method is attached to a different GenTree method.");
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                if ((uint)block.Id >= (uint)method.GenTreeMethod.Blocks.Length)
                    throw new InvalidOperationException($"SSA block B{block.Id} has no matching GenTree block.");

                var genBlock = method.GenTreeMethod.Blocks[block.Id];
                if (genBlock.Id != block.Id)
                    throw new InvalidOperationException($"SSA block B{block.Id} does not match GenTree block B{genBlock.Id}.");

                if (block.Statements.Length != genBlock.Statements.Length)
                    throw new InvalidOperationException($"SSA block B{block.Id} statement count does not match GenTree block.");

                if (block.StatementTreeLists.Length != genBlock.StatementTreeLists.Length)
                    throw new InvalidOperationException($"SSA block B{block.Id} statement tree-list count does not match GenTree block.");

                for (int s = 0; s < block.Statements.Length; s++)
                {
                    VerifySourceTreeIdentity(block.Statements[s], genBlock.Statements[s]);
                    VerifySourceTreeListIdentity(block.Id, s, block.StatementTreeLists[s], genBlock.StatementTreeLists[s]);
                }
            }
        }

        private static void VerifySourceTreeIdentity(SsaTree tree, GenTree source)
        {
            if (!ReferenceEquals(tree.Source, source))
                throw new InvalidOperationException($"SSA tree view points at node {tree.Source.Id}, but GenTree contains node {source.Id}.");

            var operands = tree.Operands;
            if (operands.Length != source.Operands.Length)
                throw new InvalidOperationException($"SSA tree view for node {source.Id} has stale operand count.");

            for (int i = 0; i < operands.Length; i++)
                VerifySourceTreeIdentity(operands[i], source.Operands[i]);
        }

        private static void VerifySourceTreeListIdentity(int blockId, int statementIndex, ImmutableArray<SsaTree> ssaTreeList, ImmutableArray<GenTree> genTreeList)
        {
            if (ssaTreeList.Length != genTreeList.Length)
                throw new InvalidOperationException($"SSA tree-list length differs from GenTree tree-list at B{blockId}:S{statementIndex}.");

            for (int i = 0; i < ssaTreeList.Length; i++)
            {
                if (!ReferenceEquals(ssaTreeList[i].Source, genTreeList[i]))
                    throw new InvalidOperationException($"SSA tree-list node at B{blockId}:S{statementIndex}:T{i} points at node {ssaTreeList[i].Source.Id}, but GenTree has node {genTreeList[i].Id}.");
            }
        }

        private static Dictionary<SsaValueName, SsaValueDefinition> BuildDefinitionMap(SsaMethod method)
        {
            var result = new Dictionary<SsaValueName, SsaValueDefinition>();

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                if (definition.Name.Version <= SsaConfig.ReservedSsaNumber)
                    throw new InvalidOperationException($"Invalid SSA version {definition.Name}.");

                if (!definition.Name.Slot.HasLclNum)
                    throw new InvalidOperationException($"SSA definition {definition.Name} does not carry a concrete lclNum.");

                if (definition.IsInitial)
                {
                    if (definition.Name.Version != SsaConfig.FirstSsaNumber || definition.DefBlockId != -1)
                        throw new InvalidOperationException($"Malformed initial SSA definition {definition.Name}.");
                }
                else if ((uint)definition.DefBlockId >= (uint)method.Blocks.Length)
                {
                    throw new InvalidOperationException($"SSA definition {definition.Name} has invalid block B{definition.DefBlockId}.");
                }

                if (definition.IsPhi && definition.DefStatementIndex != -1)
                    throw new InvalidOperationException($"Phi definition {definition.Name} has statement index {definition.DefStatementIndex}.");

                if (!definition.IsPhi && !definition.IsInitial && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException($"Tree definition {definition.Name} has no statement index.");

                if (result.ContainsKey(definition.Name))
                    throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");

                result.Add(definition.Name, definition);
            }

            for (int i = 0; i < method.InitialValues.Length; i++)
            {
                if (!result.TryGetValue(method.InitialValues[i], out var definition) || !definition.IsInitial)
                    throw new InvalidOperationException($"Initial SSA value {method.InitialValues[i]} is missing from definition table.");
            }

            return result;
        }


        private static Dictionary<SsaSlot, List<SsaValueDefinition>> BuildDefinitionsBySlot(Dictionary<SsaValueName, SsaValueDefinition> definitions)
        {
            var result = new Dictionary<SsaSlot, List<SsaValueDefinition>>();
            foreach (var item in definitions)
            {
                if (!result.TryGetValue(item.Key.Slot, out var list))
                {
                    list = new List<SsaValueDefinition>();
                    result.Add(item.Key.Slot, list);
                }
                list.Add(item.Value);
            }

            foreach (var item in result)
            {
                item.Value.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            }
            return result;
        }

        private static Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> BuildMemoryDefinitionsByKind(Dictionary<SsaMemoryValueName, SsaMemoryDefinition> definitions)
        {
            var result = new Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>>();
            foreach (var item in definitions)
            {
                if (!result.TryGetValue(item.Key.Kind, out var list))
                {
                    list = new List<SsaMemoryDefinition>();
                    result.Add(item.Key.Kind, list);
                }
                list.Add(item.Value);
            }

            foreach (var item in result)
            {
                item.Value.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            }
            return result;
        }

        private static Dictionary<SsaMemoryValueName, SsaMemoryDefinition> BuildMemoryDefinitionMap(SsaMethod method)
        {
            var result = new Dictionary<SsaMemoryValueName, SsaMemoryDefinition>();

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var definition = method.MemoryDefinitions[i];
                if (definition.Name.Version <= SsaConfig.ReservedSsaNumber)
                    throw new InvalidOperationException($"Invalid memory SSA version {definition.Name}.");

                if (definition.IsInitial)
                {
                    if (definition.Name.Version != SsaConfig.FirstSsaNumber || definition.DefBlockId != -1)
                        throw new InvalidOperationException($"Malformed initial memory SSA definition {definition.Name}.");
                }
                else if ((uint)definition.DefBlockId >= (uint)method.Blocks.Length)
                {
                    throw new InvalidOperationException($"Memory SSA definition {definition.Name} has invalid block B{definition.DefBlockId}.");
                }

                if (definition.IsPhi && definition.DefStatementIndex != -1)
                    throw new InvalidOperationException($"Memory phi definition {definition.Name} has statement index {definition.DefStatementIndex}.");

                if (definition.IsStore && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException($"Memory store definition {definition.Name}1 has no statement index.");

                if (definition.IsBlockOut && definition.DefStatementIndex < 0)
                    throw new InvalidOperationException($"Memory block-out definition {definition.Name} has no statement index.");

                if (result.ContainsKey(definition.Name))
                    throw new InvalidOperationException($"Duplicate memory SSA definition {definition.Name}.");

                result.Add(definition.Name, definition);
            }

            for (int i = 0; i < method.InitialMemoryValues.Length; i++)
            {
                if (!result.TryGetValue(method.InitialMemoryValues[i], out var definition) || !definition.IsInitial)
                    throw new InvalidOperationException($"Initial memory SSA value {method.InitialMemoryValues[i]} is missing from definition table.");
            }

            return result;
        }

        private static void VerifyDescriptorTables(SsaMethod method, Dictionary<SsaValueName, SsaValueDefinition> definitions)
        {
            var localsBySlot = new Dictionary<SsaSlot, SsaLocalDescriptor>();
            for (int i = 0; i < method.SsaLocalDescriptors.Length; i++)
            {
                var local = method.SsaLocalDescriptors[i];
                if (!local.Slot.HasLclNum)
                    throw new InvalidOperationException($"SSA local descriptor does not carry a concrete lclNum: {local.Slot}.");
                if (local.LocalDescriptor is not null && local.LocalDescriptor.LclNum != local.Slot.LclNum)
                    throw new InvalidOperationException($"SSA local descriptor lclNum disagrees with LclVarDsc: {local.Slot} vs {local.LocalDescriptor}.");
                if (localsBySlot.ContainsKey(local.Slot))
                    throw new InvalidOperationException($"Duplicate SSA local descriptor for {local.Slot}.");
                localsBySlot.Add(local.Slot, local);

                for (int ssaNum = SsaConfig.FirstSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                {
                    var descriptor = local.PerSsaData[ssaNum];
                    if (descriptor is null || !descriptor.BaseLocal.Equals(local.Slot) || descriptor.SsaNumber != ssaNum)
                        throw new InvalidOperationException($"Malformed SSA descriptor table entry for {local.Slot} at index {ssaNum}.");
                    if (!definitions.TryGetValue(descriptor.Name, out var definition))
                        throw new InvalidOperationException($"SSA descriptor {descriptor.Name} is missing from definition table.");
                    if (!ReferenceEquals(definition.Descriptor, descriptor))
                        throw new InvalidOperationException($"Definition table and descriptor table disagree for {descriptor.Name}.");
                    if (descriptor.HasUseDefSsaNum)
                    {
                        var use = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                        if (!definitions.ContainsKey(use))
                            throw new InvalidOperationException($"SSA use-def link {descriptor.Name} references missing use definition {use}.");
                    }
                }
            }

            foreach (var item in definitions)
            {
                if (!localsBySlot.TryGetValue(item.Key.Slot, out var local))
                    throw new InvalidOperationException("SSA definition " + item.Key + " is missing its base local descriptor.");
                if (!local.TryGetSsaDefByNumber(item.Key.Version, out var descriptor) || !ReferenceEquals(descriptor, item.Value.Descriptor))
                    throw new InvalidOperationException("SSA definition " + item.Key + " is not reachable through base local descriptor.");
            }
        }

        private static void VerifyLclVarDscState(SsaMethod method)
        {
            var descriptors = method.GenTreeMethod.AllLocalDescriptors;
            var seenVarIndex = new Dictionary<int, GenLocalDescriptor>();

            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor.LclNum != i)
                    throw new InvalidOperationException($"LclVarDsc table is not dense at lclNum {i}.");

                if (descriptor.VarIndex >= 0)
                {
                    if (!descriptor.Tracked)
                        throw new InvalidOperationException($"Untracked LclVarDsc has lvVarIndex: {descriptor}.");

                    if (descriptor.AddressExposed || descriptor.MemoryAliased)
                        throw new InvalidOperationException($"Address-exposed or memory-aliased LclVarDsc participates in tracked-local liveness: {descriptor}.");

                    if (seenVarIndex.TryGetValue(descriptor.VarIndex, out var other))
                        throw new InvalidOperationException($"Duplicate dense lvVarIndex {descriptor.VarIndex} for {other} and {descriptor}.");

                    seenVarIndex.Add(descriptor.VarIndex, descriptor);
                }

                if (descriptor.SsaPromoted && !descriptor.CanBeSsaRenamedAsScalar)
                    throw new InvalidOperationException($"Memory-aliased or non-scalar LclVarDsc is marked lvInSsa: {descriptor}.");

                if (descriptor.Category == GenLocalCategory.PromotedStructField)
                {
                    if (!descriptor.IsStructField || descriptor.ParentLclNum < 0 || descriptor.PromotedField is null)
                        throw new InvalidOperationException($"Malformed promoted struct field descriptor: {descriptor}.");

                    if ((uint)descriptor.ParentLclNum >= (uint)descriptors.Length)
                        throw new InvalidOperationException($"Promoted struct field {descriptor} has invalid parent lclNum {descriptor.ParentLclNum}.");

                    var parent = descriptors[descriptor.ParentLclNum];
                    if (parent.Category != GenLocalCategory.PromotedStruct)
                        throw new InvalidOperationException($"Promoted struct field {descriptor} parent is not a promoted struct: {parent}.");

                    if (!parent.TryGetPromotedField(descriptor.PromotedField, out var registeredField) || !ReferenceEquals(registeredField, descriptor))
                        throw new InvalidOperationException($"Promoted struct field {descriptor} is not registered in its parent descriptor map.");

                    if (descriptor.FieldOffset != descriptor.PromotedField.Offset)
                        throw new InvalidOperationException($"Promoted struct field {descriptor} has stale field offset.");

                    int expectedSize = Math.Max(1, descriptor.PromotedField.FieldType.SizeOf);
                    if (descriptor.FieldSize != expectedSize)
                        throw new InvalidOperationException($"Promoted struct field {descriptor} has stale field size.");
                }
            }

            for (int i = 0; i < seenVarIndex.Count; i++)
            {
                if (!seenVarIndex.ContainsKey(i))
                    throw new InvalidOperationException($"lvVarIndex is not dense; missing index {i}.");
            }

            for (int i = 0; i < method.SsaLocalDescriptors.Length; i++)
            {
                var local = method.SsaLocalDescriptors[i];
                var descriptor = local.LocalDescriptor;
                if (descriptor is null)
                    continue;

                if (local.PerSsaData.Length != descriptor.PerSsaData.Length)
                    throw new InvalidOperationException($"SsaLocalDescriptor and LclVarDsc lvPerSsaData length disagree for {descriptor}.");

                for (int ssaNum = SsaConfig.ReservedSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                {
                    if (!ReferenceEquals(local.PerSsaData[ssaNum], descriptor.PerSsaData[ssaNum]))
                        throw new InvalidOperationException($"SsaLocalDescriptor and LclVarDsc lvPerSsaData entry disagree for {descriptor} at {ssaNum}.");
                }
            }
        }


        private static bool IsPhiResultLiveAfterDefinition(SsaBlock block, SsaLocalLiveness liveness, SsaSlot slot)
        {
            if (block.Statements.Length == 0)
                return liveness.IsLiveOut(block.Id, slot);

            return liveness.IsLiveBeforeStatement(block.Id, 0, slot);
        }

        private static void VerifyPrunedSsaLiveness(SsaMethod method, SsaLocalLiveness liveness)
        {
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    if (!IsPhiResultLiveAfterDefinition(block, liveness, phi.Slot))
                        throw new InvalidOperationException($"Pruned SSA contains dead phi {phi.Target} in B{block.Id}; phi result is not live after the phi definition.");
                }

                var successors = block.CfgBlock.Successors;
                for (int s = 0; s < successors.Length; s++)
                {
                    int succ = successors[s].ToBlockId;
                    if ((uint)succ >= (uint)method.Blocks.Length)
                        continue;

                    var succBlock = method.Blocks[succ];
                    for (int p = 0; p < succBlock.Phis.Length; p++)
                    {
                        var phi = succBlock.Phis[p];
                        bool hasInput = false;
                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            if (phi.Inputs[i].PredecessorBlockId == block.Id)
                            {
                                hasInput = true;
                                break;
                            }
                        }

                        if (hasInput && IsPhiResultLiveAfterDefinition(succBlock, liveness, phi.Slot) && !liveness.IsLiveOut(block.Id, phi.Slot))
                            throw new InvalidOperationException($"SSA phi input for {phi.Target} from B{block.Id} uses a slot that is not live-out of the predecessor.");
                    }
                }
            }
        }

        private static void VerifyValueNumberBindings(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions)
        {
            if (method.ValueNumbers is null)
                return;

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                var descriptor = definition.Descriptor;
                if (!descriptor.ValueNumbers.Liberal.IsValid || !descriptor.ValueNumbers.Conservative.IsValid)
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} has no value-number pair.");

                if (method.ValueNumbers.Store.IsValueWithExc(descriptor.ValueNumbers.Liberal) ||
                    method.ValueNumbers.Store.IsValueWithExc(descriptor.ValueNumbers.Conservative))
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} stores a ValWithExc VN. " +
                        $"Persistent SSA names must store normal value VNs only.");

                if (!method.ValueNumbers.TryGetSsaValue(definition.Name, out var resultPair))
                    throw new InvalidOperationException($"SSA VN result table is missing {definition.Name}.");

                if (method.ValueNumbers.Store.IsValueWithExc(resultPair.Liberal) ||
                    method.ValueNumbers.Store.IsValueWithExc(resultPair.Conservative))
                    throw new InvalidOperationException($"SSA VN result table stores a ValWithExc VN for {definition.Name}. " +
                        $"Persistent SSA names must store normal value VNs only.");

                if (!resultPair.Equals(descriptor.ValueNumbers))
                    throw new InvalidOperationException($"SSA descriptor VN and VN result table disagree for {definition.Name}.");
            }

            foreach (var item in method.ValueNumbers.SsaValues)
            {
                if (!definitions.TryGetValue(item.Key, out var definition))
                    throw new InvalidOperationException($"SSA VN result table contains undefined value {item.Key}.");

                if (method.ValueNumbers.Store.IsValueWithExc(item.Value.Liberal) ||
                    method.ValueNumbers.Store.IsValueWithExc(item.Value.Conservative))
                    throw new InvalidOperationException($"SSA VN result table stores a ValWithExc VN for {item.Key}. " +
                        $"Persistent SSA names must store normal value VNs only.");

                if (!definition.Descriptor.ValueNumbers.Equals(item.Value))
                    throw new InvalidOperationException($"SSA VN result table bypasses descriptor value numbers for {item.Key}.");
            }

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var definition = method.MemoryDefinitions[i];
                if (!definition.Descriptor.ValueNumber.IsValid)
                    throw new InvalidOperationException($"Memory SSA descriptor {definition.Name} has no value number.");

                if (!method.ValueNumbers.TryGetMemoryValue(definition.Name, out var resultVn))
                    throw new InvalidOperationException($"Memory SSA VN result table is missing {definition.Name}.");

                if (resultVn != definition.Descriptor.ValueNumber)
                    throw new InvalidOperationException($"Memory SSA descriptor VN and VN result table disagree for {definition.Name}.");
            }

            foreach (var item in method.ValueNumbers.MemoryValues)
            {
                if (!memoryDefinitions.TryGetValue(item.Key, out var definition))
                    throw new InvalidOperationException($"Memory SSA VN result table contains undefined value {item.Key}.");

                if (definition.Descriptor.ValueNumber != item.Value)
                    throw new InvalidOperationException($"Memory SSA VN result table bypasses descriptor value number for {item.Key}.");
            }
        }

        private sealed class DescriptorUseStats
        {
            public int Count;
            public bool HasPhiUse;
            public bool HasGlobalUse;

            public void Add(int defBlockId, int useBlockId, bool isPhi)
            {
                if (Count < ushort.MaxValue)
                    Count++;
                if (isPhi)
                    HasPhiUse = true;
                if (defBlockId >= 0 && useBlockId >= 0 && defBlockId != useBlockId)
                    HasGlobalUse = true;
            }
        }

        private static void VerifyDescriptorUseCounts(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions)
        {
            var localUses = new Dictionary<SsaValueName, DescriptorUseStats>();
            var memoryUses = new Dictionary<SsaMemoryValueName, DescriptorUseStats>();

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var descriptor = method.ValueDefinitions[i].Descriptor;
                if (!descriptor.HasUseDefSsaNum)
                    continue;

                var previous = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                if (!definitions.TryGetValue(previous, out var previousDefinition))
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} has missing previous definition {previous}.");

                AddLocalUse(localUses, previousDefinition.Descriptor, descriptor.DefBlockId, isPhi: false);
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];

                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        var input = phi.Inputs[i].Value;
                        if (!definitions.TryGetValue(input, out var inputDefinition))
                            throw new InvalidOperationException($"Phi {phi.Target} uses missing SSA value {input}.");

                        AddLocalUse(localUses, inputDefinition.Descriptor, block.Id, isPhi: true);
                    }
                }

                for (int p = 0; p < block.MemoryPhis.Length; p++)
                {
                    var phi = block.MemoryPhis[p];
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        var input = phi.Inputs[i].Value;
                        if (!memoryDefinitions.TryGetValue(input, out var inputDefinition))
                            throw new InvalidOperationException($"Memory phi {phi.Target} uses missing memory SSA value {input}.");

                        AddMemoryUse(memoryUses, inputDefinition.Descriptor, block.Id, isPhi: true);
                    }
                }

                for (int s = 0; s < block.Statements.Length; s++)
                    AccumulateTreeUseStats(block.Statements[s], block.Id, definitions, memoryDefinitions, localUses, memoryUses);
            }

            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var descriptor = method.ValueDefinitions[i].Descriptor;
                localUses.TryGetValue(descriptor.Name, out var stats);
                int expectedUseCount = stats?.Count ?? 0;
                bool expectedPhiUse = stats?.HasPhiUse ?? false;
                bool expectedGlobalUse = stats?.HasGlobalUse ?? false;

                if (descriptor.UseCount != expectedUseCount)
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} use-count mismatch: descriptor={descriptor.UseCount}, recomputed={expectedUseCount}.");
                if (descriptor.HasPhiUse != expectedPhiUse)
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} phi-use flag mismatch.");
                if (descriptor.HasGlobalUse != expectedGlobalUse)
                    throw new InvalidOperationException($"SSA descriptor {descriptor.Name} global-use flag mismatch.");
            }

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var descriptor = method.MemoryDefinitions[i].Descriptor;
                memoryUses.TryGetValue(descriptor.Name, out var stats);
                int expectedUseCount = stats?.Count ?? 0;
                bool expectedPhiUse = stats?.HasPhiUse ?? false;
                bool expectedGlobalUse = stats?.HasGlobalUse ?? false;

                if (descriptor.UseCount != expectedUseCount)
                    throw new InvalidOperationException($"Memory SSA descriptor {descriptor.Name} use-count mismatch: descriptor={descriptor.UseCount}, recomputed={expectedUseCount}.");
                if (descriptor.HasPhiUse != expectedPhiUse)
                    throw new InvalidOperationException($"Memory SSA descriptor {descriptor.Name} phi-use flag mismatch.");
                if (descriptor.HasGlobalUse != expectedGlobalUse)
                    throw new InvalidOperationException($"Memory SSA descriptor {descriptor.Name} global-use flag mismatch.");
            }
        }

        private static void AddLocalUse(Dictionary<SsaValueName, DescriptorUseStats> uses, SsaDescriptor descriptor, int useBlockId, bool isPhi)
        {
            if (!uses.TryGetValue(descriptor.Name, out var stats))
            {
                stats = new DescriptorUseStats();
                uses.Add(descriptor.Name, stats);
            }

            stats.Add(descriptor.DefBlockId, useBlockId, isPhi);
        }

        private static void AddMemoryUse(Dictionary<SsaMemoryValueName, DescriptorUseStats> uses, SsaMemoryDescriptor descriptor, int useBlockId, bool isPhi)
        {
            if (!uses.TryGetValue(descriptor.Name, out var stats))
            {
                stats = new DescriptorUseStats();
                uses.Add(descriptor.Name, stats);
            }

            stats.Add(descriptor.DefBlockId, useBlockId, isPhi);
        }

        private static void AccumulateTreeUseStats(
            SsaTree tree,
            int blockId,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            Dictionary<SsaValueName, DescriptorUseStats> localUses,
            Dictionary<SsaMemoryValueName, DescriptorUseStats> memoryUses)
        {
            if (tree.Value.HasValue)
            {
                var value = tree.Value.Value;
                if (!definitions.TryGetValue(value, out var definition))
                    throw new InvalidOperationException($"Tree node {tree.Source.Id} uses missing SSA value {value}.");
                AddLocalUse(localUses, definition.Descriptor, blockId, isPhi: false);
            }

            if (tree.LocalFieldBaseValue.HasValue)
            {
                var value = tree.LocalFieldBaseValue.Value;
                if (!definitions.TryGetValue(value, out var definition))
                    throw new InvalidOperationException($"Tree node {tree.Source.Id} uses missing local-field base SSA value {value}.");
                AddLocalUse(localUses, definition.Descriptor, blockId, isPhi: false);
            }

            for (int i = 0; i < tree.MemoryUses.Length; i++)
            {
                var value = tree.MemoryUses[i];
                if (!memoryDefinitions.TryGetValue(value, out var definition))
                    throw new InvalidOperationException($"Tree node {tree.Source.Id} uses missing memory SSA value {value}.");
                AddMemoryUse(memoryUses, definition.Descriptor, blockId, isPhi: false);
            }

            for (int i = 0; i < tree.Operands.Length; i++)
                AccumulateTreeUseStats(tree.Operands[i], blockId, definitions, memoryDefinitions, localUses, memoryUses);
        }

        private static void VerifyPhis(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            SsaLocalLiveness localLiveness,
            SsaBlock block)
        {
            var expectedPreds = new HashSet<int>();
            for (int p = 0; p < block.CfgBlock.Predecessors.Length; p++)
                expectedPreds.Add(block.CfgBlock.Predecessors[p].FromBlockId);

            for (int i = 0; i < block.Phis.Length; i++)
            {
                var phi = block.Phis[i];
                if (!definitions.TryGetValue(phi.Target, out var definition))
                    throw new InvalidOperationException($"Phi target {phi.Target} in B{block.Id} is missing from definition table.");

                if (!definition.IsPhi || definition.DefBlockId != block.Id)
                    throw new InvalidOperationException($"Definition table entry for {phi.Target} does not match phi in B{block.Id}.");

                if (!ReferenceEquals(definition.Phi, phi) || !ReferenceEquals(definition.Descriptor.Phi, phi))
                    throw new InvalidOperationException($"SSA descriptor for phi {phi.Target} in B{block.Id} does not point back to its phi node.");

                if (!phi.Target.Slot.Equals(phi.Slot))
                    throw new InvalidOperationException($"Phi target {phi.Target} in B{block.Id} does not belong to phi slot {phi.Slot}.");

                var actualPreds = new HashSet<int>();
                for (int p = 0; p < phi.Inputs.Length; p++)
                {
                    var input = phi.Inputs[p];
                    if (!input.Value.Slot.Equals(phi.Slot))
                        throw new InvalidOperationException($"Phi {phi.Target} in B{block.Id} has cross-slot input {input.Value} from B{input.PredecessorBlockId}; " +
                            $"phi operands must remain on the same tracked local slot.");
                    actualPreds.Add(input.PredecessorBlockId);
                    if (block.CfgBlock.IsHandlerEntry)
                    {
                        if (!definitions.ContainsKey(input.Value))
                            throw new InvalidOperationException($"Handler phi {phi.Target} uses undefined value {input.Value} from B{input.PredecessorBlockId}.");
                    }
                    else
                    {
                        VerifyEdgeUse(method, definitions, definitionsBySlot, phi.Target, input.Value, input.PredecessorBlockId);
                    }
                }

                if (block.CfgBlock.IsHandlerEntry)
                {
                    foreach (int expectedPred in expectedPreds)
                    {
                        if (!actualPreds.Contains(expectedPred))
                            throw new InvalidOperationException($"Malformed handler phi {phi.Target} in B{block.Id}: missing predecessor B{expectedPred}.");
                    }
                }
                else if (!expectedPreds.SetEquals(actualPreds))
                {
                    throw new InvalidOperationException($"Malformed phi {phi.Target} in B{block.Id}: predecessor set mismatch.");
                }
            }
        }

        private static void VerifyStatement(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> memoryDefinitionsByKind,
            SsaLocalLiveness localLiveness,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            VerifyTreeUses(method, definitions, definitionsBySlot, memoryDefinitions, memoryDefinitionsByKind, localLiveness, blockId, statementIndex, tree);
            VerifyTreeDefinitions(method, definitions, definitionsBySlot, memoryDefinitions, localLiveness, blockId, statementIndex, tree);
        }

        private static void VerifyTreeUses(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> memoryDefinitionsByKind,
            SsaLocalLiveness localLiveness,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            if (tree.Value.HasValue)
                VerifyLocalUse(method, definitions, definitionsBySlot, localLiveness, tree.Value.Value, blockId, statementIndex, tree.Source.Id);

            if (tree.LocalFieldBaseValue.HasValue)
                VerifyLocalUse(method, definitions, definitionsBySlot, localLiveness, tree.LocalFieldBaseValue.Value, blockId, statementIndex, tree.Source.Id);

            for (int i = 0; i < tree.MemoryUses.Length; i++)
                VerifyMemoryUse(method, memoryDefinitions, memoryDefinitionsByKind, tree.MemoryUses[i], blockId, statementIndex, tree.Source.Id);

            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeUses(method, definitions, definitionsBySlot, memoryDefinitions, memoryDefinitionsByKind, localLiveness, blockId, statementIndex, tree.Operands[i]);
        }

        private static void VerifyTreeDefinitions(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            SsaLocalLiveness localLiveness,
            int blockId,
            int statementIndex,
            SsaTree tree)
        {
            for (int i = 0; i < tree.Operands.Length; i++)
                VerifyTreeDefinitions(method, definitions, definitionsBySlot, memoryDefinitions, localLiveness, blockId, statementIndex, tree.Operands[i]);

            if (tree.LocalFieldBaseValue.HasValue)
            {
                if (tree.LocalField is null)
                    throw new InvalidOperationException($"SSA local-field node {tree.Source.Id} has a base value but no field.");

                if (tree.StoreTarget.HasValue)
                    throw new InvalidOperationException($"Partial definition at node {tree.Source.Id} still carries node-level use-def metadata.");
            }

            if (tree.IsPartialDefinition && tree.LocalField is null)
                throw new InvalidOperationException($"Partial definition at node {tree.Source.Id} has no field metadata.");

            for (int i = 0; i < tree.MemoryDefinitions.Length; i++)
            {
                var memoryName = tree.MemoryDefinitions[i];
                if (!memoryDefinitions.TryGetValue(memoryName, out var memoryDefinition))
                    throw new InvalidOperationException($"Memory definition {memoryName} at node {tree.Source.Id} is missing from definition table.");
                if (!memoryDefinition.IsStore || memoryDefinition.DefBlockId != blockId
                    || memoryDefinition.DefStatementIndex != statementIndex || memoryDefinition.DefTreeId != tree.Source.Id)
                    throw new InvalidOperationException($"Memory definition table entry for {memoryName} does not match store node {tree.Source.Id}.");
            }

            if (!tree.StoreTarget.HasValue)
                return;

            var name = tree.StoreTarget.Value;
            if (!definitions.TryGetValue(name, out var definition))
                throw new InvalidOperationException($"Store target {name} at node {tree.Source.Id} is missing from definition table.");

            if (definition.IsInitial || definition.IsPhi || definition.DefBlockId != blockId
                || definition.DefStatementIndex != statementIndex || definition.DefTreeId != tree.Source.Id)
                throw new InvalidOperationException($"Definition table entry for {name} does not match store node {tree.Source.Id}.");

            if (tree.IsPartialDefinition)
            {
                if (!definition.Descriptor.HasUseDefSsaNum)
                    throw new InvalidOperationException($"Partial definition {name} at node {tree.Source.Id} has no descriptor use-def SSA number.");

                var previous = new SsaValueName(definition.Descriptor.BaseLocal, definition.Descriptor.UseDefSsaNumber);
                if (previous.Equals(name))
                    throw new InvalidOperationException($"Partial definition {name} at node {tree.Source.Id} has a self-referential use-def SSA number.");
                VerifyLocalUse(method, definitions, definitionsBySlot, localLiveness, previous, blockId, statementIndex, tree.Source.Id);
            }
            else if (definition.Descriptor.HasUseDefSsaNum)
            {
                throw new InvalidOperationException($"Full definition {name} at node {tree.Source.Id} unexpectedly has a descriptor use-def SSA number.");
            }
        }

        private static void VerifyMemoryBlockStates(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            SsaBlock block)
        {
            for (int k = 0; k < SsaMemoryKinds.All.Length; k++)
            {
                var kind = SsaMemoryKinds.All[k];
                if (!block.TryGetMemoryIn(kind, out var memoryIn))
                    throw new InvalidOperationException($"Block B{block.Id} has no incoming memory SSA value for {SsaMemoryKinds.Name(kind)}.");
                if (!memoryDefinitions.ContainsKey(memoryIn))
                    throw new InvalidOperationException($"Block B{block.Id} has undefined incoming memory SSA value {memoryIn}.");

                if (!block.TryGetMemoryOut(kind, out var memoryOut))
                    throw new InvalidOperationException($"Block B{block.Id} has no outgoing memory SSA value for {SsaMemoryKinds.Name(kind)}.");
                if (!memoryDefinitions.ContainsKey(memoryOut))
                    throw new InvalidOperationException($"Block B{block.Id} has undefined outgoing memory SSA value {memoryIn}.");
            }
        }

        private static void VerifyMemoryFlowWithinBlock(
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            SsaBlock block)
        {
            var current = new Dictionary<SsaMemoryKind, SsaMemoryValueName>();
            for (int k = 0; k < SsaMemoryKinds.All.Length; k++)
            {
                var kind = SsaMemoryKinds.All[k];
                if (!block.TryGetMemoryIn(kind, out var memoryIn))
                    throw new InvalidOperationException($"Block B{block.Id} has no incoming memory SSA value for {SsaMemoryKinds.Name(kind)}.");
                current[kind] = memoryIn;
            }

            for (int i = 0; i < block.TreeList.Length; i++)
            {
                var item = block.TreeList[i];
                var tree = item.Tree;
                for (int u = 0; u < tree.MemoryUses.Length; u++)
                {
                    var use = tree.MemoryUses[u];
                    if (!current.TryGetValue(use.Kind, out var expected) || !expected.Equals(use))
                    {
                        string expectedText = current.TryGetValue(use.Kind, out expected) ? expected.ToString() : "<missing>";
                        throw new InvalidOperationException(
                            $"Memory SSA use {use} at node {tree.Source.Id} in B{block.Id}" +
                            $" is not the current {SsaMemoryKinds.Name(use.Kind)} state. Expected {expectedText}.");
                    }
                }

                for (int d = 0; d < tree.MemoryDefinitions.Length; d++)
                {
                    var definition = tree.MemoryDefinitions[d];
                    if (!memoryDefinitions.TryGetValue(definition, out var metadata))
                        throw new InvalidOperationException($"Memory SSA definition {definition} at node {tree.Source.Id} is missing from definition table.");
                    if (!metadata.IsStore)
                        throw new InvalidOperationException($"Memory SSA definition {definition} at node {tree.Source.Id} is not a store definition.");
                    current[definition.Kind] = definition;
                }
            }

            for (int k = 0; k < SsaMemoryKinds.All.Length; k++)
            {
                var kind = SsaMemoryKinds.All[k];
                if (!block.TryGetMemoryOut(kind, out var actualOut))
                    throw new InvalidOperationException($"Block B{block.Id} has no outgoing memory SSA value for {SsaMemoryKinds.Name(kind)}.");
                var expectedOut = current[kind];
                if (!expectedOut.Equals(actualOut))
                    throw new InvalidOperationException(
                        $"Block B{block.Id} outgoing {SsaMemoryKinds.Name(kind)}" +
                        $" memory state is {actualOut}, expected last in-block state {expectedOut}.");
            }
        }

        private static void VerifyMemoryPhis(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> memoryDefinitions,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> memoryDefinitionsByKind,
            SsaBlock block)
        {
            var expectedPreds = new HashSet<int>();
            for (int p = 0; p < block.CfgBlock.Predecessors.Length; p++)
                expectedPreds.Add(block.CfgBlock.Predecessors[p].FromBlockId);

            for (int i = 0; i < block.MemoryPhis.Length; i++)
            {
                var phi = block.MemoryPhis[i];
                if (!memoryDefinitions.TryGetValue(phi.Target, out var definition))
                    throw new InvalidOperationException(
                        $"Memory phi target {phi.Target} in B{block.Id} is missing from definition table.");

                if (!definition.IsPhi || definition.DefBlockId != block.Id)
                    throw new InvalidOperationException(
                        $"Definition table entry for memory phi {phi.Target} does not match B{block.Id}.");

                if (!ReferenceEquals(definition.Phi, phi) || !ReferenceEquals(definition.Descriptor.Phi, phi))
                    throw new InvalidOperationException(
                        $"Memory SSA descriptor for phi {phi.Target} in B{block.Id} does not point back to its phi node.");

                if (phi.Target.Kind != phi.Kind)
                    throw new InvalidOperationException(
                        $"Memory phi target {phi.Target} in B{block.Id} does not belong to phi kind {SsaMemoryKinds.Name(phi.Kind)}.");

                if (!block.TryGetMemoryIn(phi.Kind, out var memoryIn) || !memoryIn.Equals(phi.Target))
                    throw new InvalidOperationException(
                        $"Block B{block.Id} incoming memory state for {SsaMemoryKinds.Name(phi.Kind)} does not point at its phi target.");

                var actualPreds = new HashSet<int>();
                for (int p = 0; p < phi.Inputs.Length; p++)
                {
                    var input = phi.Inputs[p];
                    if (input.Value.Kind != phi.Kind)
                        throw new InvalidOperationException(
                            $"Memory phi {phi.Target} in B{block.Id} has cross-kind input {input.Value} from B{input.PredecessorBlockId}.");
                    actualPreds.Add(input.PredecessorBlockId);
                    if (block.CfgBlock.IsHandlerEntry)
                    {
                        if (!memoryDefinitions.ContainsKey(input.Value))
                            throw new InvalidOperationException(
                                $"Handler memory phi {phi.Target} uses undefined value {input.Value} from B{input.PredecessorBlockId}.");
                    }
                    else
                    {
                        VerifyMemoryEdgeUse(method, memoryDefinitions, memoryDefinitionsByKind, phi.Target, input.Value, input.PredecessorBlockId);
                    }
                }

                if (block.CfgBlock.IsHandlerEntry)
                {
                    foreach (int expectedPred in expectedPreds)
                    {
                        if (!actualPreds.Contains(expectedPred))
                            throw new InvalidOperationException(
                                $"Malformed handler memory phi {phi.Target} in B{block.Id}: missing predecessor B{expectedPred}.");
                    }
                }
                else if (!expectedPreds.SetEquals(actualPreds))
                {
                    throw new InvalidOperationException($"Malformed memory phi {phi.Target} in B{block.Id}: predecessor set mismatch.");
                }
            }
        }


        private static bool DefinitionDominatesUsePoint(SsaMethod method, SsaValueDefinition definition, int useBlockId, int useStatementIndex, int useTreeId)
        {
            if (definition.IsInitial)
                return true;

            if (!method.Cfg.Dominates(definition.DefBlockId, useBlockId))
                return false;

            if (definition.DefBlockId != useBlockId)
                return true;

            if (definition.IsPhi)
                return true;

            return ValueDefinitionPrecedesTreeUse(method, definition, useBlockId, useStatementIndex, useTreeId);
        }

        private static bool DefinitionDominatesBlockEnd(SsaMethod method, SsaValueDefinition definition, int blockId)
        {
            if (definition.IsInitial)
                return true;

            return method.Cfg.Dominates(definition.DefBlockId, blockId);
        }

        private static bool DefinitionDominatesDefinition(SsaMethod method, SsaValueDefinition left, SsaValueDefinition right)
        {
            if (left.Name.Equals(right.Name))
                return true;

            if (left.IsInitial)
                return true;
            if (right.IsInitial)
                return false;

            if (!method.Cfg.Dominates(left.DefBlockId, right.DefBlockId))
                return false;

            if (left.DefBlockId != right.DefBlockId)
                return true;

            if (left.IsPhi)
                return true;
            if (right.IsPhi)
                return false;

            if (left.DefStatementIndex < right.DefStatementIndex)
                return true;
            if (left.DefStatementIndex > right.DefStatementIndex)
                return false;

            int leftOrder = GetTreeOrder(method, left.DefBlockId, left.DefStatementIndex, left.DefTreeId);
            int rightOrder = GetTreeOrder(method, right.DefBlockId, right.DefStatementIndex, right.DefTreeId);
            return leftOrder >= 0 && rightOrder >= 0 && leftOrder < rightOrder;
        }
        private static bool ValueDefinitionPrecedesTreeUse(
            SsaMethod method, SsaValueDefinition definition, int useBlockId, int useStatementIndex, int useTreeId)
        {
            if (definition.DefBlockId != useBlockId)
                return true;

            if (definition.DefStatementIndex < useStatementIndex)
                return true;
            if (definition.DefStatementIndex > useStatementIndex)
                return false;

            if (useTreeId < 0)
                return false;

            int defOrder = GetTreeOrder(method, definition.DefBlockId, definition.DefStatementIndex, definition.DefTreeId);
            int useOrder = GetTreeOrder(method, useBlockId, useStatementIndex, useTreeId);
            return defOrder >= 0 && useOrder >= 0 && defOrder < useOrder;
        }
        private static void VerifyNoInterveningSameSlotDefinition(
            SsaMethod method,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            SsaValueDefinition chosenDefinition,
            int useBlockId,
            int useStatementIndex,
            int useTreeId,
            string useDescription)
        {
            if (!definitionsBySlot.TryGetValue(chosenDefinition.Name.Slot, out var sameSlotDefinitions))
                return;

            for (int i = 0; i < sameSlotDefinitions.Count; i++)
            {
                var candidate = sameSlotDefinitions[i];
                if (candidate.Name.Equals(chosenDefinition.Name))
                    continue;

                if (!DefinitionDominatesUsePoint(method, candidate, useBlockId, useStatementIndex, useTreeId))
                    continue;

                if (DefinitionDominatesDefinition(method, chosenDefinition, candidate))
                {
                    throw new InvalidOperationException(
                        $"SSA use {chosenDefinition.Name} at {useDescription}" +
                        $" is not the nearest dominating definition for {chosenDefinition.Name.Slot}" +
                        $"; intervening definition is {candidate.Name}. This would cross a phi/store and violates conventional SSA.");
                }
            }
        }

        private static bool MemoryDefinitionDominatesUsePoint(
            SsaMethod method, SsaMemoryDefinition definition, int useBlockId, int useStatementIndex, int useTreeId)
        {
            if (definition.IsInitial)
                return true;

            if (!method.Cfg.Dominates(definition.DefBlockId, useBlockId))
                return false;

            if (definition.DefBlockId != useBlockId)
                return true;

            if (definition.IsPhi)
                return true;

            if (definition.IsBlockOut)
                return false;

            return MemoryDefinitionPrecedesTreeUse(method, definition, useBlockId, useStatementIndex, useTreeId);
        }

        private static bool MemoryDefinitionDominatesBlockEnd(SsaMethod method, SsaMemoryDefinition definition, int blockId)
        {
            if (definition.IsInitial)
                return true;

            return method.Cfg.Dominates(definition.DefBlockId, blockId);
        }

        private static bool MemoryDefinitionDominatesDefinition(SsaMethod method, SsaMemoryDefinition left, SsaMemoryDefinition right)
        {
            if (left.Name.Equals(right.Name))
                return true;

            if (left.IsInitial)
                return true;
            if (right.IsInitial)
                return false;

            if (!method.Cfg.Dominates(left.DefBlockId, right.DefBlockId))
                return false;

            if (left.DefBlockId != right.DefBlockId)
                return true;

            if (left.IsPhi)
                return true;
            if (right.IsPhi)
                return false;

            if (left.IsBlockOut || right.IsBlockOut)
                return false;

            if (left.DefStatementIndex < right.DefStatementIndex)
                return true;
            if (left.DefStatementIndex > right.DefStatementIndex)
                return false;

            int leftOrder = GetTreeOrder(method, left.DefBlockId, left.DefStatementIndex, left.DefTreeId);
            int rightOrder = GetTreeOrder(method, right.DefBlockId, right.DefStatementIndex, right.DefTreeId);
            return leftOrder >= 0 && rightOrder >= 0 && leftOrder < rightOrder;
        }
        private static bool MemoryDefinitionPrecedesTreeUse(
            SsaMethod method, SsaMemoryDefinition definition, int useBlockId, int useStatementIndex, int useTreeId)
        {
            if (definition.DefBlockId != useBlockId)
                return true;

            if (definition.DefStatementIndex < useStatementIndex)
                return true;
            if (definition.DefStatementIndex > useStatementIndex)
                return false;

            int defOrder = GetTreeOrder(method, definition.DefBlockId, definition.DefStatementIndex, definition.DefTreeId);
            int useOrder = GetTreeOrder(method, useBlockId, useStatementIndex, useTreeId);
            return defOrder >= 0 && useOrder >= 0 && defOrder < useOrder;
        }
        private static int GetTreeOrder(SsaMethod method, int blockId, int statementIndex, int treeId)
        {
            if (blockId < 0)
                return -1;

            SsaBlock? block = null;
            if ((uint)blockId < (uint)method.Blocks.Length && method.Blocks[blockId].Id == blockId)
            {
                block = method.Blocks[blockId];
            }
            else
            {
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    if (method.Blocks[b].Id == blockId)
                    {
                        block = method.Blocks[b];
                        break;
                    }
                }
            }

            if (block is null)
                return -1;

            for (int i = 0; i < block.TreeList.Length; i++)
            {
                var item = block.TreeList[i];
                if (item.StatementIndex == statementIndex && item.Tree.Source.Id == treeId)
                    return item.BlockOrdinal;
            }

            return -1;
        }
        private static void VerifyNoInterveningSameMemoryDefinition(
            SsaMethod method,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> definitionsByKind,
            SsaMemoryDefinition chosenDefinition,
            int useBlockId,
            int useStatementIndex,
            int useTreeId,
            string useDescription)
        {
            if (!definitionsByKind.TryGetValue(chosenDefinition.Name.Kind, out var sameKindDefinitions))
                return;

            for (int i = 0; i < sameKindDefinitions.Count; i++)
            {
                var candidate = sameKindDefinitions[i];
                if (candidate.Name.Equals(chosenDefinition.Name) || candidate.IsBlockOut)
                    continue;

                if (!MemoryDefinitionDominatesUsePoint(method, candidate, useBlockId, useStatementIndex, useTreeId))
                    continue;

                if (MemoryDefinitionDominatesDefinition(method, chosenDefinition, candidate))
                {
                    throw new InvalidOperationException(
                        $"Memory SSA edge use {chosenDefinition.Name} at {useDescription}" +
                        $" is not the nearest dominating {SsaMemoryKinds.Name(chosenDefinition.Name.Kind)}" +
                        $" definition; intervening definition is {candidate.Name}.");
                }
            }
        }

        private static void VerifyNoInterveningSameMemoryDefinitionAtBlockEnd(
            SsaMethod method,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> definitionsByKind,
            SsaMemoryDefinition chosenDefinition,
            int predecessorBlockId,
            string useDescription)
        {
            if (!definitionsByKind.TryGetValue(chosenDefinition.Name.Kind, out var sameKindDefinitions))
                return;

            for (int i = 0; i < sameKindDefinitions.Count; i++)
            {
                var candidate = sameKindDefinitions[i];
                if (candidate.Name.Equals(chosenDefinition.Name) || candidate.IsBlockOut)
                    continue;

                if (!MemoryDefinitionDominatesBlockEnd(method, candidate, predecessorBlockId))
                    continue;

                if (MemoryDefinitionDominatesDefinition(method, chosenDefinition, candidate))
                {
                    throw new InvalidOperationException(
                        $"Memory SSA edge use {chosenDefinition.Name} at {useDescription}" +
                        $" is not the last reaching {SsaMemoryKinds.Name(chosenDefinition.Name.Kind)}" +
                        $" definition at the end of B{predecessorBlockId}; intervening definition is {candidate.Name}.");
                }
            }
        }

        private static void VerifyMemoryUse(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> definitions,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> definitionsByKind,
            SsaMemoryValueName use,
            int useBlockId,
            int useStatementIndex,
            int useTreeId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Use of undefined memory SSA value {use} at node {useTreeId}.");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, useBlockId))
                throw new InvalidOperationException($"Memory SSA definition {use} in B{definition.DefBlockId}" +
                    $" does not dominate use at node {useTreeId} in B{useBlockId}.");

            if (definition.DefBlockId == useBlockId && !definition.IsPhi
                && !MemoryDefinitionPrecedesTreeUse(method, definition, useBlockId, useStatementIndex, useTreeId))
                throw new InvalidOperationException($"Memory SSA definition {use} at statement {definition.DefStatementIndex}, " +
                    $"node {definition.DefTreeId} does not precede use at statement {useStatementIndex}, node {useTreeId} in B{useBlockId}.");

            VerifyNoInterveningSameMemoryDefinition(method, definitionsByKind, definition, useBlockId, useStatementIndex, useTreeId, $"node {useTreeId} in B{useBlockId}");
        }

        private static void VerifyMemoryEdgeUse(
            SsaMethod method,
            Dictionary<SsaMemoryValueName, SsaMemoryDefinition> definitions,
            Dictionary<SsaMemoryKind, List<SsaMemoryDefinition>> definitionsByKind,
            SsaMemoryValueName phiTarget,
            SsaMemoryValueName use,
            int predecessorBlockId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Memory phi {phiTarget} uses undefined value {use} from B{predecessorBlockId}.");

            if (definition.IsInitial)
                return;

            if (!method.Cfg.Dominates(definition.DefBlockId, predecessorBlockId))
                throw new InvalidOperationException(
                    $"Memory phi {phiTarget} input {use} from B{predecessorBlockId} is not dominated by its definition in B{definition.DefBlockId}.");

            VerifyNoInterveningSameMemoryDefinitionAtBlockEnd(method, definitionsByKind, definition, predecessorBlockId,
                $"memory phi {phiTarget} input from B{predecessorBlockId}");
        }

        private static void VerifyLocalUse(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            SsaLocalLiveness localLiveness,
            SsaValueName use,
            int useBlockId,
            int useStatementIndex,
            int useTreeId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Use of undefined SSA value {use} at node {useTreeId}.");

            if (!localLiveness.IsLiveBeforeTree(useBlockId, useStatementIndex, useTreeId, use.Slot))
                throw new InvalidOperationException($"Use of {use} at node {useTreeId} occurs where its base local is not live; this is illegal with pruned SSA.");

            if (!DefinitionDominatesUsePoint(method, definition, useBlockId, useStatementIndex, useTreeId))
                throw new InvalidOperationException($"SSA definition {use} in B{definition.DefBlockId} does not dominate use at node {useTreeId} in B{useBlockId}.");

            VerifyNoInterveningSameSlotDefinition(method, definitionsBySlot, definition, useBlockId, useStatementIndex, useTreeId, $"node {useTreeId}");
        }

        private static void VerifyEdgeUse(
            SsaMethod method,
            Dictionary<SsaValueName, SsaValueDefinition> definitions,
            Dictionary<SsaSlot, List<SsaValueDefinition>> definitionsBySlot,
            SsaValueName phiTarget,
            SsaValueName use,
            int predecessorBlockId)
        {
            if (!definitions.TryGetValue(use, out var definition))
                throw new InvalidOperationException($"Phi {phiTarget} uses undefined value {use} from B{predecessorBlockId}.");

            if (!DefinitionDominatesBlockEnd(method, definition, predecessorBlockId))
                throw new InvalidOperationException($"Phi {phiTarget} input {use} from B{predecessorBlockId} is not dominated by its definition in B{definition.DefBlockId}.");

            VerifyNoInterveningSameSlotDefinition(method, definitionsBySlot, definition, predecessorBlockId, int.MaxValue, -1, $"phi edge B{predecessorBlockId} -> {phiTarget}");
        }
    }
}
