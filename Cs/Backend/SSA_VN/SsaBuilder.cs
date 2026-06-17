using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class GenTreeSsaBuilder
    {
        public static SsaMethod BuildMethod(
            GenTreeMethod method,
            ControlFlowGraph cfg,
            GenTreeLocalLiveness? hirLiveness = null,
            bool validate = true)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (cfg is null) throw new ArgumentNullException(nameof(cfg));
            if (method.Phase < GenTreeMethodPhase.HirLiveness)
                throw new InvalidOperationException("SSA construction requires morph, local rewriting, CFG construction, and HIR liveness to have already run.");

            if (!ReferenceEquals(cfg, method.Cfg))
                throw new InvalidOperationException("SSA construction was given a CFG that is not attached to the GenTree method.");

            hirLiveness ??= method.HirLiveness ?? GenTreeLocalLiveness.Build(method, cfg);
            if (!ReferenceEquals(hirLiveness.Cfg, cfg))
                throw new InvalidOperationException("SSA construction was given HIR liveness for a different CFG.");

            SsaSourceAnnotations.Clear(method);

            var slotTable = SlotTable.Build(method, cfg, hirLiveness);
            var liveness = Liveness.FromHirLiveness(cfg, slotTable.PromotableSlots, hirLiveness);
            var memoryLiveness = MemoryLiveness.Build(cfg, slotTable);
            var phis = InsertPhis(cfg, slotTable.PromotableSlots, liveness);
            var memoryPhis = InsertMemoryPhis(cfg, memoryLiveness);
            var rename = new RenameState(cfg, slotTable, phis, memoryPhis, liveness, memoryLiveness);
            var blocks = rename.Run();
            var initialValues = rename.InitialValues.ToImmutableArray();
            var initialMemoryValues = rename.InitialMemoryValues.ToImmutableArray();
            var useDefLinks = rename.UseDefLinks;
            var valueDefinitions = BuildValueDefinitions(slotTable, initialValues, blocks, useDefLinks);
            var memoryDefinitions = BuildMemoryDefinitions(initialMemoryValues, blocks);
            AnnotateSsaUses(valueDefinitions, memoryDefinitions, blocks);
            var ssaLocalDescriptors = BuildSsaLocalDescriptors(slotTable, valueDefinitions);

            var result = new SsaMethod(
                method,
                cfg,
                slotTable.AllSlots.ToImmutableArray(),
                initialValues,
                valueDefinitions,
                blocks,
                valueNumbers: null,
                ssaLocalDescriptors: ssaLocalDescriptors,
                initialMemoryValues: initialMemoryValues,
                memoryDefinitions: memoryDefinitions);

            SsaSourceAnnotations.Attach(result);

            if (validate)
                SsaVerifier.Verify(result);

            return result;
        }

        private static ImmutableArray<SsaValueDefinition> BuildValueDefinitions(
            SlotTable slotTable,
            ImmutableArray<SsaValueName> initialValues,
            ImmutableArray<SsaBlock> blocks,
            IReadOnlyDictionary<SsaValueName, SsaUseDefLink>? useDefLinks)
        {
            var definitions = new List<SsaValueDefinition>();
            var seen = new HashSet<SsaValueName>();

            for (int i = 0; i < initialValues.Length; i++)
            {
                var name = initialValues[i];
                var info = slotTable.GetInfo(name.Slot);
                Add(new SsaValueDefinition(new SsaDescriptor(
                    name.Slot,
                    name.Version,
                    SsaDefinitionKind.Initial,
                    -1,
                    -1,
                    -1,
                    defNode: null,
                    info.Type,
                    info.StackKind)));
            }


            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    var info = slotTable.GetInfo(phi.Target.Slot);
                    Add(new SsaValueDefinition(new SsaDescriptor(
                        phi.Target.Slot,
                        phi.Target.Version,
                        SsaDefinitionKind.Phi,
                        block.Id,
                        -1,
                        -1,
                        defNode: null,
                        info.Type,
                        info.StackKind,
                        defBlock: block.CfgBlock,
                        phiDescriptor: phi)));
                }

                for (int i = 0; i < block.TreeList.Length; i++)
                    CollectTreeDefinition(block.TreeList[i].Tree, block, block.TreeList[i].StatementIndex);
            }

            definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            return definitions.ToImmutableArray();

            void CollectTreeDefinition(SsaTree tree, SsaBlock block, int statementIndex)
            {
                int blockId = block.Id;

                if (tree.StoreTarget.HasValue)
                {
                    var name = tree.StoreTarget.Value;
                    var info = slotTable.GetInfo(name.Slot);
                    int useDefSsaNum = SsaConfig.ReservedSsaNumber;
                    if (useDefLinks is not null && useDefLinks.TryGetValue(name, out var useDefLink))
                    {
                        if (!useDefLink.Definition.Equals(name))
                            throw new InvalidOperationException("SSA use-def link is keyed by the wrong definition: " + useDefLink + ".");
                        if (!useDefLink.Use.Slot.Equals(name.Slot))
                            throw new InvalidOperationException("SSA use-def link crosses base locals: " + useDefLink + ".");
                        useDefSsaNum = useDefLink.Use.Version;
                    }

                    Add(new SsaValueDefinition(new SsaDescriptor(
                        name.Slot,
                        name.Version,
                        SsaDefinitionKind.Store,
                        blockId,
                        statementIndex,
                        tree.Source.Id,
                        tree.Source,
                        info.Type,
                        info.StackKind,
                        useDefSsaNum,
                        block.CfgBlock)));
                }
            }

            void Add(SsaValueDefinition definition)
            {
                if (!seen.Add(definition.Name))
                    throw new InvalidOperationException($"Duplicate SSA definition {definition.Name}.");
                definitions.Add(definition);
            }
        }

        internal static ImmutableArray<SsaMemoryDefinition> BuildMemoryDefinitions(
            ImmutableArray<SsaMemoryValueName> initialMemoryValues,
            ImmutableArray<SsaBlock> blocks)
        {
            var definitions = new List<SsaMemoryDefinition>();
            var seen = new HashSet<SsaMemoryValueName>();

            for (int i = 0; i < initialMemoryValues.Length; i++)
            {
                var name = initialMemoryValues[i];
                Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                    name,
                    SsaMemoryDefinitionKind.Initial,
                    -1,
                    -1,
                    -1,
                    defNode: null)));
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.MemoryPhis.Length; p++)
                {
                    var phi = block.MemoryPhis[p];
                    Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                        phi.Target,
                        SsaMemoryDefinitionKind.Phi,
                        block.Id,
                        -1,
                        -1,
                        defNode: null,
                        defBlock: block.CfgBlock,
                        phi: phi)));
                }

                for (int i = 0; i < block.TreeList.Length; i++)
                    CollectTreeMemoryDefinitions(block.TreeList[i].Tree, block, block.TreeList[i].StatementIndex);
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int i = 0; i < block.MemoryOut.Length; i++)
                {
                    var name = block.MemoryOut[i];
                    if (!seen.Contains(name))
                    {
                        throw new InvalidOperationException(
                            "Block B" + block.Id.ToString() + " has outgoing memory SSA value " + name +
                            " that is not initial, phi, or real store definition.");
                    }
                }
            }

            definitions.Sort(static (a, b) => a.Name.CompareTo(b.Name));
            return definitions.ToImmutableArray();

            void CollectTreeMemoryDefinitions(SsaTree tree, SsaBlock block, int statementIndex)
            {
                for (int i = 0; i < tree.MemoryDefinitions.Length; i++)
                {
                    var name = tree.MemoryDefinitions[i];
                    Add(new SsaMemoryDefinition(new SsaMemoryDescriptor(
                        name,
                        SsaMemoryDefinitionKind.Store,
                        block.Id,
                        statementIndex,
                        tree.Source.Id,
                        tree.Source,
                        block.CfgBlock)));
                }
            }

            void Add(SsaMemoryDefinition definition)
            {
                if (!seen.Add(definition.Name))
                    throw new InvalidOperationException($"Duplicate memory SSA definition {definition.Name}.");
                definitions.Add(definition);
            }
        }

        internal static void AnnotateSsaUses(
            ImmutableArray<SsaValueDefinition> valueDefinitions,
            ImmutableArray<SsaMemoryDefinition> memoryDefinitions,
            ImmutableArray<SsaBlock> blocks)
        {
            var descriptors = new Dictionary<SsaValueName, SsaDescriptor>();
            for (int i = 0; i < valueDefinitions.Length; i++)
                descriptors[valueDefinitions[i].Name] = valueDefinitions[i].Descriptor;

            var memoryDescriptors = new Dictionary<SsaMemoryValueName, SsaMemoryDescriptor>();
            for (int i = 0; i < memoryDefinitions.Length; i++)
                memoryDescriptors[memoryDefinitions[i].Name] = memoryDefinitions[i].Descriptor;

            for (int i = 0; i < valueDefinitions.Length; i++)
            {
                var descriptor = valueDefinitions[i].Descriptor;
                if (!descriptor.HasUseDefSsaNum)
                    continue;

                var use = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
                if (descriptors.TryGetValue(use, out var useDescriptor))
                    useDescriptor.AddUse(descriptor.DefBlockId);
            }

            for (int b = 0; b < blocks.Length; b++)
            {
                var block = blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        if (descriptors.TryGetValue(phi.Inputs[i].Value, out var descriptor))
                            descriptor.AddPhiUse(block.Id);
                    }
                }

                for (int p = 0; p < block.MemoryPhis.Length; p++)
                {
                    var phi = block.MemoryPhis[p];
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        if (memoryDescriptors.TryGetValue(phi.Inputs[i].Value, out var descriptor))
                            descriptor.AddPhiUse(block.Id);
                    }
                }

                for (int i = 0; i < block.TreeList.Length; i++)
                    AnnotateTreeUses(block.TreeList[i].Tree, block.Id, descriptors, memoryDescriptors);
            }

            static void AnnotateTreeUses(
                SsaTree tree,
                int blockId,
                Dictionary<SsaValueName, SsaDescriptor> descriptors,
                Dictionary<SsaMemoryValueName, SsaMemoryDescriptor> memoryDescriptors)
            {
                if (tree.Value.HasValue && descriptors.TryGetValue(tree.Value.Value, out var valueDescriptor))
                    valueDescriptor.AddUse(blockId);

                if (tree.LocalFieldBaseValue.HasValue && descriptors.TryGetValue(tree.LocalFieldBaseValue.Value, out var baseDescriptor))
                    baseDescriptor.AddUse(blockId);

                for (int i = 0; i < tree.MemoryUses.Length; i++)
                {
                    if (memoryDescriptors.TryGetValue(tree.MemoryUses[i], out var memoryDescriptor))
                        memoryDescriptor.AddUse(blockId);
                }
            }
        }

        private static ImmutableArray<SsaLocalDescriptor> BuildSsaLocalDescriptors(
            SlotTable slotTable,
            ImmutableArray<SsaValueDefinition> valueDefinitions)
        {
            var descriptorsBySlot = new Dictionary<SsaSlot, List<SsaDescriptor>>();
            for (int i = 0; i < valueDefinitions.Length; i++)
            {
                var descriptor = valueDefinitions[i].Descriptor;
                if (!descriptorsBySlot.TryGetValue(descriptor.BaseLocal, out var list))
                {
                    list = new List<SsaDescriptor>();
                    descriptorsBySlot.Add(descriptor.BaseLocal, list);
                }
                list.Add(descriptor);
            }

            var result = ImmutableArray.CreateBuilder<SsaLocalDescriptor>(slotTable.AllSlots.Length);
            for (int i = 0; i < slotTable.AllSlots.Length; i++)
            {
                var info = slotTable.AllSlots[i];
                descriptorsBySlot.TryGetValue(info.Slot, out var list);
                var perSsaData = DensePerSsaData(info.Slot, list);
                GenLocalDescriptor? localDescriptor = slotTable.GetLocalDescriptorOrNull(info.Slot);
                var local = new SsaLocalDescriptor(
                    info.Slot,
                    info.Type,
                    info.StackKind,
                    info.AddressExposed,
                    slotTable.IsPromotable(info.Slot),
                    localDescriptor,
                    perSsaData);
                result.Add(local);
                localDescriptor?.SetSsaDescriptors(perSsaData);
            }

            return result.ToImmutable();

            static ImmutableArray<SsaDescriptor> DensePerSsaData(SsaSlot slot, List<SsaDescriptor>? descriptors)
            {
                if (descriptors is null || descriptors.Count == 0)
                    return ImmutableArray<SsaDescriptor>.Empty;

                int max = -1;
                for (int i = 0; i < descriptors.Count; i++)
                    max = Math.Max(max, descriptors[i].SsaNumber);

                var table = new SsaDescriptor?[max + 1];
                for (int i = 0; i < descriptors.Count; i++)
                {
                    var descriptor = descriptors[i];
                    if (table[descriptor.SsaNumber] is not null)
                        throw new InvalidOperationException("Duplicate SSA descriptor " + descriptor.Name + ".");
                    table[descriptor.SsaNumber] = descriptor;
                }

                var builder = ImmutableArray.CreateBuilder<SsaDescriptor>(table.Length);
                for (int i = 0; i < table.Length; i++)
                {
                    if (i == SsaConfig.ReservedSsaNumber)
                    {
                        builder.Add(null!);
                        continue;
                    }

                    if (table[i] is null)
                        throw new InvalidOperationException("Missing SSA descriptor " + slot + "_" + i.ToString() + ".");
                    builder.Add(table[i]!);
                }
                return builder.ToImmutable();
            }
        }

        private static MutablePhi[][] InsertPhis(ControlFlowGraph cfg, ImmutableArray<SsaSlot> promotableSlots, Liveness liveness)
        {
            int n = cfg.Blocks.Length;
            var byBlock = new List<MutablePhi>[n];
            for (int i = 0; i < n; i++)
                byBlock[i] = new List<MutablePhi>();

            var hasPhi = new HashSet<(SsaSlot slot, int blockId)>();

            for (int s = 0; s < promotableSlots.Length; s++)
            {
                var slot = promotableSlots[s];
                var work = new Queue<int>();
                var inWork = new HashSet<int>();

                for (int b = 0; b < n; b++)
                {
                    if (liveness.Defs[b].Contains(slot))
                    {
                        work.Enqueue(b);
                        inWork.Add(b);
                    }
                }

                ProcessWorklist();

                for (int r = 0; r < cfg.ExceptionRegions.Length; r++)
                {
                    var region = cfg.ExceptionRegions[r];
                    int handler = region.HandlerStartBlockId;
                    if ((uint)handler >= (uint)n)
                        continue;

                    if (!HasPotentialExceptionEdgeToHandler(cfg, region))
                        continue;

                    if (liveness.LiveIn[handler].Contains(slot) && AddPhi(slot, handler) && !liveness.Defs[handler].Contains(slot) && inWork.Add(handler))
                        work.Enqueue(handler);
                }

                ProcessWorklist();

                void ProcessWorklist()
                {
                    while (work.Count != 0)
                    {
                        int x = work.Dequeue();
                        inWork.Remove(x);

                        var df = cfg.DominanceFrontiers[x];
                        for (int i = 0; i < df.Length; i++)
                        {
                            int y = df[i];
                            if (!liveness.LiveIn[y].Contains(slot))
                                continue;

                            if (AddPhi(slot, y) && !liveness.Defs[y].Contains(slot) && inWork.Add(y))
                                work.Enqueue(y);
                        }
                    }
                }
            }

            var result = new MutablePhi[n][];
            for (int i = 0; i < n; i++)
            {
                byBlock[i].Sort((a, b) => a.Slot.CompareTo(b.Slot));
                result[i] = byBlock[i].ToArray();
            }
            return result;

            bool AddPhi(SsaSlot slot, int blockId)
            {
                if (!hasPhi.Add((slot, blockId)))
                    return false;

                byBlock[blockId].Add(new MutablePhi(blockId, slot));
                return true;
            }
        }

        private static MutableMemoryPhi[][] InsertMemoryPhis(ControlFlowGraph cfg, MemoryLiveness liveness)
        {
            int n = cfg.Blocks.Length;
            var byBlock = new List<MutableMemoryPhi>[n];
            for (int i = 0; i < n; i++)
                byBlock[i] = new List<MutableMemoryPhi>();

            var hasPhi = new HashSet<(SsaMemoryKind kind, int blockId)>();

            foreach (var kind in SsaMemoryKinds.All)
            {
                var work = new Queue<int>();
                var inWork = new HashSet<int>();

                for (int b = 0; b < n; b++)
                {
                    if (liveness.Defs[b].Contains(kind))
                    {
                        work.Enqueue(b);
                        inWork.Add(b);
                    }
                }

                ProcessWorklist();

                for (int r = 0; r < cfg.ExceptionRegions.Length; r++)
                {
                    var region = cfg.ExceptionRegions[r];
                    int handler = region.HandlerStartBlockId;
                    if ((uint)handler >= (uint)n)
                        continue;

                    if (!HasPotentialExceptionEdgeToHandler(cfg, region))
                        continue;

                    if (liveness.LiveIn[handler].Contains(kind) && AddPhi(kind, handler) && !liveness.Defs[handler].Contains(kind) && inWork.Add(handler))
                        work.Enqueue(handler);
                }

                ProcessWorklist();

                void ProcessWorklist()
                {
                    while (work.Count != 0)
                    {
                        int x = work.Dequeue();
                        inWork.Remove(x);

                        var df = cfg.DominanceFrontiers[x];
                        for (int i = 0; i < df.Length; i++)
                        {
                            int y = df[i];
                            if (!liveness.LiveIn[y].Contains(kind))
                                continue;

                            if (AddPhi(kind, y) && !liveness.Defs[y].Contains(kind) && inWork.Add(y))
                                work.Enqueue(y);
                        }
                    }
                }
            }

            var result = new MutableMemoryPhi[n][];
            for (int i = 0; i < n; i++)
            {
                byBlock[i].Sort((a, b) => a.Kind.CompareTo(b.Kind));
                result[i] = byBlock[i].ToArray();
            }
            return result;

            bool AddPhi(SsaMemoryKind kind, int blockId)
            {
                if (!hasPhi.Add((kind, blockId)))
                    return false;

                byBlock[blockId].Add(new MutableMemoryPhi(blockId, kind));
                return true;
            }
        }


        private static bool HasPotentialExceptionEdgeToHandler(ControlFlowGraph cfg, CfgExceptionRegion region)
            => EhFuncletLayout.HasPotentialExceptionEdgeToHandler(cfg, region);

        private sealed class SlotTable
        {
            private const int MaxTrackedPromotableSlots = 512;

            private readonly Dictionary<SsaSlot, SsaSlotInfo> _infoBySlot;
            private readonly Dictionary<SsaSlot, GenLocalDescriptor> _localDescriptorBySlot;
            private readonly HashSet<SsaSlot> _promotable;

            public ImmutableArray<SsaSlotInfo> AllSlots { get; }
            public ImmutableArray<SsaSlot> PromotableSlots { get; }

            private SlotTable(
                ImmutableArray<SsaSlotInfo> allSlots,
                ImmutableArray<SsaSlot> promotableSlots,
                Dictionary<SsaSlot, GenLocalDescriptor> localDescriptorBySlot)
            {
                AllSlots = allSlots;
                PromotableSlots = promotableSlots;
                _infoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>();
                _localDescriptorBySlot = new Dictionary<SsaSlot, GenLocalDescriptor>(localDescriptorBySlot);
                _promotable = new HashSet<SsaSlot>();

                for (int i = 0; i < allSlots.Length; i++)
                    _infoBySlot[allSlots[i].Slot] = allSlots[i];
                for (int i = 0; i < promotableSlots.Length; i++)
                    _promotable.Add(promotableSlots[i]);
            }

            public bool IsPromotable(SsaSlot slot) => _promotable.Contains(slot);

            public SsaSlotInfo GetInfo(SsaSlot slot)
            {
                if (_infoBySlot.TryGetValue(slot, out var info))
                    return info;

                return new SsaSlotInfo(slot, type: null, stackKind: GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
            }

            public GenLocalDescriptor? GetLocalDescriptorOrNull(SsaSlot slot)
            {
                _localDescriptorBySlot.TryGetValue(slot, out var descriptor);
                return descriptor;
            }

            public static SlotTable Build(GenTreeMethod method, ControlFlowGraph cfg, GenTreeLocalLiveness hirLiveness)
            {
                if (method is null) throw new ArgumentNullException(nameof(method));
                if (cfg is null) throw new ArgumentNullException(nameof(cfg));
                if (hirLiveness is null) throw new ArgumentNullException(nameof(hirLiveness));
                if (!ReferenceEquals(hirLiveness.Cfg, cfg))
                    throw new InvalidOperationException("SSA slot table requires HIR liveness for the same CFG.");

                var slots = new List<SsaSlotInfo>();
                var promotable = new List<SsaSlot>();
                var descriptorBySlot = new Dictionary<SsaSlot, GenLocalDescriptor>();

                for (int i = 0; i < method.ArgDescriptors.Length; i++)
                {
                    var descriptor = method.ArgDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                for (int i = 0; i < method.LocalDescriptors.Length; i++)
                {
                    var descriptor = method.LocalDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                for (int i = 0; i < method.TempDescriptors.Length; i++)
                {
                    var descriptor = method.TempDescriptors[i];
                    AddSlot(new SsaSlot(descriptor), descriptor.Type, descriptor.StackKind, descriptor);
                }

                slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                promotable.Sort();
                VerifyTrackedSplit(hirLiveness.SsaCandidateSlots, promotable);

                return new SlotTable(slots.ToImmutableArray(), promotable.ToImmutableArray(), descriptorBySlot);

                void AddSlot(SsaSlot slot, RuntimeType? type, GenStackKind stackKind, GenLocalDescriptor descriptor)
                {
                    slots.Add(new SsaSlotInfo(
                        slot,
                        type,
                        stackKind,
                        descriptor.AddressExposed,
                        descriptor.MemoryAliased,
                        descriptor.Category,
                        descriptor.LclNum,
                        descriptor.VarIndex,
                        descriptor.Tracked,
                        descriptor.SsaPromoted,
                        descriptor));
                    descriptorBySlot[slot] = descriptor;

                    if (!descriptor.SsaPromoted)
                        return;

                    if (!descriptor.CanBeSsaRenamedAsScalar)
                        throw new InvalidOperationException("LclVarDsc marked non-scalar or memory-aliased local as SSA-renamable: " + descriptor + ".");

                    if (!IsPromotableStorageSlot(type, stackKind))
                        throw new InvalidOperationException("tracked SSA local has non-promotable storage kind: " + slot + ".");

                    promotable.Add(slot);
                }

                static void VerifyTrackedSplit(ImmutableArray<SsaSlot> ssaCandidateSlots, List<SsaSlot> ssaSlots)
                {
                    if (ssaCandidateSlots.Length != ssaSlots.Count)
                        throw new InvalidOperationException("SSA local set does not match HIR tracked-local SSA candidate set.");

                    var sortedCandidates = new List<SsaSlot>(ssaCandidateSlots);
                    sortedCandidates.Sort();
                    for (int i = 0; i < sortedCandidates.Count; i++)
                    {
                        if (!sortedCandidates[i].Equals(ssaSlots[i]))
                            throw new InvalidOperationException("SSA local set does not match HIR tracked-local SSA candidate set.");
                    }
                }

                static bool IsPromotableStorageSlot(RuntimeType? type, GenStackKind stackKind)
                {
                    if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                        return false;

                    if (stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                        return true;

                    if (type is not null)
                    {
                        if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                            return true;

                        if (type.IsValueType && type.ContainsGcPointers)
                            return false;
                    }

                    if (type is null)
                        return stackKind is
                            GenStackKind.I4 or
                            GenStackKind.I8 or
                            GenStackKind.R4 or
                            GenStackKind.R8 or
                            GenStackKind.NativeInt or
                            GenStackKind.NativeUInt or
                            GenStackKind.Ptr;

                    return MachineAbi.IsPhysicallyPromotableStorage(type, stackKind);
                }
            }
            private static int LoopDepth(ControlFlowGraph cfg, int blockId)
            {
                int depth = 0;
                for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                {
                    if (cfg.NaturalLoops[i].Contains(blockId))
                        depth++;
                }
                return depth;
            }

            private static void CountSlotUses(GenTree node, Dictionary<SsaSlot, int> counts, int weight)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    if (fieldAccess.Kind != SsaLocalAccessKind.FullDefinition)
                        Add(fieldAccess.Slot);
                    if (fieldAccess.IsDefinition)
                        Add(fieldAccess.Slot);

                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        CountSlotUses(node.Operands[i], counts, weight);
                    }
                    return;
                }

                if (TryGetDirectLoadSlot(node, out var loadSlot) || TryGetDirectStoreSlot(node, out loadSlot))
                    Add(loadSlot);

                void Add(SsaSlot slot)
                {
                    counts.TryGetValue(slot, out int current);
                    counts[slot] = current + Math.Max(1, weight);
                }
            }

            private static void CollectAddressExposed(GenTree node, GenTree? parent, int operandIndex, HashSet<SsaSlot> addressExposed)
            {
                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot) &&
                    (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
                    addressExposed.Add(slot);

                for (int i = 0; i < node.Operands.Length; i++)
                    CollectAddressExposed(node.Operands[i], node, i, addressExposed);
            }
        }

        private sealed class Liveness
        {
            public TrackedLocalTable Table { get; }
            public TrackedLocalSet[] Uses { get; }
            public TrackedLocalSet[] Defs { get; }
            public TrackedLocalSet[] LiveIn { get; }
            public TrackedLocalSet[] LiveOut { get; }

            private Liveness(TrackedLocalTable table, TrackedLocalSet[] uses, TrackedLocalSet[] defs, TrackedLocalSet[] liveIn, TrackedLocalSet[] liveOut)
            {
                Table = table;
                Uses = uses;
                Defs = defs;
                LiveIn = liveIn;
                LiveOut = liveOut;
            }

            public static Liveness FromHirLiveness(
                ControlFlowGraph cfg,
                ImmutableArray<SsaSlot> promotableSlots,
                GenTreeLocalLiveness hirLiveness)
            {
                if (cfg is null)
                    throw new ArgumentNullException(nameof(cfg));
                if (hirLiveness is null)
                    throw new ArgumentNullException(nameof(hirLiveness));
                if (!ReferenceEquals(hirLiveness.Cfg, cfg))
                    throw new InvalidOperationException("SSA liveness projection requires HIR liveness for the same CFG.");

                int n = cfg.Blocks.Length;
                var table = new TrackedLocalTable(promotableSlots);

                var uses = ProjectSets(hirLiveness.UseBits, table, promotableSlots, n);
                var defs = ProjectSets(hirLiveness.DefBits, table, promotableSlots, n);
                var liveIn = ProjectSets(hirLiveness.LiveInBits, table, promotableSlots, n);
                var liveOut = ProjectSets(hirLiveness.LiveOutBits, table, promotableSlots, n);

                return new Liveness(table, uses, defs, liveIn, liveOut);
            }
            private static TrackedLocalSet[] ProjectSets(
                ImmutableArray<TrackedLocalSet> source,
                TrackedLocalTable targetTable,
                ImmutableArray<SsaSlot> slots,
                int blockCount)
            {
                if (source.Length != blockCount)
                    throw new InvalidOperationException("HIR liveness block count does not match CFG block count.");

                var result = new TrackedLocalSet[blockCount];

                for (int b = 0; b < blockCount; b++)
                {
                    var set = targetTable.NewEmptySet();
                    var sourceSet = source[b];

                    for (int i = 0; i < slots.Length; i++)
                    {
                        var slot = slots[i];
                        if (sourceSet.Contains(slot))
                            set.Add(slot);
                    }

                    result[b] = set;
                }

                return result;
            }
        }

        private sealed class MemoryLiveness
        {
            public SsaMemoryKindSet[] Uses { get; }
            public SsaMemoryKindSet[] Defs { get; }
            public SsaMemoryKindSet[] LiveIn { get; }
            public SsaMemoryKindSet[] LiveOut { get; }

            private MemoryLiveness(SsaMemoryKindSet[] uses, SsaMemoryKindSet[] defs, SsaMemoryKindSet[] liveIn, SsaMemoryKindSet[] liveOut)
            {
                Uses = uses;
                Defs = defs;
                LiveIn = liveIn;
                LiveOut = liveOut;
            }

            public static MemoryLiveness Build(ControlFlowGraph cfg, SlotTable slots)
            {
                int n = cfg.Blocks.Length;
                var uses = new SsaMemoryKindSet[n];
                var defs = new SsaMemoryKindSet[n];
                var liveIn = new SsaMemoryKindSet[n];
                var liveOut = new SsaMemoryKindSet[n];

                for (int b = 0; b < n; b++)
                {
                    CollectMemoryUseDef(cfg.Blocks[b].SourceBlock.LinearNodes, slots, ref uses[b], ref defs[b]);
                }

                bool changed;
                do
                {
                    changed = false;
                    for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                    {
                        int b = cfg.ReversePostOrder[r];

                        SsaMemoryKindSet newOut = SsaMemoryKindSet.None;
                        var successors = cfg.Blocks[b].Successors;
                        for (int i = 0; i < successors.Length; i++)
                            newOut |= liveIn[successors[i].ToBlockId];

                        SsaMemoryKindSet newIn = uses[b] | (newOut & ~defs[b]);

                        if (liveOut[b] != newOut)
                        {
                            liveOut[b] = newOut;
                            changed = true;
                        }

                        if (liveIn[b] != newIn)
                        {
                            liveIn[b] = newIn;
                            changed = true;
                        }
                    }
                }
                while (changed);

                return new MemoryLiveness(uses, defs, liveIn, liveOut);
            }
        }

        private readonly struct MemoryEffects
        {
            public readonly SsaMemoryKindSet Uses;
            public readonly SsaMemoryKindSet Definitions;

            public MemoryEffects(SsaMemoryKindSet uses, SsaMemoryKindSet definitions)
            {
                Uses = uses;
                Definitions = definitions;
            }
        }

        private static void CollectMemoryUseDef(ImmutableArray<GenTree> treeList, SlotTable slots, ref SsaMemoryKindSet uses, ref SsaMemoryKindSet defs)
        {
            for (int i = 0; i < treeList.Length; i++)
            {
                var effects = GetMemoryEffects(treeList[i], slots);
                SsaMemoryKindSet nodeUses = effects.Uses | effects.Definitions;
                uses |= nodeUses & ~defs;
                defs |= effects.Definitions;
            }
        }

        private static MemoryEffects GetMemoryEffects(GenTree node, SlotTable slots)
        {
            SsaMemoryKindSet uses = SsaMemoryKindSet.None;
            SsaMemoryKindSet defs = SsaMemoryKindSet.None;

            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localFieldAccess))
            {
                var fieldSlotInfo = slots.GetInfo(localFieldAccess.Slot);
                var baseSlotInfo = slots.GetInfo(localFieldAccess.BaseSlot);
                bool fieldIsByrefExposed = fieldSlotInfo.AddressExposed || fieldSlotInfo.MemoryAliased || baseSlotInfo.AddressExposed || baseSlotInfo.MemoryAliased;

                if (localFieldAccess.Kind == SsaLocalAccessKind.Use && !_slotsArePromotable(slots, localFieldAccess))
                {
                    uses = uses.Add(SsaMemoryKind.ByrefExposed);
                }
                else if (localFieldAccess.IsDefinition && !_slotsArePromotable(slots, localFieldAccess))
                {
                    defs = defs.Add(SsaMemoryKind.ByrefExposed);
                }
            }

            switch (node.Kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
                    {
                        var info = slots.GetInfo(loadSlot);
                        if (!slots.IsPromotable(loadSlot))
                            uses = uses.Add(SsaMemoryKind.ByrefExposed);
                    }
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
                    {
                        var info = slots.GetInfo(storeSlot);
                        if (!slots.IsPromotable(storeSlot))
                            defs = defs.Add(SsaMemoryKind.ByrefExposed);
                    }
                    break;

                case GenTreeKind.Field:
                    if (!SsaSlotHelpers.TryGetLocalFieldAccess(node, out _))
                        uses = uses.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StaticField:
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayDataRef:
                    uses = uses.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.LoadIndirect:
                    uses |= IndirectMemoryKinds(node);
                    break;

                case GenTreeKind.StoreField:
                    if (!SsaSlotHelpers.TryGetLocalFieldAccess(node, out _))
                        defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StoreStaticField:
                case GenTreeKind.StoreArrayElement:
                    defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;

                case GenTreeKind.StoreIndirect:
                    defs |= IndirectMemoryKinds(node);
                    break;

                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    uses = SsaMemoryKindSet.All;
                    defs = SsaMemoryKindSet.All;
                    break;

                case GenTreeKind.NewObject:
                case GenTreeKind.NewDelegate:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    defs = defs.Add(SsaMemoryKind.GcHeap);
                    break;
            }

            if (node.WritesMemory && defs == SsaMemoryKindSet.None)
                defs = defs.Add(SsaMemoryKind.GcHeap);

            if (node.ReadsMemory && uses == SsaMemoryKindSet.None)
                uses = uses.Add(SsaMemoryKind.GcHeap);

            return new MemoryEffects(uses, defs);

            static bool _slotsArePromotable(SlotTable slots, SsaLocalAccess access)
                => access.IsPromotedFieldAccess
                    ? slots.IsPromotable(access.Slot)
                    : slots.IsPromotable(access.BaseSlot);

            static SsaMemoryKindSet IndirectMemoryKinds(GenTree node)
            {
                if (node.Operands.Length != 0 && IsLocalOrByrefAddress(node.Operands[0]))
                    return SsaMemoryKindSet.ByrefExposed;

                return SsaMemoryKindSet.All;
            }

            static bool IsLocalOrByrefAddress(GenTree node)
            {
                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _))
                    return true;

                if (node.Kind == GenTreeKind.PointerToByRef)
                    return true;

                if (node.Kind == GenTreeKind.FieldAddr && node.Operands.Length != 0)
                    return IsLocalOrByrefAddress(node.Operands[0]);

                if (node.Kind == GenTreeKind.ArrayElementAddr || node.Kind == GenTreeKind.ArrayDataRef)
                    return false;

                return false;
            }
        }

        private sealed class RenameState
        {
            private readonly ControlFlowGraph _cfg;
            private readonly SlotTable _slots;
            private readonly MutablePhi[][] _phis;
            private readonly MutableMemoryPhi[][] _memoryPhis;
            private readonly Liveness _localLiveness;
            private readonly MemoryLiveness _memoryLiveness;
            private readonly Dictionary<SsaSlot, int> _nextVersion = new();
            private readonly Dictionary<SsaSlot, Stack<SsaValueName>> _stacks = new();
            private readonly Dictionary<SsaMemoryKind, int> _nextMemoryVersion = new();
            private readonly Dictionary<SsaMemoryKind, Stack<SsaMemoryValueName>> _memoryStacks = new();
            private readonly List<SsaValueName> _initialValues = new();
            private readonly List<SsaMemoryValueName> _initialMemoryValues = new();
            private readonly Dictionary<SsaValueName, SsaUseDefLink> _useDefLinks = new();
            private readonly List<SsaTree>[] _renamedStatements;
            private readonly List<ImmutableArray<SsaTree>>[] _renamedStatementTreeLists;
            private readonly ImmutableArray<SsaMemoryValueName>[] _memoryIn;
            private readonly ImmutableArray<SsaMemoryValueName>[] _memoryOut;
            private readonly bool[] _visited;

            public IReadOnlyList<SsaValueName> InitialValues => _initialValues;
            public IReadOnlyList<SsaMemoryValueName> InitialMemoryValues => _initialMemoryValues;
            public IReadOnlyDictionary<SsaValueName, SsaUseDefLink> UseDefLinks => _useDefLinks;

            public RenameState(ControlFlowGraph cfg, SlotTable slots, MutablePhi[][] phis, MutableMemoryPhi[][] memoryPhis, Liveness localLiveness, MemoryLiveness memoryLiveness)
            {
                _cfg = cfg;
                _slots = slots;
                _phis = phis;
                _memoryPhis = memoryPhis;
                _localLiveness = localLiveness;
                _memoryLiveness = memoryLiveness;
                _renamedStatements = new List<SsaTree>[cfg.Blocks.Length];
                _renamedStatementTreeLists = new List<ImmutableArray<SsaTree>>[cfg.Blocks.Length];
                _memoryIn = new ImmutableArray<SsaMemoryValueName>[cfg.Blocks.Length];
                _memoryOut = new ImmutableArray<SsaMemoryValueName>[cfg.Blocks.Length];
                _visited = new bool[cfg.Blocks.Length];
                for (int i = 0; i < _renamedStatements.Length; i++)
                {
                    _renamedStatements[i] = new List<SsaTree>();
                    _renamedStatementTreeLists[i] = new List<ImmutableArray<SsaTree>>();
                }

                for (int i = 0; i < slots.PromotableSlots.Length; i++)
                {
                    var slot = slots.PromotableSlots[i];
                    var initial = new SsaValueName(slot, SsaConfig.FirstSsaNumber);
                    _nextVersion[slot] = SsaConfig.FirstSsaNumber;
                    _stacks[slot] = new Stack<SsaValueName>();
                    _stacks[slot].Push(initial);
                    _initialValues.Add(initial);
                }

                foreach (var kind in SsaMemoryKinds.All)
                {
                    var initial = new SsaMemoryValueName(kind, SsaConfig.FirstSsaNumber);
                    _nextMemoryVersion[kind] = SsaConfig.FirstSsaNumber;
                    _memoryStacks[kind] = new Stack<SsaMemoryValueName>();
                    _memoryStacks[kind].Push(initial);
                    _initialMemoryValues.Add(initial);
                }

                _initialValues.Sort();
                _initialMemoryValues.Sort();
            }

            public ImmutableArray<SsaBlock> Run()
            {
                for (int i = 0; i < _cfg.DominatorRoots.Length; i++)
                    RenameBlock(_cfg.DominatorRoots[i]);

                for (int i = 0; i < _cfg.Blocks.Length; i++)
                {
                    if (!_visited[i])
                        RenameBlock(i);
                }

                _initialValues.Sort();
                _initialMemoryValues.Sort();

                var blocks = ImmutableArray.CreateBuilder<SsaBlock>(_cfg.Blocks.Length);
                for (int b = 0; b < _cfg.Blocks.Length; b++)
                {
                    var phiBuilder = ImmutableArray.CreateBuilder<SsaPhi>(_phis[b].Length);
                    for (int i = 0; i < _phis[b].Length; i++)
                        phiBuilder.Add(_phis[b][i].Freeze(_cfg.Blocks[b]));

                    var memoryPhiBuilder = ImmutableArray.CreateBuilder<SsaMemoryPhi>(_memoryPhis[b].Length);
                    for (int i = 0; i < _memoryPhis[b].Length; i++)
                        memoryPhiBuilder.Add(_memoryPhis[b][i].Freeze(_cfg.Blocks[b]));

                    blocks.Add(new SsaBlock(
                        _cfg.Blocks[b],
                        phiBuilder.ToImmutable(),
                        _renamedStatements[b].ToImmutableArray(),
                        memoryPhiBuilder.ToImmutable(),
                        _memoryIn[b],
                        _memoryOut[b],
                        statementTreeLists: _renamedStatementTreeLists[b].ToImmutableArray()));
                }
                return blocks.ToImmutable();
            }

            private void RenameBlock(int blockId)
            {
                if (_visited[blockId])
                    return;
                _visited[blockId] = true;

                var pushed = new List<SsaSlot>();
                var pushedMemory = new List<SsaMemoryKind>();

                RenameIncomingMemoryStates(blockId, pushedMemory);

                for (int i = 0; i < _phis[blockId].Length; i++)
                {
                    var phi = _phis[blockId][i];
                    phi.Target = NewVersion(phi.Slot);
                    Push(phi.Slot, phi.Target);
                    pushed.Add(phi.Slot);
                }

                var sourceBlock = _cfg.Blocks[blockId].SourceBlock;
                for (int i = 0; i < sourceBlock.Statements.Length; i++)
                {
                    var renamed = RenameStatement(sourceBlock.Statements[i], sourceBlock.StatementTreeLists[i], blockId, pushed, pushedMemory);
                    _renamedStatements[blockId].Add(renamed.Root);
                    _renamedStatementTreeLists[blockId].Add(renamed.TreeList);
                }

                RenameOutgoingMemoryStates(blockId, pushedMemory);

                var successors = _cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    var edge = successors[i];
                    int succ = edge.ToBlockId;
                    AddPhiArgsToSuccessor(blockId, succ);

                    if (edge.Kind != CfgEdgeKind.Exception)
                        AddPhiArgsToNewlyEnteredHandlers(blockId, succ);
                }

                var children = _cfg.DominatorTreeChildren[blockId];
                for (int i = 0; i < children.Length; i++)
                    RenameBlock(children[i]);

                for (int i = pushed.Count - 1; i >= 0; i--)
                    Pop(pushed[i]);

                for (int i = pushedMemory.Count - 1; i >= 0; i--)
                    PopMemory(pushedMemory[i]);
            }

            private void RenameIncomingMemoryStates(int blockId, List<SsaMemoryKind> pushedMemory)
            {
                var builder = ImmutableArray.CreateBuilder<SsaMemoryValueName>(SsaMemoryKinds.All.Length);
                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (TryGetMemoryPhi(blockId, kind, out var phi))
                    {
                        phi.Target = NewMemoryVersion(kind);
                        PushMemory(kind, phi.Target);
                        pushedMemory.Add(kind);
                        builder.Add(phi.Target);
                    }
                    else
                    {
                        builder.Add(TopMemory(kind));
                    }
                }

                _memoryIn[blockId] = builder.ToImmutable();
            }

            private void RenameOutgoingMemoryStates(int blockId, List<SsaMemoryKind> pushedMemory)
            {
                var builder = ImmutableArray.CreateBuilder<SsaMemoryValueName>(SsaMemoryKinds.All.Length);
                foreach (var kind in SsaMemoryKinds.All)
                    builder.Add(TopMemory(kind));

                _memoryOut[blockId] = builder.ToImmutable();
            }

            private bool TryGetMemoryPhi(int blockId, SsaMemoryKind kind, out MutableMemoryPhi phi)
            {
                for (int i = 0; i < _memoryPhis[blockId].Length; i++)
                {
                    if (_memoryPhis[blockId][i].Kind == kind)
                    {
                        phi = _memoryPhis[blockId][i];
                        return true;
                    }
                }

                phi = null!;
                return false;
            }

            private void AddPhiArgsToSuccessor(int predBlockId, int succBlockId)
            {
                bool handlerEntry = _cfg.Blocks[succBlockId].IsHandlerEntry;

                for (int p = 0; p < _phis[succBlockId].Length; p++)
                {
                    var phi = _phis[succBlockId][p];
                    phi.AddInput(predBlockId, Top(phi.Slot), allowMultipleForPred: handlerEntry);
                }

                for (int p = 0; p < _memoryPhis[succBlockId].Length; p++)
                {
                    var phi = _memoryPhis[succBlockId][p];
                    phi.AddInput(predBlockId, TopMemory(phi.Kind), allowMultipleForPred: handlerEntry);
                }
            }

            private void AddDefToEHSuccessorPhis(int blockId, SsaSlot slot, SsaValueName value)
            {
                if (!_localLiveness.Table.Contains(slot) || !HasPotentialEHSuccs(blockId))
                    return;

                VisitEHSuccs(blockId, succBlockId =>
                {
                    if (!_localLiveness.LiveIn[succBlockId].Contains(slot))
                        return;

                    if (TryGetPhi(succBlockId, slot, out var phi))
                        phi.AddInput(blockId, value, allowMultipleForPred: true);
                });
            }

            private void AddMemoryDefToEHSuccessorPhis(int blockId, SsaMemoryKind kind, SsaMemoryValueName value)
            {
                if (!HasPotentialEHSuccs(blockId))
                    return;

                VisitEHSuccs(blockId, succBlockId =>
                {
                    if (!_memoryLiveness.LiveIn[succBlockId].Contains(kind))
                        return;

                    if (TryGetMemoryPhi(succBlockId, kind, out var phi))
                        phi.AddInput(blockId, value, allowMultipleForPred: true);
                });
            }

            private void AddPhiArgsToNewlyEnteredHandlers(int predBlockId, int enterBlockId)
            {
                for (int r = 0; r < _cfg.ExceptionRegions.Length; r++)
                {
                    var region = _cfg.ExceptionRegions[r];
                    if (region.TryStartBlockId != enterBlockId)
                        continue;

                    if (region.ContainsTryBlock(predBlockId))
                        continue;

                    int handler = region.HandlerStartBlockId;
                    if ((uint)handler >= (uint)_cfg.Blocks.Length)
                        continue;

                    for (int p = 0; p < _phis[handler].Length; p++)
                    {
                        var phi = _phis[handler][p];
                        if (_localLiveness.LiveOut[predBlockId].Contains(phi.Slot))
                            phi.AddInput(enterBlockId, Top(phi.Slot), allowMultipleForPred: true);
                    }

                    for (int p = 0; p < _memoryPhis[handler].Length; p++)
                    {
                        var phi = _memoryPhis[handler][p];
                        if (_memoryLiveness.LiveOut[predBlockId].Contains(phi.Kind))
                            phi.AddInput(enterBlockId, _memoryOut[predBlockId].IsDefaultOrEmpty ? TopMemory(phi.Kind) : GetMemoryOut(predBlockId, phi.Kind), allowMultipleForPred: true);
                    }
                }
            }

            private SsaMemoryValueName GetMemoryOut(int blockId, SsaMemoryKind kind)
            {
                var values = _memoryOut[blockId];
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i].Kind == kind)
                        return values[i];
                }

                return TopMemory(kind);
            }

            private bool TryGetPhi(int blockId, SsaSlot slot, out MutablePhi phi)
            {
                for (int i = 0; i < _phis[blockId].Length; i++)
                {
                    if (_phis[blockId][i].Slot.Equals(slot))
                    {
                        phi = _phis[blockId][i];
                        return true;
                    }
                }

                phi = null!;
                return false;
            }

            private bool HasPotentialEHSuccs(int blockId)
            {
                if ((uint)blockId >= (uint)_cfg.Blocks.Length)
                    return false;

                var block = _cfg.Blocks[blockId];
                return block.TryRegionIndexes.Length != 0 && ControlFlowGraph.BlockMayThrow(block.SourceBlock);
            }

            private void VisitEHSuccs(int blockId, Action<int> visitor)
            {
                var seen = new HashSet<int>();
                var tryRegions = _cfg.Blocks[blockId].TryRegionIndexes;
                for (int i = 0; i < tryRegions.Length; i++)
                {
                    int regionIndex = tryRegions[i];
                    if ((uint)regionIndex >= (uint)_cfg.ExceptionRegions.Length)
                        continue;

                    int handler = _cfg.ExceptionRegions[regionIndex].HandlerStartBlockId;
                    if ((uint)handler < (uint)_cfg.Blocks.Length && seen.Add(handler))
                        visitor(handler);
                }
            }

            private (SsaTree Root, ImmutableArray<SsaTree> TreeList) RenameStatement(GenTree root, ImmutableArray<GenTree> treeList, int blockId, List<SsaSlot> pushed, List<SsaMemoryKind> pushedMemory)
            {
                if (treeList.IsDefaultOrEmpty)
                    throw new InvalidOperationException("Statement root has no HIR tree-list node " + root.Id.ToString() + ".");

                var renamedByNode = new Dictionary<GenTree, SsaTree>(ReferenceEqualityComparer<GenTree>.Instance);
                var renamedTreeList = ImmutableArray.CreateBuilder<SsaTree>(treeList.Length);
                for (int i = 0; i < treeList.Length; i++)
                {
                    var node = treeList[i];
                    var renamed = RenameLinearNode(node, renamedByNode, blockId, pushed, pushedMemory);
                    renamedByNode[node] = renamed;
                    renamedTreeList.Add(renamed);
                }

                if (!renamedByNode.TryGetValue(root, out var result))
                    throw new InvalidOperationException("Statement root was not present in HIR tree-list node " + root.Id.ToString() + ".");

                var resultTreeList = renamedTreeList.ToImmutable();
                if (!ReferenceEquals(resultTreeList[resultTreeList.Length - 1], result))
                    throw new InvalidOperationException("Renamed SSA statement tree-list root is not last for HIR root " + root.Id.ToString() + ".");

                return (result, resultTreeList);
            }

            private SsaTree RenameLinearNode(GenTree node, Dictionary<GenTree, SsaTree> renamedByNode, int blockId, List<SsaSlot> pushed, List<SsaMemoryKind> pushedMemory)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                    {
                        if (fieldAccess.IsPromotedFieldAccess && _slots.IsPromotable(fieldAccess.Slot))
                        {
                            var operands = GetRenamedNonReceiverOperands(node, fieldAccess, renamedByNode);
                            return AttachMemory(node, operands, blockId, pushedMemory, value: Top(fieldAccess.Slot));
                        }

                        if (!fieldAccess.IsPromotedFieldAccess && fieldAccess.Field is not null && _slots.IsPromotable(fieldAccess.BaseSlot))
                        {
                            var operands = GetRenamedNonReceiverOperands(node, fieldAccess, renamedByNode);
                            return AttachMemory(node, operands, blockId, pushedMemory, localFieldBaseValue: Top(fieldAccess.BaseSlot), localField: fieldAccess.Field);
                        }
                    }
                    else if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                    {
                        if (_slots.IsPromotable(fieldAccess.Slot))
                        {
                            var operands = GetRenamedNonReceiverOperands(node, fieldAccess, renamedByNode);
                            var target = NewVersion(fieldAccess.Slot);
                            Push(fieldAccess.Slot, target);
                            pushed.Add(fieldAccess.Slot);
                            AddDefToEHSuccessorPhis(blockId, fieldAccess.Slot, target);
                            return AttachMemory(node, operands, blockId, pushedMemory, storeTarget: target);
                        }
                    }
                    else if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                    {
                        if (_slots.IsPromotable(fieldAccess.BaseSlot))
                        {
                            var operands = GetRenamedNonReceiverOperands(node, fieldAccess, renamedByNode);
                            var previous = Top(fieldAccess.BaseSlot);
                            var target = NewVersion(fieldAccess.BaseSlot);
                            SetUseDefSsaNum(target, previous);
                            Push(fieldAccess.BaseSlot, target);
                            pushed.Add(fieldAccess.BaseSlot);
                            AddDefToEHSuccessorPhis(blockId, fieldAccess.BaseSlot, target);
                            return AttachMemory(node, operands, blockId, pushedMemory, storeTarget: target, localField: fieldAccess.Field);
                        }
                    }
                }

                if (TryGetDirectLoadSlot(node, out var loadSlot) && _slots.IsPromotable(loadSlot))
                    return AttachMemory(node, ImmutableArray<SsaTree>.Empty, blockId, pushedMemory, value: Top(loadSlot));

                var allOperands = GetRenamedOperands(node, renamedByNode);

                if (TryGetDirectStoreSlot(node, out var storeSlot) && _slots.IsPromotable(storeSlot))
                {
                    var target = NewVersion(storeSlot);
                    Push(storeSlot, target);
                    pushed.Add(storeSlot);
                    AddDefToEHSuccessorPhis(blockId, storeSlot, target);
                    return AttachMemory(node, allOperands, blockId, pushedMemory, storeTarget: target);
                }

                return AttachMemory(node, allOperands, blockId, pushedMemory);
            }

            private SsaTree AttachMemory(
                GenTree node,
                ImmutableArray<SsaTree> operands,
                int blockId,
                List<SsaMemoryKind> pushedMemory,
                SsaValueName? value = null,
                SsaValueName? storeTarget = null,
                SsaValueName? localFieldBaseValue = null,
                RuntimeField? localField = null)
            {
                var effects = GetMemoryEffects(node, _slots);
                var memoryUses = ImmutableArray.CreateBuilder<SsaMemoryValueName>();
                var memoryDefs = ImmutableArray.CreateBuilder<SsaMemoryValueName>();

                SsaMemoryKindSet useSet = effects.Uses | effects.Definitions;
                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (useSet.Contains(kind))
                        memoryUses.Add(TopMemory(kind));
                }

                foreach (var kind in SsaMemoryKinds.All)
                {
                    if (!effects.Definitions.Contains(kind))
                        continue;

                    var def = NewMemoryVersion(kind);
                    PushMemory(kind, def);
                    pushedMemory.Add(kind);
                    memoryDefs.Add(def);
                    AddMemoryDefToEHSuccessorPhis(blockId, kind, def);
                }

                return new SsaTree(
                    node,
                    operands,
                    value,
                    storeTarget,
                    localFieldBaseValue,
                    localField,
                    memoryUses.ToImmutable(),
                    memoryDefs.ToImmutable());
            }

            private static ImmutableArray<SsaTree> GetRenamedOperands(GenTree node, Dictionary<GenTree, SsaTree> renamedByNode)
            {
                if (node.Operands.Length == 0)
                    return ImmutableArray<SsaTree>.Empty;

                var operands = ImmutableArray.CreateBuilder<SsaTree>(node.Operands.Length);
                for (int i = 0; i < node.Operands.Length; i++)
                    operands.Add(GetRenamedOperand(node, i, renamedByNode));
                return operands.ToImmutable();
            }

            private static ImmutableArray<SsaTree> GetRenamedNonReceiverOperands(GenTree node, SsaLocalAccess fieldAccess, Dictionary<GenTree, SsaTree> renamedByNode)
            {
                var operands = ImmutableArray.CreateBuilder<SsaTree>(Math.Max(0, node.Operands.Length - 1));
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i == fieldAccess.ReceiverOperandIndex)
                        continue;
                    operands.Add(GetRenamedOperand(node, i, renamedByNode));
                }
                return operands.ToImmutable();
            }

            private static SsaTree GetRenamedOperand(GenTree node, int operandIndex, Dictionary<GenTree, SsaTree> renamedByNode)
            {
                var operand = node.Operands[operandIndex];
                if (!renamedByNode.TryGetValue(operand, out var renamed))
                    throw new InvalidOperationException("HIR tree-list is not in execution order: node " + node.Id.ToString() + " operand " + operand.Id.ToString() + " has not been renamed yet.");
                return renamed;
            }

            private void SetUseDefSsaNum(SsaValueName definition, SsaValueName use)
            {
                if (!definition.Slot.Equals(use.Slot))
                    throw new InvalidOperationException("Partial SSA definition " + definition + " uses different base local " + use + ".");
                if (!_useDefLinks.TryAdd(definition, new SsaUseDefLink(definition, use)))
                    throw new InvalidOperationException("Duplicate SSA use-def link for " + definition + ".");
            }

            private SsaValueName Top(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                {
                    var initial = new SsaValueName(slot, SsaConfig.FirstSsaNumber);
                    _nextVersion[slot] = Math.Max(_nextVersion.TryGetValue(slot, out int n) ? n : SsaConfig.FirstSsaNumber, SsaConfig.FirstSsaNumber);
                    stack = new Stack<SsaValueName>();
                    stack.Push(initial);
                    _stacks[slot] = stack;
                    if (!_initialValues.Contains(initial))
                        _initialValues.Add(initial);
                    return initial;
                }

                return stack.Peek();
            }

            private SsaMemoryValueName TopMemory(SsaMemoryKind kind)
            {
                if (!_memoryStacks.TryGetValue(kind, out var stack) || stack.Count == 0)
                {
                    var initial = new SsaMemoryValueName(kind, SsaConfig.FirstSsaNumber);
                    _nextMemoryVersion[kind] = Math.Max(_nextMemoryVersion.TryGetValue(kind, out int n) ? n : SsaConfig.FirstSsaNumber, SsaConfig.FirstSsaNumber);
                    stack = new Stack<SsaMemoryValueName>();
                    stack.Push(initial);
                    _memoryStacks[kind] = stack;
                    if (!_initialMemoryValues.Contains(initial))
                        _initialMemoryValues.Add(initial);
                    return initial;
                }

                return stack.Peek();
            }

            private SsaValueName NewVersion(SsaSlot slot)
            {
                int next = _nextVersion.TryGetValue(slot, out int current) ? current + 1 : 1;
                _nextVersion[slot] = next;
                return new SsaValueName(slot, next);
            }

            private SsaMemoryValueName NewMemoryVersion(SsaMemoryKind kind)
            {
                int next = _nextMemoryVersion.TryGetValue(kind, out int current) ? current + 1 : 1;
                _nextMemoryVersion[kind] = next;
                return new SsaMemoryValueName(kind, next);
            }

            private void Push(SsaSlot slot, SsaValueName value)
            {
                if (!_stacks.TryGetValue(slot, out var stack))
                {
                    stack = new Stack<SsaValueName>();
                    _stacks[slot] = stack;
                }
                stack.Push(value);
            }

            private void PushMemory(SsaMemoryKind kind, SsaMemoryValueName value)
            {
                if (value.Kind != kind)
                    throw new InvalidOperationException("Memory SSA push kind mismatch: " + value + " for " + kind.ToString() + ".");

                if (!_memoryStacks.TryGetValue(kind, out var stack))
                {
                    stack = new Stack<SsaMemoryValueName>();
                    _memoryStacks[kind] = stack;
                }
                stack.Push(value);
            }

            private void Pop(SsaSlot slot)
            {
                if (!_stacks.TryGetValue(slot, out var stack) || stack.Count == 0)
                    throw new InvalidOperationException($"SSA rename stack underflow for {slot}.");
                stack.Pop();
            }

            private void PopMemory(SsaMemoryKind kind)
            {
                if (!_memoryStacks.TryGetValue(kind, out var stack) || stack.Count == 0)
                    throw new InvalidOperationException($"Memory SSA rename stack underflow for {kind}.");
                stack.Pop();
            }
        }

        private sealed class MutablePhi
        {
            private readonly List<SsaPhiInput> _inputs = new();

            public int BlockId { get; }
            public SsaSlot Slot { get; }
            public SsaValueName Target { get; set; }

            public MutablePhi(int blockId, SsaSlot slot)
            {
                BlockId = blockId;
                Slot = slot;
            }

            public void AddInput(int predecessorBlockId, SsaValueName value, bool allowMultipleForPred)
            {
                if (!value.Slot.Equals(Slot))
                    throw new InvalidOperationException($"SSA phi input slot mismatch for {Slot}: {value}.");

                for (int i = 0; i < _inputs.Count; i++)
                {
                    var input = _inputs[i];
                    if (input.PredecessorBlockId != predecessorBlockId)
                        continue;

                    if (input.Value.Equals(value))
                        return;

                    if (!allowMultipleForPred)
                        throw new InvalidOperationException($"Conflicting SSA phi inputs for {Slot} in B{BlockId} from B{predecessorBlockId}: {input.Value} and {value}.");
                }

                _inputs.Add(new SsaPhiInput(predecessorBlockId, value));
            }

            public SsaPhi Freeze(CfgBlock block)
            {
                var expectedPreds = UniquePredecessors(block);
                var actualPreds = new HashSet<int>();
                var sortedInputs = new List<SsaPhiInput>(_inputs);
                sortedInputs.Sort(static (a, b) =>
                {
                    int c = a.PredecessorBlockId.CompareTo(b.PredecessorBlockId);
                    return c != 0 ? c : a.Value.CompareTo(b.Value);
                });

                var inputs = ImmutableArray.CreateBuilder<SsaPhiInput>(sortedInputs.Count);
                for (int i = 0; i < sortedInputs.Count; i++)
                {
                    var input = sortedInputs[i];
                    actualPreds.Add(input.PredecessorBlockId);
                    inputs.Add(input);
                }

                foreach (int pred in expectedPreds)
                {
                    if (!actualPreds.Contains(pred))
                        throw new InvalidOperationException($"Missing SSA phi input for {Slot} in B{BlockId} from B{pred}.");
                }

                if (!block.IsHandlerEntry && !expectedPreds.SetEquals(actualPreds))
                    throw new InvalidOperationException($"Malformed SSA phi inputs for {Slot} in B{BlockId}: predecessor set mismatch.");

                return new SsaPhi(BlockId, Slot, Target, inputs.ToImmutable());
            }
        }

        private sealed class MutableMemoryPhi
        {
            private readonly List<SsaMemoryPhiInput> _inputs = new();

            public int BlockId { get; }
            public SsaMemoryKind Kind { get; }
            public SsaMemoryValueName Target { get; set; }

            public MutableMemoryPhi(int blockId, SsaMemoryKind kind)
            {
                BlockId = blockId;
                Kind = kind;
            }

            public void AddInput(int predecessorBlockId, SsaMemoryValueName value, bool allowMultipleForPred)
            {
                if (value.Kind != Kind)
                    throw new InvalidOperationException($"Memory phi input kind mismatch for {Kind}: {value}.");

                for (int i = 0; i < _inputs.Count; i++)
                {
                    var input = _inputs[i];
                    if (input.PredecessorBlockId != predecessorBlockId)
                        continue;

                    if (input.Value.Equals(value))
                        return;

                    if (!allowMultipleForPred)
                        throw new InvalidOperationException($"Conflicting memory SSA phi inputs for {Kind} in B{BlockId} from B{predecessorBlockId}: {input.Value} and {value}.");
                }

                _inputs.Add(new SsaMemoryPhiInput(predecessorBlockId, value));
            }

            public SsaMemoryPhi Freeze(CfgBlock block)
            {
                var expectedPreds = UniquePredecessors(block);
                var actualPreds = new HashSet<int>();
                var sortedInputs = new List<SsaMemoryPhiInput>(_inputs);
                sortedInputs.Sort(static (a, b) =>
                {
                    int c = a.PredecessorBlockId.CompareTo(b.PredecessorBlockId);
                    return c != 0 ? c : a.Value.CompareTo(b.Value);
                });

                var inputs = ImmutableArray.CreateBuilder<SsaMemoryPhiInput>(sortedInputs.Count);
                for (int i = 0; i < sortedInputs.Count; i++)
                {
                    var input = sortedInputs[i];
                    actualPreds.Add(input.PredecessorBlockId);
                    inputs.Add(input);
                }

                foreach (int pred in expectedPreds)
                {
                    if (!actualPreds.Contains(pred))
                        throw new InvalidOperationException($"Missing memory SSA phi input for {Kind} in B{BlockId} from B{pred}.");
                }

                if (!block.IsHandlerEntry && !expectedPreds.SetEquals(actualPreds))
                    throw new InvalidOperationException($"Malformed memory SSA phi inputs for {Kind} in B{BlockId}: predecessor set mismatch.");

                return new SsaMemoryPhi(BlockId, Kind, Target, inputs.ToImmutable());
            }
        }

        private static HashSet<int> UniquePredecessors(CfgBlock block)
        {
            var result = new HashSet<int>();
            for (int i = 0; i < block.Predecessors.Length; i++)
                result.Add(block.Predecessors[i].FromBlockId);
            return result;
        }

        private static bool TryGetLoadSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetLoadSlot(node, out slot);

        private static bool TryGetStoreSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetStoreSlot(node, out slot);

        private static bool TryGetDirectLoadSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetDirectLoadSlot(node, out slot);

        private static bool TryGetDirectStoreSlot(GenTree node, out SsaSlot slot)
            => SsaSlotHelpers.TryGetDirectStoreSlot(node, out slot);

        private static GenStackKind StackKindOf(RuntimeType? type)
        {
            if (type is null)
                return GenStackKind.Unknown;

            if (type.Namespace == "System" && type.Name == "Void")
                return GenStackKind.Void;

            if (type.IsReferenceType)
                return GenStackKind.Ref;

            if (type.Kind == RuntimeTypeKind.Pointer)
                return GenStackKind.Ptr;

            if (type.Kind == RuntimeTypeKind.ByRef)
                return GenStackKind.ByRef;

            if (type.Kind == RuntimeTypeKind.TypeParam)
                return GenStackKind.Value;

            if (type.Kind == RuntimeTypeKind.Enum)
                return type.SizeOf <= 4 ? GenStackKind.I4 : GenStackKind.I8;

            if (type.Namespace == "System")
            {
                switch (type.Name)
                {
                    case "Boolean":
                    case "Char":
                    case "SByte":
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                        return GenStackKind.I4;
                    case "Int64":
                    case "UInt64":
                        return GenStackKind.I8;
                    case "Single":
                        return GenStackKind.R4;
                    case "Double":
                        return GenStackKind.R8;
                    case "IntPtr":
                        return GenStackKind.NativeInt;
                    case "UIntPtr":
                        return GenStackKind.NativeUInt;
                }
            }

            return GenStackKind.Value;
        }
    }
}
