using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class LinearBlockOrder
    {
        public static ImmutableArray<int> Compute(ControlFlowGraph cfg)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            if (cfg.ExceptionRegions.Length != 0)
                return EhFuncletLayout.ComputeLinearBlockOrder(cfg);

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var seen = new bool[cfg.Blocks.Length];
            var loopDepth = BuildLoopDepthMap(cfg);

            void AddTrace(int blockId)
            {
                if ((uint)blockId >= (uint)cfg.Blocks.Length)
                    throw new InvalidOperationException($"CFG block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    return;

                seen[blockId] = true;
                result.Add(blockId);

                var successors = OrderSuccessorsForLayout(cfg, blockId, loopDepth);
                for (int i = 0; i < successors.Length; i++)
                    AddTrace(successors[i]);
            }

            if (cfg.Blocks.Length != 0)
                AddTrace(0);

            for (int i = 0; i < cfg.ReversePostOrder.Length; i++)
                AddTrace(cfg.ReversePostOrder[i]);

            for (int blockId = 0; blockId < cfg.Blocks.Length; blockId++)
                AddTrace(blockId);

            return result.ToImmutable();
        }

        private static int[] BuildLoopDepthMap(ControlFlowGraph cfg)
        {
            var depth = new int[cfg.Blocks.Length];
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                var loop = cfg.NaturalLoops[i];
                int loopDepth = Math.Max(1, loop.Depth + 1);
                for (int b = 0; b < loop.Blocks.Length; b++)
                {
                    int blockId = loop.Blocks[b];
                    if ((uint)blockId < (uint)depth.Length && loopDepth > depth[blockId])
                        depth[blockId] = loopDepth;
                }
            }
            return depth;
        }

        private static ImmutableArray<int> OrderSuccessorsForLayout(ControlFlowGraph cfg, int blockId, int[] loopDepth)
        {
            var successors = cfg.Blocks[blockId].Successors;
            if (successors.Length == 0)
                return ImmutableArray<int>.Empty;

            var normal = ImmutableArray.CreateBuilder<CfgEdge>(successors.Length);
            var exceptional = ImmutableArray.CreateBuilder<CfgEdge>();
            for (int i = 0; i < successors.Length; i++)
            {
                var edge = successors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    exceptional.Add(edge);
                else
                    normal.Add(edge);
            }

            normal.Sort((left, right) => CompareLayoutEdges(left, right, loopDepth));
            exceptional.Sort((left, right) => left.ToBlockId.CompareTo(right.ToBlockId));

            var ordered = ImmutableArray.CreateBuilder<int>(successors.Length);
            for (int i = 0; i < normal.Count; i++)
                ordered.Add(normal[i].ToBlockId);
            for (int i = 0; i < exceptional.Count; i++)
                ordered.Add(exceptional[i].ToBlockId);
            return ordered.ToImmutable();
        }

        private static int CompareLayoutEdges(CfgEdge left, CfgEdge right, int[] loopDepth)
        {
            int leftFallThrough = left.Kind == CfgEdgeKind.FallThrough ? 0 : 1;
            int rightFallThrough = right.Kind == CfgEdgeKind.FallThrough ? 0 : 1;
            if (leftFallThrough != rightFallThrough)
                return leftFallThrough.CompareTo(rightFallThrough);

            int leftDepth = (uint)left.ToBlockId < (uint)loopDepth.Length ? loopDepth[left.ToBlockId] : 0;
            int rightDepth = (uint)right.ToBlockId < (uint)loopDepth.Length ? loopDepth[right.ToBlockId] : 0;
            if (leftDepth != rightDepth)
                return rightDepth.CompareTo(leftDepth);

            return left.ToBlockId.CompareTo(right.ToBlockId);
        }

        public static ImmutableArray<int> Normalize(ControlFlowGraph cfg, ImmutableArray<int> order)
        {
            if (cfg is null)
                throw new ArgumentNullException(nameof(cfg));

            if (cfg.ExceptionRegions.Length != 0)
                return EhFuncletLayout.ComputeLinearBlockOrder(cfg);

            if (order.IsDefaultOrEmpty)
                return Compute(cfg);

            var result = ImmutableArray.CreateBuilder<int>(cfg.Blocks.Length);
            var seen = new bool[cfg.Blocks.Length];

            for (int i = 0; i < order.Length; i++)
            {
                int blockId = order[i];
                if ((uint)blockId >= (uint)cfg.Blocks.Length)
                    throw new InvalidOperationException($"GenTree LIR block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    throw new InvalidOperationException($"GenTree LIR block order contains duplicate block id B{blockId}.");
                seen[blockId] = true;
                result.Add(blockId);
            }

            for (int blockId = 0; blockId < cfg.Blocks.Length; blockId++)
            {
                if (!seen[blockId])
                    result.Add(blockId);
            }

            return result.ToImmutable();
        }
    }
    internal sealed class LinearRationalizationOptions
    {
        public static LinearRationalizationOptions Default => new LinearRationalizationOptions();

        public bool Validate { get; set; } = true;
    }
    internal static class GenTreeLinearIrRationalizer
    {
        public static GenTreeMethod RationalizeMethod(GenTreeMethod method, SsaMethod? ssa = null, LinearRationalizationOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= LinearRationalizationOptions.Default;

            ControlFlowGraph cfg;
            if (ssa is not null)
            {
                method = ssa.GenTreeMethod;
                if (method.Phase < GenTreeMethodPhase.Ssa)
                    throw new InvalidOperationException("Rationalization received SSA data that is not attached to the GenTree method.");
                cfg = ssa.Cfg;
            }
            else
            {
                if (method.Phase < GenTreeMethodPhase.HirLiveness)
                    throw new InvalidOperationException("Rationalization requires HIR liveness or SSA. Run GenTreeBackendPipeline.PrepareHir before HIR->LIR rationalization.");
                cfg = method.Cfg;
            }

            var builder = new MethodBuilder(method, cfg, ssa);
            return builder.Run();
        }

        private sealed class MethodBuilder
        {
            private readonly GenTreeMethod _method;
            private readonly ControlFlowGraph _cfg;
            private readonly SsaMethod? _ssa;
            private readonly Dictionary<GenTreeValueKey, GenTreeValueInfo> _valueInfos = new();
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _ssaSlotInfos = new();
            private readonly Dictionary<SsaValueName, SsaValueDefinition> _ssaDefinitions = new();
            private readonly Dictionary<SsaValueName, GenTree> _ssaValues = new();
            private readonly HashSet<SsaValueName> _usedSsaValues = new();
            private readonly List<GenTree> _allNodes = new();
            private readonly List<GenTree>[] _nodesByBlock;
            private int _nextNodeId;
            private int _nextSyntheticTreeId;
            private int _currentBlockId;
            private int _currentBlockOrdinal;

            public MethodBuilder(GenTreeMethod method, ControlFlowGraph cfg, SsaMethod? ssa = null)
            {
                _method = method;
                _cfg = cfg;
                _ssa = ssa;
                _nodesByBlock = new List<GenTree>[method.Blocks.Length];
                for (int i = 0; i < _nodesByBlock.Length; i++)
                    _nodesByBlock[i] = new List<GenTree>();
                _nextSyntheticTreeId = ComputeNextSyntheticTreeId(method);
                if (ssa is not null)
                    _nextSyntheticTreeId = Math.Max(_nextSyntheticTreeId, ComputeNextSyntheticTreeId(ssa));

                if (ssa is not null)
                {
                    for (int i = 0; i < ssa.Slots.Length; i++)
                        _ssaSlotInfos[ssa.Slots[i].Slot] = ssa.Slots[i];
                    for (int i = 0; i < ssa.ValueDefinitions.Length; i++)
                        _ssaDefinitions[ssa.ValueDefinitions[i].Name] = ssa.ValueDefinitions[i];
                }
            }

            public GenTreeMethod Run()
            {
                ResetExistingLinearState();
                if (_ssa is not null)
                    PrepareSsaValues();

                LowerBlocks();

                if (_ssa is not null)
                    EmitPhiCopies();

                return Freeze();
            }

            private static int ComputeNextSyntheticTreeId(GenTreeMethod method)
            {
                int max = -1;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Visit(statements[s]);
                }
                return max + 1;

                void Visit(GenTree node)
                {
                    if (node.Id > max)
                        max = node.Id;
                    for (int i = 0; i < node.Operands.Length; i++)
                        Visit(node.Operands[i]);
                }
            }

            private static int ComputeNextSyntheticTreeId(SsaMethod method)
            {
                int max = -1;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Visit(statements[s]);
                }
                return max + 1;

                void Visit(SsaTree tree)
                {
                    if (tree.Source.Id > max)
                        max = tree.Source.Id;
                    for (int i = 0; i < tree.Operands.Length; i++)
                        Visit(tree.Operands[i]);
                }
            }

            private void ResetExistingLinearState()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var statements = _method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        Reset(statements[s]);
                }

                static void Reset(GenTree node)
                {
                    node.ResetLinearState();
                    node.ClearSsaAnnotation();
                    for (int i = 0; i < node.Operands.Length; i++)
                        Reset(node.Operands[i]);
                }
            }

            private void PrepareSsaValues()
            {
                if (_ssa is null)
                    return;

                CollectUsedSsaValues();

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        if (IsSsaValueDemanded(phi.Target) && !_ssaValues.ContainsKey(phi.Target))
                            _ssaValues[phi.Target] = CreateSsaPlaceholderValueNode(phi.Target);
                    }

                    for (int s = 0; s < block.Statements.Length; s++)
                        AttachSsaAnnotations(block.Statements[s]);
                }
            }

            private void CollectUsedSsaValues()
            {
                if (_ssa is null)
                    return;

                var phiByTarget = new Dictionary<SsaValueName, SsaPhi>();
                var storeByTarget = new Dictionary<SsaValueName, SsaTree>();
                var work = new Queue<SsaValueName>();

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                        phiByTarget[block.Phis[p].Target] = block.Phis[p];

                    for (int s = 0; s < block.Statements.Length; s++)
                        CollectSsaStoreDefinitions(block.Statements[s], storeByTarget);
                }

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var statement = block.Statements[s];
                        if (statement.StoreTarget.HasValue)
                        {
                            if (StoreRhsHasObservableSsaEffect(statement))
                                MarkSsaUses(statement, work, includeStoreTarget: false);
                        }
                        else if (HasObservableSsaEffect(statement))
                        {
                            MarkSsaUses(statement, work, includeStoreTarget: false);
                        }
                    }
                }

                while (work.Count != 0)
                {
                    var value = work.Dequeue();

                    if (storeByTarget.TryGetValue(value, out var definingStore))
                    {
                        MarkSsaUses(definingStore, work, includeStoreTarget: false);
                        continue;
                    }

                    if (phiByTarget.TryGetValue(value, out var phi))
                    {
                        for (int i = 0; i < phi.Inputs.Length; i++)
                            MarkUsedSsaValue(phi.Inputs[i].Value, work);
                    }
                }
            }

            private void CollectSsaStoreDefinitions(SsaTree tree, Dictionary<SsaValueName, SsaTree> storeByTarget)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    CollectSsaStoreDefinitions(tree.Operands[i], storeByTarget);

                if (tree.StoreTarget.HasValue)
                    storeByTarget[tree.StoreTarget.Value] = tree;
            }

            private void MarkSsaUses(SsaTree tree, Queue<SsaValueName> work, bool includeStoreTarget)
            {
                if (tree.Value.HasValue)
                    MarkUsedSsaValue(tree.Value.Value, work);

                if (tree.LocalFieldBaseValue.HasValue)
                    MarkUsedSsaValue(tree.LocalFieldBaseValue.Value, work);

                if (includeStoreTarget && tree.StoreTarget.HasValue)
                    MarkUsedSsaValue(tree.StoreTarget.Value, work);

                for (int i = 0; i < tree.Operands.Length; i++)
                    MarkSsaUses(tree.Operands[i], work, includeStoreTarget: true);
            }

            private void MarkUsedSsaValue(SsaValueName value, Queue<SsaValueName> work)
            {
                if (_usedSsaValues.Add(value))
                    work.Enqueue(value);
            }

            private bool StoreRhsHasObservableSsaEffect(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableSsaEffect(tree.Operands[i]))
                        return true;
                }

                return false;
            }

            private bool HasObservableSsaEffect(SsaTree tree)
            {
                if (tree.HasMemoryEffects)
                    return true;

                if (tree.Value.HasValue)
                    return false;

                if (tree.StoreTarget.HasValue)
                    return StoreRhsHasObservableSsaEffect(tree);

                switch (tree.Kind)
                {
                    case GenTreeKind.ConstI4:
                    case GenTreeKind.ConstI8:
                    case GenTreeKind.ConstR4Bits:
                    case GenTreeKind.ConstR8Bits:
                    case GenTreeKind.ConstNull:
                    case GenTreeKind.ConstString:
                    case GenTreeKind.DefaultValue:
                    case GenTreeKind.SizeOf:
                    case GenTreeKind.Local:
                    case GenTreeKind.Arg:
                    case GenTreeKind.Temp:
                    case GenTreeKind.TempAddr:
                        return false;
                }

                if (MustMaterializeForSideEffects(tree.Source))
                    return true;

                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    if (HasObservableSsaEffect(tree.Operands[i]))
                        return true;
                }

                return false;
            }

            private void AttachSsaAnnotations(SsaTree tree)
            {
                if (tree.Value.HasValue)
                {
                    tree.Source.AttachSsaUse(tree.Value.Value);
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    AttachSsaAnnotations(tree.Operands[i]);

                if (tree.StoreTarget.HasValue)
                {
                    var target = tree.StoreTarget.Value;
                    if (IsSsaValueDemanded(target))
                    {
                        var info = GetSsaSlotInfo(target.Slot);
                        tree.Source.AttachSsaDefinition(target, info.Type, info.StackKind);
                        AttachSsaDescriptor(tree.Source, target.Slot);
                        _ssaValues[target] = tree.Source;
                        EnsureSsaValueInfo(target, tree.Source);
                    }
                }
            }

            private SsaSlotInfo GetSsaSlotInfo(SsaSlot slot)
            {
                if (_ssaSlotInfos.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(slot, type: null, stackKind: GenStackKind.Unknown, addressExposed: false);
            }

            private bool IsSsaValueDemanded(SsaValueName value)
                => _ssa is null || _usedSsaValues.Contains(value);

            private bool TryGetSsaDescriptor(SsaSlot slot, out GenLocalDescriptor descriptor)
            {
                if (slot.HasLclNum)
                {
                    var all = _method.AllLocalDescriptors;
                    if ((uint)slot.LclNum < (uint)all.Length)
                    {
                        descriptor = all[slot.LclNum];
                        return descriptor.Kind switch
                        {
                            GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg,
                            GenLocalKind.Local => slot.Kind == SsaSlotKind.Local,
                            GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp,
                            _ => false,
                        };
                    }
                }

                switch (slot.Kind)
                {
                    case SsaSlotKind.Arg:
                        if ((uint)slot.Index < (uint)_method.ArgDescriptors.Length)
                        {
                            descriptor = _method.ArgDescriptors[slot.Index];
                            return true;
                        }
                        break;

                    case SsaSlotKind.Local:
                        if ((uint)slot.Index < (uint)_method.LocalDescriptors.Length)
                        {
                            descriptor = _method.LocalDescriptors[slot.Index];
                            return true;
                        }
                        break;

                    case SsaSlotKind.Temp:
                        for (int i = 0; i < _method.TempDescriptors.Length; i++)
                        {
                            if (_method.TempDescriptors[i].Index == slot.Index)
                            {
                                descriptor = _method.TempDescriptors[i];
                                return true;
                            }
                        }
                        break;
                }

                descriptor = null!;
                return false;
            }

            private void AttachSsaDescriptor(GenTree node, SsaSlot slot)
            {
                if (node.LocalDescriptor is null && TryGetSsaDescriptor(slot, out var descriptor))
                    node.LocalDescriptor = descriptor;
            }

            private GenTree CreateSsaInitialValueNode(SsaValueName value)
            {
                var info = GetSsaSlotInfo(value.Slot);
                var kind = ToLoadKind(value.Slot.Kind);
                var node = new GenTree(
                    _nextSyntheticTreeId++,
                    kind,
                    pc: -1,
                    BytecodeOp.Nop,
                    info.Type,
                    info.StackKind,
                    GenTreeFlags.LocalUse | GenTreeFlags.Ordered,
                    ImmutableArray<GenTree>.Empty,
                    int32: value.Slot.Index);
                node.AttachSsaUse(value);
                AttachSsaDescriptor(node, value.Slot);
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private GenTree CreateSsaPlaceholderValueNode(SsaValueName value)
            {
                var info = GetSsaSlotInfo(value.Slot);
                var node = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Nop,
                    pc: -1,
                    BytecodeOp.Nop,
                    info.Type,
                    info.StackKind,
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty);
                node.AttachSsaDefinition(value, info.Type, info.StackKind);
                AttachSsaDescriptor(node, value.Slot);
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private static GenTreeKind ToLoadKind(SsaSlotKind kind)
                => kind switch
                {
                    SsaSlotKind.Arg => GenTreeKind.Arg,
                    SsaSlotKind.Local => GenTreeKind.Local,
                    SsaSlotKind.Temp => GenTreeKind.Temp,
                    _ => GenTreeKind.Nop,
                };

            private GenTree GetOrCreateSsaValue(SsaValueName value, GenTree? suggestedNode = null)
            {
                if (_ssaValues.TryGetValue(value, out var existing))
                {
                    EnsureSsaValueInfo(value, existing);
                    return existing;
                }

                GenTree node;
                if (suggestedNode is not null)
                {
                    var info = GetSsaSlotInfo(value.Slot);
                    suggestedNode.AttachSsaUse(value);
                    suggestedNode.Type = info.Type;
                    suggestedNode.StackKind = info.StackKind;
                    AttachSsaDescriptor(suggestedNode, value.Slot);
                    node = suggestedNode;
                }
                else if (_ssaDefinitions.TryGetValue(value, out var definition) && definition.IsInitial)
                {
                    node = CreateSsaInitialValueNode(value);
                }
                else if (_ssaDefinitions.TryGetValue(value, out definition) && definition.IsPhi)
                {
                    node = CreateSsaPlaceholderValueNode(value);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"SSA value {value} has no lowered defining node.");
                }

                _ssaValues[value] = node;
                EnsureSsaValueInfo(value, node);
                return node;
            }

            private void EnsureSsaValueInfo(SsaValueName value, GenTree representative)
            {
                var key = GenTreeValueKey.ForSsaValue(value);
                if (_valueInfos.ContainsKey(key))
                    return;

                var slotInfo = GetSsaSlotInfo(value.Slot);
                int defBlock = -1;
                int defNode = -1;
                if (_ssaDefinitions.TryGetValue(value, out var definition))
                {
                    defBlock = definition.DefBlockId;
                    defNode = definition.DefTreeId;
                }

                _valueInfos.Add(key, new GenTreeValueInfo(
                    key,
                    representative,
                    slotInfo.Type,
                    slotInfo.StackKind,
                    ResolveRegisterClass(slotInfo.Type, slotInfo.StackKind),
                    defBlock,
                    defNode));
            }

            private static RegisterClass ResolveRegisterClass(RuntimeType? type, GenStackKind stackKind)
            {
                AbiValueInfo abi = MachineAbi.ClassifyStorageValue(type, stackKind);
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister && abi.RegisterClass != RegisterClass.Invalid)
                    return abi.RegisterClass;

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    if (segments.Length != 0)
                    {
                        RegisterClass cls = segments[0].RegisterClass;
                        bool homogeneous = cls != RegisterClass.Invalid;
                        for (int i = 1; i < segments.Length; i++)
                        {
                            if (segments[i].RegisterClass != cls)
                            {
                                homogeneous = false;
                                break;
                            }
                        }
                        if (homogeneous)
                            return cls;
                    }
                }

                return stackKind is GenStackKind.R4 or GenStackKind.R8
                    ? RegisterClass.Float
                    : RegisterClass.General;
            }

            private void LowerBlocks()
            {
                if (_ssa is null)
                {
                    LowerGenTreeBlocks();
                    return;
                }

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    _currentBlockId = block.Id;
                    _currentBlockOrdinal = _nodesByBlock[_currentBlockId].Count;

                    for (int s = 0; s < block.Statements.Length; s++)
                        LowerStatement(block.Statements[s]);
                }
            }

            private void LowerGenTreeBlocks()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var block = _method.Blocks[b];
                    _currentBlockId = block.Id;
                    _currentBlockOrdinal = _nodesByBlock[_currentBlockId].Count;

                    for (int s = 0; s < block.Statements.Length; s++)
                        LowerStatement(block.Statements[s]);
                }
            }

            private void LowerStatement(GenTree tree)
            {
                if (tree is null)
                    throw new ArgumentNullException(nameof(tree));

                if (tree.Kind == GenTreeKind.Eval)
                {
                    LowerEval(tree);
                    return;
                }

                if (IsControlTransfer(tree))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (MustMaterializeForSideEffects(tree))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                LowerForSideEffects(tree);
            }

            private void LowerStatement(SsaTree tree)
            {
                if (tree is null)
                    throw new ArgumentNullException(nameof(tree));

                if (tree.Value.HasValue)
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                if (tree.Source.Kind == GenTreeKind.Eval)
                {
                    LowerEval(tree);
                    return;
                }

                if (IsControlTransfer(tree.Source))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (tree.StoreTarget.HasValue && !IsSsaValueDemanded(tree.StoreTarget.Value))
                {
                    LowerDeadSsaDefinitionForSideEffects(tree);
                    return;
                }

                if (tree.StoreTarget.HasValue || MustMaterializeForSideEffects(tree.Source))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                LowerForSideEffects(tree);
            }

            private void LowerEval(GenTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerEval(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerForSideEffects(GenTree tree)
            {
                if (IsControlTransfer(tree))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (MustMaterializeForSideEffects(tree))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerForSideEffects(SsaTree tree)
            {
                if (tree.Value.HasValue)
                    return;

                if (tree.StoreTarget.HasValue && !IsSsaValueDemanded(tree.StoreTarget.Value))
                {
                    LowerDeadSsaDefinitionForSideEffects(tree);
                    return;
                }

                if (IsControlTransfer(tree.Source))
                {
                    LowerControlTransfer(tree);
                    return;
                }

                if (tree.StoreTarget.HasValue || MustMaterializeForSideEffects(tree.Source))
                {
                    _ = LowerValueOrVoid(tree);
                    return;
                }

                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private void LowerDeadSsaDefinitionForSideEffects(SsaTree tree)
            {
                if (!MustMaterializeDeadSsaDefinitionSource(tree.Source))
                {
                    LowerOperandsForSideEffectsOnly(tree);
                    return;
                }

                var uses = LowerOperands(tree);

                GenTree? result = RequiresCodegenResultForDeadDefinition(tree.Source)
                    ? NewTemp(tree.Source)
                    : null;

                EmitTree(tree.Source, uses, result);
            }

            private void LowerOperandsForSideEffectsOnly(SsaTree tree)
            {
                for (int i = 0; i < tree.Operands.Length; i++)
                    LowerForSideEffects(tree.Operands[i]);
            }

            private static bool MustMaterializeDeadSsaDefinitionSource(GenTree tree)
            {
                if (!MustMaterializeForSideEffects(tree))
                    return false;

                return tree.Kind is not (
                    GenTreeKind.StoreLocal or
                    GenTreeKind.StoreArg or
                    GenTreeKind.StoreTemp or
                    GenTreeKind.StoreField or
                    GenTreeKind.StoreStaticField or
                    GenTreeKind.StoreIndirect or
                    GenTreeKind.StoreArrayElement);
            }

            private static bool RequiresCodegenResultForDeadDefinition(GenTree tree)
            {
                if (!ProducesValue(tree))
                    return false;

                if (tree.Kind is GenTreeKind.Call or GenTreeKind.VirtualCall or GenTreeKind.DelegateInvoke)
                {
                    var method = tree.Method;
                    if (method is null)
                        return true;

                    var returnKind = MachineAbi.StackKindForType(method.ReturnType);
                    var returnAbi = MachineAbi.ClassifyValue(method.ReturnType, returnKind, isReturn: true);
                    return returnAbi.PassingKind is AbiValuePassingKind.Indirect or AbiValuePassingKind.Stack;
                }

                return true;
            }

            private static bool MustMaterializeForSideEffects(GenTree tree)
            {
                if (tree.HasSideEffect || tree.CanThrow || tree.ContainsCall)
                    return true;

                if ((tree.Flags & (GenTreeFlags.MemoryWrite | GenTreeFlags.Allocation | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow | GenTreeFlags.Ordered)) != 0)
                    return true;

                return tree.Kind is
                    GenTreeKind.Call or
                    GenTreeKind.VirtualCall or
                    GenTreeKind.DelegateInvoke or
                    GenTreeKind.NewObject or
                    GenTreeKind.NewDelegate or
                    GenTreeKind.DelegateCombine or
                    GenTreeKind.DelegateRemove or
                    GenTreeKind.NewArray or
                    GenTreeKind.StoreIndirect or
                    GenTreeKind.StoreLocal or
                    GenTreeKind.StoreArg or
                    GenTreeKind.StoreTemp or
                    GenTreeKind.StoreField or
                    GenTreeKind.StoreStaticField or
                    GenTreeKind.StoreArrayElement or
                    GenTreeKind.Throw or
                    GenTreeKind.Rethrow or
                    GenTreeKind.EndFinally;
            }

            private void LowerControlTransfer(GenTree tree)
            {
                if (TryLowerContainedLocalLikeMultiRegisterReturn(tree))
                    return;

                if (TryLowerBooleanBranch(tree))
                    return;

                var uses = LowerOperands(tree);

                if (IsBackwardBranch(tree))
                    EmitGcPoll(tree);

                GenTree? result = ProducesValue(tree)
                    ? NewTemp(tree)
                    : null;

                EmitTree(tree, uses, result);
            }

            private bool TryLowerContainedLocalLikeMultiRegisterReturn(GenTree tree)
            {
                if (tree.Kind != GenTreeKind.Return || tree.Operands.Length != 1)
                    return false;

                var value = tree.Operands[0];
                if (!IsDirectReturnableLocalLike(value))
                    return false;

                RuntimeType? returnType = value.LocalDescriptor?.Type ?? value.RuntimeType ?? value.Type;
                if (returnType is null || !returnType.IsValueType)
                    return false;

                if (MachineAbi.RequiresHiddenReturnBuffer(_method.RuntimeMethod))
                    return false;

                var returnKind = value.LocalDescriptor?.StackKind ?? MachineAbi.StackKindForType(returnType);
                var abi = MachineAbi.ClassifyValue(returnType, returnKind, isReturn: true);
                if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
                    return false;

                value.IsContainedInLinear = true;
                EmitTree(tree, ImmutableArray.Create(LirOperandFlags.Contained), result: null);
                return true;
            }

            private static bool IsDirectReturnableLocalLike(GenTree value)
                => value.Kind is GenTreeKind.Local or GenTreeKind.Temp;

            private readonly struct LoweredBranchCondition
            {
                public readonly bool BranchWhenTrue;
                public readonly BytecodeOp CompareOp;
                public readonly GenTree? GenTreeValue;
                public readonly GenTree? GenTreeLeft;
                public readonly GenTree? GenTreeRight;
                public readonly SsaTree? SsaValue;
                public readonly SsaTree? SsaLeft;
                public readonly SsaTree? SsaRight;

                private LoweredBranchCondition(
                    bool branchWhenTrue,
                    BytecodeOp compareOp,
                    GenTree? genTreeValue,
                    GenTree? genTreeLeft,
                    GenTree? genTreeRight,
                    SsaTree? ssaValue,
                    SsaTree? ssaLeft,
                    SsaTree? ssaRight)
                {
                    BranchWhenTrue = branchWhenTrue;
                    CompareOp = compareOp;
                    GenTreeValue = genTreeValue;
                    GenTreeLeft = genTreeLeft;
                    GenTreeRight = genTreeRight;
                    SsaValue = ssaValue;
                    SsaLeft = ssaLeft;
                    SsaRight = ssaRight;
                }

                public bool IsCompare => CompareOp != BytecodeOp.Nop;

                public static LoweredBranchCondition Truth(GenTree value, bool branchWhenTrue)
                    => new LoweredBranchCondition(branchWhenTrue, BytecodeOp.Nop, value, null, null, null, null, null);

                public static LoweredBranchCondition Truth(SsaTree value, bool branchWhenTrue)
                    => new LoweredBranchCondition(branchWhenTrue, BytecodeOp.Nop, null, null, null, value, null, null);

                public static LoweredBranchCondition Compare(BytecodeOp compareOp, GenTree left, GenTree right, bool branchWhenTrue)
                    => new LoweredBranchCondition(branchWhenTrue, compareOp, null, left, right, null, null, null);

                public static LoweredBranchCondition Compare(BytecodeOp compareOp, SsaTree left, SsaTree right, bool branchWhenTrue)
                    => new LoweredBranchCondition(branchWhenTrue, compareOp, null, null, null, null, left, right);
            }

            private bool TryLowerBooleanBranch(GenTree branch)
            {
                if (branch.Kind is not (GenTreeKind.BranchTrue or GenTreeKind.BranchFalse) || branch.Operands.Length != 1)
                    return false;

                bool branchWhenTrue = branch.Kind == GenTreeKind.BranchTrue;
                if (!TryReduceBooleanBranchCondition(branch.Operands[0], branchWhenTrue, allowTruthValue: false, out var condition))
                    return false;

                if (condition.IsCompare)
                {
                    var left = condition.GenTreeLeft ?? throw new InvalidOperationException("Lowered compare branch is missing its left operand.");
                    var right = condition.GenTreeRight ?? throw new InvalidOperationException("Lowered compare branch is missing its right operand.");
                    if (!CanEmitDirectCompareBranch(condition.CompareOp, left, right, condition.BranchWhenTrue))
                        return false;

                    left.RegisterResult = LowerValue(left);
                    right.RegisterResult = LowerValue(right);
                    branch.Kind = condition.BranchWhenTrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse;
                    branch.SourceOp = condition.CompareOp;
                    branch.SetOperands(ImmutableArray.Create(left, right));

                    if (IsBackwardBranch(branch))
                        EmitGcPoll(branch);

                    EmitTree(branch, ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.None), result: null);
                    return true;
                }

                var value = condition.GenTreeValue ?? throw new InvalidOperationException("Lowered truth branch is missing its operand.");
                value.RegisterResult = LowerValue(value);
                branch.Kind = condition.BranchWhenTrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse;
                branch.SourceOp = condition.BranchWhenTrue ? BytecodeOp.Brtrue : BytecodeOp.Brfalse;
                branch.SetOperands(ImmutableArray.Create(value));

                if (IsBackwardBranch(branch))
                    EmitGcPoll(branch);

                EmitTree(branch, ImmutableArray.Create(LirOperandFlags.None), result: null);
                return true;
            }

            private bool TryLowerBooleanBranch(SsaTree branchTree)
            {
                var branch = branchTree.Source;
                if (branch.Kind is not (GenTreeKind.BranchTrue or GenTreeKind.BranchFalse) || branchTree.Operands.Length != 1)
                    return false;

                bool branchWhenTrue = branch.Kind == GenTreeKind.BranchTrue;
                if (!TryReduceBooleanBranchCondition(branchTree.Operands[0], branchWhenTrue, allowTruthValue: false, out var condition))
                    return false;

                if (condition.IsCompare)
                {
                    var left = condition.SsaLeft ?? throw new InvalidOperationException("Lowered SSA compare branch is missing its left operand.");
                    var right = condition.SsaRight ?? throw new InvalidOperationException("Lowered SSA compare branch is missing its right operand.");
                    if (!CanEmitDirectCompareBranch(condition.CompareOp, left.Source, right.Source, condition.BranchWhenTrue))
                        return false;

                    left.Source.RegisterResult = LowerValue(left);
                    right.Source.RegisterResult = LowerValue(right);
                    branch.Kind = condition.BranchWhenTrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse;
                    branch.SourceOp = condition.CompareOp;
                    branch.SetOperands(ImmutableArray.Create(left.Source, right.Source));

                    if (IsBackwardBranch(branch))
                        EmitGcPoll(branch);

                    EmitTree(branch, ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.None), result: null);
                    return true;
                }

                var value = condition.SsaValue ?? throw new InvalidOperationException("Lowered SSA truth branch is missing its operand.");
                value.Source.RegisterResult = LowerValue(value);
                branch.Kind = condition.BranchWhenTrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse;
                branch.SourceOp = condition.BranchWhenTrue ? BytecodeOp.Brtrue : BytecodeOp.Brfalse;
                branch.SetOperands(ImmutableArray.Create(value.Source));

                if (IsBackwardBranch(branch))
                    EmitGcPoll(branch);

                EmitTree(branch, ImmutableArray.Create(LirOperandFlags.None), result: null);
                return true;
            }

            private static bool TryReduceBooleanBranchCondition(
                GenTree condition,
                bool branchWhenTrue,
                bool allowTruthValue,
                out LoweredBranchCondition lowered)
            {
                if (condition.Kind == GenTreeKind.Binary && IsCompareOp(condition.SourceOp) && condition.Operands.Length == 2)
                {
                    var left = condition.Operands[0];
                    var right = condition.Operands[1];
                    if (condition.SourceOp == BytecodeOp.Ceq && TryGetIntegralZeroCompareOperand(left, right, out var comparedWithZero))
                        return TryReduceBooleanBranchCondition(comparedWithZero, !branchWhenTrue, allowTruthValue: true, out lowered);

                    if (condition.SourceOp == BytecodeOp.Ceq && TryGetBooleanOneCompareOperand(left, right, out var comparedWithOne))
                        return TryReduceBooleanBranchCondition(comparedWithOne, branchWhenTrue, allowTruthValue: true, out lowered);

                    if (IsUnsignedNonZeroTest(condition.SourceOp, left, right, out comparedWithZero))
                        return TryReduceBooleanBranchCondition(comparedWithZero, branchWhenTrue, allowTruthValue: true, out lowered);

                    lowered = LoweredBranchCondition.Compare(condition.SourceOp, left, right, branchWhenTrue);
                    return true;
                }

                if (allowTruthValue && ProducesValue(condition))
                {
                    lowered = LoweredBranchCondition.Truth(condition, branchWhenTrue);
                    return true;
                }

                lowered = default;
                return false;
            }

            private static bool TryReduceBooleanBranchCondition(
                SsaTree condition,
                bool branchWhenTrue,
                bool allowTruthValue,
                out LoweredBranchCondition lowered)
            {
                if (condition.Source.Kind == GenTreeKind.Binary && IsCompareOp(condition.Source.SourceOp) && condition.Operands.Length == 2)
                {
                    var left = condition.Operands[0];
                    var right = condition.Operands[1];
                    if (condition.Source.SourceOp == BytecodeOp.Ceq && TryGetIntegralZeroCompareOperand(left.Source, right.Source, out var comparedWithZero))
                    {
                        SsaTree next = ReferenceEquals(comparedWithZero, left.Source) ? left : right;
                        return TryReduceBooleanBranchCondition(next, !branchWhenTrue, allowTruthValue: true, out lowered);
                    }

                    if (condition.Source.SourceOp == BytecodeOp.Ceq && TryGetBooleanOneCompareOperand(left.Source, right.Source, out var comparedWithOne))
                    {
                        SsaTree next = ReferenceEquals(comparedWithOne, left.Source) ? left : right;
                        return TryReduceBooleanBranchCondition(next, branchWhenTrue, allowTruthValue: true, out lowered);
                    }

                    if (IsUnsignedNonZeroTest(condition.Source.SourceOp, left.Source, right.Source, out comparedWithZero))
                    {
                        SsaTree next = ReferenceEquals(comparedWithZero, left.Source) ? left : right;
                        return TryReduceBooleanBranchCondition(next, branchWhenTrue, allowTruthValue: true, out lowered);
                    }

                    lowered = LoweredBranchCondition.Compare(condition.Source.SourceOp, left, right, branchWhenTrue);
                    return true;
                }

                if (allowTruthValue && ProducesValue(condition.Source))
                {
                    lowered = LoweredBranchCondition.Truth(condition, branchWhenTrue);
                    return true;
                }

                lowered = default;
                return false;
            }

            private static bool TryGetIntegralZeroCompareOperand(GenTree left, GenTree right, out GenTree value)
            {
                if (IsIntegralZero(right))
                {
                    value = left;
                    return true;
                }

                if (IsIntegralZero(left))
                {
                    value = right;
                    return true;
                }

                value = null!;
                return false;
            }

            private static bool TryGetBooleanOneCompareOperand(GenTree left, GenTree right, out GenTree value)
            {
                if (IsIntegralOne(right) && IsBooleanBranchValue(left))
                {
                    value = left;
                    return true;
                }

                if (IsIntegralOne(left) && IsBooleanBranchValue(right))
                {
                    value = right;
                    return true;
                }

                value = null!;
                return false;
            }

            private static bool IsBooleanBranchValue(GenTree value)
            {
                RuntimeType? type = value.LocalDescriptor?.Type ?? value.RuntimeType ?? value.Type;
                return (value.Kind == GenTreeKind.Binary && IsCompareOp(value.SourceOp)) ||
                       type?.PrimitiveKind == RuntimePrimitiveKind.Boolean;
            }

            private static bool IsUnsignedNonZeroTest(BytecodeOp op, GenTree left, GenTree right, out GenTree value)
            {
                if (op == BytecodeOp.Cgt_Un && IsIntegralZero(right) && CanUseTruthinessForUnsignedNonZero(left))
                {
                    value = left;
                    return true;
                }

                if (op == BytecodeOp.Clt_Un && IsIntegralZero(left) && CanUseTruthinessForUnsignedNonZero(right))
                {
                    value = right;
                    return true;
                }

                value = null!;
                return false;
            }

            private static bool CanUseTruthinessForUnsignedNonZero(GenTree value)
                => !IsFloatLike(value.Type, value.StackKind) && value.StackKind != GenStackKind.Value && value.StackKind != GenStackKind.Void;

            private static bool IsIntegralZero(GenTree value)
            {
                if (value.Operands.Length != 0)
                    return false;

                return value.Kind switch
                {
                    GenTreeKind.ConstI4 => value.Int32 == 0,
                    GenTreeKind.ConstI8 => value.Int64 == 0,
                    GenTreeKind.ConstNull => true,
                    _ => false,
                };
            }

            private static bool IsIntegralOne(GenTree value)
            {
                if (value.Operands.Length != 0)
                    return false;

                return value.Kind switch
                {
                    GenTreeKind.ConstI4 => value.Int32 == 1,
                    GenTreeKind.ConstI8 => value.Int64 == 1,
                    _ => false,
                };
            }

            private static bool CanEmitDirectCompareBranch(BytecodeOp op, GenTree left, GenTree right, bool branchWhenTrue)
            {
                if (!IsCompareOp(op))
                    return false;

                var kind = left.StackKind;
                var type = left.Type;
                bool f32 = IsFloatLike(type, kind) && (kind == GenStackKind.R4 || type?.Name == "Single");
                bool f64 = IsFloatLike(type, kind) && !f32;
                if (f32 || f64)
                    return op == BytecodeOp.Ceq || (branchWhenTrue && (op == BytecodeOp.Clt || op == BytecodeOp.Cgt));

                if (left.StackKind == GenStackKind.Value || right.StackKind == GenStackKind.Value)
                    return false;

                if (left.StackKind is GenStackKind.Ref or GenStackKind.Null or GenStackKind.ByRef ||
                    right.StackKind is GenStackKind.Ref or GenStackKind.Null or GenStackKind.ByRef)
                    return op == BytecodeOp.Ceq;

                return true;
            }

            private static bool IsCompareOp(BytecodeOp op)
                => op is BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt or BytecodeOp.Cgt_Un;

            private void LowerControlTransfer(SsaTree tree)
            {
                if (TryLowerContainedLocalLikeMultiRegisterReturn(tree.Source))
                    return;

                if (TryLowerBooleanBranch(tree))
                    return;

                var uses = LowerOperands(tree);

                if (IsBackwardBranch(tree.Source))
                    EmitGcPoll(tree.Source);

                GenTree? result = ProducesValue(tree.Source)
                    ? NewTemp(tree.Source)
                    : null;

                EmitTree(tree.Source, uses, result);
            }

            private ImmutableArray<LirOperandFlags> LowerOperands(GenTree tree)
            {
                if (TryLowerCommutativeBinaryOperands(tree, out var binaryOperands))
                    return binaryOperands;

                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var operandTree = tree.Operands[i];
                    if (TryCreateContainedOperand(tree, i, operandTree, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }

                    _ = LowerValueOrVoid(operandTree);
                    flags.Add(LirOperandFlags.None);
                }
                return flags.ToImmutable();
            }

            private ImmutableArray<LirOperandFlags> LowerOperands(SsaTree tree)
            {
                if (TryLowerCommutativeBinaryOperands(tree, out var binaryOperands))
                    return binaryOperands;

                if (tree.LocalFieldBaseValue.HasValue && SsaSlotHelpers.TryGetLocalFieldAccess(tree.Source, out var localFieldAccess))
                    return LowerLocalFieldOperands(tree, localFieldAccess);

                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(tree.Operands.Length);
                var sources = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var operandTree = tree.Operands[i];
                    sources.Add(operandTree.Source);
                    if (TryCreateContainedOperand(tree.Source, i, operandTree.Source, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }

                    var value = LowerValueOrVoid(operandTree);
                    if (value is not null)
                        operandTree.Source.RegisterResult = value;
                    flags.Add(LirOperandFlags.None);
                }

                tree.Source.SetOperands(sources.ToImmutable());
                return flags.ToImmutable();
            }

            private ImmutableArray<LirOperandFlags> LowerLocalFieldOperands(SsaTree tree, SsaLocalAccess localFieldAccess)
            {
                var originalOperands = tree.Source.Operands;
                var flags = ImmutableArray.CreateBuilder<LirOperandFlags>(originalOperands.Length);
                var sources = ImmutableArray.CreateBuilder<GenTree>(originalOperands.Length);

                for (int originalIndex = 0, ssaIndex = 0; originalIndex < originalOperands.Length; originalIndex++)
                {
                    if (originalIndex == localFieldAccess.ReceiverOperandIndex)
                    {
                        var receiver = originalOperands[originalIndex];
                        sources.Add(receiver);
                        var value = LowerValueOrVoid(receiver);
                        if (value is not null)
                            receiver.RegisterResult = value;
                        flags.Add(LirOperandFlags.None);
                        continue;
                    }

                    if ((uint)ssaIndex >= (uint)tree.Operands.Length)
                        throw new InvalidOperationException($"SSA local-field operand mapping is malformed for node {tree.Source.Id}.");

                    var operandTree = tree.Operands[ssaIndex++];
                    sources.Add(operandTree.Source);
                    if (TryCreateContainedOperand(tree.Source, originalIndex, operandTree.Source, out var containedFlags))
                    {
                        flags.Add(containedFlags);
                        continue;
                    }
                    {
                        var value = LowerValueOrVoid(operandTree);
                        if (value is not null)
                            operandTree.Source.RegisterResult = value;
                    }

                    flags.Add(LirOperandFlags.None);
                }

                if (sources.Count - 1 > tree.Operands.Length)
                    throw new InvalidOperationException($"SSA local-field operand mapping has too many source operands for node {tree.Source.Id}.");

                tree.Source.SetOperands(sources.ToImmutable());
                return flags.ToImmutable();
            }

            private bool TryLowerCommutativeBinaryOperands(GenTree tree, out ImmutableArray<LirOperandFlags> operands)
            {
                operands = default;

                if (tree.Kind != GenTreeKind.Binary || tree.Operands.Length != 2)
                    return false;

                if (!IsCommutativeBinaryImmediateOp(tree.SourceOp))
                    return false;

                var left = tree.Operands[0];
                var right = tree.Operands[1];

                if (!CanContainBinaryImmediate(tree, operandIndex: 1, left))
                    return false;

                if (CanContainBinaryImmediate(tree, operandIndex: 1, right))
                    return false;

                _ = LowerValue(right);
                left.IsContainedInLinear = true;
                tree.SetOperands(ImmutableArray.Create(right, left));
                operands = ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.Contained);
                return true;
            }

            private bool TryLowerCommutativeBinaryOperands(SsaTree tree, out ImmutableArray<LirOperandFlags> operands)
            {
                operands = default;

                if (tree.Source.Kind != GenTreeKind.Binary || tree.Operands.Length != 2)
                    return false;

                if (!IsCommutativeBinaryImmediateOp(tree.Source.SourceOp))
                    return false;

                var left = tree.Operands[0];
                var right = tree.Operands[1];

                if (!CanContainBinaryImmediate(tree.Source, operandIndex: 1, left.Source))
                    return false;

                if (CanContainBinaryImmediate(tree.Source, operandIndex: 1, right.Source))
                    return false;

                var rightValue = LowerValue(right);
                right.Source.RegisterResult = rightValue;
                left.Source.IsContainedInLinear = true;
                tree.Source.SetOperands(ImmutableArray.Create(right.Source, left.Source));
                operands = ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.Contained);
                return true;
            }

            private static bool TryCreateContainedOperand(GenTree parent, int operandIndex, GenTree operand, out LirOperandFlags result)
            {
                if (CanContainBinaryImmediate(parent, operandIndex, operand) ||
                    CanContainDefaultStoreValue(parent, operandIndex, operand))
                {
                    operand.IsContainedInLinear = true;
                    result = LirOperandFlags.Contained;
                    return true;
                }

                result = LirOperandFlags.None;
                return false;
            }

            private static bool CanContainDefaultStoreValue(GenTree parent, int operandIndex, GenTree operand)
            {
                if (operand.Kind != GenTreeKind.DefaultValue || operand.Operands.Length != 0)
                    return false;

                return parent.Kind switch
                {
                    GenTreeKind.StoreField => operandIndex == 1,
                    GenTreeKind.StoreStaticField => operandIndex == 0,
                    _ => false,
                };
            }

            private static bool CanContainBinaryImmediate(GenTree parent, int operandIndex, GenTree operand)
            {
                if (parent.Kind != GenTreeKind.Binary || operandIndex != 1)
                    return false;

                if (operand.Operands.Length != 0)
                    return false;

                if (operand.Kind is not (GenTreeKind.ConstI4 or GenTreeKind.ConstI8))
                    return false;

                if (IsFloatLike(parent.Type, parent.StackKind) || parent.StackKind is GenStackKind.Ref or GenStackKind.Null)
                    return false;

                if (parent.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                    return false;

                if (parent.SourceOp == BytecodeOp.Cgt_Un)
                    return false;

                return IsBinaryImmediateOp(parent.SourceOp);
            }

            private static bool IsBinaryImmediateOp(BytecodeOp op)
                => op is
                    BytecodeOp.Add or BytecodeOp.Sub or BytecodeOp.Mul or
                    BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or
                    BytecodeOp.Shl or BytecodeOp.Shr or BytecodeOp.Shr_Un or
                    BytecodeOp.Ceq or BytecodeOp.Clt or BytecodeOp.Clt_Un or BytecodeOp.Cgt;

            private static bool IsCommutativeBinaryImmediateOp(BytecodeOp op)
                => op is BytecodeOp.Add or BytecodeOp.Mul or BytecodeOp.And or BytecodeOp.Or or BytecodeOp.Xor or BytecodeOp.Ceq;

            private static bool IsFloatLike(RuntimeType? type, GenStackKind stackKind)
                => stackKind is GenStackKind.R4 or GenStackKind.R8 || type?.Name is "Single" or "Double";

            private GenTree LowerValue(GenTree tree)
            {
                var result = LowerValueOrVoid(tree);
                if (result is null)
                    throw new InvalidOperationException($"Tree node {tree.Id} ({tree.Kind}) does not produce a value.");
                return result;
            }

            private GenTree LowerValue(SsaTree tree)
            {
                var result = LowerValueOrVoid(tree);
                if (result is null)
                    throw new InvalidOperationException($"Tree node {tree.Source.Id} ({tree.Source.Kind}) does not produce a value.");
                return result;
            }

            private bool TryLowerBinaryStrengthReduction(GenTree tree, out GenTree? result)
            {
                result = null;
                if (!IsStrengthReductionCandidate(tree))
                    return false;

                if (TryLowerMulStrengthReduction(
                    tree,
                    tree.Operands[0],
                    tree.Operands[1],
                    operandIndex => LowerGenTreeOperandForLoweredTree(tree.Operands[operandIndex]),
                    null,
                    out result))
                    return true;

                if (TryLowerDivRemStrengthReduction(
                    tree,
                    tree.Operands[0],
                    tree.Operands[1],
                    operandIndex => LowerGenTreeOperandForLoweredTree(tree.Operands[operandIndex]),
                    null,
                    out result))
                    return true;

                return false;
            }

            private bool TryLowerBinaryStrengthReduction(SsaTree tree, out GenTree? result)
            {
                result = null;
                if (!IsStrengthReductionCandidate(tree.Source) || tree.Operands.Length != 2)
                    return false;

                if (TryLowerMulStrengthReduction(
                    tree.Source,
                    tree.Operands[0].Source,
                    tree.Operands[1].Source,
                    operandIndex => LowerSsaOperandForLoweredTree(tree.Operands[operandIndex]),
                    tree,
                    out result))
                    return true;

                if (TryLowerDivRemStrengthReduction(
                    tree.Source,
                    tree.Operands[0].Source,
                    tree.Operands[1].Source,
                    operandIndex => LowerSsaOperandForLoweredTree(tree.Operands[operandIndex]),
                    tree,
                    out result))
                    return true;

                return false;
            }

            private static bool IsStrengthReductionCandidate(GenTree source)
                => source.Kind == GenTreeKind.Binary &&
                   source.Operands.Length == 2 &&
                   GenTreeArithmeticSemantics.IsIntegralArithmeticType(source.Type, source.StackKind);

            private GenTree LowerGenTreeOperandForLoweredTree(GenTree operand)
            {
                var value = LowerValue(operand);
                operand.RegisterResult = value;
                return value;
            }

            private GenTree LowerSsaOperandForLoweredTree(SsaTree operand)
            {
                var value = LowerValue(operand);
                operand.Source.RegisterResult = value;
                return value;
            }

            private bool TryLowerMulStrengthReduction(
                GenTree template,
                GenTree leftSource,
                GenTree rightSource,
                Func<int, GenTree> lowerOperand,
                SsaTree? ssaTree,
                out GenTree? result)
            {
                result = null;
                if (template.SourceOp != BytecodeOp.Mul)
                    return false;

                int bits = GenTreeArithmeticSemantics.IntegralBits(template.Type, template.StackKind);
                bool rightConst = GenTreeArithmeticSemantics.TryGetIntegralConstant(rightSource, bits, out long rightSigned, out ulong rightUnsigned);
                bool leftConst = GenTreeArithmeticSemantics.TryGetIntegralConstant(leftSource, bits, out long leftSigned, out ulong leftUnsigned);

                if (rightConst && rightSigned == -1)
                {
                    GenTree value = lowerOperand(0);
                    result = EmitFinalUnaryLoweredTree(template, BytecodeOp.Neg, leftSource, value, ssaTree);
                    return true;
                }

                if (leftConst && leftSigned == -1)
                {
                    GenTree value = lowerOperand(1);
                    result = EmitFinalUnaryLoweredTree(template, BytecodeOp.Neg, rightSource, value, ssaTree);
                    return true;
                }

                if (rightConst && GenTreeArithmeticSemantics.TryGetUnsignedPowerOfTwoDivisor(rightUnsigned, bits, out int rightShift))
                {
                    GenTree value = lowerOperand(0);
                    result = EmitFinalBinaryImmediateLoweredTree(template, BytecodeOp.Shl, leftSource, value, rightShift, ssaTree);
                    return true;
                }

                if (leftConst && GenTreeArithmeticSemantics.TryGetUnsignedPowerOfTwoDivisor(leftUnsigned, bits, out int leftShift))
                {
                    GenTree value = lowerOperand(1);
                    result = EmitFinalBinaryImmediateLoweredTree(template, BytecodeOp.Shl, rightSource, value, leftShift, ssaTree);
                    return true;
                }

                return false;
            }

            private bool TryLowerDivRemStrengthReduction(
                GenTree template,
                GenTree dividendSource,
                GenTree divisorSource,
                Func<int, GenTree> lowerOperand,
                SsaTree? ssaTree,
                out GenTree? result)
            {
                result = null;
                if (template.SourceOp is not (BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un))
                    return false;

                int bits = GenTreeArithmeticSemantics.IntegralBits(template.Type, template.StackKind);
                if (!GenTreeArithmeticSemantics.TryGetIntegralConstant(divisorSource, bits, out _, out ulong unsignedDivisor))
                    return false;

                if (unsignedDivisor == 0)
                    return false;

                if (template.SourceOp == BytecodeOp.Div_Un)
                {
                    if (!GenTreeArithmeticSemantics.TryGetUnsignedPowerOfTwoDivisor(unsignedDivisor, bits, out int shift))
                        return false;

                    GenTree dividend = lowerOperand(0);
                    result = EmitFinalBinaryImmediateLoweredTree(template, BytecodeOp.Shr_Un, dividendSource, dividend, shift, ssaTree);
                    return true;
                }

                if (template.SourceOp == BytecodeOp.Rem_Un)
                {
                    if (!GenTreeArithmeticSemantics.TryGetUnsignedPowerOfTwoDivisor(unsignedDivisor, bits, out _))
                        return false;

                    ulong mask = MaskToWidth(unchecked(unsignedDivisor - 1), bits);
                    GenTree dividend = lowerOperand(0);
                    result = EmitFinalBinaryImmediateLoweredTree(template, BytecodeOp.And, dividendSource, dividend, mask, ssaTree);
                    return true;
                }

                return false;
            }

            private GenTree EmitFinalUnaryLoweredTree(GenTree template, BytecodeOp op, GenTree operandSource, GenTree operandValue, SsaTree? ssaTree)
            {
                operandSource.RegisterResult = operandValue;
                template.Kind = GenTreeKind.Unary;
                template.SourceOp = op;
                template.Flags = GenTreeFlags.None;
                template.SetOperands(ImmutableArray.Create(operandSource));
                return EmitFinalLoweredTree(template, ImmutableArray.Create(LirOperandFlags.None), ssaTree);
            }

            private GenTree EmitFinalBinaryImmediateLoweredTree(GenTree template, BytecodeOp op, GenTree leftSource, GenTree leftValue, long immediate, SsaTree? ssaTree)
                => EmitFinalBinaryImmediateLoweredTree(template, op, leftSource, leftValue, unchecked((ulong)immediate), ssaTree);

            private GenTree EmitFinalBinaryImmediateLoweredTree(GenTree template, BytecodeOp op, GenTree leftSource, GenTree leftValue, ulong immediate, SsaTree? ssaTree)
            {
                leftSource.RegisterResult = leftValue;
                var constant = CreateContainedIntegerConstant(template, immediate);
                template.Kind = GenTreeKind.Binary;
                template.SourceOp = op;
                template.Flags = GenTreeFlags.None;
                template.SetOperands(ImmutableArray.Create(leftSource, constant));
                return EmitFinalLoweredTree(template, ImmutableArray.Create(LirOperandFlags.None, LirOperandFlags.Contained), ssaTree);
            }

            private GenTree EmitFinalLoweredTree(GenTree finalNode, ImmutableArray<LirOperandFlags> operandFlags, SsaTree? ssaTree)
            {
                GenTree result;
                if (ssaTree is not null && ssaTree.StoreTarget.HasValue)
                {
                    var targetName = ssaTree.StoreTarget.Value;
                    result = GetOrCreateSsaValue(targetName, finalNode);
                    var info = GetSsaSlotInfo(targetName.Slot);
                    finalNode.AttachSsaDefinition(targetName, info.Type, info.StackKind);
                    finalNode.RegisterResult = result;
                }
                else
                {
                    result = NewTemp(finalNode);
                }

                EmitTree(finalNode, operandFlags, result);
                return result;
            }

            private GenTree CreateContainedIntegerConstant(GenTree template, ulong value)
            {
                int bits = GenTreeArithmeticSemantics.IntegralBits(template.Type, template.StackKind);
                GenTree node = bits <= 32
                    ? new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI4,
                        template.Pc,
                        BytecodeOp.Ldc_I4,
                        type: null,
                        stackKind: GenStackKind.I4,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int32: unchecked((int)(uint)value))
                    : new GenTree(
                        _nextSyntheticTreeId++,
                        GenTreeKind.ConstI8,
                        template.Pc,
                        BytecodeOp.Ldc_I8,
                        type: null,
                        stackKind: GenStackKind.I8,
                        flags: GenTreeFlags.None,
                        operands: ImmutableArray<GenTree>.Empty,
                        int64: unchecked((long)value));
                node.IsContainedInLinear = true;
                return node;
            }

            private static ulong MaskToWidth(ulong value, int bits)
                => bits <= 32 ? unchecked((uint)value) : value;

            private GenTree? LowerValueOrVoid(GenTree tree)
            {
                if (TryLowerBinaryStrengthReduction(tree, out var loweredResult))
                    return loweredResult;

                var uses = LowerOperands(tree);

                GenTree? result = ProducesValue(tree) ? NewTemp(tree) : null;

                EmitTree(tree, uses, result);
                return result;
            }

            private GenTree? LowerValueOrVoid(SsaTree tree)
            {
                if (tree.Value.HasValue)
                {
                    var value = GetOrCreateSsaValue(tree.Value.Value, tree.Source);
                    tree.Source.RegisterResult = value;
                    tree.Source.ValueKey = value.ValueKey;
                    return value;
                }

                if (TryLowerBinaryStrengthReduction(tree, out var loweredResult))
                    return loweredResult;

                var uses = LowerOperands(tree);

                if (tree.StoreTarget.HasValue)
                {
                    var targetName = tree.StoreTarget.Value;
                    var target = GetOrCreateSsaValue(targetName, tree.Source);
                    var info = GetSsaSlotInfo(targetName.Slot);
                    tree.Source.AttachSsaDefinition(targetName, info.Type, info.StackKind);
                    tree.Source.RegisterResult = target;
                    EmitTree(tree.Source, uses, target);
                    return target;
                }

                GenTree? result = ProducesValue(tree.Source) ? NewTemp(tree.Source) : null;

                EmitTree(tree.Source, uses, result);
                return result;
            }

            private GenTree NewTemp(GenTree source)
            {
                return GetOrCreateGenTree(source);
            }

            private GenTree GetOrCreateGenTree(GenTree source)
            {
                var key = ValueKeyForNode(source);
                if (_valueInfos.TryGetValue(key, out var existing))
                    return existing.RepresentativeNode;

                _valueInfos.Add(key, new GenTreeValueInfo(
                    key,
                    source,
                    source.Type,
                    source.StackKind,
                    ResolveRegisterClass(source.Type, source.StackKind),
                    _currentBlockId,
                    definitionNodeId: key.IsTreeNode ? -1 : source.LinearId));

                return source;
            }

            private static GenTreeValueKey ValueKeyForNode(GenTree source)
                => source.LinearValueKey;

            private GenTree EmitTree(GenTree tree, ImmutableArray<LirOperandFlags> operands, GenTree? result)
            {
                int id = _nextNodeId++;
                int ordinal = _currentBlockOrdinal++;
                operands = operands.IsDefault ? ImmutableArray<LirOperandFlags>.Empty : operands;
                var uses = BuildUses(tree, operands);
                var memoryAccess = GenTreeLinearLoweringClassifier.ClassifyMemoryAccess(tree);
                var lowering = GenTreeLinearLoweringClassifier.Classify(tree, result, uses, _currentBlockId, memoryAccess);
                tree.SetLinearState(id, _currentBlockId, ordinal, GenTreeLinearKind.Tree, result, operands, uses, lowering, memoryAccess);
                RecordNode(tree);
                return tree;
            }

            private GenTree EmitGcPoll(GenTree? sourceTree)
            {
                var pollTree = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Nop,
                    sourceTree?.Pc ?? -1,
                    BytecodeOp.Nop,
                    type: null,
                    stackKind: GenStackKind.Void,
                    flags: GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                    operands: ImmutableArray<GenTree>.Empty);

                var lowering = new GenTreeLinearLoweringInfo(
                    GenTreeLinearFlags.IsStandaloneLoweredNode |
                    GenTreeLinearFlags.GcSafePoint,
                    0,
                    0);
                pollTree.SetLinearState(
                    _nextNodeId++,
                    _currentBlockId,
                    _currentBlockOrdinal++,
                    GenTreeLinearKind.GcPoll,
                    result: null,
                    ImmutableArray<LirOperandFlags>.Empty,
                    ImmutableArray<GenTree>.Empty,
                    lowering,
                    LinearMemoryAccess.None);
                RecordNode(pollTree);
                return pollTree;
            }

            private void EmitPhiCopies()
            {
                if (_ssa is null)
                    return;

                for (int b = 0; b < _ssa.Blocks.Length; b++)
                {
                    var block = _ssa.Blocks[b];
                    for (int p = 0; p < block.Phis.Length; p++)
                    {
                        var phi = block.Phis[p];
                        if (!IsSsaValueDemanded(phi.Target))
                            continue;

                        var target = GetOrCreateSsaValue(phi.Target);

                        for (int i = 0; i < phi.Inputs.Length; i++)
                        {
                            var input = phi.Inputs[i];
                            if (input.Value.Equals(phi.Target))
                                continue;

                            var source = GetOrCreateSsaValue(input.Value);
                            EmitPhiCopy(input.PredecessorBlockId, block.Id, source, target);
                        }
                    }
                }
            }

            private void EmitPhiCopy(int fromBlockId, int toBlockId, GenTree source, GenTree destination)
            {
                if ((uint)fromBlockId >= (uint)_nodesByBlock.Length)
                    throw new InvalidOperationException($"SSA phi input references invalid predecessor B{fromBlockId}.");

                var copy = new GenTree(
                    _nextSyntheticTreeId++,
                    GenTreeKind.Copy,
                    pc: -1,
                    BytecodeOp.Nop,
                    destination.Type ?? source.Type,
                    destination.StackKind != GenStackKind.Unknown ? destination.StackKind : source.StackKind,
                    GenTreeFlags.Ordered,
                    ImmutableArray.Create(source));

                var uses = ImmutableArray.Create(source);
                var lowering = new GenTreeLinearLoweringInfo(GenTreeLinearFlags.IsStandaloneLoweredNode, 0, 0);
                int placementBlockId = SelectPhiCopyPlacementBlock(fromBlockId, toBlockId);
                copy.SetLinearState(
                    _nextNodeId++,
                    placementBlockId,
                    ordinal: 0,
                    GenTreeLinearKind.PhiCopy,
                    destination,
                    ImmutableArray<LirOperandFlags>.Empty,
                    uses,
                    lowering,
                    LinearMemoryAccess.None,
                    phiCopyFromBlockId: fromBlockId,
                    phiCopyToBlockId: toBlockId);

                if (placementBlockId == fromBlockId)
                    InsertNodeBeforeTerminator(fromBlockId, copy);
                else
                    InsertNodeAtBlockEntry(toBlockId, copy);

                _allNodes.Add(copy);
                UpdateDefinitionInfo(copy);
            }

            private int SelectPhiCopyPlacementBlock(int fromBlockId, int toBlockId)
            {
                if (CountNormalSuccessors(fromBlockId) <= 1)
                    return fromBlockId;

                if (CountNormalPredecessors(toBlockId) <= 1)
                    return toBlockId;

                throw new InvalidOperationException(
                    $"SSA phi copy for critical edge B{fromBlockId}->B{toBlockId} requires CFG edge splitting before LIR/LSRA.");
            }

            private int CountNormalSuccessors(int blockId)
            {
                int count = 0;
                var successors = _cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    if (successors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private int CountNormalPredecessors(int blockId)
            {
                int count = 0;
                var predecessors = _cfg.Blocks[blockId].Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    if (predecessors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private void InsertNodeAtBlockEntry(int blockId, GenTree node)
            {
                var list = _nodesByBlock[blockId];
                int index = 0;
                while (index < list.Count && list[index].IsPhiCopy)
                    index++;
                list.Insert(index, node);
            }

            private void InsertNodeBeforeTerminator(int blockId, GenTree node)
            {
                var list = _nodesByBlock[blockId];
                int index = list.Count;
                if (index > 0 && IsControlTransfer(list[index - 1]))
                    index--;
                list.Insert(index, node);
            }

            private static ImmutableArray<GenTree> BuildUses(GenTree tree, ImmutableArray<LirOperandFlags> operandFlags)
            {
                if (tree.Operands.IsDefaultOrEmpty)
                    return ImmutableArray<GenTree>.Empty;

                if (!operandFlags.IsDefault && operandFlags.Length != tree.Operands.Length)
                    throw new InvalidOperationException($"GenTree LIR node {tree.Id} has {tree.Operands.Length} operands but {operandFlags.Length} operand flag entries.");

                var builder = ImmutableArray.CreateBuilder<GenTree>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var flags = operandFlags.IsDefaultOrEmpty ? LirOperandFlags.None : operandFlags[i];
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    var operandTree = tree.Operands[i];
                    if (operandTree.RegisterResult is not null)
                        builder.Add(operandTree.RegisterResult);
                }
                return builder.ToImmutable();
            }

            private bool IsBackwardBranch(GenTree source)
            {
                if (source.Kind is not (GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse))
                    return false;

                return source.TargetBlockId >= 0 && source.TargetBlockId <= _currentBlockId;
            }

            private void RecordNode(GenTree node)
            {
                _nodesByBlock[_currentBlockId].Add(node);
                _allNodes.Add(node);
                UpdateDefinitionInfo(node);
            }

            private void UpdateDefinitionInfo(GenTree node)
            {
                if (node.RegisterResult is not null)
                {
                    var value = ValueKeyForNode(node.RegisterResult);
                    if (_valueInfos.TryGetValue(value, out var info))
                    {
                        _valueInfos[value] = info.WithDefinitionNode(info.RepresentativeNode, node.LinearBlockId, node.LinearId);
                    }
                }
            }

            private static bool IsControlTransfer(GenTree source)
                => source.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                   GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

            private GenTreeMethod Freeze()
            {
                _allNodes.Clear();
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var nodes = _nodesByBlock[b].ToImmutableArray();
                    _method.Blocks[b].SetLinearNodes(nodes);
                    for (int i = 0; i < nodes.Length; i++)
                        _allNodes.Add(nodes[i]);
                }

                NormalizeRepresentativeValueKeys();

                var values = new List<GenTreeValueInfo>(_valueInfos.Values);
                values.Sort(static (a, b) => string.Compare(a.Value.ToString(), b.Value.ToString(), StringComparison.Ordinal));

                _method.AttachLinearBackendState(
                    _cfg,
                    _allNodes.ToImmutableArray(),
                    values.ToImmutableArray(),
                    new Dictionary<GenTreeValueKey, GenTreeValueInfo>(_valueInfos),
                    LinearBlockOrder.Compute(_cfg),
                    _ssa);
                return _method;
            }

            private void NormalizeRepresentativeValueKeys()
            {
                foreach (var kv in _valueInfos)
                {
                    var value = kv.Key;
                    var representative = kv.Value.RepresentativeNode;
                    if (!representative.LinearValueKey.Equals(value))
                        representative.ValueKey = value;
                }
            }

            private static bool ProducesValue(GenTree node)
            {
                if (node.StackKind == GenStackKind.Void)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Nop => false,
                    GenTreeKind.StoreIndirect => false,
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.StoreField => false,
                    GenTreeKind.StoreStaticField => false,
                    GenTreeKind.StoreArrayElement => false,
                    GenTreeKind.Eval => false,
                    GenTreeKind.Branch => false,
                    GenTreeKind.BranchTrue => false,
                    GenTreeKind.BranchFalse => false,
                    GenTreeKind.Return => false,
                    GenTreeKind.Throw => false,
                    GenTreeKind.Rethrow => false,
                    GenTreeKind.EndFinally => false,
                    _ => true,
                };
            }
        }
    }
}
