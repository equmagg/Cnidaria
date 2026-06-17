using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal sealed class SsaLocalLiveness
    {
        private readonly Dictionary<(int blockId, int statementIndex, int treeId), TrackedLocalSet> _liveBeforeTree;
        private readonly Dictionary<(int blockId, int statementIndex), TrackedLocalSet> _liveBeforeStatement;

        public TrackedLocalTable Table { get; }
        public ImmutableArray<TrackedLocalSet> UseBits { get; }
        public ImmutableArray<TrackedLocalSet> DefBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveInBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveOutBits { get; }

        private SsaLocalLiveness(
            TrackedLocalTable table,
            TrackedLocalSet[] uses,
            TrackedLocalSet[] defs,
            TrackedLocalSet[] liveIn,
            TrackedLocalSet[] liveOut,
            Dictionary<(int blockId, int statementIndex, int treeId), TrackedLocalSet> liveBeforeTree,
            Dictionary<(int blockId, int statementIndex), TrackedLocalSet> liveBeforeStatement)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            UseBits = FreezeBitSets(uses);
            DefBits = FreezeBitSets(defs);
            LiveInBits = FreezeBitSets(liveIn);
            LiveOutBits = FreezeBitSets(liveOut);
            _liveBeforeTree = liveBeforeTree ?? throw new ArgumentNullException(nameof(liveBeforeTree));
            _liveBeforeStatement = liveBeforeStatement ?? throw new ArgumentNullException(nameof(liveBeforeStatement));
        }

        public bool IsTracked(SsaSlot slot) => Table.Contains(slot);
        public bool IsLiveIn(int blockId, SsaSlot slot) => Contains(LiveInBits, blockId, slot);
        public bool IsLiveOut(int blockId, SsaSlot slot) => Contains(LiveOutBits, blockId, slot);

        public bool IsLiveBeforeStatement(int blockId, int statementIndex, SsaSlot slot)
        {
            if (!Table.Contains(slot))
                return false;
            if (_liveBeforeStatement.TryGetValue((blockId, statementIndex), out var live))
                return live.Contains(slot);
            return IsLiveIn(blockId, slot);
        }

        public bool IsLiveBeforeTree(int blockId, int statementIndex, int treeId, SsaSlot slot)
        {
            if (!Table.Contains(slot))
                return false;
            if (_liveBeforeTree.TryGetValue((blockId, statementIndex, treeId), out var live))
                return live.Contains(slot);
            return IsLiveBeforeStatement(blockId, statementIndex, slot);
        }

        public static SsaLocalLiveness Build(SsaMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var slots = ImmutableArray.CreateBuilder<SsaSlot>();
            var seenSlots = new HashSet<SsaSlot>();
            for (int i = 0; i < method.SsaLocalDescriptors.Length; i++)
            {
                var slot = method.SsaLocalDescriptors[i].Slot;
                if (seenSlots.Add(slot))
                    slots.Add(slot);
            }

            var sorted = new List<SsaSlot>(slots.ToImmutable());
            sorted.Sort();
            var table = new TrackedLocalTable(sorted.ToImmutableArray());
            int blockCount = method.Blocks.Length;
            var uses = NewSetArray(blockCount, table);
            var defs = NewSetArray(blockCount, table);
            var liveIn = NewSetArray(blockCount, table);
            var liveOut = NewSetArray(blockCount, table);
            var eventsByBlock = new List<SsaLocalLivenessEvent>[blockCount];
            var blockById = new SsaBlock[blockCount];

            for (int b = 0; b < blockCount; b++)
            {
                eventsByBlock[b] = new List<SsaLocalLivenessEvent>();
                blockById[method.Blocks[b].Id] = method.Blocks[b];
            }

            var partialUseByDefinition = BuildPartialUseMap(method);

            for (int b = 0; b < blockCount; b++)
            {
                var block = method.Blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    if (table.Contains(block.Phis[p].Slot))
                        defs[block.Id].Add(block.Phis[p].Slot);
                }

                CollectStatementEvents(block, partialUseByDefinition, table, eventsByBlock[block.Id]);

                for (int e = 0; e < eventsByBlock[block.Id].Count; e++)
                {
                    var ev = eventsByBlock[block.Id][e];
                    if (ev.IsUse && !defs[block.Id].Contains(ev.Slot))
                        uses[block.Id].Add(ev.Slot);
                    if (ev.IsDefinition)
                        defs[block.Id].Add(ev.Slot);
                }
            }
            var scratchOut = table.NewEmptySet();
            var scratchIn = table.NewEmptySet();
            bool changed;
            do
            {
                changed = false;
                for (int r = method.Cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                {
                    int blockId = method.Cfg.ReversePostOrder[r];
                    var block = blockById[blockId];
                    if (block is null)
                        continue;

                    scratchOut.Clear();
                    var successors = method.Cfg.Blocks[blockId].Successors;
                    for (int i = 0; i < successors.Length; i++)
                    {
                        int succ = successors[i].ToBlockId;
                        scratchOut.UnionWith(liveIn[succ]);
                        AddPhiEdgeUses(blockById[succ], blockId, scratchOut, table);
                    }
                    AddImplicitExceptionHandlerLiveOut(method.Cfg, blockId, liveIn, scratchOut);

                    scratchIn.CopyFrom(scratchOut);
                    scratchIn.ExceptWith(defs[blockId]);
                    scratchIn.UnionWith(uses[blockId]);

                    changed |= liveOut[blockId].CopyFromIfChanged(scratchOut);
                    changed |= liveIn[blockId].CopyFromIfChanged(scratchIn);
                }
            }
            while (changed);

            var liveBeforeTree = new Dictionary<(int blockId, int statementIndex, int treeId), TrackedLocalSet>();
            var liveBeforeStatement = new Dictionary<(int blockId, int statementIndex), TrackedLocalSet>();

            for (int b = 0; b < blockCount; b++)
            {
                var block = method.Blocks[b];
                var live = liveOut[block.Id].Clone();
                var events = eventsByBlock[block.Id];
                int currentStatement = block.Statements.Length;

                for (int e = events.Count - 1; e >= 0; e--)
                {
                    var ev = events[e];
                    while (currentStatement > ev.StatementIndex + 1)
                    {
                        currentStatement--;
                        liveBeforeStatement[(block.Id, currentStatement)] = live.Clone();
                    }

                    var before = live.Clone();
                    if (ev.IsDefinition)
                        before.Remove(ev.Slot);
                    if (ev.IsUse)
                        before.Add(ev.Slot);

                    liveBeforeTree[(block.Id, ev.StatementIndex, ev.TreeId)] = before.Clone();
                    live = before;
                    currentStatement = ev.StatementIndex + 1;
                }

                while (currentStatement > 0)
                {
                    currentStatement--;
                    liveBeforeStatement[(block.Id, currentStatement)] = live.Clone();
                }
            }

            return new SsaLocalLiveness(table, uses, defs, liveIn, liveOut, liveBeforeTree, liveBeforeStatement);
        }

        private static Dictionary<SsaValueName, SsaValueName> BuildPartialUseMap(SsaMethod method)
        {
            var result = new Dictionary<SsaValueName, SsaValueName>();
            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var descriptor = method.ValueDefinitions[i].Descriptor;
                if (descriptor.HasUseDefSsaNum)
                    result[descriptor.Name] = new SsaValueName(descriptor.BaseLocal, descriptor.UseDefSsaNumber);
            }
            return result;
        }

        private static void AddPhiEdgeUses(SsaBlock? successor, int predecessorBlockId, TrackedLocalSet liveOut, TrackedLocalTable table)
        {
            if (successor is null)
                return;

            for (int p = 0; p < successor.Phis.Length; p++)
            {
                var phi = successor.Phis[p];
                if (!table.Contains(phi.Slot))
                    continue;

                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    if (phi.Inputs[i].PredecessorBlockId == predecessorBlockId)
                    {
                        liveOut.Add(phi.Slot);
                        break;
                    }
                }
            }
        }

        private static void AddImplicitExceptionHandlerLiveOut(
            ControlFlowGraph cfg,
            int blockId,
            TrackedLocalSet[] liveIn,
            TrackedLocalSet target)
        {
            var tryRegions = cfg.Blocks[blockId].TryRegionIndexes;
            for (int i = 0; i < tryRegions.Length; i++)
            {
                int regionIndex = tryRegions[i];
                if ((uint)regionIndex >= (uint)cfg.ExceptionRegions.Length)
                    continue;

                int handlerBlockId = cfg.ExceptionRegions[regionIndex].HandlerStartBlockId;
                if ((uint)handlerBlockId >= (uint)liveIn.Length)
                    continue;

                target.UnionWith(liveIn[handlerBlockId]);
            }
        }

        private static void CollectStatementEvents(
            SsaBlock block,
            IReadOnlyDictionary<SsaValueName, SsaValueName> partialUseByDefinition,
            TrackedLocalTable table,
            List<SsaLocalLivenessEvent> events)
        {
            for (int i = 0; i < block.TreeList.Length; i++)
            {
                var item = block.TreeList[i];
                var tree = item.Tree;
                int statementIndex = item.StatementIndex;

                if (tree.Value.HasValue && table.Contains(tree.Value.Value.Slot))
                    events.Add(SsaLocalLivenessEvent.Use(block.Id, statementIndex, tree.Source.Id, tree.Value.Value.Slot));

                if (tree.LocalFieldBaseValue.HasValue && table.Contains(tree.LocalFieldBaseValue.Value.Slot))
                    events.Add(SsaLocalLivenessEvent.Use(block.Id, statementIndex, tree.Source.Id, tree.LocalFieldBaseValue.Value.Slot));

                if (tree.StoreTarget.HasValue)
                {
                    var target = tree.StoreTarget.Value;
                    if (partialUseByDefinition.TryGetValue(target, out var previous) && table.Contains(previous.Slot))
                        events.Add(SsaLocalLivenessEvent.Use(block.Id, statementIndex, tree.Source.Id, previous.Slot));

                    if (table.Contains(target.Slot))
                        events.Add(SsaLocalLivenessEvent.Definition(block.Id, statementIndex, tree.Source.Id, target.Slot));
                }
            }
        }

        private readonly struct SsaLocalLivenessEvent
        {
            public readonly int BlockId;
            public readonly int StatementIndex;
            public readonly int TreeId;
            public readonly SsaSlot Slot;
            public readonly bool IsUse;
            public readonly bool IsDefinition;

            private SsaLocalLivenessEvent(int blockId, int statementIndex, int treeId, SsaSlot slot, bool isUse, bool isDefinition)
            {
                BlockId = blockId;
                StatementIndex = statementIndex;
                TreeId = treeId;
                Slot = slot;
                IsUse = isUse;
                IsDefinition = isDefinition;
            }

            public static SsaLocalLivenessEvent Use(int blockId, int statementIndex, int treeId, SsaSlot slot)
                => new SsaLocalLivenessEvent(blockId, statementIndex, treeId, slot, isUse: true, isDefinition: false);

            public static SsaLocalLivenessEvent Definition(int blockId, int statementIndex, int treeId, SsaSlot slot)
                => new SsaLocalLivenessEvent(blockId, statementIndex, treeId, slot, isUse: false, isDefinition: true);
        }

        private static TrackedLocalSet[] NewSetArray(int count, TrackedLocalTable table)
        {
            var result = new TrackedLocalSet[count];
            for (int i = 0; i < result.Length; i++)
                result[i] = table.NewEmptySet();
            return result;
        }

        private static ImmutableArray<TrackedLocalSet> FreezeBitSets(TrackedLocalSet[] sets)
        {
            var builder = ImmutableArray.CreateBuilder<TrackedLocalSet>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
                builder.Add(sets[i].Clone());
            return builder.ToImmutable();
        }

        private static bool Contains(ImmutableArray<TrackedLocalSet> sets, int blockId, SsaSlot slot)
        {
            if ((uint)blockId >= (uint)sets.Length)
                throw new ArgumentOutOfRangeException(nameof(blockId));
            return sets[blockId].Contains(slot);
        }
    }
    internal sealed class GenTreeLocalLiveness
    {
        public ControlFlowGraph Cfg { get; }
        public TrackedLocalTable TrackedLocals { get; }
        public ImmutableArray<SsaSlot> TrackedSlots { get; }
        public ImmutableArray<SsaSlot> SsaCandidateSlots { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> Uses { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> Defs { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> LiveIn { get; }
        public ImmutableArray<ImmutableArray<SsaSlot>> LiveOut { get; }
        public ImmutableArray<TrackedLocalSet> UseBits { get; }
        public ImmutableArray<TrackedLocalSet> DefBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveInBits { get; }
        public ImmutableArray<TrackedLocalSet> LiveOutBits { get; }

        private GenTreeLocalLiveness(
            ControlFlowGraph cfg,
            TrackedLocalTable trackedLocals,
            ImmutableArray<SsaSlot> ssaCandidateSlots,
            TrackedLocalSet[] uses,
            TrackedLocalSet[] defs,
            TrackedLocalSet[] liveIn,
            TrackedLocalSet[] liveOut)
        {
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            TrackedLocals = trackedLocals ?? TrackedLocalTable.Empty;
            TrackedSlots = TrackedLocals.Slots;
            SsaCandidateSlots = ssaCandidateSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : ssaCandidateSlots;
            Uses = FreezeSets(uses);
            Defs = FreezeSets(defs);
            LiveIn = FreezeSets(liveIn);
            LiveOut = FreezeSets(liveOut);
            UseBits = FreezeBitSets(uses);
            DefBits = FreezeBitSets(defs);
            LiveInBits = FreezeBitSets(liveIn);
            LiveOutBits = FreezeBitSets(liveOut);
        }

        public bool IsLiveIn(int blockId, SsaSlot slot) => Contains(LiveInBits, blockId, slot);
        public bool IsLiveOut(int blockId, SsaSlot slot) => Contains(LiveOutBits, blockId, slot);
        public bool IsUsedInBlock(int blockId, SsaSlot slot) => Contains(UseBits, blockId, slot);
        public bool IsDefinedInBlock(int blockId, SsaSlot slot) => Contains(DefBits, blockId, slot);

        public static GenTreeLocalLiveness Build(GenTreeMethod method, ControlFlowGraph cfg)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("HIR liveness CFG does not match method block count.");

            var tracking = GenTreeLocalTracking.AssignTrackedLocals(method, cfg);
            var trackedLocals = tracking.TrackedLocals;
            int blockCount = cfg.Blocks.Length;
            var uses = NewSetArray(blockCount, trackedLocals);
            var defs = NewSetArray(blockCount, trackedLocals);
            var liveIn = NewSetArray(blockCount, trackedLocals);
            var liveOut = NewSetArray(blockCount, trackedLocals);

            for (int b = 0; b < blockCount; b++)
            {
                var nodes = cfg.Blocks[b].SourceBlock.LinearNodes;
                ClearLocalDataflowFlags(nodes);
                CollectUseDef(nodes, trackedLocals, uses[b], defs[b]);
            }
            var scratchOut = trackedLocals.NewEmptySet();
            var scratchIn = trackedLocals.NewEmptySet();
            bool changed;
            do
            {
                changed = false;
                for (int r = cfg.ReversePostOrder.Length - 1; r >= 0; r--)
                {
                    int blockId = cfg.ReversePostOrder[r];
                    scratchOut.Clear();
                    var successors = cfg.Blocks[blockId].Successors;
                    for (int i = 0; i < successors.Length; i++)
                        scratchOut.UnionWith(liveIn[successors[i].ToBlockId]);
                    AddImplicitExceptionHandlerLiveOut(cfg, blockId, liveIn, scratchOut);

                    scratchIn.CopyFrom(scratchOut);
                    scratchIn.ExceptWith(defs[blockId]);
                    scratchIn.UnionWith(uses[blockId]);

                    changed |= liveOut[blockId].CopyFromIfChanged(scratchOut);
                    changed |= liveIn[blockId].CopyFromIfChanged(scratchIn);
                }
            }
            while (changed);

            MarkLastUses(cfg, trackedLocals, liveOut);

            return new GenTreeLocalLiveness(
                cfg,
                trackedLocals,
                tracking.SsaCandidateSlots,
                uses,
                defs,
                liveIn,
                liveOut);
        }

        private static void AddImplicitExceptionHandlerLiveOut(
            ControlFlowGraph cfg,
            int blockId,
            TrackedLocalSet[] liveIn,
            TrackedLocalSet target)
        {
            var block = cfg.Blocks[blockId];
            var tryRegions = block.TryRegionIndexes;
            for (int i = 0; i < tryRegions.Length; i++)
            {
                int regionIndex = tryRegions[i];
                if ((uint)regionIndex >= (uint)cfg.ExceptionRegions.Length)
                    continue;

                int handlerBlockId = cfg.ExceptionRegions[regionIndex].HandlerStartBlockId;
                if ((uint)handlerBlockId >= (uint)liveIn.Length)
                    continue;

                target.UnionWith(liveIn[handlerBlockId]);
            }
        }

        private static void MarkLastUses(ControlFlowGraph cfg, TrackedLocalTable trackedLocals, TrackedLocalSet[] liveOut)
        {
            for (int b = 0; b < cfg.Blocks.Length; b++)
            {
                var live = liveOut[b].Clone();
                var events = new List<LocalLivenessEvent>();
                CollectLivenessEvents(cfg.Blocks[b].SourceBlock.LinearNodes, trackedLocals, events);

                for (int i = events.Count - 1; i >= 0; i--)
                {
                    var e = events[i];
                    if (e.IsDefinition)
                        live.Remove(e.Slot);

                    if (e.IsUse)
                    {
                        if (!live.Contains(e.Slot))
                            e.Node.Flags |= GenTreeFlags.VarDeath;
                        live.Add(e.Slot);
                    }
                }
            }
        }

        private readonly struct LocalLivenessEvent
        {
            public readonly GenTree Node;
            public readonly SsaSlot Slot;
            public readonly bool IsUse;
            public readonly bool IsDefinition;

            public LocalLivenessEvent(GenTree node, SsaSlot slot, bool isUse, bool isDefinition)
            {
                Node = node;
                Slot = slot;
                IsUse = isUse;
                IsDefinition = isDefinition;
            }
        }

        private static void CollectLivenessEvents(ImmutableArray<GenTree> treeList, TrackedLocalTable trackedLocals, List<LocalLivenessEvent> events)
        {
            for (int i = 0; i < treeList.Length; i++)
            {
                var node = treeList[i];
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    if (trackedLocals.Contains(fieldAccess.Slot))
                    {
                        events.Add(new LocalLivenessEvent(
                            node,
                            fieldAccess.Slot,
                            isUse: fieldAccess.Kind != SsaLocalAccessKind.FullDefinition,
                            isDefinition: fieldAccess.IsDefinition));
                    }
                    continue;
                }

                if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
                {
                    if (trackedLocals.Contains(storeSlot))
                        events.Add(new LocalLivenessEvent(node, storeSlot, isUse: false, isDefinition: true));
                    continue;
                }

                if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
                {
                    if (trackedLocals.Contains(loadSlot))
                        events.Add(new LocalLivenessEvent(node, loadSlot, isUse: true, isDefinition: false));
                }
            }
        }

        private static bool Contains(ImmutableArray<TrackedLocalSet> sets, int blockId, SsaSlot slot)
        {
            if ((uint)blockId >= (uint)sets.Length)
                throw new ArgumentOutOfRangeException(nameof(blockId));
            return sets[blockId].Contains(slot);
        }

        private static void ClearLocalDataflowFlags(ImmutableArray<GenTree> nodes)
        {
            for (int i = 0; i < nodes.Length; i++)
                nodes[i].Flags &= ~(GenTreeFlags.VarDef | GenTreeFlags.VarUseAsg | GenTreeFlags.VarDeath);
        }

        private static void CollectUseDef(ImmutableArray<GenTree> treeList, TrackedLocalTable trackedLocals, TrackedLocalSet uses, TrackedLocalSet defs)
        {
            for (int i = 0; i < treeList.Length; i++)
                CollectUseDefNode(treeList[i], trackedLocals, uses, defs);
        }

        private static void CollectUseDefNode(GenTree node, TrackedLocalTable trackedLocals, TrackedLocalSet uses, TrackedLocalSet defs)
        {
            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
            {
                if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                {
                    MarkUse(node, fieldAccess.Slot, trackedLocals, uses, defs);
                    return;
                }

                if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    MarkUse(node, fieldAccess.Slot, trackedLocals, uses, defs);
                    if (trackedLocals.Contains(fieldAccess.Slot))
                        defs.Add(fieldAccess.Slot);
                    node.Flags |= GenTreeFlags.LocalUse | GenTreeFlags.LocalDef | GenTreeFlags.VarDef | GenTreeFlags.VarUseAsg;
                    return;
                }

                if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                {
                    if (trackedLocals.Contains(fieldAccess.Slot))
                        defs.Add(fieldAccess.Slot);
                    node.Flags |= GenTreeFlags.LocalDef | GenTreeFlags.VarDef;
                    return;
                }
            }

            if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
            {
                if (trackedLocals.Contains(storeSlot))
                    defs.Add(storeSlot);
                node.Flags |= GenTreeFlags.LocalDef | GenTreeFlags.VarDef;
                return;
            }

            if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
            {
                MarkUse(node, loadSlot, trackedLocals, uses, defs);
                return;
            }
        }

        private static void MarkUse(GenTree node, SsaSlot slot, TrackedLocalTable trackedLocals, TrackedLocalSet uses, TrackedLocalSet defs)
        {
            if (trackedLocals.Contains(slot) && !defs.Contains(slot))
                uses.Add(slot);
            node.Flags |= GenTreeFlags.LocalUse;
        }

        private static TrackedLocalSet[] NewSetArray(int count, TrackedLocalTable table)
        {
            var result = new TrackedLocalSet[count];
            for (int i = 0; i < result.Length; i++)
                result[i] = table.NewEmptySet();
            return result;
        }

        private static ImmutableArray<TrackedLocalSet> FreezeBitSets(TrackedLocalSet[] sets)
        {
            var builder = ImmutableArray.CreateBuilder<TrackedLocalSet>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
                builder.Add(sets[i].Clone());
            return builder.ToImmutable();
        }

        private static ImmutableArray<ImmutableArray<SsaSlot>> FreezeSets(TrackedLocalSet[] sets)
        {
            var builder = ImmutableArray.CreateBuilder<ImmutableArray<SsaSlot>>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
                builder.Add(sets[i].ToImmutableSlots());
            return builder.ToImmutable();
        }
    }
}
